using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Icons;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using SuperAutoIsland.Interface.Metadata;
using SuperAutoIsland.Interface.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using SystemTools.ConfigHandlers;
using SystemTools.Services.Automations;

namespace SystemTools.Services;

/// <summary>
/// SystemTools 对 SuperAutoIsland Blockly 的可选接入。
///
/// 该文件只依赖 SAI 的接口程序集，并继续使用 SystemTools 已注册的原始行动/规则 ID。
/// 因此积木执行仍由 ClassIsland 的 IActionService/IRulesetService 处理，SystemTools
/// 不需要复制 SAI 的运行时代码。
/// </summary>
public static class SuperAutoIslandIntegration
{
    private const string SaiPluginId = "lrs2187.sai";
    private const string CategoryName = "SystemTools";
    private const string WorkflowDropdownId = "systemtools.workflows";
    private const string FloatingWindowProfileDropdownId = "systemtools.floatingWindowProfiles";
    private const string DeviceDropdownId = "systemtools.devices";
    private static readonly (string, string) DefaultIcon = ("SystemTools", FluentIcons.SettingsRegular);
    private static bool _registered;

    /// <summary>
    /// 在 ClassIsland AppStarted 后调用。SAI 未安装时保持静默，不影响 SystemTools 正常加载。
    /// </summary>
    public static void Register()
    {
        if (_registered || !IPluginService.LoadedPlugins.Any(x => x.Manifest.Id == SaiPluginId))
        {
            return;
        }

        var server = IAppHost.TryGetService<ISaiServer>();
        if (server == null)
        {
            return;
        }

        RegisterDynamicDropdowns(server);

        server.RegisterBlocks(CategoryName, register =>
        {
            register.AddLabel("模拟操作");
            AddAction(register, "SystemTools.TypeContent", "键入内容", Fields(
                ("content", BasicFields.Text("内容")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.WindowOperation", "窗口操作", Fields(
                ("operation", BasicFields.Dropdown("操作", [
                    ("最大化", "最大化"), ("最小化", "最小化"), ("向下还原", "向下还原"), ("关闭窗口", "关闭窗口")
                ])),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));

            register.AddLabel("常用按键");
            AddAction(register, "SystemTools.EnterKey", "按下 Enter 键");
            AddAction(register, "SystemTools.EscKey", "按下 Esc 键");
            AddAction(register, "SystemTools.AltF4", "按下 Alt+F4");
            AddAction(register, "SystemTools.AltTab", "按下 Alt+Tab");
            AddAction(register, "SystemTools.CtrlZ", "按下 Ctrl+Z");
            AddAction(register, "SystemTools.F11Key", "按下 F11 键");

            register.AddLabel("显示设置");
            AddAction(register, "SystemTools.CloneDisplay", "复制屏幕", NotifyFields());
            AddAction(register, "SystemTools.ExtendDisplay", "扩展屏幕", NotifyFields());
            AddAction(register, "SystemTools.InternalDisplay", "仅电脑屏幕", NotifyFields());
            AddAction(register, "SystemTools.ExternalDisplay", "仅第二屏幕", NotifyFields());
            AddAction(register, "SystemTools.BlackScreenHtml", "黑屏 HTML", NotifyFields());
            AddAction(register, "SystemTools.ShowDesktop", "显示桌面", NotifyFields());
            AddAction(register, "SystemTools.AdjustScreenBrightness", "调整屏幕亮度", Fields(
                ("brightnessPercent", BasicFields.Number("亮度百分比", 50)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));

            register.AddLabel("电源选项");
            AddAction(register, "SystemTools.Shutdown", "计时关机", Fields(
                ("seconds", BasicFields.Number("秒数", 60)),
                ("showPrompt", BasicFields.Boolean("显示提示", true)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.AdvancedShutdown", "高级计时关机", Fields(
                ("minutes", BasicFields.Number("分钟", 2))));
            AddAction(register, "SystemTools.LockScreen", "锁定屏幕", NotifyFields());
            AddAction(register, "SystemTools.CancelShutdown", "取消关机计划", NotifyFields());
            AddAction(register, "SystemTools.ImmediateRestart", "立即重启");
            AddAction(register, "SystemTools.ImmediateShutdown", "立即关机");
            AddAction(register, "SystemTools.Sleep", "睡眠");

            register.AddLabel("文件操作");
            register.AddBlock<ReadFileDataBlock>();
            AddAction(register, "SystemTools.Copy", "复制", Fields(
                ("operationType", BasicFields.Dropdown("类型", [("文件", "文件"), ("文件夹", "文件夹")])),
                ("sourcePath", BasicFields.Text("源路径")),
                ("destinationPath", BasicFields.Text("目标路径"))));
            AddAction(register, "SystemTools.Move", "移动", Fields(
                ("operationType", BasicFields.Dropdown("类型", [("文件", "文件"), ("文件夹", "文件夹")])),
                ("sourcePath", BasicFields.Text("源路径")),
                ("destinationPath", BasicFields.Text("目标路径"))));
            AddAction(register, "SystemTools.Delete", "删除", Fields(
                ("operationType", BasicFields.Dropdown("类型", [("文件", "文件"), ("文件夹", "文件夹")])),
                ("targetPath", BasicFields.Text("目标路径"))));

            register.AddLabel("系统个性化");
            AddAction(register, "SystemTools.ChangeWallpaper", "切换壁纸", Fields(
                ("imagePath", BasicFields.Text("图片路径")),
                ("mode", BasicFields.Dropdown("模式", [("图片", "0"), ("纯色", "1")], true)),
                ("solidColor", BasicFields.Color("纯色颜色", "#000000")),
                ("fitStyle", BasicFields.Dropdown("契合度", [
                    ("平铺", "0"), ("居中", "1"), ("拉伸", "2"), ("填充", "3"), ("适应", "4"), ("跨区", "5")
                ], true)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.SwitchTheme", "切换主题色", Fields(
                ("theme", BasicFields.Dropdown("主题", [("浅色", "浅色"), ("深色", "深色")])),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.SwitchSystemAccentColor", "切换系统强调色", Fields(
                ("colorHex", BasicFields.Color("颜色", "#0078D4")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));

            register.AddLabel("实用工具");
            AddAction(register, "SystemTools.ScreenShot", "屏幕截图", Fields(
                ("saveFolder", BasicFields.Text("保存文件夹")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.SetVolume", "设置系统音量", Fields(
                ("volumePercent", BasicFields.Number("音量百分比", 50)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.KillProcess", "退出进程", Fields(
                // 进程可能在编辑器加载后才启动，必须允许输入任意进程名。
                ("processName", BasicFields.Text("进程名")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.EnableDevice", "启用硬件设备", Fields(
                ("deviceId", BasicFields.DynamicDropdown("设备 ID", DeviceDropdownId)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.DisableDevice", "禁用硬件设备", Fields(
                ("deviceId", BasicFields.DynamicDropdown("设备 ID", DeviceDropdownId)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.ShowToast", "显示 Windows 通知", Fields(
                ("title", BasicFields.Text("标题")),
                ("content", BasicFields.Text("内容"))));
            AddAction(register, "SystemTools.LoadTemporaryClassPlan", "加载临时课表", Fields(
                ("classPlanId", BasicFields.DynamicDropdown("课程表", "sai.profile.dd.classPlans")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.CameraCapture", "摄像头抓拍", Fields(
                ("deviceName", BasicFields.Text("设备名称")),
                ("saveFolder", BasicFields.Text("保存文件夹"))));

            register.AddLabel("媒体工具");
            AddAction(register, "SystemTools.BackgroundPlayAudio", "后台播放音频", Fields(
                ("audioFilePath", BasicFields.Text("音频路径")),
                ("waitForPlaybackCompleted", BasicFields.Boolean("等待播放结束")),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));

            register.AddLabel("悬浮窗设置");
            AddAction(register, "SystemTools.ShowFloatingWindow", "显示悬浮窗", Fields(
                ("showFloatingWindow", BasicFields.Boolean("显示悬浮窗", true)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.ToggleFloatingWindowLayer", "切换悬浮窗层级", Fields(
                ("targetLayer", BasicFields.Dropdown("目标层级", [
                    ("切换", "-1"), ("置底", "0"), ("置顶", "1")
                ], true)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.ToggleFloatingWindowProfile", "切换悬浮窗配置方案", Fields(
                ("targetProfileName", BasicFields.DynamicDropdown("目标方案名称", FloatingWindowProfileDropdownId)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));
            AddAction(register, "SystemTools.SwitchFloatingWindowTheme", "切换悬浮窗主题", Fields(
                ("targetTheme", BasicFields.Dropdown("目标主题", [
                    ("切换到下一个", "-1"), ("跟随系统", "0"), ("浅色", "1"), ("深色", "2"), ("自适应背景", "3")
                ], true)),
                ("notifyOnExecute", BasicFields.Boolean("执行时提醒"))));

            register.AddLabel("自动化与 ClassIsland");
            AddAction(register, "SystemTools.ToggleWorkflow", "开关自动化", Fields(
                ("targetWorkflowName", BasicFields.DynamicDropdown("自动化名称", WorkflowDropdownId)),
                ("targetWorkflowIndex", BasicFields.Number("自动化索引", -1)),
                ("enableMode", BasicFields.Dropdown("操作", [
                    ("切换", ""), ("启用", "true"), ("禁用", "false")
                ])),
                ("revertToOriginal", BasicFields.Boolean("恢复原状态", true))));
            AddAction(register, "SystemTools.TriggerCustomTrigger", "触发指定触发器", Fields(
                // 该 ID 是 ActionInProgressTrigger 中由用户自定义的匹配字符串，
                // 不是 ClassIsland 已注册的触发器提供方 ID，因此保留文本输入。
                ("triggerId", BasicFields.Text("触发器 ID"))));
            AddAction(register, "SystemTools.RestartAsAdmin", "重启应用为管理员身份");
            AddAction(register, "SystemTools.ClearAllNotifications", "清除全部提醒", NotifyFields());
            AddAction(register, "SystemTools.OpenAppSettings", "打开应用设置", NotifyFields());
            AddAction(register, "SystemTools.OpenProfileEditor", "打开档案编辑", NotifyFields());
            AddAction(register, "SystemTools.OpenClassSwapWindow", "打开换课窗口", NotifyFields());
            AddAction(register, "SystemTools.FullscreenClock", "沉浸式时钟");
            AddAction(register, "SystemTools.AutoSwitchClassIslandTheme", "自动切换 ClassIsland 主题", EnableNotifyFields());
            AddAction(register, "SystemTools.AutoHideMainWindowWhenOccluded", "遮挡文字时隐藏主界面", EnableNotifyFields());
            AddAction(register, "SystemTools.AutoOpenUsbDriveOnInsert", "自动播放", EnableNotifyFields());

            register.AddLabel("AI 与实验性功能");
            AddAction(register, "SystemTools.EnableVoiceWakeAi", "启用语音唤醒 AI", EnableNotifyFields());
            AddAction(register, "SystemTools.WakeUpVoiceConversationAi", "唤醒语音对话 AI");
            AddAction(register, "SystemTools.ShowAiChatDialog", "显示 AI 对话框");
            AddAction(register, "SystemTools.DisableMouse", "禁用鼠标");
            AddAction(register, "SystemTools.EnableMouse", "启用鼠标");

            // ClickSimulation 当前没有在 SystemTools 中注册；其余三个行动依赖录制后的输入数组，
            // SAI 当前的基础字段无法完整表达其设置，暂不注册：
            // SimulateKeyCombination、SimulateKeyboard、SimulateMouse。
            register.AddLabel("规则");
            AddRule(register, "SystemTools.ProcessRunningRule", "程序正在运行", Fields(
                // 规则通常用于等待稍后启动的进程，不能限制为加载编辑器时的进程快照。
                ("processName", BasicFields.Text("进程名"))));
            AddRule(register, "SystemTools.UsingClassPlanRule", "正在使用某课程表", Fields(
                ("classPlanId", BasicFields.DynamicDropdown("课程表", "sai.profile.dd.classPlans"))));
            AddRule(register, "SystemTools.UsingTimeLayoutRule", "正在使用某时间表", Fields(
                ("timeLayoutId", BasicFields.DynamicDropdown("时间表", "sai.profile.dd.timeLayouts"))));
            AddRule(register, "SystemTools.InTimePeriodRule", "是否在某时间段", Fields(
                ("startTime", BasicFields.Text("开始时间", "08:00:00")),
                ("endTime", BasicFields.Text("结束时间", "18:00:00"))));
            AddRule(register, "SystemTools.MediaMusicPlayingRule", "正在播放媒体音乐");
        });

        _registered = true;
    }

    private static void AddAction(BlocksRegister register, string id, string name, Dictionary<string, Field>? fields = null)
    {
        if (!IActionService.ActionInfos.ContainsKey(id))
        {
            return;
        }

        register.AddBlock(new BlockMetadata(id)
        {
            Kind = BlockKind.Action,
            Name = name,
            Icon = GetActionIcon(id),
            Fields = fields ?? []
        });
    }

    private static void AddRule(
        BlocksRegister register,
        string id,
        string name,
        Dictionary<string, Field>? fields = null)
    {
        if (!IRulesetService.Rules.ContainsKey(id))
        {
            return;
        }

        register.AddBlock(new BlockMetadata(id)
        {
            Kind = BlockKind.Rule,
            Name = name,
            Icon = GetRuleIcon(id),
            Fields = fields ?? []
        });
    }

    private static (string, string) GetActionIcon(string id)
    {
        if (IActionService.ActionInfos.TryGetValue(id, out var info) &&
            info.IconSource is FAFontIconSource icon &&
            !string.IsNullOrWhiteSpace(icon.Glyph))
        {
            return (info.Name, icon.Glyph);
        }

        return DefaultIcon;
    }

    private static (string, string) GetRuleIcon(string id)
    {
        if (IRulesetService.Rules.TryGetValue(id, out var info) &&
            info.IconSource is FAFontIconSource icon &&
            !string.IsNullOrWhiteSpace(icon.Glyph))
        {
            return (info.Name, icon.Glyph);
        }

        return DefaultIcon;
    }

    private static void RegisterDynamicDropdowns(ISaiServer server)
    {
        server.RegisterDynamicDropdown(WorkflowDropdownId, GetWorkflowOptions);
        server.RegisterDynamicDropdown(FloatingWindowProfileDropdownId, GetFloatingWindowProfileOptions);
        server.RegisterDynamicDropdown(DeviceDropdownId, GetDeviceOptions);
    }

    private static Task<List<(string, string)>> GetWorkflowOptions()
    {
        var options = IAppHost.TryGetService<IAutomationService>()?.Workflows
            .Select(x => x.ActionSet.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x, x))
            .ToList() ?? [];

        return Task.FromResult(EnsureDropdownNotEmpty(options, ("按索引选择（留空）", string.Empty)));
    }

    private static Task<List<(string, string)>> GetFloatingWindowProfileOptions()
    {
        var options = IAppHost.TryGetService<FloatingWindowProfileManager>()?.GetProfileNames()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x, x))
            .ToList() ?? [];

        options.Insert(0, ("切换到下一个方案（留空）", string.Empty));
        return Task.FromResult(options);
    }

    private static Task<List<(string, string)>> GetDeviceOptions()
    {
        var options = new List<(string, string)>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL");
                using var devices = searcher.Get();
                foreach (var device in devices)
                {
                    var id = device["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var name = device["Name"]?.ToString();
                    options.Add((string.IsNullOrWhiteSpace(name) ? id : $"{name} ({id})", id));
                }
            }
            catch
            {
                // 设备管理查询可能因权限或 WMI 状态失败，保留手动输入。
            }
        }

        options = options
            .DistinctBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Task.FromResult(EnsureDropdownNotEmpty(options, ("手动输入设备 ID", string.Empty)));
    }

    private static List<(string, string)> EnsureDropdownNotEmpty(
        List<(string, string)> options,
        (string, string) fallback) => options.Count == 0 ? [fallback] : options;

    private static Dictionary<string, Field> Fields(params (string Name, Field Field)[] fields) =>
        fields.ToDictionary(x => x.Name, x => x.Field);

    private static Dictionary<string, Field> NotifyFields() =>
        Fields(("notifyOnExecute", BasicFields.Boolean("执行时提醒")));

    private static Dictionary<string, Field> EnableNotifyFields() =>
        Fields(
            ("enable", BasicFields.Boolean("启用", true)),
            ("notifyOnExecute", BasicFields.Boolean("执行时提醒")));
}
