using System.IO.Compression;
using System.Text.Json;
using System.Text;
using DWIS.DAQBridge.RheoSense.WebApp.Services;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using YPLCalibrationFromRheometer.Model;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class ModelTests
{
    [Fact]
    public void Semantic_outputs_are_not_OPCUA_transport_models()
    {
        ActivePitOutputData activePit = new();
        FlowLineOutputData flowLine = new();

        Assert.IsNotAssignableFrom<DWISDataWithOPCUA>(activePit);
        Assert.IsNotAssignableFrom<DWISDataWithOPCUA>(flowLine);
        Assert.Equal(18, activePit.Manifests.Value.Count);
        Assert.Equal(18, flowLine.Manifests.Value.Count);
    }

    [Fact]
    public void Each_instrument_exposes_the_36_numeric_OPCUA_values()
    {
        RheoSenseInstrumentData activePit = new(
            RheoSenseConstants.ActivePitNamespace,
            RheoSenseConstants.ActivePitNodePrefix);
        RheoSenseInstrumentData flowLine = new(
            RheoSenseConstants.FlowLineNamespace,
            RheoSenseConstants.FlowLineNodePrefix);

        Assert.Equal(36, activePit.OPCUANodes.Value.Count);
        Assert.Equal(36, flowLine.OPCUANodes.Value.Count);
        Assert.All(activePit.OPCUANodes.Value.Keys, property => Assert.Equal(typeof(ScalarDrillingProperty), property.PropertyType));
        Assert.True(ExpectedInstrumentTags.SetEquals(activePit.OPCUANodes.Value.Values.Select(node => node.ID)));
        Assert.True(ExpectedInstrumentTags.SetEquals(flowLine.OPCUANodes.Value.Values.Select(node => node.ID)));

        Assert.All(activePit.OPCUANodes.Value.Values, node =>
        {
            Assert.Equal(RheoSenseConstants.ActivePitNamespace, node.NameSpace);
            Assert.Equal(RheoSenseConstants.ActivePitNodePrefix, node.PrefixID);
        });
        Assert.All(flowLine.OPCUANodes.Value.Values, node =>
        {
            Assert.Equal(RheoSenseConstants.FlowLineNamespace, node.NameSpace);
            Assert.Equal(RheoSenseConstants.FlowLineNodePrefix, node.PrefixID);
        });
    }

    [Fact]
    public void Standard_deviation_tags_exclude_chosen_extrapolation_parameters()
    {
        RheoSenseInstrumentData deviations = new(
            RheoSenseConstants.ActivePitNamespace,
            RheoSenseConstants.ActivePitStandardDeviationNodePrefix,
            includeExtrapolationParameters: false);

        Assert.Equal(34, deviations.OPCUANodes.Value.Count);
        Assert.DoesNotContain(
            deviations.OPCUANodes.Value.Keys,
            property => property.Name is nameof(RheoSenseInstrumentData.ExtrapolatedTemperature) or
                nameof(RheoSenseInstrumentData.ExtrapolatedPressure));
    }

    [Fact]
    public void Mapper_builds_flow_curves_from_numeric_shear_stresses()
    {
        const double measuredYieldStress = 4.5;
        const double measuredConsistency = 0.65;
        const double measuredBehavior = 0.72;
        const double extrapolatedYieldStress = 4.1;
        const double extrapolatedConsistency = 0.57;
        const double extrapolatedBehavior = 0.68;
        RheoSenseInstrumentData input = new("urn:test", "test.")
        {
            MeasuredTemperature = Scalar(300),
            MeasuredPressure = Scalar(200_000),
            YPLYieldStressAtMeasuredTemperatureAndPressure = Scalar(999),
            YPLConsistencyIndexAtMeasuredTemperatureAndPressure = Scalar(999),
            YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure = Scalar(999),
            ShearStressAt3RPMAtMeasuredTemperatureAndPressure = PipeStress(3, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt6RPMAtMeasuredTemperatureAndPressure = PipeStress(6, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt10RPMAtMeasuredTemperatureAndPressure = PipeStress(10, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt20RPMAtMeasuredTemperatureAndPressure = PipeStress(20, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt30RPMAtMeasuredTemperatureAndPressure = PipeStress(30, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt60RPMAtMeasuredTemperatureAndPressure = PipeStress(60, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt100RPMAtMeasuredTemperatureAndPressure = PipeStress(100, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt200RPMAtMeasuredTemperatureAndPressure = PipeStress(200, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt300RPMAtMeasuredTemperatureAndPressure = PipeStress(300, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt600RPMAtMeasuredTemperatureAndPressure = PipeStress(600, measuredYieldStress, measuredConsistency, measuredBehavior),
            ShearStressAt3RPMAtExtraplatedTemperatureAndPressure = PipeStress(3, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt6RPMAtExtraplatedTemperatureAndPressure = PipeStress(6, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt10RPMAtExtraplatedTemperatureAndPressure = PipeStress(10, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt20RPMAtExtraplatedTemperatureAndPressure = PipeStress(20, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt30RPMAtExtraplatedTemperatureAndPressure = PipeStress(30, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt60RPMAtExtraplatedTemperatureAndPressure = PipeStress(60, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt100RPMAtExtraplatedTemperatureAndPressure = PipeStress(100, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt200RPMAtExtraplatedTemperatureAndPressure = PipeStress(200, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt300RPMAtExtraplatedTemperatureAndPressure = PipeStress(300, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior),
            ShearStressAt600RPMAtExtraplatedTemperatureAndPressure = PipeStress(600, extrapolatedYieldStress, extrapolatedConsistency, extrapolatedBehavior)
        };
        ActivePitOutputData output = new();

        RheoSenseBridgeMapper.Map(input, output);

        Assert.Equal(300, output.MeasuredTemperature?.Mean);
        Assert.Equal(200_000, output.MeasuredPressure?.Mean);
        Assert.Equal(10, output.FlowCurveAtMeasuredTemperatureAndPressure?.Measurements?.Count);
        Assert.Equal(10, output.FlowCurveAtExtrapolatedTemperatureAndPressure?.Measurements?.Count);
        Assert.Equal(measuredYieldStress, output.YPLYieldStressAtMeasuredTemperatureAndPressure!.Mean!.Value, 5);
        Assert.Equal(measuredConsistency, output.YPLConsistencyIndexAtMeasuredTemperatureAndPressure!.Mean!.Value, 5);
        Assert.Equal(measuredBehavior, output.YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure!.Mean!.Value, 5);
        Assert.Equal(
            3.0 / 60.0,
            Assert.IsType<OSDC.DotnetLibraries.General.Statistics.DiracDistribution>(
                output.FlowCurveAtMeasuredTemperatureAndPressure?.Measurements?[0].ConcentricCylindersRotationalSpeed).Value);
        FlowCurveMeasurement first = output.FlowCurveAtMeasuredTemperatureAndPressure!.Measurements![0];
        Assert.NotNull(first.ConcentricCylindersShearRateNewtonianHypothesis?.Mean);
        Assert.NotNull(first.ConcentricCylindersShearRateNonNewtonianHypothesis?.Mean);
        Assert.NotNull(first.ConcentricCylindersShearStressNewtonianHypothesis?.Mean);
        Assert.NotNull(first.ConcentricCylindersShearStressNonNewtonianHypothesis?.Mean);
        Assert.NotNull(first.ConcentricCylindersTorque?.Mean);
        Assert.NotEqual(
            first.ConcentricCylindersShearRateNewtonianHypothesis?.Mean,
            first.ConcentricCylindersShearRateNonNewtonianHypothesis?.Mean);
        Assert.NotEqual(
            first.ConcentricCylindersShearStressNewtonianHypothesis?.Mean,
            first.ConcentricCylindersShearStressNonNewtonianHypothesis?.Mean);

        AssertEquivalentCouetteCorrection(output.FlowCurveAtMeasuredTemperatureAndPressure);
    }

    [Fact]
    public void Mapper_uses_Zamora_fallback_for_a_shear_thickening_curve()
    {
        const double yieldStress = 2.0;
        const double consistency = 0.75;
        const double behavior = 1.5;
        RheoSenseInstrumentData input = CreateMeasuredCurve(yieldStress, consistency, behavior);
        ActivePitOutputData output = new();

        YPLModel mullineuxOnly = new();
        mullineuxOnly.FitToMullineux(
            CreateFittingData(yieldStress, consistency, behavior),
            0.25,
            YPLModel.ModelType.YPL);
        Assert.False(IsPhysicalModel(mullineuxOnly));

        RheoSenseBridgeMapper.Map(input, output);

        Assert.Equal(10, output.FlowCurveAtMeasuredTemperatureAndPressure?.Measurements?.Count);
        Assert.Equal(yieldStress, output.YPLYieldStressAtMeasuredTemperatureAndPressure!.Mean!.Value, 2);
        Assert.Equal(consistency, output.YPLConsistencyIndexAtMeasuredTemperatureAndPressure!.Mean!.Value, 2);
        Assert.Equal(behavior, output.YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure!.Mean!.Value, 2);
    }

    [Fact]
    public void Semantic_manifests_distinguish_active_pit_from_return_flow()
    {
        ActivePitOutputData activePit = new();
        FlowLineOutputData flowLine = new();

        string activeManifest = JsonSerializer.Serialize(activePit.Manifests.Value.Values);
        string flowManifest = JsonSerializer.Serialize(flowLine.Manifests.Value.Values);

        Assert.Contains("RheoSenseAtActivePitMeasurementDevice", activeManifest);
        Assert.Contains("ActivePitLogical", activeManifest);
        Assert.DoesNotContain("RheoSenseAtFlowLineMeasurementDevice", activeManifest);

        Assert.Contains("RheoSenseAtFlowLineMeasurementDevice", flowManifest);
        Assert.Contains("ReturnFlowLine", flowManifest);
        Assert.Contains("FlowLineLogical", flowManifest);
        Assert.DoesNotContain("RheoSenseAtActivePitMeasurementDevice", flowManifest);
    }

    [Fact]
    public void Both_sensor_outputs_share_the_flow_curve_data_type()
    {
        Assert.Equal(
            typeof(FlowCurve),
            typeof(ActivePitOutputData).GetProperty(nameof(ActivePitOutputData.FlowCurveAtMeasuredTemperatureAndPressure))!.PropertyType);
        Assert.Equal(
            typeof(FlowCurve),
            typeof(FlowLineOutputData).GetProperty(nameof(FlowLineOutputData.FlowCurveAtMeasuredTemperatureAndPressure))!.PropertyType);
    }

    [Fact]
    public void Flow_curve_measurements_round_trip_through_Blackboard_JSON()
    {
        RheoSenseInstrumentData input = CreateMeasuredCurve(4.5, 0.65, 0.72);
        ActivePitOutputData output = new();
        RheoSenseBridgeMapper.Map(input, output);

        string json = JsonSerializer.Serialize(output.FlowCurveAtMeasuredTemperatureAndPressure!.Measurements);
        List<FlowCurveMeasurement>? decoded = JsonSerializer.Deserialize<List<FlowCurveMeasurement>>(json);

        Assert.Equal(10, decoded?.Count);
        Assert.Equal(3.0 / 60.0, decoded![0].ConcentricCylindersRotationalSpeed!.Value);
        Assert.NotNull(decoded[0].ConcentricCylindersShearStressNonNewtonianHypothesis?.Mean);
    }

    [Fact]
    public void Default_configuration_keeps_tag_overrides_independent()
    {
        ConfigurationForRheoSense configuration = new();

        Assert.Equal("opc.tcp://localhost:48030", configuration.OPCUAURL);
        Assert.Equal(TimeSpan.FromSeconds(5), configuration.ConnectionRetryInterval);
        Assert.True(configuration.EnableDataLogging);
        Assert.Equal("/home", configuration.DataLogDirectory);
        Assert.Equal(TimeSpan.FromHours(6), configuration.DataLogSegmentDuration);
        Assert.Equal(1024L * 1024L * 1024L, configuration.MaximumDataLogStorageBytes);
        Assert.Equal(6, configuration.WebAppTimeSpanHours);
        Assert.Equal(TimeSpan.FromSeconds(10), configuration.SimulationConditionGenerationPeriod);
        Assert.Equal(TimeSpan.FromMinutes(30), configuration.SimulationDataGenerationPeriod);
        Assert.Equal(TimeSpan.FromMinutes(5), configuration.SimulationOPCUAPublishPeriod);
        Assert.NotSame(configuration.ActivePitTags.Signals, configuration.FlowLineTags.Signals);
        Assert.NotSame(configuration.ActivePitTags.StandardDeviationSignals, configuration.FlowLineTags.StandardDeviationSignals);
        Assert.NotSame(configuration.ActivePitTags.UnitConversions, configuration.FlowLineTags.UnitConversions);
        Assert.NotSame(configuration.ActivePitTags.StandardDeviationUnitConversions, configuration.FlowLineTags.StandardDeviationUnitConversions);
        Assert.Equal(36, configuration.ActivePitTags.Signals.Count);
        Assert.Equal(34, configuration.ActivePitTags.StandardDeviationSignals.Count);
        Assert.Equal(36, configuration.FlowLineTags.Signals.Count);
        Assert.Equal(34, configuration.FlowLineTags.StandardDeviationSignals.Count);
        Assert.DoesNotContain(nameof(RheoSenseInstrumentData.ExtrapolatedTemperature), configuration.ActivePitTags.StandardDeviationSignals.Keys);
        Assert.DoesNotContain(nameof(RheoSenseInstrumentData.ExtrapolatedPressure), configuration.ActivePitTags.StandardDeviationSignals.Keys);
    }

    [Fact]
    public void Partial_OPCUA_signal_configuration_uses_per_field_defaults()
    {
        const string json = """
            {
              "ActivePitTags": {
                "Signals": {
                  "MeasuredPressure": {
                    "NameSpace": "urn:custom:pressure",
                    "NodeID": "ns=4;s=RheoSense.Pressure"
                  },
                  "MeasuredTemperature": {
                    "NodeID": "ns=4;s=RheoSense.Temperature"
                  }
                }
              }
            }
            """;

        ConfigurationForRheoSense configuration = JsonSerializer.Deserialize<ConfigurationForRheoSense>(json)!;

        RheoSenseOPCUATagDefinition pressure = configuration.ActivePitTags.Resolve(nameof(RheoSenseInstrumentData.MeasuredPressure));
        Assert.Equal("urn:custom:pressure", pressure.NameSpace);
        Assert.Equal("ns=4;s=RheoSense.Pressure", pressure.NodeID);

        RheoSenseOPCUATagDefinition temperature = configuration.ActivePitTags.Resolve(nameof(RheoSenseInstrumentData.MeasuredTemperature));
        Assert.Equal(RheoSenseConstants.ActivePitNamespace, temperature.NameSpace);
        Assert.Equal("ns=4;s=RheoSense.Temperature", temperature.NodeID);

        RheoSenseOPCUATagDefinition density = configuration.ActivePitTags.Resolve(
            nameof(RheoSenseInstrumentData.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure));
        Assert.Equal(RheoSenseConstants.ActivePitNamespace, density.NameSpace);
        Assert.Equal(
            RheoSenseConstants.ActivePitNodePrefix + nameof(RheoSenseInstrumentData.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure),
            density.NodeID);

        RheoSenseOPCUATagDefinition flowLinePressure = configuration.FlowLineTags.Resolve(nameof(RheoSenseInstrumentData.MeasuredPressure));
        Assert.Equal(RheoSenseConstants.FlowLineNamespace, flowLinePressure.NameSpace);
        Assert.Equal(RheoSenseConstants.FlowLineNodePrefix + nameof(RheoSenseInstrumentData.MeasuredPressure), flowLinePressure.NodeID);
    }

    [Fact]
    public void Instrument_snapshots_compare_values_and_standard_deviations_independently()
    {
        RheoSenseInstrumentData first = new("urn:test", "first.") { MeasuredPressure = Scalar(100_000) };
        RheoSenseInstrumentData firstSigma = new("urn:test", "first.sigma.", false) { MeasuredPressure = Scalar(10) };
        RheoSenseInstrumentData same = new("urn:test", "same.") { MeasuredPressure = Scalar(100_000) };
        RheoSenseInstrumentData sameSigma = new("urn:test", "same.sigma.", false) { MeasuredPressure = Scalar(10) };

        RheoSenseInputSnapshot snapshot = RheoSenseInputSnapshot.Capture(first, firstSigma);

        Assert.Equal(snapshot, RheoSenseInputSnapshot.Capture(same, sameSigma));

        same.MeasuredPressure = Scalar(100_001);
        Assert.NotEqual(snapshot, RheoSenseInputSnapshot.Capture(same, sameSigma));

        same.MeasuredPressure = Scalar(100_000);
        sameSigma.MeasuredPressure = Scalar(11);
        Assert.NotEqual(snapshot, RheoSenseInputSnapshot.Capture(same, sameSigma));
    }

    [Fact]
    public void Instrument_snapshots_separate_density_pressure_temperature_from_rheology()
    {
        RheoSenseInstrumentData values = new("urn:test", "values.")
        {
            MeasuredPressure = Scalar(100_000),
            ShearStressAt600RPMAtMeasuredTemperatureAndPressure = Scalar(50)
        };
        RheoSenseInstrumentData deviations = new("urn:test", "sigma.", false)
        {
            MeasuredPressure = Scalar(10),
            ShearStressAt600RPMAtMeasuredTemperatureAndPressure = Scalar(1)
        };

        RheoSenseInputSnapshot conditions = RheoSenseInputSnapshot.Capture(
            values,
            deviations,
            RheoSenseDataChannel.DensityPressureTemperature);
        RheoSenseInputSnapshot rheology = RheoSenseInputSnapshot.Capture(
            values,
            deviations,
            RheoSenseDataChannel.Rheology);

        values.MeasuredPressure = Scalar(100_001);
        Assert.NotEqual(conditions, RheoSenseInputSnapshot.Capture(
            values,
            deviations,
            RheoSenseDataChannel.DensityPressureTemperature));
        Assert.Equal(rheology, RheoSenseInputSnapshot.Capture(values, deviations, RheoSenseDataChannel.Rheology));

        values.ShearStressAt600RPMAtMeasuredTemperatureAndPressure = Scalar(51);
        Assert.NotEqual(rheology, RheoSenseInputSnapshot.Capture(values, deviations, RheoSenseDataChannel.Rheology));
    }

    [Fact]
    public void Mapper_updates_only_the_requested_refresh_channel()
    {
        RheoSenseInstrumentData input = CreateMeasuredCurve(4.5, 0.65, 0.72);
        input.MeasuredPressure = Scalar(200_000);
        ActivePitOutputData output = new();

        RheoSenseBridgeMapper.Map(
            input,
            output,
            null,
            RheoSenseDataChannel.DensityPressureTemperature);

        Assert.Equal(input.MeasuredPressure?.ScalarValue, output.MeasuredPressure?.Mean);
        Assert.Null(output.FlowCurveAtMeasuredTemperatureAndPressure);

        GaussianProperty? measuredPressure = output.MeasuredPressure;
        RheoSenseBridgeMapper.Map(input, output, null, RheoSenseDataChannel.Rheology);

        Assert.Same(measuredPressure, output.MeasuredPressure);
        Assert.NotNull(output.FlowCurveAtMeasuredTemperatureAndPressure);
    }

    [Fact]
    public void WebApp_history_retains_at_most_48_hours_and_returns_requested_window()
    {
        RheoSenseHistoryStore store = new();
        DateTimeOffset now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        YplValues values = new(1, 2, 3);

        store.Add(new YplTrendSample(now.AddHours(-49), values, values, values, values));
        store.Add(new YplTrendSample(now.AddHours(-2), values, values, values, values));
        store.Add(new YplTrendSample(now, values, values, values, values));

        IReadOnlyList<YplTrendSample> samples = store.GetWindow(TimeSpan.FromHours(1), now);

        Assert.Single(samples);
        Assert.Equal(now, samples[0].TimestampUtc);
        Assert.Equal(now, store.LastSampleUtc);
    }

    [Fact]
    public void WebApp_history_downsamples_before_chart_preparation_and_preserves_endpoints()
    {
        RheoSenseHistoryStore store = new();
        DateTimeOffset start = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        YplValues values = new(1, 2, 3);
        for (int index = 0; index < 10_000; index++)
        {
            DateTimeOffset timestamp = start.AddSeconds(index);
            store.Add(new YplTrendSample(timestamp, values, values, values, values));
        }

        DateTimeOffset end = start.AddSeconds(9_999);
        IReadOnlyList<YplTrendSample> samples = store.GetWindowDownsampled(
            TimeSpan.FromHours(48),
            end,
            300);

        Assert.Equal(300, samples.Count);
        Assert.Equal(start, samples[0].TimestampUtc);
        Assert.Equal(end, samples[^1].TimestampUtc);
        Assert.True(samples.Zip(samples.Skip(1), (left, right) => left.TimestampUtc < right.TimestampUtc).All(result => result));
    }

    [Fact]
    public void WebApp_history_downsamples_conditions_and_rheology_independently()
    {
        RheoSenseHistoryStore store = new();
        DateTimeOffset start = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < 1_000; index++)
        {
            store.Add(new YplTrendSample(
                start.AddSeconds(index * 10),
                YplValues.Empty,
                YplValues.Empty,
                YplValues.Empty,
                YplValues.Empty,
                new ConditionValues(300 + index, 200_000, 1_180, 1_181)));
        }
        store.Add(new YplTrendSample(
            start.AddHours(3),
            new YplValues(1, 2, 3),
            YplValues.Empty,
            YplValues.Empty,
            YplValues.Empty));

        IReadOnlyList<YplTrendSample> conditions = store.GetConditionWindowDownsampled(
            TimeSpan.FromHours(48),
            start.AddHours(4),
            100);
        IReadOnlyList<YplTrendSample> rheology = store.GetYplWindowDownsampled(
            TimeSpan.FromHours(48),
            start.AddHours(4),
            100);

        Assert.Equal(100, conditions.Count);
        Assert.Single(rheology);
        Assert.True(rheology[0].HasYplValues);
    }

    [Fact]
    public async Task WebApp_history_reader_restores_YPL_values_from_compact_published_log()
    {
        DateTimeOffset timestamp = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        string jsonLines = """
            {"type":"schema","format":"rheosense.published.v1","units":"SI","fields":["YPLYieldStressAtMeasuredTemperatureAndPressure.mean","YPLConsistencyIndexAtMeasuredTemperatureAndPressure.mean","YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure.mean","YPLYieldStressAtExtrapolatedTemperatureAndPressure.mean","YPLConsistencyIndexAtExtrapolatedTemperatureAndPressure.mean","YPLFlowBehaviorIndexAtExtrapolatedTemperatureAndPressure.mean"],"curves":[],"curveFields":[]}
            {"t":"2026-10-16T12:00:00+00:00","ap":[1,2,3,4,5,6],"apc":[],"fl":[7,8,9,10,11,12],"flc":[]}
            """;
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(jsonLines));

        IReadOnlyList<YplTrendSample> samples = await RheoSenseHistoryLogReader.ReadJsonLinesAsync(
            stream,
            timestamp.AddHours(-1),
            timestamp.AddHours(1));

        YplTrendSample sample = Assert.Single(samples);
        Assert.Equal(new YplValues(1, 2, 3), sample.ActivePitMeasured);
        Assert.Equal(new YplValues(4, 5, 6), sample.ActivePitExtrapolated);
        Assert.Equal(new YplValues(7, 8, 9), sample.FlowLineMeasured);
        Assert.Equal(new YplValues(10, 11, 12), sample.FlowLineExtrapolated);
    }

    [Fact]
    public async Task WebApp_history_reader_restores_conditions_without_duplicating_YPL_samples()
    {
        DateTimeOffset timestamp = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        string jsonLines = """
            {"type":"schema","format":"rheosense.published.v1","units":"SI","fields":["YPLYieldStressAtMeasuredTemperatureAndPressure.mean","YPLConsistencyIndexAtMeasuredTemperatureAndPressure.mean","YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure.mean","YPLYieldStressAtExtrapolatedTemperatureAndPressure.mean","YPLConsistencyIndexAtExtrapolatedTemperatureAndPressure.mean","YPLFlowBehaviorIndexAtExtrapolatedTemperatureAndPressure.mean"],"curves":[],"curveFields":[]}
            {"t":"2026-10-16T12:00:00+00:00","c":"DensityPressureTemperature","ap":[300,0.1,200000,0,1180,0.2,1181,0.3],"apc":[],"fl":[],"flc":[]}
            {"t":"2026-10-16T12:01:00+00:00","c":"Rheology","ap":[7,8,9,10,11,12],"apc":[],"fl":[],"flc":[]}
            """;
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(jsonLines));

        IReadOnlyList<YplTrendSample> samples = await RheoSenseHistoryLogReader.ReadJsonLinesAsync(
            stream,
            timestamp.AddHours(-1),
            timestamp.AddHours(1));

        Assert.Equal(2, samples.Count);
        YplTrendSample conditions = samples[0];
        Assert.Equal(timestamp, conditions.TimestampUtc);
        Assert.False(conditions.HasYplValues);
        Assert.Equal(new ConditionValues(300, 200000, 1180, 1181), conditions.ActivePitConditions);

        YplTrendSample rheology = samples[1];
        Assert.Equal(timestamp.AddMinutes(1), rheology.TimestampUtc);
        Assert.True(rheology.HasYplValues);
        Assert.Equal(new YplValues(7, 8, 9), rheology.ActivePitMeasured);
    }

    [Fact]
    public async Task WebApp_history_reader_restores_true_and_Fann35_flow_curves()
    {
        DateTimeOffset timestamp = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        RheoSenseInstrumentData input = CreateMeasuredCurve(4.5, 0.65, 0.72);
        input.MeasuredTemperature = Scalar(300);
        input.MeasuredPressure = Scalar(200_000);
        foreach (int rpm in new[] { 3, 6, 10, 20, 30, 60, 100, 200, 300, 600 })
        {
            typeof(RheoSenseInstrumentData)
                .GetProperty($"ShearStressAt{rpm}RPMAtExtraplatedTemperatureAndPressure")!
                .SetValue(input, PipeStress(rpm, 4.1, 0.57, 0.68));
        }
        input.ExtrapolatedTemperature = Scalar(333.15);
        input.ExtrapolatedPressure = Scalar(5_000_000);
        ActivePitOutputData output = new();
        RheoSenseBridgeMapper.Map(input, output);
        RheoSenseCompactLogEntry entry = RheoSenseDataLogWriter.CreatePublishedSensorEntry(
            timestamp,
            RheoSenseSensorPosition.ActivePit,
            output,
            RheoSenseDataChannel.Rheology);
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            entry.SchemaLine + Environment.NewLine + entry.RecordLine + Environment.NewLine));

        IReadOnlyList<YplTrendSample> samples = await RheoSenseHistoryLogReader.ReadJsonLinesAsync(
            stream,
            timestamp.AddHours(-1),
            timestamp.AddHours(1));

        FlowCurveSnapshot curves = Assert.IsType<FlowCurveSnapshot>(Assert.Single(samples).ActivePitFlowCurves);
        Assert.Equal(300, curves.MeasuredTemperature);
        Assert.Equal(200_000, curves.MeasuredPressure);
        Assert.Equal(333.15, curves.ExtrapolatedTemperature);
        Assert.Equal(5_000_000, curves.ExtrapolatedPressure);
        Assert.Equal(10, curves.TrueAtMeasuredConditions.Count);
        Assert.Equal(10, curves.Fann35AtMeasuredConditions.Count);
        Assert.Equal(10, curves.TrueAtExtrapolatedConditions.Count);
        Assert.Equal(10, curves.Fann35AtExtrapolatedConditions.Count);
        Assert.NotEqual(
            curves.TrueAtMeasuredConditions[0].ShearRate,
            curves.Fann35AtMeasuredConditions[0].ShearRate);
        Assert.NotEqual(
            curves.TrueAtMeasuredConditions[0].ShearStress,
            curves.Fann35AtMeasuredConditions[0].ShearStress);
    }

    [Fact]
    public void Mapper_accepts_missing_and_zero_standard_deviation_tags()
    {
        RheoSenseInstrumentData means = new("urn:test", "mean.")
        {
            MeasuredTemperature = Scalar(300),
            MeasuredPressure = Scalar(200_000)
        };
        RheoSenseInstrumentData deviations = new("urn:test", "sigma.")
        {
            MeasuredTemperature = new ScalarDrillingProperty(),
            MeasuredPressure = Scalar(0)
        };
        ActivePitOutputData output = new();

        RheoSenseBridgeMapper.Map(means, output, deviations);

        Assert.Null(output.MeasuredTemperature?.StandardDeviation);
        Assert.Equal(0, output.MeasuredPressure?.StandardDeviation);
        Assert.Equal(200_000, output.MeasuredPressure?.Mean);
    }

    [Fact]
    public void Compact_log_entries_normalize_timestamps_to_UTC()
    {
        DateTimeOffset timestamp = new(2026, 10, 16, 14, 30, 0, TimeSpan.FromHours(2));
        RheoSenseCompactLogEntry entry = RheoSenseDataLogWriter.CreateRawSensorEntry(
            timestamp,
            RheoSenseSensorPosition.ActivePit,
            new RheoSenseInstrumentData("urn:test", "value."),
            new RheoSenseInstrumentData("urn:test", "sigma.", false));

        using JsonDocument document = JsonDocument.Parse(entry.RecordLine);
        string? serializedTimestamp = document.RootElement.GetProperty("t").GetString();

        Assert.NotNull(serializedTimestamp);
        DateTimeOffset parsedTimestamp = DateTimeOffset.Parse(serializedTimestamp);
        Assert.Equal(TimeSpan.Zero, parsedTimestamp.Offset);
        Assert.Equal(timestamp.ToUniversalTime(), parsedTimestamp);
    }

    [Fact]
    public async Task Compact_logger_appends_samples_and_compresses_completed_segments()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, $"log-test-{Guid.NewGuid():N}");
        try
        {
            RheoSenseDataLogWriter writer = new(directory, TimeSpan.FromHours(6), 1024 * 1024);
            RheoSenseInstrumentData activePit = new("urn:test", "ap.") { MeasuredPressure = Scalar(123_000) };
            RheoSenseInstrumentData activePitSigma = new("urn:test", "aps.", false) { MeasuredPressure = Scalar(25) };
            RheoSenseInstrumentData flowLine = new("urn:test", "fl.") { MeasuredPressure = Scalar(456_000) };
            RheoSenseInstrumentData flowLineSigma = new("urn:test", "fls.", false);
            DateTimeOffset firstSegment = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

            await writer.AppendRawAsync(firstSegment, activePit, activePitSigma, flowLine, flowLineSigma);
            await writer.AppendRawAsync(firstSegment.AddMinutes(1), activePit, activePitSigma, flowLine, flowLineSigma);

            string currentFile = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            string content = await File.ReadAllTextAsync(currentFile);
            Assert.Equal(3, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal(1, CountOccurrences(content, nameof(RheoSenseInstrumentData.MeasuredPressure)));
            Assert.DoesNotContain("SparQLQueries", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Manifests", content, StringComparison.Ordinal);

            await writer.AppendRawAsync(firstSegment.AddHours(6), activePit, activePitSigma, flowLine, flowLineSigma);

            string archivePath = Assert.Single(Directory.GetFiles(directory, "*.zip"));
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Single(archive.Entries);
            Assert.EndsWith(".jsonl", archive.Entries[0].Name, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Compact_logger_removes_oldest_completed_logs_when_limit_is_reached()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, $"retention-test-{Guid.NewGuid():N}");
        try
        {
            RheoSenseDataLogWriter writer = new(directory, TimeSpan.FromHours(6), 1024 * 1024);
            RheoSenseInstrumentData data = new("urn:test", "test.") { MeasuredPressure = Scalar(123_000) };
            DateTimeOffset firstSegment = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

            await writer.AppendRawAsync(firstSegment, data, data, data, data);
            await writer.AppendRawAsync(firstSegment.AddHours(6), data, data, data, data);

            string currentPath = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            long currentSize = new FileInfo(currentPath).Length;
            await writer.AppendRawAsync(firstSegment.AddHours(6).AddMinutes(1), data, data, data, data);
            long expandedCurrentSize = new FileInfo(currentPath).Length;
            long recordSize = expandedCurrentSize - currentSize;
            writer = new RheoSenseDataLogWriter(directory, TimeSpan.FromHours(6), expandedCurrentSize + recordSize + 128);
            await writer.AppendRawAsync(firstSegment.AddHours(6).AddMinutes(2), data, data, data, data);

            Assert.Empty(Directory.GetFiles(directory, "*.zip"));
            Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Compact_published_log_contains_values_without_semantic_metadata()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, $"published-log-test-{Guid.NewGuid():N}");
        try
        {
            RheoSenseDataLogWriter writer = new(directory, TimeSpan.FromHours(6), 1024 * 1024);
            ActivePitOutputData activePit = new()
            {
                MeasuredPressure = new GaussianProperty { Mean = 123_000, StandardDeviation = 25 },
                ExtrapolatedPressure = new ScalarProperty { Value = 456_000 }
            };
            FlowLineOutputData flowLine = new()
            {
                MeasuredPressure = new GaussianProperty { Mean = 789_000 }
            };

            await writer.AppendPublishedAsync(DateTimeOffset.UtcNow, activePit, flowLine);

            string content = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.jsonl")));
            Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal(2, CountOccurrences(content, nameof(ActivePitOutputData.MeasuredPressure)));
            Assert.Contains("123000", content, StringComparison.Ordinal);
            Assert.Contains("456000", content, StringComparison.Ordinal);
            Assert.DoesNotContain("SparQLQueries", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Manifests", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Compact_logger_keeps_raw_and_published_streams_separate_for_each_sensor()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, $"sensor-log-test-{Guid.NewGuid():N}");
        try
        {
            RheoSenseDataLogWriter writer = new(directory, TimeSpan.FromHours(6), 1024 * 1024);
            RheoSenseInstrumentData activeInput = new("urn:test", "ap.") { MeasuredPressure = Scalar(123_000) };
            RheoSenseInstrumentData flowInput = new("urn:test", "fl.") { MeasuredPressure = Scalar(456_000) };
            RheoSenseInstrumentData sigma = new("urn:test", "sigma.", false);
            ActivePitOutputData activeOutput = new() { MeasuredPressure = new GaussianProperty { Mean = 123_000 } };
            FlowLineOutputData flowOutput = new() { MeasuredPressure = new GaussianProperty { Mean = 456_000 } };
            DateTimeOffset timestamp = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

            await writer.AppendRawSensorAsync(timestamp, RheoSenseSensorPosition.ActivePit, activeInput, sigma);
            await writer.AppendRawSensorAsync(timestamp, RheoSenseSensorPosition.FlowLine, flowInput, sigma);
            await writer.AppendPublishedSensorAsync(timestamp, RheoSenseSensorPosition.ActivePit, activeOutput);
            await writer.AppendPublishedSensorAsync(timestamp, RheoSenseSensorPosition.FlowLine, flowOutput);

            string[] files = Directory.GetFiles(directory, "*.jsonl").Select(Path.GetFileName).Order().ToArray()!;
            Assert.Equal(4, files.Length);
            Assert.Contains(files, name => name!.StartsWith("rheosense-active-pit-raw-", StringComparison.Ordinal));
            Assert.Contains(files, name => name!.StartsWith("rheosense-active-pit-published-", StringComparison.Ordinal));
            Assert.Contains(files, name => name!.StartsWith("rheosense-flow-line-raw-", StringComparison.Ordinal));
            Assert.Contains(files, name => name!.StartsWith("rheosense-flow-line-published-", StringComparison.Ordinal));

            string activeRawPath = Directory.GetFiles(directory, "rheosense-active-pit-raw-*.jsonl").Single();
            string activeRaw = await File.ReadAllTextAsync(activeRawPath);
            Assert.Contains("\"ap\":[", activeRaw, StringComparison.Ordinal);
            Assert.Contains("\"fl\":[]", activeRaw, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void Blackboard_history_uses_eight_six_hour_buckets_for_each_sensor_and_data_kind()
    {
        Assert.Equal(32, RheoSenseBlackboardHistory.NodeIds.Count());
        Assert.Equal(16, RheoSenseBlackboardHistory.PublishedNodeIds.Count());
        Assert.Equal(
            "RheoSense.ActivePit.Published.History.0",
            RheoSenseBlackboardHistory.GetNodeId(
                RheoSenseSensorPosition.ActivePit,
                RheoSenseHistoryDataKind.Published,
                0));

        DateTimeOffset first = new(2026, 10, 16, 1, 0, 0, TimeSpan.Zero);
        DateTimeOffset sameBucket = first.AddHours(1);
        DateTimeOffset sameSlotAfter48Hours = first.AddHours(48);
        Assert.Equal(
            RheoSenseBlackboardHistory.GetBucketSlot(RheoSenseBlackboardHistory.GetBucketStart(first)),
            RheoSenseBlackboardHistory.GetBucketSlot(RheoSenseBlackboardHistory.GetBucketStart(sameSlotAfter48Hours)));

        RheoSenseCompactLogEntry firstEntry = new("{\"type\":\"schema\"}", "{\"t\":1}");
        RheoSenseCompactLogEntry secondEntry = new("{\"type\":\"schema\"}", "{\"t\":2}");
        string payload = RheoSenseBlackboardHistory.Append(null, first, firstEntry);
        payload = RheoSenseBlackboardHistory.Append(payload, sameBucket, secondEntry);

        Assert.True(RheoSenseBlackboardHistory.TryDeserialize(payload, out RheoSenseHistoryBucketPayload? bucket));
        Assert.NotNull(bucket);
        Assert.Equal(1, CountOccurrences(bucket!.JsonLines, "\"type\":\"schema\""));
        Assert.Contains("{\"t\":1}", bucket.JsonLines, StringComparison.Ordinal);
        Assert.Contains("{\"t\":2}", bucket.JsonLines, StringComparison.Ordinal);

        string replaced = RheoSenseBlackboardHistory.Append(payload, sameSlotAfter48Hours, secondEntry);
        Assert.True(RheoSenseBlackboardHistory.TryDeserialize(replaced, out RheoSenseHistoryBucketPayload? replacedBucket));
        Assert.DoesNotContain("{\"t\":1}", replacedBucket!.JsonLines, StringComparison.Ordinal);
    }

    [Fact]
    public void Blackboard_history_manifest_contains_only_plain_string_OPCUA_values()
    {
        DWIS.API.DTO.ManifestFile manifest = RheoSenseBlackboardHistory.CreateManifest();

        Assert.Equal("RheoSense", manifest.InjectionInformation.ProvidedVariablesNamespaceAlias);
        Assert.Equal(32, manifest.ProvidedVariables.Count);
        Assert.All(manifest.ProvidedVariables, variable => Assert.Equal("string", variable.DataType));
        Assert.Empty(manifest.InjectedNodes);
        Assert.Empty(manifest.InjectedVariables);
        Assert.Empty(manifest.InjectedReferences);
    }

    private static ScalarDrillingProperty Scalar(double value)
    {
        return new ScalarDrillingProperty { ScalarValue = value };
    }

    private static int CountOccurrences(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }

    private static ScalarDrillingProperty PipeStress(double rpm, double yieldStress, double consistency, double behavior)
    {
        const double bobRadius = 0.017245;
        const double cupRadius = 0.018415;
        double omega = rpm / 60.0 * 2.0 * Math.PI;
        double shearRate = 2.0 * omega / (1.0 - Math.Pow(bobRadius / cupRadius, 2.0));
        return Scalar(yieldStress + consistency * Math.Pow(shearRate, behavior));
    }

    private static RheoSenseInstrumentData CreateMeasuredCurve(double yieldStress, double consistency, double behavior)
    {
        RheoSenseInstrumentData input = new("urn:test", "test.");
        foreach (int rpm in new[] { 3, 6, 10, 20, 30, 60, 100, 200, 300, 600 })
        {
            typeof(RheoSenseInstrumentData)
                .GetProperty($"ShearStressAt{rpm}RPMAtMeasuredTemperatureAndPressure")!
                .SetValue(input, PipeStress(rpm, yieldStress, consistency, behavior));
        }

        return input;
    }

    private static List<ShearRateAndStress> CreateFittingData(double yieldStress, double consistency, double behavior)
    {
        const double bobRadius = 0.017245;
        const double cupRadius = 0.018415;
        return new[] { 3, 6, 10, 20, 30, 60, 100, 200, 300, 600 }
            .Select(rpm =>
            {
                double omega = rpm / 60.0 * 2.0 * Math.PI;
                double shearRate = 2.0 * omega / (1.0 - Math.Pow(bobRadius / cupRadius, 2.0));
                return new ShearRateAndStress(shearRate, yieldStress + consistency * Math.Pow(shearRate, behavior));
            })
            .ToList();
    }

    private static bool IsPhysicalModel(YPLModel model)
    {
        return double.IsFinite(model.Tau0) && double.IsFinite(model.K) && double.IsFinite(model.N) &&
               double.IsFinite(model.Chi2) && model.Chi2 >= 0 && model.Tau0 >= 0 && model.K >= 0 && model.N > 0;
    }

    private static void AssertEquivalentCouetteCorrection(FlowCurve curve)
    {
        CouetteRheometer rheometer = new()
        {
            ID = Guid.NewGuid(),
            Name = "Fann 35 R1B1 test",
            Description = "Round-trip test geometry",
            RheometerType = CouetteRheometer.RheometerTypeEnum.RotorBob,
            BobRadius = 0.017245,
            Gap = 0.018415 - 0.017245,
            BobLength = 0.03810,
            ConicalAngle = Math.PI / 6.0,
            MeasurementPrecision = 0.25,
            NewtonianEndEffectCorrection = 1.064,
            UseISOConvention = false
        };
        YPLCorrection correction = new()
        {
            ID = Guid.NewGuid(),
            Name = "RheoSense reverse-conversion test",
            Description = "Verifies that correcting the generated Couette curve recovers its non-Newtonian values.",
            RheogramInput = new Rheogram
            {
                ID = Guid.NewGuid(),
                Name = "Generated Couette curve"
            }
        };
        correction.RheogramInput.SetRheometer(rheometer);

        foreach (FlowCurveMeasurement measurement in curve.Measurements!)
        {
            correction.RheogramInput.Measurements.Add(new RheometerMeasurement(
                measurement.ConcentricCylindersShearRateNewtonianHypothesis!.Mean!.Value,
                measurement.ConcentricCylindersShearStressNewtonianHypothesis!.Mean!.Value,
                Rheogram.RateSourceEnum.BobNewtonianShearRate,
                Rheogram.StressSourceEnum.BobNewtonianShearStress));
        }

        Assert.True(correction.CalculateFullyCorrected(Rheogram.CalibrationMethodEnum.Mullineux));
        Assert.Equal(curve.Measurements.Count, correction.RheogramFullyCorrected.Count);
        for (int index = 0; index < curve.Measurements.Count; index++)
        {
            Assert.InRange(
                Math.Abs(correction.RheogramFullyCorrected[index].ShearRate -
                    curve.Measurements[index].ConcentricCylindersShearRateNonNewtonianHypothesis!.Mean!.Value),
                0,
                0.1);
            Assert.InRange(
                Math.Abs(correction.RheogramFullyCorrected[index].ShearStress -
                    curve.Measurements[index].ConcentricCylindersShearStressNonNewtonianHypothesis!.Mean!.Value),
                0,
                0.1);
        }
    }

    private static readonly HashSet<string?> ExpectedInstrumentTags =
    [
        "MeasuredTemperature",
        "MeasuredPressure",
        "MassDensityFromCoriolisAtMeasuredTemperatureAndPressure",
        "MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure",
        "ShearStressAt3RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt6RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt10RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt20RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt30RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt60RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt100RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt200RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt300RPMAtMeasuredTemperatureAndPressure",
        "ShearStressAt600RPMAtMeasuredTemperatureAndPressure",
        "PlasticViscosityAtMeasuredTemperatureAndPressure",
        "YieldPointAtMeasuredTemperatureAndPressure",
        "YPLYieldStressAtMeasuredTemperatureAndPressure",
        "YPLConsistencyIndexAtMeasuredTemperatureAndPressure",
        "YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure",
        "ExtrapolatedTemperature",
        "ExtrapolatedPressure",
        "ShearStressAt3RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt6RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt10RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt20RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt30RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt60RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt100RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt200RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt300RPMAtExtraplatedTemperatureAndPressure",
        "ShearStressAt600RPMAtExtraplatedTemperatureAndPressure",
        "PlasticViscosityAtExtraplatedTemperatureAndPressure",
        "YieldPointAtExtraplatedTemperatureAndPressure",
        "YPLYieldStressAtExtraplatedTemperatureAndPressure",
        "YPLConsistencyIndexAtExtraplatedTemperatureAndPressure",
        "YPLFlowBehaviorIndexAtExtraplatedTemperatureAndPressure"
    ];
}
