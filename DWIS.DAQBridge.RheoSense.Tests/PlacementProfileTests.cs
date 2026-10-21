using DWIS.DAQBridge.RheoSense.Model;
using System.Text.Json;
using Xunit;
namespace DWIS.DAQBridge.RheoSense.Tests;
public class PlacementProfileTests
{
    [Fact]
    public void IdentityUpgradeContainsNoVariablesOrInstrumentChanges()
    {
        var output = new RheometerOutputData(new() { Id = "Reserve1", Placement = PlacementProfile.ReservePit });
        var patch = output.CreatePhysicalLocationManifest();
        Assert.Empty(patch.ProvidedVariables); Assert.Empty(patch.InjectedVariables);
        Assert.Single(patch.InjectedNodes);
        Assert.All(patch.InjectedReferences, r => Assert.Equal("http://ddhub.no/IsDependentOn", r.VerbURI));
        Assert.All(patch.InjectedReferences, r => Assert.Equal("Reserve1.location", r.Subject.ID));
        Assert.Contains(output.Manifests.Value.Values, m => m.Provider.Name == patch.Provider.Name);
    }
    [Fact]
    public void PhysicalLocationIdentityIsRigScopedAndIndependentOfInstrument()
    {
        var first = new RheometerSource { Id = "Reserve1", Placement = PlacementProfile.ReservePit, RigId = "RigA" };
        var second = new RheometerSource { Id = "Reserve2", Placement = PlacementProfile.ReservePit, RigId = "RigA" };
        Assert.NotEqual(first.ResolvedPhysicalLocationId, second.ResolvedPhysicalLocationId);
        Assert.NotEqual(first.ResolvedPhysicalLocationId, new RheometerSource { Id = first.Id, Placement = first.Placement, RigId = "RigB" }.ResolvedPhysicalLocationId);
        first.PhysicalLocationId = second.PhysicalLocationId = "urn:rig:a:tank:reserve1";
        var output = new RheometerOutputData(first);
        first.PhysicalLocationId = "urn:rig:a:tank:reserve2";
        Assert.All(output.Manifests.Value.Values, m => {
            var descriptor = Assert.Single(m.InjectedNodes.Where(n => n.UniqueName.EndsWith(".physicalLocationIdentity")));
            Assert.Equal("https://digiwells.no/daq/rheometry/1/physicalLocationIdentity/" + Uri.EscapeDataString(second.ResolvedPhysicalLocationId), Assert.Single(descriptor.Fields).FieldValue);
        });
        Assert.Throws<InvalidOperationException>(() => new RheometerSource { PhysicalLocationId = "not-an-absolute-id" }.Validate());
    }
    [Fact]
    public void SimulatedMeanAndSigmaRegistrationMatchesTheirConfiguredAddresses()
    {
        var source = new RheometerSource { Id = "Reserve1", Placement = PlacementProfile.ReservePit };
        var mean = new RheoSenseInstrumentData(source.Namespace, source.Prefix);
        var sigma = new RheoSenseInstrumentData(source.Namespace, source.Prefix + "Sigma_", false);
        var manifests = new[] { RheoSenseInputManifest.Create(mean, source.ManifestName), RheoSenseInputManifest.Create(sigma, source.ManifestName) };
        Assert.Equal(78, manifests.Sum(m => m.ProvidedVariables.Count));
        var ids = manifests.SelectMany(m => m.ProvidedVariables.Select(v => m.Provider.Name + "." + v.VariableID)).ToArray();
        Assert.Equal(78, ids.Distinct().Count());
        Assert.Equal(mean.OPCUANodes.Value.Values.Concat(sigma.OPCUANodes.Value.Values).Select(n => (n.PrefixID ?? "") + n.ID).Order(), ids.Order());
    }
    [Fact]
    public void TwoInstrumentsAtTheSamePlacementHaveStableDistinctBindings()
    {
        var source = new RheometerSource { Id = "Reserve1", Placement = PlacementProfile.ReservePit };
        var first = new RheometerOutputData(source);
        source.Id = "ChangedAfterConstruction";
        var second = new RheometerOutputData(new() { Id = "Reserve2", Placement = PlacementProfile.ReservePit });
        Assert.All(first.Manifests.Value.Values, manifest => Assert.Contains("Reserve1", manifest.Provider.Name));
        Assert.All(first.SparQLQueries.Value.Values, qs => Assert.DoesNotContain("Reserve2", Assert.Single(qs.Values).SparQL));
        Assert.All(second.SparQLQueries.Value.Values, qs => Assert.DoesNotContain("Reserve1", Assert.Single(qs.Values).SparQL));
    }
    [Fact]
    public void EveryPlacementUsesOneInstrumentContractAndIsolatedGraphs()
    {
        var baseline = JsonSerializer.Serialize(new ActivePitOutputData().Manifests.Value.Values);
        foreach (var placement in Enum.GetValues<PlacementProfile>())
        {
            var source = new RheometerSource { Id = "Test" + placement, Placement = placement };
            var output = new RheometerOutputData(source);
            Assert.Equal(new ActivePitOutputData().Manifests.Value.Count, output.Manifests.Value.Count);
            Assert.All(output.Manifests.Value.Values, m => {
                Assert.StartsWith(source.ManifestName, m.Provider.Name);
                Assert.All(m.ProvidedVariables, variable => Assert.Contains(source.Id, variable.VariableID));
                Assert.DoesNotContain(m.InjectedReferences, r => r.Subject.ID.Contains("RheoSenseAtActivePit") || r.Object.ID.Contains("RheoSenseAtActivePit"));
                Assert.Contains(m.InjectedReferences, r => r.VerbURI == "http://ddhub.no/IsHydraulicallyLocatedAt");
                if (placement != PlacementProfile.ActivePit) Assert.DoesNotContain(m.InjectedNodes, n => n.TypeDictionaryURI == "ActivePitLogical");
                if (placement == PlacementProfile.ReturnBeforeShakers) {
                    Assert.Contains(m.InjectedNodes, n => n.TypeDictionaryURI == "AnnulusOutletJunction");
                    Assert.Contains(m.InjectedReferences, r => r.VerbURI.EndsWith("HasUpstreamBranch") && r.Subject.ID.EndsWith("shakerLogical"));
                }
                if (placement == PlacementProfile.ReturnAfterShakers) Assert.Contains(m.InjectedReferences, r => r.VerbURI.EndsWith("HasDownstreamBranch") && r.Subject.ID.EndsWith("shakerLogical"));
                if (placement == PlacementProfile.SuctionManifold) Assert.Contains(m.InjectedNodes, n => n.TypeDictionaryURI == "MudPump");
            });
            Assert.All(output.SparQLQueries.Value.Values, qs => Assert.Contains(source.Id, Assert.Single(qs.Values).SparQL));
        }
        Assert.Equal(baseline, JsonSerializer.Serialize(new ActivePitOutputData().Manifests.Value.Values));
    }
    [Fact]
    public void SourcesValidateIdentitiesProfilesAndSelection()
    {
        Assert.Throws<InvalidOperationException>(() => RheometerSource.ValidateAll(new[] { new RheometerSource { Id = "bad/id" } }));
        Assert.Throws<InvalidOperationException>(() => RheometerSource.ValidateAll(new[] { new RheometerSource { Id = "one" }, new RheometerSource { Id = "ONE", Enabled = false } }));
        Assert.Throws<InvalidOperationException>(() => new RheometerSource { Placement = (PlacementProfile)999 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new RheometerSource { Id = "FlowLine", Placement = PlacementProfile.TripTank }.Validate());
        Assert.Empty(RheometerSource.ValidateAll(new[] { new RheometerSource { Enabled = false } }));
        var config = JsonSerializer.Deserialize<ConfigurationForRheoSense>("{\"Sources\":[{\"Id\":\"Reserve1\",\"Placement\":\"ReservePit\"}]}")!;
        Assert.Equal(PlacementProfile.ReservePit, Assert.Single(config.GetSources()).Placement);
        Assert.Equal(new[] { "ActivePit", "FlowLine" }, new ConfigurationForRheoSense().GetSources().Select(s => s.Id));
    }
}
