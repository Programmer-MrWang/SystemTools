> 前期平台功能调研参考；本仓库 codex/cross-platform 分支为后续唯一改造主线。

**SystemTools 功能迁移可行性调研（2026-11-13）**

范围：对照本地 Windows 原版、跨平台版及 ClassIsland 宿主源码，评估 Windows、Linux X11、macOS Apple Silicon。Linux 不考虑 Wayland/XWayland；macOS 不考虑 Intel/Rosetta。本文是设计评估，没有实现新功能或运行涉及授权、设备、电源的原型。

标记：**可**＝存在明确实施路径；**条件**＝须探测硬件、权限、桌面环境、第三方后端或宿主支持；**不建议**＝本轮不承诺相同语义的稳定实现，应隐藏入口。表格不表示已开发或已通过运行验证。下列 S 编号对应文末已经读取的官方/上游资料。

**一、可以优先迁移的通用能力**

| 功能 | Windows | Linux X11 | macOS M | 实现方案与宿主对照 |
| --- | --- | --- | --- | --- |
| A1 课表、时间表、科目导出 Excel | 可 | 可 | 可 | 原版已有 NPOI 实现，生成 xls/xlsx、多文件的主要逻辑可复用。迁移时从 IProfileService 的当前内存档案建立快照，保存走宿主文件选取器；不要照搬从插件目录反推 Profiles 路径并只读磁盘的做法。字体、列宽和样式做三端验证。无需安装 Excel。[S1] |
| A2 SuperAutoIsland 积木接入 | 条件 | 条件 | 条件 | 接口和积木元数据部分可移植；需要 SAI 已加载且支持当前平台。使用跨平台版行动/规则 ID。动态下拉和积木清单按能力过滤，原版 WMI 设备列表只在 Windows 提供。 |
| A3 SAI 读取文件数据积木 | 可，需 SAI | 可，需 SAI | 可，需 SAI | 原版就是 File.ReadAllTextAsync。可直接复用逻辑，补充编码、大小、错误处理并使用独立 ID；避免把它误认为 Windows 原生功能。 |
| A4 黑屏遮罩 | 可 | 可 | 可 | 建议改用 Avalonia 全屏黑色 Window，覆盖目标屏幕，提供退出动作；不依赖 black.html、浏览器全屏或键盘模拟。它是遮罩，不是关闭显示器、不是锁屏，受系统安全界面和全屏层级约束。 |
| A5 点击主界面触发 | 可 | 可 | 可 | 宿主窗口接收输入时，可订阅 AppBase.MainWindow 的 PointerPressed，正确处理子控件和卸载。**宿主普通模式会设置鼠标穿透**，因此完整原版语义须走下一行。 |
| A6 鼠标穿透时点击主界面区域触发 | 条件 | 条件 | 条件 | Windows 沿用低级鼠标钩子；X11 使用 XI2 原始按钮事件并做坐标命中；macOS 可选全局鼠标监视/事件 tap，按实际权限探测。要验证被遮挡区域、缩放和触控；不能声称普通 PointerPressed 已覆盖穿透场景。 |
| A7 悬浮窗按钮/行拖拽排序 | 可 | 可 | 可 | 复用 Avalonia 拖放、指针捕获，拖放完成写现有 FloatingWindowProfile；无需 Windows 全局鼠标钩子。 |
| A8 悬浮窗自动重检层级 | 可 | 条件 | 条件 | 可复用宿主前台窗口变化事件，结合窗口可见性/屏幕变化，不必保留 WinEvent 固定方案。Linux 仍由 WM 策略决定；macOS 宿主 Bottommost 分支为空，不能恢复出真正置底。 |

**二、值得分平台实现的桌面操作**

| 功能 | Windows | Linux X11 | macOS M | 推荐后端与限制 |
| --- | --- | --- | --- | --- |
| B1 自定义全局热键触发 | 可 | 可 | 条件 | Windows RegisterHotKey；X11 XGrabKey；macOS 优先注册式系统热键，必要时采用经授权的事件监听后端。需检测系统保留组合、冲突、键盘布局和功能键。保存跨平台键与修饰键，不把 Win32 VK 值当三端通用键码。[S4、S5] |
| B2 长时间无操作触发/恢复 | 可 | 可 | 可 | Windows GetLastInputInfo；X11 XScreenSaverQueryInfo；macOS CGEventSourceSecondsSinceLastEventType。统一阈值、触发一次和恢复状态机，区分系统睡眠与用户空闲。[S7] |
| B3 模拟键盘、组合键 | 可，有 UIPI 限制 | 可，有 XTEST 扩展条件 | 条件，辅助功能授权 | Windows SendInput；X11 XTEST；macOS CGEvent。高权限/安全输入目标可能拒绝输入，应按结果反馈，不绕过系统输入保护。[S2、S3、S5] |
| B4 模拟鼠标、移动、点击、滚轮 | 可 | 条件 | 条件，相关授权 | Windows SendInput；X11 XTEST；macOS Quartz。三端坐标、Retina、屏幕原点和多屏映射独立实现；宿主 macOS GetMousePos 当前为零值占位，不能直接复用它。[S2、S3、S5] |
| B5 键入中文/Unicode 文本 | 可 | 条件 | 条件 | Windows Unicode 输入；macOS Quartz Unicode 事件；X11 可采用键盘映射或经过配置的剪贴板粘贴方案。剪贴板方案需处理所有权和内容变化，不假定所有控件都接受粘贴；输入法/安全文本框做运行验证。[S2、S3、S5] |
| B6 六个常用按键行动 | 可 | 可/条件 | 分开处理 | Enter/Esc 可保留字面输入；Ctrl+Z 对应“撤销”时 macOS 通常应使用 Command+Z。Alt+F4 不能直接等价到 Command+Q；关闭窗口、退出应用、切换应用、窗口切换、切换全屏需要分开命名。F11 在 Mac 也不能承诺等于应用全屏。[S2、S5、S6] |
| B7 前台窗口最大化/最小化/还原/关闭 | 可 | 条件，WM 支持 | 条件，AX 权限与目标应用支持 | Windows ShowWindow/WM_CLOSE；X11 EWMH/ICCCM 请求；macOS Accessibility 的窗口属性/动作。Mac 的 zoom、铺满和真正全屏不是同一状态，应逐子操作检测是否可用。[S6、S34] |
| B8 显示桌面 | 可 | 条件 | 条件/暂缓 | X11 检测 WM 是否支持 _NET_SHOWING_DESKTOP；macOS 未核实到与原版相同语义的统一稳定公开接口。可研究用户配置的系统快捷方式，但不能把模拟某个固定按键当作三端可靠实现。[S34] |
| B9 屏幕截图、选区/窗口截图 | 可 | 可/条件 | 条件，屏幕录制授权 | Windows 原生屏幕捕获；X11 屏幕/窗口捕获并由插件做选区；macOS ScreenCaptureKit/SCScreenshotManager。区分截图目标、输出路径、隐藏插件窗口和隐私保护。Mac 过滤器支持排除本应用；不同系统受保护内容可能不可截取。[S13] |
| B10 USB 设备插入触发 | 可 | 条件，libudev | 条件，IOKit | 原版同时有任意 USB 和仅 USB 存储两种语义。Linux 用 libudev，Mac 用 IOKit 匹配通知；别用“任意卷挂载”替代“USB 物理插入”。[S8、S33] |
| B11 U 盘就绪后自动打开 | 可 | 条件，UDisks2/挂载后端 | 可/条件，卷就绪 | Windows 卷事件；Linux 关联 UDisks2 的 ConnectionBus=usb 和挂载点；macOS 关联卷挂载通知与设备来源。等待真正挂载就绪、去重后走宿主 PlatformServices.LauncherService，不运行 U 盘自带程序。[S8、S9] |
| B12 设置系统音量 | 可 | 条件，音频服务与端点 | 条件，硬件端点可写 | Windows Core Audio IAudioEndpointVolume；Linux 可用 PulseAudio 协议后端（含提供兼容服务的 PipeWire），或另写 PipeWire 后端；macOS Core Audio。HDMI、数字输出等可能无可写音量，按端点隐藏不支持操作。**宿主 IAudioService 的播放器音量不是系统音量。**[S10、S11、S12] |
| B13 切换壁纸、图片/纯色/填充 | 可 | 条件，DE/Portal | 可，按屏幕 | Windows 用原生壁纸接口；X11 的壁纸管理属于桌面环境，可做 GNOME/KDE/XFCE/Portal 后端；macOS NSWorkspace 按 NSScreen 设置。纯色可生成图片统一实现，原版 Windows 六种填充模式不能在其他端全部照搬。[S27、S28] |

**三、显示器、亮度和主题：应拆分子能力**

| 功能 | Windows | Linux X11 | macOS M | 实现与显示策略 |
| --- | --- | --- | --- | --- |
| C1 复制屏幕 | 条件 | 条件 | 条件 | Windows SetDisplayConfig；X11 RandR 同位置/兼容模式；macOS Core Graphics 镜像配置。至少两屏、支持的镜像模式及实际设备拓扑通过检测后显示。[S22、S23、S24] |
| C2 扩展屏幕 | 条件 | 条件 | 条件 | 同一套原生后端配置各屏位置、模式、镜像关系。不同 M 芯片和机型的物理外屏上限不能由插件突破。[S22、S23、S24] |
| C3 仅内置屏幕 | 条件，有内置屏 | 条件，RandR 输出可切换 | 不承诺统一版 | Windows 明确拓扑；X11 选择内置连接器并关闭其他输出。不能把 Mac 关闭镜像当成关闭其他显示器；没有经过验证的安全公开后端则隐藏。[S22、S24] |
| C4 仅外接/第二屏幕 | 条件，有目标屏 | 条件 | 不承诺统一版 | X11 开关指定输出时必须保留恢复途径，避免全关。Mac 的合盖模式有系统/硬件约束，不应靠插件宣称可任意关闭内置屏；第三方后端可另作明确的可选适配。[S22、S24] |
| C5 内置屏真实亮度 | 条件，WMI/驱动 | 条件，backlight 驱动/权限 | 默认隐藏，待验证后端 | Linux 使用 /sys/class/backlight 或受控的桌面/授权服务；Windows 原版 WMI 路径可复用。未确认适用于所有 M 系列内置屏的稳定公开统一写亮度接口；不建议先绑定未公开 DisplayServices/CoreDisplay 符号。[S25] |
| C6 外接屏真实亮度 | 条件，DDC/CI | 条件，DDC/I²C 权限 | 条件，外部适配器 | Windows DDC/CI，Linux ddcutil 等；Mac 可选接入 m1ddc/其他已有后端，但取决于显示器、端口、线缆及后端自身限制，不能承诺所有 M 机型。软件黑色覆盖层只是变暗，不等于背光调整。[S26] |
| C7 系统浅色/深色切换 | 条件 | 条件，DE 后端 | 条件，自动化授权 | 原版 Windows 实际修改主题注册表，不是修改 ClassIsland 主题。Linux 为 GNOME/KDE 等分别适配；Mac 可使用本机 System Events 已公开的 appearance preferences/dark mode 脚本字典，需按系统自动化权限验证。没有通用后端的 DE 隐藏操作。 |
| C8 系统强调色写入 | 条件，建议 Windows 实验项 | 条件，特定 DE | 不建议默认提供 | Windows 原版没有完整的三端通用契约。Mac 未确认稳定公开的任意全局强调色写入方案，不应先用私有偏好键承诺支持。可保留为特定 OS/DE 的独立能力，ClassIsland 自身强调色则是另一回事。 |
| C9 从壁纸取色并同步 ClassIsland 强调色 | 可 | 条件，读取当前壁纸后端 | 可/条件，当前桌面图片可读 | 主色提取算法可三端共用；Win 读系统壁纸、Mac NSWorkspace、Linux 读 DE 配置。动态壁纸/多屏需要选源策略。读取不到壁纸时可另提供“从选定图片取色”，不冒充自动跟随。[S28] |
| C10 根据主界面背后亮度自动变换 ClassIsland 明暗 | 可，需捕获后端 | 条件，需验证“背后”画面 | 条件，屏幕录制授权 | Windows 原实现有排除本应用截图；Mac ScreenCaptureKit 可排除本应用；X11 截 root 会截到插件自己，需要窗口组合/合成器适配验证，不能直接称为等价实现。主题变更优先走宿主主题/设置服务并保存正确配置。[S13] |
| C11 遮挡文字时隐藏主界面 | 条件 | 条件，捕获+OCR | 条件，ScreenCaptureKit+Vision | 同 C10 的真正背景捕获要求；Windows 用原 OCR，Linux 可选 Tesseract 等本地 OCR，Mac Vision 原生识别。需置信阈值、延迟/防抖、恢复检测，避免隐藏后再也不恢复。[S13、S20] |

**四、语音、摄像头、认证、媒体和外观**

| 功能 | Windows | Linux X11 | macOS M | 推荐实现及边界 |
| --- | --- | --- | --- | --- |
| D1 摄像头抓拍 | 条件，有设备 | 条件，V4L2/设备权限 | 条件，摄像头授权 | 原版把抓拍纳入 FFmpeg 扩展；可继续为三端 FFmpeg 分别选 dshow/v4l2/avfoundation，或换成 Win Media Foundation、Linux V4L2、Mac AVCapturePhotoOutput。原版 OpenCvSharp.runtime.win 不能作为三端依赖复制。[S14、S31] |
| D2 关键词触发、语音唤醒 | 条件 | 条件 | 条件 | Windows 可保留 SAPI；三端可增加 Vosk/专门唤醒后端，共享麦克风所有权与唤醒状态机。Vosk 上游列出 OSX M1 支持，但仍需核对 C# 绑定/arm64 二进制；现有 win-x64 Worker 必须重建，不能因为上游支持 Mac 就直接装包运行。[S15、S32] |
| D3 语音输入/听写 | 条件 | 条件 | 条件，原生或离线 | 可选 Vosk/whisper.cpp；M 系列优先考虑 whisper.cpp 的 Metal/Core ML，以及 macOS 26 SpeechAnalyzer、兼容模式 SFSpeechRecognizer。需要逐语言确认模型和离线能力，不能把支持某个框架等同于中文离线识别必然可用。[S15、S16、S17、S18] |
| D4 唤醒后 AI 语音对话 | 条件 | 条件 | 条件 | 复用已有 AI、工具审批、会话和宿主 TTS，新增采音、VAD、识别、回声/打断和所有权管理。语音只改变输入渠道，不绕过已有执行/档案修改确认。 |
| D5 系统身份验证 | Windows Hello，按设备/策略 | 暂缓，PAM/fprintd 需专门设计 | Touch ID/系统认证，按实际可用性 | macOS 使用 LAContext.canEvaluatePolicy/evaluatePolicy；M 芯片不意味着设备具备可用 Touch ID，用户未注册或没有合适硬件应隐藏生物识别选项。可独立设计系统密码回退，不能偷偷把“必须指纹”改成“允许密码”。接入宿主 AuthorizeProviderControlBase。[S19] |
| D6 自定义人脸识别验证 | 条件，原后端 | 条件，重建模型/相机后端 | 条件，但优先系统认证 | Dlib/OpenCV 当前依赖含 Windows native 包。可改跨平台推理和相机后端，但模型特征、阈值、凭据格式与活体能力需重新验证。Vision 人脸检测不是人物身份验证，也不能代替 Face ID/Touch ID。[S19] |
| D7 正在播放媒体音乐规则 | 可，媒体会话 API | 条件，播放器提供 MPRIS | 默认不提供通用版 | Linux 可读 MPRIS PlaybackStatus。Apple MPNowPlayingInfoCenter 用来发布本应用播放信息，不能据此宣称已能读取全系统所有播放器；Mac 可做 Music/Spotify 等明确播放器适配，不能把“系统有声音”替代“媒体音乐正在播放”。[S29、S30] |
| D8 歌词显示组件 | 原方案可迁，限目标软件 | 条件，歌词来源/窗口捕获 | 条件，歌词来源/窗口捕获 | 原组件是 FindWindow+PrintWindow+裁切第三方歌词窗口，非文本歌词协议。Windows 可保留；Linux XComposite/Mac ScreenCaptureKit 可以研究窗口画面捕获，但目标播放器和歌词窗口必须存在。更稳的独立新后端是本地 LRC/播放器协议；MPRIS 只有常规元数据接口，不保证提供逐句歌词。[S13、S29] |
| D9 液态玻璃、磨砂、自适应材质 | 条件 | 条件，渲染/合成器 | 条件，原生材质优先 | 原版自带 Skia shader，可抽离效果层，但 System.Drawing/GDI 背景捕获不可原搬。Mac 在 macOS 26+ 可验证 NSGlassEffectView，旧系统选择 NSVisualEffectView；与 Avalonia 内容叠层、窗口透明和命中仍需原型，不是加一个原生 View 就完成。Linux 没有通用于全部 WM 的模糊协议，要区分特定合成器材质与软件 shader。[S21] |

**五、只保留平台特项，或不建议扩展的能力**

| 功能 | Windows | Linux X11 | macOS M | 建议 |
| --- | --- | --- | --- | --- |
| E1 重启整个应用为管理员 | 可，沿用 UAC | 不做整个 GUI 提权 | 不做整个 GUI 提权 | Windows 可作为独立行动保留。Linux/Mac 真正需要权限的操作用范围明确的授权服务/辅助程序；不要把整套 UI 和插件一起作为 root 运行作为迁移方案。 |
| E2 启用/禁用硬件设备 | 条件，PnP 与管理员 | 条件，只做明确设备子类 | 不提供泛化操作 | Windows 可复用原设备控制。Linux 可选 USB 授权、网络设备等独立后端，但不是与 Windows 所有 PnP 设备等价；Mac 缺少已验证的通用公开等价方案，不显示通用“禁用硬件”菜单。 |
| E3 禁用/启用全部鼠标 | 原实验功能，谨慎保留 | 可研究指定 XInput 设备开关 | 不建议 | 原实现通过 PowerShell 禁用 Mouse 类设备。若保留应针对选定设备、有自动恢复与独立紧急恢复途径；不把 Mac 事件拦截冒充禁用硬件。初期不迁移这项。 |
| E4 管理员系统内存/缓存清理 | 原实验功能 | 不迁移为同义功能 | 不迁移为同义功能 | 原版是 Windows 特定内核/缓存操作。Linux drop_caches、Mac purge 不是等价的“优化内存”。已有插件进程 GC/工作集维护继续独立保留；本项目没必要扩展出误导性的三端“一键加速”。 |

**六、宿主适配必须先处理的约束**

1. **Linux 只支持 X11。** 与宿主 AGENTS.md 及 WindowPlatformService 的直接 XOpenDisplay 使用一致。方案不引入 Wayland 所需的远程桌面/全局快捷键权限流程；桌面环境支持仍是 X11 之上的另一个条件。
2. **macOS 的权限声明属于宿主 app bundle。** 当前 ClassIsland.Desktop/Info.plist 缺少摄像头、麦克风用途声明；苹果官方要求使用对应接口时提供这些声明。原生语音和 Apple Events 还要核对各自说明/授权与签名配置。仅把一个带 Info.plist 的 CIPX 放入插件目录，不能给宿主自动补上权限声明。[S31、S32]
3. **不能把平台接口“存在”当作实装。** 当前宿主 Mac 的 GetMousePos 返回零，Bottommost 为空，若新增功能用到它们，需要补平台实现或用自己的适配器；IAudioService 是应用播放服务，不能拿来调整系统音量。
4. **控制 ClassIsland 自身优先用宿主服务。** 档案用 IProfileService，主题用 IThemeService/正式设置行动与保存流程，外部打开用 PlatformServices.LauncherService，授权用已有授权提供方，原版私有反射与路径猜测不作为迁移标准。
5. **一个入口、一组平台后端，按子能力探测。** 例如音量需要“系统端点存在且可写”，窗口操作要逐属性支持，亮度区分真实背光与软件变暗。OS 名、文件存在或命令存在不足以证明能力可用。
6. **权限与不可实现分开。** 当前平台无法实现/缺少后端时，所有菜单、设置、SAI 积木和 AI 行动目录都不注册；可以实现但未授权时允许用户在明确的设置流程授权，再启用行动。移到不支持平台的旧自动化应明确报告不可用，不能静默替换语义。
7. **Mac 原生资产只提供 osx-arm64。** 不附带 x64 或要求 Rosetta。考虑主插件保持 net10.0，以独立 Apple Silicon 原生辅助库/平台程序集承接 AppKit、Vision、AVFoundation、Speech。宿主现有 PluginLoadContext 支持 runtimes/osx-arm64/native 搜索。框架版本和硬件能力分别检测，M 芯片并不表示具备所有 macOS 新 API。
8. **资源生命周期统一。** 事件订阅、全局热键、X11 连接、屏幕捕获、相机、麦克风和原生对象需在停用/退出时释放；TCC 状态改变后刷新能力。原生调用处理主线程要求，捕获/推理不阻塞 UI。

**七、建议实施顺序（没有实施）**

- 第一批：Excel 导出、SAI/读取文件、黑屏遮罩、非穿透点击、悬浮窗拖拽排序。依赖最少，能迅速增加可用功能。
- 第二批：音量、截图、USB/自动打开、空闲检测、热键；接着迁移模拟输入和基础窗口操作。Mac 权限声明和平台桥接在这一批一起完成。
- 第三批：摄像头、语音输入/唤醒、语音 AI、Mac 系统认证；媒体规则先做 Windows+Linux，歌词按真实来源分别适配。
- 第四批：背景亮度/OCR、玻璃、多屏配置、硬件亮度。先验证最难的捕获/合成/硬件链，再承诺 UI 行为。
- Windows 特项：设备开关、管理员重启等可以单端恢复，不必为了“三端都有”强行寻找不等价替代。

**八、本轮证据和验证边界**

已经阅读原版 Plugin.cs 的实际注册、相关服务/行动/组件实现，以及宿主平台服务和打包配置。原版已注释的 ClickSimulationAction 不另计为已交付功能。已有功能及用户此前决定移除的网页时钟不重新列入开发任务。

网络资料先尝试检索，再通过 HTTPS 直读官方文档/上游仓库。下列来源均已实际读取；部分旧网页路径失效，未把读取失败的页面当证据。本文没有运行权限弹窗、抓屏、录音、摄像头或设备控制，也没有替代三端原型验收。对 Mac 通用媒体状态、任意系统强调色和内置屏背光等没有确认稳定公开通用路径的条目，明确列为不承诺，而不是断言永远无法实现。

**九、官方与上游来源**

- **S1** [NPOI 上游：跨平台、无需 Office 的工作簿生成](https://raw.githubusercontent.com/nissl-lab/npoi/master/README.md)
- **S2** [Microsoft：SendInput 与 UIPI 限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- **S3** [X.Org 上游手册镜像：XTEST 模拟输入](https://manpages.debian.org/testing/libxtst-dev/XTestFakeKeyEvent.3.en.html)
- **S4** [X.Org 上游手册镜像：XGrabKey](https://manpages.debian.org/testing/libx11-doc/XGrabKey.3.en.html)
- **S5** [Apple：CGEvent.post](https://developer.apple.com/documentation/coregraphics/cgevent/post(tap:).md)
- **S6** [Apple：AXUIElementPerformAction](https://developer.apple.com/documentation/applicationservices/1462091-axuielementperformaction)
- **S7** [Apple：Quartz 全局事件空闲时间](https://developer.apple.com/documentation/coregraphics/cgeventsource/secondssincelasteventtype(_:eventtype:).md)
- **S8** [UDisks2：磁盘连接总线与设备属性](https://storaged.org/doc/udisks2-api/latest/gdbus-org.freedesktop.UDisks2.Drive.html)
- **S9** [Apple：卷挂载通知](https://developer.apple.com/documentation/appkit/nsworkspace/didmountnotification.md)
- **S10** [Microsoft：系统端点音量](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpoint-volume-controls)
- **S11** [Apple：Core Audio 硬件音量属性](https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertyvolumescalar.md)
- **S12** [PulseAudio 上游手册镜像：系统音量控制](https://manpages.debian.org/testing/pulseaudio-utils/pactl.1.en.html)
- **S13** [Apple：ScreenCaptureKit、授权及排除自身窗口](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos.md)
- **S14** [FFmpeg 上游：DirectShow / AVFoundation / Video4Linux2 采集](https://ffmpeg.org/ffmpeg-devices.html)
- **S15** [Vosk 上游：支持的平台与语言绑定](https://alphacephei.com/vosk/install)
- **S16** [whisper.cpp 上游：Apple Silicon、Metal、Core ML](https://raw.githubusercontent.com/ggml-org/whisper.cpp/master/README.md)
- **S17** [Apple：SFSpeechRecognizer](https://developer.apple.com/documentation/speech/sfspeechrecognizer.md)
- **S18** [Apple：macOS 26 SpeechAnalyzer](https://developer.apple.com/documentation/speech/speechanalyzer.md)
- **S19** [Apple：LAContext 与认证策略可用性](https://developer.apple.com/documentation/localauthentication/lacontext.md)
- **S20** [Apple：Vision 文本识别](https://developer.apple.com/documentation/vision/vnrecognizetextrequest.md)
- **S21** [Apple：macOS 26 NSGlassEffectView](https://developer.apple.com/documentation/appkit/nsglasseffectview.md)
- **S22** [Microsoft：显示拓扑配置](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)
- **S23** [Apple：Core Graphics 显示镜像配置](https://developer.apple.com/documentation/coregraphics/cgconfiguredisplaymirrorofdisplay(_:_:_:).md)
- **S24** [X.Org 上游手册镜像：RandR 显示器布局](https://manpages.debian.org/testing/x11-xserver-utils/xrandr.1.en.html)
- **S25** [Linux 内核：背光接口](https://docs.kernel.org/gpu/backlight.html)
- **S26** [m1ddc 上游：Apple Silicon 外接显示器 DDC/CI](https://raw.githubusercontent.com/waydabber/m1ddc/main/README.md)
- **S27** [XDG Desktop Portal：壁纸接口（可用于支持它的 X11 桌面）](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Wallpaper.html)
- **S28** [Apple：按屏幕设置壁纸](https://developer.apple.com/documentation/appkit/nsworkspace/setdesktopimageurl(_:for:options:).md)
- **S29** [freedesktop.org：MPRIS 播放状态与元数据](https://specifications.freedesktop.org/mpris-spec/latest/Player_Interface.html)
- **S30** [Apple：MPNowPlayingInfoCenter 提供本应用播放信息](https://developer.apple.com/documentation/mediaplayer/mpnowplayinginfocenter.md)
- **S31** [Apple：摄像头权限用途声明](https://developer.apple.com/documentation/bundleresources/information-property-list/nscamerausagedescription.md)
- **S32** [Apple：麦克风权限用途声明](https://developer.apple.com/documentation/bundleresources/information-property-list/nsmicrophoneusagedescription.md)
- **S33** [systemd/libudev：设备监视事件](https://www.freedesktop.org/software/systemd/man/latest/udev_monitor_receive_device.html)
- **S34** [freedesktop.org：X11 窗口管理协议](https://specifications.freedesktop.org/wm-spec/latest/ar01s05.html)

**本地源码索引**

- 原版注册：`SystemTools/Plugin.cs`。
- 新增导出/积木：`SystemTools/Services/ClassIslandProfileExcelExporter.cs`、`SuperAutoIslandIntegration.cs`、`Services/Automations/ReadFileDataBlock.cs`。
- 语音：`SystemTools/Services/KeywordSpeechService.cs`、`VoskSpeechService.cs`、`AiVoiceConversationService.cs` 和 `VoskWorker/SystemTools.VoskWorker.csproj`。
- 捕获/外观：`MainWindowBackgroundCaptureService.cs`、`MainWindowTextOcclusionService.cs`、`ThirdParty/LiquidGlassAvaloniaUI`。
- 宿主平台约束：`ClassIsland/AGENTS.md`、`ClassIsland/platforms/ClassIsland.Platforms.MacOs/Services/WindowPlatformServices.cs`、`ClassIsland/platforms/ClassIsland.Platforms.Linux/Services/WindowPlatformService.cs`。
- 宿主接入：`ClassIsland/ClassIsland.Core/AppBase.cs`、`Abstractions/Services/IThemeService.cs`、`IAudioService.cs`、`ClassIsland.Desktop/Info.plist`、`ClassIsland/PluginLoadContext.cs`。
