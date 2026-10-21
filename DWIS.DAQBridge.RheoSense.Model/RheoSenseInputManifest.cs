using DWIS.API.DTO;
namespace DWIS.DAQBridge.RheoSense.Model;

// Mean and sigma transport variables share the same installation namespace.
public static class RheoSenseInputManifest
{
    public static ManifestFile Create(DWIS.RigOS.Common.Worker.DWISDataWithOPCUA data, string manifestName)
    {
        string prefix = manifestName + ".", ns = "http://ddhub.no/" + manifestName + "/Variables/";
        var variables = new List<ProvidedVariable>();
        foreach (var node in data.OPCUANodes.Value.Values)
        {
            string id = (node.PrefixID ?? "") + node.ID;
            if (node.NameSpace != ns || node.ID is null || !id.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic variable initialization requires the source's generated namespace and prefix. Disable initialization for external instrument mappings.");
            variables.Add(new() { VariableID = id[prefix.Length..], DataType = "double" });
        }
        return new() {
            ManifestName = manifestName, Provider = new() { Name = manifestName, Company = RheoSenseConstants.Provider },
            InjectionInformation = new() { EndPointURL = "", ServerName = "sourceserver", InjectedNodesNamespaceAlias = "nodes", InjectedVariablesNamespaceAlias = "variables", ProvidedVariablesNamespaceAlias = "providedNodes" },
            InjectedNodes = new List<InjectedNode>(), InjectedReferences = new List<InjectedReference>(), InjectedVariables = new List<InjectedVariable>(), ProvidedVariables = variables
        };
    }
}
