# Mac 显示桌面与系统强调色可行性

## 后续决定与实现

用户随后要求实现显示桌面，并明确任意系统强调色不做。显示桌面现已使用用户在“系统功能与权限”页配置的快捷键，经独立的原生 CGEvent 序列发送；未配置或缺辅助功能时拒绝，完整预建按下/释放事件后派发，默认不写入任何系统快捷键。原 DesktopSessionService.desktop 仍拒绝直接调用，实际注册为 MacShowDesktopAction；任意系统强调色继续 Unsupported，不新增预设色板替代功能。

本轮仅编译与自审，遵循用户“先不用测试”，没有运行单元测试、实机验收或自动 CI 测试。后文是实现前的技术研究，涉及“未实现/可研究”的描述保留为历史背景，以本段和产品范围为准。

## 原可行性研究

日期：2026-11-16。仅研究可行性，本轮没有实现或启用这两项；当前 DesktopSessionService 的 desktop、accent.read 和 accent.set 仍返回 Unsupported。所有未来实现限于 SystemTools 及自有辅助程序。

## 显示桌面：可按系统快捷键有条件实现

Apple 的当前键盘快捷键指南列出 Command-Mission Control、Fn-H 和 Fn-F11；实际映射受用户快捷键和键盘设置影响。可利用我们已有的原生输入服务，通过公开 CGEvent 路线发送用户确认的“显示桌面”快捷键。需要对应输入/辅助功能权限，缺权限或未配置时应明确拒绝。

建议新增显式的快捷键配置与验证步骤，而不是假定所有 Mac 固定为 F11、自动修改系统快捷键或隐藏/最小化所有应用来冒充系统显示桌面。系统快捷键可能在重复触发时恢复窗口，因此行动命名和重复执行契约也必须明确。需在真实桌面、Spaces、全屏与不同键盘设置下验收。

这是一条可实现的条件路线；没有查到可让当前插件不依赖快捷键配置、直接保证所有环境都生效的公开单一系统调用。本次只记录方案，不据此把当前功能标成已支持。

## 任意系统强调色：不能承诺通用公开实现

Apple 外观设置区分 Accent color 和 Highlight color：前者是按钮等控件的强调色，后者是选中文字/列表的高亮色。当前本机 macOS 26.6 SDK 的 NSColor.controlAccentColor 是 readonly、随用户偏好变化的动态颜色，公开属性本身不提供设置全局颜色的 setter。

只读核对本机 System Events 的脚本字典：appearance 是 blue/graphite 枚举，highlight color 接受预设枚举或 RGB；没有任意 accent color 属性。能写 highlight color 不代表能把所有系统控件换成任意 RGB。Apple 设置界面提供系统预设强调色；没有找到让我们稳定写入任意全局 RGB 强调色的公开 API。

可进一步研究“选择系统预设强调色”：由用户同意后通过辅助功能/UI 自动化操作系统设置，受设置界面版本、语言及权限影响，需要读回/恢复测试。它只等价于系统界面能选择的色板，不能沿用 Windows 任意 RGB 的承诺。修改未公开偏好键也不能被当成跨版本可靠的公开接口。

插件自己的窗口/控件可以采用自定义颜色，但那是插件外观，不是全局系统强调色。建议当前任意系统强调色入口继续不可用；若用户需要，另行定义预设色板功能。

## 本机核对

- src/SystemTools.Platforms.MacOS/DesktopSessionService.cs：desktop 与 accent 操作均明确不可用。
- native/macos/SystemToolsNative.swift：已有 CGEvent 原生输入与按键释放路径，可供未来快捷键方案复用。
- Xcode MacOSX SDK AppKit/NSColor.h：controlAccentColor readonly 声明。
- /usr/bin/sdef /System/Library/CoreServices/System Events.app：Appearance Suite 的 appearance 与 highlight color 属性，读取时未改变系统设置。

## 一手资料

- Apple 显示桌面： https://support.apple.com/guide/mac-help/quickly-see-the-desktop-mchlp1833/mac
- Apple 键盘快捷键： https://support.apple.com/102650
- Apple 外观设置： https://support.apple.com/guide/mac-help/change-appearance-settings-mchlp1225/mac
- Apple NSColor.controlAccentColor： https://developer.apple.com/documentation/appkit/nscolor/controlaccentcolor
- Apple UI scripting： https://developer.apple.com/library/archive/documentation/LanguagesUtilities/Conceptual/MacAutomationScriptingGuide/AutomatetheUserInterface.html
- Apple CGEvent.post： https://developer.apple.com/documentation/coregraphics/cgevent/post(tap:)
