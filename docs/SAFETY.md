# Hardware safety policy

This is an experimental project. The current milestone is **read-only Windows WMI discovery** and an explicitly labelled simulation backend.

## Prohibited at the scaffold stage

- BIOS/EC firmware flashing or modifying firmware configuration.
- EC register writes, including guessed or copied fan-curve addresses.
- Kernel driver installation, undervolting or power-limit writes.
- Changing fan mode, stopping MSI services, disabling thermal safeguards.
- Automatic stress testing or tests requiring administrator access.

The presence of an MSI WMI class is **not** proof that it can control GS66 10SE hardware. The actual EC revision must be collected on the target machine before adopting a register map.

## Requirements before future control features

1. Identify the exact model, BIOS and EC firmware version; use a device/firmware allowlist.
2. Document provenance and independent validation for every supported write operation.
3. Implement a separate, tested rollback path that works if the main process exits or hangs; do not rely solely on a timer inside that process.
4. Fail safe when telemetry is unavailable, with temperature limits and verified fallback to the manufacturer's automatic cooling mode.
5. Require explicit approval for first hardware writes, and exclude unknown registers from any automated optimization.
6. Confirm changes work without disabling the manufacturer's thermal protections or demanding BIOS edits.

No such safeguards or write operations are implemented yet; **do not assume this scaffold can safely control fans**.
