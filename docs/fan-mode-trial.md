# 第一项控制功能：原厂自动风扇模式

2026-10-13，用户批准的单次 5 秒实机试验完成：`0x8D → 0x0D → 0x8D`，两次属性更新与独立回读均成功。普通构建的 trial 入口继续锁定，启用试验的临时构建及其 bin/obj 已删除。桌面新增手动“应用原厂自动风扇模式”，切换后保留 `0x0D`；该独立入口只允许这一固定目标，不提供曲线或其他模式写入。

## 绑定与准入

已验证组合仅为 Micro-Star International Co., Ltd. / GS66 Stealth 10SE、BIOS `E16V1IMS.10C`、MSI_Software 报告的 EC `16V1EMS1.109`，原样构建文本 `0311202011:16:49`。安装的 Base Module SHA-256 为 `cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192`。原厂 Legacy 自动模式读取 `root\WMI:MSI_System` ordinal 9 / `System`，与 `0x3F` 做 AND 后更新同一实例；本机已知状态只有 `141/0x8D` 与 `13/0x0D`。

helper 核对身份、固件、artifact 哈希、完整 Active 实例、key 后缀与 ordinal 顺序，以及 System/Software/CPU/VGA schema；拒绝未知状态、来源不符、权限不足、过期采集或并发模式变化。切换准入要求 CPU 1–80°C、GPU 1–75°C，采集开始距现在不超过 10 秒；这是试验准入条件，不是认证的安全运行温度。CPU/GPU 首选原厂 WMI ordinal 1；GPU 无效时，控制预检可使用已核对的 NVIDIA 工具只读查询，详见 [来源](telemetry-provenance.md)。普通监控不连续调用该工具。

## 单次实机证据

先前 GPU WMI 温度不可用时，预检拒绝启动且没有写入。用户随后允许自主进行可逆诊断和轻量 ffmpeg 独显唤醒；使用已安装 ffmpeg 的 320×180、2 fps NVENC 空输出任务，设置最长 60 秒并在 finally 中停止自己的进程。没有处理用户文件、生成视频或运行压力测试。

18:14 的最终准入为 CPU 53°C、GPU 44°C、模式 141，全部绑定检查通过。独立 worker 更新至 13，回读后发送 `AUTOMATIC_MODE_READ_BACK_CONFIRMED`；协调器保持 5 秒后正常结束。worker 返回 `IsSimulated=false, Outcome=Restored, FinalMode=141, RecoveryVerified=true`，随后的独立查询仍为 141，CPU 49°C、GPU 45°C。两次真实属性更新分别是切换与恢复，没有重复试验或实机故障注入。

首次 PowerShell 外层包装器因 Start-Process/Refresh 丢失退出码，输出空退出码并返回脚本状态 2；原始失败日志保留。成功结论来自 worker 独立结果和随后的独立属性回读，不能把外层状态改写为成功。后续包装改为保留 .NET Process 句柄以取得退出码，未因此重跑试验。启用试验的文件已清理，一次性许可已消费。原始日志与 JSON 保存在忽略的 captures 和 GS66 验证目录，未提交机器标识。

该试验验证了模式字段更新和正常恢复；没有证明物理风扇响应、长时间散热表现或实机故障恢复，也不意味着已完整替代 Dragon Center。

## 日常桌面动作与独立进程

窗口按钮和托盘快捷操作使用同一预检、请求关联 ID 和固定 helper。用户主动点击后，Windows 仅为该次操作请求管理员权限，没有常驻提权服务。当前已是自动模式时，只核对身份/模式并报告无需写入；切换则必须取得有效温度。预检失败、取消 UAC 或缺少完整 helper 时不会开始写入。

独立 worker 拥有互斥锁与写入。READY 后收到 BEGIN 再次核验，切换后回读并发送 APPLIED；收到 KEEP_AUTOMATIC 后独立核验自动模式并保存带请求 ID 的结果。桌面要求退出码、非模拟结果、请求 ID、目标模式和新一次只读回读全部一致，才报告成功。后台不能接收任意类、字段、地址、路径或目标值。自动模式后端即使被调用恢复方法也拒绝恢复 141。

父进程断开或心跳丢失时，worker 验证并保留原厂自动模式；发现外部模式修改则拒绝覆盖并报告 RecoveryFailed。原批准 trial 的正常 END 才能恢复原基线，普通构建无法运行该试验。心跳每 2 秒到达，绝对期限 15 秒，持续心跳不能延长期限。

WMI 没有原子 compare-and-swap，检查与 Put 之间仍有并发窗口。独立进程无法保证在自身终止、Windows/WMI 故障或调用卡住时恢复。故障回退已通过进程模拟，未在实机注入验证。厂商服务、热保护、驱动和固件保持原有配置。

## 验证

macOS 和 GS66 Windows 各通过 56 项单元测试与 25 项独立进程模拟，CLI/helper/桌面编译通过。模拟覆盖正常恢复、保留自动模式、原本自动且温度不可用、父进程退出/强制结束/挂起、心跳和期限、并发修改、锁竞争、权限/写入拒绝、确认丢失、恢复失败、非法协议及模拟来源不匹配；模拟结果始终标明 IsSimulated。

Windows 空闲 GPU 预检取得 CPU 48°C、GPU 42°C（NVIDIA 只读来源）、模式 141，Eligible=true，预检写入为零。桌面四页 smoke 验证实际遥测、设置/方案持久化、非法曲线拒绝与控制预览；smoke 路径禁止启动应用操作，没有再次写硬件。CI 仅运行编译、单元测试和模拟，不访问实际硬件。
