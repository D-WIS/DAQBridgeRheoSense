using System.Reflection;
using System.Text.Json;
using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.RigOS.Common.Worker;
using Microsoft.Extensions.Logging;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>
/// Adapts the common DWIS worker to the richer drilling-property types used by
/// RheoSense. Scalar values are exchanged as doubles; Gaussian values and flow
/// curves are exchanged as JSON so their uncertainty and curve samples remain intact.
/// </summary>
public abstract class RheoSenseWorker<TConfiguration> : DWISWorkerWithOPCUA<TConfiguration, object>
    where TConfiguration : ConfigurationForOPCUA, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected RheoSenseWorker(
        ILogger<IDWISWorker<TConfiguration>> logger,
        ILogger<DWISClientOPCF>? clientLogger)
        : base(logger, clientLogger)
    {
    }

    protected override Task RegisterToOPCUA(DWISDataWithOPCUA data, string manifestName, string companyName)
    {
        if (data is not RheoSenseInstrumentData && data is not RheoSenseStateData) return base.RegisterToOPCUA(data, manifestName, companyName);
        if (_OPCUAClient?.Inject(RheoSenseInputManifest.Create(data, manifestName))?.Success != true)
            throw new InvalidOperationException("Could not register RheoSense source variables.");
        return Task.CompletedTask;
    }

    protected Task ReadStatesAsync(RheoSenseStateData data)
    {
        foreach (var (property, node) in data.OPCUANodes.Value)
        {
            double? value = null;
            if (!string.IsNullOrWhiteSpace(node.NameSpace) && _OPCUAClient?.ReadAnyVariables(out var values, new[] { (node.NameSpace, node.PrefixID + node.ID) }) == true && values.Length > 0)
            {
                var raw = values[0];
                if (raw is bool flag) value = flag ? 1 : 0;
                else if (raw is byte or short or ushort or int or uint or long or ulong or float or double or decimal)
                {
                    double number = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                    if (double.IsFinite(number)) value = number;
                }
            }
            property.SetValue(data, new ScalarDrillingProperty { ScalarValue = value });
        }
        return Task.CompletedTask;
    }

    protected override async Task PublishOPCUAAsync(
        DWISDataWithOPCUA data,
        string? configuredNamespace,
        string? configuredPrefix,
        Dictionary<string, string>? configuredIds,
        CancellationToken stoppingToken)
    {
        await PublishOPCUAValuesAsync(
            data,
            configuredNamespace,
            configuredPrefix,
            configuredIds,
            Configuration?.UnitConversions,
            property => true,
            stoppingToken,
            null,
            false);
    }

    protected Task PublishOPCUATagSetAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        CancellationToken stoppingToken)
    {
        return PublishOPCUAValuesAsync(
            data,
            null,
            null,
            null,
            tags.GetConversions(),
            property => true,
            stoppingToken,
            tags,
            false);
    }

    protected Task PublishOPCUATagSetChannelAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        RheoSenseDataChannel channel,
        CancellationToken stoppingToken)
    {
        return PublishOPCUAValuesAsync(
            data,
            null,
            null,
            null,
            tags.GetConversions(),
            property => RheoSenseDataChannels.IsInChannel(property.Name, channel),
            stoppingToken,
            tags,
            false);
    }

    protected Task PublishOPCUAStandardDeviationTagSetAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        CancellationToken stoppingToken)
    {
        return PublishOPCUAValuesAsync(
            data,
            null,
            null,
            null,
            tags.GetConversions(true),
            property => true,
            stoppingToken,
            tags,
            true);
    }

    protected Task PublishOPCUAStandardDeviationTagSetChannelAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        RheoSenseDataChannel channel,
        CancellationToken stoppingToken)
    {
        return PublishOPCUAValuesAsync(
            data,
            null,
            null,
            null,
            tags.GetConversions(true),
            property => RheoSenseDataChannels.IsInChannel(property.Name, channel),
            stoppingToken,
            tags,
            true);
    }

    protected Task ReadOPCUATagSetAsync(DWISDataWithOPCUA data, RheoSenseOPCUATagSet tags)
    {
        return ReadOPCUAValuesAsync(data, tags, standardDeviation: false);
    }

    protected Task ReadOPCUAStandardDeviationTagSetAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags)
    {
        return ReadOPCUAValuesAsync(data, tags, standardDeviation: true);
    }

    private async Task PublishOPCUAValuesAsync(
        DWISDataWithOPCUA data,
        string? configuredNamespace,
        string? configuredPrefix,
        Dictionary<string, string>? configuredIds,
        Dictionary<string, UnitConversion>? unitConversions,
        Func<PropertyInfo, bool> includeProperty,
        CancellationToken stoppingToken,
        RheoSenseOPCUATagSet? tagSet,
        bool standardDeviation)
    {
        if (_OPCUAClient is null || !_OPCUAClient.Connected)
        {
            return;
        }

        foreach ((PropertyInfo property, OPCUANode node) in data.OPCUANodes.Value)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (!includeProperty(property))
            {
                continue;
            }
            object? propertyValue = property.GetValue(data);
            object? value = EncodeValue(propertyValue);
            if (value is null || string.IsNullOrWhiteSpace(node.ID))
            {
                continue;
            }

            RheoSenseOPCUATagDefinition? definition = tagSet?.Resolve(property.Name, standardDeviation);
            if (definition is { Enabled: false } || definition?.ConstantValue is not null) continue;
            string? ns = definition?.NameSpace ?? GetNameSpace(node, configuredNamespace);
            string? id = definition?.NodeID ?? GetID(node, configuredPrefix, configuredIds);
            if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (value is double scalar && TryGetUnitConversion(unitConversions, property.Name, id, out UnitConversion conversion))
            {
                value = conversion.FromSI(scalar);
            }

            _OPCUAClient.UpdateAnyVariables(new[] { (ns, id, value, DateTime.UtcNow) });
        }

        await Task.Yield();
    }

    private async Task ReadOPCUAValuesAsync(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        bool standardDeviation)
    {
        if (_OPCUAClient is null || !_OPCUAClient.Connected)
        {
            return;
        }

        Dictionary<string, UnitConversion> unitConversions = tags.GetConversions(standardDeviation);
        foreach ((PropertyInfo property, _) in data.OPCUANodes.Value)
        {
            if (property.GetValue(data) is null)
            {
                data.CreateProperty(property);
            }

            object? propertyValue = property.GetValue(data);
            RheoSenseOPCUATagDefinition definition = tags.Resolve(property.Name, standardDeviation);
            if (!definition.Enabled) { if (propertyValue is ScalarDrillingProperty disabled) disabled.ScalarValue = null; continue; }
            if (propertyValue is null || string.IsNullOrWhiteSpace(definition.NameSpace) || string.IsNullOrWhiteSpace(definition.NodeID))
            {
                continue;
            }

            object[] values = definition.ConstantValue is double constant ? new object[] { constant } : Array.Empty<object>();
            if (values.Length > 0 || _OPCUAClient.ReadAnyVariables(out values, new[] { (definition.NameSpace, definition.NodeID) }) && values.Length > 0)
            {
                UnitConversion conversion = TryGetUnitConversion(
                    unitConversions,
                    property.Name,
                    definition.NodeID,
                    out UnitConversion configured)
                    ? configured
                    : new UnitConversion();
                DecodeValue(propertyValue, values[0], conversion);
            }
        }

        await Task.Yield();
    }

    protected static void ConfigureOPCUANodes(
        DWISDataWithOPCUA data,
        RheoSenseOPCUATagSet tags,
        bool standardDeviation = false)
    {
        foreach ((PropertyInfo property, OPCUANode node) in data.OPCUANodes.Value)
        {
            RheoSenseOPCUATagDefinition definition = tags.Resolve(property.Name, standardDeviation);
            node.NameSpace = definition.NameSpace;
            node.PrefixID = string.Empty;
            node.ID = definition.NodeID;
        }
    }

    private static bool TryGetUnitConversion(
        IReadOnlyDictionary<string, UnitConversion>? conversions,
        string propertyName,
        string nodeId,
        out UnitConversion conversion)
    {
        if (conversions is not null &&
            ((conversions.TryGetValue(propertyName, out UnitConversion? configured) && configured is not null) ||
             (conversions.TryGetValue(nodeId, out configured) && configured is not null)))
        {
            conversion = configured;
            return true;
        }

        conversion = new UnitConversion();
        return false;
    }

    protected override async Task ReadOPCUA(
        DWISDataWithOPCUA? data,
        string? configuredNamespace,
        string? configuredPrefix,
        Dictionary<string, string>? configuredIds,
        Dictionary<string, UnitConversion>? unitConversions)
    {
        if (data is null || _OPCUAClient is null || !_OPCUAClient.Connected)
        {
            return;
        }

        foreach ((PropertyInfo property, OPCUANode node) in data.OPCUANodes.Value)
        {
            if (property.GetValue(data) is null)
            {
                data.CreateProperty(property);
            }

            object? propertyValue = property.GetValue(data);
            string? ns = GetNameSpace(node, configuredNamespace);
            string? id = GetID(node, configuredPrefix, configuredIds);
            if (propertyValue is null || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (_OPCUAClient.ReadAnyVariables(out object[] values, new[] { (ns, id) }) && values.Length > 0)
            {
                UnitConversion conversion = unitConversions is not null && unitConversions.TryGetValue(id, out UnitConversion? configured)
                    ? configured
                    : new UnitConversion();
                DecodeValue(propertyValue, values[0], conversion);
            }
        }

        await Task.Yield();
    }

    protected override object? ProcessValue(object? propertyValue)
    {
        return propertyValue is FlowCurve
            ? EncodeValue(propertyValue)
            : base.ProcessValue(propertyValue);
    }

    protected override async Task PublishBlackboardAsync(DWISData data, CancellationToken stoppingToken)
    {
        await PublishBlackboardAsync(data, property => true, stoppingToken);
    }

    protected Task PublishBlackboardChannelAsync(
        DWISData data,
        RheoSenseDataChannel channel,
        CancellationToken stoppingToken)
    {
        return PublishBlackboardAsync(
            data,
            property => RheoSenseDataChannels.IsInChannel(property.Name, channel),
            stoppingToken);
    }

    private async Task PublishBlackboardAsync(
        DWISData data,
        Func<PropertyInfo, bool> includeProperty,
        CancellationToken stoppingToken)
    {
        foreach ((PropertyInfo property, _) in data.Manifests.Value)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (!includeProperty(property) ||
                !PlaceHolders.TryGetValue(property, out QueryResult? targets) ||
                property.GetValue(data) is not object propertyValue)
            {
                continue;
            }

            if (propertyValue is GaussianProperty gaussian)
            {
                PublishBlackboardValue(targets, 0, gaussian.Mean);
                PublishBlackboardValue(targets, 1, gaussian.StandardDeviation);
            }
            else if (propertyValue is ScalarProperty scalar)
            {
                PublishBlackboardValue(targets, 0, scalar.Value);
            }
            else
            {
                SendValue(targets, ProcessValue(propertyValue));
            }

            await Task.Yield();
        }
    }

    protected async Task InvalidateBlackboardAsync(DWISData data, CancellationToken token)
    {
        foreach (var (property, _) in data.Manifests.Value)
        {
            token.ThrowIfCancellationRequested();
            if (!PlaceHolders.TryGetValue(property, out var targets) || targets.Count == 0) continue;
            if (property.PropertyType == typeof(FlowCurve))
            {
                if (!SendValue(targets, "null")) throw new InvalidOperationException("Could not invalidate Blackboard flow curve.");
            }
            else for (int column = 0; column < targets[0].Count; column++) PublishBlackboardValue(targets, column, double.NaN);
            await Task.Yield();
        }
    }

    private void PublishBlackboardValue(QueryResult targets, int column, double? value)
    {
        if (_DWISClient is null || value is null || targets.Count == 0 || targets[0].Count <= column)
        {
            return;
        }

        NodeIdentifier target = targets[0][column];
        if (target is null || string.IsNullOrWhiteSpace(target.NameSpace) || string.IsNullOrWhiteSpace(target.ID))
        {
            return;
        }

        if (!_DWISClient.UpdateAnyVariables(new[] { (target.NameSpace, target.ID, (object)value.Value, DateTime.UtcNow) }))
            throw new InvalidOperationException("Could not publish Blackboard value.");
    }

    protected override void AssignValue(DWISData data, object propertyValue, object? value, DateTime? timeStamp)
    {
        if (propertyValue is not FlowCurve || !DecodeValue(propertyValue, value, new UnitConversion()))
        {
            base.AssignValue(data, propertyValue, value, timeStamp);
        }
    }

    private static object? EncodeValue(object? propertyValue)
    {
        return propertyValue switch
        {
            ScalarDrillingProperty scalar => scalar.ScalarValue,
            GaussianDrillingProperty gaussian when gaussian.Mean is not null =>
                JsonSerializer.Serialize(new GaussianPayload(gaussian.Mean, gaussian.StandardDeviation), JsonOptions),
            FlowCurve curve when curve.Measurements is not null =>
                JsonSerializer.Serialize(curve.Measurements, JsonOptions),
            _ => null
        };
    }

    private static bool DecodeValue(object propertyValue, object? value, UnitConversion conversion)
    {
        if (propertyValue is ScalarDrillingProperty && value is float or decimal or short or ushort or int or uint or long or ulong or byte or sbyte)
            value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        if (propertyValue is ScalarDrillingProperty scalar && value is double scalarValue)
        {
            scalar.ScalarValue = conversion.ToSI(scalarValue);
            return true;
        }

        if (propertyValue is GaussianDrillingProperty gaussian)
        {
            if (value is double mean)
            {
                gaussian.Mean = conversion.ToSI(mean);
                return true;
            }

            if (value is string gaussianJson)
            {
                GaussianPayload? payload = JsonSerializer.Deserialize<GaussianPayload>(gaussianJson, JsonOptions);
                if (payload is not null)
                {
                    gaussian.Mean = payload.Mean;
                    gaussian.StandardDeviation = payload.StandardDeviation;
                    return true;
                }
            }
        }

        if (propertyValue is FlowCurve curve && value is string curveJson)
        {
            IList<FlowCurveMeasurement>? measurements = JsonSerializer.Deserialize<IList<FlowCurveMeasurement>>(curveJson, JsonOptions);
            if (measurements is not null)
            {
                curve.Measurements = measurements.ToList();
                return true;
            }
        }

        return false;
    }

    private sealed record GaussianPayload(double? Mean, double? StandardDeviation);
}
