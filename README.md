# D-WIS Bridge for RheoSense

This solution bridges a configured list of RheoSense inline pipe rheometers into
the D-WIS Blackboard. Defaults are the active pit and the return flow line between
the annulus outlet and the shale shakers. These
sensors use the same engineering data types, while their OPC-UA endpoints,
semantic identities, measurement devices, hydraulic locations, refresh state,
logs, and history remain separate.

All canonical engineering and persisted values use SI units. All timestamps are
stored as UTC instants.

## Solution projects

| Project | Purpose |
| --- | --- |
| [Model](DWIS.DAQBridge.RheoSense.Model/README.md) | Shared OPC-UA input contract, semantic outputs, rheology mapping, configuration, compact logging, and Blackboard history. |
| [Server](DWIS.DAQBridge.RheoSense.Server/README.md) | Production bridge from configured RheoSense OPC-UA tag sets to semantic Blackboard outputs. |
| [OPCUASource](DWIS.DAQBridge.RheoSense.OPCUASource/README.md) | Synthetic two-sensor OPC-UA source for development and end-to-end testing. |
| [BlackboardSink](DWIS.DAQBridge.RheoSense.BlackboardSink/README.md) | Diagnostic semantic consumer used to verify Blackboard publication. |
| [Tests](DWIS.DAQBridge.RheoSense.Tests/README.md) | xUnit coverage for mapping, semantics, configuration, logging, and history. |

## Data flow

1. Each RheoSense instrument exposes numerical values through its configured
   OPC-UA tags. Means and standard deviations use separate tags.
2. The server subscribes to enabled OPC-UA tags and wakes on data-change callbacks.
   It processes only a sensor channel whose values changed since publication.
3. Numerical shear-stress points are fitted to a Yield-Power-Law model using
   Mullineux, with Zamora/Kelessidis as fallback.
4. The server publishes the fitted physical flow curve and simulated Fann 35
   Couette response, together with fitted YPL parameters and condition data.
5. Compact raw and published records are written to disk and retained in
   rotating 48-hour OPC-UA history buckets on the Blackboard.
6. DAQDisplay provides the shared rheometry display and interprets provider data
   using the schema and metadata URLs advertised on the Blackboard.

The standalone RheoSense dashboard has been retired; visualization belongs to DAQDisplay.

RheoSense supplies no flow-curve object over OPC-UA. The curves are bridge
outputs constructed from the ten shear-stress values at 3, 6, 10, 20, 30, 60,
100, 200, 300, and 600 RPM.

Flow-curve JSON follows the vendor-neutral `Rheometry/v1` contract shared with
BaraLogix and future online rheometers. The contract uses a Dirac distribution
for angular velocity and Gaussian distributions for shear rates, torque, and
shear stresses. Runtime manifests publish its canonical schema and metadata
URLs at `https://dwis.digiwells.no/Rheometry/v1/`. Angular velocity is serialized
in SI `rad/s`; other curve values use `1/s`, `N.m`, and `Pa` as applicable.

## Shared configuration

The worker applications use the untracked runtime file
`home/config.json`. In Linux containers it is located at `/home/config.json`;
mount `/home` as a persistent volume. Existing configurations may retain the retired
`WebAppTimeSpanHours` setting; workers ignore it and no migration is required. Copy
[`config/config.example.json`](config/config.example.json) to
`home/config.json` for an initial local configuration. Important settings
include:

- `OPCUAURL`: RheoSense OPC-UA endpoint, default
  `opc.tcp://localhost:48030`.
- `GeneralBlackboard`: D-WIS Blackboard OPC-UA endpoint.
- `ContractsPublicBaseUrl`: public base URL of the shared schema and metadata
  service, default `https://dwis.digiwells.no/`.
- `ActivePitTags` and `FlowLineTags`: independent per-signal namespace, complete
  node ID, and optional unit-conversion maps.
- `SubscriptionWatchdogInterval`: cached heartbeat/connection checks (default one
  second, positive and no longer than `HeartbeatTimeout`); no recurring tag reads.
- `ConnectionRetryInterval`: recovery delay. `OPCUALoopDuration` remains available
  for source/sink helpers but no longer controls production-server acquisition.
- `EnableDataLogging`, `DataLogDirectory`, `DataLogSegmentDuration`, and
  `MaximumDataLogStorageBytes`: compact-log behavior.
- `SimulationConditionGenerationPeriod`, `SimulationDataGenerationPeriod`, and
  `SimulationOPCUAPublishPeriod`: synthetic-source timing.

There are 40 value endpoints and 38 optional standard-deviation endpoints per
sensor. `ExtrapolatedTemperature` and `ExtrapolatedPressure` are chosen
calculation parameters, not measurements, and therefore have no deviation
tags. Missing signal entries or fields receive sensor-specific defaults. The
per-signal maps are the sole configurable source of namespaces and node IDs.

Raw OPC-UA tag names containing `Extraplated` retain the instrument's spelling.
Semantic output names use `Extrapolated`.

## Connection recovery and change detection

The production server starts its exchange loop only after both OPC-UA and
Blackboard connections are established. It waits and retries if either service
is unavailable. A lost connection stops the exchange loop; processing resumes
only after both connections recover and semantic outputs are registered again.

Each sensor independently tracks condition data and rheology data. Re-reading
unchanged OPC-UA values does not cause calculation, disk logging, history
updates, or Blackboard publication.

## Logs and history

The server writes four compact JSON Lines streams beneath `DataLogDirectory`:

- `rheosense-active-pit-raw-*`
- `rheosense-active-pit-published-*`
- `rheosense-flow-line-raw-*`
- `rheosense-flow-line-published-*`

Each record contains a UTC timestamp, refreshed channel, and streamlined values
without semantic metadata or SPARQL queries. Completed six-hour files are
zipped. Total storage defaults to 1 GiB; oldest files are permanently deleted
when necessary to enforce the limit.

The server also stores raw and published entries in eight rotating six-hour
buckets per sensor and data kind on the OPC-UA part of the Blackboard. Node IDs
follow `RheoSense.<ActivePit|FlowLine>.<Raw|Published>.History.<0-7>`. Published
and raw buckets remain available to consumers and diagnostic tools.

## Build and test

Prerequisites are the .NET 8 SDK and, for container builds, Docker.

```powershell
New-Item -ItemType Directory -Force .\home | Out-Null
Copy-Item .\config\config.example.json .\home\config.json
dotnet restore .\DWIS.DAQBridge.RheoSense.sln
dotnet build .\DWIS.DAQBridge.RheoSense.sln --no-restore
dotnet test .\DWIS.DAQBridge.RheoSense.sln --no-build
```

## Typical local flow

1. Start an OPC-UA server and a D-WIS Blackboard using the addresses in
   `home/config.json`.
2. Run `DWIS.DAQBridge.RheoSense.OPCUASource` to publish synthetic input data.
3. Run `DWIS.DAQBridge.RheoSense.Server`.
4. Optionally run `DWIS.DAQBridge.RheoSense.BlackboardSink` for console-level
   semantic verification.
5. Use the Online rheometry page in DAQDisplay to view the published data.

## Docker images

The workflow in
[`.github/workflows/docker-build-push.yml`](.github/workflows/docker-build-push.yml)
runs on pushes to `main` and by manual dispatch. It publishes both `stable` and
immutable `sha-<commit>` tags for:

- `digiwells/dwisdaqbridgerheosenseserver`
- `digiwells/dwisdaqbridgerheosenseopcuasource`

The repository requires `DOCKERHUB_USERNAME` and `DOCKERHUB_PASSWORD` GitHub
Actions secrets. Docker builds use the repository root as their context.

### Run on Windows

From PowerShell, create the shared host directory and copy the initial
configuration into it:

```powershell
New-Item -ItemType Directory -Force C:\Volumes\DWISBridgeRheoSense | Out-Null
Copy-Item .\config\config.example.json C:\Volumes\DWISBridgeRheoSense\config.json
```

Before starting the containers, edit
`C:\Volumes\DWISBridgeRheoSense\config.json` so `OPCUAURL` and
`GeneralBlackboard` are reachable from Docker. When those endpoints run on the
Windows host, use `host.docker.internal` instead of `localhost`.

Run the synthetic OPC-UA source:

```powershell
docker run -d --name rheosense-opcua-source --restart unless-stopped --pull always `
  --mount "type=bind,source=C:/Volumes/DWISBridgeRheoSense,target=/home" `
  digiwells/dwisdaqbridgerheosenseopcuasource:stable
```

Run the production bridge server:

```powershell
docker run -d --name rheosense-server --restart unless-stopped --pull always `
  --mount "type=bind,source=C:/Volumes/DWISBridgeRheoSense,target=/home" `
  digiwells/dwisdaqbridgerheosenseserver:stable
```

### Run on Linux

Create the shared host directory and install the initial configuration:

```bash
mkdir -p "$HOME/Volumes/DWISBridgeRheoSense"
cp ./config/config.example.json "$HOME/Volumes/DWISBridgeRheoSense/config.json"
```

Edit `$HOME/Volumes/DWISBridgeRheoSense/config.json` before starting the
containers. Then grant the container user ownership so the server can write
its compact data logs:

```bash
sudo chown -R 1654:1654 "$HOME/Volumes/DWISBridgeRheoSense"
```

If OPC-UA or Blackboard runs on the Linux host, use `host.docker.internal` in
its URL. The commands below add that hostname to each container.

Run the synthetic OPC-UA source:

```bash
docker run -d --name rheosense-opcua-source --restart unless-stopped --pull always \
  --add-host host.docker.internal:host-gateway \
  --mount type=bind,source="$HOME/Volumes/DWISBridgeRheoSense",target=/home \
  digiwells/dwisdaqbridgerheosenseopcuasource:stable
```

Run the production bridge server:

```bash
docker run -d --name rheosense-server --restart unless-stopped --pull always \
  --add-host host.docker.internal:host-gateway \
  --mount type=bind,source="$HOME/Volumes/DWISBridgeRheoSense",target=/home \
  digiwells/dwisdaqbridgerheosenseserver:stable
```

## DWIS dependency and semantic update

The bridge now uses Common Worker/Model 1.0.7, OPC Foundation client 2.2.53,
DWIS vocabulary 1.0.176 and OSDC semantic-property framework 10.2.11 (latest stable
versions verified from NuGet on 22 October 2026). Existing signal identities and
persistent paths are retained. Instrument device nodes reference an InstrumentModel
with ModelName and confirmed ModelIdentifier fields, using the same mechanism as BaseStar.


Runtime manifests now describe individual curve representations, derivation inputs,
correction purpose, compatibility targets and independent p/T conditions. See the
[rheometry semantic contract](RHEOMETRY-SEMANTICS.md).

## Configurable installations

One attributed instrument model is composed with placement profiles. The source,
bridge and sink process the enabled `Sources` list in `home/config.json`.
See [placement profiles and configuration](PLACEMENT-PROFILES.md) for defaults, tag mappings,
validation, identities and restart behavior.

## OPC-UA units and IMS tag-list review

See [unit configuration and commissioning findings](IMS-TAG-REVIEW.md).
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

Semantic model projects explicitly reference OSDC UnitConversion Conversion and
Conversion.DrillingEngineering 3.4.5, matching the display host's catalog. Rebuild
the semantic models together when changing catalog versions: enum values embedded
in C# attributes must agree with the runtime catalog used by the generator. This
alignment changes no OSDC catalog definitions and publishes no packages.

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


Flow-curve manifests use `JSonDataType` as their primary node type so the
Blackboard accepts its `SchemaURL` and `MetaDataURL` fields. `ComputedData` and
`FlowCurve` remain additional semantic classifications. The injector validates
fields against the primary type; merely including a secondary JSON classification
does not publish these fields. Restart the updated bridge to reinject its existing
identities; no Blackboard database or configuration volume reset is needed.

The Blackboard must use vocabulary 1.0.176 or newer for these JSON fields.
The bridge refreshes output manifests on startup even if matching signals already
exist, so an upgrade adds missing contract fields to existing provider identities.

`InputSubscriptionInterval` sets both OPC-UA sampling and publishing cadence
(default 500 ms); it must be positive and shorter than `HeartbeatTimeout`. Choose
an interval shorter than the PLC heartbeat dwell time so alternating states are
observed. The server requires OPC Foundation client 2.2.54 for this setting.

The query lifecycle dependency release uses OPC Foundation client 2.2.55 (directly or through mechanical bridge 1.0.6) and Common.Worker 1.0.8 where applicable. Repeated query registrations are deduplicated and released when their session closes. Existing configuration and persistent volumes are preserved.
