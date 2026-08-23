# Sensor discovery and mapping

SysWatt uses a hybrid provider model. HWiNFO Shared Memory is an optional bridge for users who want HWiNFO-reported values; LibreHardwareMonitor/PawnIO supplies SysWatt Full Hardware Access when available; Windows APIs keep Standalone Mode useful when neither low-level source is available. Raw records retain provider, hardware and sensor identifiers/names/types, unit, timestamp, availability, value, and error context.

The dashboard and tray popup show the active source. A source transition raises one in-app notice and one tray notification. SQLite stores the source alongside each integrated energy interval so days that span a provider transition remain auditable.

Normalization ranks candidates using all of the following rather than one exact display name:

1. compatible hardware category and sensor type;
2. finite, plausible value ranges;
3. freshness (readings older than five seconds are rejected);
4. normalized name and identifier hints such as `package`, `total`, `board`, or `core`;
5. negative hints that avoid core-only, memory, rail, or junction readings for broader metrics;
6. provider priority, with HWiNFO Bridge preferred over embedded hardware readings, and both preferred over targeted Windows-native fallbacks;
7. stable identifier ordering for deterministic ties.

The winning normalized reading records its source sensor ID/name and ranking score. Invalid, null, stale, absent, or out-of-range data becomes `N/A`, never zero.

## Current limitations

- Hardware firmware and drivers decide which values are exposed. Component power is often absent on older or restricted systems.
- Every valid RPM sensor exposed by LibreHardwareMonitor is kept with its hardware and sensor name and shown in the dashboard. Some graphics cards expose one aggregate RPM reading even when they have multiple physical fans.
- CPU-cooler and case fans are normally reported through motherboard/Super I/O headers. SysWatt can identify only the header labels supplied by firmware (for example `CPU Fan`, `System Fan #2`), and cannot display fans that are connected only to an unmonitored hub or powered directly from the PSU.
- CPU package temperature/power and motherboard fan access can depend on the installed hardware driver and Windows permissions. Diagnostics now retain provider and per-device update failures instead of silently discarding them.
- HWiNFO and MSI Center report Ryzen SMU data through their own privileged drivers. The embedded LibreHardwareMonitor path uses PawnIO for equivalent low-level access; when PawnIO is absent, SysWatt reports that condition explicitly and uses only the power model—not a fabricated temperature.
- Multiple programs polling Ryzen SMU or Super-I/O registers can still conflict; avoid running multiple low-level monitor engines simultaneously.
- Storage activity and throughput come from Windows physical-disk counters. Storage temperature still depends on hardware/driver exposure.
- Windows-native fallbacks never override a valid higher-ranked embedded hardware reading.
- No support claim is made for a device until it has been manually tested.

## Submit a sensor report

Open Settings, choose **Export diagnostics…**, inspect the JSON, then attach it to a bug report with the expected metric and observed behavior. The report contains hardware/sensor labels and OS version but no account name, files, serial numbers added by SysWatt, telemetry identifier, or network upload. Remove any device label you consider identifying before sharing.
