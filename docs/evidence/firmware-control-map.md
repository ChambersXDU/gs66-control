# GS66 固件控制映射：SCM0 的 WQAF–WQAI 与 WSAF–WSAI

本文记录目标机 MSI GS66 Stealth 10SE 的离线 DSDT 静态结果，目标固件绑定为 BIOS `E16V1IMS.10C`、MSI_Software EC 标签 `16V1EMS1.109`。原始 AML 表 `captures/gs66-dsdt.dat` 大小 265453 字节，SHA-256 为 `B06CDCFD6451D535E464EDA5257003741C6B93FFBD484A0EEED569AFDB7F00C1`；IASL 反编译文本是 `captures/gs66-dsdt.dsl`。补充的实机证据只执行了 `ManagementClass.Get()` schema 查询，确认类 GUID 和字段元数据，没有枚举实例值、调用 `Get_*`/`Set_*` 或 SCM0 方法。没有执行 AML、写入 WMI/EC、加载原厂服务或进行压力测试；字段名称和方法存在也不构成写入授权。

## 已确认的接口边界

DSDT 在 `\_SB.PCI0.LPCB.EC.SCM0` 声明了 `PNP0C14` WMI 设备，`_HID`、UID、`_WDG` 和 SCM0 的 EmbeddedControl 区域位于 `captures/gs66-dsdt.dsl:57617-57649`。这个设备有九组普通 WMI 对象 `AA`–`AI`，每组实例数量分别是 34、4、3、16、14、19、18、21、8；数量与 `WQAA`–`WQAI` 的分支数量对应。SCM0 的字段区从 EC 偏移 `0x2B` 开始，完整声明在 `captures/gs66-dsdt.dsl:57650-57799`。下文的偏移均是 SCM0 这个 `EmbeddedControl` 区域的偏移，不应与父级 `\_SB.PCI0.LPCB.EC` 中同名或相似字段混用。

`WQAF`、`WQAG`、`WQAH`、`WQAI` 是一参数方法，逐分支把一个 EC 字段复制到 `Local0` 并返回；对应的 `WSAF`、`WSAG`、`WSAH`、`WSAI` 是两参数方法，把 `Arg1` 写入同一个字段后返回字段值。四组读方法和四组写方法的定义位置分别是 `captures/gs66-dsdt.dsl:58243-58650` 和 `:59093-59499`。在整份 DSDT 中搜索这些名字，除方法定义外没有发现内部 AML 调用，因此这里证明的是 WMI 设备导出的字段绑定，不是某个 Windows 程序已经使用了这些字段。

## SCM0 WDG 与实机 WMI schema 的对象身份

这次实机 `ManagementClass.Get()` schema 结果把 DSDT `_WDG` 中的对象记录与现有 `root\\WMI` 类闭合起来；它只证明类定义、GUID 和属性 schema 可见，不证明任何实例值或 setter 已执行。`_WDG` 原始字节及 `AA`–`AI` 记录位于 `captures/gs66-dsdt.dsl:57622-57648`，类与对象的对应关系如下。

| Windows WMI 类/属性 | schema GUID | SCM0 对象 | DSDT 方法组 |
| --- | --- | --- | --- |
| `MSI_Software.Software` | `{24418D6A-0A79-524C-9AB1-18B78CA68CE7}` | `AA` | `WQAA/WSAA` |
| `MSI_CPU.CPU` | `{BD2A216F-2FB9-A640-B807-DDDBAD656891}` | `AF` | `WQAF/WSAF` |
| `MSI_VGA.VGA` | `{1EC3EC7A-1E9B-E74A-9026-CF122B0BBD21}` | `AG` | `WQAG/WSAG` |
| `MSI_System.System` | `{A1753D7B-B621-DE4A-B41A-55716A0ECE7A}` | `AH` | `WQAH/WSAH` |
| `MSI_AP.AP` | `{A1753D7C-B621-DE4A-B41A-55716A0ECE7A}` | `AI` | `WQAI/WSAI` |

这个身份闭合改变了字段语义的证据等级：原厂程序对 `MSI_CPU`、`MSI_VGA`、`MSI_System` 和 `MSI_AP` 的 ordinal 语义现在可以落到对应的 DSDT 字段上。它仍然不能把本次 schema 查询描述成一次 WQ/WS 读写，也不能替代写入前的设备、固件、schema、温度和回读门禁。

## WQAF/WSAF：19 个一字节字段

`WQAF` 的读映射和 `WSAF` 的写映射完全相同。方法分支分别在 `captures/gs66-dsdt.dsl:58243-58358` 和 `:59093-59208`；字段声明在 `:57693-57710`。每项都是 8 位字段，索引顺序是固件明确写出的顺序，而不是按偏移排序。

| 索引 | SCM0 字段 | EC 偏移 | 宽度 |
| --- | --- | ---: | ---: |
| `0x00` | `TD33` | `0x33` | 8 bit |
| `0x01` | `TD68` | `0x68` | 8 bit |
| `0x02` | `TD71` | `0x71` | 8 bit |
| `0x03` | `TD70` | `0x70` | 8 bit |
| `0x04` | `TD69` | `0x69` | 8 bit |
| `0x05`–`0x0A` | `TD6A`–`TD6F` | `0x6A`–`0x6F` | 8 bit each |
| `0x0B`–`0x12` | `TD72`–`TD79` | `0x72`–`0x79` | 8 bit each |

这里的 `0x02`、`0x03`、`0x04` 是 `TD71`、`TD70`、`TD69` 的非单调排列；任何实现都必须按方法索引使用这个排列，不能把索引直接当作 EC 地址。

## WQAG/WSAG：18 个一字节字段

`WQAG` 和 `WSAG` 的完整方法定义分别位于 `captures/gs66-dsdt.dsl:58360-58469` 和 `:59210-59319`，字段声明在 `:57712-57728`。这些分支同样是 8 位字段，映射如下。

| 索引 | SCM0 字段 | EC 偏移 | 宽度 |
| --- | --- | ---: | ---: |
| `0x00` | `TD33` | `0x33` | 8 bit |
| `0x01` | `TD80` | `0x80` | 8 bit |
| `0x02` | `TD89` | `0x89` | 8 bit |
| `0x03` | `TD88` | `0x88` | 8 bit |
| `0x04`–`0x0A` | `TD81`–`TD87` | `0x81`–`0x87` | 8 bit each |
| `0x0B`–`0x11` | `TD8A`–`TD90` | `0x8A`–`0x90` | 8 bit each |

`TD89` 和 `TD88` 的索引顺序也不是地址顺序。DSDT 只提供了无单位的字段读写，不提供这些字段代表风扇、温度、功率还是状态的注释。

## WQAH/WSAH：21 个字段，含 E4 位域

`WQAH` 和 `WSAH` 的完整方法定义分别位于 `captures/gs66-dsdt.dsl:58471-58598` 和 `:59321-59448`，字段声明在 `:57770-57799`。前两个字段共享 EC 字节 `0xE4`，但各自只占部分位；其余字段为完整字节。

| 索引 | SCM0 字段 | EC 偏移 | 位范围/宽度 |
| --- | --- | ---: | --- |
| `0x00` | `TE40` | `0xE4` | bit 0–2，3 bit |
| `0x01` | `TE43` | `0xE4` | bit 3–7，5 bit |
| `0x02` | `TDE7` | `0xE7` | 8 bit |
| `0x03` | `TDF0` | `0xF0` | 8 bit |
| `0x04` | `TDF1` | `0xF1` | 8 bit |
| `0x05` | `TDFE` | `0xFE` | 8 bit |
| `0x06` | `TDFF` | `0xFF` | 8 bit |
| `0x07`–`0x12` | `TDF2`–`TDFD` | `0xF2`–`0xFD` | 8 bit each |
| `0x13` | `TDE0` | `0xE0` | 8 bit |
| `0x14` | `TDE3` | `0xE3` | 8 bit |

索引 `0x05`、`0x06` 跳到 `0xFE`、`0xFF`，随后索引 `0x07` 才回到 `0xF2`；实现不能根据字段名或索引进行连续地址推断。`TDFE` 的字符串名也不能被误读成 EC 偏移 `0xEF`。

## WQAI/WSAI：8 个字段，以及一个有副作用的“读”分支

`WQAI` 和 `WSAI` 的方法定义分别位于 `captures/gs66-dsdt.dsl:58600-58650` 和 `:59450-59499`，字段声明位于 `:57761-57768`。除 `WQAI` 的索引 `0x01` 外，分支是直接读取；索引 `0x01` 在返回前执行 `TDCE = TDVR`，而 `TDVR` 是 SCM0 内的常量 `0x14`。因此调用 `WQAI(0x01)` 会先把 `0x14` 写入 EC 偏移 `0xCE`，它不是只读探针。`WSAI(0x01)` 也明确写入同一字段。

| 索引 | SCM0 字段 | EC 偏移 | 宽度 |
| --- | --- | ---: | --- |
| `0x00` | `TDCF` | `0xCF` | 8 bit |
| `0x01` | `TDCE` | `0xCE` | 8 bit；`WQAI` 会先写 `0x14` |
| `0x02` | `TDCD` | `0xCD` | 8 bit |
| `0x03` | `TDCC` | `0xCC` | 8 bit |
| `0x04` | `TDCB` | `0xCB` | 8 bit |
| `0x05` | `TDCA` | `0xCA` | 8 bit |
| `0x06` | `TDC9` | `0xC9` | 8 bit |
| `0x07` | `TDC8` | `0xC8` | 8 bit |

## 与原厂程序集的交叉核对

原厂 `MSIWMIACPI2.cs` 将 WMI 方法 10 定义为 `Get_Temperature`、14 定义为 `Get_Fan`、24 定义为 `Get_Data`，方法名和参数表在 `/tmp/gs66-research/decompiled/MSIWMIACPI2.cs:41-74`、`:190-210`。`API_NB_Base_Module.cs:8961-9184` 的 `Check_Fan` 在旧分支读取 `MSI_CPU`、`MSI_VGA` 的实例 4–9 温度和 11–16 风扇百分比；结合上表的 GUID/对象确认，这些 ordinal 现在可以落到 SCM0 的固定字段：CPU 的温度 4–9 是 `WQAF/WSAF(0x04..0x09)`、`TD69..TD6E`（EC `0x69..0x6E`），CPU 的速度 11–16 是 `WQAF/WSAF(0x0B..0x10)`、`TD72..TD77`（EC `0x72..0x77`）；VGA 的温度 4–9 是 `WQAG/WSAG(0x04..0x09)`、`TD81..TD86`（EC `0x81..0x86`），VGA 的速度 11–16 是 `WQAG/WSAG(0x0B..0x10)`、`TD8A..TD8F`（EC `0x8A..0x8F`）。因此 WSAF/WSAG 在固件结构上确实支持写入温度断点和速度点；本项目仍未执行 runtime write，也没有为曲线写入建立安全模型，所以不能启用该能力。

同一份原厂程序集对性能模式有独立而更具体的证据：`GetShiftModeValueInEC` 在 `/tmp/gs66-research/decompiled/API_NB_Base_Module.cs:1186-1247` 读取 `MSI_System:System:7`，WMI2 路径使用 `Get_Data(0)` 的返回数据；`SetShiftModeValueInEC` 在 `:1302-1347` 使用 WMI2 `Set_Data(0xD2)` 或旧服务路径。结合 `MSI_System` 的 `AH` GUID，Legacy 的 `System[7]` 正是 `WQAH/WSAH(0x07)` → `TDF2` → EC `0xF2` 的性能模式字节；原厂自动风扇模式的 `System[9]` 是 `WQAH/WSAH(0x09)` → `TDF4` → EC `0xF4`。WMI2 的 `Set_Data(0xD2)` 是另一层包装协议参数，不能把参数文本 `0xD2` 误当成 SCM0 的 `TDF2`/EC `0xF2` 地址。原厂模式字节的保留位和枚举转换在 `:1263-1299`，这条 System[7]/F2 与 System[9]/F4 绑定现已由 schema、DSDT 和原厂调用路径共同支持。

原厂 System Monitor 将风扇数默认设为 2，并允许通过配置把它扩展到 3 或 4；对应代码在 `/tmp/gs66-research/decompiled/API_NB_System_Monitor-installed.cs:5082-5090`。它的 MSI_AP 逻辑读取 ordinal 2/3 组成第一组计数、4/5 组成第二组计数；结合 `MSI_AP` 的 `AI` GUID，这对应 `WQAI/WSAI(2..5)` → `TDCD..TDCA` → EC `0xCD..0xCA`。因此现有 AP schema 支持两个逻辑风扇计数对，第三、第四风扇仍走额外 EC/服务读取路径，代码位置在 `:6018-6108` 并保留 `GpuFanSpeed3`/`GpuFanSpeed4` 数据成员。`WQAI(0x01)` 对应真实的 `MSI_AP.AP[1]`，但它会先把 `0x14` 写入 `TDCE`，监控读取不能把 AP[1] 当作普通只读样本；实现应按已核对的 key 直接读取 AP[2..5]，避免无界 provider 枚举。这个证据仍没有证明当前 GS66 有第三个物理风扇或第三通道的安全写入接口。

第三转速现在有一个可以继续验证的只读 WMI 候选，但还没有实机数值证据。旧版 System Monitor 在 `FanCount > 2` 时读取 `MSI_App_Service_ReadMemoryEC(4227860726uL)`，该十进制地址是 `0xFC0008F6`；随后用 `num6 = value >> 24`、`num7 = (value << 8) >> 24`，把 `(num7 << 8) + num6` 代入与前两组相同的 RPM 公式，精确代码在 `/tmp/gs66-research/decompiled/API_NB_System_Monitor-installed.cs:6091-6101`。如果底层 `GetPhysLong` 按小端把物理地址 `0xF6..0xF9` 的四个字节组成 `0xF9F8F7F6`，这段提取就得到高字节 `0xF8`、低字节 `0xF9`，即 `0xF8F9`；这与 DSDT 中 `WQAH(0x0D) → TDF8 → EC 0xF8`、`WQAH(0x0E) → TDF9 → EC 0xF9` 的静态映射一致，分支位于 `captures/gs66-dsdt.dsl:58551-58561`，字段声明位于 `:57792-57793`。这里的字节序仍是基于读取表达式和地址连续性的推断：`MSI_App_Service_ReadMemoryEC` 实际经过 `MSI_SERVICE_2` 命名管道，`IO:RMREQ` 返回十进制 `uint`，而可用的托管反编译中没有 `GetPhysLong` 的 native 实现，证据位置是 `/tmp/gs66-research/decompiled/API_NB_System_Monitor-installed.cs:7000-7066`、`/tmp/gs66-research/decompiled/NamedPipeClientLib.cs:1321-1372` 和 `/tmp/gs66-research/decompiled/MSIAPService-installed.cs:788-807`。因此不能把 raw service 地址直接当作本项目的 WMI 读取后端。

只读验证应优先使用已经闭合身份的 `MSI_System.System`/`AH` 对象，读取 `System` 的 ordinal `0x0D` 和 `0x0E`，而不是访问 `MSI_AP.AP[1]` 或调用有副作用的 `WQAI(0x01)`。原厂程序集有 `MSI_System` 的普通 `ManagementObject` 枚举和属性访问代码（例如 `/tmp/gs66-research/decompiled/API_NB_Base_Module.cs:10714-10725`），但该示例随后调用了 `Put()`，所以它只证明对象形状和属性读取方式，不能被当作本项目已经存在的只读实现；截至目前也没有在目标机上读取这两个 ordinal 的实例值。未来只有在同一固件指纹下确认对象存在、读取成功、返回值不是哨兵值、按 `F8/F9` 组合得到合理且连续的 RPM，并通过重复样本或独立风扇读数交叉核对后，才能把它标记为第三转速遥测；在此之前 UI 和遥测模型都必须继续报告“不可用”，不能显示推算的 RPM。目标机的 `FanCount` 也仍未从配置实读，现有证据只有默认值 2 和 `Component\\Base Module\\GeneralSetting\\Fan` 覆盖逻辑，因此不能静态断言 GS66 一定启用了第三通道。

## 温度断点、第三风扇和功率/Boost 的结论

温度断点现在已有字段级静态闭合：`MSI_CPU`/`MSI_VGA` 的 GUID 对象分别是 `AF`/`AG`，原厂 ordinal 4–9 和 11–16 与 WQAF/AG 的温度、速度点一一对应，且 WSAF/WSAG 有相同索引的 setter。DSDT 的父级 EC 区域还声明了 `CPUT`，并由 `\_TZ.THRM._TMP` 读取后转换为 ACPI 温度，字段和使用位置是 `captures/gs66-dsdt.dsl:56876` 与 `:56630-56645`；这是一个当前热区温度来源，不是六点风扇曲线。这个新证据支持“可以研究曲线写入”的结论，但没有完成 runtime write、并发处理、原值快照、温度安全边界或失败恢复验证，所以曲线写入仍保持锁定。

第三风扇仍没有可实施的控制绑定，第三转速则有上段所述的静态只读候选。`MSI_AP` 的 schema 和原厂属性语义只闭合了 AP[2..5] 的两组计数，分别对应两个逻辑通道；System Monitor 的可选第三、第四通道依赖额外 EC/服务读取。任何把 WQAF、WQAG 或 AP[6]/AP[7] 的字节当成第三风扇并读取或写入都缺少目标机证据；在 WMI 候选完成实机验证前，当前实现应排除这些猜测。

DSDT 早期 GNVS SystemMemory 区域中出现了 `XTUB`、`XTUS`、`XMPB`、`CPUS`、`CTDP`、`FPL0`–`FPL5`、`MPL0`–`MPL2` 等看起来像性能或功率的名字，声明在 `captures/gs66-dsdt.dsl:490-800`。但 `FPL*` 在后续 AML 中被 I2C 设备的 `FPCN` 方法作为三元组返回，位置在 `:49118-49283`；`TURB` 也只是父级 EC 偏移 `0xE4` 的一位字段，声明在 `:56916-56920`，没有发现使用它来改变性能状态的 AML 调用。字段名称、SystemMemory 区域和局部 I2C 返回值都不能证明 CPU PL1/PL2、GPU 功耗、Boost 或超频控制，更不能成为写入接口。

## 可扩展范围和禁止事项

本次结果可以作为诊断元数据保存：在精确固件指纹下记录 SCM0 WQ/WS 的索引、字段宽度和偏移，并明确 `WQAI(0x01)` 的副作用。曲线和模式的 WMI schema/DSDT/原厂调用链已经能证明固定绑定，但未来若要验证写入，仍必须在同一绑定下完成原值快照、单属性 expected-value 比较、显式用户操作、独立恢复和立即回读；本次 schema 查询没有执行任何 WS。监控读取 AP 时只允许针对已确认的 AP[2..5] key 使用只读对象访问，不能用可能全量枚举 provider 的查询路径。

当前实现可以继续使用已经有设备、固件、基线哈希和读回门禁的 System[7] → F2 性能模式控制，以及已批准的 System[9] → F4 自动风扇动作和只读温度门禁。WSAF/WSAG 的曲线 setter 是已识别的潜在扩展点，但尚无 runtime write 验证和安全模型，不能加入桌面控制台；WSAH 的其它模式写入、WSAI、其他 `WSAA`–`WSAE` 写方法也不应被调用。特别是 `WQAI(0x01)` 不得作为诊断读取接口。第三转速候选只能作为后续隐藏的只读诊断项，完成实机检查前不得进入 UI 或报告 RPM；第三风扇控制、CPU/GPU 功率限制、Boost/超频和任意未闭合 WQ/WS 写入，仍缺少同一目标机上的能力门禁和独立恢复证明。

因此，这份映射的结论是：固件暴露的 SCM0 字段表已经通过 WMI schema 和原厂代码闭合出 CPU/GPU 曲线点、System[7] 性能、System[9] 风扇模式以及 AP[2..5] 两组转速计数；第三转速另有 `System[13]/[14] → TDF8/TDF9` 的静态只读候选，但其字节序、FanCount 和实例值尚未在目标机上闭合，当前不能显示。曲线 setter 具备突破原厂 UI 的潜在能力，但尚未经过 runtime write 和安全恢复验证，当前不能启用。第三风扇控制、CPU/GPU 功率限制、Boost/超频仍没有足够的固定安全接口证据，不能包装成可点击控制项。
