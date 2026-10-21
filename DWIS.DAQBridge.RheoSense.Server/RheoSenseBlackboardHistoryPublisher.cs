using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation;
using DWIS.DAQBridge.RheoSense.Model;

namespace DWIS.DAQBridge.RheoSense.Server;

internal sealed class RheoSenseBlackboardHistoryPublisher
{
    private readonly Dictionary<string, string?> _payloads = new(StringComparer.Ordinal);
    private IOPCUADWISClient? _client;

    public void Initialize(IOPCUADWISClient client, IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(client);
        ManifestInjectionResult? result = client.Inject(RheoSenseBlackboardHistory.CreateManifest(sources));
        if (result?.Success != true || result.ProvidedVariables is null)
        {
            throw new InvalidOperationException("The RheoSense Blackboard history variables could not be registered.");
        }

        _client = client;
        _payloads.Clear();
        foreach (InjectionMapping mapping in result.ProvidedVariables)
        {
            string? payload = null;
            if (mapping.InjectedID is not null &&
                client.ReadAnyVariables(
                    out object[] values,
                    new[] { (mapping.InjectedID.NameSpaceIndex, mapping.InjectedID.ID) }) &&
                values.Length > 0)
            {
                payload = values[0] as string;
            }
            _payloads[mapping.ManifestItemID] = payload;
        }
    }

    public void Append(
        string sensor,
        RheoSenseHistoryDataKind kind,
        DateTimeOffset timestampUtc,
        RheoSenseCompactLogEntry entry)
    {
        if (_client?.Connected != true)
        {
            throw new InvalidOperationException("The Blackboard is unavailable while updating RheoSense history.");
        }

        DateTimeOffset bucketStart = RheoSenseBlackboardHistory.GetBucketStart(timestampUtc);
        int slot = RheoSenseBlackboardHistory.GetBucketSlot(bucketStart);
        string nodeId = RheoSenseBlackboardHistory.GetNodeId(sensor, kind, slot);
        _payloads.TryGetValue(nodeId, out string? existingPayload);
        string payload = RheoSenseBlackboardHistory.Append(existingPayload, timestampUtc, entry);
        bool updated = _client.UpdateProvidedVariables(new List<(string, object, DateTime)>
        {
            (nodeId, payload, timestampUtc.UtcDateTime)
        });
        if (!updated)
        {
            throw new InvalidOperationException($"The Blackboard rejected RheoSense history update '{nodeId}'.");
        }

        _payloads[nodeId] = payload;
    }
}
