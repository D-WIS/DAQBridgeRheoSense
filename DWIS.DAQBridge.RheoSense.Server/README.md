# DWIS.DAQBridge.RheoSense.Server

The production worker bridges configured RheoSense instruments into the D-WIS
Blackboard. Defaults are active pit and return flow before the shale shakers.

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

The worker reads `/home/config.json` in the Linux container. Copy the repository's
[`config/config.example.json`](../config/config.example.json) to the untracked
`home/config.json` path for local execution.

Important settings include:

- `OPCUAURL`: RheoSense OPC-UA endpoint; default
  `opc.tcp://localhost:48030`.
- `GeneralBlackboard`: D-WIS Blackboard OPC-UA endpoint.
- `ContractsPublicBaseUrl`: public base URL used in the published `SchemaURL` and
  `MetaDataURL` fields; defaults to `https://dwis.digiwells.no/`.
- `ConnectionRetryInterval`: delay between connection attempts.
- `OPCUALoopDuration`: OPC-UA polling period.
- `InitializeOPCUAVariables`: register configured input nodes when enabled.
- `ActivePitTags` and `FlowLineTags`: per-signal namespace, complete node ID,
  and optional unit conversions.
- `EnableDataLogging`, `DataLogDirectory`, `DataLogSegmentDuration`, and
  `MaximumDataLogStorageBytes`: compact-log retention settings.

All 40 value endpoints and 38 optional deviation endpoints are independently
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

## Configurable installations

One attributed instrument model is composed with placement profiles. The source,
bridge and sink process the enabled `Sources` list in `home/config.json`.
See [placement profiles and configuration](../PLACEMENT-PROFILES.md) for defaults, tag mappings,
validation, identities and restart behavior.

## OPC-UA units and IMS tag-list review

See [unit configuration and commissioning findings](../IMS-TAG-REVIEW.md).
`UnitConversionServiceUrl` configures the OSDC API base URL. Per-tag `UnitName`
resolves conversion coefficients before session exchange; `Enabled: false`
suppresses unavailable inputs and `ConstantValue` supplies only fixed extrapolation
conditions. Existing numeric conversions and SI simulator defaults remain supported.
The two IMS example configs require commissioning and are disabled by default.

Gel-strength support includes 10 s and 10 min tests at actual and extrapolated
conditions, SI Pa conversion, source/target-specific history, and simulator values.
AdditionalExtrapolations reuses one semantic contract for multiple target temperatures
of the same instrument. The IMS combined template uses 50 C and 150 F, SG density,
and disabled PV/YP inputs. See IMS-TAG-REVIEW.md in the repository root.

Heartbeat supervision: `HeartbeatTimeout` in `../home/config.json` is a positive duration (default `"00:00:10"`). The bridge requires a change in `RunningHeartbeat` (1/2) before publishing. A frozen or unavailable heartbeat invalidates all current engineering outputs: Double nodes use NaN, JSON flow-curve nodes use the JSON text `null`. A heartbeat transition automatically republishes every output channel, including unchanged measurements. State mappings are configured per instrument in `StateSignals`: `Gel10SecondsActive`, `Gel10MinutesActive`, `Automatic`, `RunningHeartbeat`, `OperatingHours`, and `OperationTime`. They are read as separate PLC states, never rheology inputs or uncertainties. OperatingHours retains its vendor hour encoding; OperationTime has no documented encoding and is not interpreted. The simulator toggles RunningHeartbeat every second independently of measurement publication. Additional temperature targets share the physical instrument heartbeat.


## Shared rheometry NuGet and configurable contract endpoint

The model references `DWIS.RigOS.Contracts.Rheometry` **1.0.7**. JSON flow-curve points serialize and deserialize through its v1 payload type. Local measurement classes retain only the bridge-specific semantic annotation role; shared package constants own the contract version and document paths.

Set the public scheme, host, port and optional path prefix through the existing `ContractsPublicBaseUrl` setting in `home/config.json` (`/home/config.json` inside Linux containers):

```json
{
  "ContractsPublicBaseUrl": "http://contracts.example:54321/"
}
```

Merge this setting into the existing configuration; keep the instrument and Blackboard settings. It publishes `http://contracts.example:54321/Rheometry/v1/schema.json` and `http://contracts.example:54321/Rheometry/v1/metadata.json`. An HTTPS URL, IPv6 host, non-default port and path prefix are supported. The default remains `https://dwis.digiwells.no/`. Query strings, fragments, credentials, relative URLs and non-HTTP schemes are rejected.

This setting selects the URLs advertised to consumers; configure the shared contract service to listen on that port and make it reachable from consumers separately. Restart the bridge after changing the configuration.
