# IMS tag-list review and configurable OPC-UA units

Reviewed IMS-INT-16762 revision 1A, dated 25 June 2026 (three pages), against the
RheoSense bridge. The PDF is marked Example ONLY and titled Gullfaks C Skid#2;
its containing directory is not evidence that these are the commissioned
Deepsea Stavanger addresses. The original PDF remains in the user's document
location and is not copied into the repository.

## What matches and what differs

| Topic | Finding and bridge consequence |
| --- | --- |
| RPM points | Nine equivalent readings at 3, 6, 10, 20, 30, 100, 200, 300, 600 RPM. Our transport model additionally supports 60 RPM; disable that mapping. The fitter already skips absent inputs. |
| Conditions | Actual mud temperature is supplied in C and F; choose one. Pressure is not listed and must remain unknown, not assumed atmospheric. |
| Extrapolation | Separate actual-temperature, API 50 C and higher-temperature 150 F sets. The bridge now supports additional extrapolation profiles per instrument. Each has independent raw tags, output variables and condition references while retaining one instrument identity. |
| Target temperatures | No dynamic target-condition tags are listed. Constants may be configured in their declared units; 150 F is exactly 65.555... C (338.705... K), rather than the rounded 65.5 C label. No target pressure is invented. |
| Units | Stress readings are labelled lb/100 ft2; PV is cP; temperature is C/F. Values must be converted before fitting/publication. These stress labels are not automatically Fann dial divisions. |
| Yield point | All three YP entries show mPa s, a viscosity unit, inconsistent with yield stress. The user has excluded vendor YP and PV values from use. Both inputs are disabled in the IMS configurations; disabled Bingham outputs are omitted by the server. A viscosity unit remains invalid for yield stress. |
| Density | PPG names are described as specific gravity in 'part per US gallon'; the alternate 'SI' tags are dimensionless specific gravity, not kg/m3. The user confirmed SG. Configurations now use the non-PPG rho_e_m/rho_vs_e tags with OSDC's specific gravity unit (its reference convention is water at 4 C). PPG aliases are not used. |
| Address error | Higher-temperature R100 and R300 have the same a_300rpm_e address. High-temperature R100 stays disabled; its presumed replacement is not treated as verified. |
| Missing inputs | No YPL parameters or sigma tags appear in the list. YPL can be bridge-derived; uncertainty must not be claimed as measured. All sigma inputs are disabled in the templates. |
| Additional signals | The bridge now ingests 10-second and 10-minute gel strengths at measured and both target temperatures. Operating states/time and Running heartbeat are still outside this transport model. Gel values represent the last completed test and may remain unchanged while other channels continue updating. |
| OPC identity/type | ns=6 is a session namespace index, not a stable namespace URI. Obtain the actual URI from NamespaceArray. The table does not establish scalar OPC data types; Float and common numeric integer types are now accepted as well as Double. |

## Meaning of equivalent readings

The user clarified that equivalent here denotes conversion to another temperature,
not non-Newtonian correction or Fann compatibility. The actual-temperature and
target-temperature inputs retain native RheoSense/non-Newtonian-uninfluenced
interpretation. Temperature-extrapolated values are marked Extrapolated, not
Corrected. Separately generated Fann-compatible flow-curve representations remain
explicit compatibility outputs. Gel strengths are native gel-breakout stresses;
they are not fitted into YPL or classified as Fann-compatible curves.

## Unit conversion configuration

Set the API base URL (including any deployment path base) in config.json:

```json
{
  "UnitConversionServiceUrl": "http://localhost:5002/unitconversion/api/",
  "ActivePitTags": {
    "Signals": {
      "MeasuredTemperature": { "UnitName": "celsius" },
      "PlasticViscosityAtMeasuredTemperatureAndPressure": { "UnitName": "centipoise" },
      "ShearStressAt3RPMAtMeasuredTemperatureAndPressure": { "UnitName": "pound per 100 square foot" },
      "MeasuredPressure": { "Enabled": false },
      "ExtrapolatedTemperature": { "ConstantValue": 50, "UnitName": "celsius" }
    }
  }
}
```

Existing namespace/NodeID mappings remain applicable; the example above only
illustrates unit settings. UnitName is an OSDC canonical unit name within the
property's physical quantity. The bridge calls
`GET <UnitConversionServiceUrl>/PhysicalQuantity/<OSDC quantity UUID>`.
This matches the current OSDC service controller and /unitconversion/api path.
It validates the returned quantity ID and unit, then uses the service's
ConversionFactorFromSI and ConversionBiasFromSI in the existing bridge converter.
FromSI = factor * SI + bias; ToSI = (raw - bias) / factor. Sigma conversion omits
bias. Requests are cached per quantity during configuration; realtime values do
not cause HTTP requests. Restart the source/bridge session to reload coefficients.

Unknown units, incompatible quantities, invalid coefficients, service errors,
or simultaneous named and numeric conversions fail session initialization.
There is no identity fallback for a declared named unit. Without UnitName, the
existing numeric UnitConversions remain supported; without either, existing SI
behavior is retained. Resolved coefficients are runtime state, not written back
into config.json. Canonical Blackboard values and processing remain SI.

## Commissioning templates

- config/config.ims-tag-list.50C.example.json
- config/config.ims-tag-list.150F.example.json
- config/config.ims-tag-list.two-temperatures.example.json (both targets)

All have their source disabled until commissioning. Replace the namespace URI
placeholder, confirm endpoint/placement/RigId,
select the combined or single-target configuration, then enable the source. External tag
initialization is off. Unsupported/ambiguous tags remain disabled. Density uses SG; PV and YP remain unused. Templates are additive examples; the
simulator defaults and the user's live config are unchanged. They must be used
with the bridge server, not pointed at by a simulator intended to write real tags.

## Verification

RheoSense solution builds with zero warnings/errors; 46 producer tests pass (including gel mapping and multiple targets). Tests exercise
OSDC catalog-derived responses through an HTTP test handler, exact custom API
base path/quantity UUID, coefficient direction, C/F offsets, sigma scaling,
stress/cP/US-PPG conversion, invalid dimension/unit rejection, service outage,
reconnect idempotency, conflicting numeric settings, Float decoding, and both
commissioning templates. No connection was attempted to the private IMS endpoint;
a live HTTP check against a deployed unit-conversion service was not performed.

## Gel semantics and additional targets

Gel strength is published in SI Pa using the existing DWIS FluidShearStress
quantity and GelBreakingFeature. An existing DataEndPoint/EndPointDescription
profile descriptor, `gelRestTimeSeconds/10` or `/600`, records rest time. This is
an application profile, not a new DWIS noun. Every gel signal has exact temperature
and pressure references; target results additionally carry Extrapolated
characterization. Missing pressure remains unknown.

`ActivePitTags.AdditionalExtrapolations` (and the same field on any source tag set)
contains `{ "Id": "HighTemperature", "Tags": { ... } }` entries. The additional
input reuses the existing scalar contract; its outputs compose cloned extrapolated
manifest DTOs with target-specific identities. No duplicated attributed model or
extra physical instrument is created. Parent instrument/model/location stay shared.
Target IDs must be stable and unique, and extra targets cannot contain measured-
condition inputs or nested targets. Restart source/bridge after configuration
changes. Extra-target logs/history use source-plus-target keys to avoid mixing
values at different temperatures. Default simulation configuration keeps one
extrapolated target; configured additional targets are supported by the simulator.

DAQDisplay discovers separate 10 s/10 min gel trends and preserves all target
references. Shared chart labels include available target-temperature/pressure
values using the selected display-unit system. Last-test gel samples are not
interpolated into an assumed gel evolution or used to infer a new flow curve.
