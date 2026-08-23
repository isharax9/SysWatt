# Changelog

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Semantic Versioning.

## [Unreleased]

### Added

- Power-first professional dashboard with synchronized dual-axis CPU/GPU utilization and hardware-power graphs.
- Working sidebar navigation, live dark/light themes, administrator restart action, and an About view identifying Ishara / `@isharax9`.
- Hybrid wall-power SQLite energy history with calendar totals and validated JSON archive import/export.
- Total-power discovery for supported PSU, UPS, and power-meter sensors plus measured storage-power normalization.
- Cooling power inventory for fan quantity, label-rated watts, and pumps/controllers, clearly separated from live measurement.
- Windows inventory discovery for NVMe, SSD, HDD, active displays, removable cameras/portable devices, motherboard/system identity, and fan-header-aware cooling defaults.
- User-configurable 1–240-minute rolling graph windows and timed in-app alert dismissal.

- Original SysWatt application logo across the executable, installer, windows, dashboard, and initial tray state.
- Named dashboard tiles for every fresh fan RPM sensor exposed by the hardware provider.
- Headless live-sensor diagnostics with provider/device error records and human-readable enum values.
- Standalone Windows CPU/disk telemetry and embedded low-level hardware collection with no HWiNFO dependency.
- SQLite daily kWh history, calendar lookup, 7-day/month totals, and minute-level records.
- Full resizable dashboard, professional axis/grid charts, customizable sections, and a pinnable tray dashboard.

### Changed

- Restored the original hybrid DC/wall algorithm: exact CPU/GPU sensor watts take priority, missing CPU/GPU watts use adjustable envelopes, and detected/manual system loads complete the setup total.
- Energy accumulation again integrates the hybrid wall-power result while recording its source as `HybridModel`.
- Reworked the tray popup around Current Wall Draw + PC DC, a compact power chart, explicit `PIN`/`PINNED` states, and CPU/GPU/system/peripheral breakdowns.
- Reintroduced the full power-model settings with automatic inventory switches and manual overrides.
- Fixed custom window chrome, maximized borders, dynamic theme resources, and empty chart axes that previously implied a synthetic watt scale.

- Redesigned Settings UI with branded dark window chrome, accessible control contrast, readable metric labels and operators, and consistent alert-grid interactions.
- Moved first sensor enumeration fully off the UI thread and fixed opening Settings directly from a hidden tray state.
- Added visible CPU/fan access guidance, source-detail tooltips, and rejection of implausible zero CPU temperature/package-power readings.
- Replaced unbounded white tooltips with readable wrapped, theme-aware tooltips.
- Added explicit PawnIO low-level-driver diagnostics while preserving `N/A` for inaccessible sensors.

## [0.1.0] - 2026-08-22

### Added

- Initial WPF tray application, compact live dashboard, five-minute graphs, settings, startup registration, and single-instance activation.
- Dynamic LibreHardwareMonitor discovery, deterministic normalization, Windows memory fallback, diagnostic export, and isolated monitoring loop.
- Estimated DC and wall power model with configurable base load and PSU efficiency.
- Custom sustained-duration/cooldown alerts with desktop and in-app delivery.
- Automated tests, portable publishing, Inno Setup packaging, GitHub Actions, and project documentation.
