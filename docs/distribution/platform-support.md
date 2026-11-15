# SystemTools 3.0 平台能力表

由源码功能台账生成；实现接入不等于设备、权限或对应平台已经验收。运行时设置页的实际探测为准。

| 稳定 ID | 名称 | Windows x64 | Linux x64 X11 | macOS ARM64 |
| --- | --- | --- | --- | --- |
| SystemTools.ActionFlowExecutionConfirmation | 行动流执行确认 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.AdjustScreenBrightness | 调整屏幕亮度 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.AdvancedShutdown | 高级计时关机 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.AltF4 | 按下 Alt+F4 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.AltTab | 按下 Alt+Tab | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.AutoHideMainWindowWhenOccluded | 遮挡文字时隐藏主界面 | 保留实现；需设置与现场条件 | 不支持可靠排除自身捕获 | 原生接入；需权限/依赖/设备 |
| SystemTools.AutoOpenUsbDriveOnInsert | 自动播放 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.AutoSwitchClassIslandTheme | 自动切换 ClassIsland 主题 | 保留实现；需设置与现场条件 | 不支持可靠排除自身捕获 | 原生接入；需权限/依赖/设备 |
| SystemTools.BackgroundPlayAudio | 后台播放音频 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.BlackScreenHtml | 黑屏html | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.CameraCapture | 摄像头抓拍 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.CancelShutdown | 取消关机计划 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.ChangeWallpaper | 切换壁纸 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ClearAllNotifications | 清除全部提醒 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.ClickSimulation | 模拟点击 | 原未注册；不新增入口 | 原未注册；不新增入口 | 原未注册；不新增入口 |
| SystemTools.CloneDisplay | 复制屏幕 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.Copy | 复制 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.CtrlZ | 按下 Ctrl+Z | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.Delete | 删除 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.DisableDevice | 禁用硬件设备 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.DisableMouse | 禁用鼠标 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.EnableDevice | 启用硬件设备 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.EnableMouse | 启用鼠标 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.EnableVoiceWakeAi | 启用语音唤醒 AI | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.EnterKey | 按下 Enter 键 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.EscKey | 按下 Esc 键 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ExtendDisplay | 扩展屏幕 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ExternalDisplay | 仅第二屏幕 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 公共 API 不提供关闭单屏 |
| SystemTools.F11Key | 按下 F11 键 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.FullscreenClock | 沉浸式时钟 | 产品移除 | 产品移除 | 产品移除 |
| SystemTools.ImmediateRestart | 立即重启 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.ImmediateShutdown | 立即关机 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.InternalDisplay | 仅电脑屏幕 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 公共 API 不提供关闭单屏 |
| SystemTools.KillProcess | 退出进程 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.LoadTemporaryClassPlan | 加载临时课表 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.LockScreen | 锁定屏幕 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.Move | 移动 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.OpenAppSettings | 打开应用设置 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.OpenClassSwapWindow | 打开换课窗口 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.OpenProfileEditor | 打开档案编辑 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.RestartAsAdmin | 重启应用为管理员身份 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.ScreenShot | 屏幕截图 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.SetVolume | 设置系统音量 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ShowAiChatDialog | 显示AI对话框 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ShowDesktop | 显示桌面 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.ShowFloatingWindow | 显示悬浮窗 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ShowToast | 拉起自定义Windows通知 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.Shutdown | 计时关机 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.SimulateKeyboard | 模拟键盘 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.SimulateKeyCombination | 模拟组合键 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.SimulateMouse | 模拟鼠标 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.Sleep | 睡眠 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.SwitchFloatingWindowTheme | 切换悬浮窗主题 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.SwitchSystemAccentColor | 切换系统强调色 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.SwitchTheme | 切换主题色 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.ToggleFloatingWindowLayer | 切换悬浮窗层级 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ToggleFloatingWindowProfile | 切换悬浮窗配置方案 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ToggleWorkflow | 开关自动化 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.TriggerCustomTrigger | 触发指定触发器 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.TypeContent | 键入内容 | 保留实现；需设置与现场条件 | 未接入 | 未接入 |
| SystemTools.WakeUpVoiceConversationAi | 唤醒语音对话 AI | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.WindowOperation | 窗口操作 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.BetterCarouselContainer | 更好的轮播容器 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.ClipboardContent | 显示剪切板内容 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.LocalQuote | 本地一言 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.LyricsDisplay | 音乐软件歌词显示 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 未提供通用媒体会话 |
| SystemTools.NetworkStatus | 网络延迟检测 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.NextClassDisplay | 下节课是 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.ScrollingText |  LED 文本仿真显示框 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.ActionInProgressTrigger | 行动进行时 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.FloatingWindowTrigger | 从悬浮窗触发 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.HotkeyTrigger | 按下自定义热键时 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.KeywordTrigger | 关键词触发 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.LongIdleTrigger | 长时间未操作电脑时 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.MainWindowClickTrigger | 点击主界面时 | 保留实现；需设置与现场条件 | 主界面实际点击；穿透模式不触发 | 主界面实际点击；穿透模式不触发 |
| SystemTools.UsbDeviceTrigger | USB设备插入时 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 原生接入；需权限/依赖/设备 |
| SystemTools.ProcessRunningRule | 程序正在运行 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.UsingClassPlanRule | 正在使用某课程表 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.UsingTimeLayoutRule | 正在使用某时间表 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.InTimePeriodRule | 是否在某时间段 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |
| SystemTools.MediaMusicPlayingRule | 正在播放媒体音乐 | 保留实现；需设置与现场条件 | 原生接入；需权限/依赖/设备 | 未提供通用媒体会话 |
| SystemTools.ReadFile | 读取文件 | 保留实现；需设置与现场条件 | 共享实现；需设置与现场条件 | 共享实现；需设置与现场条件 |

系统认证：Windows 使用既有系统认证，Mac 使用本人 LocalAuthentication；Linux 不开放。Mac 使用原生系统玻璃/材质，旧系统回落，Linux 材质只提供可靠能力。

选配依赖：Vosk language model、FFmpeg、SAI、Linux desktop tools、m1ddc、ddcutil。模型、授权、外屏与背光设备均不随构建自动具备。

完整实体设备、Windows Hello、外部 AI 服务、中文识别质量、多屏/热插拔、Linux 桌面和正式签名升级仍分别追踪。
