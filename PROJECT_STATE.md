# SystemTools 项目接续状态

更新：2026-11-15，Asia/Shanghai。正式开发：`C:\STWork-Windows\SystemTools`；宿主：`C:\STWork-Windows\ClassIsland`。本文件替代 M4 收尾状态，历史证据仍保留。

## 最新进展：2026-11-15 M5 开发收尾

本段优先于后文迁移前说明。正式开发目录为本文件所在 Mac SystemTools 仓库；后文 Windows 路径和旧候选状态均为历史资料。

用户要求 ClassIsland 源码始终不改。本轮仅开发自己的插件及包内 SystemTools Companion.app，宿主两仓库 1946 个受跟踪项、HEAD/原 dirty 及已安装应用 448 个文件均与接手基线一致。恢复出的 e7d8f874 只留作历史，不应用宿主声明补丁。

实现已提交并推送至 54a0debd9aa47ce1828504d285c52255912f5c64。三平台 CI 43724063356 的 Windows、Linux、Mac 与严格 aggregate 全部成功，检查分别为 211、207、252 项。业务源码指纹为 c40c13c50cfbc71148ba992195db0cb521deb93c4c6c5d7e7a96592b787d12d8。

最终同源原生包 artifacts/m5/closeout/final-native/SystemTools.M5.Native.cipx 的 SHA-256 为 9fccb8e3febc29bfb8a65ad6b77d9b29906cfd2d047fee3f2390357650e2113e；替代本轮 Mac 开发预览。已用实际 CI 包在未改动的 ClassIsland 2.1.1.1 上完成正常、异平台损坏、本机后端缺失三场景，均主动退出 0、助手退出，日常 Global.json 不变。正常和损坏场景各完成 12 项实际抓拍/采音/正式识别检查。

用户本人完成了 Mac 系统认证成功、人工取消拒绝、TCC 撤权和重新允许；恢复后真实 PNG/PCM 再次通过。后台真实采音/解码运行 60 秒及三轮前台互斥、取消恢复、停止释放也通过。13 个本机输出跨路径逐字节相同，Windows/Mac CI 可复现检查通过。

M5 开发、三端构建合包及本台 Mac 可完成的验收已收尾；完整跨平台实体设备验收仍为 false。无法在本台代替完成的部分：新包 Windows/Linux 实体设备及 Windows Hello 本人交互、物理拔出内置设备、真实外部 AI 端点/凭据、中文识别质量和外放回声。正式 Developer ID 签名、公证及升级授权稳定性属于 M7，本机无可用签名身份。

最终说明与机器结果见 docs/validation/2026-11-15-m5-closeout.md 和 -results.json。原有 IDE 修改、未跟踪 docs/reports/ 和迁移前 PROJECT_STATE.md 纠正继续保留，不夹入提交。不自动发布。

## 当前结论

M0—M5 开发已完成。本轮完成 M5 摄像头、语音/关键词/对话与打断、系统认证、媒体与歌词后端、权限/资源边界和集成工具。本台 Windows 可自动完成的构建/回归/真实宿主最终包验收已完成。完整平台与硬件验收仍为 false；Windows Hello 本人交互、外部 AI 服务以及 Mac/Linux 实机项目继续独立待验。

- 最终代码 `2f2ef3f7d0966fc2760578f5779d1cdf68cc3d3d`；分支 `codex/cross-platform`。
- 最终 M5 包 `artifacts/m5/final-native/SystemTools.M5.Native.cipx`；SHA-256 `214f91b4680067993aea061f0209aa4d6781f0be76afb6deec6b57cf5ead586b`。
- 业务源码指纹 `2e1170e3f5760132c5c2d3df80443c855249e7553cc56f1ff6b6b1fc3c364a32`。后续文档收尾提交不改变业务源码指纹，Git HEAD 以实际仓库为准。
- 验收退出策略修复 `7fcf5315b95e3029693d7aef82e3b77a5ebc8310`，另 2 项正反路径工具回归通过；不改变已验收包的业务源码指纹。
- 原生 CI 43703787629 四项全部成功；Mac/Windows 跨路径可复现通过，公共与 M5 合成检查 208 项通过。
- 最终包 Windows 正常、异平台损坏、本机后端缺失三场景均通过，主动退出 0，无超时、无强制终止、清理失败 0。前两场景分别完成 17 项 M5 宿主检查。

## 最新证据入口

- [M5 Windows 验收](docs/validation/2026-11-15-m5-windows.md)、[结果 JSON](docs/validation/2026-11-15-m5-windows-results.json)。
- [M5 逐批记录](docs/roadmap/2026-11-15-m5-progress.md)、[对应平台/本人清单](docs/validation/2026-11-15-m5-platform-checklist.md)。
- [M4 Windows 收尾](docs/validation/2026-11-15-m4-windows.md)、[原 Mac M4 验收](docs/validation/2026-11-14-m4.md)。M4 证据不能冒充新 M5 实机成功。

## 保持的决定

唯一主仓库 SystemTools；ClassIsland 为宿主，SystemTools-Cross-platform 仅参考。正式身份、旧行动 ID、组件 GUID、资源 URI、设置、密文和未知字段保持兼容。沉浸式时钟继续移除，ClickSimulation 不新增入口。

目标 Windows x64、Linux x64 原生 X11、Mac 原生 ARM64。Linux 系统认证及 Mac 通用媒体状态按计划不开放；不会为统一菜单伪造能力。Windows 继续保留 SAPI/原 Worker；Mac 使用 AVAudioEngine、Linux 使用 PulseAudio/FFmpeg，模型需用户显式选择。会话 API 密钥只驻留内存，未新增原生 Keychain/Secret Service 后端。

原普通退出 shutdown /a 行为保留；测试未执行真实关机、重启、设备启停。每批先相关检查和自审，再中文约定式提交并用本机代理推送，不强推、不自动发布。当前代理 127.0.0.1:7897，使用前复核。

## 环境及宿主限制

SDK 精确 10.0.403，位于 `C:\STWork-Windows\.dotnet`，运行时 10.0.14；不要放宽 global.json。进程环境见 `C:\STHandoff-Recovery-20261115\m4-env.ps1`。仓库用 reftable 保留历史特殊引用；宿主 SourceLink 8 不识别，验证构建继续用 `-p:EnableSourceControlManagerQueries=false`。

宿主本地提交 e7d8f874 补 Mac 相机/麦克风用途声明；上游推送 403，补丁保存在 eng/host-patches/classisland-m5-media-permissions.patch，应用需 --unidiff-zero。不得声称官方宿主已交付该改动。Windows 实测宿主哈希 `7f535c4c8df304bd29d8a38fc1704119fcd7aac3658551bfbe67275380839bdb`。

原 IDE 修改、未跟踪 docs/reports/2026-11-15-m0-m4-progress-report.md，以及 ClassIsland 的 EdgeTtsSharp 原 dirty 均保留。机器 Global.json 哈希不变。不要编辑原移机档案 repositories 快照，`C:\STWork` 失败恢复目录不是开发目录。

## 后续工作顺序

1. 按对应平台清单完成 Mac/Linux M5 实机和 Windows 本人/外部条件验收，记录成功、拒绝、取消、重复进入与退出证据；交付宿主权限声明。
2. 若继续 M6，依赖顺序为真实排除自身捕获 → OCR 避让 → 材质叠层/命中原型 → 显示器模式 → 内置背光/外屏 DDC → 平台专属设备功能。先界定每项真实支持环境，不用软件变暗替代硬件背光。
3. M7 再处理签名发行、真实旧版升级/回退/卸载矩阵；本次未发布。

M4 的 Mac 最小化、真实热键/组合输入/权限撤销、USB 插拔、空闲/锁屏睡眠唤醒、多屏/Spaces 和 Linux GNOME/KDE，Windows Robocopy /copyall 权限成功路径仍按原清单跟踪。
