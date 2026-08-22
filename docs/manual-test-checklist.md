# Manual test checklist

- [ ] Launch as a normal user; confirm no UAC prompt and no taskbar window.
- [ ] Left-click toggles the dashboard; right-click exposes every required command.
- [ ] A second launch focuses the existing dashboard and exits cleanly.
- [ ] Move the taskbar to each edge and test on each monitor at 100%, 125%, 150%, and 200% scaling.
- [ ] Change display layout/scaling while running; verify the popup remains inside the selected work area.
- [ ] Confirm one-second updates and roughly five minutes of bounded graphs.
- [ ] Compare discovered CPU/GPU values on supported test hardware; missing sensors show `N/A`.
- [ ] Verify component readings and both estimates are labeled distinctly; alter base watts and efficiency.
- [ ] Create boundary, sustained, recovery, cooldown, disabled, and missing-metric alerts.
- [ ] Enable/disable startup and inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- [ ] Sleep/resume and confirm collection restarts without overlapping or duplicate notifications.
- [ ] Export diagnostics and confirm it contains raw sensors and selected mappings but no unexpected personal data.
- [ ] Exit while polling; confirm the tray icon and process disappear.
- [ ] Install/uninstall the Inno package; confirm startup is not forced and user settings are retained.
- [ ] Run portable build from a writable folder; confirm `data\settings.json` is used.
