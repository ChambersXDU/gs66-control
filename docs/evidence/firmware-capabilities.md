# GS66 固件能力证据：电池充电控制

本文只记录目标机 MSI GS66 Stealth 10SE 的离线 DSDT 反编译结果，以及已保存的原厂程序集静态证据。目标固件身份是 BIOS `E16V1IMS.10C`、MSI_Software EC 标签 `16V1EMS1.109`；这些绑定只适用于本机。分析没有执行 AML、调用 WMI 方法、加载原厂服务或写入 EC。`captures/gs66-dsdt.dsl` 是本地分析输入，原始固件转储不作为功能实现依据提交。

## DSDT 中实际存在的 WMI 设备

DSDT 中有三个 `PNP0C14` 设备，但它们不是同一个接口。`WMTF` 的 UID 是 `TBFP`，只包含一个 WDG 记录，方法 `WMTF` 根据参数切换 `TBFP`，位置在 `captures/gs66-dsdt.dsl:20695-20716`。`WFDE` 的 UID 是 `SampleDev`，有自己的 `WMDE`、`_WED` 和大块 `WQCC` 数据，位置在 `captures/gs66-dsdt.dsl:38274-38330`；它没有电池充电上限语义。

原厂 WMI2 包装器的候选目标路径使用 `root\\WMI` 和 `MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'`，位置在 `/tmp/gs66-research/decompiled/MSIWMIACPI2.cs:187-196`。DSDT 另有 `SCM0`：它被声明为 `PNP0C14`、UID `0`，并提供 0xC8 字节的 `_WDG`，位置在 `captures/gs66-dsdt.dsl:57617-57649`。现有证据没有把 `MSI_ACPI` 的 Windows provider 身份、`_WDG` 对象记录、WQ/WS 方法名建立成一一映射，因此这里只记录相似的设备身份线索，不把 `MSI_ACPI` 路径解释成已证实的 SCM0 对象入口，也不据此推断 D7、EF 或任何 WQ/WS 语义。

SCM0 的 WDG 包含九个普通对象 `AA` 到 `AI`，记录的实例数依次是 `34, 4, 3, 16, 14, 19, 18, 21, 8`，以及一个事件对象 `0xC0`。这些对象的原始记录位于 `captures/gs66-dsdt.dsl:57622-57648`；对象数量分别与 `WQAA` 到 `WQAI` 的分支数量相符。`WQAA` 到 `WQAI` 是读方法，位置为 `captures/gs66-dsdt.dsl:57802-58650`；对应的 `WSAA` 到 `WSAI` 是写方法，位置为 `captures/gs66-dsdt.dsl:58652-59499`。这证明固件有一组通用 EC 字段访问入口，但没有证明这些字段是充电上限。

## D7 和 EF 的静态映射结论

SCM0 的 EmbeddedControl 字段从 EC 偏移 `0x2B` 开始，命名字段覆盖若干离散区间。`TDEE` 位于偏移 `0xEE`，`TDFE` 位于偏移 `0xFE`；字段声明在 `captures/gs66-dsdt.dsl:57650-57800`。因此这里没有名为 `TDD7` 或 `TDEF` 的字段，且 `TDFE` 是 `0xFE`，不能按字符串外观误读成 `0xEF`。`WQAA` 的索引 2 读取 `TDEE`，见 `captures/gs66-dsdt.dsl:57816-57820`；`WQAH` 的索引 5 读取 `TDFE`，见 `captures/gs66-dsdt.dsl:58503-58507`。这两个入口都不是电池 D7/EF 证明。

主 EC 区域也没有给出充电上限字段。电池相关字段中，`GPST` 位于 `0xD6`，随后直接跳到 `TSIT` 的 `0xD8`，因此 DSDT 没有命名的 `0xD7` 字段；其余相关字段和空洞位于 `captures/gs66-dsdt.dsl:56797-56929`，对应行包括 `56906-56915` 和 `56921-56928`。这只能说明 DSDT 没有公开一个名为 D7/EF 的字段，不能把 D7 WMI 参数等同于 EC 地址，也不能据此猜测邻近字节。

原厂 `API_NB_Battery.cs` 的 D7 是一个抽象的 WMI2 `Get_Data`/`Set_Data` 参数：读取和 0xD7 参数在 `/tmp/gs66-research/decompiled/API_NB_Battery.cs:1162-1181`，读改写 60/80/100 并保留 bit7 在 `:1184-1238`。旧 WMI1 分支则通过私有服务管道访问 `0xFC0008EF`，位置在 `:1240-1268`；这不是 DSDT 暴露的普通 ACPI 属性，不能作为本项目的替代实现。原厂包装器将 `Get_Data` 和 `Set_Data` 的有效数据长度固定为一个字节，位置在 `/tmp/gs66-research/decompiled/MSIWMIACPI2.cs:943-1007`；返回值的首字节是状态标志，后续数据被复制到固定缓冲区，位置在 `:1015-1033`。

上游验证流程已经对目标机的 `Get_Data(0xD7)` 做过一次有界只读调用，返回 `Not supported`；本文件不重复调用。结合该运行时结果与 DSDT 没有 D7/EF 命名字段的静态结果，目前没有经过实机证明的替代充电控制入口。这里的 `Not supported` 是运行时事实，不能反推所有 WQ/WS 字段都不可用；反过来也不能把 WQ/WS 的存在当成充电控制能力。

## 原厂电池模块和 MSI Foundation Service 的真实边界

原厂电池模块的 “AI Charger” 代码不是充电阈值写入器。`API_NB_Battery.cs` 在 `/tmp/gs66-research/decompiled/API_NB_Battery.cs:455-641` 和 `:874-922` 创建或读取 `BatteryMode`、`AIChargerEnableDate`、`DaysForAnalysisData` 等注册表值，注册表变化 watcher，并在日期条件满足时启动 `MSI.NotifyServer.exe ShowUI msi-bc:system_diagnosis`。这些路径只维护分析状态或打开诊断界面，没有向 WMI、ACPI、EC 或电池控制器写入 60/80/100 阈值。该文件中真正接触抽象 D7 的路径仍只有 `:1162-1238`；更早的 WMI1 分支 `:1240-1268` 是私有物理内存访问，不能作为本项目后端。

电池模块使用的 `MSI_SERVICE_2` 是私有特权协议，管道名和连接/握手代码位于 `/tmp/gs66-research/decompiled/NamedPipeClientLib-battery.cs:100-106`、`:1236-1345`。握手发送 `HS:REQ:<product>;<version>`，成功后还会设置 WMI 事件 watcher；这只能证明客户端与服务协商并注册事件，不能证明某个命令的硬件语义，也不构成普通的只读设备接口。该库同时暴露物理内存和端口读写 `:1347-1449`，以及注册表写入 `:1697-1744`，因此项目不能把管道连接或其命令标签当作安全替代路径。

已安装 `MSIAPService` 的正常启动本身也不是无副作用的只读初始化。`/tmp/gs66-research/decompiled/MSIAPService-installed.cs:2888-2956` 显示，服务启动线程加载 WMI、调用 `WMI.writeWMI("root\\WMI", "MSI_Software", "Software", 0, "1")`，在 WMI2 分支读取 `Get_Data` 参数 `217`（`0xD9`），若返回标志满足条件再写回 `Set_Data` 参数 `217` 的首字节或上 `1`，并调用 `WriteRegistryRestartPC()`；WMI1 分支也可能写入重启注册表标志。`WriteRegistryRestartPC` 在 `:2823-2877` 创建或写入 MSI One Dragon Center/MSI Center SDK 的 `Restart_PC=1`。服务还会启动命名管道服务器线程；WMI 原生库加载路径在 `:4548-4607`，原始端口/物理内存 I/O 所需的 `KernCoreLib64.dll` 初始化在 `:4471-4546`，不能在本项目中触发。

服务中 `Get_Power` 的包装位于 `/tmp/gs66-research/decompiled/MSIAPService-installed.cs:2327-2366`，对应的通用设置路由在 `:3013-3016` 和 `:3075-3081`。目前只知道它接收或返回一个三字节数据块，没有实机 schema、字段含义或充电阈值证明；“Power” 这个方法名不能赋予它充电控制语义。综合服务启动写入、私有特权协议和未定义的通用 payload，当前没有可接受的“只读连接后顺便使用”的服务替代方案；本项目不启动、连接或调用原厂服务和私有管道。

## 标准电池接口和可扩展能力

DSDT 的 `BAT1` 是标准 `PNP0C0A` Control Method Battery。`_BIF` 与 `_BST` 只返回电池信息和状态，位置在 `captures/gs66-dsdt.dsl:56981-57035`；`UPBI` 和 `UPBS` 从容量、电压、电流、剩余量等 EC 字段组装这些只读结果，位置在 `:57057-57190`。这些方法没有设置充电停止阈值的参数或写回路径，因此可以作为普通电池遥测来源，不能作为 60/80/100 控制后端。

原厂服务对 `Get_MasterBattery` 和 `Get_SlaveBattery` 只按索引读取 2 或 8 个字节的数据块，位置在 `/tmp/gs66-research/decompiled/MSIAPService-installed.cs:2029-2123`。System Monitor 也只查询 `MSI_Master_Battery` 并把数值用于功率或电量计算，位置在 `/tmp/gs66-research/decompiled/API_NB_System_Monitor-installed.cs:5088-5092` 和 `:5910-5923`。这些证据支持遥测，不支持充电上限语义。原厂服务的通用 `Get_Data` 包装在 `MSIAPService-installed.cs:2471-2503`，与 D7 失败的实机结果一致时，不能自动转用未验证的参数。

因此当前能力清单应将充电控制标记为“不可用/无实机证明”，而不是显示一个可点击但不会工作的占位开关。可安全扩展的部分是：继续使用 `BAT1._BIF/_BST` 和已经验证的电池 WMI 数据做状态展示，并在能力元数据中保留 DSDT 对象计数、固件指纹和 D7 只读探针结果。只有在同一 BIOS/EC 绑定下取得成功标志、恰好一个有效数据字节、已识别的 60/80/100 当前值，并完成独立读回验证后，策略模型才可以把目标设为 eligible；DSDT 中的 WQ/WS 写方法和旧私有服务地址都不能绕过这些条件。

## Windows 电源计划和 CPU 功率边界

原厂程序确实包含 Windows 电源计划的读写路径，但它与电池充电阈值和 CPU PL1/PL2 是不同层次的能力。`API_NB_System_Monitor-installed.cs:5091-5093` 注册了 `MSI_Power`、`MSI_Master_Battery` 和 `Win32_PowerPlan` 查询；`API_NB_System_Monitor-installed.cs:5851-5879` 读取当前 `Win32_PowerPlan` 的 `ElementName`，这支持只读状态展示。`API_NB_Base_Module.cs:4094-4152` 等路径使用系统 `powercfg -getactivescheme` 和 `powercfg -setactive <GUID>`，说明可以把已有 Windows 电源计划作为一个可逆的操作系统级后端，但没有证明它会设置 MSI 固件中的性能模式或硬件功率上限。

安全的扩展边界是枚举并显示现有计划，用户主动选择一个已有 GUID 后调用系统 `powercfg -setactive`，保存切换前的 GUID，并在应用退出或明确恢复操作时切回原计划。实现不应创建或删除计划、修改计划内的 CPU/PCIe/GPU 子设置，也不应把计划名称包装成 “Turbo”“CPU 功率限制” 或其他未经证明的硬件能力。真实目标机仍需一次只读的当前计划查询；静态证据不能保证目标系统存在某个特定计划或其实际性能效果。

原厂的硬件性能路径不能作为上述后端的证明。`API_NB_Base_Module.cs:1186-1340` 的 Shift 模式在 WMI2 分支使用 `Set_Data` 参数 `0xD2`，旧分支使用 `\\.\\pipe\\simple`；代码中的 “EC address 0xD2” 是软件标签，不足以证明真实 EC 地址或 CPU 功率语义，已有整理见 `docs/evidence/dragon-center/firmware-control-map.md`。`MSI.CentralServer.cs:5590-5595` 使用 `SetProcessorIdleState` 和 `SetPowerPlan_GUID_V2` 做原厂恢复动作，`:5997-6053` 只列出 `API_Kernel.dll` 的导出；这些命名不能替代副作用、参数和实机结果证明，项目不加载该 DLL。当前没有 DSDT 或运行时证据证明 PL1、PL2、GPU 功耗上限或同等硬件控制入口，因此这部分保持 unavailable。

仓库中的 `src/Gs66Control.Windows/WindowsPowerPlanBackend.cs` 已按 Windows 官方 `PowrProf.dll` 接口整理出独立后端：使用 `PowerEnumerate` 的 `ACCESS_SCHEME=16`，最多接受 64 个已有 scheme GUID；使用 `PowerGetActiveScheme` 后通过 `LocalFree` 释放系统分配的 GUID；使用 `PowerReadFriendlyName` 读取不超过 4 KiB 的严格 UTF-16 名称；`PowerSetActiveScheme` 只接受刚刚枚举出的 GUID。激活流程会将新鲜读取的当前 GUID 与调用方的 expected-current 比较，再执行切换，并用独立的最终快照验证读回值。该后端记录生产适配器 `IsSimulated=false`，不读取或写入固件、EC、厂商服务或电源计划的单项设置。

该实现后续已完成 Windows 编译、原生枚举和真实 UI 验收；2026-10-16 普通权限下平衡→本机已有卓越性能计划，五秒后恢复平衡，目标回读与恢复通过，没有修改计划内容。接口依据 [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate)、[PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme)、[PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme) 和 [PowerReadFriendlyName](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreadfriendlyname) 的公开契约；它属于 Windows 电源计划，不是 MSI 性能模式或 CPU 功率控制，不能从切换成功推断实际性能收益。详见 [Windows 验证记录](../windows-validation.md)。

## 证据边界

上述 DSDT 结论来自 IASL 成功反编译后的 `captures/gs66-dsdt.dsl:21` 所示 `MSI_NB/MEGABOOK` 表头及指定行号，属于静态观察。原厂程序集路径和方法只说明软件如何尝试访问接口，不说明目标机一定实现了该分支；`MSI_ACPI` 与 DSDT `SCM0` 的关系仍只是设备身份层面的候选假设。真实设备的 D7 返回值来自单次已批准只读验证；没有执行 AML、没有调用 SCM0 的 `WS*` 写方法、没有启动或连接 MSIAPService/私有管道，也没有触碰旧服务的物理地址，因此本文没有为充电写入提供授权或实现依据。Windows 电源计划切换属于独立的操作系统级能力，不能作为固件充电或 CPU 功率控制的证明。
