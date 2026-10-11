# Native evidence index

The focused evidence files in this directory are preserved from the following offline Ghidra runs:

```sh
JAVA_HOME=/tmp/gs66-research/tools/java21/Contents/Home \
  /tmp/gs66-research/tools/ghidra/ghidra_12.1.4_PUBLIC/support/analyzeHeadless \
  /tmp/gs66-research/ghidra-project KernCore \
  -import /tmp/gs66-research/nbfoundation/app/KernCoreLib64.dll

JAVA_HOME=/tmp/gs66-research/tools/java21/Contents/Home \
  /tmp/gs66-research/tools/ghidra/ghidra_12.1.4_PUBLIC/support/analyzeHeadless \
  /tmp/gs66-research/ghidra-sys-project KernCoreSys \
  -import /tmp/gs66-research/nbfoundation/app/KernCoreLib64.Sys
```

The macOS arm64 Ghidra decompiler used by these runs was built locally from the bundled Ghidra 12.1.4 source and installed under the isolated Ghidra tree. See `/tmp/gs66-research/native-notes.md` for the build command and SHA-256 values. The raw script output is `/tmp/gs66-research/kerncore-evidence.txt` and `/tmp/gs66-research/kerncore-sys-all.txt`.

## REA native-provider evidence

REA 6.3.0 was run through the isolated Node 24.21.0 runtime with `GHIDRA_INSTALL_DIR=/tmp/gs66-research/tools/ghidra/ghidra_12.1.4_PUBLIC` and `JAVA_HOME=/tmp/gs66-research/tools/java21/Contents/Home`. REA selected provider `ghidra` and reported provider version `12.1.4`. The complete `structuredContent` evidence objects are preserved as `rea-*-evidence.json`. Duplicate text/structured MCP envelopes and session/procedure inventories are retained in `/tmp/gs66-research` rather than duplicated in the repository.

| target | REA operation | entry | evidence ID |
|---|---|---:|---|
| `KernCoreLib64.dll` | `analyze_function` / `GetPhysLong` | `0x180001590` | `ev_0e667c1e60ec4ea3e468ce3d07fe926f252068b945ca084fd74d717eb0af845b` |
| `KernCoreLib64.dll` | `analyze_function` / `SetPhysLong` | `0x180001720` | `ev_d8c0c8a401f38727a4b17adbe931876041ff07b980c9a4dc07d806c96c5b475f` |
| `KernCoreLib64.Sys` | `analyze_function` / `FUN_140001300` | `0x140001300` | `ev_c9aa2a7aa8dd9f0c8fd08e7c6294f73911ee78a6fa6ec50da0028ec6cdc375d3` |

The three dossiers contain REA's observed Ghidra procedure identity, complete body ranges, provider pseudocode, assembly, direct calls, references, referenced names/strings, CFG blocks, native API observations, and bounded high-p-code value flow. For example, REA independently reports `GetPhysLong` body range `0x180001590..0x1800016a3`, `SetPhysLong` `0x180001720..0x180001818`, and the driver dispatch `0x140001300..0x14000160c`.

The REA analysis was source-immutable and ephemeral: Ghidra created a temporary project and the session was closed after each sequence. The tool was used only for static analysis; no target process, Windows service, driver, or hardware interface was run.

## GInf GPU loader evidence

The focused loader dossier is `ginf-loader.json`; the complete REA `CallToolResult` files for the three newly analyzed loader functions are retained in `/tmp/gs66-research/repo-raw-rea-GInf-loader-*.json`; the repository keeps the focused derivative, source evidence IDs and raw-result hashes. They analyze the `pf/MSI/One Dragon Center/GInf.dll` copy, SHA-256 `92555c7f8f92953862ce5d6d6883193a758e1ba7085a0956012cbe5c5aa25857`.

| function | role | entry | evidence ID |
|---|---|---:|---|
| `FUN_180001190` | select/load `nvapi64.dll` or `nvpowerapi.dll`, then initialize interface state | `0x180001190` | `ev_8e959cbfc6296d2dfdc0bcefc7cd478305d3ecc82478cfd952d6216f1e9ada7f` |
| `FUN_180001030` | `GetProcAddress("nvapi_QueryInterface")` and assign `DAT_18000c118` | `0x180001030` | `ev_e26289676493f7e038f276e84a469a5f247bd564392bed4cb98ecd01d2cee05d` |
| `FUN_180002ea0` | wrapper reaching imported `KERNEL32.DLL::LoadLibraryExW` | `0x180002ea0` | `ev_cb35b93e16ea361f51c4d45426581881eaa566b40aa5d71c17df26442ed14e25` |

The existing GInf evidence for `FUN_180001270` (`ev_59a040e97b84de161ac6f2e2f50e9e14468011030de45e8b9c09e79a98167e1c`) confirms the 100 ms polling wrapper and call into `0x180001190`; the existing `FUN_180001a00` evidence (`ev_7134e28811fe75623ea82d07696a8ac6d41270ef9f242dc320a705dcc83432fa`) shows the later `DAT_18000c118(0x0f4dae6b)` query. The exact call chain and uncertainty boundary are summarized in `ginf-loader.json`: the binary proves the loader and function-pointer assignment, but it does not by itself prove the meaning of the NVIDIA ID or any GS66 fan/power effect.

## Other native boundaries

`ginf.json` preserves selected GInf procedure pseudocode, assembly and direct calls from REA/Ghidra, identified by source evidence IDs. `ginf-sections.json` compares the app and pf copies section by section. `wmilib.json` preserves the WriteWMI wrapper and selected inner functions; COM/provider and target firmware semantics must be kept distinct. These are focused derivatives, not complete raw MCP results.

## Legacy simple pipe

[simple-pipe.md](simple-pipe.md) records the x86 MSIService CreateNamedPipeA/ReadFile/parser/WriteFile chain and MSIWmiAcpi Q/S dispatch. [simple-pipe.json](simple-pipe.json) preserves six focused REA function dossiers with unmodified pseudocode, assembly, call edges, sample hashes, source evidence IDs and original-envelope hashes. Complete raw envelopes remain in the local research directory. The parser contains unresolved MFC/variadic prototypes; field semantics and the GS66 provider-to-EC mapping are not claimed from those artifacts.
