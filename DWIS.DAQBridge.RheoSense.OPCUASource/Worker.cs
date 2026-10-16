using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using OSDC.DotnetLibraries.General.Common;

namespace DWIS.DAQBridge.RheoSense.OPCUASource;

public class Worker : RheoSenseWorker<ConfigurationForRheoSense>
{
    private readonly RheoSenseInstrumentData _activePit = new(
        RheoSenseConstants.ActivePitNamespace,
        RheoSenseConstants.ActivePitNodePrefix);
    private readonly RheoSenseInstrumentData _flowLine = new(
        RheoSenseConstants.FlowLineNamespace,
        RheoSenseConstants.FlowLineNodePrefix);
    private readonly RheoSenseInstrumentData _activePitStandardDeviation = new(
        RheoSenseConstants.ActivePitNamespace,
        RheoSenseConstants.ActivePitStandardDeviationNodePrefix,
        includeExtrapolationParameters: false);
    private readonly RheoSenseInstrumentData _flowLineStandardDeviation = new(
        RheoSenseConstants.FlowLineNamespace,
        RheoSenseConstants.FlowLineStandardDeviationNodePrefix,
        includeExtrapolationParameters: false);

    public Worker(
        ILogger<IDWISWorker<ConfigurationForRheoSense>> logger,
        ILogger<DWISClientOPCF>? clientLogger)
        : base(logger, clientLogger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectToOPCUA();
        if (Configuration is null || _OPCUAClient is null || !_OPCUAClient.Connected)
        {
            Logger?.LogError("Could not connect to the RheoSense OPC-UA endpoint.");
            return;
        }

        ConfigureOPCUANodes(_activePit, Configuration.ActivePitTags);
        ConfigureOPCUANodes(_flowLine, Configuration.FlowLineTags);
        ConfigureOPCUANodes(_activePitStandardDeviation, Configuration.ActivePitTags, standardDeviation: true);
        ConfigureOPCUANodes(_flowLineStandardDeviation, Configuration.FlowLineTags, standardDeviation: true);

        await RegisterToOPCUA(_activePit, RheoSenseConstants.ActivePitManifest, RheoSenseConstants.Provider);
        await RegisterToOPCUA(_flowLine, RheoSenseConstants.FlowLineManifest, RheoSenseConstants.Provider);
        await RegisterToOPCUA(_activePitStandardDeviation, RheoSenseConstants.ActivePitStandardDeviationManifest, RheoSenseConstants.Provider);
        await RegisterToOPCUA(_flowLineStandardDeviation, RheoSenseConstants.FlowLineStandardDeviationManifest, RheoSenseConstants.Provider);

        Random activePitRandom = new(Configuration.SimulationSeed);
        Random flowLineRandom = new(unchecked(Configuration.SimulationSeed * 397) ^ 1);
        DateTime started = DateTime.UtcNow;
        TimeSpan conditionPeriod = Configuration.SimulationConditionGenerationPeriod > TimeSpan.Zero
            ? Configuration.SimulationConditionGenerationPeriod
            : TimeSpan.FromSeconds(10);
        TimeSpan rheologyGenerationPeriod = Configuration.SimulationDataGenerationPeriod > TimeSpan.Zero
            ? Configuration.SimulationDataGenerationPeriod
            : TimeSpan.FromMinutes(30);
        TimeSpan rheologyPublishPeriod = Configuration.SimulationOPCUAPublishPeriod > TimeSpan.Zero
            ? Configuration.SimulationOPCUAPublishPeriod
            : TimeSpan.FromMinutes(5);
        DateTime nextActivePitRheologyGeneration = started + rheologyGenerationPeriod;
        DateTime nextFlowLineRheologyGeneration = started + rheologyGenerationPeriod;
        DateTime nextRheologyPublish = started + rheologyPublishPeriod;

        FillConditions(_activePit, 0, activePitRandom, 0);
        FillConditions(_flowLine, 0, flowLineRandom, 1);
        FillRheology(_activePit, 0, 0);
        FillRheology(_flowLine, 0, 1);
        FillStandardDeviations(_activePitStandardDeviation, 0);
        FillStandardDeviations(_flowLineStandardDeviation, 1);
        await PublishCurrentValuesAsync(stoppingToken);

        using PeriodicTimer timer = new(conditionPeriod);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - started).TotalSeconds;
            FillConditions(_activePit, elapsed, activePitRandom, 0);
            FillConditions(_flowLine, elapsed, flowLineRandom, 1);
            FillConditionStandardDeviations(_activePitStandardDeviation, 0);
            FillConditionStandardDeviations(_flowLineStandardDeviation, 1);
            await PublishChannelValuesAsync(RheoSenseDataChannel.DensityPressureTemperature, stoppingToken);

            bool rheologyChanged = false;
            if (now >= nextActivePitRheologyGeneration)
            {
                FillRheology(_activePit, elapsed, 0);
                nextActivePitRheologyGeneration = AdvanceSchedule(
                    nextActivePitRheologyGeneration,
                    rheologyGenerationPeriod,
                    now);
                rheologyChanged = true;
            }

            if (now >= nextFlowLineRheologyGeneration)
            {
                FillRheology(_flowLine, elapsed, 1);
                nextFlowLineRheologyGeneration = AdvanceSchedule(
                    nextFlowLineRheologyGeneration,
                    rheologyGenerationPeriod,
                    now);
                rheologyChanged = true;
            }

            if (rheologyChanged || now >= nextRheologyPublish)
            {
                await PublishChannelValuesAsync(RheoSenseDataChannel.Rheology, stoppingToken);
                nextRheologyPublish = AdvanceSchedule(nextRheologyPublish, rheologyPublishPeriod, now);
            }

            Logger?.LogInformation(
                "Published RheoSense condition values: active pit={ActivePitPressure:F0} Pa, return flow={FlowLinePressure:F0} Pa",
                _activePit.MeasuredPressure?.ScalarValue,
                _flowLine.MeasuredPressure?.ScalarValue);
            ConfigurationUpdater<ConfigurationForRheoSense>.Instance.UpdateConfiguration(this);
        }
    }

    private async Task PublishCurrentValuesAsync(CancellationToken stoppingToken)
    {
        await PublishOPCUATagSetAsync(_activePit, Configuration!.ActivePitTags, stoppingToken);
        await PublishOPCUATagSetAsync(_flowLine, Configuration.FlowLineTags, stoppingToken);
        await PublishOPCUAStandardDeviationTagSetAsync(_activePitStandardDeviation, Configuration.ActivePitTags, stoppingToken);
        await PublishOPCUAStandardDeviationTagSetAsync(_flowLineStandardDeviation, Configuration.FlowLineTags, stoppingToken);
    }

    private async Task PublishChannelValuesAsync(
        RheoSenseDataChannel channel,
        CancellationToken stoppingToken)
    {
        await PublishOPCUATagSetChannelAsync(_activePit, Configuration!.ActivePitTags, channel, stoppingToken);
        await PublishOPCUATagSetChannelAsync(_flowLine, Configuration.FlowLineTags, channel, stoppingToken);
        await PublishOPCUAStandardDeviationTagSetChannelAsync(
            _activePitStandardDeviation,
            Configuration.ActivePitTags,
            channel,
            stoppingToken);
        await PublishOPCUAStandardDeviationTagSetChannelAsync(
            _flowLineStandardDeviation,
            Configuration.FlowLineTags,
            channel,
            stoppingToken);
    }

    private static DateTime AdvanceSchedule(DateTime next, TimeSpan period, DateTime now)
    {
        do
        {
            next += period;
        }
        while (next <= now);

        return next;
    }

    private static void FillStandardDeviations(RheoSenseInstrumentData data, int sensorIndex)
    {
        foreach (var property in data.OPCUANodes.Value.Keys)
        {
            property.SetValue(data, Scalar(0.0));
        }

        FillConditionStandardDeviations(data, sensorIndex);
    }

    private static void FillConditionStandardDeviations(RheoSenseInstrumentData data, int sensorIndex)
    {
        data.MeasuredTemperature = Scalar(0.05 + sensorIndex * 0.01);
        data.MeasuredPressure = new ScalarDrillingProperty();
        data.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure = Scalar(0.2);
        data.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure = Scalar(0.3);
    }

    private static void FillConditions(RheoSenseInstrumentData data, double elapsed, Random random, int sensorIndex)
    {
        double phase = elapsed / 45.0 + sensorIndex * 0.8;
        double noise = (random.NextDouble() - 0.5) * 0.4;
        double measuredTemperature = 298.15 + sensorIndex * 3.0 + 1.5 * Math.Sin(phase) + noise;
        double measuredPressure = 1.8e5 + sensorIndex * 0.7e5 + 1.5e4 * Math.Sin(phase / 2.0);
        double density = 1180 + sensorIndex * 8 + 3 * Math.Sin(phase / 3.0);

        data.MeasuredTemperature = Scalar(measuredTemperature);
        data.MeasuredPressure = Scalar(measuredPressure);
        data.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure = Scalar(density);
        data.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure = Scalar(density + 1.5);
    }

    private static void FillRheology(RheoSenseInstrumentData data, double elapsed, int sensorIndex)
    {
        double phase = elapsed / 45.0 + sensorIndex * 0.8;
        double yieldStress = 4.5 + sensorIndex * 0.8 + 0.2 * Math.Sin(phase);
        double consistency = 0.65 + sensorIndex * 0.08;
        double behavior = 0.72 + 0.02 * Math.Sin(phase / 2.0);

        SetMeasuredShearStresses(data, yieldStress, consistency, behavior);
        data.PlasticViscosityAtMeasuredTemperatureAndPressure = Scalar(0.031 + sensorIndex * 0.002);
        data.YieldPointAtMeasuredTemperatureAndPressure = Scalar(7.5 + sensorIndex);
        data.YPLYieldStressAtMeasuredTemperatureAndPressure = Scalar(yieldStress);
        data.YPLConsistencyIndexAtMeasuredTemperatureAndPressure = Scalar(consistency);
        data.YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure = Scalar(behavior);

        data.ExtrapolatedTemperature = Scalar(333.15);
        data.ExtrapolatedPressure = Scalar(5.0e6);
        SetExtrapolatedShearStresses(data, yieldStress * 0.92, consistency * 0.88, behavior);
        data.PlasticViscosityAtExtraplatedTemperatureAndPressure = Scalar((0.031 + sensorIndex * 0.002) * 0.88);
        data.YieldPointAtExtraplatedTemperatureAndPressure = Scalar((7.5 + sensorIndex) * 0.92);
        data.YPLYieldStressAtExtraplatedTemperatureAndPressure = Scalar(yieldStress * 0.92);
        data.YPLConsistencyIndexAtExtraplatedTemperatureAndPressure = Scalar(consistency * 0.88);
        data.YPLFlowBehaviorIndexAtExtraplatedTemperatureAndPressure = Scalar(behavior);
    }

    private static void SetMeasuredShearStresses(RheoSenseInstrumentData data, double yieldStress, double consistency, double behavior)
    {
        data.ShearStressAt3RPMAtMeasuredTemperatureAndPressure = Stress(3, yieldStress, consistency, behavior);
        data.ShearStressAt6RPMAtMeasuredTemperatureAndPressure = Stress(6, yieldStress, consistency, behavior);
        data.ShearStressAt10RPMAtMeasuredTemperatureAndPressure = Stress(10, yieldStress, consistency, behavior);
        data.ShearStressAt20RPMAtMeasuredTemperatureAndPressure = Stress(20, yieldStress, consistency, behavior);
        data.ShearStressAt30RPMAtMeasuredTemperatureAndPressure = Stress(30, yieldStress, consistency, behavior);
        data.ShearStressAt60RPMAtMeasuredTemperatureAndPressure = Stress(60, yieldStress, consistency, behavior);
        data.ShearStressAt100RPMAtMeasuredTemperatureAndPressure = Stress(100, yieldStress, consistency, behavior);
        data.ShearStressAt200RPMAtMeasuredTemperatureAndPressure = Stress(200, yieldStress, consistency, behavior);
        data.ShearStressAt300RPMAtMeasuredTemperatureAndPressure = Stress(300, yieldStress, consistency, behavior);
        data.ShearStressAt600RPMAtMeasuredTemperatureAndPressure = Stress(600, yieldStress, consistency, behavior);
    }

    private static void SetExtrapolatedShearStresses(RheoSenseInstrumentData data, double yieldStress, double consistency, double behavior)
    {
        data.ShearStressAt3RPMAtExtraplatedTemperatureAndPressure = Stress(3, yieldStress, consistency, behavior);
        data.ShearStressAt6RPMAtExtraplatedTemperatureAndPressure = Stress(6, yieldStress, consistency, behavior);
        data.ShearStressAt10RPMAtExtraplatedTemperatureAndPressure = Stress(10, yieldStress, consistency, behavior);
        data.ShearStressAt20RPMAtExtraplatedTemperatureAndPressure = Stress(20, yieldStress, consistency, behavior);
        data.ShearStressAt30RPMAtExtraplatedTemperatureAndPressure = Stress(30, yieldStress, consistency, behavior);
        data.ShearStressAt60RPMAtExtraplatedTemperatureAndPressure = Stress(60, yieldStress, consistency, behavior);
        data.ShearStressAt100RPMAtExtraplatedTemperatureAndPressure = Stress(100, yieldStress, consistency, behavior);
        data.ShearStressAt200RPMAtExtraplatedTemperatureAndPressure = Stress(200, yieldStress, consistency, behavior);
        data.ShearStressAt300RPMAtExtraplatedTemperatureAndPressure = Stress(300, yieldStress, consistency, behavior);
        data.ShearStressAt600RPMAtExtraplatedTemperatureAndPressure = Stress(600, yieldStress, consistency, behavior);
    }

    private static ScalarDrillingProperty Stress(double rpm, double yieldStress, double consistency, double behavior)
    {
        double shearRate = rpm * Constants.R1B1Model35RotationalSpeedToShearRateConversion;
        return Scalar(yieldStress + consistency * Math.Pow(shearRate, behavior));
    }

    private static ScalarDrillingProperty Scalar(double value)
    {
        return new ScalarDrillingProperty { ScalarValue = value };
    }
}
