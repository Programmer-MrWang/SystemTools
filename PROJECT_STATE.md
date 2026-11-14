# SystemTools 项目接续状态

更新：2026-11-14，Asia/Shanghai。当前 Mac 工作区：`/Library/root directory/My github project/stwork/SystemTools`；宿主源码：`/Library/root directory/My github project/ClassIsland`。

## 目标

M3 Mac 本机收尾已完成，M4 尚未开始。当前状态优先看 `docs/validation/2026-11-14-m3-macos.md`，后续按 `docs/roadmap/2026-11-14-m4-plan.md` 推进。旁边的 `SystemTools-Mac-Handoff-2026-11-14` 保留移机前历史、决定与 Windows 证据，不再用其“Mac M3 待验证”覆盖本轮结果。

## 用户确认的决定

- 主开发仓库仅 SystemTools，分支 `codex/cross-platform`；SystemTools-Cross-platform 仅参考。
- 正式身份沿用 SystemTools，Windows 原 ID、组件 GUID、资源 URI、配置、授权与密文保持兼容。
- 沉浸式时钟继续移除；ClickSimulation 原未注册，不新增入口。
- Windows 普通退出的 `shutdown /a` 行为保留。
- Windows x64；Linux x64 原生 X11，不做 Wayland/XWayland；Mac 仅原生 ARM64。
- Linux M1 按用户要求完成代码和构建检查，不要求真机测试；运行状态仍标记未验证。
- 分批开发、检查、中文约定式本地提交。没有推送或发布授权。

## 已完成与验证

- M1 Windows 构建与真实宿主加载；M2 Windows 业务接入；M3-0 至 M3-8 开发均已完成。
- Windows 交接 HEAD：`041aa9d`；原 M3 共 19 个中文约定式提交，Windows 包来源代码 `baef28bc4caf8b68b92996ba60a24f6f1df56d50`。
- Mac 收尾代码/工具提交：`e59beda`、`71a7051`、`cf8d1ba`。修复真实宿主 Notch 主题解析和共享 AI 退出取消，补齐独立验收入口；最终源码指纹 `39f24ad62d0c44a95095236e597c6ff5e722c37500d1b23b4d84c41f681a1639`。
- Mac ARM64 clean build 零警告零错误，140 项自动检查、9 个输出跨检出路径字节一致；最终包正常安装/异平台 DLL 损坏各 109 项业务和 3 项退出检查通过，后端缺失明确失败，三个宿主正常退出码 0。
- Mac 预览包：`artifacts/m3/mac/SystemTools.M3.Mac.cipx`，SHA-256 `c10181a3f020f0e0c1ca94266991d339d0d2d7103053436f9b76187d12991659`。它保留原 Windows/Linux 专属资产，不等于新版三端发行验收。
- M3：144 项检查、9 个产物跨检出路径字节一致；同一 CIPX 的正常安装、损坏异平台 DLL、本机后端缺失场景通过。
- Windows 真实宿主验证 8 个设置页、7 个组件、7 个触发器、5 项规则、3 套主题、旧 URI、文件行动、截图、Excel、DPAPI、导入页与回退、同组恢复快照及正常退出。成功场景退出码 0、清理失败 0。
- 预览包：`artifacts/m3/SystemTools.M3.cipx`，SHA-256 `981a57e717ce7498086e109a008247afaa5705cd4137d4d06fc9976e22cc9386`。
- 详情：[M3 提交与回退](docs/roadmap/2026-11-14-m3-progress.md)、[M3 验收](docs/validation/2026-11-14-m3.md)、[总路线图](docs/roadmap/2026-11-13-systemtools-unified-roadmap.md)。
- 原有未提交 IDE 修改 `.idea/.idea.SystemTools/.idea/workspace.xml` 保留；本状态文件原由交接任务新建，本轮更新为 Mac 收尾状态并纳入文档提交。
- 当前无已知仍在运行的验收宿主或后台开发任务；机器级 Global.json 哈希不变。

## 尚未完成或未验证

- Mac 额外人工截图/视觉品质复核因锁屏未完成；真实组件视觉树与按钮事件已有程序化验收。宿主默认目标与本机工作负载不匹配，本次使用明确记录的独立本机覆盖；不是正式发布配置。
- 本轮共享修复的 Windows 原生包回归、新版同源三端汇总与远端 CI 尚未完成；旧 Windows 包已验收结果保留，但不冒充本轮新包结果。
- 当前 Windows 账户缺审核权限，Robocopy `/copyall` 原版对照同样失败；已验证两端数据保留，具有对应权限时的成功复制仍未验收。
- 真实 SAI、模型/麦克风、摄像头/人脸、Windows Hello、硬件电源/显示/输入、正式旧版升级卸载与远端 CI 尚未全部验收。
- Keychain/Secret Service 只建立凭据契约和不可用保留边界，原生实现尚未完成。
- M4 尚未开始：音量/空闲 → 截图/设备 → 输入/窗口 → 壁纸。后续 M5/M6 按总路线图推进。

## 下一步

按 M4 详细计划先做 M4.0 原生接入/权限矩阵，再做 M4.1a 系统音量闭环，随后空闲→截图/设备→输入/窗口→壁纸。保留宿主 `1d532ce6` 的正式辅助功能修复，不套用旧补丁。每批检查并中文约定式本地提交，未授权推送或发布。历史交接校验只适用于移机快照，本轮新增提交后不应再要求源码匹配旧 HEAD。
