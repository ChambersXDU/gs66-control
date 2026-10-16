# Dragon Center 证据包说明

研究对象与结论见 [主报告](../../dragon-center-re.md)。本目录仅包含公开原厂安装文件的离线元数据、有限反编译片段和静态分析结果；没有硬件 dump、序列号、用户账户或实机 WMI 输出，没有目标 PE/driver 文件。

## 样本与清单

`inventory-ui.json` 是外层 Inno 的 184 文件，`inventory-sdk.json` 是 MSI SDK 的 29 文件，`inventory-nbfoundation.json` 是 Setup.exe 的 160 文件。每个 JSON 的 path 相对于其提取根，不能用同名文件跨层替换。`binary-index.md` 索引三个树的所有 204 个 PE；非 PE 文件及完整 imports/exports/AssemblyRef 在 JSON 内。`config-excerpts.json` 逐字保存 AppxManifest 和 DCv2.exe.config，另由主报告解释启动/自动更新/Framework声明。CLI 标志与 PE Machine 均保留，避免把 PE32 IL-only 自动解释为仅 x86。

`rea-package.json`、`rea-installer.json` 是 REA artifact 静态检查结果。`rea-*-inspect_managed_artifact.json` 保存 MVID、metadata 和依赖；`rea-*-inspect_managed_native_boundaries.json` 保存 P/Invoke/动态 native 入口等证据。`rea-*-focused-members.json` **经过筛选**，以 source_evidence_id 和原完整结果 SHA-256 标识出处，保留研究所需方法的 token/RVA、CIL hash、anchors、call_edges，不是完整成员清单。原始完整结果留在本地研究目录。

## 控制流与原生代码

`managed-excerpts.md` 是便于阅读的片段说明，少数表达式为了压缩篇幅改写为规范化伪代码；精确的未改写 ILSpy 文本在 `managed-exact.json`，每段含原始行号、完整源文本哈希与 artifact SHA。NamedPipeClientLib 的旧版反编译来自 Battery 目录 1.1.2103.1701 (`29ce...`)，Foundation 1.1.2408.0501 (`3bef...`) 是另一份样本；JSON 同时截取两者密钥实现。UI 目录旧版的反编译文本与 Battery 副本相同，但完整二进制 SHA 不同。

`gamecenter-oc.md` 保存 CPU XTU/OC 与 GPU 注册表及 helper 的证据和未闭合调用边界。`native/README.md` 索引 KernCore DLL/SYS 的 Ghidra/REA 分析。KernCore 的 `*-evidence.json` 保存从 MCP structuredContent 取出的完整证据对象，MCP text+structuredContent 的重复 envelope 和会话/函数清单留在本地，未重复存入仓库。`native/ginf.json`、`native/ginf-loader.json` 和 `native/wmilib.json` 是原生函数范围受限的证据副本，保留 source_evidence_id、源结果哈希、函数VA/assembly/pseudocode。`native/ginf-sections.json` 记录两个同版本、不同完整哈希 GInf 的 section 对照。`native/simple-pipe.md` 和 `native/simple-pipe.json` 补齐旧 MSIService 的管道创建/读写到 MSIWmiAcpi 的 Q/S 解析边界；完整字段和 provider→EC 映射保留未知。

## 工具与复现边界

本次实际使用 [morluto/rea](https://github.com/morluto/rea) 的 reverse-engineer-anything Skill v34、rea-agents 6.3.0，Skill 来自该版本包内的 `skills/reverse-engineer-anything/SKILL.md`。桌面会话未暴露预注册 REA MCP，研究客户端通过 stdio 直接连接该版本的 MCP server；这是实际 REA 工具调用而不是替代工具的改名。工具结果中的 provider 及 evidence ID 可核对这一点。

REA 使用 Node 24.21.0；托管解码 ILSpy/ICSharpCode.Decompiler 11.1.0.9782；原生分析 Ghidra 12.1.4 + Temurin 21.0.12.1。7-Zip 26.04、InnoExtract 1.9 用于容器提取。macOS arm64 Ghidra 原生 decompiler 从该版本随包源码构建，命令如下；仅执行分析器，不执行被分析文件：

```sh
make -j4 ghidra_opt ARCH_TYPE='-arch arm64' OSDIR=mac_arm_64 \
  ADDITIONAL_FLAGS='-w' BFDLIB= LNK=''
```

对 Windows 目标保持执行禁用。REA 调用顺序为 `open_binary(path, provider_id="ghidra")` → `analyze_function(procedure="0x...")` → `close_binary()`；托管元数据工具直接读取 path。只使用 static provider，不使用 debugger/execution、patch、驱动加载或硬件工具。复现需要相同样本 SHA、相同版本和 source-immutable/ephemeral analysis；工具的新版本或不同样本须建立新的证据身份。

下载、完整提取树、Ghidra 临时工程、完整 C# 反编译和工具本次位于 `/tmp/gs66-research`。这些临时路径可能被清理；仓库保留的片段/清单可以独立核对关键结论。官方下载链接会更新，重新下载后首先核对主报告的 SHA-256。仓库离线清单脚本与固定 Python 依赖位于 `scripts/`，解析不会加载 target CLR 或调用任何 Windows API。
