using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using OSDC.DotnetLibraries.General.Statistics;
using DWIS.RigOS.Common.Worker;
using YPLCalibrationFromRheometer.Model;

namespace DWIS.DAQBridge.RheoSense.Model;

public static class RheoSenseBridgeMapper
{
    private const double FannR1B1BobRadius = 0.017245;
    private const double FannR1B1CupRadius = 0.018415;
    private const double MeasurementPrecision = 0.25;

    private static readonly CouetteRheometer FannR1B1 = new()
    {
        ID = new Guid("63818a86-1d21-4aa6-ae9f-a55f3c39b64f"),
        Name = "Fann 35 R1B1",
        Description = "Reference Couette rheometer used to express the pipe-rheometer curve as an equivalent Couette measurement.",
        RheometerType = CouetteRheometer.RheometerTypeEnum.RotorBob,
        BobRadius = FannR1B1BobRadius,
        Gap = FannR1B1CupRadius - FannR1B1BobRadius,
        BobLength = 0.03810,
        ConicalAngle = Math.PI / 6.0,
        MeasurementPrecision = MeasurementPrecision,
        NewtonianEndEffectCorrection = 1.064,
        UseISOConvention = false,
        FixedSpeedList = [3.0 / 60.0, 6.0 / 60.0, 10.0 / 60.0, 20.0 / 60.0, 30.0 / 60.0, 60.0 / 60.0, 100.0 / 60.0, 200.0 / 60.0, 300.0 / 60.0, 600.0 / 60.0]
    };

    public static void Map(
        RheoSenseInstrumentData input,
        IRheoSenseOutputData output,
        RheoSenseInstrumentData? standardDeviations = null)
    {
        Map(input, output, standardDeviations, RheoSenseDataChannel.DensityPressureTemperature);
        Map(input, output, standardDeviations, RheoSenseDataChannel.Rheology);
    }

    public static void Map(
        RheoSenseInstrumentData input,
        IRheoSenseOutputData output,
        RheoSenseInstrumentData? standardDeviations,
        RheoSenseDataChannel channel)
    {
        if (channel == RheoSenseDataChannel.DensityPressureTemperature)
        {
            MapDensityPressureTemperature(input, output, standardDeviations);
        }
        else
        {
            MapRheology(input, output, standardDeviations);
        }
    }

    private static void MapDensityPressureTemperature(
        RheoSenseInstrumentData input,
        IRheoSenseOutputData output,
        RheoSenseInstrumentData? standardDeviations)
    {
        output.MeasuredTemperature = Gaussian(input.MeasuredTemperature, standardDeviations?.MeasuredTemperature);
        output.MeasuredPressure = Gaussian(input.MeasuredPressure, standardDeviations?.MeasuredPressure);
        output.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure = Gaussian(
            input.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure,
            standardDeviations?.MassDensityFromCoriolisAtMeasuredTemperatureAndPressure);
        output.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure = Gaussian(
            input.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure,
            standardDeviations?.MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure);
    }

    private static void MapRheology(
        RheoSenseInstrumentData input,
        IRheoSenseOutputData output,
        RheoSenseInstrumentData? standardDeviations)
    {
        output.PlasticViscosityAtMeasuredTemperatureAndPressure = Gaussian(
            input.PlasticViscosityAtMeasuredTemperatureAndPressure,
            standardDeviations?.PlasticViscosityAtMeasuredTemperatureAndPressure);
        output.YieldPointAtMeasuredTemperatureAndPressure = Gaussian(
            input.YieldPointAtMeasuredTemperatureAndPressure,
            standardDeviations?.YieldPointAtMeasuredTemperatureAndPressure);

        output.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure = Gaussian(input.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure, standardDeviations?.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure);
        output.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure = Gaussian(input.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure, standardDeviations?.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure);
        output.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure = Gaussian(input.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure, standardDeviations?.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure);
        output.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure = Gaussian(input.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure, standardDeviations?.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure);
        RheologyMapping measured = FitAndGenerateCouetteCurve(MeasuredShearStresses(input));
        output.FlowCurveAtMeasuredTemperatureAndPressure = measured.FlowCurve;
        output.YPLYieldStressAtMeasuredTemperatureAndPressure = Gaussian(measured.Model?.Tau0);
        output.YPLConsistencyIndexAtMeasuredTemperatureAndPressure = Gaussian(measured.Model?.K);
        output.YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure = Gaussian(measured.Model?.N);

        output.ExtrapolatedTemperature = Scalar(input.ExtrapolatedTemperature);
        output.ExtrapolatedPressure = Scalar(input.ExtrapolatedPressure);
        output.PlasticViscosityAtExtrapolatedTemperatureAndPressure = Gaussian(
            input.PlasticViscosityAtExtraplatedTemperatureAndPressure,
            standardDeviations?.PlasticViscosityAtExtraplatedTemperatureAndPressure);
        output.YieldPointAtExtrapolatedTemperatureAndPressure = Gaussian(
            input.YieldPointAtExtraplatedTemperatureAndPressure,
            standardDeviations?.YieldPointAtExtraplatedTemperatureAndPressure);

        RheologyMapping extrapolated = FitAndGenerateCouetteCurve(ExtrapolatedShearStresses(input));
        output.FlowCurveAtExtrapolatedTemperatureAndPressure = extrapolated.FlowCurve;
        output.YPLYieldStressAtExtrapolatedTemperatureAndPressure = Gaussian(extrapolated.Model?.Tau0);
        output.YPLConsistencyIndexAtExtrapolatedTemperatureAndPressure = Gaussian(extrapolated.Model?.K);
        output.YPLFlowBehaviorIndexAtExtrapolatedTemperatureAndPressure = Gaussian(extrapolated.Model?.N);
    }

    private static RheologyMapping FitAndGenerateCouetteCurve(IEnumerable<(double Rpm, ScalarDrillingProperty? Stress)> values)
    {
        List<(double Rpm, double Stress)> pipeMeasurements = values
            .Where(value => value.Stress?.ScalarValue is double stress && double.IsFinite(stress))
            .Select(value => (value.Rpm, value.Stress!.ScalarValue!.Value))
            .ToList();

        YPLModel? model = FitYplModel(pipeMeasurements);
        if (model is null)
        {
            return new RheologyMapping(new FlowCurve { Measurements = [] }, null);
        }

        List<FlowCurveMeasurement> measurements = [];
        foreach ((double rpm, _) in pipeMeasurements)
        {
            double rotationalSpeed = rpm / 60.0;
            double omega = rotationalSpeed * 2.0 * Math.PI;
            double newtonianShearRate = YPLCorrection.GetNewtonianShearRate(omega, FannR1B1BobRadius / FannR1B1CupRadius);
            double correctedShearRate = YPLCorrection.GetShearRate(
                FannR1B1BobRadius,
                FannR1B1CupRadius,
                model.K,
                model.N,
                model.Tau0,
                omega,
                out _);
            double correctedShearStress = model.Eval(correctedShearRate);
            double stressCorrection = YPLCorrection.GetShearStressCorrectionFactor(
                FannR1B1BobRadius,
                FannR1B1CupRadius,
                model.K,
                model.N,
                model.Tau0,
                omega);
            // The library's forward Couette correction divides the apparent
            // stress by (1 + phi); multiply here to perform the reverse mapping.
            double newtonianShearStress = correctedShearStress * (1.0 + stressCorrection);

            RheometerMeasurement couetteMeasurement = new(
                rotationalSpeed,
                newtonianShearStress,
                Rheogram.RateSourceEnum.RotationalSpeed,
                Rheogram.StressSourceEnum.BobNewtonianShearStress);
            couetteMeasurement.Calculate(FannR1B1, Rheogram.RateSourceEnum.RotationalSpeed, Rheogram.StressSourceEnum.BobNewtonianShearStress);

            if (!AreFinite(
                    newtonianShearRate,
                    correctedShearRate,
                    correctedShearStress,
                    newtonianShearStress,
                    couetteMeasurement.Torque))
            {
                continue;
            }

            measurements.Add(new FlowCurveMeasurement
            {
                // The shared Rheometry/v1 contract uses the SI angular-velocity unit (rad/s).
                ConcentricCylindersRotationalSpeed = new DiracDistribution(omega),
                ConcentricCylindersShearRateNewtonianHypothesis = new GaussianDistribution(newtonianShearRate, 0.0),
                ConcentricCylindersShearRateNonNewtonianHypothesis = new GaussianDistribution(correctedShearRate, 0.0),
                ConcentricCylindersTorque = new GaussianDistribution(couetteMeasurement.Torque, 0.0),
                ConcentricCylindersShearStressNewtonianHypothesis = new GaussianDistribution(newtonianShearStress, 0.0),
                ConcentricCylindersShearStressNonNewtonianHypothesis = new GaussianDistribution(correctedShearStress, 0.0)
            });
        }

        return new RheologyMapping(new FlowCurve { Measurements = measurements }, model);
    }

    private static YPLModel? FitYplModel(List<(double Rpm, double Stress)> pipeMeasurements)
    {
        if (pipeMeasurements.Count < 3)
        {
            return null;
        }

        List<ShearRateAndStress> fittingData = pipeMeasurements
            .Select(measurement => new ShearRateAndStress(
                // RheoSense labels its pipe-rheometer points with the equivalent
                // Fann R1B1 RPM, so use that geometry for the nominal shear rate.
                YPLCorrection.GetNewtonianShearRate(
                    measurement.Rpm / 60.0 * 2.0 * Math.PI,
                    FannR1B1BobRadius / FannR1B1CupRadius),
                measurement.Stress))
            .ToList();

        YPLModel model = new();
        model.FitToMullineux(fittingData, MeasurementPrecision, YPLModel.ModelType.YPL);
        if (IsValid(model))
        {
            return model;
        }

        // The library names the Zamora/Kelessidis implementation after
        // Kelessidis. It is the fallback when Mullineux does not converge to a
        // physically usable YPL model.
        model = new YPLModel();
        model.FitToKelessidis(fittingData, MeasurementPrecision, YPLModel.ModelType.YPL);
        return IsValid(model) ? model : null;
    }

    private static bool IsValid(YPLModel model)
    {
        return AreFinite(model.Tau0, model.K, model.N, model.Chi2) &&
               model.Chi2 >= 0 && model.Tau0 >= 0 && model.K >= 0 && model.N > 0;
    }

    private static bool AreFinite(params double[] values)
    {
        return values.All(double.IsFinite);
    }

    private static GaussianProperty Gaussian(
        ScalarDrillingProperty? input,
        ScalarDrillingProperty? standardDeviation = null)
    {
        double? deviation = standardDeviation?.ScalarValue;
        if (deviation is double value && (!double.IsFinite(value) || value < 0))
        {
            deviation = null;
        }

        return new GaussianProperty
        {
            Mean = input?.ScalarValue,
            StandardDeviation = deviation
        };
    }

    private static GaussianProperty Gaussian(double? value)
    {
        return new GaussianProperty
        {
            Mean = value,
            StandardDeviation = null
        };
    }

    private static ScalarProperty Scalar(ScalarDrillingProperty? input)
    {
        return new ScalarProperty { Value = input?.ScalarValue };
    }

    private static IEnumerable<(double, ScalarDrillingProperty?)> MeasuredShearStresses(RheoSenseInstrumentData input)
    {
        yield return (3, input.ShearStressAt3RPMAtMeasuredTemperatureAndPressure);
        yield return (6, input.ShearStressAt6RPMAtMeasuredTemperatureAndPressure);
        yield return (10, input.ShearStressAt10RPMAtMeasuredTemperatureAndPressure);
        yield return (20, input.ShearStressAt20RPMAtMeasuredTemperatureAndPressure);
        yield return (30, input.ShearStressAt30RPMAtMeasuredTemperatureAndPressure);
        yield return (60, input.ShearStressAt60RPMAtMeasuredTemperatureAndPressure);
        yield return (100, input.ShearStressAt100RPMAtMeasuredTemperatureAndPressure);
        yield return (200, input.ShearStressAt200RPMAtMeasuredTemperatureAndPressure);
        yield return (300, input.ShearStressAt300RPMAtMeasuredTemperatureAndPressure);
        yield return (600, input.ShearStressAt600RPMAtMeasuredTemperatureAndPressure);
    }

    private static IEnumerable<(double, ScalarDrillingProperty?)> ExtrapolatedShearStresses(RheoSenseInstrumentData input)
    {
        yield return (3, input.ShearStressAt3RPMAtExtraplatedTemperatureAndPressure);
        yield return (6, input.ShearStressAt6RPMAtExtraplatedTemperatureAndPressure);
        yield return (10, input.ShearStressAt10RPMAtExtraplatedTemperatureAndPressure);
        yield return (20, input.ShearStressAt20RPMAtExtraplatedTemperatureAndPressure);
        yield return (30, input.ShearStressAt30RPMAtExtraplatedTemperatureAndPressure);
        yield return (60, input.ShearStressAt60RPMAtExtraplatedTemperatureAndPressure);
        yield return (100, input.ShearStressAt100RPMAtExtraplatedTemperatureAndPressure);
        yield return (200, input.ShearStressAt200RPMAtExtraplatedTemperatureAndPressure);
        yield return (300, input.ShearStressAt300RPMAtExtraplatedTemperatureAndPressure);
        yield return (600, input.ShearStressAt600RPMAtExtraplatedTemperatureAndPressure);
    }

    private sealed record RheologyMapping(FlowCurve FlowCurve, YPLModel? Model);
}
