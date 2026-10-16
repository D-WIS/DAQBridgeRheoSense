# DWIS.DAQBridge.RheoSense.WebApp

A server-side Blazor and MudBlazor dashboard for both RheoSense sensors. The
default route is `/RheoSense/webapp`.

## Dashboard

The screen provides three equal desktop columns that stack responsively:

- Active-pit and return-flow density, temperature, and pressure trends.
- Measured- and extrapolated-condition YPL yield stress, consistency index, and
  flow-behavior index trends.
- Active-pit and return-flow rheograms showing physical and simulated Fann 35
  curves at measured and extrapolated conditions.

Physical curves use solid lines and Fann 35 curves use dashed lines. Active-pit
series use cool colors and return-flow series use warm colors. The visible
history window can be adjusted from 1 to 48 hours.

## Units and time reference

The page uses `MudUnitAndReferenceChoiceTag` from
`OSDC.UnitConversion.DrillingRazorMudComponents`. It obtains unit systems from
`UnitConversionHostURL`, defaulting to `https://app.digiwells.no/`. Model values
remain SI; conversion affects presentation only.

All stored timestamps are UTC. The enabled time-reference selector controls how
chart axes display those instants, including UTC or the user's local time.

## History and persistence

Every five seconds the background reader obtains the 16 published history
buckets from the OPC-UA part of the Blackboard. It rebuilds the in-memory
48-hour window only when a bucket changes and limits chart snapshots to 300
timestamps.

`WebAppTimeSpanHours` in `/home/config.json` stores the last selected window.
Updates use atomic file replacement and preserve other configuration properties.
Data-protection keys are also stored under `/home`; mount it persistently.

If Blackboard is unavailable, the WebApp remains running, shows a waiting
state, and retries according to `ConnectionRetryInterval`.

## Configuration

- `BasePath`: route prefix; default `/RheoSense/webapp`.
- `UnitConversionHostURL`: host for the OSDC unit conversion API.
- Runtime `home/config.json`: shared bridge and Blackboard configuration. Start
  from [`config/config.example.json`](../config/config.example.json).

## Run locally

```powershell
dotnet run --project .\DWIS.DAQBridge.RheoSense.WebApp\DWIS.DAQBridge.RheoSense.WebApp.csproj --no-launch-profile --urls http://localhost:5080
```

Open `http://localhost:5080/RheoSense/webapp`.

## Docker

```powershell
docker build -f DWIS.DAQBridge.RheoSense.WebApp/Dockerfile -t rheosense-webapp .
docker run --rm -p 5080:8080 -v <persistent-home>:/home rheosense-webapp
```

The published Docker Hub image is
`digiwells/dwisdaqbridgerheosensewebapp`.
