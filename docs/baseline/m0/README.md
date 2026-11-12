# M0：范围与兼容基线

2026-11-13。静态冻结已完成；Windows 原版实机回归待对应机器验收。

## 可恢复基线

- 原 Windows 源码：`10a39f9db130ecaf38158e7fad8808dc3416acc3`，恢复标记 `baseline/windows-2026-11-13`。
- 宿主：`3afce2714b6ec50d4bd2a3baac95a71d96ce3b9b`；原跨平台参考：`0efd2c1`。
- 实施分支：原仓库 `codex/cross-platform`。已有 IDE 工作区改动不纳入提交。
- 要恢复查阅原版，请在其他目录执行 `git worktree add --detach ../SystemTools-baseline baseline/windows-2026-11-13`。不在当前工作区执行 reset/clean。
- `source-hashes.json` 是原始字节 SHA-256，可用于 M2 检查移动前后内容。生成器只读取冻结 Git 树，不读取用户配置。

## 台账范围与边界

`windows-features.json` 和 `windows-checklist.md` 收录 62 行动、7 触发器、7 组件、5 规则、3 主题、2 授权器、6 设置页，以及 AI、SAI、Excel、托盘、扩展依赖、退出行为六类集成验收项。集成 ID 是台账索引，并非新增宿主注册 ID。设置来源、声明字段和默认表达式见 `settings-contracts.json`，共 70 个源码文件。

行为以冻结源码为准；提取器不能求值所有计算属性、条件分支及生成器默认值，因此保留源文件和行号。Windows 实机验收要逐条填写结果、机器、依赖版本和失败日志。源码扫描不能替代运行。

台账 dependencies 是直接语法证据，空列表不表示没有间接依赖；完整项目包版本以冻结的 SystemTools.csproj 为准。重点验收矩阵如下：

其中 `SystemTools.ClickSimulation` 仅发现行动特性声明，原版 Plugin.cs 没有对应注册调用；它是源码保留项，不计作已向用户开放的功能。M2 不应因为存在类就自行启用该入口。

| 依赖/集成 | 基线版本或资产 | 权限/失败与恢复验收 |
| --- | --- | --- |
| ClassIsland/Avalonia/WinRT | PluginSdk 2.1.1.1、宿主源码基线 | 宿主类型身份；禁止重复运行时；页面与资源 URI |
| WinForms/Win32/管理接口 | UseWindowsForms、CsWin32 0.3.298、System.Management 8.0.0 | 宿主是否提供 Desktop 共享框架；设备/管理员/拒绝权限；M1 不接入 |
| Windows SAPI/Vosk | System.Speech 8.0.0、原 win-x64 Worker/模型 | 麦克风授权、模型缺失、中断、设备切换、退出释放 |
| FFmpeg | 可选 ffmpeg.exe | 未安装/安装/损坏；仅相关功能受影响 |
| 人脸识别 | DlibDotNet 19.21.0.20220724、OpenCvSharp4 4.9.0.20240103、Extensions 4.5.3.20211225 | 相机拒绝/断开、模型、凭据保留、比对成功与失败 |
| Windows Hello | 原服务系统版本检查 | 可用/不可用/取消/认证失败；不可用不能删除原认证项 |
| SAI | SuperAutoIsland.Interface 0.3.9.2-dev.3 | SAI 装/未装；接口类型一致；可选性 |
| Excel | NPOI 2.7.2、SharpZipLib 1.4.2、RecyclableMemoryStream 3.0.1 | 输出打开、排版、文件权限、依赖缺失 |
| AI/悬浮窗/托盘/主题 | 原服务与配置台账 | 密钥/会话保留、审批、中断、窗口恢复与旧布局 |

跨平台总体目标均待 M2 之后逐功能实现。M1 唯一业务入口是诊断；不得把台账中的 Windows 功能默认显示为 Mac/Linux 可用。原版沉浸式时钟登记为基线项目，但按用户决定不迁移。普通 Windows 退出无条件 `shutdown /a` 保留，M1 诊断壳没有接入原版退出流程。

## 支持与受测环境

| 平台 | 首个目标 | 系统基线与验收状态 |
| --- | --- | --- |
| Windows | win-x64 | 原项目 Windows SDK 10.0.19041；建议验收 Windows 11 24H2 x64。SDK 目标不等于所有功能最低系统版本；Windows Hello 等另有约束。待实机 |
| Linux | linux-x64，原生 X11 会话 | 建议 Ubuntu 24.04 x64。排除 Wayland/XWayland；需要 XDG_SESSION_TYPE=x11、DISPLAY 非空、无 WAYLAND_DISPLAY。待实机 |
| macOS | osx-arm64，原生 Apple Silicon | M1 实测机器 macOS 26.6、.NET 10.0.403、Xcode 27.2；不承诺更低系统已通过，不支持 Intel/Rosetta |

宿主 API 使用 PluginSdk 2.1.1.1、清单 apiVersion 2.2.0.0；真实兼容对象是上述宿主提交。入口中不得重复分发宿主 ClassIsland、Avalonia、WinRT 或 Microsoft.Extensions 程序集。M1 纯托管平台模块不依赖其他平台 SDK，不引入 native；真正加入 native 时再验证架构、最低系统和签名。

## 合成兼容夹具

`tests/fixtures/legacy-windows` 所有值均为人工构造，包含 Main、行动组、组件记录、悬浮窗方案、AI 会话和认证记录。非完整宿主配置快照，不可导入日常配置；宿主外围容器结构在 M2 用实际序列化器另行验证。密文字段仅是带原前缀的无效占位字节；人脸字段不是生物特征。测试不得解密后将其改为空值。

M1 只验证夹具字节不被加载/保存流程改动，不宣称完成旧版迁移或原版序列化回归。M2/M3 验收需使用真实类型反序列化并回写副本，核对稳定 ID、未知字段、未支持平台字段和凭据仍然保留。

## M0 审查

已检查提取数量、分类内 ID 唯一性、JSON 可解析、源文件存在、原版生产源码未改动。Windows 冒烟/硬件验收是明确待办，不计入已通过。重建：`python3 eng/freeze-baseline.py`。
