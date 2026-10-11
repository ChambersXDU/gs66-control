# MSIService `simple` 管道与 MSIWmiAcpi 解析边界

本记录针对原厂包中的 `nbfoundation/syswow64/MSIService.exe` 和
`nbfoundation/syswow64/MSIWmiAcpi.dll`，只做离线静态分析。二进制没有在
Windows、Wine 或 macOS/Linux 上执行，未连接 GS66，也没有向 EC、WMI 或任何
设备接口发出写入请求。因此下面的“已证实”指反编译、导入表和交叉引用共同
证明的静态事实；它不等价于 GS66 10SE 实机上的运行成功。

## 样本与分析证据

| 文件 | SHA-256 | 格式/地址信息 |
|---|---|---|
| `MSIService.exe` | `38b5536abcf7eaa9f249b6c325e29bdcc450e7c51239785466d6afc236fe9a90` | PE32 x86；ImageBase `0x00400000`；入口 RVA `0xD29A` |
| `MSIWmiAcpi.dll` | `3a2b4ebe2b3f6e6baaa7bd472237da778c5869c9feef82b450aecbe6ebccb3bd` | PE32 x86；ImageBase `0x10000000` |

REA 使用 Ghidra `12.1.4_PUBLIC` 生成了本记录引用的函数档案。`MSIService.exe`
中 `\\.\pipe\simple` 字符串的搜索证据为
`ev_ce04d4975036c03fb601ece5d466d63b39a4a6668a07469feb212a2f61c81637`，其引用
证据为 `ev_a9890b6c042735f77193f1defb711f834a2f7a6b0ce60e572dff00c3cd602c8d`。
包含引用的函数档案为 `ev_5d33dea82f17941a7965197c003c7c52f5c3ea942f3be7809d88fbd463e775fa`，
外部调用者/被调用者关系为 `ev_bae74ff2820d14116ffddc2034c4e9fea6300860015c0c009f67cf3a1cdb60f8`。

`MSIWmiAcpi.dll` 中 `root\\WMI` 的字符串证据是
`ev_956f7270c851b10a599eb237037b171ea7d80d39b49c7d590ce430b9b1812134`，WMI
相关导出和字符串索引为 `ev_67c816184e53857a72c6608188406594153c8e71d451e1736f53685d8476f44d`。
解析器函数档案为 `ev_4b94162d85779066757b890fbf6dc0b3fb16b2dc84e8291e829a748da0865bd6`；
`MSI_CallNamePipe` 为 `ev_dc333f2e4cdb4db4536ee252c97067bfb95bbcc6c12af2a35f6c4dd512dfe7e3`，
其 C# 适配入口为 `ev_34938d2b826ea1a5ab883ac16a28e04e780fc07d96a204f97c3fcf3f7e519be1`。
`IsMSIMethodWMI` 和 `MethodWMI` 的函数证据分别为
`ev_d8a72db37d3c84125b6bf6cf4cb26951ca87fa8a597b446ff938a00561450a7e` 与
`ev_aff1d891be291f44a21c2a53227b1769823b87b78fa1ea0143c6cb989ee25ae7`。
解析器中包含通用 `%s:%x:%x:%x:%x` 格式字符串的搜索证据为
`ev_01e870d2f69c296db4284f3ff49543d2c94572963324ded5eee82c25e3c44914`。

为使外部审阅者可以校验 REA 返回物，下面同时记录本地保存的
`CallToolResult` JSON 源文件 SHA-256；这些哈希针对 `/tmp/gs66-research` 中的
原始结果文件，而不是目标程序本身：

| REA 源结果 | SHA-256 |
|---|---|
| `rea-msiservice-fn401cd8.json` | `49b3317f29a00e571033836a711c6b370afdbe993a48b59ffd93396f464550f9` |
| `rea-msiservice-callees401cd8.json` | `8b6a24d36a6c9f0e41f7e72e333f43750a83bccae21ac742e385e3af84c06570` |
| `rea-msiservice-simple3.json` | `0fe080186229b369ce4a26eda844c71c93a906d169a629193fddffe59dcd26d7` |
| `rea-msiservice-simple-xrefs.json` | `aff0174c97bdb0207d52f13a4f1dafdd459695e33573e4bdfb444de6524f0312` |
| `rea-msiservice-refs401cd8.json` | `3be7e475f179bbb81a67633817ed0a9572273d80d3477c1b3a504f69cc756435` |
| `rea-msiwmiacpi-parser.json` | `dea3a22f4028deacfec6bbecaa30d72e086277211072cb55582610f0b4d75ec7` |
| `rea-msiwmiacpi-pipe.json` | `ac3a57c212ee4ced1526b357558549318720ad1367b9a017976e7c8b5039d6ab` |
| `rea-msiwmiacpi-pipe-csharp.json` | `ea2f3587206fea98593ac781543c8cbd2fade43eafbfa36abd721df18c777012` |
| `rea-msiwmiacpi-is-method.json` | `d31d8e539c44bdcfe55b379139b51eb3ca22e23cd8c62766b32a29f60489fbce` |
| `rea-msiwmiacpi-method.json` | `c18f5a4abde18659bfc1274f5c90c1a77a6e7e430b2b87b414bedfe96c9f8ba8` |
| `rea-msiwmiacpi-root.json` | `fa22f35c457b4479b54fc420787e0e11678d7b9c278242ae3f86101e1bb65c97` |
| `rea-msiwmiacpi-wmi.json` | `279b7a0e7072c32352aaf76e1a7dc4ab931d3eb4f10ca121e3a6283721ab9310` |
| `rea-msiwmiacpi-s.json` | `bbc475152702069448c91f677ba298c890f5942647e92c1749a983afa2683d8a` |

源结果文件位于当前研究工作区的临时目录，仓库只保留本聚焦摘录；如果临时
目录被清理，应以本节中的函数地址、样本 SHA 和证据 ID 重新生成并比对。
聚焦函数的未改写伪代码、汇编和调用边保存于 [simple-pipe.json](simple-pipe.json)，
同时保留源结果 SHA 和 evidence ID；该 JSON 不是完整 MCP envelope。

## MSIService 服务端：管道创建到 WMI 解析

### 已确认的服务端调用链

`MSIService.exe!FUN_00401ca0`（函数体 `0x00401ca0–0x00402129`）在
`0x00401cd8` 将默认管道名设为 `\\.\pipe\simple`。函数随后在
`0x00401eb0` 调用导入的 `KERNEL32!CreateNamedPipeA`，静态恢复出的参数为：

```text
CreateNamedPipeA(
    "\\\\.\\pipe\\simple",  // 或命令行 -pipe /pipe 后的覆盖值
    0x40000003,               // PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED
    6,                        // PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE
    1,                        // 单实例
    0, 0,                      // 输出/输入缓冲大小按 API 参数传入 0
    0x3e8,                    // 1000 ms 默认超时
    &security_attributes);
```

同一函数在 `0x00401f2f` 调用 `ConnectNamedPipe`，在 `0x00401faa` 调用
`ReadFile`，读取缓冲区上限是 `0x104` 字节；随后在 `0x00401ff3` 构造
`CMSIWmiAcpi`，并在 `0x0040200c` 调用其解析器 thunk（目标是
`MSIWmiAcpi.dll!CMSIWmiAcpi::CallMSIWmiAcpiParser`）。解析完成后，
`0x0040205b` 调用 `WriteFile`，从服务端输出缓冲区请求写出固定 `0x168` 字节；
`0x004020b3` 调用 `DisconnectNamedPipe`，`0x004020a8/0x004020bf` 释放
解析器对象。导入表和 REA 的外部被调用者清单同时显示了
`CreateNamedPipeA`、`ConnectNamedPipe`、`ReadFile`、`WriteFile` 和
`DisconnectNamedPipe`，所以这是二进制级别的 IPC 事实，不是依据字符串名称
推断出来的协议。

该函数先扫描命令行参数；参数名的首字符可以是 `-` 或 `/`，而参数名去掉
前缀后与 `pipe` 比较成功时，取其后一个参数作为管道名。因此默认名称是
`\\.\pipe\simple`，但服务也有静态可见的管道名覆盖路径。函数中还创建事件、
使用 `OVERLAPPED` 结构并等待事件，这解释了导入的
`CreateEventA`、`WaitForMultipleObjects`、`ResetEvent` 和 `CloseHandle`；
它没有证明任何 TCP 端口或网络协议。

`FUN_00401ca0` 的两个静态调用者位于 `0x004011c0` 和 `0x00401930`，对应
服务初始化/线程路径；这两个位置把两个参数转交给该函数。入口
`0x0040d29a` 只是 CRT 启动代码，不能把它当作管道处理函数。服务端静态字符串
没有 `Q:` 或 `S:`，因此请求命令的解释边界确实在外部 `MSIWmiAcpi.dll`，而
不是 MSIService.exe 自己硬编码的另一个协议解析器。

### `SECURITY_ATTRIBUTES` 的范围

函数会准备一个 `SECURITY_ATTRIBUTES` 并将其地址传给 `CreateNamedPipeA`；
反编译中可见 `nLength`、继承标志和安全描述符初始化辅助路径，相关导入包括
`InitializeSecurityDescriptor`。目前证据只足以确认创建管道时传入了该结构，
不足以恢复最终 DACL、客户端 SID 要求或服务安装后的实际 ACL。实现替代客户
端时不能把“可调用 `CallNamedPipeA`”解释成“任何用户一定能打开该管道”，应在
Windows 实机上以只读句柄检查确认访问控制。

## `MSIWmiAcpi.dll`：客户端包装和请求解析

### 导出表和同步客户端入口

样本导出了以下与本边界直接相关的符号（地址为 ImageBase `0x10000000` 加
RVA）：

| 导出 | 地址 | 静态作用 |
|---|---:|---|
| `MSI_CallNamePipe_CSharp` | `0x10001920` | C# 适配入口；将两个指针参数解引用为输入/输出缓冲区后调用 `CallNamedPipeA` |
| `MSI_CallNamePipe` | `0x10001950` | C 入口；直接调用 `CallNamedPipeA` |
| `IsMSIMethodWMI` | `0x10001980` | 建立 WMI COM 连接并在 `root\\WMI` 做方法相关查询 |
| `CMSIWmiAcpi::CallMSIWmiAcpiParser` | `0x10002eb0` | 解析管道输入并调用 Query/Set/Method WMI 边界 |
| `CMSIWmiAcpi::QueryWMIProperty` | `0x100026d0` | C 字符串参数版本；由解析器 Q 分支调用 |
| `CMSIWmiAcpi::SetWMIProperty` | `0x10002940` | C 字符串参数版本；由解析器 S 分支调用 |
| `CMSIWmiAcpi::MethodWMI` | `0x100043e0` | 执行方法定义；由解析器方法分支调用 |

`MSIWmiAcpi.dll!MSI_CallNamePipe`（`0x10001950–0x10001978`）恢复为：

```c
CallNamedPipeA("\\\\.\\pipe\\simple",
               param_1, param_2, param_3, param_4,
               &bytes_read, 0);
```

`MSI_CallNamePipe_CSharp`（`0x10001920–0x1000194c`）执行同一调用，但传入
`(LPVOID)*param_1` 和 `(LPVOID)*param_3`，即为托管层持有的指针包装提供 ABI
适配。两者的管道字符串、`CallNamedPipeA` 导入和参数顺序均由反编译及汇编
调用点确认；该调用是同步请求/响应，`nTimeOut=0` 由函数字面量传入。它证明
DLL 提供了调用 `simple` 的客户端能力，但单凭导出表不能证明 Dragon Center
的每个 .NET 页面都走这个入口；需要将 .NET 调用点与同版本 DLL 做逐项绑定。

### Q/S 分支和 WMI 边界

`CMSIWmiAcpi::CallMSIWmiAcpiParser` 的函数体为 `0x10002eb0–0x100043d6`，
签名恢复为 `void __thiscall (CMSIWmiAcpi*, char*, char*)`。函数开始建立
COM/WMI 上下文并按冒号扫描字段。首字段被转为宽字符后传入 WMI 包装；
随后截取第二字段，`__mbscmp` 明确将其与 `"Q"` 比较，之后又与 `"S"` 比较，
与托管调用中的 `root\WMI:Q:...` 顺序一致；所以 Q/S 是真实的输入分派 token，
不是仅由导出名猜出的命名。

Q 分支先继续切分冒号字段，然后把
`%s.InstanceName='ACPI\\pnp0c14\\0_%s'`（只读格式字符串位于
`0x101401dc`）格式化为 WQL 过滤条件。REA 的汇编/伪代码显示其在解析器
源地址 `0x1000378e` 调用 `QueryWMIProperty`（目标 `0x100026d0`），并把
`L"WQL"` 作为查询语言参数传入，同时准备 `0x104` 字节的结果/错误缓冲区。
这条链已经到达 WMI 查询包装，而不是停留在字符串匹配层。

S 分支同样按冒号切分多个字段，然后构造相同形式的 `InstanceName` 过滤条件，
把字段内容复制到输入缓冲区，并在解析器源地址 `0x10003be6` 调用
`SetWMIProperty`（目标 `0x10002940`），也传入 `L"WQL"`。函数内部还可见
多处按十六进制字段格式化的通用字符串 `%s:%x:%x:%x:%x`（搜索证据中的
地址为 `0x1011b640`）。然而，Q/S token 是通过比较和运行时切分得到的，
没有一个可直接引用的完整 `S:` 字符串。此次有限分析尚未逐项恢复并核对
所有字段到 class/property/value 参数的对应；MFC CString/可变参数辅助函数的
自动原型也有缺失，伪代码中出现无法解释的 `猥` 字面值。保留原始输出和汇编，
不能将这些显示异常升级为完整协议语义；这不意味着协议无法继续静态恢复。

解析器后续的另一分支在 `0x10004203` 调用 `MethodWMI`（目标
`0x100043e0`）。该函数的反编译主体分布在 `0x100043e0–0x100044e9` 和
`0x100044f0–0x10004936`；它通过 WMI COM 服务执行方法定义，检查
`__PATH`/输出参数，并在异常输出路径使用
`"ExecMethod function is OK, but OutParam is not correct!"`。因此 DLL
不只支持属性 Query/Set，也存在方法调用边界；但目前仍没有把某个 Dragon
Center UI 操作静态一一映射到某个具体 WMI 方法名。

`IsMSIMethodWMI`（`0x10001980–0x10001b73`）的 COM 路径直接包含
`root\\WMI` 并执行 WQL 查询，这与解析器使用 WQL 和 `ACPI\\pnp0c14` 实例
筛选相互印证。它不能单独证明所有 Q/S 请求都必定落在同一命名空间，但至少
证明该 DLL 的方法/查询基础设施确实面向 `root\\WMI`，而不是只有用户态配置
文件或注册表读写。

## 证据分级与尚未恢复的协议字段

**二进制已证实。** `MSIService.exe` 创建默认 `\\.\pipe\simple` 的单实例
消息模式管道，接受 `-pipe`/`/pipe` 覆盖名，读取最多 `0x104` 字节，经导入的
`CMSIWmiAcpi` 解析后请求写出 `0x168` 字节并断开；`MSIWmiAcpi.dll` 的两个
`MSI_CallNamePipe*` 导出同步调用同名管道；解析器按冒号切分并显式分派 Q/S，
Q 到 `QueryWMIProperty`、S 到 `SetWMIProperty`，另有 `MethodWMI` 分支；WMI
COM 路径包含 `root\\WMI` 和 WQL。

**尚待 Windows 实机验证。** 当前仍未确认 Dragon Center 运行时实际传入的
完整 Q/S 请求字节串、是否含 NUL/CRLF、输出缓冲中的返回值布局、服务进程的实际安装/启动绑定、最终管道 ACL、WMI 类/属性/方法在 GS66 10SE 的
`16V1EMS1` 固件上具体对应哪些 EC 通道，以及 Dragon Center UI 的哪一个版本
是否采用 `MSI_CallNamePipe_CSharp` 而不是另一条 .NET/服务调用路径。这些尚未恢复的参数和 provider 映射，不能由公开项目中其他代际的寄存器
地址或请求例子替代。

在后续 Windows 只读验证中，应先记录服务版本、文件哈希、管道 ACL、已存在的
WMI 类/实例和 Dragon Center 版本；先抓取“读取 UI 状态”对应的调用参数，
再在隔离、明确授权的测试机上比较 Q 与 S 的差异。任何 EC/平台写入、未知
寄存器尝试和驱动实验都不属于本阶段。
