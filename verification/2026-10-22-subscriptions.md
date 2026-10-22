# Subscription-based acquisition verification, 22 October 2026

The production worker now uses DWIS client Subscribe callbacks for enabled input,
uncertainty and operating-state tags. One binding per transport node fans out to
all consumers. Callbacks only buffer values and wake the worker; a 50 ms burst
coalescing interval keeps related changes together. Existing value snapshots
control channel calculation, logging and publication. There are no recurring
measurement reads. A one-second watchdog evaluates cached heartbeat expiry and
connection state. Constants, disabled inputs and SI conversions are retained.

InputSubscriptionInterval defaults to 500 ms for sampling and publishing;
SubscriptionWatchdogInterval defaults to one second. Tag/conversion changes
require restarting the bridge. The OPC Foundation client adds a public
ConfigureDataSubscription(TimeSpan publishingInterval, TimeSpan samplingInterval)
method and IDisposable session cleanup. Other callers retain the prior defaults.

## Verification

- RheoSense: all 71 tests pass; Release solution builds with zero warnings/errors.
- Client: all 11 tests pass, including interval validation. Existing client
  nullability/logging warnings remain; these were not reformatted or suppressed.
- Package manifest checked: DWIS.Client.ReferenceImplementation.OPCFoundation
  2.2.54 depends on core client 2.2.53 and DWIS.OPCUA.Schemas 2.2.34.
- Baseline observed RheoSense CPU 27.62%; final active snapshots 0.25%-0.71%.
  Values are individual Docker samples, not a controlled long-duration benchmark.
- OPC-UA diagnostic counters over 20 seconds: RheoSense input reads fell from
  58.85 requests/sec to 0.2 requests/sec (remaining client/session maintenance).
  Final output session measured 0.2 reads/sec and 1.8 writes/sec; channel mix varies.
- Pausing the simulator caused all four RheoSense curve values to become unavailable
  after heartbeat timeout while BaraLogix remained available. Unpausing restored
  them. Five live payloads decoded using their Blackboard-advertised documents:
  BaraLogix two six-point series; each RheoSense payload two ten-point series.
- Server subscription diagnostics confirmed 500 ms publishing cadence. Disposal
  closes sessions rather than waiting for the old ten-minute subscription lifetime.
- Local final image: dwis-rheosense-server:subscriptions-final, running as
  rheosense-server with C:/Volumes/DWISBridgeRheoSense mounted at /home.

## Remaining Blackboard load and release ordering

Blackboard CPU remains elevated and is not claimed as improved. Two managed stack
captures show RDFSharp filter evaluation in QueryManager.Resolve called from
DDHubServerManager.PostProcessInjection/ManageManifestsToInject. This is separate
from the removed recurring RheoSense tag reads. The Viewer registered 153 scoped
queries during the clean restart/discovery. The stack snapshots identify work in
progress, not a complete sampled CPU profile or proof of its percentage share.

Evidence is under C:/DWIS/ViewerOnlineContractVerification, particularly
opc-request-diagnostics.log, opc-request-diagnostics-final-steady.log,
rheo-heartbeat-paused.log, rheo-final-live-curves.log and blackboard-stack-final.txt.
The local Blackboard was restarted once to remove stale subscriptions left by
intermediate test builds; all existing volume paths were retained. The simulator
containers needed another restart after the Blackboard became ready because their
existing startup routines do not retry an initial failed connection.

Release order: publish OPC Foundation client 2.2.54 before running RheoSense CI
against this dependency. Initial local verification used the locally built package
and local-packages/NuGet.Config in the evidence directory. The normal Dockerfiles
restore from NuGet.org; no local package feed or patched binary is shipped in the
repository. The GitHub Docker workflow publishes stable and immutable SHA tags.

Final browser inspection confirmed all three instruments green and both blue
BaraLogix and orange RheoSense curves visible at
http://localhost:5086/DAQDisplay/rheometry after reloading the restarted Viewer.
