# GS66 Windows 验证记录

以下前半部分是 2026-10-13 初始只读阶段记录，后续状态见末尾更新。通过既有 SSH 连接，对目标 GS66 进行了只读 WMI 核对。以下为去标识化结论，原始捕获位于 Git 忽略的 `captures/`，未包含在仓库中。没有调用 WMI 方法、写入 EC、切换模式、启停服务或运行压力测试。

## 已确认的硬件信息与接口

新 CLI 的实机报告确认 GS66 Stealth 10SE、i7-10750H 和 NVIDIA RTX 2060，并如实列出 Intel UHD Graphics 及 Parsec 虚拟显示适配器。Win32_BIOS 报告 BIOS `E16V1IMS.10C`。SMBIOS 的 EmbeddedControllerMajorVersion/MinorVersion 均为 `255`，因此实际 MSI EC 固件标识仍然未知，不能据此匹配 `16V1EMS1.118` 或其他固件白名单。

新 CLI 在 Windows 的 System.Management 查询结果与此前 PowerShell 的 Get-CimClass/Get-CimInstance 核对结果一致。报告识别了 11 个 `MSI_` 类，排除了 Windows 的 MSiSCSI/MSIde 存储类。实例数量不代表风扇数量或控制通道数，也没有验证实例顺序对应的功能。

| root/WMI 类 | 可枚举实例数 | 方法元数据 |
| --- | ---: | --- |
| MSI_CPU | 19 | 未报告方法 |
| MSI_VGA | 18 | 未报告方法 |
| MSI_System | 21 | 未报告方法 |
| MSI_AP | 8 | 未报告方法 |
| MSI_ACPI | 未知，枚举返回“不支持”（0x8004100c） | 29 个方法，仅元数据 |
| MSI_Device | 4 | 未报告方法 |
| MSI_Event | 0，附带警告 | 未报告方法 |
| MSI_Master_Battery | 16 | 未报告方法 |
| MSI_Power | 3 | 未报告方法 |
| MSI_Slave_Battery | 14 | 未报告方法 |
| MSI_Software | 34 | 未报告方法 |

MSI_ACPI 的 schema 可读不意味着其实例可枚举，更不意味着可以安全调用任何 Get/Set 方法。当前没有调用这些方法。Win32_Service 查询显示 MSI Foundation 与 MSI Center 相关服务正在运行；这些服务保持原状态。这些结果不能证明之前分析的通用 Dragon Center 包与本机安装组件完全一致。

## 实现与验证边界

本次诊断增强修正了 MSI 类名筛选：`MSI_` 前缀不会误匹配 `MSiSCSI` 或 `MSIde`。新增 EC SMBIOS 版本的未知值处理，以及不输出实例值的属性、方法参数和实例数量报告。查询失败或空值/零实例会产生明确 warning，实例查询失败时数量保留 `null`。

macOS 使用本地 .NET SDK 8.0.425 编译 CLI 成功，0 warnings/0 errors；单元测试 14/14 通过，模拟报告和非 Windows 平台保护检查通过。测试不访问硬件，CI 仍只运行编译、单元测试和模拟模式。

GS66 使用官方便携式 .NET SDK 8.0.425，位于 `%LOCALAPPDATA%\gs66-control-validation\dotnet8`；通过发布元数据中的 SHA-512 校验后解压，没有替换原有 .NET 6 Runtime。独立工作目录为 `%LOCALAPPDATA%\gs66-control-validation\run-20261013`。Windows CLI 编译成功，0 warnings/0 errors；14/14 单元测试通过，模拟与实机模式均运行成功。14 个源码/项目配置文件的 SHA-256 与 Mac 当前工作区逐一一致。

实机 JSON 明确报告 `source=windows-wmi`、`isSimulated=false`、`embeddedControllerVersion=null`，保留 MSI_ACPI 实例枚举失败及 MSI_Event 零实例的 warning；未输出实例 key 值。该结果验证了查询代码与失败处理，尚未测量风扇 RPM、曲线、温度或原始 EC，也没有确认任何写入能力。

后续在 Windows 工作目录中可使用便携 SDK 重跑以下命令。`diagnose` 会访问实际硬件；单元测试与 `simulate` 不访问硬件。原始报告继续保存在忽略目录，不能提交机器标识或完整 WMI dump。

```powershell
$sdk = "$env:LOCALAPPDATA\gs66-control-validation\dotnet8\dotnet.exe"
Set-Location "$env:LOCALAPPDATA\gs66-control-validation\run-20261013"
& $sdk build src/Gs66Control.Cli/Gs66Control.Cli.csproj
& $sdk test tests/Gs66Control.Tests/Gs66Control.Tests.csproj
& $sdk run --project src/Gs66Control.Cli -- simulate
& $sdk run --project src/Gs66Control.Cli -- diagnose > gs66-diagnostic.json
```

## 桌面与遥测进展

后续增加 Windows WPF 桌面版和 WindowsTelemetryProbe，仍只查询 WMI 属性、不调用 WMI 方法。CLI 与桌面在 Windows 编译通过，Core 测试增加至 24 项，全部通过。桌面实机 smoke 测试验证监控、方案保存/校验、设置持久化和历史曲线；风扇 RPM 为原厂公式换算，详情见 [桌面使用说明](desktop.md) 和 [遥测来源](telemetry-provenance.md)。原先身份报告不提供风扇读数的结论仍适用于 diagnose，桌面遥测是独立后端。

## 只读温度与固件标签验证

本机安装的 System Monitor artifact 已另行校验与离线分析：其 CPU/VGA ordinal 1 为摄氏度实时温度，0/255 返回不可用。桌面复现实机 WMI 属性读取，本次采样 CPU 50°C、GPU 42°C；没有调用原厂构造或加载硬件库。进一步读取 MSI_Software 现有属性得到 `16V1EMS1.1090311202011:16:49`，其中版本 `16V1EMS1.109` 由 WMI 报告，构建文本保持原样、不猜日期格式；因此早期“EC 固件未知”是当时标准 SMBIOS/身份查询的有界结论。

Get_WMI/Get_EC 的批准尝试与单次批准重试均没有得到可解析版本包，修复版完整记录 Not supported。原始错误、字节数组与软件组件路径不入仓库。MSIAPService 历史日志将 BIOS 标签写成 EC_Version，不能将 `E16V1IMS.10C` 当成 EC 版本。原厂版本读取的物理内存路径可能触发 native I/O 初始化，本项目没有执行；FW 身份来自 MSI_Software。

macOS/Windows CLI、桌面编译无警告/错误，测试增加至 34 项，固件解析、未知/非 ASCII/不完整数据处理均纳入测试。控制写入次数仍为零；当前只读自动模式预览为 System ordinal 9：141→13，计划详见 [自动模式试验](fan-mode-trial.md)。
## 受限自动模式 helper（首次试验前的历史记录）

发布版在 17:38 再次只读预检：CPU 53°C、GPU 45°C，模式 141，候选 13，全部准入检查通过。硬件写入开关仍为 false，真实写入次数为零。之前的 GPU 不可用结果保留为拒绝路径验证记录。

GS66 上 CLI、桌面与 helper 编译成功，零警告/错误；47 项单元测试、21 项独立进程模拟全部通过。模拟不访问真实硬件。实际 dry-run 仅查询身份、固件、schema、模式和温度，并读取原厂 Base Module 文件的 SHA-256，不执行原厂程序集。模式 141 的自动候选值为 13；当次 GPU 原厂 WMI 温度不可用，预检拒绝试验。桌面新增控制验证页，硬件写入保持锁定，真实写入次数为零。细节见 [自动模式验证](fan-mode-trial.md)。


## 实机自动模式与桌面发布更新

18:14 完成已获批的单次 5 秒试验，真实更新两次：141→13→141，独立 worker 结果与随后只读查询均确认恢复。准入 CPU 53°C/GPU 44°C，结束后 CPU 49°C/GPU 45°C。外层包装器丢失退出码的错误保留，成功依据为独立结果和回读；没有重跑试验。临时启用构建已删除，普通 trial 入口保持锁定，详情见 [完整证据](fan-mode-trial.md)。

后续 macOS/Windows 各通过 56 项单元测试、25 项独立进程模拟；Windows CLI/helper/桌面编译、35 项源码/项目文件 SHA-256 核对及四页真实只读 smoke 均通过。独显空闲预检得到 GPU 42°C，来源为已核对的 NVIDIA 工具，mode=141、Eligible=true、预检写入零次。温度解析测试拒绝未知值、错误型号、多个 GPU、单位后缀与多余字段。网络偶发导致 NuGet advisory feed 查询告警；没有更改系统代理、DNS 或路由。

最新发布包含固定自动模式 helper 和托盘快捷入口，快捷方式指向 `app-20261013-auto-control-tray`。日常自动动作仅用户主动触发并按次请求权限；agent 没有为验收再调用真实 apply。后台构建/只读 smoke 使用有限权限的一次性用户任务，日志与状态持久化，避免依赖长 SSH 会话。曲线、Boost、充电与功耗仍未验证，MSI 服务保持原状态。

## 实机曲线草稿更新

后续只读核对确认原厂保存的两条曲线首温度点均为 0。新增固定索引的 WindowsSavedCurveProbe 和跨平台解析器，拒绝未核对来源、变化快照或无法原样表示的数据。桌面支持从实机曲线新建、原始 0 点保存/导出和随编辑更新的图形，没有增加曲线写入方法。最新测试增加至 64 项，之前 25 项进程模拟覆盖的自动模式逻辑未变。最新发布目标为 `app-20261013-saved-curves`，原始验收日志保留在忽略目录。

## 极简原生界面更新

根据用户最新偏好，统一为一套浅色原生窗口与 WPF 标准控件，删除原先深色控件模板、大段说明和重复状态。新增左侧导航、图标操作及其悬停提示/自动化名称，详细来源和失败原因留在诊断折叠区。没有云母适配或双套 UI。窗口初始尺寸适配可用屏幕；状态检查失败时清除旧的成功标记，风扇动作仍由既有独立 helper 执行。

macOS CLI/桌面编译与 64 项测试通过；GS66 Windows 桌面编译零警告/错误，39 个源码/项目文件传输核对通过。四页只读 smoke 验证导航与页面一致、图标可访问性、方案/设置保存、非法曲线拒绝、实机曲线导入和真实遥测，未触发硬件应用。发布到 `app-20261013-native` 并更新桌面快捷方式，后台任务完成后删除。截图与原始验收结果保存在忽略的 captures。


## 2026-10-14 离线控制与固件证据更新

原厂组件、实际 DSDT 和 WMI schema 已交叉确认曲线速度、Shift 性能值及温度断点 setter；发现 MSI_AP index 1 的读取存在协议字节写入副作用后，诊断与遥测改为审查过的类和固定风扇计数 2–5。实际安全计数读取和新曲线基线在关机前完成；不重试已消耗的 Get_WMI/Get_EC/Get_Data 探测。固件结论见 [接口映射](evidence/firmware-control-map.md)。

关机前 Windows v3 编译／120 项单元测试、19 项曲线模拟、2 项曲线跨进程模拟及原生界面只读 smoke 通过；发布 helper 通过，控制预览 CPU 48°C、GPU 44°C、风扇模式 141、Shift 193，Eligible=true、显式写入 0。没有执行新曲线或性能写入。

关机后完成方案滑杆、真实样本记录比较、Windows 原生电源计划、托盘方案应用、独立恢复接管、超时／并发状态修复和统一程序图标。离线测试增加至 146 项，24 项曲线模拟及 2 项跨进程故障模拟通过，旧自动模式 25 项进程模拟仍通过。最新 Windows 运行、截图验收、新写入与真实资源开销尚待开机验证；没有尝试连接已关闭的 PC。完整功能与验收矩阵见 [开发状态](development-status.md)。

## 2026-10-15 至 16 日实机验收

Windows 最新构建通过 151 项单元测试、24 项曲线事务模拟、2 项跨进程曲线模拟及 25 项自动模式跨进程模拟。编译无代码错误；NuGet 漏洞信息源不可达产生 NU1900 网络告警。模拟结果的文件发布改为临时文件加原子移动，读取对短暂文件共享冲突有界重试，保留原有断言。该改动解决实际 Windows 上的共享冲突，不把失败当通过。

普通权限 WPF 窗口与单独提升权限的固定只读采集进程完成联调。真实四页 smoke 验证导航、图标可访问名称、曲线与平衡滑杆、草稿保存导入、记录格式、原生电源计划枚举和实际读数；一次验收得到 CPU 49°C、GPU 44°C、两路 RPM 2096/0，非模拟、无传感器警告。周期采集只访问已审查的 AP 2–5 与 CPU/VGA 温度属性；控制快照按 nonce 关联且仅按需取得，所有快照验证时效、来源、设备／固件与结构。采集进程在停止标志或父进程心跳失效后退出，实机只读进程退出码 0、显式硬件写入 0。普通窗口不能可靠访问外部提升进程的 HasExited／StartTime，实际实现使用受限会话文件的新鲜度，不因 Windows 进程权限错误把有效读数丢弃。

硬件试验按固定五秒、最小改动、独立全状态核对执行，成功结果如下。每次均观察到目标字段，最后核对全部十二个速度值、十二个温度断点、风扇模式与 Shift 回到完整基线；没有执行压力负载或更改 MSI 服务。

| 项目 | 目标变化 | 实机结果 |
| --- | --- | --- |
| CPU 曲线 | 第二速度点 50→51，五秒后恢复 50 | 目标与完整恢复均确认 |
| GPU 曲线 | 第二速度点 50→51，五秒后恢复 50 | 目标与完整恢复均确认 |
| 省电档 | Shift C1→C2→C1 | 目标与完整恢复均确认 |
| 性能档 | Shift C1→C0→C1 | 目标与完整恢复均确认 |
| 极速档 | Shift C1→C4→C1 | 目标与完整恢复均确认 |

首次 CPU 试验的 WMI 更新返回“无效的对象”，自动模式回退也失败，结果保留 RecoveryFailed；当次与其后的独立完整快照均没有观察到基线改变。写入实现改为仅对已审查 CPU/VGA/System 类使用完整实例查询，并让 searcher、结果集合及对象在 Put 完成前保持有效；固定属性、实例数量／顺序检查、比较后更新、超时、UpdateOnly 和独立回读约束不变。修复后上述 CPU/GPU 试验成功，不能把首次失败写成“已恢复”。完整实例更新依据微软 [Updating an Entire Instance](https://learn.microsoft.com/en-us/windows/win32/wmisdk/updating-an-entire-instance) 与 [IWbemServices::PutInstance](https://learn.microsoft.com/en-us/windows/win32/api/wbemcli/nf-wbemcli-iwbemservices-putinstance)；本次合并修复不能单独证明投影查询是唯一原因。

2026-10-16 重新只读核对 CPU 44°C、GPU 41°C、原曲线、模式 141 与 Shift 193，且没有活动控制进程后，把这一条已知失败试验的完整请求／日志移至独立验证审计目录，保留原始失败结果及归档依据，避免日常窗口对已确认未改变的测试事务继续显示待恢复。没有借归档执行新的硬件操作，也没有重复已成功的试验。

Windows 电源计划另以普通权限调用实际 PowrProf 后端：平衡→电脑已存在的卓越性能计划，五秒后恢复平衡，目标回读与恢复通过，IsElevated=false。第一次验收只接受节能／平衡两个预设 GUID，未找到备用计划，故在切换前停止；核对本机实际列表后修正验收目标，没有创建、删除或编辑计划。

2026-10-16 安装前将全部 659 项发布文件与前一天实机验收时保存的清单逐一核对，再由普通权限安装器复制到新版本目录并更新桌面快捷方式。初次安装前检查误以为只读试验目录包含桌面二进制，检查阶段停止，没有安装；改用已保存的完整验收清单后安装通过。当前桌面快捷方式指向本次 v7 构建；主窗口位于交互会话、普通权限且响应正常，旧目录与设置保留。启动稳定后约 20 秒采样得到主窗口 CPU 占整机平均 0.182%（12 个逻辑处理器归一化），工作集 150.1 MiB，当时辅助进程数量为零；这不包含开启只读采集／控制事务后的开销，也没有测量 WmiPrvSE 的归属成本，不能作为优于 Dragon Center 的比较结论。

这些证据证明受限字段事务、恢复与界面路径可运行，不证明风扇百分比／RPM 标定、持续噪声、功耗或性能收益。充电接口仍未取得支持证据，温度断点写入、第三风扇、功耗限制及降压保持未启用。原始日志、界面截图和设备捕获存于忽略目录，不能提交完整 WMI dump 或设备标识。
