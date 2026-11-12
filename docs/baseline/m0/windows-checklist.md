# M0 Windows 功能验收台账

基线 `10a39f9db130ecaf38158e7fad8808dc3416acc3`。由 eng/freeze-baseline.py 生成。Windows 实机均待验收；字段与默认表达式见 settings-contracts.json。

| 分类 | 稳定 ID | 功能 | 源码 | 恢复/中断 | 验证 |
| --- | --- | --- | --- | --- | --- |
| action | `SystemTools.ActionFlowExecutionConfirmation` | 行动流执行确认 | Actions/ActionFlowExecutionConfirmationAction.cs:18 | False/True | 开关/重启；新增/保存配置；执行；运行中停止；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AdjustScreenBrightness` | 调整屏幕亮度 | Actions/AdjustScreenBrightnessAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AdvancedShutdown` | 高级计时关机 | Actions/AdvancedShutdownAction.cs:22 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AltF4` | 按下 Alt+F4 | Actions/AltF4Action.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AltTab` | 按下 Alt+Tab | Actions/AltTabAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AutoHideMainWindowWhenOccluded` | 遮挡文字时隐藏主界面 | Actions/AutoHideMainWindowWhenOccludedAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AutoOpenUsbDriveOnInsert` | 自动播放 | Actions/AutoOpenUsbDriveOnInsertAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.AutoSwitchClassIslandTheme` | 自动切换 ClassIsland 主题 | Actions/AutoSwitchClassIslandThemeAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.BackgroundPlayAudio` | 后台播放音频 | Actions/BackgroundPlayAudioAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.BlackScreenHtml` | 黑屏html | Actions/BlackScreenHtmlAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.CameraCapture` | 摄像头抓拍 | Actions/CameraCaptureAction.cs:13 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.CancelShutdown` | 取消关机计划 | Actions/CancelShutdownAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ChangeWallpaper` | 切换壁纸 | Actions/ChangeWallpaperAction.cs:18 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ClearAllNotifications` | 清除全部提醒 | Actions/ClearAllNotificationsAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ClickSimulation` | 模拟点击 | Actions/ClickSimulationAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.CloneDisplay` | 复制屏幕 | Actions/CloneDisplayAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.Copy` | 复制 | Actions/CopyAction.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.CtrlZ` | 按下 Ctrl+Z | Actions/CtrlZAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.Delete` | 删除 | Actions/DeleteAction.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.DisableDevice` | 禁用硬件设备 | Actions/DisableDeviceAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.DisableMouse` | 禁用鼠标 | Actions/DisableMouseAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.EnableDevice` | 启用硬件设备 | Actions/EnableDeviceAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.EnableMouse` | 启用鼠标 | Actions/EnableMouseAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.EnableVoiceWakeAi` | 启用语音唤醒 AI | Actions/EnableVoiceWakeAiAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.EnterKey` | 按下 Enter 键 | Actions/EnterKeyAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.EscKey` | 按下 Esc 键 | Actions/EscAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ExtendDisplay` | 扩展屏幕 | Actions/ExtendDisplayAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ExternalDisplay` | 仅第二屏幕 | Actions/ExternalDisplayAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.F11Key` | 按下 F11 键 | Actions/F11Action.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.FullscreenClock` | 沉浸式时钟 | Actions/FullscreenClockAction.cs:10 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ImmediateRestart` | 立即重启 | Actions/ImmediateRestartAction.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ImmediateShutdown` | 立即关机 | Actions/ImmediateShutdownAction.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.InternalDisplay` | 仅电脑屏幕 | Actions/InternalDisplayAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.KillProcess` | 退出进程 | Actions/KillProcessAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.LoadTemporaryClassPlan` | 加载临时课表 | Actions/LoadTemporaryClassPlanAction.cs:15 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.LockScreen` | 锁定屏幕 | Actions/LockScreenAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.Move` | 移动 | Actions/MoveAction.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.OpenAppSettings` | 打开应用设置 | Actions/OpenAppSettingsAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.OpenClassSwapWindow` | 打开换课窗口 | Actions/OpenClassSwapWindowAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.OpenProfileEditor` | 打开档案编辑 | Actions/OpenProfileEditorAction.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.RestartAsAdmin` | 重启应用为管理员身份 | Actions/RestartAsAdminAction.cs:13 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ScreenShot` | 屏幕截图 | Actions/ScreenShotAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SetVolume` | 设置系统音量 | Actions/SetVolume.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ShowAiChatDialog` | 显示AI对话框 | Actions/ShowAiChatDialogAction.cs:9 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ShowDesktop` | 显示桌面 | Actions/ShowDesktopAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ShowFloatingWindow` | 显示悬浮窗 | Actions/ShowFloatingWindowAction.cs:15 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ShowToast` | 拉起自定义Windows通知 | Actions/ShowToastAction.cs:11 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.Shutdown` | 计时关机 | Actions/ShutdownAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SimulateKeyCombination` | 模拟组合键 | Actions/SimulateKeyCombinationAction.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SimulateKeyboard` | 模拟键盘 | Actions/SimulateKeyboardAction.cs:15 | False/True | 开关/重启；新增/保存配置；执行；运行中停止；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SimulateMouse` | 模拟鼠标 | Actions/SimulateMouseAction.cs:17 | False/True | 开关/重启；新增/保存配置；执行；运行中停止；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.Sleep` | 睡眠 | Actions/SleepAction.cs:10 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SwitchFloatingWindowTheme` | 切换悬浮窗主题 | Actions/SwitchFloatingWindowThemeAction.cs:15 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SwitchSystemAccentColor` | 切换系统强调色 | Actions/SwitchSystemAccentColorAction.cs:15 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.SwitchTheme` | 切换主题色 | Actions/SwitchThemeAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ToggleFloatingWindowLayer` | 切换悬浮窗层级 | Actions/ToggleFloatingWindowLayerAction.cs:18 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ToggleFloatingWindowProfile` | 切换悬浮窗配置方案 | Actions/ToggleFloatingWindowProfileAction.cs:17 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.ToggleWorkflow` | 开关自动化 | Actions/ToggleWorkflowAction.cs:15 | True/False | 开关/重启；新增/保存配置；执行；恢复初值；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.TriggerCustomTrigger` | 触发指定触发器 | Actions/TriggerCustomTriggerAction.cs:13 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.TypeContent` | 键入内容 | Actions/TypeContentAction.cs:16 | False/True | 开关/重启；新增/保存配置；执行；运行中停止；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.WakeUpVoiceConversationAi` | 唤醒语音对话 AI | Actions/WakeUpVoiceConversationAiAction.cs:9 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| action | `SystemTools.WindowOperation` | 窗口操作 | Actions/WindowOperationAction.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `A7C3455E-6A4E-4D4D-9D0D-7C6FCB5E1E3A` | 更好的轮播容器 | Controls/Components/BetterCarouselContainerComponent.axaml.cs:22 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `E2A41B7D-9F36-4A08-8B8D-1BA29E570F62` | 显示剪切板内容 | Controls/Components/ClipboardContentComponent.axaml.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `5D2C0E65-8648-4A67-BBEA-3FA713B1CF8D` | 本地一言 | Controls/Components/LocalQuoteComponent.axaml.cs:23 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `A1B2C3D4-E5F6-7890-ABCD-EF1234567891` | 音乐软件歌词显示 | Controls/Components/LyricsDisplayComponent.axaml.cs:22 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `8F5E2D1C-3B4A-5678-9ABC-DEF012345678` | 网络延迟检测 | Controls/Components/NetworkStatusComponent.axaml.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `C3E56B6B-0E01-4F3C-8F7B-9264CA2B2143` | 下节课是 | Controls/Components/NextClassDisplayComponent.axaml.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| component | `A7B3E4D1-2F8C-4B9A-9E5D-6C1B2A3F4E5D` |  LED 文本仿真显示框 | Controls/Components/ScrollingTextComponent.axaml.cs:17 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| authorizer | `systemtools.authProviders.faceRecognition` | 人脸识别 | Controls/FaceRecognitionAuthorizer.axaml.cs:20 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| authorizer | `systemtools.authProviders.windowsHello` | Windows Hello 验证器 | Controls/WindowsHelloAuthorizer.axaml.cs:18 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.about` | 关于 | SettingsPage/AboutSettingsPage.axaml.cs:23 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.aiChat` | AI 对话 | SettingsPage/AiChatSettingsPage.axaml.cs:26 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.floating` | 悬浮窗编辑 | SettingsPage/FloatingWindowEditorSettingsPage.axaml.cs:32 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.more` | 更多功能选项… | SettingsPage/MoreFeaturesOptionsSettingsPage.axaml.cs:18 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.pluginDebug` | 插件调试 | SettingsPage/PluginDebugSettingsPage.axaml.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| settingsPage | `systemtools.settings.main` | 主设置 | SettingsPage/SystemToolsSettingsPage.axaml.cs:27 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.ActionInProgressTrigger` | 行动进行时 | Triggers/ActionInProgressTrigger.cs:14 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.FloatingWindowTrigger` | 从悬浮窗触发 | Triggers/FloatingWindowTrigger.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.HotkeyTrigger` | 按下自定义热键时 | Triggers/HotkeyTrigger.cs:9 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.KeywordTrigger` | 关键词触发 | Triggers/KeywordTrigger.cs:10 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.LongIdleTrigger` | 长时间未操作电脑时 | Triggers/LongIdleTrigger.cs:12 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.MainWindowClickTrigger` | 点击主界面时 | Triggers/MainWindowClickTrigger.cs:9 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| trigger | `SystemTools.UsbDeviceTrigger` | USB设备插入时 | Triggers/UsbDeviceTrigger.cs:15 | False/False | 开关/重启；新增/保存配置；执行；缺依赖/拒绝权限；退出重进；稳定 ID |
| rule | `SystemTools.ProcessRunningRule` | 程序正在运行 | Plugin.cs:496 | —/— | 启用/禁用；真假分支；保存重进；目标/依赖缺失 |
| rule | `SystemTools.UsingClassPlanRule` | 正在使用某课程表 | Plugin.cs:502 | —/— | 启用/禁用；真假分支；保存重进；目标/依赖缺失 |
| rule | `SystemTools.UsingTimeLayoutRule` | 正在使用某时间表 | Plugin.cs:508 | —/— | 启用/禁用；真假分支；保存重进；目标/依赖缺失 |
| rule | `SystemTools.InTimePeriodRule` | 是否在某时间段 | Plugin.cs:514 | —/— | 启用/禁用；真假分支；保存重进；目标/依赖缺失 |
| rule | `SystemTools.MediaMusicPlayingRule` | 正在播放媒体音乐 | Plugin.cs:520 | —/— | 启用/禁用；真假分支；保存重进；目标/依赖缺失 |
| theme | `Card-type-component` | Card-type Component | Plugin.cs:77 | —/— | 列表/启用；横竖布局；重启；旧 avares URI；截图对照 |
| theme | `classwidgets` | ClassWidgets 2 Style | Plugin.cs:89 | —/— | 列表/启用；横竖布局；重启；旧 avares URI；截图对照 |
| theme | `notch-style` | Notch Style | Plugin.cs:101 | —/— | 列表/启用；横竖布局；重启；旧 avares URI；截图对照 |
| integration | `SystemTools.AI` | AI 对话/课程档案/行动工具/语音 | Services/AiChatWindowService.cs:1 | —/— | 密钥保留；会话保存；中断/审批；语音争用；网络失败 |
| integration | `SystemTools.SAI` | SAI/Blockly 接入 | Plugin.cs:1 | —/— | SAI 已装/未装；接口身份；能力目录 |
| integration | `SystemTools.Excel` | 课程表 Excel 导出 | Services/ClassIslandProfileExcelExporter.cs:1 | —/— | 导出/打开；排版；NPOI 缺失 |
| integration | `SystemTools.Tray` | 托盘/快捷入口 | Plugin.cs:1 | —/— | 菜单；窗口；重复启动；退出清理 |
| integration | `SystemTools.OptionalDependencies` | 扩展下载/FFmpeg/人脸/Vosk | Shared/DependencyPaths.cs:1 | —/— | 空目录/模型缺失/损坏/下载失败 |
| integration | `SystemTools.Exit` | 普通退出取消关机计划 | Plugin.cs:1 | —/— | Windows 普通退出无条件 shutdown /a，按用户要求保持 |
