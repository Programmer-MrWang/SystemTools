# SystemTools 项目接续状态

更新：2026-11-15，Asia/Shanghai。正式工作区：`C:\STWork-Windows\SystemTools`；宿主：`C:\STWork-Windows\ClassIsland`。本文件替代此前停留在 M3 收尾的状态，历史证据仍保留。

## 当前目标与结论

M0—M4 开发已完成。最终三端原生聚合包的 Mac 桌面复验已有通过记录；本轮 Windows 的构建、184 项公共检查、9 个输出跨路径一致、Windows native stage 和最终包三种隔离桌面场景均已通过。正常安装、异平台损坏和本机后端缺失均主动退出，退出码 0，无强制终止或超时。

完整平台与硬件验收仍未全部完成，`fullDesktopAndHardwareAcceptanceComplete=false`。当前下一阶段是按 [M5 执行顺序](docs/roadmap/2026-11-15-m5-plan.md) 推进；本轮没有开始实现 M5 或发布正式版本。

## 继续遵循的决定

- 唯一主开发仓库 SystemTools，分支 `codex/cross-platform`；旧 SystemTools-Cross-platform 仅参考。
- 正式身份、旧行动 ID、组件 GUID、资源 URI、设置、密文和未知字段保持兼容。沉浸式时钟继续移除，ClickSimulation 不新增入口。
- Windows x64；Linux x64 原生 X11；Mac 原生 ARM64。Linux 实机未验证，不把构建结果当成桌面通过。
- Windows 普通退出原有 `shutdown /a` 行为保留；测试不执行真实关机、重启或设备启停。
- 每批相关检查和自审后中文约定式提交并通过本机代理推送，不强推、不自动发布。当前可用代理为 127.0.0.1:7897，后续操作前按实际状态复核。

## 最新证据入口

- [Windows M4 收尾验收](docs/validation/2026-11-15-m4-windows.md) 和 [结构化结果](docs/validation/2026-11-15-m4-windows-results.json)。
- [M4 原有三端构建与 Mac 结果](docs/validation/2026-11-14-m4.md) 及 [逐批记录](docs/roadmap/2026-11-14-m4-progress.md)。
- 最终包 SHA-256：`693ad40a4e28df2250a1eeeb6df65181055ed88c7d7b141125ec119cbcbdc75c`；包代码 `3359948fe3c09dceb3f887f101c60c454bec8763`。
- 业务源码指纹：`02dd16669e1e6cdce24c82b219725dfbc1b8272aac1b5bf900574dfd3051e995`。本轮只补验收工具与文档，原包业务未改。
- 工具修复提交：`656104ba2f96fa51cc4b8732df23f44f4fe3ce87`，已推送；文档收尾以 Git HEAD 为准。Mac 宿主无障碍修复 `1d532ce6` 保留。
- 工具提交的三端原生 CI `43696195206` 已完成，Windows/Linux/Mac 及严格 aggregate 四项均成功；桌面证据继续绑定原最终包哈希。
- Windows 最终运行：`artifacts/m4/windows/runs/20261115-164708-normal`、`20261115-164719-foreign-corrupt`、`20261115-164728-missing-backend`。原先脚本强制停止的运行保留，不能记正常退出。
- 本机 Global.json 哈希维持 `adf5aef13c7072c92d685576e3468ffcfed7e83c03dd82023373291f6223b3be`，没有验收宿主/Worker 残留。

## 环境与原有工作树

精确 SDK 10.0.403 已安装在 `C:\STWork-Windows\.dotnet`，运行时 10.0.14。后续命令需设置进程 DOTNET_ROOT/PATH；系统默认 SDK 仍为原先的 10.0.302。不得放宽插件 global.json。

四个仓库使用 reftable 保留含 Windows 文件名禁用字符的历史引用。宿主旧 SourceLink 不识别该格式；本次普通 Debug 验证使用 `-p:EnableSourceControlManagerQueries=false`，未改宿主源码，失败和成功日志均保留。不是默认宿主构建已无条件通过。

原有 `.idea/.idea.SystemTools/.idea/workspace.xml` 修改和未跟踪的 `docs/reports/2026-11-15-m0-m4-progress-report.md` 均保留且未夹带提交。ClassIsland 仍只有 EdgeTtsSharp 子模块原有 dirty；旧参考仓库改动不清理。恢复初次失败目录 `C:\STWork` 保留，不能作为开发目录。

## 独立待验与后续顺序

Mac 最小化成功、真实热键/组合输入/权限撤销、USB 实物插拔、真实空闲阈值/锁屏睡眠唤醒、多屏与 Spaces、Linux X11 GNOME/KDE 桌面仍待对应环境。Windows Robocopy `/copyall` 的权限成功路径仍待具备对应权限；本账户原版对照同样失败，数据保留已验证。完整真实旧版升级、回退与卸载矩阵继续按 M7 追踪。

真实 SAI、带模型的语音/麦克风、摄像头/人脸、Windows Hello、硬件电源/显示和正式签名发行不能据本轮标为完成。Keychain/Secret Service 尚无原生凭据后端。

M5 按“能力/权限和资源约定 → 单次摄像头抓拍 → 语音听写 → 关键词/唤醒 → 对话/打断 → 系统认证 → 媒体/歌词 → 集成验收”逐批推进。每批需要对应平台成功路径和取消/拒绝/停止证据，再进入下一批。
