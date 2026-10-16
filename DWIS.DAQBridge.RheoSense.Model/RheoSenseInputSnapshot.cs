using System.Reflection;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>
/// An immutable value snapshot used to detect whether one RheoSense instrument
/// has supplied new values for one independently refreshed data channel. OPC-UA
/// source timestamps are excluded because the instrument can republish unchanged
/// values.
/// </summary>
public sealed class RheoSenseInputSnapshot : IEquatable<RheoSenseInputSnapshot>
{
    private static readonly PropertyInfo[] Properties = typeof(RheoSenseInstrumentData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(property => property.PropertyType == typeof(ScalarDrillingProperty))
        .OrderBy(property => property.MetadataToken)
        .ToArray();

    private readonly double?[] _values;

    private RheoSenseInputSnapshot(double?[] values)
    {
        _values = values;
    }

    public static RheoSenseInputSnapshot Capture(
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations)
    {
        return Capture(values, standardDeviations, channel: null);
    }

    public static RheoSenseInputSnapshot Capture(
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations,
        RheoSenseDataChannel channel)
    {
        return Capture(values, standardDeviations, (RheoSenseDataChannel?)channel);
    }

    private static RheoSenseInputSnapshot Capture(
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations,
        RheoSenseDataChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(standardDeviations);

        PropertyInfo[] selectedProperties = channel is null
            ? Properties
            : Properties.Where(property => RheoSenseDataChannels.IsInChannel(property.Name, channel.Value)).ToArray();
        double?[] snapshot = new double?[selectedProperties.Length * 2];
        for (int index = 0; index < selectedProperties.Length; index++)
        {
            snapshot[index] = Read(selectedProperties[index], values);
            snapshot[index + selectedProperties.Length] = Read(selectedProperties[index], standardDeviations);
        }

        return new RheoSenseInputSnapshot(snapshot);
    }

    public bool Equals(RheoSenseInputSnapshot? other)
    {
        return other is not null && _values.AsSpan().SequenceEqual(other._values);
    }

    public override bool Equals(object? obj) => Equals(obj as RheoSenseInputSnapshot);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (double? value in _values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    private static double? Read(PropertyInfo property, RheoSenseInstrumentData data)
    {
        return (property.GetValue(data) as ScalarDrillingProperty)?.ScalarValue;
    }
}

public enum RheoSenseDataChannel
{
    DensityPressureTemperature,
    Rheology
}

public static class RheoSenseDataChannels
{
    private static readonly HashSet<string> DensityPressureTemperatureProperties = new(StringComparer.Ordinal)
    {
        nameof(RheoSenseInstrumentData.MeasuredTemperature),
        nameof(RheoSenseInstrumentData.MeasuredPressure),
        nameof(RheoSenseInstrumentData.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure),
        nameof(RheoSenseInstrumentData.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure)
    };

    public static bool IsInChannel(string propertyName, RheoSenseDataChannel channel)
    {
        bool isDensityPressureTemperature = DensityPressureTemperatureProperties.Contains(propertyName);
        return channel == RheoSenseDataChannel.DensityPressureTemperature
            ? isDensityPressureTemperature
            : !isDensityPressureTemperature;
    }
}
