# Configurable rheometer installations

The instrument contract is `RheometerOutputData`: its attributes own quantities,
principles, uncertainty, p/T references, native/corrected/compatible/extrapolated
provenance and individual flow-curve representations. `ActivePitOutputData` and
`FlowLineOutputData` are thin legacy identity adapters, not copies of that contract.
`PlacementSemantics` clones the generated manifest DTOs and composes placement
nodes and relationships. It never edits serialized manifest or SPARQL text.
Queries are generated from the exact identities of value-binding descriptors in
the composed graph, so
publication and subscriptions use the same source identities as the manifests.
The descriptors use existing DWIS DataEndPoint/EndPointDescription/IsDependentOn.

## Runtime configuration

The OPCUA source, bridge server and sink read `../home/config.json` relative to
their working directory. Copy `config/config.example.json` there for the defaults,
or use `config/config.all-placements.example.json` to enable all seven placements.
The defaults enable `ActivePit` and `FlowLine` (return before the shakers). The source list is read at session
initialization; restart the simulator and bridge after changing installations.
Use the same source list and tag maps in both processes.

```json
"Sources": [
  { "Id": "Reserve1", "Enabled": true, "Placement": "ReservePit" },
  { "Id": "ReturnAfterShakers", "Enabled": true, "Placement": "ReturnAfterShakers" }
]
```

`Id` is a stable installation identity: 1–64 ASCII letters/digits/underscores,
starting with a letter, unique ignoring case even for disabled entries. Changing
it creates a new source; it does not rename or delete already published signals.
Several source IDs may use the same placement profile. An empty Sources list
publishes no installations. Unknown profiles, invalid/duplicate IDs and overlapping
input addresses are rejected. `ActivePit` and `FlowLine` are reserved legacy IDs
and retain their original placements. Existing instrument and variable identities
for those two IDs are preserved.

| Profile | Published placement facts |
| --- | --- |
| ActivePit | ActivePitLogical |
| ReservePit | ReservePitLogical |
| SlugPit | SlugPitLogical |
| TripTank | TripTankLogical |
| SuctionManifold | Hydraulic junction with a branch upstream of a hydraulic representation of MudPump (no assumed port count) |
| ReturnBeforeShakers | FlowLineLogical representing ReturnFlowLine; branch downstream of AnnulusOutletJunction and upstream of a hydraulic representation of ShaleShakers |
| ReturnAfterShakers | FlowLineLogical representing ReturnFlowLine; branch downstream of a hydraulic representation of ShaleShakers |

All nouns and relationships are from the existing DWIS vocabulary. A pit profile
does not assert an unsupported connection directly to the top of string. Placement
facts describe the sampling association, not the internal rheometer measuring cell.
Equipment instances remain scoped to the published source. Each sampling location
also publishes a shared physical-location identity. `RigId` defaults to `local-rig`;
the default identity is `urn:dwis:rig:<escaped RigId>:location:<escaped Id>`.
Use the same rig and installation IDs across brands at the same location. When
instrument IDs differ, set their `PhysicalLocationId` to the same absolute URI,
for example `urn:dwis:rig:RigA:tank:reserve-1`. Use a different URI for reserve-2,
a different tank, or a different rig. Configure real rig IDs before installation.
DAQDisplay groups matching shared identities and hydraulic positions; before and
after the shakers remain separate even if the identity is accidentally reused.
Without a shared identity it uses exact published equipment/location identities,
never matching tank-role names alone. Conflicting identities stay separate.
On startup/reconnect the bridge publishes an additive location-identity manifest,
including when existing signal bindings already exist; it does not reset values.
DAQDisplay discovers the graph; it does not depend on these configuration enum names.

## Transport and simulation

Every enabled source owns independent input values, output values, random sequence,
processing state and Blackboard publication/subscription targets. New source IDs
use `http://ddhub.no/RheoSense<Id>DataManifest/Variables/` and
`RheoSense<Id>DataManifest.` tag prefixes. The legacy active-pit namespace is retained.
The same enabled list controls simulation and bridge publication. Values are
synthetic; enabling a profile does not imply a physical instrument is installed.
The existing instrument processing and SI wire units are unchanged.

`SourceTags` maps additional source IDs to the existing instrument-specific tag-set
configuration. Legacy active-pit and flow-line tag settings remain supported.
Default source IDs need no explicit address mapping. Source IDs and configuration
are deployment configuration; this change does not remove old Blackboard providers.

RheoSense retains `ActivePitTags` and `FlowLineTags`. Additional `SourceTags`
entries use RheoSenseOPCUATagSet with signal/sigma maps and unit conversions.
The default simulation channel cadences remain 10 seconds for conditions,
30 minutes for rheology generation, and 5 minutes for rheology republication.
Legacy active-pit/flow-line compact history stays in v1 format. Additional sources
use source-named v2 compact streams and source-scoped Blackboard history buckets;
they are included in the same retention budget. The legacy RheoSense-specific
DAQDisplay discovers every configured source and provides the shared visualization.

Publication queries use the framework's `http://ddhub.no/<provider>/` node
namespace convention. A source may set `BlackboardNodeNamespace` for a server
using another semantic namespace. The descriptors are addressed as RDF nodes;
publication does not depend on the server's string-literal equality behavior.
Additional profiles compose graph DTOs; adding another profile does not require
another instrument data class.

Disabling a source stops registration/publication on the next process session.
It does not delete an already registered Blackboard provider or rewrite its old
samples. DAQDisplay may still discover that provider and reports its data age.
