using System.Text.Json;
using System.Text.Json.Serialization;
using ContractMeasurement = DWIS.RigOS.Contracts.Rheometry.V1.FlowCurveMeasurement;

namespace DWIS.DAQBridge.RheoSense.Model;

// Local properties retain instrument-specific semantic annotations. The shared package owns the wire payload.
public sealed class FlowCurveMeasurementConverter : JsonConverter<FlowCurveMeasurement>
{
    public override FlowCurveMeasurement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var point = JsonSerializer.Deserialize<ContractMeasurement>(ref reader, options)
            ?? throw new JsonException("A flow-curve measurement must be an object.");
        return new FlowCurveMeasurement
        {
            ConcentricCylindersRotationalSpeed = point.ConcentricCylindersRotationalSpeed,
            ConcentricCylindersShearRateNewtonianHypothesis = point.ConcentricCylindersShearRateNewtonianHypothesis,
            ConcentricCylindersShearRateNonNewtonianHypothesis = point.ConcentricCylindersShearRateNonNewtonianHypothesis,
            ConcentricCylindersTorque = point.ConcentricCylindersTorque,
            ConcentricCylindersShearStressNewtonianHypothesis = point.ConcentricCylindersShearStressNewtonianHypothesis,
            ConcentricCylindersShearStressNonNewtonianHypothesis = point.ConcentricCylindersShearStressNonNewtonianHypothesis
        };
    }

    public override void Write(Utf8JsonWriter writer, FlowCurveMeasurement value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new ContractMeasurement
        {
            ConcentricCylindersRotationalSpeed = value.ConcentricCylindersRotationalSpeed!,
            ConcentricCylindersShearRateNewtonianHypothesis = value.ConcentricCylindersShearRateNewtonianHypothesis!,
            ConcentricCylindersShearRateNonNewtonianHypothesis = value.ConcentricCylindersShearRateNonNewtonianHypothesis!,
            ConcentricCylindersTorque = value.ConcentricCylindersTorque!,
            ConcentricCylindersShearStressNewtonianHypothesis = value.ConcentricCylindersShearStressNewtonianHypothesis!,
            ConcentricCylindersShearStressNonNewtonianHypothesis = value.ConcentricCylindersShearStressNonNewtonianHypothesis!
        }, options);
    }
}
