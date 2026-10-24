using System.Reflection;
using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SourceWorker = DWIS.DAQBridge.RheoSense.OPCUASource.Worker;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class SourceReconnectTests
{
    private sealed class TestWorker : SourceWorker
    {
        private readonly Queue<IOPCUADWISClient> clients;
        public CancellationTokenSource Stop { get; } = new();
        public int Attempts { get; private set; }
        public int Retries { get; private set; }
        public Action<int>? Tick { get; set; }
        public Action<int>? Retry { get; set; }
        public bool UseRealRetryDelay { get; set; }
        public DateTime? Clock { get; set; }
        protected override DateTime UtcNow => Clock ?? base.UtcNow;
        private int ticks;
        public TestWorker(params IOPCUADWISClient[] clients)
            : base(NullLogger<IDWISWorker<ConfigurationForRheoSense>>.Instance, null)
        {
            this.clients = new(clients);
            Configuration = new() { Sources = new() { new() { Id = "RecoveryTest", Placement = PlacementProfile.ActivePit } } };
        }
        public Task RunAsync() => ExecuteAsync(Stop.Token);
        protected override IOPCUADWISClient? OpenSourceClient()
        {
            Attempts++;
            return clients.Dequeue();
        }
        protected override Task<bool> WaitForTickAsync(PeriodicTimer timer, CancellationToken token)
        {
            Tick?.Invoke(++ticks);
            if (Tick is null) Stop.Cancel();
            return Task.FromResult(!token.IsCancellationRequested);
        }
        protected override Task WaitBeforeReconnectAsync(CancellationToken token)
        {
            Retries++;
            Retry?.Invoke(Retries);
            return UseRealRetryDelay ? base.WaitBeforeReconnectAsync(token) : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task UnavailableStartupRetriesAndPublishesWithoutRestart()
    {
        var first = SourceClient.New(false); var second = SourceClient.New(false); var ready = SourceClient.New();
        using var worker = new TestWorker(first.Client, second.Client, ready.Client);
        await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, worker.Attempts); Assert.Equal(2, worker.Retries);
        Assert.Empty(first.Values); Assert.Empty(second.Values);
        Assert.NotEmpty(ready.Manifests); Assert.NotEmpty(ready.Values);
        Assert.All(new[] { first, second, ready }, client => Assert.Equal(1, client.Disposals));
    }

    [Fact]
    public async Task LostConnectionRecreatesSessionRegistersAndRestoresValues()
    {
        var first = SourceClient.New(); var recovered = SourceClient.New();
        using var worker = new TestWorker(first.Client, recovered.Client);
        worker.Tick = tick => { if (tick == 2) first.Connected = false; else if (tick > 2) worker.Stop.Cancel(); };
        await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, worker.Attempts); Assert.Equal(1, worker.Retries);
        Assert.Equal(first.Manifests.Select(m => m.ManifestName), recovered.Manifests.Select(m => m.ManifestName));
        Assert.NotEmpty(first.Values);
        Assert.Equal(first.Values.Keys.Order(), recovered.Values.Keys.Order());
        foreach (string id in first.Values.Keys) Assert.Equal(first.Values[id], recovered.Values[id]);
        Assert.Equal(1, first.Disposals); Assert.Equal(1, recovered.Disposals);
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("false")]
    [InlineData("registration")]
    public async Task FailedRegistrationOrPublicationIsRetried(string failure)
    {
        var first = SourceClient.New(); var ready = SourceClient.New();
        first.Failure = failure;
        using var worker = new TestWorker(first.Client, ready.Client);
        await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, worker.Attempts); Assert.Equal(1, worker.Retries);
        Assert.NotEmpty(ready.Values); Assert.Equal(1, first.Disposals); Assert.Equal(1, ready.Disposals);
    }

    [Fact]
    public async Task CancellationInterruptsTheRealRetryDelayAndDisposesFailedSession()
    {
        var failed = SourceClient.New(false);
        using var worker = new TestWorker(failed.Client) { UseRealRetryDelay = true };
        worker.Configuration!.ConnectionRetryInterval = TimeSpan.FromMinutes(5);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.Retry = _ => waiting.SetResult();
        Task run = worker.RunAsync();
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        worker.Stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, worker.Attempts); Assert.Equal(1, failed.Disposals);
    }

    [Fact]
    public async Task ReconnectRetainsGeneratedRheologyAndSimulationClock()
    {
        var first = SourceClient.New(); var recovered = SourceClient.New();
        using var worker = new TestWorker(first.Client, recovered.Client) { Clock = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc) };
        object? initialStress = null;
        worker.Tick = tick =>
        {
            if (tick == 1)
            {
                initialStress = first.Values.Single(p => p.Key.EndsWith(".ShearStressAt3RPMAtMeasuredTemperatureAndPressure") && !p.Key.Contains("Sigma_")).Value;
                worker.Clock = worker.Clock!.Value.AddMinutes(31);
            }
            else if (tick == 2) first.Connected = false;
            else worker.Stop.Cancel();
        };
        await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        string stress = first.Values.Keys.Single(id => id.EndsWith(".ShearStressAt3RPMAtMeasuredTemperatureAndPressure") && !id.Contains("Sigma_"));
        Assert.NotEqual(initialStress, first.Values[stress]);
        Assert.Equal(first.Values[stress], recovered.Values[stress]);
        string hours = first.Values.Keys.Single(id => id.EndsWith(".State_OperatingHours"));
        Assert.True((double)recovered.Values[hours]! > 0.5);
        Assert.Equal(first.Values[hours], recovered.Values[hours]);
        Assert.Equal(2, worker.Attempts);
    }

    [Fact]
    public async Task AlreadyCancelledDoesNotOpenAConnection()
    {
        using var worker = new TestWorker(); worker.Stop.Cancel();
        await worker.RunAsync(); Assert.Equal(0, worker.Attempts);
    }
}

public class SourceClient : DispatchProxy, IDisposable
{
    public IOPCUADWISClient Client { get; private set; } = null!;
    public bool Connected { get; set; } = true;
    public string? Failure { get; set; }
    public int Disposals { get; private set; }
    public List<ManifestFile> Manifests { get; } = new();
    public Dictionary<string, object?> Values { get; } = new();
    public static SourceClient New(bool connected = true)
    {
        var client = Create<IOPCUADWISClient, SourceClient>();
        var state = (SourceClient)(object)client;
        state.Client = client; state.Connected = connected; return state;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == "get_Connected") return Connected;
        if (method.Name == "Inject")
        {
            Manifests.Add((ManifestFile)arguments![0]!);
            return new ManifestInjectionResult { Success = Failure != "registration" };
        }
        if (method.Name == "UpdateAnyVariables")
        {
            if (Failure == "exception") throw new IOException("Simulated transport outage");
            if (Failure == "false") return false;
            foreach (var (ns, id, value, _) in ((string, string, object, DateTime)[])arguments![0]!) Values[ns + "|" + id] = value;
            return true;
        }
        throw new NotSupportedException(method.Name);
    }
    public void Dispose() { Connected = false; Disposals++; }
}
