# DWIS.DAQBridge.RheoSense.BlackboardSink

A diagnostic Blackboard consumer for validating RheoSense semantic discovery
and publication. It communicates with the D-WIS Blackboard, not a RheoSense
OPC-UA server.

## Behavior

The worker registers semantic queries for `ActivePitOutputData` and
`FlowLineOutputData`, resolves their Blackboard variables, and reads them at
`LoopDuration`. It logs representative measured pressures and measured-condition
flow-curve sample counts for both sensors.

This project is intended for development, demonstrations, and end-to-end
diagnostics. It does not persist data and is not required by the production
server or WebApp.

## Configuration

The shared [`home/config.json`](../home/config.json) supplies
`GeneralBlackboard` and `LoopDuration`. The sink exits when its initial
Blackboard connection cannot be established.

## Run locally

Run it after the Blackboard and RheoSense server are available:

```powershell
dotnet run --project .\DWIS.DAQBridge.RheoSense.BlackboardSink\DWIS.DAQBridge.RheoSense.BlackboardSink.csproj
```

Its Dockerfile is available for diagnostic container use, but this image is not
part of the production Docker publishing workflow.
