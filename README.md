# GS66 Control

面向 **MSI GS66 Stealth 10SE（i7-10750H / RTX 2060）** 的实验性硬件诊断与控制项目，长期目标是轻量替代 Dragon Center 中的功耗、风扇和性能管理功能。

**当前只有脚手架与只读诊断能力。** 尚不能读取原始 EC 数据，也不能读取风扇转速或修改风扇、CPU/GPU 功耗。不要将模拟数据当作实测结果。

Dragon Center 的离线静态研究已整理至 [逆向研究报告](docs/dragon-center-re.md)，包含官方安装包版本与哈希、UI 到后台及硬件访问边界的调用链、GS66 控制能力矩阵和后续只读验证方案。对应的 [证据索引](docs/evidence/dragon-center/README.md) 保存文件清单、REA/ILSpy/Ghidra 分析片段；当前官方通用包尚未确认为 2020 年预装版本，研究结论也不代表 GS66 实机验证成功。

## 结构

- `src/Gs66Control.Core`：跨平台数据模型、硬件探针接口、模拟后端。
- `src/Gs66Control.Windows`：Windows WMI 只读查询（系统型号、CPU/GPU、BIOS、MSI 相关 WMI 类）。
- `src/Gs66Control.Cli`：命令行入口；暂时不做界面。
- `tests/Gs66Control.Tests`：无需硬件的单元测试。
- `docs/SAFETY.md`：硬件写入的安全门槛。
- `docs/dragon-center-re.md`：Dragon Center 静态逆向研究与证据索引。
- `scripts/inventory-dragon-center.py`：离线提取文件清单，不执行目标程序。
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

`diagnose` 只查询 WMI 信息，不会修改 BIOS、EC、风扇或电源策略。不保证所有 MSI WMI 类都能枚举到；空结果并不证明机器不支持相应控制。诊断报告可能包含机器标识相关信息，请检查后再公开分享。

## 近期里程碑

1. Windows 实机只读诊断：确认实际硬件、固件版本、WMI 接口与可用性。
2. 加入只读传感器与 EC 数据采集（必须先验证可靠来源）。
3. 在明确的机型/固件白名单与人工批准下，单独验证控制接口和恢复路径。
4. 最后才开发自动调优、常驻服务和图形界面。

参考研究项目：[GhostDeck](https://github.com/wygodad/ghostdeck)、[msi-ec](https://github.com/BeardOverflow/msi-ec)、[MControlCenter](https://github.com/dmitry-s93/MControlCenter)。本仓库当前**没有移植它们的 EC 写入实现**。

本软件处于实验阶段，使用者应先阅读 [安全约束](docs/SAFETY.md)。
