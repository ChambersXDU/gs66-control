# Windows 桌面程序

当前只维护 `src/Gs66Control.Desktop` 原生 WPF 界面，使用与构建说明见 [桌面控制台](desktop.md)，当前证据及待验证问题见 [开发状态](development-status.md)。

早期 `v0.1.0-preview.1` Release 是 WinForms 只读诊断原型，源码保留在 Git 历史，不代表当前控制功能或最新实机验证结果。不再维护第二套界面。

离线构建完整 Windows 包：

```sh
python scripts/package-windows.py --output artifacts/release
```

输出包含桌面程序、独立 helper、校验与安装脚本、源码 ZIP 和哈希报告。交叉构建成功不能替代 Windows 运行验证。GitHub 预览发布工作流仅在显式推送预览 tag 时运行，普通源码推送只触发模拟与离线构建测试。
