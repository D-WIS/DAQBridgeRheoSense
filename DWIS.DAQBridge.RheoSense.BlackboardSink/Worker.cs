using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.BlackboardSink;

public class Worker : RheoSenseWorker<ConfigurationForRheoSense>
{
    private readonly ActivePitOutputData _activePit = new();
    private readonly FlowLineOutputData _flowLine = new();

    public Worker(
        ILogger<IDWISWorker<ConfigurationForRheoSense>> logger,
        ILogger<DWISClientOPCF>? clientLogger)
        : base(logger, clientLogger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectToBlackboard();
        if (_DWISClient is null || !_DWISClient.Connected)
        {
            Logger?.LogError("Could not connect to the DWIS Blackboard.");
            return;
        }

        await RegisterQueries(_activePit);
        await RegisterQueries(_flowLine);

        TimeSpan period = LoopSpan > TimeSpan.Zero ? LoopSpan : TimeSpan.FromSeconds(1);
        using PeriodicTimer timer = new(period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReadBlackboardAsync(_activePit, stoppingToken);
            await ReadBlackboardAsync(_flowLine, stoppingToken);
            Logger?.LogInformation(
                "DWIS RheoSense data: active-pit pressure={ActivePitPressure:F0} Pa, return-flow pressure={FlowLinePressure:F0} Pa, active-pit samples={ActivePitSamples}, return-flow samples={FlowLineSamples}",
                _activePit.MeasuredPressure?.Mean,
                _flowLine.MeasuredPressure?.Mean,
                _activePit.FlowCurveAtMeasuredTemperatureAndPressure?.Measurements?.Count ?? 0,
                _flowLine.FlowCurveAtMeasuredTemperatureAndPressure?.Measurements?.Count ?? 0);
            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);
        }
    }
}
