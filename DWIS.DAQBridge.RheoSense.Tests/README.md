# DWIS.DAQBridge.RheoSense.Tests

xUnit regression tests for the shared model and WebApp history helpers. The
project is not packaged or deployed.

## Covered behavior

- Separation of OPC-UA transport models from semantic Blackboard outputs.
- Complete 36-value and 34-deviation tag sets for both sensors.
- Per-signal configuration defaults and independent sensor overrides.
- Active-pit and return-flow semantic identity separation.
- Mullineux YPL fitting and Zamora/Kelessidis fallback.
- Physical and simulated Fann 35 curve generation and serialization.
- Independent condition and rheology change detection.
- Missing and zero standard-deviation handling.
- Compact logging, UTC normalization, compression, retention, and sensor
  separation.
- Eight-bucket, 48-hour Blackboard history layout.
- WebApp history restoration, filtering, downsampling, and flow curves.

Filesystem tests create isolated temporary directories and do not use the
repository's `home` directory or deployed log storage.

## Run

```powershell
dotnet test .\DWIS.DAQBridge.RheoSense.Tests\DWIS.DAQBridge.RheoSense.Tests.csproj
```

For an already built Release configuration:

```powershell
dotnet test .\DWIS.DAQBridge.RheoSense.Tests\DWIS.DAQBridge.RheoSense.Tests.csproj -c Release --no-build --no-restore
```
