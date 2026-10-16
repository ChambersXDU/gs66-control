# Agent guidelines

Target device: MSI GS66 Stealth 10SE, Intel Core i7-10750H and NVIDIA RTX 2060. Actual queries confirmed BIOS E16V1IMS.10C and MSI_Software EC tag 16V1EMS1.109, build text 0311202011:16:49. These bindings do not authorize another firmware or device.

## Current scope

- Prefer small, reviewable changes and clear boundaries between Core, Windows, and CLI.
- Keep Core and its unit tests runnable on macOS/Linux with simulated hardware.
- Windows diagnostic code may only perform **read-only** WMI queries.
- Missing or inaccessible WMI classes must produce informative warnings, not guessed readings.
- Never report simulated output as hardware measurements.
- Do not add an EC write method, vendor firmware flash, undocumented register poke, kernel driver, undervolting operation, or automatic stress test in this phase.
- Do not disable or uninstall Dragon Center/MSI services without a separately validated replacement.
- Do not demand BIOS setup changes or reboots for the initial diagnostics.
- Never request broad unattended administrator rights; separate any future write operations behind explicit user approval, tested device/firmware allowlists and independent safety fallback.
- Do not commit dumps, serial numbers or identifiable WMI output.

## Explicitly approved implementation exception

The user approved the restricted automatic-fan helper implementation described in `docs/fan-mode-trial.md`, including its fixed WMI property update, device/firmware checks and independent recovery worker. The trial approval and its completed result are recorded below. Diagnostic dry-run never writes; the normal trial entry remains locked in code. Diagnostic/telemetry providers stay read-only; all other prohibitions above still apply. Never infer first-write authorization from this implementation approval.

The user subsequently explicitly approved **one 5-second real automatic-fan trial**: 0x8D to 0x0D, normal restoration to 0x8D, independent automatic-mode fallback on parent failure. This is a limited exception to the dry-run-only line above. Require fresh valid CPU/GPU temperatures and every documented device/firmware/schema/state check; otherwise stop before writing. Use an isolated single-use build, keep normal builds locked, and do not repeat a hardware trial or extend to curves/services/stress tests. This approval was consumed successfully on 2026-10-13: one update to 0x0D, a 5-second interval and one update restoring 0x8D, with independent read-back. The enabled temporary artifacts were removed. Do not repeat that trial.

The user subsequently authorized autonomous reversible development and operation within the safety boundary. The desktop now exposes a separately restricted, user-initiated factory automatic action: only 0x0D, with the same binding/firmware checks, independent worker and per-action Windows elevation. That backend cannot restore 0x8D. Do not invoke another real action merely to repeat validation, or infer authorization for curve, other-mode or service writes.

The latest user instruction explicitly expands implementation to working custom fan speeds, stock performance levels and charging control, plus read-only investigation of additional software/firmware capabilities. Use reviewed fixed vendor bindings, exact-device/firmware validation, independent recovery and simulated failure tests; perform a bounded reversible hardware validation only after those gates pass. Temperature breakpoints are device supplied, not arbitrarily writable. Charging must have actual supported-interface proof before enabling it. This expansion does not authorize guessed register pokes, new kernel drivers, firmware flashing, overvolting/undervolting, stress tests or service removal. ACPI table capture via the documented Windows firmware-table APIs and a bounded read-only Get_Data(0xD7) check are within the requested investigation; do not execute AML or firmware code.

The user has now deferred charging work and prioritized quiet light-office/light-entertainment cooling while retaining responsiveness. They authorize repeated bounded representative workload comparisons and application of validated settings, not repetition of the consumed automatic-only trial or saturation/stress workloads. Keep fan preference separate from stock performance selection: quiet must not implicitly select C2, disable boost or reduce maximum processor state. Investigate finer frequency/voltage capabilities read-only; use documented Windows processor power-policy APIs for supported EPP, boost and frequency/state limits with exact snapshots, fresh comparisons, rollback and independent experiment recovery. OS policy read-back does not prove its physical frequency effect. No new voltage/driver/firmware operation is authorized merely by this research request. Keep thermal emergency recovery; never promise silence at unsafe temperatures or claim 99% daily coverage from short laboratory samples.

## Validation

The user explicitly authorized autonomous reversible diagnostics and a short, light ffmpeg GPU workload to wake the discrete GPU before the approved trial. Read-only `nvidia-smi` queries are permitted for this investigation; this is a limited exception to WMI-only diagnostic tooling. Do not turn the workload into a stress test or change GPU clocks, power limits, drivers, or services. Continue without repeated questions for already authorized reversible actions.

Run `dotnet build src/Gs66Control.Cli/Gs66Control.Cli.csproj` and `dotnet test tests/Gs66Control.Tests/Gs66Control.Tests.csproj`. Changes to Windows queries should also be built on a Windows runner. Actual hardware access must not be part of CI.
