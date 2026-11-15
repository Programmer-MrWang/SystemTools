# M5 Mac 独立采集助手验收

日期：2026-11-15，Asia/Shanghai。本文件保留 Mac 独立采集助手首次验收记录；后续同源原生包、本人认证、撤权恢复和真实互斥验收已完成，最新结果见 2026-11-15-m5-closeout.md。实现提交为 54a0deb。

## 本次完成的范围

Mac 相机、麦克风改为插件自带的 SystemTools Companion.app 实际采集，再通过本机 Unix socket 回传 PNG 和 16 kHz 单声道 PCM。识别、关键词、AI 对话、审批及原行动身份继续留在插件。宿主进程内的旧相机和麦克风入口明确拒绝执行；原生桥只负责通过 NSWorkspace 启动独立应用。

ClassIsland 源码、索引、HEAD、原 dirty 及已安装应用均未改动。恢复的宿主仓库仍保留历史 e7d8f874；本轮没有应用该补丁，也没有重置它。实际验收使用 /Applications/ClassIsland.Desktop.app，2.1.1.1-Misha-19bb60c，Info.plist 没有两项设备用途声明。该安装的官方发行来源尚未独立核实，不能把本次结果写成对所有官方发行版的验收。

## 可核查结果

| 检查 | 实际结果 |
| --- | --- |
| 本机工具链 | ARM64，.NET SDK 10.0.403，Xcode Swift 6.4.2 |
| 公共、加载、M4/M5、原生生命周期、打包、IPC 与验收工具回归 | 252 项通过 |
| IPC 专项 | 17 项通过，包含错误令牌、仅本人访问的会话文件、并发乱序回复、取消、迟到回复、大消息、断连与显式重连 |
| 独立应用设备专项 | 17 项通过；两张 1920×1080 PNG、实际 PCM、进行中取消、错误设备、停止与断连退出 |
| Mac 原生 Vosk | 官方英文模型和样本的已知内容识别通过；实际助手 PCM 也已送入解码器 |
| 未改动宿主正常场景 | 12 项通过，正式抓拍行动、实际 PNG 解码、正式识别服务加载模型、真实采音、取消释放 |
| 异平台模块损坏场景 | 同样 12 项通过；未加载损坏的 Windows/Linux 后端 |
| 本机后端缺失场景 | 明确报告插件加载失败，宿主主动退出 0 |
| 三个最终场景 | 全部主动退出 0，无超时、无强停；助手已退出；日常 Global.json 字节不变 |
| 跨路径可复现 | 13 个输出逐字节相同，含助手可执行文件、Info.plist、签名资源和原生桥 |
| 宿主不变 | 2 个仓库合计 1946 个受跟踪项及已安装应用 448 个文件与本轮前基线一致 |

系统设置中摄像头和麦克风列表均显示 SystemTools Companion 已开启；用户本人同意授权后完成真实设备测试。系统 TCC 日志也记录授权 subject 为 io.github.programmer-mrwang.systemtools.companion。此身份验证同时覆盖通过 open 启动的独立测试，以及插件在真实宿主内通过 NSWorkspace 启动的路径。

## 生命周期和本地连接

- 会话目录为短路径 /tmp/stc-随机值，权限 0700；会话文件和 socket 为 0600。随机会话令牌仅用于握手，诊断不会保存令牌，握手后移除凭据文件。
- 助手检查 socket 对端用户、会话文件归属和权限，限制操作、消息大小和并发数。图片由助手写入本次私有目录后通过连接返回，不接受宿主传来的任意输出路径。
- 取消、停止和释放按连接顺序处理；图片经校验后才提交到最终路径。正在执行的抓拍取消已实测没有残留任务或半成品。
- 断连、心跳超时和菜单栏停止会释放设备并退出。助手退出后后台查询不会自动重启它，用户可以从申请权限入口显式重新连接。
- Mac 入口先注册，能力在调用时检查，避免助手异步连接造成每次启动都漏注册相机行动。

## 包与证据

本轮产物为 artifacts/m5/mac-companion/SystemTools.M5.MacPreview.cipx，类型明确为 mac-development-preview。它使用本机新编译的公共/Mac 产物，异平台资产来自恢复包中的历史 M5 包；没有把它伪装成新的三平台同提交原生汇总。当前包哈希、源码指纹和最终三场景路径见同目录 2026-11-15-m5-macos-companion-results.json。

详细证据保存在 artifacts/m5/mac-companion/：native-devices.json、ipc-tests.json、native-devices.log、host-*/result.json、host-*/acceptance.json、host-unchanged.json、reproducible.log。公共回归在 artifacts/m5/checks.json，跨路径逐项哈希在 artifacts/m1/reproducible-build.json，官方英文样本识别在 artifacts/m5/vosk-file.json。原始照片和音频只保存在忽略的本地产物目录。

异平台损坏场景曾有一次验收进程在启动阶段超时，该失败记录保留；随后用同一候选包重跑成功。最终采用当前源码和包哈希对应的最近三场景，不覆盖或删除旧失败证据。

## 仍未完成，不能写成全部 M5 交付

以下为首次预览验收时的待办快照。新三端 CI/合包、本机撤权恢复、本人系统认证、60 秒真实采音与三轮互斥切换已在收尾记录中补齐；外放回声、物理拔出、其他系统设备和 M7 正式签名仍未实测。

- 本轮修改对应的新三平台同源码原生 CI、Windows/Linux 实机回归及正式聚合包尚未生成；历史三端通过仅适用于历史代码。
- 助手目前为本机 ad hoc 开发签名。Developer ID 签名、公证、跨机器安装和升级后的授权稳定性仍待验证；重新编译可能要求重新授权。
- Mac 设备物理拔出、系统撤权后再允许、外放回声、长期关键词/听写/对话互斥和真实 LocalAuthentication 本人成功/取消仍待进一步实测。已有合成互斥、打断和清理检查不能替代这些结果。
- 本次英文模型和官方样本不代表中文识别质量；实际商业或自有 AI 服务仍需要单独验证。
- Windows Hello 本人成功/取消，以及 Linux 真实设备、桌面媒体与朗读链路仍按平台清单跟踪。

## 本机复验命令

在 SystemTools 根目录使用项目锁定的 SDK：

```sh
dotnet build SystemTools.slnx -c Release
python3 -B eng/m1.py build
python3 -B eng/m5.py check
python3 -B eng/check_reproducible.py --working-tree
dotnet run --project tests/SystemTools.M5.Companion.Tests -c Release -- --native --devices --model artifacts/m5/mac-companion/vosk-model-small-en-us-0.15
dotnet build tests/SystemTools.M5.MacHostProbe -c Release
python3 -B eng/m5_macos.py smoke --package artifacts/m5/mac-companion/SystemTools.M5.MacPreview.cipx
python3 -B eng/m5_macos.py smoke --package artifacts/m5/mac-companion/SystemTools.M5.MacPreview.cipx --case foreign-corrupt
python3 -B eng/m5_macos.py smoke --package artifacts/m5/mac-companion/SystemTools.M5.MacPreview.cipx --case missing-backend
```

设备专项会申请权限并短暂打开相机与麦克风；系统授权由本人选择。三场景使用新建隔离数据目录和已安装的宿主，不编译或编辑宿主。复验应先准备对应模型与候选包，不能照搬旧工作记录中的 Windows 环境路径。
