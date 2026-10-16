# Legacy EC 版本读取的证据边界

本文只记录保留的 Dragon Center 反编译文本和已经保存的证据索引。没有启动
`MSIAPService`，没有连接 `MSI_SERVICE_2`，没有调用 WMI 方法、native delegate
或任何硬件访问。下文的“读取契约”是静态代码路径，不是 GS66 当前硬件已经返回的
版本值；任何 Windows 实机验证都属于当前范围之外，必须单独得到新的明确批准。
本次新增的服务证据来自实际安装的 `MSIAPService-installed.exe`：FileVersion
`1.1.2111.1602`，MVID `fbb89088-047c-4a11-915b-8a73bc0eee39`，SHA-256
`8ea193c84dd69bd0d0e9f808ae560ca658c72254e2556eaf4393e16b27bd0de5`。反编译文件和
REA 证据索引均保留在 `/tmp/gs66-research/decompiled/MSIAPService-installed.cs` 与
`docs/evidence/dragon-center/rea-installed-msiapservice.json`；这仍然是静态证据。

## 结论

旧 Base Module 的 EC 版本分支不是一个已证明的 WMI 属性读取。WMI 版本小于 2
时，`Check_Fan` 调用 `MemRwService.Read_To_Hex_String("A0", 16)`；该函数把
`A0` 拼成 `FC0008A0`，再通过旧的 named-pipe client 读取四个连续的 32-bit
地址。服务端最终把地址交给 `WinIO.GetPhysLong`，而 `WinIO` 又从
`KernCoreLib64.dll` 取得 native 物理内存入口。这个链条只证明了原厂实现如何尝试
取得一个 16 字节原始块，不能证明 `0xFC0008A0` 是 GS66 10SE 的合法 EC 映射，
也不能证明返回字节是可打印的固件版本。

WMI 版本大于等于 2 时，旧代码使用 `WmiAcpiLib2.EcFwVersionAndDate`。这个名称
看起来像属性，但它是托管对象的缓存字段：构造过程从 `MSI_ACPI` 的 `Get_EC`
方法解析 28 字节 UTF-8 字符串，或从
`HKCU\Software\Wow6432Node\MSI\MSI HELP DESK\Live Update\EC_Version` 等缓存路径取得信息。
它不是 `MSI_ACPI` 类已证明存在的普通 WMI 属性。根据父任务提供的、已经获批的
一次有界 C# 重试结果，当前 GS66 的规范 `MSI_ACPI` 实例上的 `Get_WMI` 和
`Get_EC` 均返回 `ManagementException: Not supported`，`WMIEC` 缓存为空，普通
明文扫描也没有发现 EC 版本标签；因此这个字段目前必须保持 unknown。该段是运行时
上下文，不是本次静态反编译所得的硬件测量。

## 静态读取契约

`API_NB_Base_Module.cs` 中的 `MemRwService` 定义 `BaseAddress = "FC0008"`。
`Read_To_Hex_String("A0", 16)` 通过 `result + 4 * index` 访问
`0xFC0008A0`、`0xFC0008A4`、`0xFC0008A8`、`0xFC0008AC`，每次请求一个
无符号 32-bit 值。客户端最多重试五次，每次间隔两秒；失败或返回零会被包装层当作
空值，失败和有效的零因此不可区分。四个值直接拼接成大写八位十六进制字符串，
没有版本格式校验，也没有在该路径中移除 NUL 或确认字节序。`Read_To_ASCII_String`
虽然存在，但 `Check_Fan` 的 legacy 分支调用的是十六进制函数。

保留的 Foundation client 将这个服务请求编码为
`IO:RMREQ:<decimal physical address>;1`。六字节头部是
`00 FF || BE32(header + payload 的总字节数)`；握手是
`HS:REQ:<当前进程 ProductName>;<FileVersion>`。除握手外，client 使用
`MD5(ASCII("msiservice" + ProductName + yyyyMMdd))` 作为 TripleDES-ECB
密钥并加密整包，服务端还会尝试前一天的日期。响应的静态形状是
`IO:RMRST:1;<decimal uint>` 或 `IO:RMRST:0;ERR`；client 取最后一个分号字段，
用 `Convert.ToUInt32` 解析，管道/解密/`ERR` 则返回失败字节。请求中的 `;1` 在
服务端 legacy `ReadMemory` 中只是被取出而未用于选择读取宽度，真正调用仍是
`GetPhysLong` 的 32-bit 读取。

实际安装服务的解析器在 `processInputCommand` 中从输入字节 2--5 读取大端
32-bit 总长度，分配 `length - 6` 字节的 payload，再按命令组选择 ASCII 或
UTF-16LE；命令组和命令类型分别取两个冒号分隔字段，数据再按分号切分。
命名管道服务端和单次 client 缓冲区都是 4096 字节，并处理 Win32
`ERROR_MORE_DATA`（234），但解析器没有为总长度、地址或第二个 width 字段建立
机型范围检查。已安装版本的 RM 分支固定返回
`IO:RMRST:1;<decimal uint>` 或 `IO:RMRST:0;ERR`；它不会循环读取，也不会在
RM 分支调用端口写或物理写。

这条路径的返回值仍有几个不能忽略的歧义。服务端以十进制文本返回 `uint`，client
丢失了服务端的成功状态并只相信最后一个字段；`MemRwService` 再把零视为空值。
物理内存读到的四个 word 可能需要平台特定的字节序解释，代码没有证明它们构成
日期、版本或 ASCII。`Read_To_Hex_String` 的结果也不能当作已验证的 EC firmware
identifier。

## 初始化和副作用

实际安装服务的 `processInputCommandIO` 在进入任何 IO 分支前无条件调用
`WinIO.loadWinIO()`，然后才处理 `RMREQ`。`loadWinIO` 在 `pDllWinIO != IntPtr.Zero`
时直接返回；否则动态加载同目录的 `KernCoreLib64.dll`，取得
`InitializeKernCoreLib`、`GetPhysLong`、`SetPhysLong`、端口读写和 shutdown
delegate，并调用初始化入口。异常时 `WinIO.ReadMemory` 还会
`freeWinIO()`、再次 `loadWinIO()`，最多递归重试三次。因此，“服务已经运行”不等于
WinIO 已经加载：已安装版本的 `OnStart` 启动管道和 WMI 初始化，但静态调用图中没有
`loadWinIO()`；第一次 `RMREQ` 可能才触发 DLL/驱动初始化。`ReadMemory` 分支本身
只调用 `GetPhysLong`，没有调用 `SetPhysLong` 或 `SetPortVal`，但这不能消除其前置
`loadWinIO()` 的初始化副作用。地址仍是未经 GS66 白名单或范围检查的物理地址。

更不能用“启动一次服务来测试”替代验证。`MSIAPService.OnStart` 会调用
`WMI.writeWMI("root\\WMI", "MSI_Software", "Software", 0, "1")`；WMI2 分支
还会读取后改写 `Get_Data/Set_Data` 的 217（十六进制 `0xD9`）并写 restart
注册表。`CheckMSIAPCFG` 会检查 `msiapcfg.dll`，必要时涉及
`WmiAcpi\MofImagePath` 和系统目录；这些都是安装、注册表或重启相关副作用。该
服务启动路径当前不属于只读诊断。

`WinIO.readECRam` 也不能被称为只读 EC 读取：它在每个 `0xA0` 到 `0xA3` 索引
前先调用 `SetPortVal` 写 EC index/data 端口，候选端口是
`0x34D/0x34E/0x34F`（代码也会探测 `0x4D/0x4E/0x4F`）。因此这个 back-door
探测和 `MemRwService.Write`、`IO:WMREQ`、`SetPhysLong` 都必须从未来的只读
验证中排除。

服务自身还有一个更弱的版本线索：`WriteHardwareInfoToLog` 读取
`HKCU\Software\Wow6432Node\MSI\MSI HELP DESK\Live Update\EC_Version`，但随后
把 BIOS 的变量 `text3` 写进 `"EC_Version : "` 日志，而不是读取到的 `text4`。
即使修正这个静态 bug，它仍然是软件缓存，不足以证明当前 EC 固件身份。

## 是否能改成现有 WMI 属性读取

目前没有被证实的“EC 版本普通 WMI 属性”。`MSIWMIACPI2` 的
`MethodNames` 明确列出 `Get_WMI`、`Get_EC` 和其它 `Get_*`/`Set_*` 方法；
`EcFwVersionAndDate` 是对 `Get_EC` 返回包的解析结果，返回字符串的源头仍是
方法调用或注册表缓存。`MSI_CPU`、`MSI_VGA`、`MSI_AP` 的静态属性/ordinal 是
温度、风扇或平台数据协议，不能替代 EC firmware identifier；BIOS 注册表的
`BIOSVersion` 也只是 BIOS 版本。实机上 `Get_EC/Get_WMI` 不支持且 `WMIEC` 为空，
所以当前控制台应显示 unknown 和诊断 warning，而不是把缓存或零值当版本。

## 未来验证提案（当前范围之外，需重新批准）

未来若要关闭这个未知项，第一步仍应是离线核对安装文件的完整 SHA-256、BIOS/EC
允许列表和目标实例身份；当前已知 BIOS 为 `E16V1IMS.10C`，EC 仍未知。任何
Windows 方案必须由用户针对这一次验证单独批准，使用独立的超时和可中止边界，
不启动、修复或安装 MSI 服务，不写 WMI/注册表/EC/物理内存，不调用
`SetPortVal`、`SetPhysLong`、`IO:WMREQ` 或 `WmiAcpiLib2` 的 setter，也不执行
WMI repository salvage。

在这些前提下，首选仍是继续收集只读的类/属性 schema 和软件缓存状态，并把
`Get_EC` 的“不支持”作为明确结果保留。对于“复用已经运行且 WinIO 已加载的服务”
这一想法，静态证据只允许把它列为有条件的未来审查项，不能直接提出可执行方案：
`pDllWinIO` 是服务进程内部状态，现有管道没有只读的“已加载”确认接口，服务启动本身
也不能证明它已加载；若该状态无法在不改状态的前提下独立证明，必须拒绝这个路径。
只有在新的安全审查确认目标服务就是上述已安装 SHA、确认 `pDllWinIO` 已非零、禁止
其它命令且确认不会触发异常重试时，才可以进一步评估四个预先固定地址
（`0xFC0008A0` 至 `0xFC0008AC`）各至多一次的 `RMREQ` 读取；首个失败即停止，
这仍需新的明确批准，并不是当前授权。不能复用会自动启动/初始化
`MSIAPService`、加载 `KernCoreLib64.dll`、执行 `readECRam` 或提供写入口的客户
端。只有在返回成功、地址映射有机型依据、字节序和已知版本格式都通过交叉核对
时才可填入版本；零、`ERR`、非打印或格式不明的结果都必须保持 unknown，原始
EC dump、序列号和可识别 WMI 输出不得提交到仓库。

这只是一个未来验证的验收框架，不是当前授权，也没有在本次工作中实现或执行任何
管道、服务或硬件调用。

## 来源与身份

| 证据 | 位置与成员/行号 | artifact 身份 |
| --- | --- | --- |
| legacy 地址拼接、四个 word、零值处理和写入口 | `/tmp/gs66-research/decompiled/API_NB_Base_Module.cs:224-380`，`MemRwService`; `Check_Fan` 在 `:9012-9016` | `API_NB Base Module.dll`, SHA-256 `cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192` |
| 管道头、握手、加密、`ReadMemory`/`WriteMemory` 返回解析 | `/tmp/gs66-research/decompiled/NamedPipeClientLib-foundation.cs:1334-1584` | Foundation `NamedPipeClientLib.dll`, SHA-256 `3bef0ce4026ef03bbfb491ceb550ce85a1ddf0451053c048a2c83a589ad8c38d`; 旧 Battery 副本 SHA `29ce3e3a0ac831aeadd118316ef5740963bdf60e8516b72c9cd4ae703cbf94c5` 不应混用 |
| 实际安装服务的 framing、`RMREQ`、`ReadMemory`、启动副作用 | `/tmp/gs66-research/decompiled/MSIAPService-installed.cs:352-481`，`:520-705`，`:788-840`，`:2888-2960` | `MSIAPService-installed.exe`, FileVersion `1.1.2111.1602`, MVID `fbb89088-047c-4a11-915b-8a73bc0eee39`, SHA-256 `8ea193c84dd69bd0d0e9f808ae560ca658c72254e2556eaf4393e16b27bd0de5`; REA `ev_90f5c77889c1cde59aeb69ccc6ecdb45990037a04f00cc7c429a8ffb03b066dc` |
| 实际安装服务的 `WinIO` 物理读、加载入口和 EC 端口探测 | `/tmp/gs66-research/decompiled/MSIAPService-installed.cs:4170-4517`，`:4441-4466` | 同一 installed artifact；被加载的 `KernCoreLib64.dll` 仍以独立 SHA-256 核验，不因服务哈希变化而复用旧服务结论 |
| WMI2 `Get_EC` 解析、`EcFwVersionAndDate` 与 `WMIEC` | `/tmp/gs66-research/decompiled/MSIWMIACPI2.cs:133-193`，`:600-704`，`:818-835` | `MSIWMIACPI2.dll`, SHA-256 `1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828` |
| 已有静态证据索引和“没有执行”边界 | `docs/evidence/dragon-center/managed-excerpts.md:1-22,270-375`; `docs/windows-validation.md:17-35` | 仓库保留的证据副本；行号对应完整反编译文本 |

现有服务协议和地址只足以指导后续审查，不能当作 GS66 10SE 已验证的 EC 读取
接口；本文件也不授予任何服务、驱动、WMI 方法或硬件访问权限。

## 后续更新：已从普通 WMI 属性取得报告标签

父任务后续只读查询 MSI_Software，取得其索引 6–33 的 ASCII 标签 `16V1EMS1.1090311202011:16:49`，并通过 CLI/桌面实机验证。当前可报告 MSI WMI 提供的 EC 标签 `16V1EMS1.109`；早先本页“EC 保持 unknown”的结论针对当时未成功的方法/缓存和有界源代码检查。项目仍没有执行本页描述的物理内存读取，也不需要为取得这个标签走该危险路径。服务初始化/重载风险结论保持有效。
