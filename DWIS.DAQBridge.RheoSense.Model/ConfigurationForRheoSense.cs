using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

public class ConfigurationForRheoSense : ConfigurationForOPCUA
{
    private RheoSenseOPCUATagSet _activePitTags = RheoSenseOPCUATagSet.CreateDefault(
        RheoSenseConstants.ActivePitNamespace,
        RheoSenseConstants.ActivePitNodePrefix,
        RheoSenseConstants.ActivePitStandardDeviationNodePrefix);
    private RheoSenseOPCUATagSet _flowLineTags = RheoSenseOPCUATagSet.CreateDefault(
        RheoSenseConstants.FlowLineNamespace,
        RheoSenseConstants.FlowLineNodePrefix,
        RheoSenseConstants.FlowLineStandardDeviationNodePrefix);

    public ConfigurationForRheoSense()
    {
        OPCUAURL = "opc.tcp://localhost:48030";
    }

    public bool InitializeOPCUAVariables { get; set; } = true;
    public TimeSpan ConnectionRetryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableDataLogging { get; set; } = true;
    public string DataLogDirectory { get; set; } = "/home";
    public TimeSpan DataLogSegmentDuration { get; set; } = TimeSpan.FromHours(6);
    public long MaximumDataLogStorageBytes { get; set; } = 1024L * 1024L * 1024L;
    public int WebAppTimeSpanHours { get; set; } = 6;
    public int SimulationSeed { get; set; } = 20261016;
    public TimeSpan SimulationConditionGenerationPeriod { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan SimulationDataGenerationPeriod { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SimulationOPCUAPublishPeriod { get; set; } = TimeSpan.FromMinutes(5);
    public RheoSenseOPCUATagSet ActivePitTags
    {
        get => _activePitTags;
        set
        {
            _activePitTags = value ?? new RheoSenseOPCUATagSet();
            _activePitTags.ApplyDefaults(
                RheoSenseConstants.ActivePitNamespace,
                RheoSenseConstants.ActivePitNodePrefix,
                RheoSenseConstants.ActivePitStandardDeviationNodePrefix);
        }
    }

    public RheoSenseOPCUATagSet FlowLineTags
    {
        get => _flowLineTags;
        set
        {
            _flowLineTags = value ?? new RheoSenseOPCUATagSet();
            _flowLineTags.ApplyDefaults(
                RheoSenseConstants.FlowLineNamespace,
                RheoSenseConstants.FlowLineNodePrefix,
                RheoSenseConstants.FlowLineStandardDeviationNodePrefix);
        }
    }
}

public class RheoSenseOPCUATagSet
{
    private string _defaultNameSpace = string.Empty;
    private string _defaultNodeIDPrefix = string.Empty;
    private string _defaultStandardDeviationNodeIDPrefix = string.Empty;

    public Dictionary<string, UnitConversion> UnitConversions { get; set; } = new();
    public Dictionary<string, UnitConversion> StandardDeviationUnitConversions { get; set; } = new();
    public Dictionary<string, RheoSenseOPCUATagDefinition> Signals { get; set; } = new();
    public Dictionary<string, RheoSenseOPCUATagDefinition> StandardDeviationSignals { get; set; } = new();

    public static RheoSenseOPCUATagSet CreateDefault(
        string nameSpace,
        string nodeIdPrefix,
        string standardDeviationNodeIdPrefix)
    {
        RheoSenseOPCUATagSet result = new();
        result.ApplyDefaults(nameSpace, nodeIdPrefix, standardDeviationNodeIdPrefix);
        return result;
    }

    public RheoSenseOPCUATagDefinition Resolve(string propertyName, bool standardDeviation = false)
    {
        Dictionary<string, RheoSenseOPCUATagDefinition> definitions = standardDeviation
            ? StandardDeviationSignals
            : Signals;
        definitions.TryGetValue(propertyName, out RheoSenseOPCUATagDefinition? configured);
        string prefix = standardDeviation ? _defaultStandardDeviationNodeIDPrefix : _defaultNodeIDPrefix;

        return new RheoSenseOPCUATagDefinition
        {
            NameSpace = string.IsNullOrWhiteSpace(configured?.NameSpace) ? _defaultNameSpace : configured.NameSpace,
            NodeID = string.IsNullOrWhiteSpace(configured?.NodeID)
                ? prefix + propertyName
                : configured.NodeID
        };
    }

    public void ApplyDefaults(
        string nameSpace,
        string nodeIdPrefix,
        string standardDeviationNodeIdPrefix)
    {
        _defaultNameSpace = nameSpace;
        _defaultNodeIDPrefix = nodeIdPrefix;
        _defaultStandardDeviationNodeIDPrefix = standardDeviationNodeIdPrefix;
        UnitConversions ??= new Dictionary<string, UnitConversion>();
        StandardDeviationUnitConversions ??= new Dictionary<string, UnitConversion>();
        Signals ??= new Dictionary<string, RheoSenseOPCUATagDefinition>();
        StandardDeviationSignals ??= new Dictionary<string, RheoSenseOPCUATagDefinition>();

        foreach (string propertyName in SignalPropertyNames)
        {
            ApplyDefault(Signals, propertyName, Resolve(propertyName));
            if (propertyName is not nameof(RheoSenseInstrumentData.ExtrapolatedTemperature) and
                not nameof(RheoSenseInstrumentData.ExtrapolatedPressure))
            {
                ApplyDefault(StandardDeviationSignals, propertyName, Resolve(propertyName, standardDeviation: true));
            }
        }
    }

    private static readonly string[] SignalPropertyNames = typeof(RheoSenseInstrumentData)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(ScalarDrillingProperty))
        .Select(property => property.Name)
        .ToArray();

    private static void ApplyDefault(
        IDictionary<string, RheoSenseOPCUATagDefinition> definitions,
        string propertyName,
        RheoSenseOPCUATagDefinition fallback)
    {
        if (!definitions.TryGetValue(propertyName, out RheoSenseOPCUATagDefinition? definition) || definition is null)
        {
            definitions[propertyName] = fallback;
            return;
        }

        definition.NameSpace = string.IsNullOrWhiteSpace(definition.NameSpace) ? fallback.NameSpace : definition.NameSpace;
        definition.NodeID = string.IsNullOrWhiteSpace(definition.NodeID) ? fallback.NodeID : definition.NodeID;
    }
}

public class RheoSenseOPCUATagDefinition
{
    public string? NameSpace { get; set; }
    public string? NodeID { get; set; }
}
