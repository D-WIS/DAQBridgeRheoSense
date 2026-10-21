using System.Reflection;
using DWIS.API.DTO;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>PLC states, kept separate from engineering measurements and their uncertainties.</summary>
public sealed class RheoSenseStateData : DWISDataWithOPCUA
{
    public ScalarDrillingProperty? Gel10SecondsActive { get; set; }
    public ScalarDrillingProperty? Gel10MinutesActive { get; set; }
    public ScalarDrillingProperty? Automatic { get; set; }
    public ScalarDrillingProperty? RunningHeartbeat { get; set; }
    public ScalarDrillingProperty? OperatingHours { get; set; }
    // Vendor document does not specify the encoding or unit of OperationTime.
    public ScalarDrillingProperty? OperationTime { get; set; }
    public override Lazy<IReadOnlyDictionary<PropertyInfo, OPCUANode>> OPCUANodes { get; }
    public override Lazy<IReadOnlyDictionary<PropertyInfo, Dictionary<string, QuerySpecification>>> SparQLQueries { get; } = new(() => new Dictionary<PropertyInfo, Dictionary<string, QuerySpecification>>());
    public override Lazy<IReadOnlyDictionary<PropertyInfo, ManifestFile>> Manifests { get; } = new(() => new Dictionary<PropertyInfo, ManifestFile>());
    public RheoSenseStateData(RheometerSource source, RheoSenseOPCUATagSet tags)
    {
        OPCUANodes = new(() => GetType().GetProperties().Where(p => p.PropertyType == typeof(ScalarDrillingProperty))
            .Where(p => !tags.StateSignals.TryGetValue(p.Name, out var t) || t.Enabled)
            .ToDictionary(p => p, p => {
                tags.StateSignals.TryGetValue(p.Name, out var t);
                return new OPCUANode { NameSpace = t?.NameSpace ?? source.Namespace, PrefixID = "", ID = t?.NodeID ?? source.Prefix + "State_" + p.Name };
            }));
    }
}

/// <summary>A cached heartbeat value or a new timestamp alone never proves that the PLC is running.</summary>
public sealed class RunningHeartbeatMonitor
{
    private double? previous;
    private DateTimeOffset? changed;
    public bool Observe(double? value, DateTimeOffset now, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (value is not (1 or 2)) { changed = null; return false; }
        if (previous is not null && previous != value) changed = now;
        previous = value;
        return changed is not null && now - changed.Value < timeout;
    }
}
