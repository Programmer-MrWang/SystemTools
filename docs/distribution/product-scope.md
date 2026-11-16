# 已确认的平台支持范围

更新：2026-11-16。本文记录用户明确的产品取舍，优先于旧研究建议和历史待办；ClassIsland 源码与安装应用始终不改。

## 平台

Windows x64、macOS ARM64、Linux x64 原生 X11。Linux Wayland/XWayland 明确不支持，不列为后续开发或验收任务。

## Mac 明确不做

| 功能 | 稳定 ID / 对应入口 | 产品行为 |
| --- | --- | --- |
| 媒体状态与播放器适配 | SystemTools.MediaMusicPlayingRule | Mac 不提供，不创建可执行规则入口 |
| 歌词适配与歌词显示 | SystemTools.LyricsDisplay | Mac 不提供，包括逐播放器和本地 LRC 的适配路线 |
| 关闭指定显示器 | SystemTools.InternalDisplay、SystemTools.ExternalDisplay | Mac 不提供仅内屏/仅外屏或其他单屏关闭路线 |

以上三类属于明确不做的范围，从后续开发清单移除；旧研究中“存在技术路线”的记录不代表继续开发授权。来自其他平台的旧配置与 ID 保留，Mac 上相应功能不得执行或产生模拟成功。

现有 Mac 后端已返回 Unsupported：媒体使用 UnsupportedMediaSessions；仅内屏/仅外屏在 ProbeLayout 与 ApplyAsync 的前置检查中拒绝；相关规则、组件及行动不注册为可执行入口。镜像/扩展仍按已有两屏条件和恢复验收要求处理。相机、语音、后台播放音频及系统认证按各自既有能力处理。

## 尚未决定新增的两项

Mac 显示桌面和任意系统强调色目前仍不可用。用户本轮询问可行性，没有要求实现；研究结论见 ../research/2026-11-16-mac-desktop-accent-options.md。显示桌面可考虑用户确认的系统快捷键；任意 RGB 全局系统强调色未发现可交付的公开写入路线。固定色板 UI 自动化与选中文字高亮颜色不能当成任意系统强调色已实现。
