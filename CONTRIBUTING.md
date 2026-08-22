# Contributing

Use a focused branch, keep provider-specific types inside Infrastructure, and add deterministic fixtures for mapping or state-machine changes. Run restore, Release build, and all tests before opening a pull request. Hardware reports should distinguish tested behavior from expected support and should redact device labels if needed.

For sensor bugs, include a Settings → Export diagnostics report, the metric you expected, hardware/driver context, and whether another monitoring tool can read it. Do not commit generated `artifacts`, `bin`, `obj`, settings, or local NuGet caches.
