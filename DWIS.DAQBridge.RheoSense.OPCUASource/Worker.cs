using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using OSDC.DotnetLibraries.General.Common;
namespace DWIS.DAQBridge.RheoSense.OPCUASource;
public class Worker(ILogger<IDWISWorker<ConfigurationForRheoSense>> logger, ILogger<DWISClientOPCF>? clientLogger)
    : RheoSenseWorker<ConfigurationForRheoSense>(logger, clientLogger)
{
    private sealed class SourceRuntime(RheometerInstallation installation, int seed)
    {
        public RheometerSource Source { get; } = installation.Source;
        public RheometerInstallation Installation { get; } = installation;
        public RheoSenseOPCUATagSet Tags { get; } = installation.Tags;
        public RheoSenseInstrumentData Data { get; } = new(installation.InputNamespace, installation.InputPrefix);
        public RheoSenseInstrumentData Sigma { get; } = new(installation.InputNamespace, installation.InputPrefix + "Sigma_", false);
        public RheoSenseStateData States { get; } = new(installation.Source, installation.Tags);
        public Random Random { get; } = new(seed);
        public int Index => (int)Source.Placement;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectToOPCUA();
        if (Configuration is null || _OPCUAClient?.Connected != true) return;
        Configuration.ValidateSources();
        var sources = Configuration.GetInstallations().Select(installation => new SourceRuntime(installation,
            installation.HistoryId.Aggregate(Configuration.SimulationSeed, (seed, c) => unchecked(seed * 31 + c)))).ToArray();
        foreach (var source in sources)
        {
            await RheoSenseUnitConversions.ConfigureAsync(Configuration, source.Tags, cancellationToken: stoppingToken);
            ConfigureOPCUANodes(source.Data, source.Tags);
            ConfigureOPCUANodes(source.Sigma, source.Tags, standardDeviation: true);
            if (source.Installation.ExtrapolationId is null) await RegisterToOPCUA(source.States, source.Installation.InputManifestName, RheoSenseConstants.Provider);
            await RegisterToOPCUA(source.Data, source.Installation.InputManifestName, RheoSenseConstants.Provider);
            await RegisterToOPCUA(source.Sigma, source.Installation.InputManifestName, RheoSenseConstants.Provider);
            FillConditions(source.Data, 0, source.Random, source.Index);
            FillRheology(source.Data, 0, source.Index);
            FillStandardDeviations(source.Sigma, source.Index);
            await PublishOPCUATagSetAsync(source.Data, source.Tags, stoppingToken);
            await PublishOPCUAStandardDeviationTagSetAsync(source.Sigma, source.Tags, stoppingToken);
        }
        DateTime started = DateTime.UtcNow;
        TimeSpan conditions = Configuration.SimulationConditionGenerationPeriod > TimeSpan.Zero ? Configuration.SimulationConditionGenerationPeriod : TimeSpan.FromSeconds(10);
        TimeSpan generation = Configuration.SimulationDataGenerationPeriod > TimeSpan.Zero ? Configuration.SimulationDataGenerationPeriod : TimeSpan.FromMinutes(30);
        TimeSpan publish = Configuration.SimulationOPCUAPublishPeriod > TimeSpan.Zero ? Configuration.SimulationOPCUAPublishPeriod : TimeSpan.FromMinutes(5);
        DateTime nextGeneration = started + generation, nextPublish = started + publish, nextConditions = started;
        int heartbeat = 1;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DateTime now = DateTime.UtcNow;
            heartbeat = heartbeat == 1 ? 2 : 1;
            foreach (var source in sources.Where(s => s.Installation.ExtrapolationId is null))
            {
                source.States.RunningHeartbeat = Scalar(heartbeat);
                source.States.Automatic = Scalar(1);
                source.States.Gel10SecondsActive = Scalar(0);
                source.States.Gel10MinutesActive = Scalar(0);
                source.States.OperatingHours = Scalar((now - started).TotalHours);
                // OperationTime encoding is unspecified by IMS; do not simulate an invented value.
                await PublishOPCUAAsync(source.States, null, null, null, stoppingToken);
            }
            if (now < nextConditions) continue;
            nextConditions = AdvanceSchedule(nextConditions, conditions, now);
            bool generate = now >= nextGeneration;
            bool publishRheology = generate || now >= nextPublish;
            foreach (var source in sources)
            {
                FillConditions(source.Data, (now - started).TotalSeconds, source.Random, source.Index);
                FillConditionStandardDeviations(source.Sigma, source.Index);
                await PublishOPCUATagSetChannelAsync(source.Data, source.Tags, RheoSenseDataChannel.DensityPressureTemperature, stoppingToken);
                await PublishOPCUAStandardDeviationTagSetChannelAsync(source.Sigma, source.Tags, RheoSenseDataChannel.DensityPressureTemperature, stoppingToken);
                if (generate) FillRheology(source.Data, (now - started).TotalSeconds, source.Index);
                if (publishRheology)
                {
                    await PublishOPCUATagSetChannelAsync(source.Data, source.Tags, RheoSenseDataChannel.Rheology, stoppingToken);
                    await PublishOPCUAStandardDeviationTagSetChannelAsync(source.Sigma, source.Tags, RheoSenseDataChannel.Rheology, stoppingToken);
                }
            }
            if (generate) nextGeneration = AdvanceSchedule(nextGeneration, generation, now);
            if (publishRheology) nextPublish = AdvanceSchedule(nextPublish, publish, now);
        }
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

        data.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure = Scalar(3.5 + sensorIndex * .2);
        data.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure = Scalar(5.0 + sensorIndex * .3);
        data.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure = Scalar(3.0 + sensorIndex * .2);
        data.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure = Scalar(4.3 + sensorIndex * .3);
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
