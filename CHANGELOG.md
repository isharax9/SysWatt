# Changelog

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning.

## [Unreleased]

### Added

- Original SysWatt application logo across the executable, installer, windows, dashboard, and initial tray state.
- Named dashboard tiles for every fresh fan RPM sensor exposed by the hardware provider.
- Headless live-sensor diagnostics with provider/device error records and human-readable enum values.
- Standalone Windows CPU/disk telemetry and embedded low-level hardware collection with no HWiNFO dependency.
- SQLite daily kWh history, calendar lookup, 7-day/month totals, and minute-level records.
- Full resizable dashboard, professional axis/grid charts, customizable sections, and a pinnable tray dashboard.
- Utilization-based CPU/GPU fallback power and activity/throughput-aware storage power modeling.
- Structured setup-power contributors for motherboard/RAM, storage, fans, cooling, USB devices, displays, external peripherals, and other wall loads.

### Changed

- Redesigned Settings UI with branded dark window chrome, accessible control contrast, readable metric labels and operators, and consistent alert-grid interactions.
- Moved first sensor enumeration fully off the UI thread and fixed opening Settings directly from a hidden tray state.
- Added visible CPU/fan access guidance, source-detail tooltips, and rejection of implausible zero CPU temperature/package-power readings.
- Replaced unbounded white tooltips with readable wrapped dark tooltips and split PC DC loads from external AC loads in the estimate formula.
- Raised the desktop CPU package-idle model from 8 W to 22 W, migrated unchanged legacy defaults, marked modeled component watts with `~`, and added explicit PawnIO low-level-driver diagnostics.

## [0.1.0] - 2026-08-22

### Added

- Initial WPF tray application, compact live dashboard, five-minute graphs, settings, startup registration, and single-instance activation.
- Dynamic LibreHardwareMonitor discovery, deterministic normalization, Windows memory fallback, diagnostic export, and isolated monitoring loop.
- Estimated DC and wall power model with configurable base load and PSU efficiency.
- Custom sustained-duration/cooldown alerts with desktop and in-app delivery.
- Automated tests, portable publishing, Inno Setup packaging, GitHub Actions, and project documentation.
