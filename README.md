# NVIDIA Crash Detective

We built this monitor alongside Goblin Governor because we needed to see what our GPU was doing while investigating laptop crashes. Detective records evidence; it does not identify a crash's cause or change GPU settings. Independent, experimental software; not an NVIDIA product.

## Download and open

Download the Windows x64 ZIP from [Releases](https://github.com/notsuremedea-rgb/nvidia-crash-detective/releases), extract it, then open `bin/NvidiaCrashDetective.exe` or `run_gpu_monitor.bat`. The executable is unsigned. No personal logs or saved machine settings are included.

Requires 64-bit Windows, .NET Framework 4.8, an NVIDIA driver and `nvidia-smi.exe` on PATH. Python is not required. To build from source, run `build_native.bat`.

## How it works

The dashboard queries NVIDIA GPU index 0 approximately once per second using a hidden nvidia-smi process. It displays clocks, temperature, power, utilisation, VRAM, performance state and limiter flags where available. Unsupported sensors show N/A or the driver's placeholder. It does not change clocks, voltage, power limits, GPU selection or driver settings. Multiple-GPU selection is not implemented.

The graph holds 60 accepted samples, which may cover longer than 60 seconds if readings fail. Session minimum/maximum values include idle time, not just a benchmark.

## Record a benchmark

1. Wait for LIVE readings, then press **START LOG**. The latest 60 accepted samples are written first as PREBUFFER rows.
2. Press **MARK EVENT** near the start and end of the workload. Each click queues USER_MARK for the next accepted sample; it does not label start/end separately.
3. Press **STOP LOG** afterwards. Compare the workload interval separately from idle readings.

Logs go to your **Desktop/NVDetective/Logs** folder. If Windows provides no Desktop path, the app uses its own folder instead. Every row is flushed promptly, but a system freeze or crash can still lose data. Nothing is uploaded automatically. Review files before sharing: they contain timestamps, GPU/driver details, telemetry and potentially diagnostic error text.

## Automatic anomaly recording

**You do not need to press START LOG for incident recording.** While the app is running, failed/incomplete queries, timeouts or readings rejected by basic plausibility checks can trigger an incident. If no log is active, Detective creates a timestamped `gpu-incident-...csv`, writes the rolling pre-buffer and adds the incident. If a log is already active, the marker is added there. Recording continues until STOP LOG or closing the app.

Repeated failures are latched until a valid sample arrives. Queries have a three-second process timeout; six seconds without an accepted sample can trigger a stall marker. Current checks flag graphics clocks below zero or above 6,000 MHz and temperatures outside 1–115°C. These are broad checks, not proof that values inside the bounds are accurate. Suspect values can still affect extrema. One-second sampling can miss brief events.

**An incident means telemetry needs investigation, not that a GPU crash has been proved.** The tool does not collect crash dumps and cannot record while closed. A log-write failure is shown on screen; recording must be restarted after resolving it.

## A setting that helped me

In my testing, changing **NVIDIA Control Panel → Manage 3D Settings → Global Settings → Preferred graphics processor** from Auto-select to **High-performance NVIDIA processor** made clock readings more consistent. That is an observation from my laptop, not a confirmed cause or a guaranteed fix. The option may not appear on every system, and Windows graphics preferences can affect GPU selection. Detective does not change the setting for you.

### Cannot find NVIDIA Control Panel?

Search the Windows Start menu for **NVIDIA Control Panel**. It is separate from the NVIDIA app. If it is missing after a driver installation, install the official [NVIDIA Control Panel from Microsoft Store](https://www.microsoft.com/store/apps/9NF8H0H7WMLT), then open it from Start. [NVIDIA's installation guidance](https://nvidia.custhelp.com/app/answers/detail/a_id/4733/) describes this for DCH drivers.

Look under **Manage 3D Settings → Global Settings → Preferred graphics processor** for **High-performance NVIDIA processor**, then click Apply. This is the setting I tested; availability depends on the system. If it is absent, do not assume a similarly named power-management setting is equivalent. Detective works without changing this preference.

## Alongside Goblin Governor

Detective monitors; [Goblin Governor](https://github.com/notsuremedea-rgb/goblin-governor) controls supported clock settings. They are separate tools.

**A driver update or reinstall can leave Governor's automatic startup paused** if driver access is interrupted. Once the driver is working, manually apply and test your intended profile, choose CONFIRM TESTED PROFILE, then check START WITH WINDOWS. Do not delete recovery files to bypass a pause.

## License and feedback

MIT license; see LICENSE. No NVIDIA binaries are bundled. NVIDIA is a trademark of NVIDIA Corporation; this project is independent and is not endorsed by NVIDIA.

We welcome reproducible bug reports with GPU/driver versions, steps and reviewed log excerpts. Keep personal information out of public reports.
