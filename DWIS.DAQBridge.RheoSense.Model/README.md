# DWIS.DAQBridge.RheoSense.Model

Shared domain contracts and worker infrastructure for the RheoSense bridge.
This library is referenced by every executable in the solution and by the
tests. It is not an independently runnable application.

## Responsibilities

- Define the 36 numerical OPC-UA values produced by each RheoSense instrument.
- Keep active-pit and return-flow sensors separate through independent tag maps,
  semantic manifests, measurement devices, and hydraulic locations.
- Define the semantic Blackboard outputs in `ActivePitOutputData` and
  `FlowLineOutputData`.
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

`ActivePitOutputData` and `FlowLineOutputData` are semantic Blackboard models.
They share the `FlowCurve` type but use different semantic identities so a
consumer cannot confuse the two physical sensor positions.

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

Canonical model and wire values are SI. Optional `UnitConversions` and
`StandardDeviationUnitConversions` convert instrument values into SI before
publication.

## Build

```powershell
dotnet build .\DWIS.DAQBridge.RheoSense.Model\DWIS.DAQBridge.RheoSense.Model.csproj
```

The model's behavior is exercised by `DWIS.DAQBridge.RheoSense.Tests`.
