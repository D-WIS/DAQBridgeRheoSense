using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DWIS.DAQBridge.RheoSense.Model;

namespace DWIS.DAQBridge.RheoSense.WebApp.Services;

public sealed class RheoSenseHistoryLogReader
{
    private static readonly string[] PublishedPrefixes =
    {
        "rheosense-published-",
        "rheosense-active-pit-published-",
        "rheosense-flow-line-published-"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RheoSenseHistoryLogReader> _logger;

    public RheoSenseHistoryLogReader(IWebHostEnvironment environment, ILogger<RheoSenseHistoryLogReader> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<YplTrendSample>> ReadRecentAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        string configurationPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "home", "config.json"));
        ConfigurationForRheoSense configuration = await ReadConfigurationAsync(configurationPath, cancellationToken);
        string logDirectory = ResolveLogDirectory(configuration.DataLogDirectory, configurationPath);
        if (!Directory.Exists(logDirectory))
        {
            _logger.LogInformation("No RheoSense history directory was found at {LogDirectory}.", logDirectory);
            return Array.Empty<YplTrendSample>();
        }

        DateTimeOffset cutoffUtc = nowUtc.AddHours(-48);
        TimeSpan segmentDuration = configuration.DataLogSegmentDuration > TimeSpan.Zero
            ? configuration.DataLogSegmentDuration
            : TimeSpan.FromHours(6);
        List<YplTrendSample> samples = new();
        IEnumerable<string> paths = PublishedPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(logDirectory, $"{prefix}*.jsonl")
                .Concat(Directory.EnumerateFiles(logDirectory, $"{prefix}*.zip")))
            .Distinct(StringComparer.Ordinal)
            .Where(path => SegmentCanOverlap(path, cutoffUtc, segmentDuration))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ReadArchiveAsync(path, cutoffUtc, nowUtc, samples, cancellationToken);
                }
                else
                {
                    await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await ReadJsonLinesAsync(stream, cutoffUtc, nowUtc, samples, cancellationToken);
                }
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable RheoSense history file {HistoryFile}.", path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Skipping RheoSense history file {HistoryFile} while it is unavailable.", path);
            }
        }

        return samples;
    }

    public static async Task<IReadOnlyList<YplTrendSample>> ReadJsonLinesAsync(
        Stream stream,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        List<YplTrendSample> samples = new();
        await ReadJsonLinesAsync(stream, cutoffUtc, nowUtc, samples, cancellationToken);
        return samples;
    }

    private async Task<ConfigurationForRheoSense> ReadConfigurationAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                return JsonSerializer.Deserialize<ConfigurationForRheoSense>(json, JsonOptions) ?? new ConfigurationForRheoSense();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the RheoSense configuration while restoring WebApp history.");
        }

        return new ConfigurationForRheoSense();
    }

    private static string ResolveLogDirectory(string? configuredDirectory, string configurationPath)
    {
        string directory = string.IsNullOrWhiteSpace(configuredDirectory) ? "/home" : configuredDirectory;
        return Path.IsPathRooted(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configurationPath)!, directory));
    }

    private static bool SegmentCanOverlap(string path, DateTimeOffset cutoffUtc, TimeSpan duration)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string? prefix = PublishedPrefixes.FirstOrDefault(candidate =>
            name.StartsWith(candidate, StringComparison.Ordinal));
        return prefix is not null && DateTimeOffset.TryParseExact(
                   name[prefix.Length..],
                   "yyyyMMddTHHmmssZ",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                   out DateTimeOffset segmentStart) &&
               segmentStart + duration >= cutoffUtc;
    }

    private static async Task ReadArchiveAsync(
        string path,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc,
        ICollection<YplTrendSample> samples,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry =>
                     PublishedPrefixes.Any(prefix => entry.Name.StartsWith(prefix, StringComparison.Ordinal)) &&
                     entry.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)))
        {
            await using Stream entryStream = entry.Open();
            await ReadJsonLinesAsync(entryStream, cutoffUtc, nowUtc, samples, cancellationToken);
        }
    }

    private static async Task ReadJsonLinesAsync(
        Stream stream,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc,
        ICollection<YplTrendSample> samples,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream);
        string? schemaLine = await reader.ReadLineAsync(cancellationToken);
        PublishedSchema? schema = Deserialize<PublishedSchema>(schemaLine);
        if (schema?.Format != "rheosense.published.v1")
        {
            return;
        }

        FieldIndexes indexes = FieldIndexes.From(schema.Fields);
        if (!indexes.IsComplete)
        {
            return;
        }
        CurveIndexes curveIndexes = CurveIndexes.From(schema.Curves ?? Array.Empty<string>());

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            CompactPublishedRecord? record = Deserialize<CompactPublishedRecord>(line);
            if (record is null || record.TimestampUtc < cutoffUtc || record.TimestampUtc > nowUtc)
            {
                continue;
            }

            DateTimeOffset timestampUtc = record.TimestampUtc.ToUniversalTime();

            if (string.Equals(
                    record.Channel,
                    RheoSenseDataChannel.DensityPressureTemperature.ToString(),
                    StringComparison.Ordinal))
            {
                samples.Add(new YplTrendSample(
                    timestampUtc,
                    YplValues.Empty,
                    YplValues.Empty,
                    YplValues.Empty,
                    YplValues.Empty,
                    ReadConditions(record.ActivePit),
                    ReadConditions(record.FlowLine)));
                continue;
            }

            samples.Add(new YplTrendSample(
                timestampUtc,
                indexes.ReadMeasured(record.ActivePit),
                indexes.ReadExtrapolated(record.ActivePit),
                indexes.ReadMeasured(record.FlowLine),
                indexes.ReadExtrapolated(record.FlowLine),
                null,
                null,
                ReadFlowCurves(record.ActivePitCurves, record.ActivePit, indexes, curveIndexes),
                ReadFlowCurves(record.FlowLineCurves, record.FlowLine, indexes, curveIndexes)));
        }
    }

    private static FlowCurveSnapshot? ReadFlowCurves(
        double?[][][]? curves,
        IReadOnlyList<double?> signals,
        FieldIndexes fieldIndexes,
        CurveIndexes curveIndexes)
    {
        if (curves is null || !curveIndexes.IsComplete)
        {
            return null;
        }

        IReadOnlyList<FlowCurvePoint> trueCurrent = ReadCurve(curves, curveIndexes.Current, 3, 9);
        IReadOnlyList<FlowCurvePoint> fannCurrent = ReadCurve(curves, curveIndexes.Current, 1, 7);
        IReadOnlyList<FlowCurvePoint> trueExtrapolated = ReadCurve(curves, curveIndexes.Extrapolated, 3, 9);
        IReadOnlyList<FlowCurvePoint> fannExtrapolated = ReadCurve(curves, curveIndexes.Extrapolated, 1, 7);
        if (trueCurrent.Count == 0 && fannCurrent.Count == 0 &&
            trueExtrapolated.Count == 0 && fannExtrapolated.Count == 0)
        {
            return null;
        }

        return new FlowCurveSnapshot(
            fieldIndexes.ReadMeasuredTemperature(signals),
            fieldIndexes.ReadMeasuredPressure(signals),
            fieldIndexes.ReadExtrapolatedTemperature(signals),
            fieldIndexes.ReadExtrapolatedPressure(signals),
            trueCurrent,
            fannCurrent,
            trueExtrapolated,
            fannExtrapolated);
    }

    private static IReadOnlyList<FlowCurvePoint> ReadCurve(
        IReadOnlyList<double?[][]> curves,
        int curveIndex,
        int shearRateIndex,
        int shearStressIndex)
    {
        if (curveIndex < 0 || curveIndex >= curves.Count)
        {
            return Array.Empty<FlowCurvePoint>();
        }

        return curves[curveIndex]
            .Where(values => values.Length > shearStressIndex &&
                values[shearRateIndex] is double shearRate && double.IsFinite(shearRate) &&
                values[shearStressIndex] is double shearStress && double.IsFinite(shearStress))
            .Select(values => new FlowCurvePoint(
                values[shearRateIndex]!.Value,
                values[shearStressIndex]!.Value))
            .OrderBy(point => point.ShearRate)
            .ToArray();
    }

    private static ConditionValues? ReadConditions(IReadOnlyList<double?> values)
    {
        // Channel-specific compact records contain four Gaussian properties,
        // each represented as mean followed by standard deviation.
        if (values.Count < 8)
        {
            return null;
        }

        return new ConditionValues(
            Clean(values[0]),
            Clean(values[2]),
            Clean(values[4]),
            Clean(values[6]));
    }

    private static double? Clean(double? value)
    {
        return value is not null && double.IsFinite(value.Value) ? value : null;
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // The active JSONL file can end in a partial line if the process is
            // interrupted during an append. Earlier complete records remain usable.
            return default;
        }
    }

    private sealed record PublishedSchema(
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("fields")] string[] Fields,
        [property: JsonPropertyName("curves")] string[]? Curves);

    private sealed record CompactPublishedRecord(
        [property: JsonPropertyName("t")] DateTimeOffset TimestampUtc,
        [property: JsonPropertyName("c")] string? Channel,
        [property: JsonPropertyName("ap")] double?[] ActivePit,
        [property: JsonPropertyName("apc")] double?[][][]? ActivePitCurves,
        [property: JsonPropertyName("fl")] double?[] FlowLine,
        [property: JsonPropertyName("flc")] double?[][][]? FlowLineCurves);

    private sealed record FieldIndexes(
        int MeasuredYieldStress,
        int MeasuredConsistencyIndex,
        int MeasuredFlowBehaviorIndex,
        int ExtrapolatedYieldStress,
        int ExtrapolatedConsistencyIndex,
        int ExtrapolatedFlowBehaviorIndex,
        int MeasuredTemperature,
        int MeasuredPressure,
        int ExtrapolatedTemperature,
        int ExtrapolatedPressure)
    {
        public bool IsComplete => new[]
        {
            MeasuredYieldStress,
            MeasuredConsistencyIndex,
            MeasuredFlowBehaviorIndex,
            ExtrapolatedYieldStress,
            ExtrapolatedConsistencyIndex,
            ExtrapolatedFlowBehaviorIndex
        }.All(index => index >= 0);

        public static FieldIndexes From(IReadOnlyList<string> fields) => new(
            IndexOf(fields, "YPLYieldStressAtMeasuredTemperatureAndPressure.mean"),
            IndexOf(fields, "YPLConsistencyIndexAtMeasuredTemperatureAndPressure.mean"),
            IndexOf(fields, "YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure.mean"),
            IndexOf(fields, "YPLYieldStressAtExtrapolatedTemperatureAndPressure.mean"),
            IndexOf(fields, "YPLConsistencyIndexAtExtrapolatedTemperatureAndPressure.mean"),
            IndexOf(fields, "YPLFlowBehaviorIndexAtExtrapolatedTemperatureAndPressure.mean"),
            IndexOf(fields, "MeasuredTemperature.mean"),
            IndexOf(fields, "MeasuredPressure.mean"),
            IndexOf(fields, "ExtrapolatedTemperature.value"),
            IndexOf(fields, "ExtrapolatedPressure.value"));

        public YplValues ReadMeasured(double?[] values) => new(
            Read(values, MeasuredYieldStress),
            Read(values, MeasuredConsistencyIndex),
            Read(values, MeasuredFlowBehaviorIndex));

        public YplValues ReadExtrapolated(double?[] values) => new(
            Read(values, ExtrapolatedYieldStress),
            Read(values, ExtrapolatedConsistencyIndex),
            Read(values, ExtrapolatedFlowBehaviorIndex));

        public double? ReadMeasuredTemperature(IReadOnlyList<double?> values) => Read(values, MeasuredTemperature);
        public double? ReadMeasuredPressure(IReadOnlyList<double?> values) => Read(values, MeasuredPressure);
        public double? ReadExtrapolatedTemperature(IReadOnlyList<double?> values) => Read(values, ExtrapolatedTemperature);
        public double? ReadExtrapolatedPressure(IReadOnlyList<double?> values) => Read(values, ExtrapolatedPressure);

        private static int IndexOf(IReadOnlyList<string> fields, string value)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                if (string.Equals(fields[index], value, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private static double? Read(IReadOnlyList<double?> values, int index)
        {
            double? value = index >= 0 && index < values.Count ? values[index] : null;
            return value is not null && double.IsFinite(value.Value) ? value : null;
        }
    }

    private sealed record CurveIndexes(int Current, int Extrapolated)
    {
        public bool IsComplete => Current >= 0 && Extrapolated >= 0;

        public static CurveIndexes From(IReadOnlyList<string> curves) => new(
            IndexOf(curves, "FlowCurveAtMeasuredTemperatureAndPressure"),
            IndexOf(curves, "FlowCurveAtExtrapolatedTemperatureAndPressure"));

        private static int IndexOf(IReadOnlyList<string> values, string expected)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], expected, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
