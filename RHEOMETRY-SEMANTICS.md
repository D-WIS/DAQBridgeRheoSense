# Rheometry provenance application profile, version 1

This contract is published by the runtime output manifests of RheoSense and
BaraLogix and consumed by DAQDisplay. It adds no DWIS vocabulary classes,
predicates or fields. It uses vocabulary 1.0.175. The controlled string namespace
`https://digiwells.no/daq/rheometry/1/` identifies this application profile; it is
not a claim that these identifiers are official DWIS vocabulary or a web service.

## Graph and serialization

The existing JSON payload and signal identities are unchanged. A combined
`FlowCurve` is `ComputedData`, not a direct measurement: it contains different
representations. It `IsDependentOn` two separately identified `FlowCurve` nodes.
Each representation publishes `IsComputedBy`, a rheological hypothesis, independent
p/T references and, when applicable, `IsCharacterizedBy` an `Extrapolated` node.
Shear-rate and shear-stress components have separate semantic nodes, physical
quantities, processing and JSON-member descriptors. Nominal speed and torque are
also described individually. BaraLogix torque is explicitly `NotPopulated`.
These component/representation nodes describe members of the container; they do
not claim independently subscribable Blackboard variables.

A value or operation `IsDependentOn` a `DataEndPoint` descriptor whose existing
string field `EndPointDescription` contains one controlled profile token:
`<namespace><axis>/<value>`. These describe interpretation/mapping at the semantic
endpoint, rather than adding unregistered OPC UA reference types. Descriptors are
not measurements. Descriptor node identifiers are local to the instrument and
position; discovery matches the exact token values, never their node names.

| Axis | Values / meaning |
| --- | --- |
| `nonNewtonianTreatment` | `Uncorrected`, `Corrected`, `NotInfluenced`, `ModelConsistent` |
| `compatibility` | `InstrumentNative`, `NoCompatibilityConversion`, `Fann35Equivalent` |
| `purpose` | `NativeRheologyFit`, `Fann35Compatibility`, `NativeRheometerConversion`, `NonNewtonianCorrection` |
| `compatibilityTarget` | `Fann35-R1B1` |
| `shearRateMember`, `shearStressMember`, `jsonMember` | Exact member names in each JSON array entry; no unit conversion is implied |
| `availability` | `NotPopulated` means the bridge does not populate that component |

Multiple different values for one axis are ambiguous. Consumers leave it
unspecified, and must not pick one. Unknown profile versions/tokens do not imply
native, corrected or compatible data. Missing member mappings must not trigger
fallback to guessed fields once explicit representations are published.

## Producer distinctions

| Output | Acquisition / processing | Non-Newtonian treatment | Compatibility |
| --- | --- | --- | --- |
| RheoSense native input curve | Measurement-derived (computed when extrapolated) | Not influenced | Instrument native |
| RheoSense fitted YPL | Model fit of native input, not a measurement correction | Not influenced | No compatibility conversion |
| RheoSense Newtonian curve representation | Synthesized response of a Fann 35 R1B1 | Convention retains the apparent Newtonian bias | Fann 35 equivalent |
| RheoSense non-Newtonian curve representation | Synthesized physical/model-consistent curve | Model consistent; no correction of a native measurement | Fann 35 equivalent |
| RheoSense speed and torque in that payload | Computed synthetic quantities | No blanket correction claim | Fann 35 equivalent |
| BaraLogix Newtonian curve representation | Instrument-derived stress and computed nominal rate | Uncorrected | Instrument native |
| BaraLogix non-Newtonian curve and fitted YPL | Computation correcting the instrument-derived curve | Corrected | No compatibility conversion |
| BaraLogix instrument-reported Bingham outputs | Passed through from instrument | Uncorrected | Instrument native |

RheoSense instrument-reported Bingham outputs are passed through and retain the
instrument-native convention; no Fann equivalence is claimed for those scalars.

`IsComputationInput` and `IsDerivedFrom` identify the dependencies: BaraLogix
corrected curves depend on the Newtonian representation; its YPL fit uses the
corrected representation. RheoSense YPL fits depend on the native input, and the
synthetic Fann simulations consume those fitted parameters. Native-input nodes
without a dynamic value document lineage, not additional available signals.

Acquisition, processing purpose, compatibility and reference conditions are
independent. Extrapolated RheoSense curves and fitted values retain their own
target p/T references and explicit extrapolation characterization; the target
parameters themselves are not extrapolated measurements. At measured conditions,
reference signals remain measurement-derived. This profile does not establish
that separately timestamped live p/T samples are historical conditions aligned
with every rheology result.

## Source of truth and generation

The semantic attributes define the base quantities, instrument associations and
references. `RheometryProvenance.Enrich` adds the runtime representation graph and
profile descriptors without changing wire payloads. Keep its contract synchronized
between the independent bridge repositories and DAQDisplay's `RheometryProfile`.
DAQDisplay executes the normal broad generated queries first, then scopes the
profile, representation and derivation queries to the identities discovered.

BaraLogix: regenerate exchange manifests with the existing ExportDataStructures
project. DAQDisplay: regenerate RDF fixtures with
`dotnet run --project tools/ExportSemanticFixtures/ExportSemanticFixtures.csproj -- DWIS.DAQDisplay.Tests/Fixtures`
from DAQDisplay, with the two sibling bridge repositories present. Production
DAQDisplay has no project reference to either bridge.

Tests execute discovery against exports of actual runtime manifests, check both
positions, p/T references, extrapolation, purpose, conflicts, missing metadata and
exact JSON-member decoding. Live Blackboard injection must still be verified in
the target rig environment; no deployment is performed by these source changes.

## Shared sampling locations

A location may depend on a DWIS DataEndPoint whose EndPointDescription is
`https://digiwells.no/daq/rheometry/1/physicalLocationIdentity/<URI-escaped absolute URI>`.
This application-profile descriptor uses existing DWIS vocabulary; it introduces
no DWIS noun or relationship. It identifies the shared physical sampling place,
independently of publisher-scoped instrument, variable and equipment nodes.
DAQDisplay requires matching hydraulic positions as well as matching shared
identities. Distinct tanks/rigs use distinct URIs; conflicting declarations do
not merge sources. Placement profiles own this metadata and publish an additive
identity-only upgrade on bridge startup without changing live signal values.

## Gel-breakout stresses and rest times

Gel signals use existing DWIS FluidShearStress and GelBreakingFeature. A
DataEndPoint description under the existing profile namespace specifies
`gelRestTimeSeconds/10` or `gelRestTimeSeconds/600`. These classify 10-second
and 10-minute tests; consumers must not guess rest time from variable names.
Every signal has its own condition references. Temperature-converted gel results
carry Extrapolated characterization, independent of non-Newtonian correction and
compatibility classification. Additional temperature targets share the physical
instrument while retaining distinct signal, uncertainty and condition identities.
RheoSense provides this extension; it does not imply every producer publishes it.
