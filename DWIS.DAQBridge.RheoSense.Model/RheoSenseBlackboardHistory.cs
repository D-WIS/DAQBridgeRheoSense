using System.Text.Json;
using DWIS.API.DTO;

namespace DWIS.DAQBridge.RheoSense.Model;

public static class RheoSenseBlackboardHistory
{
    public const int BucketCount = 8;
    public static readonly TimeSpan BucketDuration = TimeSpan.FromHours(6);
    private const string NodePrefix = "RheoSense";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEnumerable<string> NodeIds =>
        from sensor in Enum.GetValues<RheoSenseSensorPosition>()
        from kind in Enum.GetValues<RheoSenseHistoryDataKind>()
        from slot in Enumerable.Range(0, BucketCount)
        select GetNodeId(sensor, kind, slot);

    public static IEnumerable<string> PublishedNodeIds =>
        from sensor in Enum.GetValues<RheoSenseSensorPosition>()
        from slot in Enumerable.Range(0, BucketCount)
        select GetNodeId(sensor, RheoSenseHistoryDataKind.Published, slot);

    public static string GetNodeId(
        RheoSenseSensorPosition sensor,
        RheoSenseHistoryDataKind kind,
        int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, BucketCount);
        string sensorName = sensor == RheoSenseSensorPosition.ActivePit ? "ActivePit" : "FlowLine";
        return $"{NodePrefix}.{sensorName}.{kind}.History.{slot}";
    }

    public static string GetNodeId(string source, RheoSenseHistoryDataKind kind, int slot)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(source, "^[A-Za-z][A-Za-z0-9_]{0,63}\\z")) throw new ArgumentException("Invalid source identity.");
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, BucketCount);
        return $"{NodePrefix}.{source}.{kind}.History.{slot}";
    }
    public static ManifestFile CreateManifest(IEnumerable<string> sources)
    {
        var manifest = CreateManifest();
        manifest.ProvidedVariables = (from source in sources.Distinct(StringComparer.Ordinal)
            from kind in Enum.GetValues<RheoSenseHistoryDataKind>() from slot in Enumerable.Range(0, BucketCount)
            select new ProvidedVariable { DataType = "string", VariableID = GetNodeId(source, kind, slot) }).ToList();
        return manifest;
    }

    public static DateTimeOffset GetBucketStart(DateTimeOffset timestampUtc)
    {
        long ticks = BucketDuration.Ticks;
        return new DateTimeOffset((timestampUtc.UtcTicks / ticks) * ticks, TimeSpan.Zero);
    }

    public static int GetBucketSlot(DateTimeOffset bucketStartUtc)
    {
        return (int)((bucketStartUtc.UtcTicks / BucketDuration.Ticks) % BucketCount);
    }

    public static string Append(
        string? existingPayload,
        DateTimeOffset timestampUtc,
        RheoSenseCompactLogEntry entry)
    {
        DateTimeOffset bucketStart = GetBucketStart(timestampUtc);
        string jsonLines;
        if (TryDeserialize(existingPayload, out RheoSenseHistoryBucketPayload? existing) &&
            existing is not null &&
            existing.BucketStartUtc == bucketStart)
        {
            jsonLines = existing.JsonLines.TrimEnd('\r', '\n') + Environment.NewLine + entry.RecordLine + Environment.NewLine;
        }
        else
        {
            jsonLines = entry.SchemaLine + Environment.NewLine + entry.RecordLine + Environment.NewLine;
        }

        return JsonSerializer.Serialize(new RheoSenseHistoryBucketPayload(bucketStart, jsonLines), JsonOptions);
    }

    public static bool TryDeserialize(string? payload, out RheoSenseHistoryBucketPayload? bucket)
    {
        bucket = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            bucket = JsonSerializer.Deserialize<RheoSenseHistoryBucketPayload>(payload, JsonOptions);
            return bucket is not null && !string.IsNullOrWhiteSpace(bucket.JsonLines);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ManifestFile CreateManifest()
    {
        return new ManifestFile
        {
            ManifestName = "RheoSenseHistory",
            Provider = new InjectionProvider { Company = "DigiWells", Name = "RheoSenseHistory" },
            InjectionInformation = new InjectionInformation
            {
                ServerName = "RheoSense",
                EndPointURL = string.Empty,
                InjectedNodesNamespaceAlias = "RheoSenseNodes",
                InjectedVariablesNamespaceAlias = "RheoSenseVariables",
                ProvidedVariablesNamespaceAlias = "RheoSense"
            },
            InjectedNodes = new List<InjectedNode>(),
            InjectedReferences = new List<InjectedReference>(),
            InjectedVariables = new List<InjectedVariable>(),
            ProvidedVariables = NodeIds
                .Select(id => new ProvidedVariable { DataType = "string", VariableID = id })
                .ToList()
        };
    }
}

public enum RheoSenseHistoryDataKind
{
    Raw,
    Published
}

public sealed record RheoSenseHistoryBucketPayload(
    DateTimeOffset BucketStartUtc,
    string JsonLines);

public sealed record RheoSenseCompactLogEntry(
    string SchemaLine,
    string RecordLine);
