using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DWIS.API.DTO;
using DWIS.Vocabulary.Schemas;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

[JsonConverter(typeof(JsonStringEnumConverter<PlacementProfile>))]
public enum PlacementProfile { ActivePit, ReservePit, SlugPit, TripTank, SuctionManifold, ReturnBeforeShakers, ReturnAfterShakers }

public sealed class RheometerSource
{
    public string Id { get; set; } = "ActivePit";
    public bool Enabled { get; set; } = true;
    public PlacementProfile Placement { get; set; }
    public string? BlackboardNodeNamespace { get; set; }
    public string RigId { get; set; } = "local-rig";
    public string? PhysicalLocationId { get; set; }
    [JsonIgnore]
    public string ResolvedPhysicalLocationId => PhysicalLocationId ?? "urn:dwis:rig:" + Uri.EscapeDataString(RigId) + ":location:" + Uri.EscapeDataString(Id);

    [JsonIgnore]
    public string ManifestName => "RheoSense" + Id + "DataManifest";
    [JsonIgnore]
    public string Namespace => "http://ddhub.no/" + ManifestName + "/Variables/";
    [JsonIgnore]
    public string Prefix => ManifestName + ".";
    public void Validate()
    {
        if (!Regex.IsMatch(Id ?? "", "^[A-Za-z][A-Za-z0-9_]{0,63}\\z") || !Enum.IsDefined(Placement))
            throw new InvalidOperationException("A source needs a stable alphanumeric Id and a known placement profile.");
        if (string.IsNullOrWhiteSpace(RigId) || RigId.Any(char.IsControl)) throw new InvalidOperationException("Sources require a rig identity.");
        if (PhysicalLocationId is not null && (!Uri.TryCreate(PhysicalLocationId, UriKind.Absolute, out var locationUri) || !locationUri.IsWellFormedOriginalString()))
            throw new InvalidOperationException("PhysicalLocationId must be an absolute URI shared by instruments at the same physical location.");
        if (BlackboardNodeNamespace is not null && (!Uri.TryCreate(BlackboardNodeNamespace, UriKind.Absolute, out _) || BlackboardNodeNamespace.Any(c => char.IsWhiteSpace(c) || "<>\"{}|^`\\".Contains(c))))
            throw new InvalidOperationException("Invalid Blackboard node namespace.");
        if (Id == "ActivePit" && Placement != PlacementProfile.ActivePit || Id == "FlowLine" && Placement != PlacementProfile.ReturnBeforeShakers)
            throw new InvalidOperationException("Reserved legacy source identities must retain their original placement.");
    }
    public static List<RheometerSource> Defaults() => new() { new() { Id = "ActivePit", Placement = PlacementProfile.ActivePit }, new() { Id = "FlowLine", Placement = PlacementProfile.ReturnBeforeShakers } };
    public static IReadOnlyList<RheometerSource> ValidateAll(IEnumerable<RheometerSource> sources)
    {
        var result = sources.ToArray();
        if (result.Any(s => s is null)) throw new InvalidOperationException("Source entries cannot be null.");
        foreach (var source in result) source.Validate();
        if (result.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new InvalidOperationException("Source Ids must be unique, including disabled sources.");
        return result.Where(s => s.Enabled).ToArray();
    }
}

/// <summary>Composes installation facts into cloned, attribute-generated instrument graphs.</summary>
internal static class PlacementSemantics
{
    private const string D = "http://ddhub.no/";
    public static IReadOnlyDictionary<PropertyInfo, ManifestFile> Compose(IReadOnlyDictionary<PropertyInfo, ManifestFile> template, RheometerSource source)
    {
        source.Validate();
        var result = new Dictionary<PropertyInfo, ManifestFile>();
        foreach (var (property, original) in template)
        {
            // Serialization is used only for a deep copy. Identities and edges are edited as typed DTOs.
            var m = JsonSerializer.Deserialize<ManifestFile>(JsonSerializer.Serialize(original))!;
            m.ManifestName = source.ManifestName;
            m.Provider.Name = source.ManifestName + original.Provider.Name[original.ManifestName.Length..];
            string Map(string id) => source.Id == "ActivePit" ? id : id.Replace("RheoSenseAtActivePit", "RheoSenseAt" + source.Id, StringComparison.Ordinal);
            var removed = m.InjectedNodes.Where(n => n.TypeDictionaryURI is "ActivePitLogical" or "ActivePit" or "TopOfStringJunction" or "HydraulicBranch")
                .Select(n => n.UniqueName).ToHashSet(StringComparer.Ordinal);
            bool Local(NodeIdentifier n) => n.NameSpace == m.InjectionInformation.InjectedNodesNamespaceAlias;
            m.InjectedReferences = m.InjectedReferences.Where(r => !(Local(r.Subject) && removed.Contains(r.Subject.ID)) && !(Local(r.Object) && removed.Contains(r.Object.ID))).ToList();
            m.InjectedNodes = m.InjectedNodes.Where(n => !removed.Contains(n.UniqueName)).ToList();
            foreach (var node in m.InjectedNodes) { node.UniqueName = Map(node.UniqueName); node.BrowseName = Map(node.BrowseName); node.DisplayName = Map(node.DisplayName); }
            foreach (var variable in m.ProvidedVariables) variable.VariableID = Map(variable.VariableID);
            foreach (var edge in m.InjectedReferences)
            {
                if (edge.Subject.NameSpace is "nodes" or "providedNodes" or "variables" || edge.Subject.NameSpace == m.InjectionInformation.InjectedNodesNamespaceAlias || edge.Subject.NameSpace == m.InjectionInformation.ProvidedVariablesNamespaceAlias) edge.Subject.ID = Map(edge.Subject.ID);
                if (edge.Object.NameSpace is "nodes" or "providedNodes" or "variables" || edge.Object.NameSpace == m.InjectionInformation.InjectedNodesNamespaceAlias || edge.Object.NameSpace == m.InjectionInformation.ProvidedVariablesNamespaceAlias) edge.Object.ID = Map(edge.Object.ID);
            }
            string ns = m.InjectionInformation.InjectedNodesNamespaceAlias;
            NodeIdentifier Node(string id, Nouns.Enum type)
            {
                m.InjectedNodes.Add(new() { UniqueName = id, BrowseName = id, DisplayName = id, TypeDictionaryURI = type.ToString(), Fields = new List<Field>() });
                return new() { NameSpace = ns, ID = id };
            }
            void Link(NodeIdentifier a, Verbs.Enum verb, NodeIdentifier b) => m.InjectedReferences.Add(new() { Subject = a, VerbURI = D + verb, Object = b });
            var location = Node(source.Placement == PlacementProfile.ActivePit ? "ActivePitLogical#01" : source.Id == "FlowLine" ? "returnFlowLineLogical#01" : source.Id + ".location", source.Placement switch {
                PlacementProfile.ActivePit => Nouns.Enum.ActivePitLogical, PlacementProfile.ReservePit => Nouns.Enum.ReservePitLogical,
                PlacementProfile.SlugPit => Nouns.Enum.SlugPitLogical, PlacementProfile.TripTank => Nouns.Enum.TripTankLogical,
                PlacementProfile.SuctionManifold => Nouns.Enum.HydraulicJunction, _ => Nouns.Enum.FlowLineLogical });
            var placementIdentity = Node(source.Id + ".physicalLocationIdentity", Nouns.Enum.DataEndPoint);
            m.InjectedNodes.Last().Fields.Add(new() { FieldName = "EndPointDescription", FieldValue = "https://digiwells.no/daq/rheometry/1/physicalLocationIdentity/" + Uri.EscapeDataString(source.ResolvedPhysicalLocationId) });
            Link(location, Verbs.Enum.IsDependentOn, placementIdentity);
            var device = new NodeIdentifier { NameSpace = ns, ID = "RheoSenseAt" + source.Id + "MeasurementDevice#01" };
            Link(device, Verbs.Enum.IsHydraulicConnectedTo, location);
            var data = m.InjectedReferences.Where(r => r.VerbURI == D + "HasDynamicValue" && !r.Object.ID.StartsWith("Sigma_", StringComparison.Ordinal)).Select(r => r.Subject).ToArray();
            foreach (var point in data) Link(point, Verbs.Enum.IsHydraulicallyLocatedAt, location);
            if (source.Placement is PlacementProfile.ReturnBeforeShakers or PlacementProfile.ReturnAfterShakers)
            {
                var physicalLine = Node(source.Id == "FlowLine" ? "returnFlowLine#01" : source.Id + ".returnFlowLine", Nouns.Enum.ReturnFlowLine);
                Link(location, Verbs.Enum.IsAHydraulicRepresentationFor, physicalLine);
            }
            if (source.Placement is PlacementProfile.ReturnBeforeShakers or PlacementProfile.ReturnAfterShakers or PlacementProfile.SuctionManifold)
            {
                var branch = Node(source.Id == "FlowLine" ? "outletHydraulicBranch#01" : source.Id + ".branch", Nouns.Enum.HydraulicBranch);
                foreach (var point in data) Link(point, Verbs.Enum.IsAssociatedToHydraulicBranch, branch);
                if (source.Placement == PlacementProfile.SuctionManifold)
                {
                    var pump = Node(source.Id + ".pumpLogical", Nouns.Enum.HydraulicLogicalElement);
                    var physical = Node(source.Id + ".mudPump", Nouns.Enum.MudPump);
                    Link(pump, Verbs.Enum.IsAHydraulicRepresentationFor, physical);
                    Link(pump, Verbs.Enum.HasUpstreamBranch, branch);
                    Link(location, Verbs.Enum.HasDownstreamBranch, branch);
                }
                else
                {
                    var shaker = Node(source.Id + ".shakerLogical", Nouns.Enum.CuttingSeparatorLogical);
                    var physical = Node(source.Id + ".shaleShakers", Nouns.Enum.ShaleShakers);
                    Link(shaker, Verbs.Enum.IsAHydraulicRepresentationFor, physical);
                    Link(shaker, source.Placement == PlacementProfile.ReturnBeforeShakers ? Verbs.Enum.HasUpstreamBranch : Verbs.Enum.HasDownstreamBranch, branch);
                    if (source.Placement == PlacementProfile.ReturnBeforeShakers)
                    {
                        var annulus = Node("annulusOutletJunction#01", Nouns.Enum.AnnulusOutletJunction);
                        Link(annulus, Verbs.Enum.HasDownstreamBranch, branch);
                    }
                }
            }
            // A source-specific identity anchor also disambiguates two instruments with the same profile.
            var identity = Node("RheoSenseAt" + source.Id + ".sourceIdentity", Nouns.Enum.DataEndPoint);
            m.InjectedNodes.Last().Fields.Add(new() { FieldName = "EndPointDescription", FieldValue = "https://digiwells.no/daq/source/RheoSense/" + source.Id });
            Link(device, Verbs.Enum.IsDependentOn, identity);
            foreach (var variable in m.ProvidedVariables)
            {
                var binding = Node(variable.VariableID + ".binding", Nouns.Enum.DataEndPoint);
                m.InjectedNodes.Last().Fields.Add(new() { FieldName = "EndPointDescription", FieldValue = "https://digiwells.no/daq/source/RheoSense/" + source.Id + "/value/" + variable.VariableID });
                Link(new() { NameSpace = m.InjectionInformation.ProvidedVariablesNamespaceAlias, ID = variable.VariableID }, Verbs.Enum.IsDependentOn, binding);
            }
            result.Add(property, m);
        }
        return result;
    }

    public static ManifestFile PhysicalLocationManifest(ManifestFile original)
    {
        var patch = JsonSerializer.Deserialize<ManifestFile>(JsonSerializer.Serialize(original))!;
        patch.ManifestName += ".PhysicalLocationIdentity";
        patch.InjectedNodes = patch.InjectedNodes.Where(n => n.UniqueName.EndsWith(".physicalLocationIdentity", StringComparison.Ordinal)).ToList();
        var ids = patch.InjectedNodes.Select(n => n.UniqueName).ToHashSet(StringComparer.Ordinal);
        patch.InjectedReferences = patch.InjectedReferences.Where(r => r.VerbURI == D + "IsDependentOn" && ids.Contains(r.Object.ID)).ToList();
        patch.ProvidedVariables = new List<ProvidedVariable>();
        patch.InjectedVariables = new List<InjectedVariable>();
        return patch;
    }

    // Query the composed graph, never the original attributes with a stale placement.
    public static IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>> Queries(IReadOnlyDictionary<PropertyInfo, ManifestFile> manifests, string? nodeNamespace = null)
    {
        var result = new Dictionary<PropertyInfo, Dictionary<string, QuerySpecification>>();
        foreach (var (property, m) in manifests)
        {
            var variables = m.ProvidedVariables.Select((v, i) => (v.VariableID, Name: "value" + i)).ToArray();
            var triples = new List<string>();
            foreach (var (id, name) in variables)
            {
                var binding = m.InjectedReferences.Single(r => r.Subject.NameSpace == m.InjectionInformation.ProvidedVariablesNamespaceAlias && r.Subject.ID == id && r.VerbURI == D + "IsDependentOn").Object;
                string iri = (nodeNamespace ?? D + m.Provider.Name + "/").TrimEnd('/') + "/" + binding.ID;
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _) || iri.Any(c => char.IsWhiteSpace(c) || "<>\"{}|^`\\".Contains(c))) throw new InvalidOperationException("Invalid composed binding URI.");
                triples.Add("?" + name + " <" + D + "IsDependentOn> <" + iri + "> .");

            }
            var q = new QuerySpecification { Variables = variables.Select(v => v.Name).ToList(), NumberOfArguments = variables.Length,
                SparQL = "SELECT " + string.Join(" ", variables.Select(v => "?" + v.Name)) + " WHERE {\n" + string.Join("\n", triples) + "\n}" };
            result.Add(property, new() { ["composed"] = q });
        }
        return result;
    }
}
