using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Model;

public interface IRheoSenseOutputData
{
    GaussianProperty? MeasuredTemperature { get; set; }
    GaussianProperty? MeasuredPressure { get; set; }
    GaussianProperty? MassDensityFromCoriolisAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? MassDensityFromVerticalPipeAtMeasuredTemperatureAndPressure { get; set; }
    FlowCurve? FlowCurveAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? PlasticViscosityAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? YieldPointAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? YPLYieldStressAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? YPLConsistencyIndexAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? YPLFlowBehaviorIndexAtMeasuredTemperatureAndPressure { get; set; }
    ScalarProperty? ExtrapolatedTemperature { get; set; }
    ScalarProperty? ExtrapolatedPressure { get; set; }
    FlowCurve? FlowCurveAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? PlasticViscosityAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? YieldPointAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? YPLYieldStressAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? YPLConsistencyIndexAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? YPLFlowBehaviorIndexAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure { get; set; }
    GaussianProperty? GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure { get; set; }
    GaussianProperty? GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure { get; set; }
}
