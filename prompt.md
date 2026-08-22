# Codex Build Prompt — SysWatt

You are building **SysWatt**, an open-source-quality Windows tray utility for real-time PC monitoring and estimated power usage. Work autonomously and incrementally: inspect the repository first, preserve useful existing work, establish a clean architecture, implement in small verifiable stages, and run relevant builds and tests after each material change. Do not stop at scaffolding or pseudocode; deliver a working application and its release infrastructure.

## Product goal

Create a lightweight, polished Windows utility with a minimal gaming-overlay aesthetic inspired by NVIDIA/AMD utilities (without copying their branding or assets). It should live primarily in the system tray, open a compact popup dashboard, monitor hardware every second, graph recent session data, show measured CPU/GPU power where available, and present a clearly labeled estimate of total PC power.

The product name is **SysWatt**.

## Fixed technology choices

- C# and .NET 8 LTS.
- WPF for the user interface.
- MVVM and dependency injection using maintained, appropriate .NET packages where they improve clarity.
- LibreHardwareMonitor as the primary sensor provider.
- Windows Performance Counters and/or WMI only as sensible fallbacks for metrics that LibreHardwareMonitor cannot provide reliably. Isolate every provider behind an interface.
- Normal user privileges by default. Request elevation only for a specific operation that genuinely requires it; never require the entire app to run permanently as administrator.
- Current-session history in memory for V1. Do not add a persistent history database yet.
- One-second default polling interval, structured so the interval can be configured later.

## Primary test hardware

Optimize initial testing and validation for this machine, but do not hardcode its sensor names, indexes, identifiers, or expected sensor availability:

- AMD Ryzen 5 3600
- NVIDIA GeForce RTX 3060 12 GB
- 16 GB DDR4 RAM
- NVMe SSD and HDD
- MSI B550M PRO-VDH WIFI motherboard
- Case fans
- CPU air cooler
- RGB light strip

Discover hardware and sensors dynamically at runtime. Different drivers, firmware versions, language settings, and LibreHardwareMonitor releases may expose different names and sensor sets.

## Core functional requirements

### Tray behavior

- Run as a proper Windows notification-area application without an unnecessary taskbar window.
- Left-click opens or toggles a small popup dashboard positioned sensibly near the tray.
- Right-click opens a concise context menu with at least: Open Dashboard, Settings, Start with Windows toggle, and Exit.
- The tray icon focuses on estimated watts by default. Allow the displayed metric to be configured, including sensible choices such as estimated watts, CPU temperature, GPU temperature, CPU usage, and GPU usage.
- Generate/read tray-icon text so it remains legible at Windows tray sizes and common DPI/scaling settings. Provide a tooltip with a compact summary.
- Support start with Windows, start minimized to tray, and remembering settings.
- Implement startup registration in a transparent, reversible, user-scoped way where possible.
- Ensure only one instance normally runs; activating a second instance should bring the existing dashboard forward if practical.
- Exit cleanly, dispose sensor providers and timers, and avoid leaving startup entries or background processes in a broken state.

### Compact dashboard

- Use a minimal, dark, gaming-utility visual language with strong hierarchy, compact spacing, readable typography, and restrained accent color.
- Show the current values for CPU utilization, temperature, and measured package power when available.
- Show the current values for GPU utilization, temperature, and measured board/chip power when available.
- Show RAM usage, relevant storage activity/health metrics that can be obtained safely, and useful fan readings when available.
- Show **CPU power**, **GPU power**, and **Estimated Total PC Power** as distinct values. Never imply that the estimate is a wall-meter measurement.
- Clearly indicate unavailable or unsupported values without errors, fake zeroes, or a broken layout. Prefer `N/A` plus a concise explanation where useful.
- Include session graphs in V1 for at least CPU usage, CPU temperature, GPU usage, GPU temperature, and estimated watts over roughly the last five minutes at the default polling rate.
- Keep graph storage bounded with a ring buffer or equivalent structure. Avoid unbounded memory growth.
- Keep the UI responsive: hardware collection must not block the UI thread.
- Correctly support Windows DPI scaling, display changes, taskbar placement, and multi-monitor boundaries as far as practical.

### Sensor discovery and normalization

Create a strict separation between:

1. **Raw sensor discovery/readings**: provider-specific hardware identifiers, names, types, units, values, timestamps, and availability.
2. **Normalized application metrics**: stable app concepts such as CPU total load, CPU package temperature, CPU package power, GPU core load, GPU temperature, GPU power, memory use, storage activity, and fan RPM.

Requirements:

- Traverse hardware and sub-hardware dynamically and update them correctly before reading sensors.
- Never select a sensor solely through one exact display-name string.
- Build explicit, testable normalization/ranking rules using hardware type, sensor type, identifiers, normalized name hints, validity ranges, and provider priority as appropriate.
- Record enough diagnostic metadata to explain which raw sensor was selected for a normalized metric.
- Handle absent, null, stale, duplicated, invalid, or transient readings gracefully.
- Do not silently replace a genuinely missing reading with zero.
- Add optional diagnostic logging or a diagnostics view/export that helps contributors report detected hardware, raw sensors, mappings, and fallback use without exposing unnecessary personal data.
- Treat WMI and Performance Counters as targeted fallbacks rather than mixing provider-specific details throughout the UI.
- Make provider failures isolated: one failed source or hardware device must not take down the monitoring loop.

### Power model

- Prefer actual CPU and GPU power sensors when available.
- Present CPU and GPU power separately.
- Calculate a clearly labeled **Estimated Total PC Power** using available measured component power plus user-configurable assumptions.
- At minimum, settings must include:
  - Base system consumption in watts, covering components not otherwise measured (motherboard, RAM, drives, fans, cooler, lighting, and similar loads).
  - PSU efficiency as a percentage or decimal with clear validation and help text.
- Define and document whether the total is estimated DC component/system load, estimated AC wall draw, or both. If both are shown, label them unambiguously and use a documented formula such as estimated wall draw = estimated DC load / PSU efficiency.
- Do not double-count components. If base consumption represents all unmeasured equipment, explain that clearly in the settings UI.
- Validate ranges and handle unavailable CPU/GPU power sensors. An estimate made from partial data must be visibly marked as partial or lower-confidence.
- Put the calculation in a pure, unit-testable service. Include test cases for complete inputs, missing CPU/GPU power, efficiency boundaries, invalid settings, and double-counting prevention assumptions.
- Do not implement smart-plug integration in this version.

### Fully customizable alerts

Build alerts as a first-class subsystem rather than hardcoded conditions.

- Users can create, edit, enable/disable, duplicate, and delete alert rules.
- Support all normalized numeric metrics that make sense, not merely a fixed CPU-temperature alert.
- A rule should include: metric, comparison operator, threshold, required duration, cooldown, severity, enabled state, and notification behavior.
- At minimum support greater-than, greater-than-or-equal, less-than, and less-than-or-equal comparisons.
- Duration prevents one-sample spikes from firing. Cooldown prevents notification spam.
- Support Windows toast/desktop notifications and an optional in-app visual indication. If sound is added, make it optional and configurable.
- Missing or stale sensor values must not trigger misleading alerts.
- Persist alert definitions with the rest of the settings and validate malformed/out-of-range rules safely.
- Keep alert evaluation separate from notification delivery and make both testable.
- Unit-test threshold boundaries, sustained-duration behavior, cooldown behavior, recovery/retrigger behavior, disabled rules, and missing data.

### Settings and persistence

- Include settings for tray metric, startup behavior, base consumption, PSU efficiency, alerts, theme/accent where practical, and other user-facing options introduced by the app.
- Persist settings in a user-appropriate application data location; do not depend on the current working directory.
- Use versioned settings with safe defaults and a migration strategy from the start.
- Write settings safely to reduce corruption risk. Recover gracefully from missing or malformed settings and retain/backup the invalid file when helpful for diagnosis.
- Keep portable-build behavior in mind. If portable mode needs a deliberate convention, document and implement it clearly without weakening normal installation behavior.

## Architecture and project structure

Use clear boundaries and keep WPF concerns out of the monitoring domain. A suitable starting structure is:

```text
SysWatt.sln
src/
  SysWatt.App/               WPF shell, tray integration, views, view models
  SysWatt.Core/              Domain models, normalized metrics, power and alert logic
  SysWatt.Infrastructure/    Hardware providers, settings, startup, notifications, logging
tests/
  SysWatt.Core.Tests/
  SysWatt.Infrastructure.Tests/
docs/
  screenshots/
  architecture.md
  sensor-mapping.md
installer/
```

Adjust this only when there is a concrete reason, and document the choice. Favor small cohesive services, interfaces at external boundaries, immutable reading snapshots where practical, cancellation tokens, deterministic time abstractions for tests, and structured logging. Avoid a single global service locator, giant view models, code-behind business logic, and provider-specific types leaking into the UI.

Suggested concepts include:

- `IRawSensorProvider`
- `SensorDescriptor` and `RawSensorReading`
- `ISensorNormalizer` or metric-selection policies
- `MetricSnapshot` / `NormalizedMetricReading`
- `IMonitoringService`
- `IPowerEstimationService`
- `IAlertEvaluator`
- `INotificationService`
- `ISettingsStore`
- `IStartupRegistrationService`
- bounded session-history service
- injectable clock/time provider

Use names that fit the final design rather than following this list mechanically.

## Reliability, privacy, and performance

- No telemetry, account, cloud service, or network dependency in V1.
- Do not collect or upload hardware data.
- Sanitize logs and diagnostic exports where appropriate.
- Catch errors at external/provider boundaries, log actionable context, and keep monitoring alive where safe.
- Prevent overlapping poll cycles if one collection takes longer than the interval.
- Make cancellation and shutdown deterministic.
- Minimize idle CPU usage, allocations, disk writes, and notification-area resource leaks.
- Do not fabricate sensor readings for normal operation. A mock provider may be available only as an explicit developer/demo mode and must be clearly identified.
- Avoid fragile reflection or undocumented Windows behavior when a maintained API is available.

## Testing requirements

- Use a mainstream .NET test framework and a clear assertion library if helpful.
- Unit-test normalization rules with representative raw sensor fixtures, including renamed/duplicated sensors and missing values.
- Unit-test power estimation and alert state machines thoroughly.
- Unit-test bounded history behavior, settings validation/migration, and fallback selection where feasible.
- Ensure tests do not require the primary physical hardware or administrator access.
- Add a small manual test checklist for tray behavior, popup placement, startup registration, sleep/resume, display/DPI changes, unsupported sensors, and clean shutdown.
- If reliable UI automation is not practical for V1, do not create brittle tests merely for coverage; keep UI thin and test its logic below the view layer.

## Build, CI, versioning, and release packaging

Set these up from the beginning:

- Semantic Versioning (`MAJOR.MINOR.PATCH`), beginning with an appropriate pre-1.0 version such as `0.1.0`.
- Centralized version metadata and a documented release process.
- GitHub Actions workflows for restore, build, test, and release packaging on Windows.
- Deterministic/reproducible build settings where practical.
- A portable Windows EXE/package and a conventional Windows installer.
- Prefer a self-contained, single-file portable distribution if compatible with LibreHardwareMonitor/WPF behavior; test it rather than assuming. If trimming or native AOT breaks required reflection/native behavior, disable it and document why.
- Choose a reputable installer technology appropriate for an open-source .NET Windows application (for example WiX Toolset or Inno Setup), automate it, and document the choice.
- The installer must support clean install/uninstall and must not silently force startup with Windows.
- Produce checksums for release artifacts.
- Trigger release packaging from semantic version tags, with prerelease handling where appropriate.
- Include dependency/license review and a third-party notices approach. Select a suitable open-source project license only if the repository does not already specify one; call out the choice for confirmation rather than silently changing an existing license.
- Do not sign binaries unless valid signing credentials are provided. Document where signing would enter the pipeline and ensure CI works without secrets for normal builds/tests.

## Documentation and repository quality

Create or update:

- `README.md` with product overview, feature list, status, supported OS/runtime, install/portable instructions, power-estimate disclaimer, permissions behavior, troubleshooting, development setup, test/build commands, and contribution guidance.
- A screenshot section with stable paths and placeholders initially; replace placeholders with real screenshots when the UI can be run and captured.
- `docs/architecture.md` describing layers, data flow, threading, polling, history, and shutdown.
- `docs/sensor-mapping.md` describing dynamic discovery, normalization, fallbacks, limitations, and how to submit a diagnostic sensor report.
- Alert and power-model documentation with example configurations.
- A changelog following a consistent format such as Keep a Changelog.
- Appropriate `.gitignore`, editor settings, issue/bug-report guidance, and contribution/security documents where proportionate.

Do not claim hardware support that has not been tested. Clearly distinguish tested, expected, and unsupported behavior.

## Incremental execution plan

Use this sequence, keeping the solution buildable at each milestone:

1. Inspect the repository, existing instructions, dependency state, and working tree. Summarize what exists and identify constraints. Do not overwrite unrelated user changes.
2. Create the solution/projects, architecture skeleton, configuration, logging, test projects, and CI baseline.
3. Implement raw LibreHardwareMonitor discovery and a diagnostic enumeration path. Verify recursive hardware/sub-hardware traversal and graceful provider failure.
4. Implement normalized metrics and deterministic mapping policies with fixture-driven unit tests.
5. Implement the one-second monitoring loop, immutable snapshots, bounded five-minute session history, cancellation, and sleep/resume resilience.
6. Implement and test the power-estimation model and its settings.
7. Implement the WPF tray lifecycle, single-instance behavior, compact dashboard, dynamic metric display, graphs, and DPI-aware popup placement.
8. Implement versioned settings, startup registration, and normal-privilege behavior.
9. Implement the customizable alert editor, evaluation state machine, notifications, persistence, and tests.
10. Add portable publishing, installer automation, semantic-version release workflow, checksums, documentation, screenshot structure, and license notices.
11. Run the complete test suite, release build, portable smoke test where possible, and installer validation where possible. Review logs/warnings and fix material issues.

At the end of each milestone, report concisely:

- What changed.
- Files/projects affected.
- Commands/checks run and their results.
- Any assumptions or hardware-dependent behavior still needing manual validation.
- The next milestone.

Continue through all milestones unless genuinely blocked by missing user input, unavailable credentials, or an environment limitation. When blocked, complete every unaffected task first and explain the exact blocker and safest next action.

## Definition of done for V1

V1 is complete when:

- SysWatt builds and launches as a WPF Windows tray app on .NET 8.
- It dynamically discovers available hardware sensors without hardware-name hardcoding.
- Raw sensors and normalized metrics are cleanly separated.
- It refreshes at one-second intervals without freezing the UI or overlapping polls.
- The tray icon defaults to estimated watts and can display another configured metric.
- The compact dashboard displays live normalized metrics and bounded five-minute graphs.
- CPU/GPU power and Estimated Total PC Power are distinct and honestly labeled.
- Base system consumption and PSU efficiency are configurable and validated.
- Missing/unsupported sensors degrade gracefully.
- Fully customizable, duration-aware, cooldown-aware alerts work and persist.
- Start with Windows, minimized-to-tray startup, settings persistence, and clean exit work.
- The app normally runs without elevation.
- Core normalization, power, alert, history, and settings logic has meaningful unit tests.
- GitHub Actions builds and tests the solution and can package tagged releases.
- Both portable and installer artifacts can be produced, with checksums.
- README, architecture, sensor mapping, release, screenshot, and troubleshooting documentation are present.
- Versioning follows Semantic Versioning from the first release.

## Important product wording

Use explicit language throughout the UI and documentation:

- “Estimated Total PC Power” rather than “Power Draw” when the value is modeled.
- “Estimated wall draw” only when PSU efficiency is applied and the formula is explained.
- “Measured by hardware sensor” for component readings when applicable.
- “Unavailable” or “N/A” for missing data—never a misleading `0 W` or `0 °C`.

Begin by inspecting the repository and proposing the concrete milestone-1 file structure and dependency choices. Then implement it, verify it, and continue incrementally to the working V1.
