# GS66 Control

面向 **MSI GS66 Stealth 10SE（i7-10750H / RTX 2060）** 的实验性硬件诊断与控制项目，长期目标是轻量替代 Dragon Center 中的功耗、风扇和性能管理功能。

**当前已有中文 Windows 桌面诊断原型与 CLI。** 支持只读系统身份、BIOS 版本及 MSI WMI 类定义查询、模拟演示和 JSON 导出；尚不能读取原始 EC 数据、风扇转速或修改风扇、CPU/GPU 功耗。不要将模拟数据当作实测结果。

Dragon Center 的离线静态研究已整理至 [逆向研究报告](docs/dragon-center-re.md)，包含官方安装包版本与哈希、UI 到后台及硬件访问边界的调用链、GS66 控制能力矩阵和后续只读验证方案。对应的 [证据索引](docs/evidence/dragon-center/README.md) 保存文件清单、REA/ILSpy/Ghidra 分析片段；当前官方通用包尚未确认为 2020 年预装版本，研究结论也不代表 GS66 实机验证成功。

进一步的 [BIOS / EC 分析](docs/firmware-re.md) 下载了官方 `E16V1IMS.112` / `16V1EMS1.118`，恢复该 BIOS 的 WMI GUID 与候选 G1 曲线字段，并发现一个 AP 查询分支会写入 EC，因此初始诊断只读取类定义。完整实机 EC 版本、实例索引与第三风扇映射仍待验证。

## Windows 原型

从 [GitHub Release：v0.1.0-preview.1](https://github.com/ChambersXDU/gs66-control/releases/tag/v0.1.0-preview.1) 下载 `GS66Control-win-x64.zip` 与对应 SHA-256 校验文件。这是只读诊断预览版，尚未进行 GS66 实机验证。

完整发布包解压后运行 `windows-x64/Gs66Control.exe`，无需安装 .NET。启动不自动采集；点击“实机只读诊断”或“模拟演示”，查看设备/WMI 定义/警告后可“导出 JSON”。发布包须完整保留，不能只复制 exe。构建方式、使用说明与验证限制见 [Windows app 文档](docs/windows-app.md)。

## 结构

- `src/Gs66Control.Core`：跨平台数据模型、硬件探针接口、模拟后端。
- `src/Gs66Control.Windows`：Windows WMI 只读查询（系统型号、CPU/GPU、BIOS、MSI 相关 WMI 类）。
- `src/Gs66Control.App`：中文 WinForms 原型，诊断、模拟演示、类定义查看和 JSON 导出。
- `src/Gs66Control.Cli`：跨平台模拟与 Windows 只读诊断命令行。
- `tests/Gs66Control.Tests`：无需硬件的单元测试。
- `docs/SAFETY.md`：硬件写入的安全门槛。
- `docs/dragon-center-re.md`：Dragon Center 静态逆向研究与证据索引。
- `scripts/inventory-dragon-center.py`：离线提取文件清单，不执行目标程序。
- `scripts/extract-acpi-tables.py` / `summarize-gs66-acpi.py`：离线固件候选表与 SCM0 映射提取。
- `AGENTS.md`：交给编程智能体时应遵守的开发约束。

## 开始开发

需要 .NET 8 SDK。可以在 macOS、Linux 或 Windows 上编译和测试：

```sh
dotnet restore src/Gs66Control.Cli/Gs66Control.Cli.csproj
dotnet build src/Gs66Control.Cli/Gs66Control.Cli.csproj
dotnet test tests/Gs66Control.Tests/Gs66Control.Tests.csproj
dotnet run --project src/Gs66Control.Cli -- simulate
```

`simulate` 只返回明确标记为模拟的数据，方便跨平台开发。Windows 实机上还可以运行：

```powershell
dotnet run --project src/Gs66Control.Cli -- diagnose > gs66-diagnostic.json
```

`diagnose` 只查询系统身份与 WMI 类定义，不读取厂商类实例，不会修改 BIOS、EC、风扇或电源策略。不保证所有 MSI WMI 类都能枚举到；空结果并不证明机器不支持相应控制。报告字段不收集序列号/账户或原始异常消息；型号和版本仍可能识别设备类型，请检查后再公开分享。

## 近期里程碑

1. Windows 实机只读诊断：确认实际硬件、固件版本、WMI 接口与可用性。
2. 加入只读传感器与 EC 数据采集（必须先验证可靠来源）。
3. 在明确的机型/固件白名单与人工批准下，单独验证控制接口和恢复路径。
4. 在已完成的诊断界面基础上，验证控制后再研究自动调优与常驻服务。

参考研究项目：[GhostDeck](https://github.com/wygodad/ghostdeck)、[msi-ec](https://github.com/BeardOverflow/msi-ec)、[MControlCenter](https://github.com/dmitry-s93/MControlCenter)。本仓库当前**没有移植它们的 EC 写入实现**。

本软件处于实验阶段，使用者应先阅读 [安全约束](docs/SAFETY.md)。
