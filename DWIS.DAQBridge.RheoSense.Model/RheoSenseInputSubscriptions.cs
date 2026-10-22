namespace DWIS.DAQBridge.RheoSense.Model;

/// <summary>Coalesces transport notifications; only the worker applies values to its input models.</summary>
public sealed class RheoSenseInputSubscriptions
{
    public readonly record struct Node(string Namespace, string Id);
    private readonly object gate = new();
    private readonly Dictionary<Node, List<Action<object?>>> bindings = new();
    private readonly Dictionary<Node, object?> pending = new();
    private readonly SemaphoreSlim changed = new(0, 1);
    private bool stopped;

    public void Add(string ns, string id, Action<object?> apply)
    {
        var node = new Node(ns, id);
        if (!bindings.TryGetValue(node, out var targets)) bindings[node] = targets = new();
        targets.Add(apply);
    }

    public void Start(Action<Node, Action<object?>> subscribe)
    {
        foreach (var node in bindings.Keys)
            subscribe(node, value =>
            {
                lock (gate)
                {
                    if (stopped) return;
                    pending[node] = value;
                    if (changed.CurrentCount == 0) changed.Release();
                }
            });
    }

    public Task<bool> WaitAsync(TimeSpan watchdogInterval, CancellationToken token) => changed.WaitAsync(watchdogInterval, token);

    public int ApplyPending()
    {
        KeyValuePair<Node, object?>[] values;
        lock (gate) { values = pending.ToArray(); pending.Clear(); changed.Wait(0); }
        foreach (var (node, value) in values)
            foreach (var apply in bindings[node]) apply(value);
        return values.Length;
    }

    public void Stop()
    {
        lock (gate) { stopped = true; pending.Clear(); }
    }
}
