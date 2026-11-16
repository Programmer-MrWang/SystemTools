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
| 任意系统强调色 | SystemTools.SwitchSystemAccentColor | Mac 不提供全局任意 RGB 强调色；不增加系统色板替代入口 |

以上四类属于明确不做的范围，从后续开发清单移除；旧研究中“存在技术路线”的记录不代表继续开发授权。来自其他平台的旧配置与 ID 保留，Mac 上相应功能不得执行或产生模拟成功。

现有 Mac 后端已返回 Unsupported：媒体使用 UnsupportedMediaSessions；仅内屏/仅外屏在 ProbeLayout 与 ApplyAsync 的前置检查中拒绝；相关规则、组件及行动不注册为可执行入口。镜像/扩展仍按已有两屏条件和恢复验收要求处理。相机、语音、后台播放音频及系统认证按各自既有能力处理。

## Mac 显示桌面

用户已要求实现通过原生输入发送用户配置的系统快捷键。入口为 SystemTools.ShowDesktop；在“系统功能与权限”页选择修饰键和普通按键，保存到 macShowDesktopShortcut。默认留空，未配置或无辅助功能权限时拒绝执行；保存后无需重启。行动设置提供配置位置说明，发送后只报告“快捷键已发送”，不冒充系统画面已验收。

执行复用键盘互斥队列，原生桥检查辅助功能、受支持的键和已按下键；完整创建按下/逆序释放序列后派发，开始派发后完成释放。它是全局系统快捷键，不依赖前台普通窗口，不修改用户的系统快捷键。重复执行遵循 macOS 可能恢复窗口的行为。用户要求本轮先不测试，仅做编译和代码自审；真实桌面/Spaces/全屏/不同键盘与取消场景继续待验。历史 CI 包不包含本轮新增实现，不能借用其验收结论。

研究背景见 ../research/2026-11-16-mac-desktop-accent-options.md。
