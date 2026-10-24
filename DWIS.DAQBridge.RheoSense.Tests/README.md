# DWIS.DAQBridge.RheoSense.Tests

xUnit regression tests for the shared model and bridge runtime. The
project is not packaged or deployed.

## Covered behavior

- Separation of OPC-UA transport models from semantic Blackboard outputs.
- Complete 36-value and 34-deviation tag sets for both sensors.
- Per-signal configuration defaults and independent sensor overrides.
- Active-pit and return-flow semantic identity separation.
- Mullineux YPL fitting and Zamora/Kelessidis fallback.
- Physical and simulated Fann 35 curve generation and serialization.
- Conformance of flow-curve distributions, SI angular velocity, and semantic
  SchemaURL/MetaDataURL fields to the shared `Rheometry/v1` contract.
- Independent condition and rheology change detection.
- Missing and zero standard-deviation handling.
- Compact logging, UTC normalization, compression, retention, and sensor
  separation.
- Eight-bucket, 48-hour Blackboard history layout.

Filesystem tests create isolated temporary directories and do not use the
repository's `home` directory or deployed log storage.

## Run

```powershell
dotnet test .\DWIS.DAQBridge.RheoSense.Tests\DWIS.DAQBridge.RheoSense.Tests.csproj
```

For an already built Release configuration:

```powershell
dotnet test .\DWIS.DAQBridge.RheoSense.Tests\DWIS.DAQBridge.RheoSense.Tests.csproj -c Release --no-build --no-restore
```

Instrument identity tests also verify model fields and links in runtime manifests for both positions.


Runtime manifests now describe individual curve representations, derivation inputs,
correction purpose, compatibility targets and independent p/T conditions. See the
[rheometry semantic contract](../RHEOMETRY-SEMANTICS.md).

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

SharedContractTests verifies JSON configuration with explicit contract host/port and path prefixes, invalid endpoint rejection, and payload interoperability with DWIS.RigOS.Contracts.Rheometry 1.0.7.


Flow-curve manifests use `JSonDataType` as their primary node type so the
Blackboard accepts its `SchemaURL` and `MetaDataURL` fields. `ComputedData` and
`FlowCurve` remain additional semantic classifications. The injector validates
fields against the primary type; merely including a secondary JSON classification
does not publish these fields. Restart the updated bridge to reinject its existing
identities; no Blackboard database or configuration volume reset is needed.

`InputSubscriptionTests` covers callback/worker separation, burst coalescing,
shared-node fanout, stopped-session callback isolation, cancellation, conversions,
constant/disabled tags, independent uncertainties, heartbeat timeout/recovery and
watchdog interval validation.
