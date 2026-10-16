# DWIS.DAQBridge.RheoSense.OPCUASource

A synthetic OPC-UA data source for local development and end-to-end testing. It
represents two independent RheoSense instruments: active pit and return flow.

## Published data

For each sensor the source registers and publishes:

- 36 numerical value tags.
- 34 numerical standard-deviation tags.
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

The source currently exits if its initial connection cannot be established;
restart it after the endpoint is available.

## Docker

```powershell
docker build -f DWIS.DAQBridge.RheoSense.OPCUASource/Dockerfile -t rheosense-opcua-source .
```

Mount shared configuration at `/home`. The published Docker Hub image is
`digiwells/dwisdaqbridgerheosenseopcuasource`.
