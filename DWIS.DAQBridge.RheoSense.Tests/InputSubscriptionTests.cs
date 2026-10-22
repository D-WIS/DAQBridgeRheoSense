using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class InputSubscriptionTests
{
    [Fact]
    public async Task NotificationsWakeWorkerAndCoalesceWithoutMutatingModelsOnCallbackThread()
    {
        var inputs = new RheoSenseInputSubscriptions();
        object? applied = null;
        inputs.Add("urn:test", "value", value => applied = value);
        Action<object?>? receive = null;
        inputs.Start((_, callback) => receive = callback);
        Parallel.For(0, 100, i => receive!(i));
        receive!(123d);
        Assert.Null(applied);
        Assert.True(await inputs.WaitAsync(TimeSpan.Zero, default));
        Assert.Equal(1, inputs.ApplyPending());
        Assert.Equal(123d, applied);
        Assert.False(await inputs.WaitAsync(TimeSpan.Zero, default));
    }

    [Fact]
    public void SharedNodesSubscribeOnceAndFanOutToIndependentBindings()
    {
        var inputs = new RheoSenseInputSubscriptions();
        object? first = null, second = null;
        inputs.Add("urn:test", "value", value => first = value);
        inputs.Add("urn:test", "value", value => second = value);
        int registrations = 0;
        inputs.Start((_, receive) => { registrations++; receive(42d); });
        inputs.ApplyPending();
        Assert.Equal(1, registrations);
        Assert.Equal(42d, first);
        Assert.Equal(42d, second);
    }

    [Fact]
    public void OldSessionCallbacksCannotPopulateAStoppedSession()
    {
        var inputs = new RheoSenseInputSubscriptions();
        bool applied = false;
        inputs.Add("urn:test", "value", _ => applied = true);
        Action<object?>? receive = null;
        inputs.Start((_, callback) => receive = callback);
        receive!(1d);
        inputs.Stop();
        receive(2d);
        Assert.Equal(0, inputs.ApplyPending());
        Assert.False(applied);
    }

    [Fact]
    public async Task SilentInputsAllowWatchdogAndShutdownWithoutPolling()
    {
        var inputs = new RheoSenseInputSubscriptions();
        Assert.False(await inputs.WaitAsync(TimeSpan.Zero, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inputs.WaitAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public void NotificationsPreserveConversionsConstantsDisabledTagsAndUncertaintyIsolation()
    {
        var tags = RheoSenseOPCUATagSet.CreateDefault("urn:test", "raw.", "sigma.");
        foreach (var tag in tags.Signals.Values.Concat(tags.StandardDeviationSignals.Values)) tag.Enabled = false;
        tags.Signals[nameof(RheoSenseInstrumentData.MeasuredTemperature)].Enabled = true;
        tags.StandardDeviationSignals[nameof(RheoSenseInstrumentData.MeasuredTemperature)].Enabled = true;
        tags.Signals[nameof(RheoSenseInstrumentData.ExtrapolatedPressure)].Enabled = true;
        tags.Signals[nameof(RheoSenseInstrumentData.ExtrapolatedPressure)].ConstantValue = 123d;
        tags.UnitConversions[nameof(RheoSenseInstrumentData.MeasuredTemperature)] = new UnitConversion { ConversionOffset = -273.15 };
        var raw = new RheoSenseInstrumentData("urn:test", "raw.");
        var sigma = new RheoSenseInstrumentData("urn:test", "sigma.", false);
        var inputs = new RheoSenseInputSubscriptions();
        TestWorker.Tags(inputs, raw, tags, false);
        TestWorker.Tags(inputs, sigma, tags, true);
        var callbacks = new Dictionary<string, Action<object?>>();
        inputs.Start((node, callback) => callbacks.Add(node.Id, callback));
        Assert.Equal(2, callbacks.Count);
        callbacks["raw.MeasuredTemperature"](20f);
        callbacks["sigma.MeasuredTemperature"](0.5d);
        inputs.ApplyPending();
        Assert.Equal(293.15, raw.MeasuredTemperature!.ScalarValue!.Value, 8);
        Assert.Equal(0.5, sigma.MeasuredTemperature!.ScalarValue);
        Assert.Equal(123d, raw.ExtrapolatedPressure!.ScalarValue);
        Assert.Null(raw.MeasuredPressure!.ScalarValue);
        callbacks["raw.MeasuredTemperature"]("invalid");
        inputs.ApplyPending();
        Assert.Null(raw.MeasuredTemperature.ScalarValue);
        Assert.Equal(0.5, sigma.MeasuredTemperature.ScalarValue);
    }

    [Fact]
    public void CachedHeartbeatExpiresDuringSilenceAndRecoversOnNewNotifications()
    {
        var inputs = new RheoSenseInputSubscriptions();
        var states = new RheoSenseStateData(RheometerSource.Defaults()[0], new());
        TestWorker.States(inputs, states);
        Action<object?>? heartbeat = null;
        inputs.Start((node, receive) => { if (node.Id.EndsWith("State_RunningHeartbeat")) heartbeat = receive; });
        var monitor = new RunningHeartbeatMonitor();
        var time = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(10);
        heartbeat!(1d); inputs.ApplyPending();
        Assert.False(monitor.Observe(states.RunningHeartbeat!.ScalarValue, time, timeout));
        heartbeat(2d); inputs.ApplyPending();
        Assert.True(monitor.Observe(states.RunningHeartbeat.ScalarValue, time.AddSeconds(1), timeout));
        Assert.False(monitor.Observe(states.RunningHeartbeat.ScalarValue, time.AddSeconds(12), timeout));
        heartbeat(1d); inputs.ApplyPending();
        Assert.True(monitor.Observe(states.RunningHeartbeat.ScalarValue, time.AddSeconds(13), timeout));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void WatchdogIntervalMustRespectHeartbeatTimeout(int seconds) =>
        Assert.Throws<InvalidOperationException>(() => new ConfigurationForRheoSense { SubscriptionWatchdogInterval = TimeSpan.FromSeconds(seconds) }.ValidateSources());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void InputCadenceMustBePositiveAndFasterThanHeartbeatTimeout(int milliseconds)
    {
        var configuration = new ConfigurationForRheoSense { InputSubscriptionInterval = TimeSpan.FromMilliseconds(milliseconds) };
        if (milliseconds >= 10000) Assert.Throws<InvalidOperationException>(configuration.ValidateSources);
        else Assert.Throws<ArgumentOutOfRangeException>(configuration.ValidateSources);
    }

    private sealed class TestWorker() : RheoSenseWorker<ConfigurationForRheoSense>(null!, null)
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        public static void Tags(RheoSenseInputSubscriptions inputs, RheoSenseInstrumentData data, RheoSenseOPCUATagSet tags, bool sigma) => ObserveTagSet(inputs, data, tags, sigma);
        public static void States(RheoSenseInputSubscriptions inputs, RheoSenseStateData states) => ObserveStates(inputs, states);
    }
}

