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
| [WebApp](DWIS.DAQBridge.RheoSense.WebApp/README.md) | MudBlazor dashboard for conditions, fitted YPL parameters, and flow curves. |
| [Tests](DWIS.DAQBridge.RheoSense.Tests/README.md) | xUnit coverage for mapping, semantics, configuration, logging, history, and WebApp data preparation. |

## Data flow

1. Each RheoSense instrument exposes numerical values through its configured
   OPC-UA tags. Means and standard deviations use separate tags.
2. The server polls enabled sensors but processes only a sensor channel whose data
   changed since its last successful publication.
3. Numerical shear-stress points are fitted to a Yield-Power-Law model using
   Mullineux, with Zamora/Kelessidis as fallback.
4. The server publishes the fitted physical flow curve and simulated Fann 35
   Couette response, together with fitted YPL parameters and condition data.
5. Compact raw and published records are written to disk and retained in
   rotating 48-hour OPC-UA history buckets on the Blackboard.
6. The WebApp reads published Blackboard history and presents unit- and
   time-reference-aware trends and rheograms.

RheoSense supplies no flow-curve object over OPC-UA. The curves are bridge
outputs constructed from the ten shear-stress values at 3, 6, 10, 20, 30, 60,
100, 200, 300, and 600 RPM.

## Shared configuration

The worker applications and WebApp use the untracked runtime file
`home/config.json`. In Linux containers it is located at `/home/config.json`;
mount `/home` as a persistent volume. Copy
[`config/config.example.json`](config/config.example.json) to
`home/config.json` for an initial local configuration. Important settings
include:

- `OPCUAURL`: RheoSense OPC-UA endpoint, default
  `opc.tcp://localhost:48030`.
- `GeneralBlackboard`: D-WIS Blackboard OPC-UA endpoint.
- `ActivePitTags` and `FlowLineTags`: independent per-signal namespace, complete
  node ID, and optional unit-conversion maps.
- `OPCUALoopDuration` and `ConnectionRetryInterval`: polling and recovery
  periods.
- `EnableDataLogging`, `DataLogDirectory`, `DataLogSegmentDuration`, and
  `MaximumDataLogStorageBytes`: compact-log behavior.
- `WebAppTimeSpanHours`: persisted dashboard window from 1 to 48 hours.
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
follow `RheoSense.<ActivePit|FlowLine>.<Raw|Published>.History.<0-7>`. The WebApp
reads the 16 published buckets; raw buckets remain available for diagnostics.

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
5. Run `DWIS.DAQBridge.RheoSense.WebApp` and open
   `http://localhost:5080/RheoSense/webapp`.

The WebApp's OSDC unit and time-reference selector defaults to the unit service
at `https://app.digiwells.no/`. Override `UnitConversionHostURL` when another
service deployment is required.

## Docker images

The workflow in
[`.github/workflows/docker-build-push.yml`](.github/workflows/docker-build-push.yml)
runs on pushes to `main` and by manual dispatch. It publishes both `stable` and
immutable `sha-<commit>` tags for:

- `digiwells/dwisdaqbridgerheosenseserver`
- `digiwells/dwisdaqbridgerheosenseopcuasource`
- `digiwells/dwisdaqbridgerheosensewebapp`

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

Run the WebApp and publish it on host port 5080:

```powershell
docker run -d --name rheosense-webapp --restart unless-stopped --pull always `
  --mount "type=bind,source=C:/Volumes/DWISBridgeRheoSense,target=/home" `
  -p 5080:8080 `
  digiwells/dwisdaqbridgerheosensewebapp:stable
```

Open `http://localhost:5080/RheoSense/webapp` after the WebApp starts.

### Run on Linux

Create the shared host directory and install the initial configuration:

```bash
mkdir -p "$HOME/Volumes/DWISBridgeRheoSense"
cp ./config/config.example.json "$HOME/Volumes/DWISBridgeRheoSense/config.json"
```

Edit `$HOME/Volumes/DWISBridgeRheoSense/config.json` before starting the
containers. Then grant the container user ownership so the server can write
logs and the WebApp can persist preferences and data-protection keys:

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

Run the WebApp and publish it on host port 5080:

```bash
docker run -d --name rheosense-webapp --restart unless-stopped --pull always \
  --add-host host.docker.internal:host-gateway \
  --mount type=bind,source="$HOME/Volumes/DWISBridgeRheoSense",target=/home \
  -p 5080:8080 \
  digiwells/dwisdaqbridgerheosensewebapp:stable
```

Open `http://localhost:5080/RheoSense/webapp` after the WebApp starts.

## DWIS dependency and semantic update

The bridge now uses Common Worker/Model 1.0.6, OPC Foundation client 2.2.52,
DWIS vocabulary 1.0.175 and OSDC semantic-property framework 10.2.10 (latest stable
versions verified from NuGet on 20 October 2026). Existing signal identities and
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
