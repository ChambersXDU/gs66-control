# Windows 桌面诊断原型

`src/Gs66Control.App` 是 .NET 8 WinForms 中文界面，复用 Core 的模拟探针和 Windows 的只读 WMI 探针。目标设备为 MSI GS66 Stealth 10SE；具体 BIOS / EC 版本仍以用户机器上的诊断为准。

## 使用

1. 解压 `artifacts/GS66Control-win-x64.zip`，在 Windows x64 上运行 `windows-x64/Gs66Control.exe`。自包含发布不要求预装 .NET；保留整个目录，不要只复制 exe。交付包附有 `GS66Control-win-x64.zip.sha256` 校验文件。
2. 启动不会自动访问硬件。点击“实机只读诊断”查询厂商、型号、CPU、GPU、BIOS 版本及 MSI 命名的 WMI 类定义（类名、GUID、属性、方法名称）；不会读取 MSI 类的实例或执行方法。普通用户权限即可尝试。查询失败会提示缺失值，无需提权、重启或修改 BIOS。
3. 点击“模拟演示”查看明确标注的示例。模拟数据不能用于确认机器配置、固件版本或控制支持情况。
4. 查看“缺失读数与警告”以及“导出预览”，然后使用“导出 JSON”。保存的 JSON 使用完整共享 snapshot 的 camelCase 字段，保留来源、`isSimulated` 和 `msiWmiSchemas` 类定义，不收集序列号、账户名或原始 WMI 对象；探针仅保留异常类型，省略 WMI 异常消息。型号和版本仍可能识别设备类型，分享前可检查预览。

查询和导出期间按钮防止重复操作。取消为协作式：正在执行的 WMI 查询未必立即停止，返回后不会显示已取消的结果；关闭窗口会请求取消，后台工作不会更新已关闭的窗口。取消导出使用临时文件，不覆盖原文件。

界面显示的采集时间采用运行机器的本地时区，JSON 的 `capturedAtUtc` 保留 UTC。当前没有测量温度、风扇转速、风扇曲线或 EC 版本，也没有 EC 写入、BIOS 刷写、驱动加载、功耗控制或自动压力测试功能。发现 WMI 类名不等于验证其方法可用于控制。

## 构建与发布

安装 .NET 8 SDK 后：

```powershell
dotnet build src/Gs66Control.App/Gs66Control.App.csproj
./scripts/publish-windows.ps1
```

脚本默认发布自包含 `win-x64` 到 `artifacts/windows-x64`；可传入 `-OutputDirectory`。Linux 上可交叉构建发布：

```bash
dotnet publish src/Gs66Control.App/Gs66Control.App.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -o artifacts/windows-x64
```

Linux 构建需要 NuGet 下载 Windows Desktop targeting / runtime 包。交叉编译成功不能代替 Windows UI 或 GS66 硬件验证。当前环境不能运行 WinForms，因此必须在 Windows 上手工验证启动不采集、两种数据来源、取消/关闭、查询失败提示及 JSON 导出。

GitHub 发布流程在推送 `v*-preview.*` tag 时，由 Windows runner 执行模拟/离线测试、自包含发布、完整 ZIP 和 SHA-256 校验文件生成，再上传预览 Release；只有构建与测试成功才公开 Release。此流程不进行 WMI 实机采集，发布包的实际 SHA-256 以对应 Release 附件为准。

2026-10-11 验证结果：Linux 上 Release 构建成功，0 个警告、0 个错误；`win-x64` 自包含发布成功。生成的 `artifacts/windows-x64/Gs66Control.exe` 已确认是 x86-64 Windows GUI PE，完整目录约 162 MiB。没有在 Windows 上运行 UI，也没有采集真实 GS66 的硬件数据。
