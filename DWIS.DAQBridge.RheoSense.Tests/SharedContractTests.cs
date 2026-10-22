using System.Text.Json;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using OSDC.DotnetLibraries.General.Statistics;
using Xunit;
using ContractMeasurement = DWIS.RigOS.Contracts.Rheometry.V1.FlowCurveMeasurement;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class SharedContractTests
{
    [Theory]
    [InlineData("http://contracts.example:54321/")]
    [InlineData("https://contracts.example:8443/contracts/")]
    [InlineData("http://[::1]:54321/")]
    public void ConfigJsonControlsPublishedContractHostAndPort(string baseUrl)
    {
        var config = JsonSerializer.Deserialize<ConfigurationForRheoSense>(
            JsonSerializer.Serialize(new { ContractsPublicBaseUrl = baseUrl }))!;
        var data = RheometryContract.ApplyPublicBaseUrl(new ActivePitOutputData(), config.ContractsPublicBaseUrl);
        var fields = data.Manifests.Value.Where(p => p.Key.Name.StartsWith("FlowCurveAt"))
            .SelectMany(p => p.Value.InjectedNodes).SelectMany(n => n.Fields ?? []);
        Assert.Contains(fields, f => f.FieldName == "SchemaURL" && f.FieldValue == baseUrl + "Rheometry/v1/schema.json");
        Assert.Contains(fields, f => f.FieldName == "MetaDataURL" && f.FieldValue == baseUrl + "Rheometry/v1/metadata.json");
    }

    [Theory]
    [InlineData("contracts.example:54321")]
    [InlineData("ftp://contracts.example/")]
    [InlineData("https://contracts.example/?query=1")]
    [InlineData("https://contracts.example/#fragment")]
    public void InvalidContractBaseUrlsAreRejected(string baseUrl) =>
        Assert.Throws<InvalidOperationException>(() => RheometryContract.ResolveUrls(baseUrl));

    [Fact]
    public void PayloadRoundTripsThroughThePublishedContract()
    {
        var point = new FlowCurveMeasurement
        {
            ConcentricCylindersRotationalSpeed = new DiracDistribution(2.0),
            ConcentricCylindersShearRateNewtonianHypothesis = new GaussianDistribution(3.0, null),
            ConcentricCylindersShearRateNonNewtonianHypothesis = new GaussianDistribution(4.0, 0.5),
            ConcentricCylindersTorque = new GaussianDistribution(5.0, 0.2),
            ConcentricCylindersShearStressNewtonianHypothesis = new GaussianDistribution(6.0, null),
            ConcentricCylindersShearStressNonNewtonianHypothesis = new GaussianDistribution(7.0, 0.3)
        };
        string json = JsonSerializer.Serialize(new[] { point });
        var shared = JsonSerializer.Deserialize<ContractMeasurement[]>(json)!;
        Assert.Single(shared);
        Assert.Equal(2.0, shared[0].ConcentricCylindersRotationalSpeed.Value);
        Assert.Equal(4.0, shared[0].ConcentricCylindersShearRateNonNewtonianHypothesis.Mean);
        var decoded = JsonSerializer.Deserialize<FlowCurveMeasurement[]>(JsonSerializer.Serialize(shared))!;
        Assert.Equal(JsonSerializer.SerializeToElement(shared).GetRawText(), JsonSerializer.Serialize(decoded));
        Assert.Null(decoded[0].ConcentricCylindersShearRateNewtonianHypothesis!.StandardDeviation);
    }
}
