using System.Reflection;
using DWIS.API.DTO;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>
/// Numeric values exposed by one RheoSense instrument over OPC-UA. This raw
/// transport contract deliberately contains no semantic output or flow curve.
/// </summary>
public sealed class RheoSenseInstrumentData : DWISDataWithOPCUA
{
    private static readonly Lazy<IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>>> EmptyQueries =
        new(() => new Dictionary<PropertyInfo, Dictionary<string, QuerySpecification>>());
    private static readonly Lazy<IReadOnlyDictionary<PropertyInfo, ManifestFile>> EmptyManifests =
        new(() => new Dictionary<PropertyInfo, ManifestFile>());
    private readonly Lazy<IReadOnlyDictionary<PropertyInfo, OPCUANode>> _opcuaNodes;

    public RheoSenseInstrumentData(
        string nameSpace,
        string nodeIdPrefix,
        bool includeExtrapolationParameters = true)
    {
        _opcuaNodes = new(() => BuildInstrumentNodes(nameSpace, nodeIdPrefix, includeExtrapolationParameters));
    }

    public override Lazy<IReadOnlyDictionary<PropertyInfo, OPCUANode>> OPCUANodes => _opcuaNodes;
    public override Lazy<IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>>> SparQLQueries => EmptyQueries;
    public override Lazy<IReadOnlyDictionary<PropertyInfo, ManifestFile>> Manifests => EmptyManifests;

    public ScalarDrillingProperty? MeasuredTemperature { get; set; }
    public ScalarDrillingProperty? MeasuredPressure { get; set; }
    public ScalarDrillingProperty? MassDensityFromCoriolisAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure { get; set; }

    public ScalarDrillingProperty? ShearStressAt3RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt6RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt10RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt20RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt30RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt60RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt100RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt200RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt300RPMAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt600RPMAtMeasuredTemperatureAndPressure { get; set; }

    public ScalarDrillingProperty? PlasticViscosityAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YieldPointAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLYieldStressAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLConsistencyIndexAtMeasuredTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure { get; set; }

    public ScalarDrillingProperty? ExtrapolatedTemperature { get; set; }
    public ScalarDrillingProperty? ExtrapolatedPressure { get; set; }

    // "Extraplated" is retained because it is the spelling used by the instrument tags.
    public ScalarDrillingProperty? ShearStressAt3RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt6RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt10RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt20RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt30RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt60RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt100RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt200RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt300RPMAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? ShearStressAt600RPMAtExtraplatedTemperatureAndPressure { get; set; }

    public ScalarDrillingProperty? PlasticViscosityAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YieldPointAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLYieldStressAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLConsistencyIndexAtExtraplatedTemperatureAndPressure { get; set; }
    public ScalarDrillingProperty? YPLFlowBehaviorIndexAtExtraplatedTemperatureAndPressure { get; set; }

    private static IReadOnlyDictionary<PropertyInfo, OPCUANode> BuildInstrumentNodes(
        string nameSpace,
        string nodeIdPrefix,
        bool includeExtrapolationParameters)
    {
        return typeof(RheoSenseInstrumentData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(ScalarDrillingProperty))
            .Where(property => includeExtrapolationParameters ||
                property.Name is not nameof(ExtrapolatedTemperature) and not nameof(ExtrapolatedPressure))
            .ToDictionary(
                property => property,
                property => new OPCUANode
                {
                    NameSpace = nameSpace,
                    PrefixID = nodeIdPrefix,
                    ID = property.Name
                });
    }
}
