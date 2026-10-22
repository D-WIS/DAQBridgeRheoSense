# DWIS.DAQBridge.RheoSense.Model

Shared domain contracts and worker infrastructure for the RheoSense bridge.
This library is referenced by every executable in the solution and by the
tests. It is not an independently runnable application.

## Responsibilities

- Define the 40 numerical OPC-UA values produced by each RheoSense instrument.
- Keep active-pit and return-flow sensors separate through independent tag maps,
  semantic manifests, measurement devices, and hydraulic locations.
- Define signal semantics once in `RheometerOutputData`, composed with placement
  profiles for each enabled source.
- Convert raw instrument values into published distributions and flow curves.
- Fit Yield-Power-Law models and simulate the equivalent Fann 35 response.
- Provide compact disk-log and Blackboard-history formats.
- Provide common OPC-UA and Blackboard worker helpers.

## Input and output contracts

`RheoSenseInstrumentData` is an OPC-UA transport model. Its properties are
floating-point scalar values. Means and standard deviations are carried by
separate instances and therefore by separate OPC-UA tags. The chosen
`ExtrapolatedTemperature` and `ExtrapolatedPressure` parameters have no
standard-deviation tags.

`RheometerOutputData` owns the attributed signal model. `ActivePitOutputData` and
`FlowLineOutputData` are legacy identity adapters. Configured instances use their
own semantic identities and the shared `FlowCurve` type.

Serialized flow curves implement the shared, vendor-neutral `Rheometry/v1`
contract. `RheometryContract` supplies the canonical SchemaURL and MetaDataURL
placed on every `JSonDataType` flow-curve description. Curve coordinates use
distribution-bearing properties, and rotational speed is published as SI
angular velocity in `rad/s`.

The raw OPC-UA names containing `Extraplated` retain the instrument spelling.
Public semantic properties use the corrected `Extrapolated` spelling.

## Rheology processing

For measured and extrapolated conditions, `RheoSenseBridgeMapper`:

1. Converts the ten nominal RPM values to Fann 35 R1B1 shear rates.
2. Fits a Yield-Power-Law model using Mullineux.
3. Falls back to Zamora/Kelessidis if Mullineux does not return a finite,
   physically usable result.
4. Regenerates the physical pipe-rheometer curve from the fitted model.
5. Applies the inverse Couette transformation to calculate the curve a Fann 35
   R1B1 rheometer would report under the Newtonian hypothesis.
6. Publishes the fitted YPL parameters instead of the YPL values supplied by the
   instrument.

At least three valid shear-stress points are required. If both fits fail, the
mapper publishes neither fitted parameters nor generated curve samples.

## Configuration contract

`ConfigurationForRheoSense` is shared by all executables. `ActivePitTags` and
`FlowLineTags` contain independent `Signals` and `StandardDeviationSignals`
maps. Every entry contains its own complete namespace and node ID. Missing
entries or fields receive sensor-specific defaults when configuration is loaded.

`ContractsPublicBaseUrl` selects the externally reachable shared-contract host
used in flow-curve semantic manifests. It defaults to
`https://dwis.digiwells.no/` and must be an absolute HTTP or HTTPS URL.

Canonical model and wire values are SI. Optional `UnitConversions` and
`StandardDeviationUnitConversions` convert instrument values into SI before
publication.

## Build

```powershell
dotnet build .\DWIS.DAQBridge.RheoSense.Model\DWIS.DAQBridge.RheoSense.Model.csproj
```

The model's behavior is exercised by `DWIS.DAQBridge.RheoSense.Tests`.

## DWIS dependency and semantic update

The bridge now uses Common Worker/Model 1.0.7, OPC Foundation client 2.2.53,
DWIS vocabulary 1.0.176 and OSDC semantic-property framework 10.2.11 (latest stable
versions verified from NuGet on 22 October 2026). Existing signal identities and
persistent paths are retained. Instrument device nodes reference an InstrumentModel
with ModelName and confirmed ModelIdentifier fields, using the same mechanism as BaseStar.

RheoSense publishes ModelName `RheoSense` and ModelIdentifier `IMS/RheoSense`,
identifying Intelligent Mud Solutions (IMS). Separate devices describe the
active-pit and return-flow locations. Extrapolated rheology/curve outputs carry
an explicit Extrapolated characterization and keep their target temperature and
pressure references. Chosen target parameters are not themselves labelled as
extrapolated measurements. The combined JSON rheogram still contains multiple
representations; a Newtonian-hypothesis field alone must not be interpreted as a
native measurement or proof of synthetic Fann 35 provenance by a generic consumer.


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
