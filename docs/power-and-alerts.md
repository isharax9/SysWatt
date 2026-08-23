# Power model and alerts

## Power

SysWatt separates PC-side DC loads from devices plugged directly into the wall. The PC model includes motherboard/RAM, storage, regular fans (`count × rated watts`), other cooling/pumps, and USB-powered devices. Displays, externally powered peripherals, and other wall loads are added after PSU losses:

```text
PC DC watts = measured-or-modeled CPU + measured-or-modeled GPU + activity-aware storage + configured PC auxiliaries
Setup wall watts = PC DC watts / PSU efficiency + displays + external peripherals + other wall loads
```

Fan RPM does not reveal electrical power. Use the rated current printed on the fan label or datasheet: `rated watts = 12 V × rated amps` (for example, `12 V × 0.16 A = 1.92 W`). This is normally a conservative maximum; actual PWM-controlled consumption varies. Fans on a shared hub still need to be counted individually.

Defaults are 30 W motherboard/RAM, a 22 W desktop CPU package-idle floor, an 8 W active storage ceiling, three 2 W fans, 5 W USB devices, and 87% PSU efficiency. Storage interpolates between per-device idle draw and its active ceiling using the larger of disk active time and normalized read/write throughput. Missing CPU/GPU power sensors use a non-linear live-utilization curve between configurable idle and peak envelopes; modeled readings are explicitly labeled with `~` and should be calibrated against a trusted package-power reading when available.

Example: CPU 65 W + GPU 170 W + 49 W PC auxiliaries = 284 W estimated DC. At 80% efficiency that PC draws 355 W at the wall; adding a 45 W monitor gives a 400 W total setup estimate.

## Alerts

Each rule has a stable ID, name, normalized numeric metric, `>`, `>=`, `<`, or `<=` comparison, threshold, required duration, cooldown, severity, enabled state, desktop-notification switch, and in-app switch.

The duration timer begins at the first breaching valid sample and resets on recovery, missing/stale data, or disablement. A sustained breach fires once. Recovery rearms it, but cooldown must also have elapsed before another notification. This avoids one-sample spikes and repeated notifications during one long incident.

Example: CPU Temperature `>= 85 °C` for `10 s`, cooldown `300 s`, severity Warning.
