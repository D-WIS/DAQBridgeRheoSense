# DWIS.DAQBridge.RheoSense.Server

The production worker that bridges two independent RheoSense instruments into
the D-WIS Blackboard: one at the active pit and one in the return flow line.

## Processing loop

The worker waits until both the RheoSense OPC-UA endpoint and Blackboard are
connected. If either connection is lost, processing stops and returns to the
configured retry cycle. After reconnection it registers the semantic outputs
again before resuming.

Each sensor has two independently tracked refresh channels:

- Density, pressure, and temperature.
- Rheology, extrapolation parameters, YPL values, and shear stresses.

The endpoint may be polled frequently, but a channel is mapped, logged, and
published only when one of its values or available standard deviations changes.
A change from one sensor does not trigger work for the other sensor.

Rheology mapping fits the numerical pipe-rheometer curve and publishes both the
fitted physical curve and equivalent Fann 35 Couette curve. See the
[model documentation](../DWIS.DAQBridge.RheoSense.Model/README.md) for the fit
and conversion rules.

## Configuration

The worker reads `/home/config.json` in the Linux container. The repository's
[`home/config.json`](../home/config.json) is a complete example.

Important settings include:

- `OPCUAURL`: RheoSense OPC-UA endpoint; default
  `opc.tcp://localhost:48030`.
- `GeneralBlackboard`: D-WIS Blackboard OPC-UA endpoint.
- `ConnectionRetryInterval`: delay between connection attempts.
- `OPCUALoopDuration`: OPC-UA polling period.
- `InitializeOPCUAVariables`: register configured input nodes when enabled.
- `ActivePitTags` and `FlowLineTags`: per-signal namespace, complete node ID,
  and optional unit conversions.
- `EnableDataLogging`, `DataLogDirectory`, `DataLogSegmentDuration`, and
  `MaximumDataLogStorageBytes`: compact-log retention settings.

All 36 value endpoints and 34 optional deviation endpoints are independently
configurable for each sensor. Extrapolated pressure and temperature deliberately
have no deviation endpoints.

## Logging and Blackboard history

The server writes four compact JSON Lines streams: raw and published data for
each sensor. Records contain only the refreshed channel, UTC timestamp, and
compact numerical values. Completed six-hour segments are zipped by default,
and the oldest files are permanently deleted when the storage limit is exceeded.

Changed records are also placed in ordinary OPC-UA string variables on the
Blackboard. Each raw/published sensor stream uses eight rotating six-hour
buckets, retaining up to 48 hours. These history variables intentionally carry
no semantic graph.

## Run locally

Start the OPC-UA endpoint and Blackboard first, then run:

```powershell
dotnet run --project .\DWIS.DAQBridge.RheoSense.Server\DWIS.DAQBridge.RheoSense.Server.csproj
```

## Docker

Build from the repository root and mount a persistent volume at `/home`:

```powershell
docker build -f DWIS.DAQBridge.RheoSense.Server/Dockerfile -t rheosense-server .
```

The published Docker Hub image is
`digiwells/dwisdaqbridgerheosenseserver`.
