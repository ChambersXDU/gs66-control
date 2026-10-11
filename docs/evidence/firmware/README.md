# 固件证据索引

完整解释见 [firmware-re.md](../../firmware-re.md)。这里的文件均为离线证据，不是可刷写固件或可用于实机控制的 profile。

- `samples.json`：官方下载 URL、ZIP/image SHA-256、包内成员、工具身份、DSDT 最小 section 位置与现代 GUID 负面搜索边界。
- `release-notes.json`：两份包内 release notes，保留源文件哈希。
- `rea-bios-package.json` / `rea-ec-package.json`：REA `inspect_artifact` 完整 envelopes。
- `rea-msiapcfg-resources.json`：REA `inspect_pe_resources` 完整 envelope，Evidence ID `ev_bca22e2aefa374c97ae1dff286c0d0672ddc543289bba56edeaba06f81cb0b75`。
- `rea-ec-regions-unavailable.json`：缺少 caller-supplied Binwalk 的工具可用性错误；不能作为固件分析结果。
- `acpi-index.json`：75 张唯一候选，每张的哈希、ACPI 头和解压 section byte range；重复内容的来源保留多条。
- `acpi-wmi-map.json`：`SCM0` 的 GUID / 字段 / 18 个方法及十段逐字 ASL（含 header、WDG、Field 和七个方法）。含 DSL SHA-256、原始行号与限制；类名额外按 MOF GUID 关联。
- `legacy-classes.mof`：scope adapter 使用 upstream bmfdec 解析的首 15/16 类；最后的 MSI_ACPI 方法类未解析。
- `iasl-diagnostics.txt`：反编译成功与外部声明冲突警告。
- `uefiextract-diagnostics.txt`：解包格式警告与诊断原文；Intel/CSE 格式问题不用于推出运行时 ACPI 行为。

REA 包 Evidence IDs 为 `ev_e8aea2c2653deea1cac371e0376cf044b9defa036065b794220c3ce40e358c17`（BIOS）和 `ev_e2e74708893695d08befc53e46c274cb14a8db491ae9e5725c2da383a2ec4cbd`（EC）。固件/完整 AML、DSL、DLL 和工具二进制未提交。
