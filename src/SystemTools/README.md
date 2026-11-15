# SystemTools 3.0 候选版

本包使用正式 SystemTools 身份和 3.0.0.0 版本，渠道为 rc。安装前退出宿主，先预览和备份旧插件与配置。不能与旧跨平台预览同时启用。支持 ClassIsland Avalonia/Misha API 2.2 宿主线；实际 Mac 验收宿主显示 2.1.1.1，不代表覆盖全部官方稳定版。

随包 release/platform-support.md 和 release/capabilities.json 由功能台账生成，逐项说明 Windows x64、Linux x64 原生 X11、macOS Apple Silicon ARM64 的实现状态。条件接入仍受设置、系统权限、设备和依赖限制；构建通过不代表实体设备全部验收。

Mac 相机和麦克风由独立 SystemTools Companion.app 实际采集并回传，授权对象为 SystemTools 采集助手；插件断开即停止设备。宿主源码和应用不需修改。首次使用通过“相机、语音与媒体”页明确申请权限。候选使用 ad-hoc 签名，未完成 Developer ID、公证和正式升级授权稳定性验证。

离线语音需显式选择语言匹配的完整 Vosk 模型；FFmpeg、SAI、Linux 桌面工具、m1ddc/ddcutil 等按功能选配。Mac 模拟输入需辅助功能权限，背景与截图需录屏权限。Linux Wayland/XWayland 不提供同等入口；Linux 背景/OCR 和认证、Mac 通用媒体会话及关闭单屏等能力不伪装支持。

Windows 保留原功能和系统凭据保护；Mac/Linux 会话密钥只在内存使用，原有系统密文保持原样。旧版数据导入先预览冲突再选择，后续重启生效；升级快照和条件回退工具见源码 docs/distribution/install-upgrade-rollback.md。卸载保留配置和恢复资料。

依赖版本、漏洞公告查询、来源与许可证随包保存。NPOI 从固定 Apache 源码构建，未使用需要额外 OSMF 条款的官方二进制。沉浸式时钟保持移除；原未注册 ClickSimulation 不新增入口。正式签名、公证、跨端实体设备和明确发布批准不足时，发行工具阻止提升为 stable。本候选不自动发布到市场或创建公开 Release。
