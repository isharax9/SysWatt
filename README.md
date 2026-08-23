# SysWatt

SysWatt is a standalone Windows hardware power and energy monitor. It combines embedded LibreHardwareMonitor access, Windows-native activity counters, Windows hardware inventory, and an optional HWiNFO shared-memory bridge. Its live graph window is configurable from 1 to 240 minutes, and daily energy history is stored locally in SQLite.

> **Status:** `0.1.0` pre-release. The architecture and automated domain tests are ready; real-hardware behavior still needs validation across more systems. The Ryzen 5 3600 / RTX 3060 target is an initial manual-test target, not a compatibility claim.

![SysWatt redesigned settings](docs/screenshots/settings.png)

## Features

- Full resizable application dashboard plus a compact pinnable tray dashboard and live numeric tray icon.
- Exact CPU/GPU power sensors when exposed, plus an explicitly labeled hybrid DC/wall model for the rest of the system.
- Professional axis/grid charts with synchronized utilization/power axes and a configurable 1–240-minute rolling window.
- SQLite-backed daily kWh history with 7-day/month summaries, calendar lookup, and validated archive import/export.
- Working dashboard navigation, dark/light themes, administrator recovery guidance, and an About view.
- Per-section dashboard customization persisted across launches.
- Dynamic, ranked sensor mapping—no hardcoded machine sensor names or indexes.
- Automatic NVMe/SSD/HDD classification, active/idle storage curves, active-display and removable camera/portable-device discovery, and fan-header-aware cooling inventory with manual overrides.
- User-defined alerts with metric, operator, threshold, duration, cooldown, severity, toast, and in-app behavior.
- User-scoped, reversible start-with-Windows registration.
- Versioned atomic JSON settings and portable mode.
- JSON diagnostics export with raw sensor metadata and selected normalized mappings.
- No telemetry, accounts, cloud calls, or hardware-data upload.

## Measurement policy

SysWatt prefers exact hardware-reported CPU/GPU watts and never overwrites them. If an exact component sensor is absent, the hybrid total uses a labeled, adjustable utilization envelope. Storage draw uses detected drive classes plus live activity/throughput; motherboard/RAM, CPU and case cooling, displays, USB devices, and external peripherals use visible settings. These values are labeled **calculated**, not presented as hardware measurements. Wall draw is `PC DC / PSU efficiency + displays + external wall loads`, and that result is integrated into daily kWh.

## Requirements and installation

- Windows 10 version 1809 or newer, or Windows 11, x64.
- Normal user access for Windows counters and GPU APIs. Ryzen package temperature/power and motherboard sensors require a compatible low-level driver such as PawnIO; unlike HWiNFO or MSI Center, SysWatt does not silently reuse another application's driver.

For an installed build, run `SysWatt-Setup-<version>.exe`. The installer is per-user and does not force Windows startup. For the portable ZIP, extract it to a writable folder and run `SysWatt.App.exe`; `portable.flag` keeps settings in the adjacent `data` directory. Remove that flag to use `%LOCALAPPDATA%\SysWatt\settings.json`.

## Usage

- Left-click the tray icon to toggle the quick dashboard; use its pin button to keep it visible.
- Right-click it for the quick dashboard, full dashboard, Settings, startup, and Exit.
- Use Settings to select the theme, tray metric, graph duration, alert banner duration, automatic inventory policy, and every manual power-model input.
- Use Energy history to export or import a validated SysWatt energy archive. Matching dates are replaced rather than added twice.
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
- **A low-level CPU temperature or package-power reading is missing:** use **Restart as administrator** from the dashboard. Diagnostics identify whether access is permission-restricted or PawnIO is absent. The reading remains `N/A` until a hardware provider succeeds.
- **HWiNFO/MSI Center is running at the same time:** low-level SMU/Super-I/O polling can conflict. Close other hardware monitors before judging SysWatt's direct collector.
- **Tray icon is hidden:** open the Windows tray overflow and pin SysWatt.
- **A calculated total looks wrong:** open Settings and adjust the detected storage, fan/cooling, motherboard/RAM, display, peripheral, PSU-efficiency, or CPU/GPU fallback envelope. Exact sensor readings remain labeled separately.
- **Settings were reset:** malformed JSON is moved to `settings.json.invalid-<timestamp>` before defaults are loaded.
- **A second launch exits:** this is expected; it signals the existing instance to open.

See [architecture](docs/architecture.md), [sensor mapping](docs/sensor-mapping.md), [power and alerts](docs/power-and-alerts.md), and the [manual test checklist](docs/manual-test-checklist.md).

## Contributing and security

Contributions are welcome; start with [CONTRIBUTING.md](CONTRIBUTING.md). Please report security issues privately as described in [SECURITY.md](SECURITY.md). SysWatt is MIT-licensed; dependency notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
