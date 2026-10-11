# Dragon Center GameCenter-OC / GPU-OC 静态证据

本记录来自 Dragon Center 安装目录的离线 .NET 反编译。没有执行任何目标 PE，没有连接 Windows 实机，也没有进行 EC、驱动或 GPU 写入。反编译器输出是证据的便于阅读表示；判断以原始文件 SHA-256 与对应反编译行号为准。

## 样本与可复核性

| 文件 | SHA-256 | 说明 |
| --- | --- | --- |
| nbfoundation/pf/MSI/One Dragon Center/GameCenter-OC.exe | 7096d68310d46b3444c6d5a660ae57b6a5773d1161d57c99d55784e32e52f110 | PE32 .NET GUI；AssemblyFileVersion 2.0.2006.0401、AssemblyVersion 2.0.2006.401；目标 .NET Framework 4.6.1 |
| nbfoundation/pf/MSI/One Dragon Center/inteloverclockingsdk.dll | 59c61ef28ab6bbb61f9418a8342309fab6d18cebe448edea1f8286bd0ee54b02 | PE32 .NET；Intel XTU 客户端 SDK 边界 |
| nbfoundation/pf/MSI/One Dragon Center/GInf.dll | 92555c7f8f92953862ce5d6d6883193a758e1ba7085a0956012cbe5c5aa25857 | PE32+ x64 原生 DLL；GPU/NVIDIA 导出 |
| nbfoundation/app/gpuControl.exe | f065a27f00fe20342718e2612acdce95069791c19d5ac999efb7eabe0ddbc01d | PE32 .NET；独立 GPU OC helper |
| nbfoundation/app/GInf.dll | e0675578d0bf298fec13ec7f4d4a0b81081611f4fc18a3dc3da98b19e82d6ef2 | PE32+ x64 原生 DLL；app 目录副本，导出名/RVA 与 pf 副本一致 |

反编译输出位置为 /tmp/gs66-research/decompiled/GameCenter-OC.cs、inteloverclockingsdk.cs、API_NB_Base_Module.cs、UC_NB_Base_Module.cs 与 gpuControl.cs。下列行号指向这些输出文件；逐字片段、源文本哈希及 artifact SHA-256 保存在 [managed-exact.json](managed-exact.json)，以该 JSON 的 first_line/last_line 核对范围。

## 1. GameCenter-OC 是 CPU XTU 客户端

### 1.1 启动、事件入口和注册表输入

GameCenter-OC.cs:485-519在构造函数中查询 Win32_Processor 并累加 NumberOfCores；创建 IntelOverclockingLibrary；读取注册表

    HKLM\SOFTWARE\Wow6432Node\MSI\One Dragon Center\Component\Base Module\Scenario\CPU_Preset

当 CPU_Preset 存在时，它建立 RegistryValueChangeEvent，监听同一 Scenario 键的 OC 值。GameCenter-OC.cs:521-526 的 ContentRendered handler 隐藏窗口，随后调用 InitializeIntelOverClock 和 setIniXTU_OC。这里没有读取 GPU、VRAM、功耗上限或风扇字段。

GameCenter-OC.cs:541-582是 OC 事件分派。OC=21 只开关 CPU monitor 线程；OC=22 清零 OC 后调用 setXTU；OC 1 至 5 直接调用 setXTU。事件协议是 Base Module 写注册表后由 WMI RegistryValueChangeEvent 唤醒 helper，不是该进程直接接收前端 TCP。

### 1.2 CPU core ratio 调用链

GameCenter-OC.cs:584-684中的 setIniXTU_OC 和 setXTU 根据

    ...\Component\Base Module\User Scenario\Mode

选择 Scenario\High_CPU_Core_i、Scenario\User_CPU_Core_i，或者在 Mode 2/3/4 从 DefaultValue 恢复。每个值都通过 setIntelOC(CpuID[i], value) 进入 XTU 客户端。

GameCenter-OC.cs:528-539（包含在上述 485-540 focused snippet）先取得 ocLibrary.TuningLib.GetAvailableControls；只要存在 Name 非空的控制项，就以传入的 id/value 调用 Tune。该反编译结果显示这里没有按 item.Id 做匹配，实际控制 ID 来自 InitializeIntelOverClock 填充的 CpuID。

GameCenter-OC.cs:730-734是最终边界：

    ocLibrary.TuningLib.Tune(id, value, out bRequiresReboot);

GameCenter-OC.cs:755-930先调用 IsProcessorCoreTunable，再枚举 XTU 控制项。它仅把名称为 1 active core 至 14 active cores 的条目保存为 ActiveCoreID_N、ActiveCoreValue_N、DefaultCoreValue_N；GameCenter-OC.cs:869-930 的 addInfo 按 WMI coreCount 生成 CpuID 和 DefaultValue。GameCenter-OC.cs:932-1063还寻找 processor core ratio，但本文件中没有找到该字段的 GPU 或功耗调用。

证据结论（BIN）：GameCenter-OC.exe 的已恢复控制流是 CPU active-core ratio 的 Intel XTU 客户端。它并不直接暴露 PL1、PL2、CPU package power、频率锁定或 GPU/VRAM OC 的写入。XTU 服务是否接受某个控制项由服务端返回的 control list 决定。

## 2. IntelOverclockingSDK 的真实服务边界

inteloverclockingsdk.cs:18469-18515定义 ITuningLibrary。接口同时存在 IsProcessorCoreTunable、IsProcessorGraphicTunable、IsProcessorRingCacheTunable、IsMemoryTunable、ProposeChange、ApplyChanges、Tune 等 API，但这些是通用 SDK 能力，不能证明 GameCenter-OC 会调用每一个能力。

inteloverclockingsdk.cs:19738-19860显示：

    IsProcessorCoreTunable -> availableTuningControls 中 Category 包含 Processor
    IsProcessorGraphicTunable -> Category 包含 Graphics
    IsMemoryTunable -> Category 包含 Memory
    ProposeChange -> 将 ClientTuningProposal 转成 XtuTuningProposal 后交给 tuningLibraryConnection
    ApplyChanges -> tuningLibraryConnection.ApplyChanges(forceRestart, tuningDelay)

inteloverclockingsdk.cs:19936-19967中的 Tune 先 ProposeChange，再 ApplyChanges。inteloverclockingsdk.cs:19975-19985 取得控制列表；:20093-20142 建立 WCF NetNamedPipeBinding，并连接

    net.pipe://localhost/ExtremeTuningUtility/Tuning

这说明 CPU OC 的硬件/平台写入边界在 Extreme Tuning Utility 服务，不在 GameCenter-OC.exe 或 SDK 客户端本身。该证据没有恢复 XTU 服务内部如何修改 MSR、PL1/PL2 或 ACPI，因此不能把 Dragon Center 的性能模式等同于 PL1/PL2 写入。

## 3. i7-10750H 的 CPU XTU 支持判断

API_NB_Base_Module.cs:9187-9237中的 UserScenario.Check_CPU_OC 首先要求 DataCenter.PlatformInfo.MKT.Contains("GT")，随后 CPU 字符串必须满足 i9 且 hk，或精确包含 i7-10700kf、i9-10900kf、i9-9900k、i9-10900k、i7-10700k、i7-9700k。i7-10750H 不在该白名单中。即使平台字符串被实机确认包含 GT，该型号仍不匹配。

命中后，代码才把 isSupportXTU 设为 true 并初始化 CPU_Preset、CPU_Maximum、CPU_Minimum 以及 High_CPU_Core_i/User_CPU_Core_i。API_NB_Base_Module.cs:8711-8750显示只有 isSupportXTU 为真时才把 SupportOC_CPU 置为 1，检查 XTU3SERVICE 状态，并在服务运行后启动 GameCenter-OC.exe。

证据结论（BIN）：对这份 Dragon Center 构建，静态代码不支持 GS66 10SE 的 i7-10750H 走 Base Module 的 Intel XTU CPU OC 路径。实际机器的 MKT、CPU 字符串和安装时是否被其他组件覆盖仍应在 Windows 上只读核对；这不会改变该构建白名单中缺失 i7-10750H 的事实。

## 4. GPU/VRAM 注册表路径与 RTX 2060 预设

API_NB_Base_Module.cs:3508-3558（未单列 focused hash）声明 GPU_Core_Preset、GPU_VRAM_Preset、GPU_Core_Maximum/Minimum、GPU_VRAM_Maximum/Minimum、SupportOC_CPU 和 SupportOC_GPU 等数据字段。这些字段是 UI 与场景数据，不等于硬件已经被写入。

API_NB_Base_Module.cs:9333-9663 的 Check_NV_OC 先检查 GPU_Core_Preset 与 GPU_VRAM_Maximum 是否存在；没有时查询 Win32_VideoController 的 Name。RTX 2060 分支位于 API_NB_Base_Module.cs:9486-9500，其写入的是默认 UI 范围：

    GPU_Core_Preset=0, GPU_Core_Maximum=200, GPU_Core_Minimum=0
    GPU_VRAM_Preset=0, GPU_VRAM_Maximum=350, GPU_VRAM_Minimum=0
    High_GPU_Core=0, High_GPU_VRAM=0
    User_GPU_Core=0, User_GPU_VRAM=0

这些是对字符串 nvidia geforce rtx 2060 的软件预设，不能据此推断 GS66 10SE 实际功耗墙、硬件可用偏移或 NVIDIA 驱动接受了 OC。

API_NB_Base_Module.cs:8846-8878 的 GetOC_Data 从 Scenario index 1 读取 Performance_GPU_Core_Preset/Performance_GPU_VRAM_Preset，从 index 5 读取 User 等值。API_NB_Base_Module.cs:10438-10487的 SetOC_Data 则把 UI 序列化数据写回 High_GPU_Core/High_GPU_VRAM 或 User_GPU_Core/User_GPU_VRAM，最后写 Scenario\OC 并在风扇值变化时调用 setFan。该方法本身没有 GInf、SetGPUOC 或 SetMemoryOC 调用。

UC_NB_Base_Module.cs:8825-8929显示 GPU slider 由最大值和当前 High/User 值初始化；UxSaveBtn_Click 将 GPU_Core_Preset/GPU_VRAM_Preset 序列化后交给 C_Client.SendData 的 CMD_SetOC_Data。这个链条能证明“UI -> TCP/Base Module -> 注册表场景数据”，不能单独证明“UI -> NVIDIA 硬件寄存器”。

## 5. 独立 gpuControl.exe / GInf.dll 原生路径

gpuControl.cs:58-138定义 GPUInfoStruct，包含 pState、CoreClockMin/Max、MemoryClockMin/Max、GPUOverClocks、MemoryOverClocks、GPUClock、MemoryClock、CurrentTemperature；声明 GInf.dll 的 InitialAndGetCurrentG、GetGInfo、SetGPUOC(int delta)、SetMemoryOC(int delta)、Unload。MainWindow 构造函数调用 InitialAndGetCurrentG，要求两个命令行整数；ContentRendered 调用 SetGPUOC(gpu)，再调用 SetMemoryOC(memory)，关闭窗口并 Unload。

API_NB_Base_Module.cs:11755-11813 声明同一组 GInf P/Invoke，包括 SetGPUOC(int delta)、SetMemoryOC(int delta)，但 API_Dynamic._Feature.GPU_Control 的 GPU_SetGlobalPowerManagementModeSetting(bool) 方法体为空（:11811-11813）。对整个已反编译安装树搜索时，除声明和 gpuControl.exe 本身外没有找到 Base Module 启动 gpuControl.exe 或调用 SetGPUOC/SetMemoryOC 的已恢复调用者。

两个 GInf.dll 副本的原生导出包含 CreateProfileAndSetGPU、SetGPUOC、SetMemoryOC、GetGInfo、InitialAndGetCurrentG、SetHighPerformanceProfile、SetGlobalPowerManagementModeSetting 等；静态导入/字符串还包含 nvapi_QueryInterface、NvAPI_GPU_GetPstates20 与 NVIDIA 错误字符串。导出函数的具体参数语义、偏移单位、是否受驱动版本/显卡 VBIOS 限制，仍需在 Windows 上对原生 DLL 做不执行的 PE/反汇编比对或在隔离实机上只读验证。原生反汇编看到 SetGPUOC/SetMemoryOC 构造内部结构并调用 GPU 对象，但本记录不把立即数或结构字段命名为已确认的寄存器。

证据结论（BIN）：Dragon Center 安装树同时包含一个潜在的 NVIDIA OC 原生边界和一个会直接调用它的独立 helper；证据没有闭合从当前 UI TCP 命令到 helper 的调用链。应把 GPU/VRAM 控制标成“存在静态入口，当前构建的调用者未恢复”，而不是声称已经验证可用。

## 6. 结论分级与后续 Windows 只读核验

已由二进制证实（BIN）：GameCenter-OC 的可见调用链为 Scenario 注册表事件 -> Mode/High_CPU_Core_i 或 User_CPU_Core_i -> XTU ITuningLibrary.Tune -> WCF named pipe ExtremeTuningUtility/Tuning；Base Module 仅为指定 CPU/MKT 设置 SupportOC_CPU；RTX 2060 分支只建立 GPU/VRAM slider 的注册表预设；GInf/gpuControl 提供独立 SetGPUOC/SetMemoryOC 入口。

由公开源码或其他代际项目支持（OPEN）：16V1EMS1、G1 机型上的 EC/风扇寄存器映射不能从本文件的 GPU OC 预设推出，也不能把其他代际寄存器地址迁移到 GS66 10SE。相关公开项目只能作为候选假设，必须以 GS66 实机 BIOS/EC 版本与 Dragon Center 运行时调用证据交叉验证。

尚待 Windows 实机验证（UNVERIFIED）：实际 GS66 的 PlatformInfo.MKT 与 Processor 字符串；XTU3SERVICE 是否存在及其返回控制列表；GInf.dll 所加载的 NVAPI 版本、SetGPUOC/SetMemoryOC delta 单位与实际驱动行为；当前 Dragon Center 前端是否通过未随样本保留的插件、服务或不同版本 helper 启动 gpuControl.exe；以及性能模式是否通过别的 MSI 服务修改 PL1/PL2、系统电源策略或 GPU power management。

建议的只读验证顺序是：记录 BIOS/EC 版本、WMI Win32_ComputerSystemProduct/Win32_Processor/Win32_VideoController 与服务清单；读取而不修改 Dragon Center Scenario 下的 GPU/CPU preset；对 XTU3SERVICE 和 ExtremeTuningUtility 只做服务状态、文件版本和 named-pipe ACL 记录；静态检查 MSI 服务/插件是否出现 gpuControl.exe、GInf.dll 或导出名。不要向 EC、GInf、XTU 或未知驱动接口发送写入请求。
