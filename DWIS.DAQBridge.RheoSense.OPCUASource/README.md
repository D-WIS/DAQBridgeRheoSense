# DWIS.DAQBridge.RheoSense.OPCUASource

A synthetic OPC-UA data source for local development and end-to-end testing. It
represents the enabled entries in `Sources`. Defaults are active pit and return
flow between the annulus outlet and the shale shakers. All seven placement profiles
are listed in the example configuration.

## Published data

For each sensor the source registers and publishes:

- 40 numerical value tags.
- 38 numerical standard-deviation tags.
- No standard deviations for selected extrapolated pressure and temperature.

Pressure, temperature, and the two density values are generated independently
from rheology. The sensors use different random sequences and phase offsets to
produce distinct values.

Default timing is:

- New condition values every 10 seconds.
- New rheology values every 30 minutes.
- Republish current rheology values every 5 minutes.

These periods are controlled by `SimulationConditionGenerationPeriod`,
`SimulationDataGenerationPeriod`, and `SimulationOPCUAPublishPeriod` in
the runtime `home/config.json`. The tracked
[`config/config.example.json`](../config/config.example.json) provides the
initial values. `SimulationSeed` makes a run repeatable.

## OPC-UA addresses

The source uses the same `ActivePitTags` and `FlowLineTags` per-signal maps as
the production server. Test values are therefore written to the exact endpoints
the server reads. The default endpoint is `opc.tcp://localhost:48030`.

## Run locally

Start an OPC-UA server at the configured endpoint, then run:

```powershell
dotnet run --project .\DWIS.DAQBridge.RheoSense.OPCUASource\DWIS.DAQBridge.RheoSense.OPCUASource.csproj
```

The source keeps retrying if its initial connection cannot be established. No container restart is needed when the endpoint becomes available.

## Docker

```powershell
docker build -f DWIS.DAQBridge.RheoSense.OPCUASource/Dockerfile -t rheosense-opcua-source .
```

Mount shared configuration at `/home`. The published Docker Hub image is
`digiwells/dwisdaqbridgerheosenseopcuasource`.

## Configurable installations

One attributed instrument model is composed with placement profiles. The source,
bridge and sink process the enabled `Sources` list in `home/config.json`.
See [placement profiles and configuration](../PLACEMENT-PROFILES.md) for defaults, tag mappings,
validation, identities and restart behavior.

Mean and sigma variables share the installation namespace, with `Sigma_` marking
the latter. Synthetic initialization strips the already configured prefix before
registration, avoiding doubled prefixes. External instrument mappings require
`InitializeOPCUAVariables: false` in the bridge.

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

## Connection recovery

The source retries startup failures, dropped connections, registration failures and transport/write failures. Each retry disposes the previous client/session, opens a fresh session, re-registers all enabled variables and republishes the current values. It uses `ConnectionRetryInterval` from `/home/config.json` (default `"00:00:05"`; nonpositive values fall back to five seconds). Retry waits and periodic publication stop promptly on cancellation. Configuration, tag mappings and mounted data are not rewritten by recovery.

Synthetic samples and random-generator state remain in memory across reconnects. An outage does not buffer or replay historical samples; publication resumes with current simulated values. A process restart still initializes a new simulation. Changes to sources or tag mappings require a process restart.

The condition/rheology generation and republication schedules, simulation start time and operating-hours clock survive reconnects. Reconnection restores current rheology and heartbeat/state values without waiting for the next five-minute republication or resetting the thirty-minute generation schedule.
