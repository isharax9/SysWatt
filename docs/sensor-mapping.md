# Sensor discovery and mapping

SysWatt enables supported LibreHardwareMonitor categories, opens the provider lazily, and recursively visits every hardware and sub-hardware node. Each node is updated before its sensors are read. Raw records retain provider, hardware and sensor identifiers/names/types, unit, timestamp, availability, value, and error context.

Normalization ranks candidates using all of the following rather than one exact display name:

1. compatible hardware category and sensor type;
2. finite, plausible value ranges;
3. freshness (readings older than five seconds are rejected);
4. normalized name and identifier hints such as `package`, `total`, `board`, or `core`;
5. negative hints that avoid core-only, memory, rail, or junction readings for broader metrics;
6. provider priority, with LibreHardwareMonitor preferred over targeted fallbacks;
7. stable identifier ordering for deterministic ties.

The winning normalized reading records its source sensor ID/name and ranking score. Invalid, null, stale, absent, or out-of-range data becomes `N/A`, never zero.

## Current limitations

- Hardware firmware and drivers decide which values are exposed. Component power is often absent on older or restricted systems.
- Every valid RPM sensor exposed by LibreHardwareMonitor is kept with its hardware and sensor name and shown in the dashboard. Some graphics cards expose one aggregate RPM reading even when they have multiple physical fans.
- CPU-cooler and case fans are normally reported through motherboard/Super I/O headers. SysWatt can identify only the header labels supplied by firmware (for example `CPU Fan`, `System Fan #2`), and cannot display fans that are connected only to an unmonitored hub or powered directly from the PSU.
- CPU package temperature/power and motherboard fan access can depend on the installed hardware driver and Windows permissions. Diagnostics now retain provider and per-device update failures instead of silently discarding them.
- Storage activity/temperature depends on available LibreHardwareMonitor sensors.
- The Windows fallback currently supplies memory load only and never overrides a higher-ranked primary reading.
- No support claim is made for a device until it has been manually tested.

## Submit a sensor report

Open Settings, choose **Export diagnostics…**, inspect the JSON, then attach it to a bug report with the expected metric and observed behavior. The report contains hardware/sensor labels and OS version but no account name, files, serial numbers added by SysWatt, telemetry identifier, or network upload. Remove any device label you consider identifying before sharing.
