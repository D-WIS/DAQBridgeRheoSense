using SharedContract = DWIS.RigOS.Contracts.Rheometry.RheometryContract;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>Canonical public contract for serialized online-rheometry flow curves.</summary>
public static class RheometryContract
{
    public const string Version = SharedContract.Version;
    public const string DefaultPublicBaseUrl = SharedContract.DefaultPublicBaseUrl;
    public const string SchemaUrl = SharedContract.DefaultSchemaUrl;
    public const string MetaDataUrl = SharedContract.DefaultMetaDataUrl;

    public static (string SchemaUrl, string MetaDataUrl) ResolveUrls(string? publicBaseUrl)
    {
        string value = string.IsNullOrWhiteSpace(publicBaseUrl) ? DefaultPublicBaseUrl : publicBaseUrl;
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("ContractsPublicBaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return (
            new Uri(baseUri, SharedContract.SchemaPath.TrimStart('/')).AbsoluteUri,
            new Uri(baseUri, SharedContract.MetaDataPath.TrimStart('/')).AbsoluteUri);
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
