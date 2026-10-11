# Dragon Center 托管反编译证据索引

本文只保留原厂托管程序集的可复核静态证据，供主报告引用。代码来自离线 ILSpy/REA 反编译文本；每个证据块给出反编译文件、原始 artifact SHA-256 和 1-based 行号。没有执行 Windows 程序、服务、WMI、命名管道、原生 DLL 或任何硬件访问。

固定的 WMI ordinal（例如 MSI_CPU 的 11–16、MSI_System 的 9）是 WMI 枚举协议中的对象序号，不是 EC 地址。WMI2 的 D2/D4/D7 与 0x98 等数值是原厂实现中的参数/端点常量，不能直接当作 GS66 10SE、16V1EMS1 G1 的实机寄存器；下文明确区分分支和不确定性。

NamedPipeClientLib.cs 对应 N pf/MSI/One Dragon Center/Battery 的 1.1.2103.1701 副本（29ce...），不是 Foundation 1.1.2408.0501 副本（3bef...）；两个旧副本的反编译文本相同但二进制哈希不同。当前服务端协议另由 MSIAPService 证实。

为控制篇幅，代码块只摘录关键语句，部分表达式规范化为伪代码；逐字截取、精确行号与源文本哈希见 [managed-exact.json](managed-exact.json)；省略号表示原始反编译代码中未复制的中间语句，不是对原始文件行号的替换。源行号仍指向 /tmp/gs66-research/decompiled 中对应 artifact 的完整反编译文本。

## Artifact 清单

| 反编译文件 | 原始 artifact | SHA-256 |
| --- | --- | --- |
| UC_NB_Base_Module.cs | UC_NB_Base Module.dll | c11007bc5bb73b9b16ea5d60b331257cbbe7a8e20ade75f611b54810298c72e7 |
| API_NB_Base_Module.cs | API_NB_Base Module.dll | cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192 |
| MSIWMIACPI2.cs | MSIWMIACPI2.dll | 1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828 |
| API_NB_Battery.cs | API_NB_Battery.dll | db88c39b264beafd8fd409a344316a1ea867316e43fda7ff39c8cc77d778af9e |
| UC_NB_Battery.cs | UC_NB_Battery.dll | ecb508b9473b1d3a92fa01fcdae3b71822750a30f1dc2debed5db4a708dad46d |
| NamedPipeClientLib.cs | NamedPipeClientLib.dll | 29ce3e3a0ac831aeadd118316ef5740963bdf60e8516b72c9cd4ae703cbf94c5 |
| MSIAPService.cs | MSIAPService.exe | 52de5b9df1212f3cf040338b1ae92250d1021af9980424baaef9551e427ad982 |
| KernCoreLib64.dll | 原生 DLL（未反编译为 C#） | dafc2c87b674e3b7f1fb355064061ad48fb33c79c0eb172390b04e1261e8bf0b |

## A. UI 风扇曲线：两组各六个值，命令头 {0,21}

### A.1 非游戏窗口的保存路径

证据：UC_NB_Base_Module.cs，UC_NB_Base Module.dll，SHA c11007bc5bb73b9b16ea5d60b331257cbbe7a8e20ade75f611b54810298c72e7，行 9169–9311。

~~~csharp
9171 private static byte[] CMD_SetFanAdvanced = new byte[2] { 0, 21 };
9292 for (int i = 0; i < 6; i++)
9294     text = text + CPU_Slider[i].Value + ";";
9298     text2 = text2 + GPU_Slider[i].Value + ";";
9303 text2 = text2.TrimEnd(new char[1] { ';' });
9304 fanValue.Advanced = text + text2;
9305 byte[] bytes = Encoding.ASCII.GetBytes(C_JSON.JSONSerializer(fanValue));
9307 Array.Resize(ref CMD_SetFanAdvanced, 2 + bytes.Length);
9308 Array.Copy(bytes, 0, CMD_SetFanAdvanced, 2, bytes.Length);
9309 C_Client.SendData(DataCenter.ComponentID, CMD_SetFanAdvanced);
~~~

反编译原文的控件访问分别是 uxCPU_Grid.Children[i] 和 uxGPU_Grid.Children[i]；上面用 CPU_Slider[i]/GPU_Slider[i] 仅为压缩表示。i < 6 同时遍历两组，Advanced 是 CPU 六项字符串与 GPU 六项字符串拼接后的 JSON 字段，命令前两字节为 {0,21}。这证明 UI 数据布局为 6+6；没有证明点值的单位、温度阈值语义或底层硬件通道。

### A.2 游戏窗口重复相同协议

证据：同文件同 SHA，行 9500–9642。该窗口再次声明 CMD_SetFanAdvanced = new byte[2] { 0, 21 }，在行 9623–9635 以 for (int i = 0; i < 6; i++) 收集 CPU/GPU 两组滑块，在行 9636–9641 序列化、扩容、复制并调用 C_Client.SendData。这是第二个 UI 入口，不是第二套硬件映射；后端场景由窗口上下文决定。

## B. Legacy/WMI1 风扇曲线：MSI_CPU、MSI_VGA 和 MSI_System

### B.1 六次 WMI Put

证据：API_NB_Base_Module.cs，API_NB_Base Module.dll，SHA cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192，行 10898–10951。

~~~csharp
10899 string text = "CPU";
10901 if (!isCPU) {
10903     text = "VGA";
10905     val5 = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSI_VGA");
10907 } else
10909     val5 = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSI_CPU");
10912 var enumerator = val5.Get().GetEnumerator();
10918 switch (num) {
10920 case 11: val6.SetPropertyValue(text, value0); val6.Put(); break;
10925 case 12: val6.SetPropertyValue(text, value1); val6.Put(); break;
10930 case 13: val6.SetPropertyValue(text, value2); val6.Put(); break;
10935 case 14: val6.SetPropertyValue(text, value3); val6.Put(); break;
10940 case 15: val6.SetPropertyValue(text, value4); val6.Put(); break;
10945 case 16: val6.SetPropertyValue(text, value5); val6.Put(); break;
10951 num++;
~~~

isCPU=true 选择 MSI_CPU/属性 CPU，否则选择 MSI_VGA/属性 VGA。只有枚举序号 11–16 触发写入，每项单独调用 Put()。这是两个逻辑 WMI 风扇对象组的证据；它不能证明机器有六把物理风扇，也不能把 11–16 当成 EC 偏移。

### B.2 高级模式状态位

证据：同文件同 SHA，行 10952–10981；自动模式清除路径行 10711–10738。

~~~csharp
10952 num = 0;
10953 enumerator = new ManagementObjectSearcher(
10954     "root\\WMI", "SELECT * FROM MSI_System").Get().GetEnumerator();
10960 if (num == 9) {
10962     int.TryParse(val7["System"].ToString(), ..., out var result);
10967     result |= 0x80;
10968     result &= 0xBF;
10970     val7.SetPropertyValue("System", result);
10971     val7.Put();
10973     break;
10975 num++;
~~~

高级曲线写完后，MSI_System 的 ordinal 9 设置 bit7、清除 bit6，再 Put()。自动模式在行 10720–10729 对相同 ordinal 使用 result &= 0x3F。可证实的是 WMI1 抽象状态字的 bit6/bit7 操作；源码没有给出它与 16V1EMS1 某个固定 EC 地址的等价关系。

## C. WMI2 风扇包：Get_Fan/Set_Fan 和模式保存

### C.1 方法编号与长度

证据：MSIWMIACPI2.cs，MSIWMIACPI2.dll，SHA 1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828，行 41–76、207–242。

~~~csharp
41 public enum Methods {
53 Get_Temperature, 55 Get_Thermal, 57 Get_Fan, 58 Set_Fan,
65 Get_AP, 66 Set_AP, 67 Get_Data, 68 Set_Data, 69 GetPackage, 70 SetPackage
}
207 public List<int[]> DataLength { get; } = new List<int[]> {
221 new int[4] { 2, 7, 7, 7 }, 223 new int[3] { 8, 8, 8 },
231 new int[3] { 6, 3, 7 }, 233 new int[1] { 1 },
235 new int[1] { 16 }, 236 new int[1] { 16 }
};
~~~

枚举从零开始，故 Get_Fan=14、Set_Fan=15、Get_AP=22、Get_Data=24、Set_Data=25。Fan 参数的有效长度为 8 字节；Data 方法为 1 字节。长度数组的其他行属于 WMI2 的其它方法，不能用来推断十代机型的 EC 结构。

### C.2 CPU/GPU 逻辑组、六值写回、D4

证据：API_NB_Base_Module.cs，API_NB_Base Module.dll，SHA cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192，行 10824–10896。

~~~csharp
10824 if (_wmiAcpiLib2.WmiMajorVersion >= 2) {
10826 byte b = 1; if (!isCPU) b = 2;
10832 var val = InvokeWmiMethod((Methods)14, b, new byte[8]);
10836 val.Data[1] = (byte)value0; ... val.Data[6] = (byte)value5;
10855 var val2 = InvokeWmiMethod((Methods)15, b, val.Data);
10866 var val3 = InvokeWmiMethod((Methods)22, (byte)1, new byte[3]);
10869 byte b2 = val3.Data[0];
10870 b2 = SetBit(b2, 6, true); b2 = SetBit(b2, 7, false);
10888 var val4 = InvokeWmiMethod((Methods)25, (byte)212, new byte[1] { b2 });
10893 Console.WriteLine("Failed to write data into EC address 0xD4.");
~~~

WMI2 端的 b=1 代表 CPU 逻辑组，b=2 代表 GPU 逻辑组；Fan 返回包的 Data[1]–Data[6] 被 UI 六值替换，再以 Set_Fan 写回。随后 Get_AP(1) 修改 bit6/bit7，并以 Set_Data 参数 212（0xD4）保存模式。D4 是该 WMI2 分支的固定参数；不能回填为 GS66 10SE G1 的已验证寄存器。

## D. Cooler Boost：Legacy 0x98 与 WMI2 参数 0x98

证据：API_NB_Base_Module.cs，API_NB_Base Module.dll，SHA cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192，行 8450–8519。

~~~csharp
8462 var val = InvokeWmiMethod((Methods)12, (byte)3, new byte[7]);
8467 num = val.Data[0];
8472 num = int.Parse(MemRwService.Read_To_Byte_Array("98")[0].ToString(), ...);
8476 if (num >= 128) isCoolerBoost = true;
8497 var val = InvokeWmiMethod((Methods)12, (byte)3, new byte[7]);
8504 b = isCoolerBoost ? SetBit(b, 7, true) : SetBit(b, 7, false);
8510 InvokeWmiMethod((Methods)25, (byte)152, new byte[1] { b });
8513 int num = int.Parse(MemRwService.Read_To_Byte_Array("98")[0].ToString(), ...);
8517 int num2 = isCoolerBoost ? (num | 0x80) : (num & 0x7F);
8519 MemRwService.Write("98", data);
~~~

WMI2 Get_Thermal(12, 参数 3) 读取状态，Set_Data(25, 参数 152/0x98)写回 bit7；Legacy 通过 MemRwService 读写 "98"，同样把 bit7 作为启用位。此证据只证明逻辑字段/端点，未证明三物理风扇分别可控。

## E. Shift 与 UserScenario F4 旁路

### E.1 Legacy simple 管道与 WMI2 D2

证据：API_NB_Base_Module.cs，API_NB_Base Module.dll，SHA cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192，读取行 1196–1233，写入行 1312–1336。

~~~csharp
1198 var val = InvokeWmiMethod((Methods)22, (byte)0, new byte[6]);
1201 result = val.Data[2];
1208 string text = "root\\WMI:Q:MSI_System:System:" + num;
1211 flag = CallNamedPipe("\\\\.\\pipe\\simple", ...);
1225 string s = Marshal.PtrToStringAnsi(intPtr2);
1227 if (!int.TryParse(s, ..., out result2)) return Fallback();
1314 var val = InvokeWmiMethod((Methods)25, (byte)210, new byte[1] { (byte)num });
1323 string text2 = "root\\WMI:S:MSI_System:System:" + num2 + ":" + num;
1328 flag = CallNamedPipe("\\\\.\\pipe\\simple", ...);
~~~

Legacy 读取请求是 root\\WMI:Q:MSI_System:System:7，通过 \\\\.\\pipe\\simple；写请求是 root\\WMI:S:MSI_System:System:7:<num>。WMI2 对应 Get_AP 参数 0 读取，Set_Data 参数 210（0xD2）写回。D2 是 WMI2 端点，不是 G1 寄存器事实。

### E.2 F4 初始化路径（会写入）

证据：API_NB_Base_Module.cs，API_NB_Base Module.dll，SHA cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192，行 8642–8675。

~~~csharp
8642 if (Scenario_Info.Index == 3) {
8645 if (_wmiAcpiLib2.WmiMajorVersion >= 2) {
8648 var val = InvokeWmiMethod((Methods)22, (byte)1, new byte[3]);
8653 b = val.Data[0];
8654 if (!GetBit(b, 4)) {
8656 b = SetBit(b, 4, true);
8657 InvokeWmiMethod((Methods)25, (byte)212, new byte[1] { b });
8663 } else {
8667 uint num = WinAndFnChange.MSI_App_Service_ReadMemoryEC(
8668     4227860724uL, out success);
8669 if (success && (num & 0x10) != 16) {
8671 uint value = num + 16;
8672 WinAndFnChange.MSI_App_Service_WriteMemory(
8673     4227860724uL, value);
~~~

4227860724 十进制为 0xFC0008F4。这是 Scenario index 3 初始化时，Legacy 读取 F4、置 bit4、再写回；WMI2 使用 AP(1) 与 D4(212)。它是 F4 相关模式位的二进制证据，但不等价于所有模式，也不证明 MSI_System ordinal 9 就是 F4。由于 init 明确含写调用，不能将启动该场景作为只读验证方案。

## F. 电池充电上限：UI 命令、D7 与 WMI1 服务地址

### F.1 UI 命令到场景

证据：UC_NB_Base_Module.cs，UC_NB_Base Module.dll，SHA c11007bc5bb73b9b16ea5d60b331257cbbe7a8e20ade75f611b54810298c72e7，行 15967–15985、16662–16678、19660–19707。

~~~csharp
15973 CMD_SetExtremeMode = new byte[2] { 1, 1 };
15975 CMD_SetBalancedMode = new byte[2] { 1, 2 };
15977 CMD_SetSlientMode = new byte[2] { 1, 3 };
15979 CMD_SetUserMode = new byte[2] { 1, 4 };
15981 CMD_SetBatteryMode = new byte[2] { 1, 5 };
19686 if (CompareBytes(SourceData, CMD_SetBatteryMode, 0, 2) == 0) {
19689 if (DataCenter.isGaming) User_Scenario.ScenarioIndex(4);
19691 else User_Scenario_P.ScenarioIndex(4);
~~~

{1,5} 只是 UI 到场景组件的命令，场景 index 4 不是 EC 地址。硬件相关路径由 Battery API 的文本命令继续处理。

### F.2 UI 文案与 Foundation 命令

证据：UC_NB_Battery.cs，UC_NB_Battery.dll，SHA ecb508b9473b1d3a92fa01fcdae3b71822750a30f1dc2debed5db4a708dad46d，行 3193–3199、3402–3497。

~~~csharp
3193 enum BatteryModeNumber { Mobility, Balanced, Battery, AI }
3408 Mobility: "Charge the battery to 100% all the time";
3412 Balanced: "under 70%, stop at 80%";
3416 Battery: "under 50%, stop at 60%";
3428 ReadECBatteryMemory() -> "ReadECBatteryMemory";
3470 case 0 -> "WriteECBatteryModeBestMobility";
3477 case 1 -> "WriteECBatteryModeBalanced";
3484 case 2 -> "WriteECBatteryModeBestBattery";
~~~

UI 文案直接给出 100%、80%、60% 停止阈值；AI 是枚举项，但上述写入 switch 只发送前三种命令。命令字符串是到 API 的调用边界，不是硬件地址。

### F.3 WMI2 D7 读改写

证据：API_NB_Battery.cs，API_NB_Battery.dll，SHA db88c39b264beafd8fd409a344316a1ea867316e43fda7ff39c8cc77d778af9e，行 1162–1238。

~~~csharp
1171 var val = InvokeWmiMethod((Methods)24, (byte)215, new byte[1]);
1174 if (val.Flag == 1) ECBatteryMemory = val.Data[0];
1184 bool WriteECBatteryMode(...) {
1186 if (_wmiAcpiLib.WmiMajorVersion >= 2) return Write..._WMIACPI_2(...);
1205 val = InvokeWmiMethod((Methods)24, (byte)215, new byte[1]);
1212 byte b = val.Data[0]; byte b2 = (byte)(b & 0x7F);
1217 Mobility: array[0] = (byte)(b - b2 + 100);
1225 Balanced: array[0] = (byte)(b - b2 + 80);
1231 BestForBattery: array[0] = (byte)(b - b2 + 60);
1221/1227/1233 InvokeWmiMethod((Methods)25, (byte)215, array);
~~~

WMI2 使用 Get_Data/Set_Data 参数 215（0xD7），保留原 bit7，再将低七位替换为 100/80/60。D7 是 WMI2 分支端点；它不能直接作为 GS66 G1 的 WMI1/EC 地址。

### F.4 WMI1/Legacy 的 0xFC0008EF

证据：API_NB_Battery.cs，API_NB_Battery.dll，SHA db88c39b264beafd8fd409a344316a1ea867316e43fda7ff39c8cc77d778af9e，行 1240–1268。

~~~csharp
1240 private bool WriteECBatteryMode_WMIACPI_1(...) {
1243 uint num = NativeNamedPipeClient.ReadMemory(4227860719uL, out readSuccess);
1245 uint num2 = num & 0x7F;
1255 Mobility -> WriteMemory(4227860719uL, num - num2 + 100);
1259 Balanced -> WriteMemory(4227860719uL, num - num2 + 80);
1263 BestForBattery -> WriteMemory(4227860719uL, num - num2 + 60);
~~~

4227860719 十进制为 0xFC0008EF。WMI1 通过 Foundation Service 的 ReadMemory/WriteMemory，对相同状态字使用 & 0x7F。这证明原厂服务协议中的地址和 mask，不证明该地址跨 BIOS/EC 版本或跨机型有效。

### F.5 文本命令分发

证据：API_NB_Battery.cs，同 SHA，行 1371–1405。Transfer_Command 在 ASCII 命令 ReadECBatteryMemory、WriteECBatteryModeBestMobility、WriteECBatteryModeBalanced、WriteECBatteryModeBestBattery 上分别调用读取或 WriteECBatteryMode(BestForMobility/Balanced/BestForBattery)；返回值是电池值或 ECBatteryModeWriteSuccess。这连接了 UI 命令、Battery API 和上述 WMI1/WMI2 两个端点。

## G. NamedPipeClientLib：服务管道、加密与内存请求

### G.1 密钥、3DES 和 framing

证据：NamedPipeClientLib.cs，NamedPipeClientLib.dll，SHA 29ce3e3a0ac831aeadd118316ef5740963bdf60e8516b72c9cd4ae703cbf94c5，密钥行 41–82，framing/握手行 100–110、1184–1234。

~~~csharp
47 return "msiservice" + ProductName + DateTime.Now.ToString("yyyyMMdd");
54 byte[] key = MD5(ASCII(GetClientRawStringForKeyGeneration()));
59 triple.Key = key; triple.Mode = CipherMode.ECB;
63 triple.Padding = PaddingMode.PKCS7; triple.Padding = PaddingMode.Zeros;
104 const string PipeName = "MSI_SERVICE_2";
106 const string FullPipeName = "\\\\.\\pipe\\MSI_SERVICE_2";
1187 bytes = BitConverter.GetBytes(command.Length + 6);
1189 if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
1191 byte[] header = new byte[6] { 0, 255, 0, 0, 0, 0 };
1192 Array.Copy(bytes, 0, header, 2, 4);
1222 if (byteInput[0] == 0 && byteInput[1] == byte.MaxValue &&
1224 text.ToUpper().IndexOf("HS:") > 0) return true;
~~~

客户端用 msiservice + 进程 ProductName + yyyyMMdd 的 ASCII 字符串做 MD5 key，TripleDES 为 ECB；反编译文本连续设置 PKCS7 后又设置 Zeros，最后一次属性赋值需要按 .NET 语义解释。普通 framing 是 00 FF 加大端四字节长度，长度含 6 字节 header。HS: 握手包单独识别。

### G.2 连接和请求边界

证据：同文件同 SHA，服务调用行 1236–1345，内存读写行 1347–1397。

~~~csharp
1243 CreateFile("\\\\.\\pipe\\MSI_SERVICE_2", GENERIC_READ|GENERIC_WRITE, ...);
1263 if (!CommandHandShake(messageToSend))
1264     messageToSend = EncrptyDecrypt.EncryptData(messageToSend);
1292 else if (!CommandHandShake(array3))
1293     array2 = EncrptyDecrypt.DecryptData(array3);
1329 header = GetHeaderByteArray("HS:REQ:" + ProductName + ";" + FileVersion);
1351 text = memoryAddress + ";" + 1;
1354 header = GetHeaderByteArray("IO:RMREQ:" + text);
1377 text = memoryAddress + ";" + 1 + ";" + memoryValue;
1381 header = GetHeaderByteArray("IO:WMREQ:" + text);
~~~

除握手外，发送前加密、接收后解密；响应中含 ERR 时返回错误字节。Foundation Service 的内存请求文本格式是 IO:RMREQ:<address>;1 和 IO:WMREQ:<address>;1;<value>。这些是协议格式证据，不代表本研究发送过请求或证明某地址可写。

## H. MSIAPService/KernCoreLib64 原生 I/O 边界

### H.1 委托和 EC 端口

证据：MSIAPService.cs，MSIAPService.exe，SHA 52de5b9df1212f3cf040338b1ae92250d1021af9980424baaef9551e427ad982，行 4840–4917、4974–5019。

~~~csharp
4841 private delegate bool InitializeWinIo();
4843 private unsafe delegate bool GetPortVal(ushort port, uint* value, byte size);
4846 private unsafe delegate bool SetPortVal(ushort port, uint value, byte size);
4849 private unsafe delegate bool GetPhysLong(UIntPtr addr, uint* value);
4852 private unsafe delegate bool SetPhysLong(UIntPtr addr, uint value);
4855 private delegate bool ShutdownWinIo();
4867 private static uint EC_Ram_Address = 244u;
4869 private static ushort EC_HIGH = 845;
4870 private static ushort EC_LOW = 846;
4871 private static ushort EC_DATA = 847;
4974 ReadMemory(UIntPtr PhysAddr, uint* pPhysVal) -> getPhysLong(...);
4998 WriteMemory(IntPtr PhysAddr, uint PhysVal) -> setPhysLong(...);
~~~

托管服务声明了端口读写、物理内存读写以及 EC index/data 端口候选值；这些是原生访问边界能力证据。没有调用任何 delegate，也没有将端口值解释为 GS66 的安全写入接口。

### H.2 动态加载 KernCoreLib64.dll

证据：MSIAPService.cs，同 SHA，行 5130–5177；被加载 artifact KernCoreLib64.dll，SHA dafc2c87b674e3b7f1fb355064061ad48fb33c79c0eb172390b04e1261e8bf0b。

~~~csharp
5140 string directoryName = Path.GetDirectoryName(
5141     Assembly.GetExecutingAssembly().Location) + "\\KernCoreLib64.dll";
5142 pDllWinIO = LoadLibrary(directoryName);
5144 GetProcAddress(pDllWinIO, "InitializeKernCoreLib");
5147 GetProcAddress(pDllWinIO, "GetPortVal");
5149 GetProcAddress(pDllWinIO, "SetPortVal");
5151 GetProcAddress(pDllWinIO, "GetPhysLong");
5153 GetProcAddress(pDllWinIO, "SetPhysLong");
5155 GetProcAddress(pDllWinIO, "ShutdownKernCoreLib");
5157 initializeWinIO = Marshal.GetDelegateForFunctionPointer(...);
5165 if (initializeWinIO()) { "Load driver success"; return; }
5171 "Load driver failed";
~~~

这说明 MSIAPService 在运行时加载同目录 KernCoreLib64.dll 并解析 native delegates。它说明服务硬件访问边界，不说明 DLL 内部的 GS66/G1 映射。

### H.3 EC index/data 探测代码

证据：MSIAPService.cs，同 SHA，行 5100–5128。

~~~csharp
5100 readECRam(ushort EC_HIGH, ushort EC_LOW, ushort EC_DATA)
5108 for (int i = 0; i < 4; i++) {
5110     setPortVal(EC_HIGH, EC_Ram_Address, 1);
5111     setPortVal(EC_LOW, (uint)(160 + i), 1);
5112     getPortVal(EC_DATA, &num, 1);
5117     if (text2 == "FFFFFFFF") return false;
~~~

静态代码出现 EC index 0xA0 起始探测（160 decimal）和 native port functions。这个片段不构成实机结果，不能作为任何未知寄存器的写入授权。

## I. WMI2 Package32 请求/响应布局

### I.1 Get/Set 输入包

证据：MSIWMIACPI2.cs，MSIWMIACPI2.dll，SHA 1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828，行 165–167、878–941。

~~~csharp
165 private readonly int Package_32 = 32;
167 private readonly int Header_Length_For_Set_Method = 1;
888 byte[] array = new byte[Package_32]; array[0] = parameter_in_byte;
925 Set(string methodName, byte parameter, int dataLength, byte[] inData)
935 array = new byte[Package_32];
937 array[0] = parameter;
938 Array.Copy(inData, 0, array, Header_Length_For_Set_Method, dataLength);
941 return MoClassInstance.InvokeMethod(methodName, ...);
~~~

普通 WMI2 Get/Set 使用 32 字节包，参数位于 array[0]，Set 的输入数据从偏移 1 复制；只有 SetPackage 使用 16 字节包。Fan 的 8 字节有效数据因此是 32 字节 Data 包中的一段。

### I.2 返回包的 Flag/Data 拆分

证据：同文件同 SHA，行 943–1038。

~~~csharp
957 if (methodName.Contains("Package")) outData_bytes = new byte[16];
960 else outData_bytes = new byte[Package_32];
966 int num = (methods == Methods.Get_Data ||
967            methods == Methods.Set_Data) ? 1 : DataLength[(int)methods][b];
971 var val = Get(methodName, b);
1021 result.Flag = outData_bytes[0];
1022 result.Data = new byte[Package_32];
1023 Array.Copy(outData_bytes, 1, result.Data, 0, outData_bytes.Length - 1);
~~~

API 中的 InvokeResults.Flag 是返回包第 0 字节，Data 从第 1 字节开始；因此 API 使用的 val.Data[0] 已跳过原始 WMI 包状态字节。这个偏移关系是请求格式证据，不是硬件寄存器映射。

## J. 可直接交给主报告的边界结论

1. 二进制直接证明：UI 将风扇曲线编码为 6 个 CPU 值和 6 个 GPU 值，命令头 {0,21}；Legacy 通过 MSI_CPU/MSI_VGA 的序号 11–16 写入，再操作 MSI_System 序号 9；WMI2 通过逻辑参数 1/2 和 Fan Get/Set 写入，之后保存 D4 模式状态。
2. 二进制直接证明：Cooler Boost 的逻辑字段是 bit7，Legacy 读取/写入 "98"，WMI2 读取 Thermal(3) 并以 Set_Data(152/0x98)写回；Shift 使用 Legacy \\\\.\\pipe\\simple 的 MSI_System:7 或 WMI2 D2；UserScenario index 3 的初始化路径有 F4/bit4 写入，不能作为只读探针。
3. 二进制直接证明：电池 UI 通过文本命令进入 Battery API；100/80/60 阈值在 WMI2 D7 或 WMI1 Foundation Service 地址 0xFC0008EF 的读改写路径中出现。WMI1 地址来自服务协议层，仍需实机/BIOS/EC 版本核验。
4. 二进制直接证明：Foundation Service 通过 MSI_SERVICE_2、动态 key/3DES framing、IO:RMREQ/IO:WMREQ 和 KernCoreLib64 delegates 提供硬件访问边界；这些协议和能力没有在本次环境执行。
5. 尚未由这些 artifact 证明：GS66 10SE 三把物理风扇是否映射到两个逻辑通道、每把风扇是否独立可控、16V1EMS1 G1 的具体 F2/F4 地址是否与其他代际相同，以及 WMI2 的 D2/D4/D7 是否在该 i7-10750H 机器可用。必须把公开 G1 源码和 Windows 实机只读查询作为独立证据。

本索引没有刷写 BIOS/EC、写 EC 寄存器、执行驱动、关闭安全机制、调用 WMI/命名管道或加载 KernCoreLib64.dll。
