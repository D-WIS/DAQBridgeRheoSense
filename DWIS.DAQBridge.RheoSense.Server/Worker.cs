using DWIS.Client.ReferenceImplementation;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Server;

public class Worker : RheoSenseWorker<ConfigurationForRheoSense>
{
    private readonly RheoSenseInstrumentData _activePitInput = new(
        RheoSenseConstants.ActivePitNamespace,
        RheoSenseConstants.ActivePitNodePrefix);
    private readonly RheoSenseInstrumentData _flowLineInput = new(
        RheoSenseConstants.FlowLineNamespace,
        RheoSenseConstants.FlowLineNodePrefix);
    private readonly RheoSenseInstrumentData _activePitStandardDeviationInput = new(
        RheoSenseConstants.ActivePitNamespace,
        RheoSenseConstants.ActivePitStandardDeviationNodePrefix,
        includeExtrapolationParameters: false);
    private readonly RheoSenseInstrumentData _flowLineStandardDeviationInput = new(
        RheoSenseConstants.FlowLineNamespace,
        RheoSenseConstants.FlowLineStandardDeviationNodePrefix,
        includeExtrapolationParameters: false);
    private readonly ActivePitOutputData _activePitOutput = new();
    private readonly FlowLineOutputData _flowLineOutput = new();
    private bool _opcConnectionNeedsRecovery;
    private bool _blackboardConnectionNeedsRecovery;
    private RheoSenseDataLogWriter? _dataLogWriter;
    private string? _dataLogSettingsKey;
    private readonly SensorProcessingState _activePitState = new();
    private readonly SensorProcessingState _flowLineState = new();
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
        ConfigureOPCUANodes(_activePitInput, Configuration!.ActivePitTags);
        ConfigureOPCUANodes(_flowLineInput, Configuration.FlowLineTags);
        ConfigureOPCUANodes(_activePitStandardDeviationInput, Configuration.ActivePitTags, standardDeviation: true);
        ConfigureOPCUANodes(_flowLineStandardDeviationInput, Configuration.FlowLineTags, standardDeviation: true);

        if (Configuration!.InitializeOPCUAVariables)
        {
            await RegisterToOPCUA(_activePitInput, RheoSenseConstants.ActivePitManifest, RheoSenseConstants.Provider);
            await RegisterToOPCUA(_flowLineInput, RheoSenseConstants.FlowLineManifest, RheoSenseConstants.Provider);
            await RegisterToOPCUA(_activePitStandardDeviationInput, RheoSenseConstants.ActivePitStandardDeviationManifest, RheoSenseConstants.Provider);
            await RegisterToOPCUA(_flowLineStandardDeviationInput, RheoSenseConstants.FlowLineStandardDeviationManifest, RheoSenseConstants.Provider);
        }

        PlaceHolders.Clear();
        await RegisterToBlackboard(_activePitOutput, false);
        await RegisterToBlackboard(_flowLineOutput, false);
        _historyPublisher.Initialize(_DWISClient!);
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
                return;
            }

            try
            {
                await ReadTagSetAsync(_activePitInput, Configuration.ActivePitTags);
                await ReadTagSetAsync(_flowLineInput, Configuration.FlowLineTags);
                await ReadOPCUAStandardDeviationTagSetAsync(_activePitStandardDeviationInput, Configuration.ActivePitTags);
                await ReadOPCUAStandardDeviationTagSetAsync(_flowLineStandardDeviationInput, Configuration.FlowLineTags);

                if (!ConnectionsAreEstablished)
                {
                    return;
                }

                await ProcessSensorAsync(
                    RheoSenseSensorPosition.ActivePit,
                    _activePitInput,
                    _activePitStandardDeviationInput,
                    _activePitOutput,
                    _activePitState,
                    stoppingToken);
                if (!ConnectionsAreEstablished)
                {
                    return;
                }

                await ProcessSensorAsync(
                    RheoSenseSensorPosition.FlowLine,
                    _flowLineInput,
                    _flowLineStandardDeviationInput,
                    _flowLineOutput,
                    _flowLineState,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error in RheoSense bridge exchange.");
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

    private async Task ProcessSensorAsync<TOutput>(
        RheoSenseSensorPosition sensor,
        RheoSenseInstrumentData input,
        RheoSenseInstrumentData standardDeviations,
        TOutput output,
        SensorProcessingState state,
        CancellationToken stoppingToken)
        where TOutput : DWISData, IRheoSenseOutputData
    {
        foreach (RheoSenseDataChannel channel in Enum.GetValues<RheoSenseDataChannel>())
        {
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
        RheoSenseSensorPosition sensor,
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
        RheoSenseSensorPosition sensor,
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
