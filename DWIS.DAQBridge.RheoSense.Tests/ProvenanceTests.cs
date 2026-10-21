using DWIS.API.DTO;
using DWIS.RigOS.Common.Worker;
using DWIS.Vocabulary.Schemas;
using DWIS.DAQBridge.RheoSense.Model;
using Xunit;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class ProvenanceTests
{
    [Fact]
    public void RuntimeManifestsUseRegisteredDwisTypesAndKeepBothCurveRepresentations()
    {
        foreach (DWISData data in new DWISData[] { new ActivePitOutputData(), new FlowLineOutputData() })
        foreach (var (property, manifest) in data.Manifests.Value)
        {
            Assert.All(manifest.InjectedNodes, node => {
                Assert.True(Enum.TryParse<Nouns.Enum>(node.TypeDictionaryURI, out _), node.TypeDictionaryURI);
                Assert.False(string.IsNullOrEmpty(node.BrowseName));
            });
            Assert.All(manifest.InjectedReferences, reference => {
                Assert.StartsWith("http://ddhub.no/", reference.VerbURI);
                Assert.True(Enum.TryParse<Verbs.Enum>(reference.VerbURI["http://ddhub.no/".Length..], out _));
                Assert.False(reference.Object.NameSpace.EndsWith('/'));
            });
            if (!property.Name.StartsWith("FlowCurveAt")) continue;
            var descriptions = manifest.InjectedNodes.SelectMany(n => n.Fields ?? Array.Empty<Field>()).Where(f => f.FieldName == "EndPointDescription").Select(f => f.FieldValue).ToArray();
            Assert.Equal(2, descriptions.Count(d => d.Contains("/shearRateMember/")));
            Assert.Equal(2, descriptions.Count(d => d.Contains("/shearStressMember/")));
            Assert.Contains(descriptions, d => d.EndsWith("/purpose/Fann35Compatibility"));
            Assert.Contains(descriptions, d => d.EndsWith("/nonNewtonianTreatment/ModelConsistent"));
            Assert.Equal(6, descriptions.Count(d => d.Contains("/jsonMember/")));
            var curve = manifest.InjectedNodes.First(n => n.TypeDictionaryURI == "ComputedData" && n.UniqueName.Contains("FlowCurveAt"));
            Assert.DoesNotContain(manifest.InjectedNodes, n => n.UniqueName == curve.UniqueName && n.TypeDictionaryURI == "DirectMeasurement");
            Assert.DoesNotContain(manifest.InjectedNodes.SelectMany(n => n.Fields ?? Array.Empty<Field>()), f => f.FieldName == "Value");
        }
    }
}
