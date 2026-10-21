using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DWIS.API.DTO;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

public sealed class RheometerExtrapolation
{
    public string Id { get; set; } = "HighTemperature";
    public RheoSenseOPCUATagSet Tags { get; set; } = new();
}

public sealed record RheometerInstallation(RheometerSource Source, RheoSenseOPCUATagSet Tags, string? ExtrapolationId = null)
{
    public string HistoryId => ExtrapolationId is null ? Source.Id : Source.Id + "_" + ExtrapolationId;
    public string InputManifestName => ExtrapolationId is null ? Source.ManifestName : Source.ManifestName + "_" + ExtrapolationId;
    public string InputNamespace => "http://ddhub.no/" + InputManifestName + "/Variables/";
    public string InputPrefix => InputManifestName + ".";
}

/// <summary>Reuses the attributed extrapolation contract with independent condition/value identities.</summary>
public sealed class RheometerExtrapolationOutput : RheometerOutputData
{
    private readonly Lazy<IReadOnlyDictionary<PropertyInfo, ManifestFile>> targetManifests;
    private readonly Lazy<IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>>> targetQueries;
    public RheometerExtrapolationOutput(RheometerSource source, string targetId) : base(source)
    {
        if (!Regex.IsMatch(targetId, "^[A-Za-z][A-Za-z0-9_]{0,31}\\z")) throw new InvalidOperationException("Invalid extrapolation target identity.");
        var original = base.Manifests;
        string? nodeNamespace = source.BlackboardNodeNamespace;
        targetManifests = new(() => Compose(original.Value, targetId));
        targetQueries = new(() => PlacementSemantics.Queries(targetManifests.Value, nodeNamespace));
    }
    public override Lazy<IReadOnlyDictionary<PropertyInfo, ManifestFile>> Manifests => targetManifests;
    public override Lazy<IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>>> SparQLQueries => targetQueries;
    public override IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>> CreateQueries(string nodeNamespace) => PlacementSemantics.Queries(Manifests.Value, nodeNamespace);
    private static IReadOnlyDictionary<PropertyInfo, ManifestFile> Compose(IReadOnlyDictionary<PropertyInfo, ManifestFile> originals, string targetId)
    {
        var result = new Dictionary<PropertyInfo, ManifestFile>();
        foreach (var (property, original) in originals.Where(pair => pair.Key.Name.Contains("Extrapolated", StringComparison.Ordinal) && !pair.Key.Name.StartsWith("PlasticViscosity") && !pair.Key.Name.StartsWith("YieldPoint")))
        {
            var manifest = JsonSerializer.Deserialize<ManifestFile>(JsonSerializer.Serialize(original))!;
            string Map(string value) => value.Replace("Extrapolated", "Extrapolated" + targetId, StringComparison.Ordinal);
            foreach (var node in manifest.InjectedNodes)
            {
                node.UniqueName = Map(node.UniqueName); node.BrowseName = Map(node.BrowseName); node.DisplayName = Map(node.DisplayName);
                foreach (var field in node.Fields ?? new List<Field>())
                    if (field.FieldName == "EndPointDescription" && field.FieldValue.StartsWith("https://digiwells.no/daq/source/", StringComparison.Ordinal)) field.FieldValue = Map(field.FieldValue);
            }
            foreach (var variable in manifest.ProvidedVariables) variable.VariableID = Map(variable.VariableID);
            foreach (var edge in manifest.InjectedReferences)
            {
                if (edge.Subject.NameSpace is "nodes" or "providedNodes" or "variables" || edge.Subject.NameSpace == manifest.InjectionInformation.InjectedNodesNamespaceAlias) edge.Subject.ID = Map(edge.Subject.ID);
                if (edge.Object.NameSpace is "nodes" or "providedNodes" or "variables" || edge.Object.NameSpace == manifest.InjectionInformation.InjectedNodesNamespaceAlias) edge.Object.ID = Map(edge.Object.ID);
            }
            result.Add(property, manifest);
        }
        return result;
    }
}
