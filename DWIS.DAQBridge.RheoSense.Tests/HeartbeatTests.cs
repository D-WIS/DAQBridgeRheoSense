using DWIS.DAQBridge.RheoSense.Model;
namespace DWIS.DAQBridge.RheoSense.Tests;
public class HeartbeatTests
{
    [Fact] public void RequiresChangeTimesOutAndRecoversWithoutRestart()
    {
        var monitor = new RunningHeartbeatMonitor();
        var time = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(10);
        Assert.False(monitor.Observe(1, time, timeout));
        Assert.True(monitor.Observe(2, time.AddSeconds(1), timeout));
        Assert.True(monitor.Observe(2, time.AddSeconds(10), timeout));
        Assert.False(monitor.Observe(2, time.AddSeconds(11), timeout));
        Assert.False(monitor.Observe(2, time.AddSeconds(30), timeout));
        Assert.True(monitor.Observe(1, time.AddSeconds(31), timeout));
        Assert.False(monitor.Observe(double.NaN, time.AddSeconds(32), timeout));
        Assert.False(monitor.Observe(1, time.AddSeconds(33), timeout));
        Assert.True(monitor.Observe(2, time.AddSeconds(34), timeout));
    }
    [Fact] public void RejectsInvalidTimeout() => Assert.Throws<InvalidOperationException>(() => new ConfigurationForRheoSense { HeartbeatTimeout = TimeSpan.Zero }.ValidateSources());
    [Fact] public void SixStatesHaveIndependentTransportMappings()
    {
        var source = RheometerSource.Defaults()[0];
        var tags = new RheoSenseOPCUATagSet();
        tags.StateSignals["RunningHeartbeat"] = new() { NameSpace = "urn:plc", NodeID = "StateSystem.Running" };
        var states = new RheoSenseStateData(source, tags);
        Assert.Equal(6, states.OPCUANodes.Value.Count);
        Assert.Equal("StateSystem.Running", states.OPCUANodes.Value.Single(p => p.Key.Name == "RunningHeartbeat").Value.ID);
    }
}
