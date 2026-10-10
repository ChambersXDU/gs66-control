# Agent guidelines

Target device: MSI GS66 Stealth 10SE, Intel Core i7-10750H and NVIDIA RTX 2060. The exact BIOS/EC firmware revision is **unknown until queried on the user's actual machine**.

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

## Validation

Run `dotnet build src/Gs66Control.Cli/Gs66Control.Cli.csproj` and `dotnet test tests/Gs66Control.Tests/Gs66Control.Tests.csproj`. Changes to Windows queries should also be built on a Windows runner. Actual hardware access must not be part of CI.
