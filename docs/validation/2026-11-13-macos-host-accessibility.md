# Mac 界面崩溃：宿主辅助功能树空引用

日期：2026-11-13。状态：根因已复现，宿主源码补丁已生成，隔离副本的辅助功能读取与诊断按钮已通过。原宿主源码、原 .app 与插件业务代码没有为此故障改写。

## 现象与定位

诊断页显示后，读取 macOS 辅助功能树触发进程退出。用户两次提供的报告属于同一事件：PID 14389，2026-11-13 11:07:53 +0800，事件标识 39A67779-621B-4CD2-A530-5B591F7B9A69。第二份报告不是新的一次修复副本崩溃。

有两次原宿主退出记录：隔离测试进程 14327 的日志在 11:00:37 记录致命空引用；工具随后隐式重开原宿主，进程 14389 使用默认用户数据，在 11:07:52 记录同一空引用，系统于 11:07:53 生成报告。这次工具隐式重启没有保持隔离环境，是验收流程问题。默认宿主日志明确记载“此次会话没有加载插件”，因此第二次崩溃也提供了未加载 SystemTools 时的对照。默认宿主启动期间按其正常流程更新了日常设置/天气缓存；没有手工回写或恢复覆盖这些共享文件。

相关托管堆栈：

```text
System.NullReferenceException
FluentAvalonia.UI.Controls.FAItemsRepeaterAutomationPeer.GetChildrenCore()
Avalonia.Automation.Peers.ControlAutomationPeer.GetOrCreateChildrenCore()
Avalonia.Native.AvnAutomationPeer.get_Children()
Avalonia.Native.Interop.Impl.__MicroComIAvnAutomationPeerVTable.GetChildren(...)
```

FluentAvaloniaUI 3.0.0 对应源码提交 246e09f17e5538ca4a434a87e2676d343b8be6e0。该方法直接访问 `base.GetChildrenCore()` 的返回值 `.Count`；空的 repeater 在当前 Avalonia 12.1.4 下会返回 null。原宿主 Android 已在 `ClassIsland.Android/Helpers/NavigationRepeaterAutomationFix.cs` 对导航 repeater 使用空子节点保护，桌面 Mac 入口没有初始化同样保护。

这与早先显示器离线导致的 RenderTimer -6661 是两个独立故障。显示器上线后，最新插件 CIPX 在原宿主的正常安装、异平台 DLL 损坏、本机 DLL 缺失三项测试全部通过；触发辅助功能枚举才暴露本问题。不能把短时加载通过扩展为完整界面稳定性通过。

## 修复位置和范围

`eng/host-patches/classisland-macos-navigation-accessibility.patch` 是供宿主审查的源码补丁：在 Mac 桌面入口初始化与宿主现有 Android 修复同源的保护，只接管 FANavigationView/FANavigationViewItem 模板里的 repeater。空集合返回空的辅助功能节点列表；有子元素时继续原枚举逻辑。未关闭系统辅助功能、未吞掉全局 UI 异常、未给插件增加全局 UI 钩子。

补丁使用宿主原有的 UnsafeAccessor 方案，依赖 Avalonia 的 `_automationPeer` 字段及 FluentAvalonia 3.0.0 结构。升级这两项依赖前必须复核，不能无限期套用。

```sh
dotnet run --project tests/SystemTools.HostCompatibility.Tests -c Release
git -C '/path/to/ClassIsland' apply --check '/path/to/SystemTools/eng/host-patches/classisland-macos-navigation-accessibility.patch'
```

六项回归检查已在 Mac 通过：原版空 repeater 确实抛出空引用；保护后返回空列表；非空枚举保留；类处理器实际装到导航控件；已安装节点可安全读取；无关 repeater 不被接管。补丁对冻结宿主提交 `3afce2714b6ec50d4bd2a3baac95a71d96ce3b9b` 的 `git apply --check` 已通过，但未自动应用到用户原宿主仓库。

## 隔离副本验证

```sh
python3 eng/prepare_mac_host_compat.py \
  --host-source '/path/to/ClassIsland' \
  --host-app '/path/to/ClassIsland.Desktop.app' \
  --output artifacts/m1/host-compat-new
```

该工具只重编译宿主三份桌面入口源码及保护类，引用原 .app 的同一组程序集，然后修改新的 .app 副本并重新做本地 ad-hoc 签名。它不是宿主全源码构建，也不是正式签名发行包。原宿主项目和原 .app 字节不会被覆盖。新副本使用独立 bundle ID `cn.classisland.app.m1compat`；若启动时未提供隔离数据根目录，最终脚本还为它设置专用 fallback sandbox，避免 UI 工具意外重启时进入日常目录。补丁本身不含这些仅用于测试副本的身份和路径设置。

本机文件：

- 通过按钮与辅助功能读取验证的副本：`artifacts/m1/host-compat/ClassIsland.Desktop.M1Compat.app`。
- 补齐独立身份、fallback sandbox 和 Mac 平台标记后的最终生成器输出：`artifacts/m1/host-compat-final/ClassIsland.Desktop.M1Compat.app`；构建零警告、零错误，实际加载测试通过。
- 界面结果：`artifacts/m1/runs/20261113-112127-normal/gui-result.json`。观察到正确平台、完整页面和“诊断已保存。”；保存文件 source 为 `settings-button`。进程停止后六份夹具及机器级 Global.json 哈希保持一致，界面日志没有未处理异常。
- 最终副本加载结果：`artifacts/m1/runs/20261113-113102-normal/result.json`。

本轮插件包 SHA-256 为 `ee4d9611f71aa891a5161fc8aa265eecd87d933168a71dca73962f54f4923874`。原宿主三项测试、修复副本加载及按钮测试使用同一个插件文件。宿主兼容补丁不在 CIPX 内。

## 后续验收边界

原宿主仍保留该缺陷；日常宿主需要维护者合入补丁并正常构建，或采用已修复该依赖问题的宿主版本后，再进行完整辅助功能/界面验收。这里没有把修复副本的结果冒充原宿主已修好。

验收工具新增退出码检查：取得启动 JSON 后若宿主仍以 SIGABRT 等异常码结束，测试将失败，避免只看早期成功文件。工具关闭它自己创建的进程不代表原 Windows 普通退出逻辑已验收。
