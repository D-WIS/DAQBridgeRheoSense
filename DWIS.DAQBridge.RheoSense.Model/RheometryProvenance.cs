using System.Reflection;
using DWIS.API.DTO;

namespace DWIS.DAQBridge.RheoSense.Model;

// Application profile shared with DAQDisplay. All engineering/processing relations
// use DWIS; the versioned extension supplies purpose and JSON-member mappings only.
internal static class RheometryProvenance
{
    private const string D = "http://ddhub.no/";
    private const string P = "https://digiwells.no/daq/rheometry/1/";
    public static IReadOnlyDictionary<PropertyInfo, ManifestFile> Enrich(
        IReadOnlyDictionary<PropertyInfo, ManifestFile> manifests, bool rheoSense)
    {
        foreach (var (property, manifest) in manifests)
        {
            var dynamic = manifest.InjectedReferences.FirstOrDefault(r => r.VerbURI == D + "HasDynamicValue" && !r.Object.ID.StartsWith("Sigma_", StringComparison.Ordinal));
            if (dynamic is null) continue;
            var data = dynamic.Subject;
            bool curve = property.Name.StartsWith("FlowCurveAt", StringComparison.Ordinal);
            bool ypl = property.Name.StartsWith("YPL", StringComparison.Ordinal);
            bool bingham = property.Name.StartsWith("PlasticViscosity", StringComparison.Ordinal) || property.Name.StartsWith("YieldPoint", StringComparison.Ordinal);
            bool gel = property.Name.StartsWith("GelStrength", StringComparison.Ordinal);
            if (!curve && !ypl && !bingham && !gel) continue;
            var refs = manifest.InjectedReferences.ToList();
            var nodes = manifest.InjectedNodes.ToList();
            NodeIdentifier Local(string id) => new() { NameSpace = data.NameSpace, ID = id };
            void Link(NodeIdentifier s, string v, NodeIdentifier o) => refs.Add(new InjectedReference { Subject = s, VerbURI = v, Object = o });
            void Mark(NodeIdentifier subject, string axis, string value)
            {
                // A semantic endpoint descriptor states how this value/operation is to
                // be interpreted. EndPointDescription is an existing string field; no
                // new OPC UA reference types or unregistered node fields are required.
                var descriptor = Add(subject.ID + ".profile." + axis, "DataEndPoint");
                nodes.Last().Fields = new List<Field> { new() { FieldName = "EndPointDescription", FieldValue = P + axis + "/" + value } };
                Link(subject, D + "IsDependentOn", descriptor);
            }
            NodeIdentifier Add(string id, string type)
            {
                nodes.Add(new InjectedNode { UniqueName = id, BrowseName = id, DisplayName = id, TypeDictionaryURI = type, Fields = new List<Field>() });
                return Local(id);
            }
            var device = refs.FirstOrDefault(r => r.VerbURI == D + "IsMeasuredBy" && r.Subject.ID == data.ID)?.Object;
            device ??= refs.FirstOrDefault(r => r.VerbURI == D + "IsSubSystemOf")?.Object;
            var temp = refs.FirstOrDefault(r => r.VerbURI == D + "HasTemperatureReference" && r.Subject.ID == data.ID)?.Object;
            var pressure = refs.FirstOrDefault(r => r.VerbURI == D + "HasPressureReference" && r.Subject.ID == data.ID)?.Object;
            bool extrapolated = property.Name.Contains("Extrapolated", StringComparison.Ordinal);
            void Conditions(NodeIdentifier n)
            {
                if (temp is not null) Link(n, D + "HasTemperatureReference", temp);
                if (pressure is not null) Link(n, D + "HasPressureReference", pressure);
                if (extrapolated)
                {
                    var c = Add(n.ID + ".extrapolation", "Extrapolated");
                    Link(n, D + "IsCharacterizedBy", c);
                }
            }
            if (gel)
            {
                Mark(data, "gelRestTimeSeconds", property.Name.Contains("10Seconds", StringComparison.Ordinal) ? "10" : "600");
                Mark(data, "compatibility", "InstrumentNative");
                Conditions(data);
                manifest.InjectedNodes = nodes; manifest.InjectedReferences = refs;
                continue;
            }
            if (bingham)
            {
                Mark(data, "nonNewtonianTreatment", rheoSense ? "NotInfluenced" : "Uncorrected");
                Mark(data, "compatibility", "InstrumentNative");
                manifest.InjectedNodes = nodes; manifest.InjectedReferences = refs;
                continue;
            }
            if (ypl)
            {
                Mark(data, "compatibility", "NoCompatibilityConversion");
                Mark(data, "nonNewtonianTreatment", rheoSense ? "NotInfluenced" : "Corrected");
                var operation = Add(data.ID + ".derivation", "ModelFitter");
                Link(data, D + "IsComputedBy", operation);
                if (device is not null) Link(operation, D + "IsSubSystemOf", device);
                Mark(operation, "purpose", rheoSense ? "NativeRheologyFit" : "NonNewtonianCorrection");
                // The corresponding curve is linked below after all properties are built.
                manifest.InjectedNodes = nodes; manifest.InjectedReferences = refs;
                continue;
            }
            // The container combines representations; it is not a direct measurement.
            var nativeInput = Add(data.ID + ".nativeInput", extrapolated ? "ComputedData" : "DerivedMeasurement");
            Mark(nativeInput, "nonNewtonianTreatment", rheoSense ? "NotInfluenced" : "Uncorrected");
            Mark(nativeInput, "compatibility", "InstrumentNative");
            if (device is not null) Link(nativeInput, D + "IsMeasuredBy", device);
            Conditions(nativeInput);
            foreach (bool newtonian in new[] { true, false })
            {
                string suffix = newtonian ? "Newtonian" : "NonNewtonian";
                var representation = Add(data.ID + "." + suffix, "FlowCurve");
                Link(representation, D + "BelongsToClass", new() { NameSpace = D.TrimEnd('/'), ID = !rheoSense && newtonian ? "DerivedMeasurement" : "ComputedData" });
                Link(data, D + "IsDependentOn", representation);
                Mark(representation, "shearRateMember", "ConcentricCylindersShearRate" + suffix + "Hypothesis");
                Mark(representation, "shearStressMember", "ConcentricCylindersShearStress" + suffix + "Hypothesis");
                Mark(representation, "nonNewtonianTreatment", newtonian ? "Uncorrected" : rheoSense ? "ModelConsistent" : "Corrected");
                Mark(representation, "compatibility", rheoSense ? "Fann35Equivalent" : newtonian ? "InstrumentNative" : "NoCompatibilityConversion");
                var hypothesis = Add(representation.ID + ".hypothesis", newtonian ? "NewtonianFluid" : "NonNewtonianFluid");
                Link(representation, D + "IsCalculatedUsingRheologicalHypothesis", hypothesis);
                var operation = Add(representation.ID + ".operation", rheoSense ? "Simulator" : "ComputationUnit");
                Link(representation, D + "IsComputedBy", operation);
                if (device is not null) Link(operation, D + "IsSubSystemOf", device);
                Mark(operation, "purpose", rheoSense ? "Fann35Compatibility" : newtonian ? "NativeRheometerConversion" : "NonNewtonianCorrection");
                if (rheoSense) Mark(operation, "compatibilityTarget", "Fann35-R1B1");
                if (rheoSense || newtonian)
                {
                    Link(nativeInput, D + "IsComputationInput", operation);
                    Link(representation, D + "IsDerivedFrom", nativeInput);
                }
                foreach (string component in new[] { "ShearRate", "ShearStress" })
                {
                    var member = Add(representation.ID + "." + component, !rheoSense && !newtonian ? "CorrectedMeasurement" : !rheoSense && component == "ShearStress" ? "DerivedMeasurement" : "ComputedData");
                    Link(member, D + "IsDependentOn", representation);
                    Link(member, D + "IsComputedBy", operation);
                    Link(member, D + "IsOfMeasurableQuantity", new() { NameSpace = "http://ddhub.no/UnitAndQuantity", ID = component == "ShearRate" ? "FluidShearRate" : "FluidShearStress" });
                    Mark(member, "jsonMember", "ConcentricCylinders" + component + suffix + "Hypothesis");
                    Mark(member, "nonNewtonianTreatment", newtonian ? "Uncorrected" : rheoSense ? "ModelConsistent" : "Corrected");
                    Mark(member, "compatibility", rheoSense ? "Fann35Equivalent" : newtonian ? "InstrumentNative" : "NoCompatibilityConversion");
                    Conditions(member);
                }
                if (!rheoSense && !newtonian)
                {
                    Link(representation, D + "BelongsToClass", new() { NameSpace = D.TrimEnd('/'), ID = "CorrectedMeasurement" });
                    Link(Local(data.ID + ".Newtonian"), D + "IsComputationInput", operation);
                    Link(representation, D + "IsDerivedFrom", Local(data.ID + ".Newtonian"));
                }
                Conditions(representation);
            }
            foreach (string component in new[] { "RotationalSpeed", "Torque" })
            {
                var member = Add(data.ID + "." + component, rheoSense ? "ComputedData" : component == "RotationalSpeed" ? "SetPoint" : "Measurement");
                Link(member, D + "IsDependentOn", data);
                Mark(member, "jsonMember", "ConcentricCylinders" + component);
                Mark(member, "compatibility", rheoSense ? "Fann35Equivalent" : "InstrumentNative");
                Link(member, D + "IsOfMeasurableQuantity", new() { NameSpace = "http://ddhub.no/UnitAndQuantity", ID = component == "Torque" ? "TorqueSmall" : "AngularVelocityDrilling" });
                if (rheoSense) Link(member, D + "IsComputedBy", Local(data.ID + ".Newtonian.operation"));
                // BaraLogix does not currently populate torque; no claim that it is available.
                if (!rheoSense && component == "Torque") Mark(member, "availability", "NotPopulated");
                Conditions(member);
            }
            manifest.InjectedNodes = nodes; manifest.InjectedReferences = refs;
        }
        // Link fitted rheology and synthetic curves to their actual input representations.
        foreach (var (property, manifest) in manifests)
        {
            bool extrapolated = property.Name.Contains("Extrapolated", StringComparison.Ordinal);
            var curvePair = manifests.FirstOrDefault(p => p.Key.Name == (extrapolated ? "FlowCurveAtExtrapolatedTemperatureAndPressure" : "FlowCurveAtMeasuredTemperatureAndPressure"));
            if (curvePair.Value is null) continue;
            var curve = curvePair.Value.InjectedReferences.First(r => r.VerbURI == D + "HasDynamicValue" && !r.Object.ID.StartsWith("Sigma_", StringComparison.Ordinal)).Subject;
            var refs = manifest.InjectedReferences.ToList();
            if (property.Name.StartsWith("YPL", StringComparison.Ordinal))
            {
                var op = refs.First(r => r.VerbURI == D + "IsComputedBy" && r.Object.ID.EndsWith(".derivation", StringComparison.Ordinal)).Object;
                refs.Add(new() { Subject = new() { NameSpace = curve.NameSpace, ID = curve.ID + (rheoSense ? ".nativeInput" : ".NonNewtonian") }, VerbURI = D + "IsComputationInput", Object = op });
                if (rheoSense)
                {
                    var fitted = refs.First(r => r.VerbURI == D + "IsComputedBy" && r.Object.ID.EndsWith(".derivation", StringComparison.Ordinal)).Subject;
                    foreach (string suffix in new[] { ".Newtonian.operation", ".NonNewtonian.operation" })
                        refs.Add(new() { Subject = fitted, VerbURI = D + "IsComputationInput", Object = new() { NameSpace = curve.NameSpace, ID = curve.ID + suffix } });
                }
            }
            manifest.InjectedReferences = refs;
        }
        return manifests;
    }
}
