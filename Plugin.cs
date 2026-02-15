using AvaloniaEdit.Utils;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Helpers;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using SystemTools.Actions;
using SystemTools.ConfigHandlers;
using SystemTools.Controls;
using SystemTools.Services;
using SystemTools.Shared;
using SystemTools.Triggers;

namespace SystemTools;

public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // ========== 初始化配置 ==========
        GlobalConstants.PluginConfigFolder = PluginConfigFolder;
        GlobalConstants.Information.PluginFolder = Info.PluginFolderPath;
        GlobalConstants.Information.PluginVersion = Info.Manifest.Version;
        GlobalConstants.MainConfig = new MainConfigHandler(PluginConfigFolder);

        Console.WriteLine($"[SystemTools] 实验性功能状态: {GlobalConstants.MainConfig.Data.EnableExperimentalFeatures}");

        // ========== 注册设置页面 ==========
        services.AddSettingsPage<SystemToolsSettingsPage>();
        services.AddSettingsPage<AboutSettingsPage>();


        // ========== 构建行动树 ==========
        BuildBaseActionTree();

        // ========== 注册行动和触发器 ==========
        RegisterBaseActions(services);

        // ========== 注册实验性功能 ==========
        if (GlobalConstants.MainConfig.Data.EnableExperimentalFeatures)
        {
            Console.WriteLine("[SystemTools] 正在注册实验性功能...");
            RegisterExperimentalFeatures(services);
        }
        else
        {
            Console.WriteLine("[SystemTools] 实验性功能未启用");
        }
        AppBase.Current.AppStarted += (o, args) =>
        {
            Console.WriteLine("[SystemTools] 启动完成");
        };

        // ========== 注册 FFmpeg 功能（新增）==========
        if (GlobalConstants.MainConfig.Data.EnableFfmpegFeatures)
        {
            RegisterFfmpegFeatures(services);
        }
        else
        {
            Console.WriteLine("[SystemTools] FFmpeg功能未启用");
        }

        // ========== 版本检查 ==========
        AppBase.Current.AppStarted += (_, _) =>
        {
            VersionCheckService.CheckAndNotify();
        };

        // ========== 订阅关闭事件 ==========
        AppBase.Current.AppStopping += OnAppStopping;

        AppBase.Current.AppStarted += (_, _) => RegisterSettingsPageGroup(services);
    }

    private void BuildBaseActionTree()
    {
        IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup("SystemTools 行动", "\uE079"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("模拟操作…", "\uEA0B"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("显示设置…", "\uF397"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("电源选项…", "\uEDE8"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("文件操作…", "\uE759"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("系统个性化…", "\uF42F"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("实用工具…", "\uE352"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("其他工具…", "\uE32C"));

        BuildSimulationMenu();
        BuildDisplayMenu();
        BuildPowerMenu();
        BuildFileMenu();
        BuildPersonalizationMenu();
        BuildUtilityMenu();
        BuildOtherMenu();

        IActionService.ActionMenuTree["SystemTools 行动"].AddRange([
            new ActionMenuTreeItem("SystemTools.TriggerCustomTrigger", "触发指定触发器", "\uEAB7"),
            new ActionMenuTreeItem("SystemTools.RestartAsAdmin", "重启应用为管理员身份", "\uEF53"),
        ]);
    }

    private void RegisterExperimentalFeatures(IServiceCollection services)
    {
        // 实验性功能…
        IActionService.ActionMenuTree["SystemTools 行动"].Add(
            new ActionMenuTreeGroup("实验性功能…", "\uE508")
        );

        IActionService.ActionMenuTree["SystemTools 行动"]["实验性功能…"].AddRange([
            new ActionMenuTreeItem("SystemTools.DisableMouse", "禁用鼠标", "\uE5C7"),
            new ActionMenuTreeItem("SystemTools.EnableMouse", "启用鼠标", "\uE5BF")
        ]);

        services.AddAction<DisableMouseAction, DisableMouseSettingsControl>();
        services.AddAction<EnableMouseAction>();
    }

    private void RegisterFfmpegFeatures(IServiceCollection services)
    {
        Console.WriteLine("[SystemTools] 正在注册 FFmpeg 依赖功能...");

        // FFmpeg 依赖功能
        services.AddAction<CameraCaptureAction, CameraCaptureSettingsControl>();

        IActionService.ActionMenuTree["SystemTools 行动"]["实用工具…"].Add(
            new ActionMenuTreeItem("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E")
        );
    }

    private void RegisterBaseActions(IServiceCollection services)
    {
        // 模拟操作…
        services.AddAction<EnterKeyAction>();
        services.AddAction<EscAction>();
        services.AddAction<AltF4Action>();
        services.AddAction<AltTabAction>();
        services.AddAction<F11Action>();
        services.AddAction<SimulateKeyboardAction, SimulateKeyboardSettingsControl>();
        services.AddAction<SimulateMouseAction, SimulateMouseSettingsControl>();
        services.AddAction<TypeContentAction, TypeContentSettingsControl>();
        services.AddAction<WindowOperationAction, WindowOperationSettingsControl>();

        // 显示设置…
        services.AddAction<CloneDisplayAction>();
        services.AddAction<ExtendDisplayAction>();
        services.AddAction<InternalDisplayAction>();
        services.AddAction<ExternalDisplayAction>();
        services.AddAction<BlackScreenHtmlAction>();

        // 电源选项…
        services.AddAction<ShutdownAction, ShutdownSettingsControl>();
        services.AddAction<LockScreenAction>();
        services.AddAction<CancelShutdownAction>();

        // 文件操作…
        services.AddAction<CopyAction, CopySettingsControl>();
        services.AddAction<MoveAction, MoveSettingsControl>();
        services.AddAction<DeleteAction, DeleteSettingsControl>();

        // 系统个性化…
        services.AddAction<ChangeWallpaperAction, ChangeWallpaperSettingsControl>();
        services.AddAction<SwitchThemeAction, ThemeSettingsControl>();

        // 实用工具…
        //services.AddAction<CameraCaptureAction, CameraCaptureSettingsControl>();
        services.AddAction<ScreenShotAction, ScreenShotSettingsControl>();
        services.AddAction<KillProcessAction, KillProcessSettingsControl>();
        services.AddAction<EnableDeviceAction, EnableDeviceSettingsControl>();
        services.AddAction<DisableDeviceAction, DisableDeviceSettingsControl>();
        services.AddAction<ShowToastAction, ShowToastSettingsControl>();

        //其他工具…
        services.AddAction<FullscreenClockAction, FullscreenClockSettingsControl>();

        // 触发指定触发器
        services.AddAction<TriggerCustomTriggerAction, TriggerCustomTriggerSettingsControl>();
        // 重启ClassIsland到管理员身份
        services.AddAction<RestartAsAdminAction>();

        
        // 触发器们
        services.AddTrigger<UsbDeviceTrigger, UsbDeviceTriggerSettings>();
        services.AddTrigger<HotkeyTrigger, HotkeyTriggerSettings>();
        services.AddTrigger<ActionInProgressTrigger, ActionInProgressTriggerSettings>();
    }

    #region 菜单构建辅助方法

    private void BuildSimulationMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].Add(
            new ActionMenuTreeGroup("常用模拟键", "\uEA0B")
        );

        IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].AddRange([
            new ActionMenuTreeItem("SystemTools.SimulateKeyboard", "模拟键盘", "\uEA0F"),
            new ActionMenuTreeItem("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1"),
            new ActionMenuTreeItem("SystemTools.TypeContent", "键入内容", "\uE4BE"),
            new ActionMenuTreeItem("SystemTools.WindowOperation", "窗口操作", "\uF4B3")
        ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"]["常用模拟键"].AddRange([
            new ActionMenuTreeItem("SystemTools.AltF4", "按下 Alt+F4", "\uEA0B"),
            new ActionMenuTreeItem("SystemTools.AltTab", "按下 Alt+Tab", "\uEA0B"),
            new ActionMenuTreeItem("SystemTools.EnterKey", "按下 Enter 键", "\uEA0B"),
            new ActionMenuTreeItem("SystemTools.EscKey", "按下 Esc 键", "\uEA0B"),
            new ActionMenuTreeItem("SystemTools.F11Key", "按下 F11 键", "\uEA0B")
        ]);
    }

    private void BuildDisplayMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["显示设置…"].AddRange([
            new ActionMenuTreeItem("SystemTools.CloneDisplay", "复制屏幕", "\uE635"),
            new ActionMenuTreeItem("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647"),
            new ActionMenuTreeItem("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE62F"),
            new ActionMenuTreeItem("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE641"),
            new ActionMenuTreeItem("SystemTools.BlackScreenHtml", "黑屏html", "\uE643")
        ]);
    }

    private void BuildPowerMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["电源选项…"].AddRange([
            new ActionMenuTreeItem("SystemTools.Shutdown", "计时关机", "\uE4C4"),
            new ActionMenuTreeItem("SystemTools.CancelShutdown", "取消关机计划", "\uE4CC"),
            new ActionMenuTreeItem("SystemTools.LockScreen", "锁定屏幕", "\uEAF0")
        ]);
    }

    private void BuildFileMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["文件操作…"].AddRange([
            new ActionMenuTreeItem("SystemTools.Copy", "复制", "\uE6AB"),
            new ActionMenuTreeItem("SystemTools.Move", "移动", "\uE6E7"),
            new ActionMenuTreeItem("SystemTools.Delete", "删除", "\uE61D")
        ]);
    }

    private void BuildPersonalizationMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["系统个性化…"].AddRange([
            new ActionMenuTreeItem("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC"),
            new ActionMenuTreeItem("SystemTools.SwitchTheme", "切换主题色", "\uF42F")
        ]);
    }

    private void BuildUtilityMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["实用工具…"].AddRange([
            new ActionMenuTreeItem("SystemTools.KillProcess", "退出进程", "\uE0DE"),
            new ActionMenuTreeItem("SystemTools.ShowToast", "拉起自定义Windows通知", "\uE3E4"),
            //new ActionMenuTreeItem("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E"),
            new ActionMenuTreeItem("SystemTools.ScreenShot", "屏幕截图", "\uEEE7"),
            new ActionMenuTreeItem("SystemTools.DisableDevice", "禁用硬件设备", "\uE09F"),
            new ActionMenuTreeItem("SystemTools.EnableDevice", "启用硬件设备", "\uE0AD")
        ]);
    }

    private void BuildOtherMenu()
    {
        IActionService.ActionMenuTree["SystemTools 行动"]["其他工具…"].AddRange([
            new ActionMenuTreeItem("SystemTools.FullscreenClock", "沉浸式时钟", "\uE4D2")
        ]);
    }

    #endregion

    private void RegisterSettingsPageGroup(IServiceCollection services)
    {

        if (InjectServices.TryGetAddSettingsPageGroupMethod(out var addSettingsPageGroupMethod))
        {
            addSettingsPageGroupMethod.Invoke(
                null,
                [services, "systemtools.settings", "\uE079", "SystemTools 设置"]);

            var groupIdProperty = InjectServices.GetSettingsPageInfoGroupIdProperty();

            foreach (var info in SettingsWindowRegistryService.Registered
                .Where(info => info.Id.StartsWith("systemtools.settings")))
            {
                groupIdProperty?.SetValue(info, "systemtools.settings");
            }
        }
        else
        {
            var nameField = InjectServices.GetSettingsPageInfoNameField();
            foreach (var info in SettingsWindowRegistryService.Registered
                .Where(info => info.Id.StartsWith("systemtools.settings")))
            {
                var currentName = (string?)nameField.GetValue(info);
                nameField.SetValue(info, "SystemTools 设置 - " + currentName);
            }
        }
    }


    private void OnAppStopping(object? sender, EventArgs e)
    {
        Console.WriteLine("[SystemTools] 应用正在关闭，正在保存配置...");
        GlobalConstants.MainConfig?.Save();
    }

}