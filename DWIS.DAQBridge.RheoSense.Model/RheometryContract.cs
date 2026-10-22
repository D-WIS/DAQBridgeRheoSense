using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>Canonical public contract for serialized online-rheometry flow curves.</summary>
public static class RheometryContract
{
    public const string Version = "v1";
    public const string DefaultPublicBaseUrl = "https://dwis.digiwells.no/";
    public const string SchemaUrl = "https://dwis.digiwells.no/Rheometry/v1/schema.json";
    public const string MetaDataUrl = "https://dwis.digiwells.no/Rheometry/v1/metadata.json";

    public static (string SchemaUrl, string MetaDataUrl) ResolveUrls(string? publicBaseUrl)
    {
        string value = string.IsNullOrWhiteSpace(publicBaseUrl) ? DefaultPublicBaseUrl : publicBaseUrl;
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("ContractsPublicBaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return (
            new Uri(baseUri, "Rheometry/v1/schema.json").AbsoluteUri,
            new Uri(baseUri, "Rheometry/v1/metadata.json").AbsoluteUri);
    }

    public static T ApplyPublicBaseUrl<T>(T data, string? publicBaseUrl) where T : DWISData
    {
        (string schemaUrl, string metadataUrl) = ResolveUrls(publicBaseUrl);
        foreach (var manifest in data.Manifests.Value.Values)
        foreach (var node in manifest.InjectedNodes)
        foreach (var field in node.Fields ?? [])
        {
            if (field.FieldName == "SchemaURL") field.FieldValue = schemaUrl;
            else if (field.FieldName == "MetaDataURL") field.FieldValue = metadataUrl;
        }

        return data;
    }
}
