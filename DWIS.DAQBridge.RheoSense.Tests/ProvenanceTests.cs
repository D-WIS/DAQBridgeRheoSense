using DWIS.API.DTO;
using DWIS.RigOS.Common.Worker;
using DWIS.Vocabulary.Schemas;
using DWIS.DAQBridge.RheoSense.Model;
using Xunit;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class ProvenanceTests
{
    [Fact]
    public void ContractDocumentHostCanBeConfiguredForPublishedManifests()
    {
        var data = RheometryContract.ApplyPublicBaseUrl(new ActivePitOutputData(), "https://contracts.example/base/");
        Field[] fields = data.Manifests.Value
            .Where(pair => pair.Key.Name.StartsWith("FlowCurveAt"))
            .SelectMany(pair => pair.Value.InjectedNodes)
            .SelectMany(node => node.Fields ?? Array.Empty<Field>())
            .ToArray();

        Assert.Contains(fields, field => field.FieldName == Attributes.JSonDataType_SchemaURL
            && field.FieldValue == "https://contracts.example/base/Rheometry/v1/schema.json");
        Assert.Contains(fields, field => field.FieldName == Attributes.JSonDataType_MetaDataURL
            && field.FieldValue == "https://contracts.example/base/Rheometry/v1/metadata.json");
    }

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
            var fields = manifest.InjectedNodes.SelectMany(node => node.Fields ?? Array.Empty<Field>()).ToArray();
            Assert.Contains(fields, field =>
                field.FieldName == Attributes.JSonDataType_SchemaURL && field.FieldValue == RheometryContract.SchemaUrl);
            Assert.Contains(fields, field =>
                field.FieldName == Attributes.JSonDataType_MetaDataURL && field.FieldValue == RheometryContract.MetaDataUrl);
            Assert.DoesNotContain(manifest.InjectedNodes, n => n.UniqueName == curve.UniqueName && n.TypeDictionaryURI == "DirectMeasurement");
            Assert.DoesNotContain(manifest.InjectedNodes.SelectMany(n => n.Fields ?? Array.Empty<Field>()), f => f.FieldName == "Value");
        }
    }
}
