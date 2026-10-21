# DWIS.DAQBridge.RheoSense.BlackboardSink

A diagnostic Blackboard consumer for validating RheoSense semantic discovery
and publication. It communicates with the D-WIS Blackboard, not a RheoSense
OPC-UA server.

## Behavior

The worker registers composed semantic queries for each enabled `Sources` entry,
resolves its Blackboard variables, and reads them at `LoopDuration`. It logs the
source identity and measured pressure. Query bindings are kept per source.

This project is intended for development, demonstrations, and end-to-end
diagnostics. It does not persist data and is not required by the production
server or WebApp.

## Configuration

The shared runtime `home/config.json` supplies `GeneralBlackboard` and
`LoopDuration`. Start from
[`config/config.example.json`](../config/config.example.json). The sink exits
when its initial Blackboard connection cannot be established.

## Run locally

Run it after the Blackboard and RheoSense server are available:

```powershell
dotnet run --project .\DWIS.DAQBridge.RheoSense.BlackboardSink\DWIS.DAQBridge.RheoSense.BlackboardSink.csproj
```

Its Dockerfile is available for diagnostic container use, but this image is not
part of the production Docker publishing workflow.

## Configurable installations

One attributed instrument model is composed with placement profiles. The source,
bridge and sink process the enabled `Sources` list in `home/config.json`.
See [placement profiles and configuration](../PLACEMENT-PROFILES.md) for defaults, tag mappings,
validation, identities and restart behavior.
