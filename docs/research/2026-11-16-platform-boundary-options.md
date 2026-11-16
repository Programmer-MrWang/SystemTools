# 当前平台边界与可选实现路线

日期：2026-11-16。本文区分“当前未实现/未验收”“依赖特定环境”“需要私有接口”，不将它们统一描述为操作系统绝对无法实现。ClassIsland 始终不改，扩展只在 SystemTools 及自己的辅助程序内进行。

## 固定范围

用户明确 Linux 只支持 x64 原生 X11，Wayland 与 XWayland 均不支持，不列作后续待开发项。现有 PlatformSelection 已要求 XDG_SESSION_TYPE=x11、有 DISPLAY、无 WAYLAND_DISPLAY；这次无需为此改动后端选择代码。

## Mac 媒体状态与歌词

可以开发，但需分级承诺。MPNowPlayingInfoCenter 面向应用自身发布正在播放的信息，不是公开的全系统读取接口。可按播放器分别使用其公开脚本字典或服务接口读取标题、进度和播放状态；对支持脚本的 Music、Spotify 等做适配。此路线需要逐播放器验证与自动化授权，不能覆盖所有播放器或网页。

通用“系统正在播放”可研究 MediaRemote 私有框架。开源 mediaremote-adapter 已提供实际实现，并记录 macOS 15.4+ 对调用环境的变化和额外适配。它证明技术上存在办法，同时也证明私有接口会随系统版本改变；不能将其当稳定公共 API。

歌词还需要独立来源。可沿用本地 LRC 加真实播放器进度，或适配播放器提供的歌词来源；不得凭曲名制造歌词。若需要迁移原版“捕获第三方歌词窗口”的行为，可使用 ScreenCaptureKit 指定窗口截图再裁切，要求真实歌词窗口存在，受内容保护、隐藏/最小化和目标应用行为影响。没有歌词或进度时应清空/明确不可用。

## Mac 关闭指定显示器

存在实现办法，但现有 CoreGraphics 公共显示配置接口不能直接提供我们已验证的可靠单屏关闭路径。公共镜像、黑色覆盖层与硬件背光降低也不等同于从桌面拓扑断开某一屏。

Lunar、BetterDisplay 等产品的断开/黑屏功能证明并非绝对不可能；实现路线可能涉及私有显示服务、虚拟/镜像方案或具体外屏的 DDC 电源控制。必须逐路线审计实际接口、系统版本与恢复能力。可以开展“高级实验功能”，但没有验证前保持当前入口不可用；不能用私有接口存在推断跨版本稳定，不能将覆盖黑屏当单屏关闭。

## Linux X11 背景捕获与 OCR

可研究并实现，当前“不可用”是缺少已交付可靠后端。X Composite 扩展可读取窗口的离屏 pixmap，包括部分被遮挡区域；可以枚举窗口树与堆叠顺序，排除宿主/助手，合成目标区域的背景，再交给 Tesseract 等 OCR。XDamage 可减少重复采样。

难点在 GNOME/KDE 不同窗口管理器、窗口装饰、ARGB 透明、阴影、缩放、全屏绕过合成和窗口生命周期。需要组合真实画面并证明排除自己，不能直接把包含 ClassIsland 的桌面截图当背景。可先限制为明确支持的 X11 合成环境；不涉及 Wayland，也无需为 Wayland 增加入口。

## Linux 系统身份验证

可以做，当前是安全设计与实测范围的选择。PAM 提供 pam_start、pam_authenticate、pam_acct_mgmt 等公开接口，可针对当前用户设计专用 PAM 服务；fprintd 提供公开 D-Bus 指纹验证接口，依赖真实设备、已登记指纹和系统策略。polkit 更适合授权某个具体系统操作，不能把任意 polkit 放行直接当通用身份验证成功。

需要明确“仅指纹”与“允许密码回退”的契约，验证当前用户、会话归属、取消、重试、迟到回调及辅助程序权限。若需要系统配置或特权助手，使用单一职责辅助程序，不能把宿主和插件整体以 root 运行。凭据不进日志、聊天或插件配置，失败不得降级放行。

## 建议顺序

Linux X11 背景/OCR → Mac 逐播放器状态及本地 LRC → Linux PAM/fprintd 认证 → Mac 单屏关闭实验。优先选择能用公开接口、能给出明确支持范围的路线；Mac 全局私有媒体与显示接口作为独立可选实验，不影响当前三端范围收尾。

## 主要一手资料

- Apple MPNowPlayingInfoCenter： https://developer.apple.com/documentation/mediaplayer/mpnowplayinginfocenter
- Apple ScreenCaptureKit： https://developer.apple.com/documentation/screencapturekit
- mediaremote-adapter 作者说明与限制： https://github.com/ungive/mediaremote-adapter
- Lunar 作者的功能与版本限制： https://lunar.fyi/
- X.Org Composite 协议： https://www.x.org/releases/current/doc/compositeproto/compositeproto.txt
- Linux-PAM 上游文档： https://github.com/linux-pam/linux-pam/tree/master/doc
- fprintd Device D-Bus API： https://fprint.freedesktop.org/fprintd-dev/Device.html
- polkit 上游文档： https://www.freedesktop.org/software/polkit/docs/latest/
