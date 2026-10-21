using System.Text.Json;
using DWIS.DAQBridge.RheoSense.Model;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
namespace DWIS.DAQBridge.RheoSense.Tests;
public class GelAndExtrapolationTests
{
    [Fact]
    public void GelValuesAreMappedWithoutFlowCurveFittingOrCorrection()
    {
        var input = new RheoSenseInstrumentData("urn:test", "test");
        input.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure = new() { ScalarValue = 2 };
        input.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure = new() { ScalarValue = 4 };
        input.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure = new() { ScalarValue = 1.5 };
        input.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure = new() { ScalarValue = 3 };
        var output = new ActivePitOutputData();
        RheoSenseBridgeMapper.Map(input, output);
        Assert.Equal(2, output.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure!.Mean);
        Assert.Equal(4, output.GelStrengthAfter10MinutesAtMeasuredTemperatureAndPressure!.Mean);
        Assert.Equal(1.5, output.GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure!.Mean);
        Assert.Equal(3, output.GelStrengthAfter10MinutesAtExtrapolatedTemperatureAndPressure!.Mean);
        Assert.Null(output.GelStrengthAfter10SecondsAtMeasuredTemperatureAndPressure.StandardDeviation);
    }
    [Fact]
    public void AdditionalTargetReusesDeviceButHasIndependentConditionsValuesAndBindings()
    {
        var source = new RheometerSource();
        var primary = new RheometerOutputData(source);
        var target = new RheometerExtrapolationOutput(source, "HighTemperature");
        var primaryVariables = primary.Manifests.Value.Values.SelectMany(m => m.ProvidedVariables).Select(v => v.VariableID).ToHashSet();
        Assert.All(target.Manifests.Value.Values, manifest => {
            Assert.All(manifest.ProvidedVariables, v => { Assert.Contains("HighTemperature", v.VariableID); Assert.DoesNotContain(v.VariableID, primaryVariables); });
            Assert.All(manifest.InjectedReferences.Where(r => r.VerbURI.EndsWith("HasTemperatureReference")), r => Assert.Contains("ExtrapolatedHighTemperature", r.Object.ID));
            Assert.DoesNotContain(manifest.InjectedReferences, r => r.Object.ID.Contains("HighTemperatureMeasurementDevice"));
        });
        Assert.All(target.CreateQueries("https://fixture/Nodes/").Values, query => Assert.Contains("HighTemperature", Assert.Single(query.Values).SparQL));
        Assert.Equal(2, target.Manifests.Value.Keys.Count(p => p.Name.StartsWith("GelStrength")));
        foreach (var manifest in target.Manifests.Value.Where(pair => pair.Key.Name.StartsWith("GelStrength")).Select(pair => pair.Value))
            Assert.All(manifest.InjectedReferences.Where(edge => edge.VerbURI.EndsWith("IsOfMeasurableQuantity")), edge => Assert.Equal("fluidShearStress", edge.Object.ID));
    }
    [Fact]
    public void CombinedVendorConfigurationUsesSgAndTwoTargetsAndIgnoresPvYp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DWIS.DAQBridge.RheoSense.sln"))) directory = directory.Parent;
        var config = JsonSerializer.Deserialize<ConfigurationForRheoSense>(File.ReadAllText(Path.Combine(directory!.FullName, "config", "config.ims-tag-list.two-temperatures.example.json")))!;
        config.Sources![0].Enabled = true;
        config.ValidateSources();
        var installations = config.GetInstallations();
        Assert.Equal(2, installations.Count);
        var output = new RheometerOutputData(installations[0].Source, installations[0].Tags.DisabledBinghamOutputs());
        Assert.DoesNotContain(output.Manifests.Value.Keys, property => property.Name.StartsWith("PlasticViscosity") || property.Name.StartsWith("YieldPoint"));
        Assert.Equal("specific gravity", installations[0].Tags.Resolve("MassDensityFromCoriolisAtMeasuredTemperatureAndPressure").UnitName);
        Assert.All(installations, installation => {
            Assert.False(installation.Tags.Resolve("PlasticViscosityAtExtraplatedTemperatureAndPressure").Enabled);
            Assert.False(installation.Tags.Resolve("YieldPointAtExtraplatedTemperatureAndPressure").Enabled);
            Assert.True(installation.Tags.Resolve("GelStrengthAfter10SecondsAtExtrapolatedTemperatureAndPressure").Enabled);
        });
        Assert.Equal(50, installations[0].Tags.Resolve("ExtrapolatedTemperature").ConstantValue);
        Assert.Equal(150, installations[1].Tags.Resolve("ExtrapolatedTemperature").ConstantValue);
    }
}
