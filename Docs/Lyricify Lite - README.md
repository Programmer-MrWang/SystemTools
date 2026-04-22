## 歌词显示组件 -  Lyricify Lite 适配帮助

[ [Lyricify-App 官方仓库](https://github.com/WXRIW/Lyricify-App/) ]

   本歌词显示组件仅能读取 Lyricify Lite 的桌面歌词模式。

   要正常使用组件，您必须按以下要求设置您的 Lyricify Lite：

1.设置为”桌面歌词“模式

![截图](https://livefile.xesimg.com/programme/python_assets/551117c41953cb1ec9475f292e6e3495.png)

2.”歌词"选项卡：禁用”中文翻译“

![截图](https://livefile.xesimg.com/programme/python_assets/b8ed7e26601435f1bdb1eb0b2c7a4a63.png)

3.”桌面歌词”选项卡：启用”隐藏翻译和音译“；禁用“双行模式”、“自动隐藏”、“阴影效果”；“歌词对齐”选择“居中”

![截图](https://livefile.xesimg.com/programme/python_assets/656d5894a52b8a344409afc55e2336c4.png)

![截图](https://livefile.xesimg.com/programme/python_assets/30da528a90873d45f9052510341c201c.png)

![截图](https://livefile.xesimg.com/programme/python_assets/39e4bb8fe6661359bdf1a61cedc5e6e2.png)

![截图](https://livefile.xesimg.com/programme/python_assets/7fa5df1ab173fa6cebe0d3080170371d.png)

![截图](https://livefile.xesimg.com/programme/python_assets/81227944686ef830b2c7f0779c4b7fb0.png)

**可选：**

1.”桌面歌词”选项卡：关闭“歌词切换动画”：支持显示相应动画但可能在切换时会有阴影

![截图](https://livefile.xesimg.com/programme/python_assets/13d30f7e32e57c5336aa324c27ca495a.png)

2.锁定桌面歌词：避免鼠标移入时黑色背景显现

![截图](https://livefile.xesimg.com/programme/python_assets/1ac31568c8016a06a9196217694fd597.png)

.

.

## Lyricify Lite 官方问题解决文档：[ [GitHub](https://github.com/WXRIW/Lyricify-App/blob/main/docs/Lyricify%20Lite/README.md) ]

App 支持列表

已知支持 Lyricify Lite 的 app 如下表所示：

| 名称 | SMTC 支持情况 | 曲目匹配 | 备注 |
| :-: | :-: | :-: | :-: |
| HyPlayer | 完美 | 完美 | - |
| LyricEase | 完美 | 完美 | - |
| Spotify | 完美 | 较好 | 建议 [Lyricify 4](https://apps.microsoft.com/store/detail/9P4WB75RHWCH?launch=true&mode=full) |
| Apple Music | 一般 | 一般 | 可优化时间轴 |
| Tidal | 完美 | 较好 | - |
| QQ 音乐 | 完美 | 较好 | [插件](https://github.com/apoint123/QQMusic-ID-Injector)实现匹配 |
| QQ 音乐 UWP | 一般 | 较好 | - |
| 网易云音乐 | 完美 | 完美 | 需额外[插件](https://github.com/apoint123/inflink-rs/) |
| 网易云音乐 UWP | 无时间轴信息 | 较好 | - |
| 酷狗音乐 | 无时间轴信息 | 较好 | - |
| 汽水音乐 | 完美 | 较好 | - |
| Media Player | 良好 | 一般 | 原 Groove Music |
| Foobar 2000 | 一般 | 一般 | 时间[插件](https://github.com/ungive/foo_mediacontrol)提供 |
| PotPlayer | 良好 | 较差 | - |
| AIMP | 一般 |  一般 | 需额外[插件](https://www.aimp.ru/?do=catalog&rec_id=1097) |
| Salt Player | 良好 |  一般 | - |
| LX Music | 良好 |  一般 | - |
| MusicPlayer2 | 良好 |  一般 | - |
| Google Chrome | 良好 | 一般 | 默认不启用 |
| Microsoft Edge | 良好 | 一般 | 默认不启用 |

**注意：**

.       .请确保相关 app 已更新至最新版本，部分旧版本可能并不支持 SMTC。Spotify 用户建议使用 Lyricify 4，而非 Lyricify Lite，查看[具体原因](#spotify-用户应使用-lyricify-4)。

.       .对于 SMTC 无时间轴信息的 app，Lyricify Lite 将使用内置定时器更新歌词进度，所以在手动修改播放进度后，歌词会发生前后错位。

.       .所有接入 SMTC 的应用均支持 Lyricify Lite，未在上表中列出的不代表不支持。

.

.

## 常见问题（逐应用）

### Spotify 用户应使用 Lyricify 4
对于 Spotify 用户，我们推荐使用 Lyricify 4，而不建议使用 Lyricify Lite。如果你仅使用 Lyricify Lite 的灵动词岛或桌面歌词功能配合 Spotify，仍然建议切换到 Lyricify 4。尽管它们在表面功能上相似，但在实际体验上，Lyricify 4 将为你提供更好的词库与功能支持。

.

### 使用 QQ 音乐时没有时间轴
更新 QQ 音乐至最新版，确保其版本号不低于 21.10.2962。并确保 SMTC 已启用（位置：设置->常规设置->通知->显示系统媒体传输控件）

.

### 无法检测到网易云音乐
网易云音乐Win32原版并不支持 SMTC。安装由 apoint123 重构的 [InfLink 插件](https://github.com/apoint123/inflink-rs/)。

在使用网易云音乐时，Lyricify Lite 歌词卡在曲目信息或第一句歌词，无法正常显示：安装由 apoint123 重构的 [InfLink 插件](https://github.com/apoint123/inflink-rs/)。

.

### 无法检测到酷狗音乐
更新酷狗音乐至最新版，并确保 SMTC 已启用（位置：设置->常规设置->播放->支持系统播放控件）

.

### 无法检测到 PotPlayer
更新 PotPlayer 至最新版，最低版本要求 240618。
