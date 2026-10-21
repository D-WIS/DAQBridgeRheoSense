using System.Net;
using System.Text;
using System.Text.Json;
using DWIS.DAQBridge.RheoSense.Model;
using OSDC.UnitConversion.Conversion;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class UnitConfigurationTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
    private static HttpResponseMessage Response(BasePhysicalQuantity quantity) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(quantity, quantity.GetType()), Encoding.UTF8, "application/json") };

    [Fact]
    public void NonDoubleOpcNumericValuesAreConvertedToSi()
    {
        var scalar = new OSDC.DotnetLibraries.Drilling.DrillingProperties.ScalarDrillingProperty();
        var method = typeof(RheoSenseWorker<ConfigurationForRheoSense>).GetMethod("DecodeValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, new object[] { scalar, 20f, new DWIS.RigOS.Common.Worker.UnitConversion { ConversionOffset = -273.15 } })!);
        Assert.Equal(293.15, scalar.ScalarValue!.Value, 8);
    }
    [Theory]
    [InlineData("50C", 50)]
    [InlineData("150F", 150)]
    public void VendorTemplatesRequireCommissioningAndDoNotInventMissingTags(string variant, double target)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DWIS.DAQBridge.RheoSense.sln"))) directory = directory.Parent;
        var config = JsonSerializer.Deserialize<ConfigurationForRheoSense>(File.ReadAllText(Path.Combine(directory!.FullName, "config", "config.ims-tag-list." + variant + ".example.json")))!;
        Assert.Empty(config.GetSources());
        config.Sources![0].Enabled = true;
        config.ValidateSources();
        Assert.False(config.InitializeOPCUAVariables);
        Assert.False(config.ActivePitTags.Resolve("ShearStressAt60RPMAtMeasuredTemperatureAndPressure").Enabled);
        Assert.False(config.ActivePitTags.Resolve("MeasuredPressure").Enabled);
        Assert.All(config.ActivePitTags.StandardDeviationSignals.Values, tag => Assert.False(tag.Enabled));
        Assert.Equal(target, config.ActivePitTags.Resolve("ExtrapolatedTemperature").ConstantValue);
        if (variant == "150F") Assert.False(config.ActivePitTags.Resolve("ShearStressAt100RPMAtExtraplatedTemperatureAndPressure").Enabled);
    }
    [Fact]
    public async Task ServiceBasePathIsPreservedAndTemperatureSigmaHasNoOffset()
    {
        var configuration = new ConfigurationForRheoSense { UnitConversionServiceUrl = "http://units.example/custom/unitconversion/api" };
        var tags = configuration.ActivePitTags;
        tags.Signals[nameof(RheoSenseInstrumentData.MeasuredTemperature)].UnitName = "fahrenheit";
        tags.StandardDeviationSignals[nameof(RheoSenseInstrumentData.MeasuredTemperature)].UnitName = "fahrenheit";
        int requests = 0;
        using var client = new HttpClient(new Handler(request => {
            requests++;
            Assert.Equal("http://units.example/custom/unitconversion/api/PhysicalQuantity/" + TemperatureQuantity.Instance.ID, request.RequestUri!.AbsoluteUri);
            return Response(TemperatureQuantity.Instance);
        }));
        await RheoSenseUnitConversions.ConfigureAsync(configuration, tags, client);
        Assert.Equal(273.15, tags.GetConversions()["MeasuredTemperature"].ToSI(32), 8);
        Assert.Equal(5, tags.GetConversions(true)["MeasuredTemperature"].ToSI(9), 8);
        Assert.Equal(1, requests);
        Assert.Empty(tags.UnitConversions);
        await RheoSenseUnitConversions.ConfigureAsync(configuration, tags, client); // reconnect is safe
        Assert.Equal(2, requests);
    }
    [Fact]
    public async Task StressViscosityAndDensityUseTheirOwnOsdcQuantity()
    {
        var configuration = new ConfigurationForRheoSense { UnitConversionServiceUrl = "https://units.example/api/" };
        var tags = configuration.ActivePitTags;
        tags.Signals["ShearStressAt3RPMAtMeasuredTemperatureAndPressure"].UnitName = "pound per 100 square foot";
        tags.Signals["PlasticViscosityAtMeasuredTemperatureAndPressure"].UnitName = "centipoise";
        tags.Signals["MassDensityFromCoriolisAtMeasuredTemperatureAndPressure"].UnitName = "pound per gallon (US)";
        using var client = new HttpClient(new Handler(request => {
            var id = Guid.Parse(request.RequestUri!.Segments.Last());
            return Response(new BasePhysicalQuantity[] { StressQuantity.Instance, DynamicViscosityQuantity.Instance, MassDensityQuantity.Instance }.Single(q => q.ID == id));
        }));
        await RheoSenseUnitConversions.ConfigureAsync(configuration, tags, client);
        Assert.InRange(tags.GetConversions()["ShearStressAt3RPMAtMeasuredTemperatureAndPressure"].ToSI(1), .4788, .4789);
        Assert.Equal(.001, tags.GetConversions()["PlasticViscosityAtMeasuredTemperatureAndPressure"].ToSI(1), 9);
        Assert.InRange(tags.GetConversions()["MassDensityFromCoriolisAtMeasuredTemperatureAndPressure"].ToSI(1), 119.82, 119.84);
    }
    [Fact]
    public async Task InvalidUnitsAndUnavailableServiceNeverFallBackToIdentity()
    {
        var configuration = new ConfigurationForRheoSense { UnitConversionServiceUrl = "https://units.example/api/" };
        var tags = configuration.ActivePitTags;
        tags.Signals["YieldPointAtMeasuredTemperatureAndPressure"].UnitName = "centipoise";
        using var client = new HttpClient(new Handler(request => Response(StressQuantity.Instance)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RheoSenseUnitConversions.ConfigureAsync(configuration, tags, client));
        Assert.Empty(tags.GetConversions());
        using var unavailable = new HttpClient(new Handler(request => new(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => RheoSenseUnitConversions.ConfigureAsync(configuration, tags, unavailable));
        Assert.Empty(tags.GetConversions());
    }
    [Fact]
    public async Task MixedNumericAndNamedConfigurationIsRejected()
    {
        var configuration = new ConfigurationForRheoSense { UnitConversionServiceUrl = "https://units.example/api/" };
        configuration.ActivePitTags.Signals["MeasuredTemperature"].UnitName = "celsius";
        configuration.ActivePitTags.UnitConversions["MeasuredTemperature"] = new();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RheoSenseUnitConversions.ConfigureAsync(configuration, configuration.ActivePitTags));
    }
    [Fact]
    public void DisabledAndConstantMappingsSurviveResolution()
    {
        var configuration = new ConfigurationForRheoSense();
        configuration.ActivePitTags.Signals["MeasuredPressure"].Enabled = false;
        configuration.ActivePitTags.Signals["ExtrapolatedTemperature"].ConstantValue = 50;
        Assert.False(configuration.ActivePitTags.Resolve("MeasuredPressure").Enabled);
        Assert.Equal(50, configuration.ActivePitTags.Resolve("ExtrapolatedTemperature").ConstantValue);
        configuration.ValidateSources();
        configuration.ActivePitTags.Signals["MeasuredPressure"].ConstantValue = 10;
        Assert.Throws<InvalidOperationException>(() => configuration.ValidateSources());
    }
}
