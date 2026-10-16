# GS66 Control

面向 **MSI GS66 Stealth 10SE（i7-10750H / RTX 2060）** 的实验性硬件诊断与控制项目，长期目标是轻量替代 Dragon Center 中的功耗、风扇和性能管理功能。

**单一原生 Windows 控制台已实现监控、托盘、风扇方案、性能档和方案记录。** CPU/GPU 各六点速度可编辑，安静／散热滑杆生成可继续调整的方案，与性能档位分开；方案可保存、导入、导出，并从窗口或托盘应用。独立 helper 负责设备校验、真实回读、温度保护、父进程失联及恢复，主窗口以普通用户运行，不安装常驻管理员服务。原厂自动模式、CPU/GPU 曲线最小增量、C2/C0/C4 性能档已完成有界实机切换、独立回读及恢复；新版普通权限窗口与只读传感器 helper 已完成实机联调。

当前源码是开发快照，尚未作为稳定版本发布。2026-10-16 新增 Windows CPU 能效、睿频、频率上限和最大处理器状态控制，以及独显休眠识别；最新 macOS CLI 构建与 176 项单元测试通过。此前 Windows 构建通过 174 项测试，最新运行时采集优化仍需重新在 Windows 验证。当晚用户报告 CPU-Z 多核约 700 分；远程连接不可用，当前性能状态、原因和恢复结果尚未核实，温控实验已暂停。详见 [当前开发状态](docs/development-status.md)。

目标是找到适合实际工作负载的温度／转速／性能平衡。方案记录保存真实温度、风扇 RPM、CPU 负载和供电状态，样本不足、断流或负载不可比时拒绝比较；不生成分贝或性能跑分。充电上限暂未找到已证明可用的受限接口，界面没有伪造控制；温度断点写入、第三路风扇、功耗限制与降压也未启用。使用方式及验证边界见 [桌面控制台](docs/desktop.md) 和 [当前开发状态](docs/development-status.md)。

Dragon Center 的离线静态研究已整理至 [逆向研究报告](docs/dragon-center-re.md)，包含官方安装包版本与哈希、UI 到后台及硬件访问边界的调用链、GS66 控制能力矩阵和后续只读验证方案。对应的 [证据索引](docs/evidence/dragon-center/README.md) 保存文件清单、REA/ILSpy/Ghidra 分析片段；当前官方通用包尚未确认为 2020 年预装版本，研究结论也不代表 GS66 实机验证成功。

## 结构

- `src/Gs66Control.Core`：跨平台数据模型、硬件探针接口、模拟后端。
- `src/Gs66Control.Windows`：Windows 只读诊断/遥测，以及隔离的固定 WMI 控制与现有 Windows 电源计划接口。
- `src/Gs66Control.Helper`：自动模式、曲线／性能事务、独立恢复进程与故障模拟。
- `src/Gs66Control.Cli`：只读诊断命令行入口。
- `src/Gs66Control.Desktop`：Windows WPF 桌面窗口、托盘、快捷键和方案管理。
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

报告中的 `msiWmiClasses` 仅匹配 `MSI_` 前缀，避免混入 Windows 的 `MSiSCSI` 存储类。`msiWmiSchema` 包含属性类型、数组/key 标记、方法输入输出参数和实例数量；只读取元数据与枚举实例，不调用任何方法，也不输出实例 key 的值。实例数量为 `null` 表示查询失败，`0` 表示未发现实例，两者都会附带警告。

`embeddedControllerVersion` 仅表示 Win32_BIOS 的 SMBIOS EC 主/次版本字段。任一字段缺失、无效或为未知标记 `255` 时返回 `null`，不会猜测 MSI 的 `16V1EMS1.xxx` 固件标识。独立的 `msiEcFirmware` 字段从已核对的 MSI_Software 属性解析报告标签，当前为 `16V1EMS1.109`；源字段和构建文本保留来源，不等同于写入验证。`diagnose` 身份报告本身不提供传感器或写入能力；桌面监控另行读取标准 Windows 指标与已核对的 MSI_AP 计数。实机验证记录见 [Windows 验证说明](docs/windows-validation.md)。

## 近期里程碑

核心、CLI、helper 与单元测试保持跨平台可运行；桌面代码可交叉编译，实际运行要求 Windows。2026-10-15 Windows 验证通过 151 项单元测试、24 项曲线事务模拟、2 项曲线跨进程故障模拟和 25 项自动模式跨进程模拟。真实 UI 验收通过方案滑杆、保存导入、记录格式、原生电源计划枚举与传感器读取；曲线及性能档的五秒试验均核对原状态恢复。实际噪声、持续降温效果及最佳工作档仍需日常负载记录，不从字段回读推断。

固件与原厂程序集的静态交叉证据已确认更多控制入口，详见 [固件接口映射](docs/evidence/firmware-control-map.md) 和 [能力研究](docs/evidence/firmware-capabilities.md)。其中曲线温度断点存在固件 setter，MSI_AP 的“全量只读枚举”却会触发协议字节写入：诊断已限制到审查过的类，风扇计数只读取固定的 2–5 项。发现接口不等于授权启用或完成实机验证。

发布包可离线生成：`python3 scripts/package-windows.py --output captures/releases/<新目录>`。包内 `verify-release.ps1` 默认仅做文件校验、模拟、真实只读界面 smoke 与控制预览；`install-release.ps1` 校验后安装到新目录并更新桌面快捷方式，不删除旧版本或设置。PC 上的具体验收步骤见 [验证计划](docs/control-validation-plan.md)。

参考研究项目：[GhostDeck](https://github.com/wygodad/ghostdeck)、[msi-ec](https://github.com/BeardOverflow/msi-ec)、[MControlCenter](https://github.com/dmitry-s93/MControlCenter)。本仓库当前**没有移植它们的 EC 写入实现**。

本软件处于实验阶段，使用者应先阅读 [安全约束](docs/SAFETY.md)。
