# Dragon Center 遥测绑定证据

本笔记保留最初静态研究的有界结论，后续实机证据与已采用来源见后半部分。最初仅整理已取得的 Dragon Center 静态反编译材料与已提交证据索引。所有结论都是代码中存在的绑定，不表示目标 GS66 已执行过该路径；本轮没有调用 WMI 方法、启动原厂服务、读取硬件或写入 EC。

## 结论

原厂 Legacy/WMI1 代码明确把 `root\\WMI` 的 `MSI_CPU` 类属性 `CPU` 和 `MSI_VGA` 类属性 `VGA` 作为六个风扇曲线温度断点读取。`API_NB_Base_Module.cs:9012-9184` 的 `API_Dynamic.UserScenario.Check_Fan` 查询两个类；枚举序号 4–9 的值写入 `Default_Temp`，序号 11–16 的值写入风扇速度配置。相邻常量 `FanTemperature1..6=4..9`（`API_NB_Base_Module.cs:8388-8398`）和 `FanCPUT1..6Percentage`/`FanVGAT1..6Percentage=11..16`（`API_NB_Base_Module.cs:8378-8406`）支持这个语义。该绑定证明了曲线的温度参考点，不证明这些属性是实时当前温度、单位是摄氏度，或枚举序号在目标机上稳定对应某个硬件通道。

因此，在“只读现有属性、禁止 WMI 方法调用”的范围内，当前 CPU/GPU 实时温度仍是未知。WMI2 代码确实有 `MSI_ACPI` 的 `Get_Temperature` 路径，并在 `MSIAPService.cs:2626-2670` 和 `API_NB_Base_Module.cs:9025-9046` 读取返回字节；但这是 `InvokeMethod`/`Get_Temperature`，本轮证据边界明确排除它。`gpuControl.cs:81` 的 `GPUInfoStruct.CurrentTemperature` 来自 `GInf.dll` P/Invoke，也不是 WMI 属性，因此不能替代上述结论。

`MSI_AP` 的 Legacy 风扇转速换算是已证实的只读属性绑定。`API_NB_Base_Module.cs:8638` 创建 `ManagementObjectSearcher("root\\WMI", "SELECT AP FROM MSI_AP")`；`API_Dynamic.UserScenario.GetFanSpeed`（token `0x060001f7`）在 `API_NB_Base_Module.cs:11029-11110` 按枚举顺序取 `AP`：序号 2 作为第一组低字节、序号 3 作为高字节，序号 4/5 作为第二组低/高字节，计算式为 `int(60,000,000 / (((high << 8) + low) * 2 * 62.5))`，最后返回两个 `"<value> RPM"` 字符串。这个公式和两个逻辑通道是原厂实现事实；它没有证明计数单位、枚举顺序在 GS66 10SE 上稳定，或第三颗物理风扇如何映射。WMI2 的 `Get_Fan` 分支同样是方法调用，不能在本轮作为属性遥测证据。

EC 固件标识没有符合本轮边界的实时 WMI 属性证据。`MSIWMIACPI2.WmiAcpiLib2` 静态声明 `Scope="root\\WMI"`、`Path="MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'"`，并有字段 `EcFwVersionAndDate`、`EcRamMainVersion`、`EcRamSubVersion`（`MSIWMIACPI2.cs:125-137,187-195`）。但这些字段由构造初始化中的 `MoClassInstance.InvokeMethod("Get_EC", ...)`（`MSIWMIACPI2.cs:483-493`）及 `Parse_EC_Info`（`MSIWMIACPI2.cs:809-836`）填充；`WMIEC` 注册表值只是缓存读取（`MSIWMIACPI2.cs:623-648`），不是当前硬件属性测量。Legacy 的 `MemRwService.Read_To_Hex_String("A0",16)`（`API_NB_Base_Module.cs:9012`）是服务/物理内存读取候选，也不是 WMI 属性，不能据此报告 EC 版本。

## Artifact 身份与复核坐标

主要曲线和 RPM 证据来自 `API_NB_Base Module.dll`，解包 artifact SHA-256 为 `cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192`，MVID 为 `e4faaa61-d4a9-492b-971c-503e66fdfe85`。REA focused evidence 将 `GetFanSpeed` 标为 `API_Dynamic.UserScenario`、token `0x060001f7`、RVA `94856`；详见 `docs/evidence/dragon-center/rea-API_NB_Base_Module.dll-focused-members.json`，逐字摘录与行号索引见 `docs/evidence/dragon-center/managed-exact.json` 和 `docs/evidence/dragon-center/managed-excerpts.md`。

WMI2 方法与字段证据来自 `MSIWMIACPI2.dll`，artifact SHA-256 为 `1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828`，MVID 为 `7298d906-9507-4f4e-abc2-bfa09726c559`。其 REA 静态结果明确说明未加载 CLR、未执行程序集；本笔记只用它确认方法/字段边界，不把方法返回值当作实机数据。

`MSIAPService.exe` 的离线文本（SHA-256 `52de5b9df1212f3cf040338b1ae92250d1021af9980424baaef9551e427ad982`）在 `MSIAPService.cs:3772-3868` 还原了原厂自检：Legacy 分支读取 `MSI_CPU.CPU` 序号 4–9 与 11–16，WMI2 分支调用 `Get_Temperature`/`Get_Fan`。它是交叉佐证，不改变“实时温度需要方法调用或额外实机证据”的结论。

## 未知与实现约束

目标机已有只读 schema 验证显示 `MSI_CPU` 19 个实例、`MSI_VGA` 18 个实例、`MSI_AP` 8 个实例，但该验证没有输出实例值，也没有确认实例顺序。因此可以实现安全的属性枚举和 schema 诊断，却不能把 ordinal 直接硬编码成 GS66 的实时传感器映射；第三风扇、温度单位、RPM 计数标定和 EC 固件字符串仍须另行取得不涉及写入的证据。


## 本机属性核对与桌面实现

后续只读 PowerShell 捕获发现 MSI_AP 的 8 个实例按枚举顺序与 InstanceName 末尾索引 0–7 一致，均 Active=true；原始键值保留在 Git 忽略目录。WindowsTelemetryProbe 不依赖返回枚举顺序，而要求全部实例属于同一 key 基础、索引唯一且完整 0–7，按索引 2/3 和 4/5 读取计数。绑定只在本机已确认的 GS66 Stealth 10SE / E16V1IMS.10C 生效；这是只读绑定检查，不是控制写入的设备白名单。数据形状或身份变化时停止显示 RPM 并返回 warning。

桌面运行核对成功，第一逻辑通道换算值约 2,000 RPM，第二逻辑通道计数为零并显示 0 RPM。该结果是原厂公式换算，尚无外部转速计标定或第三颗物理风扇对应关系；不能将这两个值描述为三颗独立风扇实测。实时 CPU/GPU 温度和 EC 固件标识仍保持未知。

## 新增本机 System Monitor 证据：实时温度

进一步检查本机安装的 System Monitor artifact 后，已确认只读 WMI 实时温度路径：MSI_CPU.CPU 与 MSI_VGA.VGA 的 ordinal 1，单位摄氏度；0/255 表示不可用。[artifact 身份与代码摘录](evidence/dragon-center/installed-system-monitor-temperature.md) 保存本机哈希、MVID、REA Evidence 和有限反编译片段。该结论扩展了先前仅 Base Module/Service 的有界检查，不把曲线温度断点当作实时温度。

WindowsTelemetryProbe 对本机型号/BIOS 验证后，要求 CPU/VGA 类分别具有完整的 19/18 个 Active 实例，实例基础一致、索引唯一且连续，枚举顺序也须与索引一致；再按索引 1 读取温度。0/255 和超过 125 的读数保留 null 与 warning。125 是数据合理性过滤的实现上界，不是允许运行或控制的温度阈值。AP 的相同检查也要求原厂 ordinal 与 key 后缀一致。

第三风扇的 System Monitor legacy 路径使用现有服务的物理内存读取，不是 MSI_AP 属性 6/7；本阶段没有执行该路径，第三风扇继续未知。Get_WMI/Get_EC 的两次批准尝试没有返回可解析版本包，EC 固件仍未知。

## 固件标签与 NVIDIA 温度补充

后续直接读取 MSI_Software 的 34 个 Active 属性，按已核对索引从 6–33 解析 ASCII，得到 EC `16V1EMS1.109` 与构建文本 `0311202011:16:49`；Get_WMI/Get_EC 未返回版本包不妨碍该独立属性来源，标准 SMBIOS 255/255 仍表示未知。解析和非 ASCII/缺失/非法值拒绝已纳入 Core 测试，不执行物理内存路径。

控制预检在 MSI_VGA 当前温度无效时允许只读备用来源：`%SystemRoot%\System32\nvidia-smi.exe --query-gpu=name,temperature.gpu --format=csv,noheader,nounits`。实机安装工具 SHA-256 为 `B7D26DAB016A9DC4C2DB725633C8EC1A7B58310B18A6610A6DEC7D67057DB248`；捕获时 Authenticode 有效，签名者为 Microsoft Windows Hardware Compatibility Publisher。代码核对固定系统路径与精确哈希，不通过 PATH 寻找或 shell 执行；驱动更新改变哈希后拒绝该来源，须重新核对。

解析只接受恰好一行、两列、精确名称 NVIDIA GeForce RTX 2060 和 1–125 的整数温度；多设备、额外字段、NA/0/255、单位字符串与其他型号均拒绝。查询限时 2 秒，超时停止自己的查询进程。该 125 上界仅是数据合理性过滤，控制准入仍为 GPU≤75°C。来源单独标为 NVIDIA 只读查询，不冒充原厂 WMI 或模拟值。实机空闲预检读取 42°C；正常周期监控不调用该工具，原厂 WMI 缺失时仍如实显示不可用。

## 保存曲线的只读导入

WindowsSavedCurveProbe 在完整设备/固件/artifact/schema 核对后读取 MSI_CPU 的 19 个实例与 MSI_VGA 的 18 个实例，按原厂 Check_Fan 绑定取 4–9 为温度、11–16 为速度值。只选择固定属性，不调用 Check_Fan 本体、其物理内存读取、注册表缓存写入或 WMI 方法。两次曲线读取必须一致，前后模式须一致、身份采集仍新鲜；这减少混合快照风险，不构成跨属性原子读取保证。

实际 GS66 两条保存曲线首个温度点为 0。编辑器只在首点允许该原始值，保留其他温度点 20–100、严格递增与速度 0–100、不下降的草稿校验；不把 0 当作当前温度，不改成 20，不猜其底层哨兵含义。超范围、实例数量错误或非单调数据拒绝导入，没有静默截断或归一化。速度值尚未完成硬件百分比或 RPM 标定；读取保存曲线不证明当前模式正在采用它。

实机只读 smoke 检查非模拟来源、CPU/GPU 原始点与编辑器一致、草稿保存及图形渲染。新增 8 项跨平台测试覆盖原始首点 0、未知/越界值、非递增/非单调和实例数量变化。

## 后续固件审查与普通权限采集

以上全量 AP 枚举描述属于早期历史。实际 DSDT 静态审查发现 MSI_AP 第 1 项的固件查询会写协议字节，因此当前代码禁止枚举 MSI_AP，只按固定实例路径直接读取已经审查的 2–5 项。CPU/VGA/System 属于分别审查过的固定类，不把其完整实例查询扩展到未知类。

2026-10-15 Windows 普通权限窗口出现过 WMI AccessDenied；AP 拒绝结果缓存三十秒后重新尝试。用户可通过盾牌按钮启动只读采集 helper，主窗口不提升权限。采集器核对准确设备／固件，按两秒间隔读取固定风扇计数和温度，结果关联请求标识、父进程身份、时间戳及来源；窗口只接受八秒内的新鲜帧，不保留过期值。控制快照另按 nonce 关联，只在操作预检或回读时请求，不给周期监控增加 nvidia-smi 调用。独立实机联调取得非模拟温度与两路 RPM，停止／父进程心跳失效后进程退出，不安装后台服务。详细验收见 [Windows 验证记录](windows-validation.md)。
