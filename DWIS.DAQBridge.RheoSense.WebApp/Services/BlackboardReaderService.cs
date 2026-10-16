using System.Text;
using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.WebApp.Services;

public sealed class BlackboardReaderService : RheoSenseWorker<ConfigurationForRheoSense>
{
    private static readonly TimeSpan HistoryRefreshPeriod = TimeSpan.FromSeconds(5);
    private readonly RheoSenseHistoryStore _history;
    private readonly Dictionary<string, DWISNodeID> _historyNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _lastPayloads = new(StringComparer.Ordinal);

    public BlackboardReaderService(
        RheoSenseHistoryStore history,
        ILogger<IDWISWorker<ConfigurationForRheoSense>> logger,
        ILogger<DWISClientOPCF>? clientLogger)
        : base(logger, clientLogger)
    {
        _history = history;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);
            try
            {
                ConnectToBlackboard();
                if (_DWISClient?.Connected != true)
                {
                    _history.SetConnected(false);
                    Logger?.LogWarning("RheoSense WebApp is waiting for the DWIS Blackboard.");
                    await WaitBeforeRetryAsync(stoppingToken);
                    continue;
                }

                RegisterHistoryVariables();
                _history.SetConnected(true);
                Logger?.LogInformation("RheoSense WebApp is reading its 48-hour YPL history from Blackboard OPC-UA variables.");

                await RefreshHistoryAsync(stoppingToken);
                await ReadLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "RheoSense WebApp lost its Blackboard history session and will reconnect.");
            }

            _history.SetConnected(false);
            await WaitBeforeRetryAsync(stoppingToken);
        }
    }

    private void RegisterHistoryVariables()
    {
        ManifestInjectionResult? result = _DWISClient!.Inject(RheoSenseBlackboardHistory.CreateManifest());
        if (result?.Success != true || result.ProvidedVariables is null)
        {
            throw new InvalidOperationException("The RheoSense Blackboard history variables could not be resolved.");
        }

        HashSet<string> publishedIds = RheoSenseBlackboardHistory.PublishedNodeIds.ToHashSet(StringComparer.Ordinal);
        _historyNodes.Clear();
        foreach (InjectionMapping mapping in result.ProvidedVariables.Where(mapping =>
                     publishedIds.Contains(mapping.ManifestItemID) && mapping.InjectedID is not null))
        {
            _historyNodes[mapping.ManifestItemID] = mapping.InjectedID;
        }

        if (_historyNodes.Count != publishedIds.Count)
        {
            throw new InvalidOperationException(
                $"Resolved {_historyNodes.Count} of {publishedIds.Count} RheoSense published-history variables.");
        }

        _lastPayloads.Clear();
    }

    private async Task ReadLoopAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(HistoryRefreshPeriod);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_DWISClient?.Connected != true)
            {
                return;
            }

            await RefreshHistoryAsync(stoppingToken);
            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);
        }
    }

    private async Task RefreshHistoryAsync(CancellationToken stoppingToken)
    {
        Dictionary<string, string?> payloads = new(StringComparer.Ordinal);
        foreach ((string nodeId, DWISNodeID injectedId) in _historyNodes)
        {
            stoppingToken.ThrowIfCancellationRequested();
            string? payload = null;
            if (_DWISClient!.ReadAnyVariables(
                    out object[] values,
                    new[] { (injectedId.NameSpaceIndex, injectedId.ID) }) &&
                values.Length > 0)
            {
                payload = values[0] as string;
            }
            payloads[nodeId] = payload;
        }

        if (PayloadsEqual(payloads, _lastPayloads))
        {
            return;
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset cutoffUtc = nowUtc.AddHours(-48);
        List<YplTrendSample> samples = new();
        foreach (string? payload in payloads.Values)
        {
            if (!RheoSenseBlackboardHistory.TryDeserialize(payload, out RheoSenseHistoryBucketPayload? bucket) ||
                bucket is null || bucket.BucketStartUtc + RheoSenseBlackboardHistory.BucketDuration < cutoffUtc)
            {
                continue;
            }

            await using MemoryStream stream = new(Encoding.UTF8.GetBytes(bucket.JsonLines));
            samples.AddRange(await RheoSenseHistoryLogReader.ReadJsonLinesAsync(
                stream,
                cutoffUtc,
                nowUtc,
                stoppingToken));
        }

        _history.Restore(samples, nowUtc);
        _lastPayloads.Clear();
        foreach ((string nodeId, string? payload) in payloads)
        {
            _lastPayloads[nodeId] = payload;
        }
    }

    private static bool PayloadsEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        return left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out string? value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private async Task WaitBeforeRetryAsync(CancellationToken stoppingToken)
    {
        TimeSpan delay = Configuration?.ConnectionRetryInterval > TimeSpan.Zero
            ? Configuration.ConnectionRetryInterval
            : TimeSpan.FromSeconds(5);
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
