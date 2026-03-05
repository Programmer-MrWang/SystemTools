# SecRandom（avalonia 分支）悬浮窗实现分析

目标仓库：`https://github.com/SECTL/SecRandom/tree/avalonia`

## 1) 窗口层（Avalonia Window）

SecRandom 单独定义了一个 `FloatingWindow` 作为悬浮窗，其 XAML 关键点：

- `Topmost="True"`：窗口置顶。
- `ShowActivated="False"`：显示时不主动抢焦点。
- `SystemDecorations="None"` + `ExtendClientAreaChromeHints="NoChrome"`：去系统边框/标题栏，形成无框悬浮效果。
- `Background="Transparent"` + 内层 `Border` 透明度绑定配置：支持半透明外观。
- `ShowInTaskbar="False"`：不出现在任务栏。
- `SizeToContent="WidthAndHeight"`：随内容自适应尺寸。

这些配置组合起来就是“轻量、无框、置顶、不抢焦点”的悬浮窗基础。

## 2) 生命周期与入口

在 `App.axaml.cs` 的应用初始化中，`FloatingWindow` 会被创建，并直接设为桌面生命周期的 `MainWindow`：

- `_floatingWindow = new FloatingWindow();`
- `desktop.MainWindow = _floatingWindow;`

这表示应用启动后，悬浮窗是主入口窗口；主界面与设置页是按需另开窗口（`ShowMainWindow` / `ShowSettingsWindow`）。

## 3) 交互实现（拖动 + 按钮）

`FloatingWindow.axaml.cs` 里通过指针事件实现拖动：

- `PointerPressed` 时，如果不是点到 `Button/CommandBarButton`，调用 `BeginMoveDrag(e)`。
- `PointerReleased` 时，将当前位置写回配置（`FloatPosition`），用于下次恢复位置。

同时为了避免拖动误触按钮，它会沿视觉树向上检查父级，只要命中按钮类就不进入拖拽逻辑。

## 4) 位置与外观状态恢复

`OnLoaded` 中会从配置读取坐标并设置：

- `Position = new PixelPoint(ViewModel.Config.FloatPosition.X, ViewModel.Config.FloatPosition.Y)`

并根据能力和配置切换透明效果：

- 支持亚克力且开启时：`TransparencyLevelHint = [AcrylicBlur]`
- 否则：`TransparencyLevelHint = [Transparent]`

这实现了“记住上次位置 + 可选亚克力背景”。

## 5) 悬浮窗内容组织

悬浮窗内容不是写死在 XAML，而是动态生成：

- 启动后 `RefreshItems()` 清空 `RootStackPanel`，先插入拖动条（`TouchDragThumb`）。
- 再根据 `FloatingWindowButtonControl`（配置中的按钮列表）逐个生成 `CommandBarButton`。
- 例如 `roll_call` 按钮点击后会打开主窗口并跳转对应导航项。

这让悬浮窗可通过设置页配置显示哪些功能入口。

## 6) 关闭策略

悬浮窗默认不可直接关闭：

- `Closing` 事件里若 `CanClose == false`，则 `e.Cancel = true`。
- 应用退出流程（`Stop`）会先将 `CanClose = true`，再走关停。

属于“托盘常驻/后台运行”常见模式：平时拦截关闭，只有真正退出时才允许销毁。

## 7) 托盘协作

应用定义了 Tray 菜单（`App.axaml` + `CreateTrayIconMenu`）：

- 可从托盘打开主窗口、打开设置、退出程序。
- 悬浮窗与托盘共同组成“常驻型工具”交互模型。

## 8) 实现结论（可复用思路）

SecRandom avalonia 分支的悬浮窗实现可以总结为：

1. **独立 `Window` + 无框透明置顶属性**（外观基础）。
2. **指针事件 + `BeginMoveDrag`**（拖动能力）。
3. **坐标持久化 + `OnLoaded` 恢复**（状态记忆）。
4. **动态按钮布局**（功能可配置）。
5. **关闭拦截 + 托盘退出**（后台常驻体验）。

如果你要在自己的 Avalonia 项目复刻，直接按这 5 步搭建即可。
