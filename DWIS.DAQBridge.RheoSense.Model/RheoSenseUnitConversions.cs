using System.Net.Http.Json;
using System.Text.Json;
using DWIS.RigOS.Common.Worker;
using OSDC.UnitConversion.Conversion;

namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>Resolves OSDC unit choices once per session; realtime conversion remains local.</summary>
public static class RheoSenseUnitConversions
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static async Task ConfigureAsync(ConfigurationForRheoSense configuration, RheoSenseOPCUATagSet tags,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        var pending = new List<(Dictionary<string, UnitConversion> Target, string Property, UnitConversion Conversion)>();
        var quantities = new Dictionary<Guid, JsonElement>();
        foreach (bool sigma in new[] { false, true })
        foreach (var (property, definition) in sigma ? tags.StandardDeviationSignals : tags.Signals)
        {
            if (!definition.Enabled || string.IsNullOrWhiteSpace(definition.UnitName)) continue;
            var explicitConversions = sigma ? tags.StandardDeviationUnitConversions : tags.UnitConversions;
            var target = sigma ? tags.ResolvedSigmaUnits : tags.ResolvedUnits;
            if (explicitConversions.ContainsKey(property) || (definition.NodeID is not null && explicitConversions.ContainsKey(definition.NodeID)))
                throw new InvalidOperationException($"Specify a named unit OR numeric conversion for {property}, not both.");
            if (!Uri.TryCreate(configuration.UnitConversionServiceUrl, UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
                throw new InvalidOperationException("Named OPC-UA units require an absolute HTTP(S) UnitConversionServiceUrl API base URL.");
            var expected = Quantity(property);
            if (!quantities.TryGetValue(expected.ID, out var quantity))
            {
                var uri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/PhysicalQuantity/" + expected.ID);
                quantity = await (client ?? Client).GetFromJsonAsync<JsonElement>(uri, cancellationToken);
                if (quantity.GetProperty("ID").GetGuid() != expected.ID)
                    throw new InvalidOperationException("Unit conversion service returned a different physical quantity.");
                quantities.Add(expected.ID, quantity);
            }
            var matches = quantity.GetProperty("UnitChoices").EnumerateArray()
                .Where(unit => string.Equals(unit.GetProperty("UnitName").GetString(), definition.UnitName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Unknown or ambiguous OSDC unit '{definition.UnitName}' for {property}.");
            double factor = matches[0].GetProperty("ConversionFactorFromSI").GetDouble();
            double offset = sigma ? 0 : matches[0].GetProperty("ConversionBiasFromSI").GetDouble();
            if (!double.IsFinite(factor) || factor <= 0 || !double.IsFinite(offset))
                throw new InvalidOperationException("Invalid OSDC conversion coefficients.");
            pending.Add((target, property, new() { ConversionFactor = factor, ConversionOffset = offset }));
        }
        // No partial configuration if any service request or unit validation fails.
        tags.ResolvedUnits.Clear(); tags.ResolvedSigmaUnits.Clear();
        foreach (var item in pending) item.Target.Add(item.Property, item.Conversion);
    }

    public static BasePhysicalQuantity Quantity(string property)
    {
        if (property is nameof(RheoSenseInstrumentData.MeasuredTemperature) or nameof(RheoSenseInstrumentData.ExtrapolatedTemperature)) return TemperatureQuantity.Instance;
        if (property is nameof(RheoSenseInstrumentData.MeasuredPressure) or nameof(RheoSenseInstrumentData.ExtrapolatedPressure)) return PressureQuantity.Instance;
        // Membership is constrained to the instrument's declared scalar contract.
        if (typeof(RheoSenseInstrumentData).GetProperty(property)?.PropertyType != typeof(OSDC.DotnetLibraries.Drilling.DrillingProperties.ScalarDrillingProperty))
            throw new InvalidOperationException("Unknown instrument property: " + property);
        if (property.StartsWith("MassDensity", StringComparison.Ordinal)) return MassDensityQuantity.Instance;
        if (property.StartsWith("PlasticViscosity", StringComparison.Ordinal)) return DynamicViscosityQuantity.Instance;
        if (property.StartsWith("GelStrength", StringComparison.Ordinal) || property.StartsWith("ShearStress", StringComparison.Ordinal) || property.StartsWith("YieldPoint", StringComparison.Ordinal) || property.StartsWith("YPLYieldStress", StringComparison.Ordinal)) return StressQuantity.Instance;
        throw new InvalidOperationException("Named units are not supported for this property: " + property);
    }
}
