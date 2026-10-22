using DWIS.Client.ReferenceImplementation;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Server;

public class Worker : RheoSenseWorker<ConfigurationForRheoSense>
{
    private readonly List<SourceRuntime> sources = new();
    private sealed class SourceRuntime(RheometerInstallation installation, string contractsPublicBaseUrl)
    {
        public RheometerSource Source { get; } = installation.Source;
        public RheometerInstallation Installation { get; } = installation;
        public RheoSenseOPCUATagSet Tags { get; } = installation.Tags;
        public RheoSenseInstrumentData Input { get; } = new(installation.InputNamespace, installation.InputPrefix);
        public RheoSenseInstrumentData Sigma { get; } = new(installation.InputNamespace, installation.InputPrefix + "Sigma_", false);
        public RheometerOutputData Output { get; } = RheometryContract.ApplyPublicBaseUrl(
            installation.ExtrapolationId is null
                ? new RheometerOutputData(installation.Source, installation.Tags.DisabledBinghamOutputs())
                : new RheometerExtrapolationOutput(installation.Source, installation.ExtrapolationId),
            contractsPublicBaseUrl);
        public SensorProcessingState State { get; } = new();
        public RheoSenseStateData States { get; } = new(installation.Source, installation.Tags);
        public bool Inactive { get; set; }
        public Dictionary<System.Reflection.PropertyInfo, DWIS.API.DTO.QueryResult> Targets { get; } = new();
    }
    private readonly Dictionary<string, RunningHeartbeatMonitor> heartbeats = new();
    private bool _opcConnectionNeedsRecovery;
    private bool _blackboardConnectionNeedsRecovery;
    private RheoSenseDataLogWriter? _dataLogWriter;
    private string? _dataLogSettingsKey;
    private readonly RheoSenseBlackboardHistoryPublisher _historyPublisher = new();

    public Worker(
        ILogger<IDWISWorker<ConfigurationForRheoSense>> logger,
        ILogger<DWISClientOPCF>? clientLogger)
        : base(logger, clientLogger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);

            if (!TryEstablishConnections())
            {
                await WaitBeforeReconnectAsync(stoppingToken);
                continue;
            }

            try
            {
                await InitializeConnectedSessionAsync();
                if (!ConnectionsAreEstablished)
                {
                    Logger?.LogWarning("A RheoSense bridge connection was lost during session initialization. Returning to connection retry mode.");
                    await WaitBeforeReconnectAsync(stoppingToken);
                    continue;
                }

                Logger?.LogInformation("Both the RheoSense OPC-UA endpoint and DWIS Blackboard are connected. Starting the bridge exchange loop.");
                await RunBridgeUntilDisconnectedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "The RheoSense bridge session failed. Returning to connection retry mode.");
                _opcConnectionNeedsRecovery = true;
                _blackboardConnectionNeedsRecovery = true;
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                Logger?.LogWarning("The RheoSense bridge exchange session ended. Returning to connection retry mode.");
                await WaitBeforeReconnectAsync(stoppingToken);
            }
        }
    }

    private bool TryEstablishConnections()
    {
        if (Configuration is null)
        {
            Logger?.LogWarning("RheoSense configuration is not available. Waiting before trying again.");
            return false;
        }

        bool opcConnected = TryConnect(
            () => _OPCUAClient,
            ConnectToOPCUA,
            "RheoSense OPC-UA endpoint",
            _opcConnectionNeedsRecovery);
        bool blackboardConnected = TryConnect(
            () => _DWISClient,
            ConnectToBlackboard,
            "DWIS Blackboard",
            _blackboardConnectionNeedsRecovery);

        _opcConnectionNeedsRecovery = !opcConnected;
        _blackboardConnectionNeedsRecovery = !blackboardConnected;

        if (!opcConnected || !blackboardConnected)
        {
            Logger?.LogWarning(
                "Waiting for bridge connections. RheoSense OPC-UA connected={OPCUAConnected}; DWIS Blackboard connected={BlackboardConnected}.",
                opcConnected,
                blackboardConnected);
        }

        return opcConnected && blackboardConnected;
    }

    private bool TryConnect(
        Func<IOPCUADWISClient?> getClient,
        Action createClient,
        string connectionName,
        bool forceReconnect)
    {
        IOPCUADWISClient? client = getClient();
        if (forceReconnect)
        {
            try
            {
                createClient();
                return getClient()?.Connected == true;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Could not replace the failed {ConnectionName} session.", connectionName);
                return false;
            }
        }

        if (client?.Connected == true && !forceReconnect)
        {
            return true;
        }

        try
        {
            if (client is null)
            {
                createClient();
            }
            else
            {
                return client.TryReconnect() && client.Connected;
            }

            return getClient()?.Connected == true;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Could not connect to the {ConnectionName}.", connectionName);
            return false;
        }
    }

    private bool ConnectionsAreEstablished =>
        _OPCUAClient?.Connected == true && _DWISClient?.Connected == true;

    private async Task InitializeConnectedSessionAsync()
    {
        Configuration!.ValidateSources();
        sources.Clear();
        heartbeats.Clear();
        foreach (var installation in Configuration!.GetInstallations())
        {
            var runtime = new SourceRuntime(installation, Configuration.ContractsPublicBaseUrl);
            await RheoSenseUnitConversions.ConfigureAsync(Configuration, runtime.Tags);
            ConfigureOPCUANodes(runtime.Input, runtime.Tags);
            ConfigureOPCUANodes(runtime.Sigma, runtime.Tags, standardDeviation: true);
            if (Configuration.InitializeOPCUAVariables)
            {
                await RegisterToOPCUA(runtime.Input, installation.InputManifestName, RheoSenseConstants.Provider);
                await RegisterToOPCUA(runtime.Sigma, installation.InputManifestName, RheoSenseConstants.Provider);
            }
            PlaceHolders = runtime.Targets;
            await RegisterToBlackboard(runtime.Output, false);
            if (_DWISClient!.Inject(runtime.Output.CreatePhysicalLocationManifest())?.Success != true)
                throw new InvalidOperationException("Could not publish the shared physical-location identity.");
            sources.Add(runtime);
            if (installation.ExtrapolationId is null) heartbeats.Add(runtime.Source.Id, new());
            PlaceHolders = runtime.Targets;
            await InvalidateBlackboardAsync(runtime.Output, CancellationToken.None);
            runtime.Inactive = true;
        }
        if (sources.Count > 0) _historyPublisher.Initialize(_DWISClient!, sources.Select(s => s.Installation.HistoryId));
    }

    private async Task RunBridgeUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        TimeSpan opcPeriod = Configuration!.OPCUALoopDuration > TimeSpan.Zero
            ? Configuration.OPCUALoopDuration
            : TimeSpan.FromSeconds(1);
        using PeriodicTimer timer = new(opcPeriod);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!ConnectionsAreEstablished)
            {
                await InvalidateSourcesAsync(stoppingToken);
                return;
            }

            try
            {
                foreach (var runtime in sources)
                {
                    var primary = sources.First(s => s.Source.Id == runtime.Source.Id && s.Installation.ExtrapolationId is null);
                    if (runtime == primary) await ReadStatesAsync(primary.States);
                    bool online = heartbeats[runtime.Source.Id].Observe(primary.States.RunningHeartbeat?.ScalarValue, DateTimeOffset.UtcNow, Configuration!.HeartbeatTimeout);
                    PlaceHolders = runtime.Targets;
                    if (!online)
                    {
                        if (!runtime.Inactive) await InvalidateBlackboardAsync(runtime.Output, stoppingToken);
                        runtime.Inactive = true;
                        continue;
                    }
                    if (runtime.Inactive)
                    {
                        runtime.State.ResetPublished();
                        runtime.Inactive = false;
                        Logger?.LogInformation("Heartbeat resumed for {Source}; restoring all output channels.", runtime.Installation.HistoryId);
                    }
                    await ReadTagSetAsync(runtime.Input, runtime.Tags);
                    await ReadOPCUAStandardDeviationTagSetAsync(runtime.Sigma, runtime.Tags);
                    if (!ConnectionsAreEstablished) return;
                    PlaceHolders = runtime.Targets;
                    await ProcessSensorAsync(runtime.Installation.HistoryId, runtime.Input, runtime.Sigma, runtime.Output, runtime.State, stoppingToken);
                    if (!ConnectionsAreEstablished) return;
                }

            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error in RheoSense bridge exchange.");
                await InvalidateSourcesAsync(stoppingToken);
                // The OPC Foundation client can temporarily retain Connected=true
                // after an operation has failed with BadNotConnected. Force both
                // transports through their reconnect path instead of trusting that
                // potentially stale flag.
                _opcConnectionNeedsRecovery = true;
                _blackboardConnectionNeedsRecovery = true;
                return;
            }

            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);
        }
    }

    private async Task InvalidateSourcesAsync(CancellationToken token)
    {
        if (_DWISClient?.Connected != true) return;
        foreach (var runtime in sources)
        {
            PlaceHolders = runtime.Targets;
            try { await InvalidateBlackboardAsync(runtime.Output, token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Logger?.LogWarning(ex, "Could not invalidate outputs for {Source}", runtime.Source.Id); }
            runtime.Inactive = true;
        }
    }

    private async Task ProcessSensorAsync<TOutput>(
        string sensor,
        RheoSenseInstrumentData input,
        RheoSenseInstrumentData standardDeviations,
        TOutput output,
        SensorProcessingState state,
        CancellationToken stoppingToken)
        where TOutput : DWISData, IRheoSenseOutputData
    {
        foreach (RheoSenseDataChannel channel in Enum.GetValues<RheoSenseDataChannel>())
        {
            if (output is RheometerExtrapolationOutput && channel == RheoSenseDataChannel.DensityPressureTemperature) continue;
            RheoSenseInputSnapshot snapshot = RheoSenseInputSnapshot.Capture(input, standardDeviations, channel);
            if (snapshot.Equals(state.GetPublished(channel)))
            {
                continue;
            }

            if (!snapshot.Equals(state.GetLogged(channel)))
            {
                DateTimeOffset rawTimestampUtc = DateTimeOffset.UtcNow;
                await LogRawDataAsync(rawTimestampUtc, sensor, input, standardDeviations, channel);
                _historyPublisher.Append(
                    sensor,
                    RheoSenseHistoryDataKind.Raw,
                    rawTimestampUtc,
                    RheoSenseDataLogWriter.CreateRawSensorEntry(
                        rawTimestampUtc,
                        sensor,
                        input,
                        standardDeviations,
                        channel));
                state.SetLogged(channel, snapshot);
            }

            RheoSenseBridgeMapper.Map(input, output, standardDeviations, channel);
            await PublishBlackboardChannelAsync(output, channel, stoppingToken);
            if (!ConnectionsAreEstablished)
            {
                return;
            }

            DateTimeOffset publishedTimestampUtc = DateTimeOffset.UtcNow;
            await LogPublishedDataAsync(publishedTimestampUtc, sensor, output, channel);
            _historyPublisher.Append(
                sensor,
                RheoSenseHistoryDataKind.Published,
                publishedTimestampUtc,
                RheoSenseDataLogWriter.CreatePublishedSensorEntry(
                    publishedTimestampUtc,
                    sensor,
                    output,
                    channel));
            state.SetPublished(channel, snapshot);

            Logger?.LogInformation(
                "Published changed {Sensor} RheoSense {DataChannel} data.",
                sensor,
                channel);
        }
    }

    private async Task WaitBeforeReconnectAsync(CancellationToken stoppingToken)
    {
        TimeSpan retryInterval = Configuration?.ConnectionRetryInterval > TimeSpan.Zero
            ? Configuration.ConnectionRetryInterval
            : TimeSpan.FromSeconds(5);

        try
        {
            await Task.Delay(retryInterval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown while waiting for an endpoint.
        }
    }

    private async Task LogRawDataAsync(
        DateTimeOffset timestampUtc,
        string sensor,
        RheoSenseInstrumentData values,
        RheoSenseInstrumentData standardDeviations,
        RheoSenseDataChannel channel)
    {
        RheoSenseDataLogWriter? writer = GetDataLogWriter();
        if (writer is null)
        {
            return;
        }

        try
        {
            await writer.AppendRawSensorAsync(timestampUtc, sensor, values, standardDeviations, channel);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Could not append the compact RheoSense OPC-UA data log.");
        }
    }

    private async Task LogPublishedDataAsync(
        DateTimeOffset timestampUtc,
        string sensor,
        IRheoSenseOutputData output,
        RheoSenseDataChannel channel)
    {
        RheoSenseDataLogWriter? writer = GetDataLogWriter();
        if (writer is null)
        {
            return;
        }

        try
        {
            await writer.AppendPublishedSensorAsync(timestampUtc, sensor, output, channel);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Could not append the compact RheoSense Blackboard publication log.");
        }
    }

    private RheoSenseDataLogWriter? GetDataLogWriter()
    {
        if (Configuration is null || !Configuration.EnableDataLogging)
        {
            return null;
        }

        string settingsKey = string.Join(
            '|',
            Configuration.DataLogDirectory,
            Configuration.DataLogSegmentDuration.Ticks,
            Configuration.MaximumDataLogStorageBytes);
        if (_dataLogWriter is null || !string.Equals(_dataLogSettingsKey, settingsKey, StringComparison.Ordinal))
        {
            _dataLogWriter = new RheoSenseDataLogWriter(
                Configuration.DataLogDirectory,
                Configuration.DataLogSegmentDuration,
                Configuration.MaximumDataLogStorageBytes);
            _dataLogSettingsKey = settingsKey;
        }

        return _dataLogWriter;
    }

    private Task ReadTagSetAsync(RheoSenseInstrumentData data, RheoSenseOPCUATagSet tags)
    {
        return ReadOPCUATagSetAsync(data, tags);
    }

    private sealed class SensorProcessingState
    {
        public void ResetPublished() { _publishedDensityPressureTemperature = null; _publishedRheology = null; }
        private RheoSenseInputSnapshot? _publishedDensityPressureTemperature;
        private RheoSenseInputSnapshot? _publishedRheology;
        private RheoSenseInputSnapshot? _loggedDensityPressureTemperature;
        private RheoSenseInputSnapshot? _loggedRheology;

        public RheoSenseInputSnapshot? GetPublished(RheoSenseDataChannel channel) =>
            channel == RheoSenseDataChannel.DensityPressureTemperature
                ? _publishedDensityPressureTemperature
                : _publishedRheology;

        public RheoSenseInputSnapshot? GetLogged(RheoSenseDataChannel channel) =>
            channel == RheoSenseDataChannel.DensityPressureTemperature
                ? _loggedDensityPressureTemperature
                : _loggedRheology;

        public void SetPublished(RheoSenseDataChannel channel, RheoSenseInputSnapshot snapshot)
        {
            if (channel == RheoSenseDataChannel.DensityPressureTemperature)
            {
                _publishedDensityPressureTemperature = snapshot;
            }
            else
            {
                _publishedRheology = snapshot;
            }
        }

        public void SetLogged(RheoSenseDataChannel channel, RheoSenseInputSnapshot snapshot)
        {
            if (channel == RheoSenseDataChannel.DensityPressureTemperature)
            {
                _loggedDensityPressureTemperature = snapshot;
            }
            else
            {
                _loggedRheology = snapshot;
            }
        }
    }
}
