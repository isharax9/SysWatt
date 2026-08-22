# Power model and alerts

## Power

`Base system consumption` covers only equipment not otherwise measured: motherboard, RAM, storage, fans, cooling, and lighting. The model adds each CPU and GPU reading once, preventing implicit double counting:

```text
DC watts = available CPU watts + available GPU watts + base watts
Wall watts = DC watts / PSU efficiency
```

Defaults are 45 W base and 87% efficiency. Valid base load is 0–1000 W; valid efficiency is 50–100%. Both results are estimates. Missing CPU/GPU sensors yield a numeric partial estimate plus an explicit lower-confidence message, not a fabricated component reading.

Example: CPU 65 W + GPU 170 W + base 45 W = 280 W estimated DC; at 80% efficiency, estimated wall draw is 350 W.

## Alerts

Each rule has a stable ID, name, normalized numeric metric, `>`, `>=`, `<`, or `<=` comparison, threshold, required duration, cooldown, severity, enabled state, desktop-notification switch, and in-app switch.

The duration timer begins at the first breaching valid sample and resets on recovery, missing/stale data, or disablement. A sustained breach fires once. Recovery rearms it, but cooldown must also have elapsed before another notification. This avoids one-sample spikes and repeated notifications during one long incident.

Example: CPU Temperature `>= 85 °C` for `10 s`, cooldown `300 s`, severity Warning.
