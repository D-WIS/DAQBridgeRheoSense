namespace DWIS.DAQBridge.RheoSense.WebApp.Services;

public sealed class RheoSenseHistoryInitializationService : IHostedService
{
    private readonly RheoSenseHistoryLogReader _reader;
    private readonly RheoSenseHistoryStore _history;
    private readonly ILogger<RheoSenseHistoryInitializationService> _logger;

    public RheoSenseHistoryInitializationService(
        RheoSenseHistoryLogReader reader,
        RheoSenseHistoryStore history,
        ILogger<RheoSenseHistoryInitializationService> logger)
    {
        _reader = reader;
        _history = history;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        IReadOnlyList<YplTrendSample> samples = await _reader.ReadRecentAsync(nowUtc, cancellationToken);
        int count = _history.Restore(samples, nowUtc);
        _logger.LogInformation("Restored {HistorySampleCount} RheoSense YPL history samples from compact logs.", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
