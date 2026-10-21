using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;
using System.Reflection;
namespace DWIS.DAQBridge.RheoSense.BlackboardSink;
public class Worker(ILogger<IDWISWorker<ConfigurationForRheoSense>> logger, ILogger<DWISClientOPCF>? clientLogger)
    : RheoSenseWorker<ConfigurationForRheoSense>(logger, clientLogger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectToBlackboard();
        if (_DWISClient?.Connected != true || Configuration is null) return;
        var sources = new List<(RheometerSource Source, RheometerOutputData Data, Dictionary<PropertyInfo, Dictionary<string, Guid>> Bindings)>();
        foreach (var source in Configuration.GetSources())
        {
            var output = new RheometerOutputData(source);
            RegisteredResultQueries = new();
            await RegisterQueries(output);
            sources.Add((source, output, RegisteredResultQueries));
        }
        using var timer = new PeriodicTimer(LoopSpan > TimeSpan.Zero ? LoopSpan : TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            foreach (var source in sources)
            {
                RegisteredResultQueries = source.Bindings;
                await ReadBlackboardAsync(source.Data, stoppingToken);
                Logger?.LogInformation("RheoSense {Source}: pressure={Pressure} Pa", source.Source.Id, source.Data.MeasuredPressure?.Mean);
            }
    }
}
