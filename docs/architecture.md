# Architecture

## Boundaries

`SysWatt.Core` contains immutable readings, stable metric concepts, normalization policies, the pure power model, alert state machine, bounded history, settings models, and interfaces. It has no WPF or LibreHardwareMonitor reference.

`SysWatt.Infrastructure` owns external boundaries: recursive LibreHardwareMonitor traversal, the isolated Windows memory fallback, the serialized monitoring loop, atomic JSON settings, current-user startup registration, and diagnostic export.

`SysWatt.App` is the WPF/WinForms-notification-area shell. View models format snapshots and expose bounded series; windows handle only presentation and OS dialogs. Services are composed through Microsoft.Extensions.DependencyInjection.

## Data flow

```text
LibreHardwareMonitor ─┐
Windows fallback ─────┴─> raw readings ─> ranking/normalization ─> immutable snapshot
                                                               ├─> power model
                                                               ├─> ring-buffer history
                                                               ├─> alert evaluator ─> tray notification
                                                               └─> dashboard/tray bindings
```

Providers run sequentially off the UI thread inside one monitoring loop. This intentionally prevents overlapping cycles. Each provider/device boundary is caught and logged independently, cancellation is propagated, and the next cycle proceeds after a recoverable failure. UI subscribers marshal updates to the WPF dispatcher.

The current polling interval is one second. History owns one fixed-capacity queue per metric (300 samples), so memory use stays bounded. There is no database or cross-session history in V1.

## Startup and shutdown

A named mutex establishes the primary instance. Later instances send a byte over a user-local named pipe; the primary dispatches dashboard activation. Exit hides/disposes the tray icon, cancels and awaits the monitoring loop, disposes providers (closing LibreHardwareMonitor), stops the DI host, releases the mutex, then shuts down WPF.

Settings are written to a temporary file and atomically moved into place. Normal mode uses `%LOCALAPPDATA%\SysWatt`; a `portable.flag` beside the executable switches to `data\settings.json` beside the app.
