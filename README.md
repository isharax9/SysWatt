# SysWatt

SysWatt is a standalone Windows hardware and energy monitor. It combines embedded LibreHardwareMonitor access with Windows-native CPU, memory, and physical-disk counters; no HWiNFO process or shared-memory feed is required. It keeps a 15-minute live window and stores daily energy history locally in SQLite.

> **Status:** `0.1.0` pre-release. The architecture and automated domain tests are ready; real-hardware behavior still needs validation across more systems. The Ryzen 5 3600 / RTX 3060 target is an initial manual-test target, not a compatibility claim.

![SysWatt redesigned settings](docs/screenshots/settings.png)

## Features

- Full resizable application dashboard plus a compact pinnable tray dashboard and live numeric tray icon.
- CPU, GPU, memory, storage throughput/activity/power, measured or modeled component power, and named fan channels.
- Professional axis/grid charts with a bounded 15-minute live window sampled every second.
- SQLite-backed daily kWh history with 7-day/month summaries and a calendar day picker.
- Per-section dashboard customization persisted across launches.
- Dynamic, ranked sensor mapping—no hardcoded machine sensor names or indexes.
- Configurable CPU/GPU idle and peak envelopes; activity-aware storage device/idle/throughput parameters; and motherboard, fans, cooling, USB, display, peripheral, PSU-efficiency, and wall loads.
- User-defined alerts with metric, operator, threshold, duration, cooldown, severity, toast, and in-app behavior.
- User-scoped, reversible start-with-Windows registration.
- Versioned atomic JSON settings and portable mode.
- JSON diagnostics export with raw sensor metadata and selected normalized mappings.
- No telemetry, accounts, cloud calls, or hardware-data upload.

## Power estimate disclaimer

SysWatt is **not a wall meter**. CPU and GPU values labeled as hardware sensor readings come from the device when available. The app calculates:

```text
Estimated PC DC = CPU + GPU + motherboard/RAM + storage + fans + cooling + USB devices
Estimated setup wall = PC DC / PSU efficiency + displays + external peripherals + other wall loads
```

Do not include CPU or GPU power again in a configured category when those sensors are available. Fan power uses a rated-watts estimate, not RPM-derived electrical measurement. If component power sensors are missing, SysWatt applies a non-linear utilization model between the configured idle and peak values and labels the result `UTILIZATION MODEL`.

## Requirements and installation

- Windows 10 version 1809 or newer, or Windows 11, x64.
- Normal user access for Windows counters and GPU APIs. Ryzen package temperature/power and motherboard sensors require a compatible low-level driver such as PawnIO; unlike HWiNFO or MSI Center, SysWatt does not silently reuse another application's driver.

For an installed build, run `SysWatt-Setup-<version>.exe`. The installer is per-user and does not force Windows startup. For the portable ZIP, extract it to a writable folder and run `SysWatt.App.exe`; `portable.flag` keeps settings in the adjacent `data` directory. Remove that flag to use `%LOCALAPPDATA%\SysWatt\settings.json`.

## Usage

- Left-click the tray icon to toggle the quick dashboard; use its pin button to keep it visible.
- Right-click it for the quick dashboard, full dashboard, Settings, startup, and Exit.
- Use Settings to select the tray metric, tune the power model, edit alerts, or export a diagnostic JSON report.
- Missing data appears as `N/A`; SysWatt never substitutes a fake zero.

## Build and test

Install the .NET 8 SDK, then run:

```powershell
dotnet restore SysWatt.sln --configfile NuGet.Config
dotnet build SysWatt.sln --no-restore --configuration Release
dotnet test SysWatt.sln --no-build --configuration Release
```

Build release artifacts with:

```powershell
./scripts/package.ps1 -Version 0.1.0
```

Inno Setup 6 is optional locally; when `ISCC.exe` is available the script also creates the per-user installer. The self-contained single-file publish is deliberately untrimmed because hardware libraries and WPF may rely on reflection and native resources.

## Troubleshooting

- **A reading is `N/A`:** the device/driver may not expose a compatible sensor. Export diagnostics from Settings and review the mapping explanation.
- **A low-level CPU temperature or package-power reading is missing:** diagnostics identify whether PawnIO is absent. HWiNFO and MSI Center ship privileged drivers, while ordinary Windows counters do not expose Ryzen SMU telemetry. SysWatt models watts with a visible `~` prefix but never fabricates temperature or RPM.
- **HWiNFO/MSI Center is running at the same time:** low-level SMU/Super-I/O polling can conflict. Close other hardware monitors before judging SysWatt's direct collector.
- **Tray icon is hidden:** open the Windows tray overflow and pin SysWatt.
- **Power looks inaccurate:** tune the CPU/GPU idle and peak envelopes, storage device parameters, PSU efficiency, and external wall loads in Settings.
- **Settings were reset:** malformed JSON is moved to `settings.json.invalid-<timestamp>` before defaults are loaded.
- **A second launch exits:** this is expected; it signals the existing instance to open.

See [architecture](docs/architecture.md), [sensor mapping](docs/sensor-mapping.md), [power and alerts](docs/power-and-alerts.md), and the [manual test checklist](docs/manual-test-checklist.md).

## Contributing and security

Contributions are welcome; start with [CONTRIBUTING.md](CONTRIBUTING.md). Please report security issues privately as described in [SECURITY.md](SECURITY.md). SysWatt is MIT-licensed; dependency notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
