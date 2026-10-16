namespace DWIS.DAQBridge.RheoSense.WebApp.Services;

public sealed class RheoSenseHistoryStore
{
    private static readonly TimeSpan MaximumHistory = TimeSpan.FromHours(48);
    private readonly object _gate = new();
    private readonly List<YplTrendSample> _samples = new();

    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastSampleUtc { get; private set; }

    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            IsConnected = connected;
        }
    }

    public void Add(YplTrendSample sample)
    {
        lock (_gate)
        {
            _samples.Add(sample);
            LastSampleUtc = sample.TimestampUtc;
            DateTimeOffset oldest = sample.TimestampUtc - MaximumHistory;
            int removeCount = _samples.FindIndex(item => item.TimestampUtc >= oldest);
            if (removeCount < 0)
            {
                _samples.Clear();
                _samples.Add(sample);
            }
            else if (removeCount > 0)
            {
                _samples.RemoveRange(0, removeCount);
            }
        }
    }

    public int Restore(IEnumerable<YplTrendSample> samples, DateTimeOffset nowUtc)
    {
        DateTimeOffset oldest = nowUtc - MaximumHistory;
        YplTrendSample[] restored = samples
            .Where(sample => sample.TimestampUtc >= oldest && sample.TimestampUtc <= nowUtc)
            .GroupBy(sample => sample.TimestampUtc)
            .Select(group => group.Last())
            .OrderBy(sample => sample.TimestampUtc)
            .ToArray();

        lock (_gate)
        {
            _samples.Clear();
            _samples.AddRange(restored);
            LastSampleUtc = restored.Length > 0 ? restored[^1].TimestampUtc : null;
        }

        return restored.Length;
    }

    public IReadOnlyList<YplTrendSample> GetWindow(TimeSpan duration, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            DateTimeOffset start = nowUtc - duration;
            int firstIndex = FindFirstIndexAtOrAfter(start);
            return _samples.Skip(firstIndex).ToArray();
        }
    }

    public IReadOnlyList<YplTrendSample> GetWindowDownsampled(
        TimeSpan duration,
        DateTimeOffset nowUtc,
        int maximumSamples)
    {
        return GetWindowDownsampled(duration, nowUtc, maximumSamples, _ => true);
    }

    public IReadOnlyList<YplTrendSample> GetYplWindowDownsampled(
        TimeSpan duration,
        DateTimeOffset nowUtc,
        int maximumSamples)
    {
        return GetWindowDownsampled(duration, nowUtc, maximumSamples, sample => sample.HasYplValues);
    }

    public IReadOnlyList<YplTrendSample> GetConditionWindowDownsampled(
        TimeSpan duration,
        DateTimeOffset nowUtc,
        int maximumSamples)
    {
        return GetWindowDownsampled(duration, nowUtc, maximumSamples, sample => sample.HasConditionValues);
    }

    public (FlowCurveSnapshot? ActivePit, FlowCurveSnapshot? FlowLine) GetLatestFlowCurves()
    {
        lock (_gate)
        {
            FlowCurveSnapshot? activePit = null;
            FlowCurveSnapshot? flowLine = null;
            for (int index = _samples.Count - 1; index >= 0 && (activePit is null || flowLine is null); index--)
            {
                activePit ??= _samples[index].ActivePitFlowCurves;
                flowLine ??= _samples[index].FlowLineFlowCurves;
            }
            return (activePit, flowLine);
        }
    }

    private IReadOnlyList<YplTrendSample> GetWindowDownsampled(
        TimeSpan duration,
        DateTimeOffset nowUtc,
        int maximumSamples,
        Func<YplTrendSample, bool> include)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSamples, 2);

        lock (_gate)
        {
            DateTimeOffset start = nowUtc - duration;
            int firstIndex = FindFirstIndexAtOrAfter(start);
            List<YplTrendSample> selected = _samples
                .Skip(firstIndex)
                .Where(include)
                .ToList();
            int count = selected.Count;
            if (count <= 0)
            {
                return Array.Empty<YplTrendSample>();
            }

            if (count <= maximumSamples)
            {
                return selected;
            }

            YplTrendSample[] result = new YplTrendSample[maximumSamples];
            double scale = (count - 1d) / (maximumSamples - 1d);
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = selected[(int)Math.Round(index * scale)];
            }

            return result;
        }
    }

    private int FindFirstIndexAtOrAfter(DateTimeOffset timestampUtc)
    {
        int lower = 0;
        int upper = _samples.Count;
        while (lower < upper)
        {
            int middle = lower + ((upper - lower) / 2);
            if (_samples[middle].TimestampUtc < timestampUtc)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower;
    }
}

public sealed record YplValues(double? YieldStress, double? ConsistencyIndex, double? FlowBehaviorIndex)
{
    public static YplValues Empty { get; } = new(null, null, null);
    public bool HasValue => YieldStress is not null || ConsistencyIndex is not null || FlowBehaviorIndex is not null;
}

public sealed record ConditionValues(
    double? Temperature,
    double? Pressure,
    double? CoriolisDensity,
    double? VerticalPipeDensity)
{
    public bool HasValue => Temperature is not null || Pressure is not null || CoriolisDensity is not null || VerticalPipeDensity is not null;
}

public sealed record FlowCurvePoint(double ShearRate, double ShearStress);

public sealed record FlowCurveSnapshot(
    double? MeasuredTemperature,
    double? MeasuredPressure,
    double? ExtrapolatedTemperature,
    double? ExtrapolatedPressure,
    IReadOnlyList<FlowCurvePoint> TrueAtMeasuredConditions,
    IReadOnlyList<FlowCurvePoint> Fann35AtMeasuredConditions,
    IReadOnlyList<FlowCurvePoint> TrueAtExtrapolatedConditions,
    IReadOnlyList<FlowCurvePoint> Fann35AtExtrapolatedConditions);

public sealed record YplTrendSample(
    DateTimeOffset TimestampUtc,
    YplValues ActivePitMeasured,
    YplValues ActivePitExtrapolated,
    YplValues FlowLineMeasured,
    YplValues FlowLineExtrapolated,
    ConditionValues? ActivePitConditions = null,
    ConditionValues? FlowLineConditions = null,
    FlowCurveSnapshot? ActivePitFlowCurves = null,
    FlowCurveSnapshot? FlowLineFlowCurves = null)
{
    public bool HasYplValues =>
        ActivePitMeasured.HasValue || ActivePitExtrapolated.HasValue ||
        FlowLineMeasured.HasValue || FlowLineExtrapolated.HasValue;

    public bool HasConditionValues =>
        ActivePitConditions?.HasValue == true || FlowLineConditions?.HasValue == true;
}

public sealed record TrendPoint(DateTimeOffset TimestampUtc, double Value);
public sealed record TrendSeries(string Name, string Color, IReadOnlyList<TrendPoint> Points);
public sealed record FlowCurveSeries(
    string Name,
    string Color,
    bool Dashed,
    IReadOnlyList<FlowCurvePoint> Points);
