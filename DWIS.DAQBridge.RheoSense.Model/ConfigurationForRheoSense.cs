using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

public class ConfigurationForRheoSense : ConfigurationForOPCUA
{
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public string? UnitConversionServiceUrl { get; set; }
    public List<RheometerSource>? Sources { get; set; }
    public IReadOnlyList<RheometerSource> GetSources() => RheometerSource.ValidateAll(Sources ?? RheometerSource.Defaults());

    public Dictionary<string, RheoSenseOPCUATagSet> SourceTags { get; set; } = new();
    public RheoSenseOPCUATagSet TagsFor(RheometerSource source)
    {
        if (source.Id == "ActivePit") return ActivePitTags;
        if (source.Id == "FlowLine") return FlowLineTags;
        SourceTags ??= new();
        var tags = SourceTags.TryGetValue(source.Id, out var configured) ? configured ?? throw new InvalidOperationException("Source tag sets cannot be null.") : new RheoSenseOPCUATagSet();
        tags.ApplyDefaults(source.Namespace, source.Prefix, source.Prefix + "Sigma_");
        return tags;
    }
    public IReadOnlyList<RheometerInstallation> GetInstallations()
    {
        var result = new List<RheometerInstallation>();
        foreach (var source in GetSources())
        {
            var tags = TagsFor(source);
            result.Add(new(source, tags));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in tags.AdditionalExtrapolations)
            {
                if (target is null || !System.Text.RegularExpressions.Regex.IsMatch(target.Id ?? "", "^[A-Za-z][A-Za-z0-9_]{0,31}\\z") || !ids.Add(target.Id!)) throw new InvalidOperationException("Extrapolation target IDs must be valid and unique per instrument.");
                if (target.Tags.AdditionalExtrapolations.Count > 0) throw new InvalidOperationException("Nested extrapolation targets are not supported.");
                var installation = new RheometerInstallation(source, target.Tags, target.Id);
                foreach (var property in typeof(RheoSenseInstrumentData).GetProperties().Where(p => p.PropertyType == typeof(ScalarDrillingProperty)))
                {
                    if (!property.Name.Contains("Extrap", StringComparison.Ordinal)) target.Tags.Signals.TryAdd(property.Name, new() { Enabled = false });
                    target.Tags.StandardDeviationSignals.TryAdd(property.Name, new() { Enabled = false });
                }
                target.Tags.ApplyDefaults(installation.InputNamespace, installation.InputPrefix, installation.InputPrefix + "Sigma_");
                if (target.Tags.Signals.Any(pair => pair.Value.Enabled && !pair.Key.Contains("Extrap", StringComparison.Ordinal))) throw new InvalidOperationException("Additional target inputs must contain only extrapolated quantities.");
                result.Add(installation);
            }
        }
        if (result.Select(i => i.HistoryId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) throw new InvalidOperationException("Source and target history identities collide.");
        return result;
    }
    public void ValidateSources()
    {
        if (HeartbeatTimeout <= TimeSpan.Zero) throw new InvalidOperationException("HeartbeatTimeout must be positive.");
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var installation in GetInstallations())
        {
            var tags = installation.Tags;
            foreach (var (property, definition) in tags.Signals.Concat(tags.StandardDeviationSignals))
                if (definition.ConstantValue is double value && (!double.IsFinite(value) || property is not (nameof(RheoSenseInstrumentData.ExtrapolatedTemperature) or nameof(RheoSenseInstrumentData.ExtrapolatedPressure))))
                    throw new InvalidOperationException("Constants are only valid for finite extrapolation target conditions.");
            foreach (var mapping in tags.Signals.Values.Concat(tags.StandardDeviationSignals.Values).Where(m => m.Enabled && m.ConstantValue is null))
                if (!addresses.Add(mapping.NameSpace + "|" + mapping.NodeID)) throw new InvalidOperationException("Source input mappings overlap.");
        }
    }
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

    public IEnumerable<string> DisabledBinghamOutputs() => new[] {
        "PlasticViscosityAtMeasuredTemperatureAndPressure", "YieldPointAtMeasuredTemperatureAndPressure",
        "PlasticViscosityAtExtrapolatedTemperatureAndPressure", "YieldPointAtExtrapolatedTemperatureAndPressure" }
        .Where(output => !Resolve(output.Replace("Extrapolated", "Extraplated", StringComparison.Ordinal)).Enabled);
    public Dictionary<string, RheoSenseOPCUATagDefinition> StateSignals { get; set; } = new();
    public List<RheometerExtrapolation> AdditionalExtrapolations { get; set; } = new();
    internal Dictionary<string, UnitConversion> ResolvedUnits { get; } = new();
    internal Dictionary<string, UnitConversion> ResolvedSigmaUnits { get; } = new();
    public Dictionary<string, UnitConversion> GetConversions(bool sigma = false)
    {
        var result = new Dictionary<string, UnitConversion>(sigma ? StandardDeviationUnitConversions : UnitConversions);
        foreach (var pair in sigma ? ResolvedSigmaUnits : ResolvedUnits) result.Add(pair.Key, pair.Value);
        return result;
    }
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
            Enabled = configured?.Enabled ?? true,
            UnitName = configured?.UnitName,
            ConstantValue = configured?.ConstantValue,
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
    public bool Enabled { get; set; } = true;
    public string? UnitName { get; set; }
    public double? ConstantValue { get; set; }
    public string? NameSpace { get; set; }
    public string? NodeID { get; set; }
}
