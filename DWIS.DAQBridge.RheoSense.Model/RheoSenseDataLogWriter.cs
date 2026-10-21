using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>
/// Writes compact, append-only RheoSense logs. Semantic annotations, manifests,
/// and SPARQL queries never enter the log payload.
/// </summary>
public sealed class RheoSenseDataLogWriter
{
    private const long DefaultMaximumStorageBytes = 1024L * 1024L * 1024L;
    private const string RawPrefix = "rheosense-raw";
    private const string PublishedPrefix = "rheosense-published";
    private const string ActivePitRawPrefix = "rheosense-active-pit-raw";
    private const string ActivePitPublishedPrefix = "rheosense-active-pit-published";
    private const string FlowLineRawPrefix = "rheosense-flow-line-raw";
    private const string FlowLinePublishedPrefix = "rheosense-flow-line-published";
    private static readonly string[] LogPrefixes =
    {
        RawPrefix,
        PublishedPrefix,
        ActivePitRawPrefix,
        ActivePitPublishedPrefix,
        FlowLineRawPrefix,
        FlowLinePublishedPrefix,
        "rheosense-source"
    };
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly PropertyInfo[] RawProperties = typeof(RheoSenseInstrumentData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(property => property.PropertyType == typeof(ScalarDrillingProperty))
        .OrderBy(property => property.MetadataToken)
        .ToArray();
    private static readonly PropertyInfo[] PublishedSignalProperties = typeof(RheometerOutputData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(property => property.PropertyType == typeof(GaussianProperty) || property.PropertyType == typeof(ScalarProperty))
        .OrderBy(property => property.MetadataToken)
        .ToArray();
    private static readonly PropertyInfo[] PublishedCurveProperties = typeof(RheometerOutputData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(property => property.PropertyType == typeof(FlowCurve))
        .OrderBy(property => property.MetadataToken)
        .ToArray();
    private static readonly string[] PublishedFields = PublishedSignalProperties
        .SelectMany(property => property.PropertyType == typeof(GaussianProperty)
            ? new[] { $"{property.Name}.mean", $"{property.Name}.standardDeviation" }
            : new[] { $"{property.Name}.value" })
        .ToArray();
    private static readonly string[] FlowCurveFields =
    {
        "rotationalSpeed.value",
        "shearRateNewtonian.mean",
        "shearRateNewtonian.standardDeviation",
        "shearRateNonNewtonian.mean",
        "shearRateNonNewtonian.standardDeviation",
        "torque.mean",
        "torque.standardDeviation",
        "shearStressNewtonian.mean",
        "shearStressNewtonian.standardDeviation",
        "shearStressNonNewtonian.mean",
        "shearStressNonNewtonian.standardDeviation"
    };

    private readonly string _directory;
    private readonly TimeSpan _segmentDuration;
    private readonly long _maximumStorageBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RheoSenseDataLogWriter(string? directory, TimeSpan segmentDuration, long maximumStorageBytes)
    {
        _directory = ResolveDirectory(string.IsNullOrWhiteSpace(directory) ? "/home" : directory);
        _segmentDuration = segmentDuration > TimeSpan.Zero ? segmentDuration : TimeSpan.FromHours(6);
        _maximumStorageBytes = maximumStorageBytes > 0 ? maximumStorageBytes : DefaultMaximumStorageBytes;
    }

    private static string ResolveDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(directory, "/home", StringComparison.Ordinal))
        {
            return directory;
        }

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "home");
            if (File.Exists(Path.Combine(candidate, "config.json")))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return directory;
    }

    public async Task AppendRawAsync(
        DateTimeOffset timestampUtc,
        RheoSenseInstrumentData activePit,
        RheoSenseInstrumentData activePitStandardDeviations,
        RheoSenseInstrumentData flowLine,
        RheoSenseInstrumentData flowLineStandardDeviations)
    {
        CompactRawRecord record = new(
            timestampUtc.ToUniversalTime(),
            null,
            ExtractRawValues(activePit),
            ExtractRawValues(activePitStandardDeviations),
            ExtractRawValues(flowLine),
            ExtractRawValues(flowLineStandardDeviations));
        RawSchema schema = new(
            "schema",
            "rheosense.raw.v1",
            "SI",
            RawProperties.Select(property => property.Name).ToArray());

        await AppendAsync(RawPrefix, timestampUtc, schema, record);
    }

    public async Task AppendPublishedAsync(
        DateTimeOffset timestampUtc,
        ActivePitOutputData activePit,
        FlowLineOutputData flowLine)
    {
        CompactPublishedRecord record = new(
            timestampUtc.ToUniversalTime(),
            null,
            ExtractPublishedSignals(activePit),
            ExtractCurves(activePit),
            ExtractPublishedSignals(flowLine),
            ExtractCurves(flowLine));
        PublishedSchema schema = new(
            "schema",
            "rheosense.published.v1",
            "SI",
            PublishedFields,
            PublishedCurveProperties.Select(property => property.Name).ToArray(),
            FlowCurveFields);

        await AppendAsync(PublishedPrefix, timestampUtc, schema, record);
    }

    public Task AppendRawSensorAsync(
        DateTimeOffset timestampUtc,
        RheoSenseSensorPosition sensor,
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations,
        RheoSenseDataChannel? channel = null)
    {
        RheoSenseCompactLogEntry entry = CreateRawSensorEntry(timestampUtc, sensor, values, standardDeviations, channel);
        return AppendSerializedAsync(
            sensor == RheoSenseSensorPosition.ActivePit ? ActivePitRawPrefix : FlowLineRawPrefix,
            timestampUtc,
            entry);
    }

    public Task AppendPublishedSensorAsync(
        DateTimeOffset timestampUtc,
        RheoSenseSensorPosition sensor,
        IRheoSenseOutputData data,
        RheoSenseDataChannel? channel = null)
    {
        RheoSenseCompactLogEntry entry = CreatePublishedSensorEntry(timestampUtc, sensor, data, channel);
        return AppendSerializedAsync(
            sensor == RheoSenseSensorPosition.ActivePit ? ActivePitPublishedPrefix : FlowLinePublishedPrefix,
            timestampUtc,
            entry);
    }

    // Additional installations use source-scoped v2 streams; legacy two-slot records remain unchanged.
    public Task AppendRawSensorAsync(DateTimeOffset time, string source, RheoSenseInstrumentData values, RheoSenseInstrumentData sigmas, RheoSenseDataChannel? channel = null) =>
        source is "ActivePit" or "FlowLine" ? AppendRawSensorAsync(time, Enum.Parse<RheoSenseSensorPosition>(source), values, sigmas, channel) :
        AppendSerializedAsync("rheosense-source-" + source + "-raw", time, CreateRawSensorEntry(time, source, values, sigmas, channel));
    public Task AppendPublishedSensorAsync(DateTimeOffset time, string source, IRheoSenseOutputData data, RheoSenseDataChannel? channel = null) =>
        source is "ActivePit" or "FlowLine" ? AppendPublishedSensorAsync(time, Enum.Parse<RheoSenseSensorPosition>(source), data, channel) :
        AppendSerializedAsync("rheosense-source-" + source + "-published", time, CreatePublishedSensorEntry(time, source, data, channel));
    public static RheoSenseCompactLogEntry CreateRawSensorEntry(DateTimeOffset time, string source, RheoSenseInstrumentData values, RheoSenseInstrumentData sigmas, RheoSenseDataChannel? channel = null) =>
        source is "ActivePit" or "FlowLine" ? CreateRawSensorEntry(time, Enum.Parse<RheoSenseSensorPosition>(source), values, sigmas, channel) :
        SerializeEntry(new { kind = "schema", schema = "rheosense.source.raw.v2", units = "SI", fields = RawProperties.Select(p => p.Name) },
            new { t = time.ToUniversalTime(), source, channel = channel?.ToString(), values = ExtractRawValues(values, channel), sigmas = ExtractRawValues(sigmas, channel) });
    public static RheoSenseCompactLogEntry CreatePublishedSensorEntry(DateTimeOffset time, string source, IRheoSenseOutputData data, RheoSenseDataChannel? channel = null) =>
        source is "ActivePit" or "FlowLine" ? CreatePublishedSensorEntry(time, Enum.Parse<RheoSenseSensorPosition>(source), data, channel) :
        SerializeEntry(new { kind = "schema", schema = "rheosense.source.published.v2", units = "SI", fields = PublishedFields, curveFields = FlowCurveFields, curves = PublishedCurveProperties.Select(p => p.Name) },
            new { t = time.ToUniversalTime(), source, channel = channel?.ToString(), values = ExtractPublishedSignals(data, channel), curves = channel == RheoSenseDataChannel.DensityPressureTemperature ? Array.Empty<double?[][]>() : ExtractCurves(data) });

    public static RheoSenseCompactLogEntry CreateRawSensorEntry(
        DateTimeOffset timestampUtc,
        RheoSenseSensorPosition sensor,
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations,
        RheoSenseDataChannel? channel = null)
    {
        double?[] sensorValues = ExtractRawValues(values, channel);
        double?[] sensorStandardDeviations = ExtractRawValues(standardDeviations, channel);
        timestampUtc = timestampUtc.ToUniversalTime();
        CompactRawRecord record = sensor == RheoSenseSensorPosition.ActivePit
            ? new(timestampUtc, channel?.ToString(), sensorValues, sensorStandardDeviations, Array.Empty<double?>(), Array.Empty<double?>())
            : new(timestampUtc, channel?.ToString(), Array.Empty<double?>(), Array.Empty<double?>(), sensorValues, sensorStandardDeviations);
        RawSchema schema = new(
            "schema",
            "rheosense.raw.v1",
            "SI",
            RawProperties.Select(property => property.Name).ToArray());
        return SerializeEntry(schema, record);
    }

    public static RheoSenseCompactLogEntry CreatePublishedSensorEntry(
        DateTimeOffset timestampUtc,
        RheoSenseSensorPosition sensor,
        IRheoSenseOutputData data,
        RheoSenseDataChannel? channel = null)
    {
        double?[] signals = ExtractPublishedSignals(data, channel);
        double?[][][] curves = channel == RheoSenseDataChannel.DensityPressureTemperature
            ? Array.Empty<double?[][]>()
            : ExtractCurves(data);
        timestampUtc = timestampUtc.ToUniversalTime();
        CompactPublishedRecord record = sensor == RheoSenseSensorPosition.ActivePit
            ? new(timestampUtc, channel?.ToString(), signals, curves, Array.Empty<double?>(), Array.Empty<double?[][]>())
            : new(timestampUtc, channel?.ToString(), Array.Empty<double?>(), Array.Empty<double?[][]>(), signals, curves);
        PublishedSchema schema = new(
            "schema",
            "rheosense.published.v1",
            "SI",
            PublishedFields,
            PublishedCurveProperties.Select(property => property.Name).ToArray(),
            FlowCurveFields);
        return SerializeEntry(schema, record);
    }

    private async Task AppendAsync<TSchema, TRecord>(
        string prefix,
        DateTimeOffset timestampUtc,
        TSchema schema,
        TRecord record)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_directory);
            DateTimeOffset segmentStart = GetSegmentStart(timestampUtc);
            string currentPath = GetCurrentPath(prefix, segmentStart);

            await CompressCompletedSegmentsAsync(segmentStart);

            bool writeSchema = !File.Exists(currentPath) || new FileInfo(currentPath).Length == 0;
            StringBuilder content = new();
            if (writeSchema)
            {
                content.AppendLine(JsonSerializer.Serialize(schema, JsonOptions));
            }
            content.AppendLine(JsonSerializer.Serialize(record, JsonOptions));

            await File.AppendAllTextAsync(currentPath, content.ToString(), Utf8WithoutBom);
            EnforceStorageLimit();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AppendSerializedAsync(
        string prefix,
        DateTimeOffset timestampUtc,
        RheoSenseCompactLogEntry entry)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_directory);
            DateTimeOffset segmentStart = GetSegmentStart(timestampUtc);
            string currentPath = GetCurrentPath(prefix, segmentStart);

            await CompressCompletedSegmentsAsync(segmentStart);

            bool writeSchema = !File.Exists(currentPath) || new FileInfo(currentPath).Length == 0;
            StringBuilder content = new();
            if (writeSchema)
            {
                content.AppendLine(entry.SchemaLine);
            }
            content.AppendLine(entry.RecordLine);

            await File.AppendAllTextAsync(currentPath, content.ToString(), Utf8WithoutBom);
            EnforceStorageLimit();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RheoSenseCompactLogEntry SerializeEntry<TSchema, TRecord>(TSchema schema, TRecord record)
    {
        return new RheoSenseCompactLogEntry(
            JsonSerializer.Serialize(schema, JsonOptions),
            JsonSerializer.Serialize(record, JsonOptions));
    }

    private async Task CompressCompletedSegmentsAsync(DateTimeOffset currentSegmentStart)
    {
        foreach (string sourcePath in GetLogFiles(".jsonl"))
        {
            if (!TryParseSegmentStart(sourcePath, out DateTimeOffset segmentStart) || segmentStart >= currentSegmentStart)
            {
                continue;
            }

            string archivePath = Path.ChangeExtension(sourcePath, ".zip");
            string temporaryPath = archivePath + ".tmp";
            if (File.Exists(archivePath))
            {
                File.Delete(sourcePath);
                continue;
            }

            try
            {
                await using (FileStream archiveStream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(Path.GetFileName(sourcePath), CompressionLevel.SmallestSize);
                    await using Stream entryStream = entry.Open();
                    await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await sourceStream.CopyToAsync(entryStream);
                }

                File.Move(temporaryPath, archivePath, true);
                File.Delete(sourcePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private void EnforceStorageLimit()
    {
        List<FileInfo> files = GetLogFiles(".jsonl")
            .Concat(GetLogFiles(".zip"))
            .Select(path => new FileInfo(path))
            .OrderBy(file => TryParseSegmentStart(file.FullName, out DateTimeOffset segmentStart)
                ? segmentStart
                : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
            .ToList();
        long totalBytes = files.Sum(file => file.Length);

        foreach (FileInfo file in files)
        {
            if (totalBytes <= _maximumStorageBytes)
            {
                break;
            }

            // File.Delete removes the file directly and does not use an OS
            // recycle bin. Active files are closed after every append, so even
            // they can be removed safely if required to enforce the hard cap.
            long length = file.Length;
            file.Delete();
            totalBytes -= length;
        }
    }

    private IEnumerable<string> GetLogFiles(string extension)
    {
        return Directory.EnumerateFiles(_directory, $"rheosense-*{extension}", SearchOption.TopDirectoryOnly)
            .Where(path => LogPrefixes.Any(prefix =>
                Path.GetFileName(path).StartsWith(prefix + "-", StringComparison.Ordinal)));
    }

    private string GetCurrentPath(string prefix, DateTimeOffset segmentStart)
    {
        return Path.Combine(_directory, $"{prefix}-{segmentStart:yyyyMMddTHHmmssZ}.jsonl");
    }

    private DateTimeOffset GetSegmentStart(DateTimeOffset timestampUtc)
    {
        long ticks = _segmentDuration.Ticks;
        return new DateTimeOffset((timestampUtc.UtcTicks / ticks) * ticks, TimeSpan.Zero);
    }

    private static bool TryParseSegmentStart(string path, out DateTimeOffset segmentStart)
    {
        segmentStart = default;
        string fileName = Path.GetFileNameWithoutExtension(path);
        int timestampSeparator = fileName.LastIndexOf('-');
        return timestampSeparator >= 0 && DateTimeOffset.TryParseExact(
            fileName[(timestampSeparator + 1)..],
            "yyyyMMddTHHmmssZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out segmentStart);
    }

    private static double?[] ExtractRawValues(
        RheoSenseInstrumentData data,
        RheoSenseDataChannel? channel = null)
    {
        IEnumerable<PropertyInfo> properties = channel == RheoSenseDataChannel.DensityPressureTemperature
            ? RawProperties.Where(property => RheoSenseDataChannels.IsInChannel(
                property.Name,
                RheoSenseDataChannel.DensityPressureTemperature))
            : RawProperties;
        return properties
            .Select(property => Clean((property.GetValue(data) as ScalarDrillingProperty)?.ScalarValue))
            .ToArray();
    }

    private static double?[] ExtractPublishedSignals(
        object data,
        RheoSenseDataChannel? channel = null)
    {
        List<double?> values = new(PublishedFields.Length);
        IEnumerable<PropertyInfo> properties = channel == RheoSenseDataChannel.DensityPressureTemperature
            ? PublishedSignalProperties.Where(property => RheoSenseDataChannels.IsInChannel(
                property.Name,
                RheoSenseDataChannel.DensityPressureTemperature))
            : PublishedSignalProperties;
        foreach (PropertyInfo sourceProperty in properties)
        {
            PropertyInfo? property = data.GetType().GetProperty(sourceProperty.Name);
            object? value = property?.GetValue(data);
            if (value is GaussianProperty gaussian)
            {
                values.Add(Clean(gaussian.Mean));
                values.Add(Clean(gaussian.StandardDeviation));
            }
            else if (value is ScalarProperty scalar)
            {
                values.Add(Clean(scalar.Value));
            }
            else
            {
                values.Add(null);
                if (sourceProperty.PropertyType == typeof(GaussianProperty))
                {
                    values.Add(null);
                }
            }
        }
        return values.ToArray();
    }

    private static double?[][][] ExtractCurves(object data)
    {
        return PublishedCurveProperties.Select(sourceProperty =>
        {
            FlowCurve? curve = data.GetType().GetProperty(sourceProperty.Name)?.GetValue(data) as FlowCurve;
            return curve?.Measurements?.Select(measurement => new double?[]
            {
                Clean(measurement.ConcentricCylindersRotationalSpeed?.Value),
                Clean(measurement.ConcentricCylindersShearRateNewtonianHypothesis?.Mean),
                Clean(measurement.ConcentricCylindersShearRateNewtonianHypothesis?.StandardDeviation),
                Clean(measurement.ConcentricCylindersShearRateNonNewtonianHypothesis?.Mean),
                Clean(measurement.ConcentricCylindersShearRateNonNewtonianHypothesis?.StandardDeviation),
                Clean(measurement.ConcentricCylindersTorque?.Mean),
                Clean(measurement.ConcentricCylindersTorque?.StandardDeviation),
                Clean(measurement.ConcentricCylindersShearStressNewtonianHypothesis?.Mean),
                Clean(measurement.ConcentricCylindersShearStressNewtonianHypothesis?.StandardDeviation),
                Clean(measurement.ConcentricCylindersShearStressNonNewtonianHypothesis?.Mean),
                Clean(measurement.ConcentricCylindersShearStressNonNewtonianHypothesis?.StandardDeviation)
            }).ToArray() ?? Array.Empty<double?[]>();
        }).ToArray();
    }

    private static double? Clean(double? value)
    {
        return value is not null && double.IsFinite(value.Value) ? value : null;
    }

    private sealed record RawSchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("units")] string Units,
        [property: JsonPropertyName("fields")] string[] Fields);

    private sealed record PublishedSchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("units")] string Units,
        [property: JsonPropertyName("fields")] string[] Fields,
        [property: JsonPropertyName("curves")] string[] Curves,
        [property: JsonPropertyName("curveFields")] string[] CurveFields);

    private sealed record CompactRawRecord(
        [property: JsonPropertyName("t")] DateTimeOffset TimestampUtc,
        [property: JsonPropertyName("c")] string? Channel,
        [property: JsonPropertyName("ap")] double?[] ActivePit,
        [property: JsonPropertyName("aps")] double?[] ActivePitStandardDeviations,
        [property: JsonPropertyName("fl")] double?[] FlowLine,
        [property: JsonPropertyName("fls")] double?[] FlowLineStandardDeviations);

    private sealed record CompactPublishedRecord(
        [property: JsonPropertyName("t")] DateTimeOffset TimestampUtc,
        [property: JsonPropertyName("c")] string? Channel,
        [property: JsonPropertyName("ap")] double?[] ActivePit,
        [property: JsonPropertyName("apc")] double?[][][] ActivePitCurves,
        [property: JsonPropertyName("fl")] double?[] FlowLine,
        [property: JsonPropertyName("flc")] double?[][][] FlowLineCurves);
}

public enum RheoSenseSensorPosition
{
    ActivePit,
    FlowLine
}
