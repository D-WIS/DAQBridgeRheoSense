namespace DWIS.DAQBridge.RheoSense.Model;

public static class RheoSenseConstants
{
    public const string Provider = "IntelligentMudSystem";
    public const string ActivePitManifest = "RheoSenseActivePitDataManifest";
    public const string ActivePitStandardDeviationManifest = "RheoSenseActivePitStandardDeviationDataManifest";
    public const string FlowLineManifest = "RheoSenseFlowLineDataManifest";
    public const string FlowLineStandardDeviationManifest = "RheoSenseFlowLineStandardDeviationDataManifest";
    public const string ActivePitNamespace = "http://ddhub.no/RheoSenseActivePitDataManifest/Variables/";
    public const string FlowLineNamespace = "http://ddhub.no/RheoSenseFlowLineDataManifest/Variables/";
    public const string ActivePitNodePrefix = "RheoSenseActivePitDataManifest.";
    public const string ActivePitStandardDeviationNodePrefix = "RheoSenseActivePitDataManifest.Sigma_";
    public const string FlowLineNodePrefix = "RheoSenseFlowLineDataManifest.";
    public const string FlowLineStandardDeviationNodePrefix = "RheoSenseFlowLineDataManifest.Sigma_";
}
