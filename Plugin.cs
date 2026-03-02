using AvaloniaEdit.Utils;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using ClassIsland.Core.Helpers;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using SystemTools.Actions;
using SystemTools.Models.ComponentSettings;
using SystemTools.ConfigHandlers;
using SystemTools.Controls;
using SystemTools.Controls.Components;
using SystemTools.Services;
using SystemTools.Shared;
using SystemTools.Triggers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SystemTools;
/*
            _________                 __                  ___________              .__
          /   _____/___.__.  _______/  |_   ____    _____\__    ___/____    ____  |  |    ______
          \_____  \<   |  | /  ___/\   __\_/ __ \  /     \ |    |  /  _ \  /  _ \ |  |   /  ___/
          /        \\___  | \___ \  |  |  \  ___/ |  Y Y  \|    | (  <_> )(  <_> )|  |__ \___ \
         /_______  // ____|/____  > |__|   \___  >|__|_|  /|____|  \____/  \____/ |____//____  >
                \/ \/          \/             \/       \/                                   \/
*/
public class Plugin : PluginBase
{
    private ILogger<Plugin>? _logger;
    private bool _faceRecognitionRegistered = false;

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // ========== 初始化配置 ==========
        Console.WriteLine("[SystemTools]-------------------------------------------------------------------\r\n" 
                          + GlobalConstants.Assets.AsciiLogo
                          + "\r\n Copyright (C) 2026 Programmer_MrWang \r\n Licensed under GNU AGPLv3. \r\n" 
                          + "正在初始化SystemTools配置...-----------------------------------------------------------");
        
        GlobalConstants.PluginConfigFolder = PluginConfigFolder;
        GlobalConstants.Information.PluginFolder = Info.PluginFolderPath;
        GlobalConstants.Information.PluginVersion = Info.Manifest.Version;
        GlobalConstants.MainConfig = new MainConfigHandler(PluginConfigFolder);

        services.AddLogging();
        
        // ========== 注册可选人脸识别 ==========
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (GlobalConstants.MainConfig.Data.EnableFaceRecognition)
            {
                if (CheckFaceRecognitionFilesExist())
                {
                    services.AddAuthorizeProvider<FaceRecognitionAuthorizer>();
                    _faceRecognitionRegistered = true;
                }
                else
                {
                    _faceRecognitionRegistered = false;
                }
            }
        }
        
        // ========== 注册设置页面 ==========
        services.AddSettingsPage<SystemToolsSettingsPage>();
        services.AddSettingsPage<AboutSettingsPage>();

        // ========== 构建行动树（根据配置）==========
        BuildBaseActionTree();

        // ========== 注册行动、触发器和组件（根据配置）==========
        RegisterBaseActions(services);
        RegisterBaseTriggers(services);
        RegisterBaseComponents(services);

        var experimentalEnabled = GlobalConstants.MainConfig.Data.EnableExperimentalFeatures;
        var ffmpegEnabled = GlobalConstants.MainConfig.Data.EnableFfmpegFeatures;

        AppBase.Current.AppStarted += (o, args) =>
        {
            _logger = IAppHost.GetService<ILogger<Plugin>>();

            _logger?.LogInformation("[SystemTools]实验性功能状态: {Status}", experimentalEnabled);
            _logger?.LogInformation("[SystemTools]FFmpeg功能状态: {Status}", ffmpegEnabled);
            if (GlobalConstants.MainConfig.Data.EnableFaceRecognition)
            {
                if (_faceRecognitionRegistered)
                {
                    _logger?.LogInformation("[SystemTools]人脸识别认证器已注册");
                }
                else
                {
                    _logger?.LogWarning("[SystemTools]人脸识别功能已启用，但缺少必要的文件或文件夹（Models、runtimes、OpenCvSharp.Extensions.dll、OpenCvSharp.dll、DlibDotNet.dll），已跳过注册。");
                }
            }
            _logger?.LogInformation("[SystemTools]SystemTools 启动完成");
        };

        // ========== 注册实验性功能 ==========
        if (experimentalEnabled)
        {
            RegisterExperimentalFeatures(services);
        }

        // ========== 注册 FFmpeg 功能 ==========
        if (ffmpegEnabled)
        {
            RegisterFfmpegFeatures(services);
        }

        // ========== 注册热键服务 ==========
        services.AddSingleton<IHotkeyService, HotkeyService>();

        // ========== 版本检查 ==========
        AppBase.Current.AppStarted += (_, _) => { VersionCheckService.CheckAndNotify(); };

        // ========== 订阅关闭事件 ==========
        AppBase.Current.AppStopping += OnAppStopping;

        // ========== 注册设置页面分组 ==========
        AppBase.Current.AppStarted += (_, _) => RegisterSettingsPageGroup(services);
    }
    
    #region 人脸识别文件检查

    private bool CheckFaceRecognitionFilesExist()
    {
        try
        {
            var pluginFolder = GlobalConstants.Information.PluginFolder;
            var requiredPaths = new[]
            {
                Path.Combine(pluginFolder, "Models"),
                Path.Combine(pluginFolder, "runtimes"),
                Path.Combine(pluginFolder, "OpenCvSharp.Extensions.dll"),
                Path.Combine(pluginFolder, "OpenCvSharp.dll"),
                Path.Combine(pluginFolder, "DlibDotNet.dll")
            };

            return requiredPaths.All(p => 
                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) 
                    ? File.Exists(p) 
                    : Directory.Exists(p));
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 注册方法

    private void RegisterBaseActions(IServiceCollection services)
    {
        var config = GlobalConstants.MainConfig!.Data;

        // 模拟操作
        RegisterActionIfEnabled<SimulateKeyboardAction, SimulateKeyboardSettingsControl>(services, config,
            "SystemTools.SimulateKeyboard");
        RegisterActionIfEnabled<SimulateMouseAction, SimulateMouseSettingsControl>(services, config,
            "SystemTools.SimulateMouse");
        RegisterActionIfEnabled<TypeContentAction, TypeContentSettingsControl>(services, config,
            "SystemTools.TypeContent");
        RegisterActionIfEnabled<WindowOperationAction, WindowOperationSettingsControl>(services, config,
            "SystemTools.WindowOperation");

        // 常用按键
        RegisterActionIfEnabled<EnterKeyAction>(services, config, "SystemTools.EnterKey");
        RegisterActionIfEnabled<EscAction>(services, config, "SystemTools.EscKey");
        RegisterActionIfEnabled<AltF4Action>(services, config, "SystemTools.AltF4");
        RegisterActionIfEnabled<AltTabAction>(services, config, "SystemTools.AltTab");
        RegisterActionIfEnabled<F11Action>(services, config, "SystemTools.F11Key");

        // 显示设置
        RegisterActionIfEnabled<CloneDisplayAction>(services, config, "SystemTools.CloneDisplay");
        RegisterActionIfEnabled<ExtendDisplayAction>(services, config, "SystemTools.ExtendDisplay");
        RegisterActionIfEnabled<InternalDisplayAction>(services, config, "SystemTools.InternalDisplay");
        RegisterActionIfEnabled<ExternalDisplayAction>(services, config, "SystemTools.ExternalDisplay");
        RegisterActionIfEnabled<BlackScreenHtmlAction>(services, config, "SystemTools.BlackScreenHtml");

        // 电源选项
        RegisterActionIfEnabled<ShutdownAction, ShutdownSettingsControl>(services, config, "SystemTools.Shutdown");
        RegisterActionIfEnabled<LockScreenAction>(services, config, "SystemTools.LockScreen");
        RegisterActionIfEnabled<CancelShutdownAction>(services, config, "SystemTools.CancelShutdown");

        // 文件操作
        RegisterActionIfEnabled<CopyAction, CopySettingsControl>(services, config, "SystemTools.Copy");
        RegisterActionIfEnabled<MoveAction, MoveSettingsControl>(services, config, "SystemTools.Move");
        RegisterActionIfEnabled<DeleteAction, DeleteSettingsControl>(services, config, "SystemTools.Delete");

        // 系统个性化
        RegisterActionIfEnabled<ChangeWallpaperAction, ChangeWallpaperSettingsControl>(services, config,
            "SystemTools.ChangeWallpaper");
        RegisterActionIfEnabled<SwitchThemeAction, ThemeSettingsControl>(services, config, "SystemTools.SwitchTheme");

        // 实用工具（基础）
        RegisterActionIfEnabled<ScreenShotAction, ScreenShotSettingsControl>(services, config,
            "SystemTools.ScreenShot");
        RegisterActionIfEnabled<SetVolumeAction, SetVolumeSettingsControl>(services, config, "SystemTools.SetVolume");
        RegisterActionIfEnabled<KillProcessAction, KillProcessSettingsControl>(services, config,
            "SystemTools.KillProcess");
        RegisterActionIfEnabled<EnableDeviceAction, EnableDeviceSettingsControl>(services, config,
            "SystemTools.EnableDevice");
        RegisterActionIfEnabled<DisableDeviceAction, DisableDeviceSettingsControl>(services, config,
            "SystemTools.DisableDevice");
        RegisterActionIfEnabled<ShowToastAction, ShowToastSettingsControl>(services, config, "SystemTools.ShowToast");

        // 其他工具
        RegisterActionIfEnabled<FullscreenClockAction, FullscreenClockSettingsControl>(services, config,
            "SystemTools.FullscreenClock");

        // 独立行动
        RegisterActionIfEnabled<TriggerCustomTriggerAction, TriggerCustomTriggerSettingsControl>(services, config,
            "SystemTools.TriggerCustomTrigger");
        RegisterActionIfEnabled<RestartAsAdminAction>(services, config, "SystemTools.RestartAsAdmin");
    }

    private void RegisterBaseTriggers(IServiceCollection services)
    {
        var config = GlobalConstants.MainConfig!.Data;

        RegisterTriggerIfEnabled<UsbDeviceTrigger, UsbDeviceTriggerSettings>(services, config,
            "SystemTools.UsbDeviceTrigger");
        RegisterTriggerIfEnabled<HotkeyTrigger, HotkeyTriggerSettings>(services, config, "SystemTools.HotkeyTrigger");
        RegisterTriggerIfEnabled<ActionInProgressTrigger, ActionInProgressTriggerSettings>(services, config,
            "SystemTools.ActionInProgressTrigger");
    }

    private void RegisterBaseComponents(IServiceCollection services)
    {
        var config = GlobalConstants.MainConfig!.Data;

        RegisterComponentIfEnabled<NetworkStatusComponent, NetworkStatusSettingsControl>(services, config,
            "SystemTools.NetworkStatus");
        RegisterComponentIfEnabled<LyricsDisplayComponent, LyricsDisplaySettingsControl>(services, config,
            "SystemTools.LyricsDisplay");
    }

    private void RegisterExperimentalFeatures(IServiceCollection services)
    {
        _logger?.LogInformation("[SystemTools]正在注册实验性功能...");

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
        _logger?.LogInformation("[SystemTools]正在注册 FFmpeg 依赖功能...");

        services.AddAction<CameraCaptureAction, CameraCaptureSettingsControl>();

        IActionService.ActionMenuTree["SystemTools 行动"]["实用工具…"].Add(
            new ActionMenuTreeItem("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E")
        );
    }

    #endregion

    #region 条件注册辅助方法

    private void RegisterActionIfEnabled<TAction>(IServiceCollection services, MainConfigData config, string actionId)
        where TAction : ClassIsland.Core.Abstractions.Automation.ActionBase
    {
        if (config.IsActionEnabled(actionId))
        {
            services.AddAction<TAction>();
        }
    }

    private void RegisterActionIfEnabled<TAction, TSettingsControl>(IServiceCollection services, MainConfigData config,
        string actionId)
        where TAction : ClassIsland.Core.Abstractions.Automation.ActionBase
        where TSettingsControl : ClassIsland.Core.Abstractions.Controls.ActionSettingsControlBase
    {
        if (config.IsActionEnabled(actionId))
        {
            services.AddAction<TAction, TSettingsControl>();
        }
    }

    private void RegisterTriggerIfEnabled<TTrigger, TSettings>(IServiceCollection services, MainConfigData config,
        string triggerId)
        where TTrigger : ClassIsland.Core.Abstractions.Automation.TriggerBase
        where TSettings : ClassIsland.Core.Abstractions.Controls.TriggerSettingsControlBase
    {
        if (config.IsTriggerEnabled(triggerId))
        {
            services.AddTrigger<TTrigger, TSettings>();
        }
    }

    private void RegisterComponentIfEnabled<TComponent, TSettingsControl>(IServiceCollection services,
        MainConfigData config, string componentId)
        where TComponent : ClassIsland.Core.Abstractions.Controls.ComponentBase
        where TSettingsControl : ClassIsland.Core.Abstractions.Controls.ComponentBase
    {
        if (config.IsComponentEnabled(componentId))
        {
            services.AddComponent<TComponent, TSettingsControl>();
        }
    }

    #endregion

    #region 菜单构建

    private void BuildBaseActionTree()
    {
        var config = GlobalConstants.MainConfig!.Data;

        IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup("SystemTools 行动", "\uE079"));

        // 模拟操作
        if (HasAnyActionEnabled(config, "SystemTools.SimulateKeyboard", "SystemTools.SimulateMouse",
                "SystemTools.TypeContent", "SystemTools.WindowOperation", "SystemTools.EnterKey",
                "SystemTools.EscKey", "SystemTools.AltF4", "SystemTools.AltTab", "SystemTools.F11Key"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("模拟操作…", "\uEA0B"));
            BuildSimulationMenu(config);
        }

        // 显示设置
        if (HasAnyActionEnabled(config, "SystemTools.CloneDisplay", "SystemTools.ExtendDisplay",
                "SystemTools.InternalDisplay", "SystemTools.ExternalDisplay", "SystemTools.BlackScreenHtml"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("显示设置…", "\uF397"));
            BuildDisplayMenu(config);
        }

        // 电源选项
        if (HasAnyActionEnabled(config, "SystemTools.Shutdown", "SystemTools.LockScreen", "SystemTools.CancelShutdown"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("电源选项…", "\uEDE8"));
            BuildPowerMenu(config);
        }

        // 文件操作
        if (HasAnyActionEnabled(config, "SystemTools.Copy", "SystemTools.Move", "SystemTools.Delete"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("文件操作…", "\uE759"));
            BuildFileMenu(config);
        }

        // 系统个性化
        if (HasAnyActionEnabled(config, "SystemTools.ChangeWallpaper", "SystemTools.SwitchTheme"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("系统个性化…", "\uF42F"));
            BuildPersonalizationMenu(config);
        }

        // 实用工具
        if (HasAnyActionEnabled(config, "SystemTools.ScreenShot", "SystemTools.SetVolume", "SystemTools.KillProcess",
                "SystemTools.EnableDevice", "SystemTools.DisableDevice", "SystemTools.ShowToast"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("实用工具…", "\uE352"));
            BuildUtilityMenu(config);
        }

        // 其他工具
        if (config.IsActionEnabled("SystemTools.FullscreenClock"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("其他工具…", "\uE32C"));
            BuildOtherMenu(config);
        }

        // 独立行动项
        var standaloneActions = new List<ActionMenuTreeItem>();
        if (config.IsActionEnabled("SystemTools.TriggerCustomTrigger"))
            standaloneActions.Add(new ActionMenuTreeItem("SystemTools.TriggerCustomTrigger", "触发指定触发器", "\uEAB7"));
        if (config.IsActionEnabled("SystemTools.RestartAsAdmin"))
            standaloneActions.Add(new ActionMenuTreeItem("SystemTools.RestartAsAdmin", "重启应用为管理员身份", "\uEF53"));

        if (standaloneActions.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"].AddRange(standaloneActions);
        }
    }

    private bool HasAnyActionEnabled(MainConfigData config, params string[] actionIds)
    {
        return actionIds.Any(id => config.IsActionEnabled(id));
    }

    private void BuildSimulationMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.SimulateKeyboard"))
            items.Add(new ActionMenuTreeItem("SystemTools.SimulateKeyboard", "模拟键盘", "\uEA0F"));
        if (config.IsActionEnabled("SystemTools.SimulateMouse"))
            items.Add(new ActionMenuTreeItem("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1"));
        if (config.IsActionEnabled("SystemTools.TypeContent"))
            items.Add(new ActionMenuTreeItem("SystemTools.TypeContent", "键入内容", "\uE4BE"));
        if (config.IsActionEnabled("SystemTools.WindowOperation"))
            items.Add(new ActionMenuTreeItem("SystemTools.WindowOperation", "窗口操作", "\uF4B3"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].AddRange(items);
        }

        // 常用模拟键子菜单
        var commonKeys = new List<ActionMenuTreeItem>();
        if (config.IsActionEnabled("SystemTools.AltF4"))
            commonKeys.Add(new ActionMenuTreeItem("SystemTools.AltF4", "按下 Alt+F4", "\uEA0B"));
        if (config.IsActionEnabled("SystemTools.AltTab"))
            commonKeys.Add(new ActionMenuTreeItem("SystemTools.AltTab", "按下 Alt+Tab", "\uEA0B"));
        if (config.IsActionEnabled("SystemTools.EnterKey"))
            commonKeys.Add(new ActionMenuTreeItem("SystemTools.EnterKey", "按下 Enter 键", "\uEA0B"));
        if (config.IsActionEnabled("SystemTools.EscKey"))
            commonKeys.Add(new ActionMenuTreeItem("SystemTools.EscKey", "按下 Esc 键", "\uEA0B"));
        if (config.IsActionEnabled("SystemTools.F11Key"))
            commonKeys.Add(new ActionMenuTreeItem("SystemTools.F11Key", "按下 F11 键", "\uEA0B"));

        if (commonKeys.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].Add(new ActionMenuTreeGroup("常用模拟键", "\uEA0B"));
            IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"]["常用模拟键"].AddRange(commonKeys);
        }
    }

    private void BuildDisplayMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.CloneDisplay"))
            items.Add(new ActionMenuTreeItem("SystemTools.CloneDisplay", "复制屏幕", "\uE635"));
        if (config.IsActionEnabled("SystemTools.ExtendDisplay"))
            items.Add(new ActionMenuTreeItem("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647"));
        if (config.IsActionEnabled("SystemTools.InternalDisplay"))
            items.Add(new ActionMenuTreeItem("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE62F"));
        if (config.IsActionEnabled("SystemTools.ExternalDisplay"))
            items.Add(new ActionMenuTreeItem("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE641"));
        if (config.IsActionEnabled("SystemTools.BlackScreenHtml"))
            items.Add(new ActionMenuTreeItem("SystemTools.BlackScreenHtml", "黑屏html", "\uE643"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["显示设置…"].AddRange(items);
        }
    }

    private void BuildPowerMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.Shutdown"))
            items.Add(new ActionMenuTreeItem("SystemTools.Shutdown", "计时关机", "\uE4C4"));
        if (config.IsActionEnabled("SystemTools.CancelShutdown"))
            items.Add(new ActionMenuTreeItem("SystemTools.CancelShutdown", "取消关机计划", "\uE4CC"));
        if (config.IsActionEnabled("SystemTools.LockScreen"))
            items.Add(new ActionMenuTreeItem("SystemTools.LockScreen", "锁定屏幕", "\uEAF0"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["电源选项…"].AddRange(items);
        }
    }

    private void BuildFileMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.Copy"))
            items.Add(new ActionMenuTreeItem("SystemTools.Copy", "复制", "\uE6AB"));
        if (config.IsActionEnabled("SystemTools.Move"))
            items.Add(new ActionMenuTreeItem("SystemTools.Move", "移动", "\uE6E7"));
        if (config.IsActionEnabled("SystemTools.Delete"))
            items.Add(new ActionMenuTreeItem("SystemTools.Delete", "删除", "\uE61D"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["文件操作…"].AddRange(items);
        }
    }

    private void BuildPersonalizationMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.ChangeWallpaper"))
            items.Add(new ActionMenuTreeItem("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC"));
        if (config.IsActionEnabled("SystemTools.SwitchTheme"))
            items.Add(new ActionMenuTreeItem("SystemTools.SwitchTheme", "切换主题色", "\uF42F"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["系统个性化…"].AddRange(items);
        }
    }

    private void BuildUtilityMenu(MainConfigData config)
    {
        var items = new List<ActionMenuTreeItem>();

        if (config.IsActionEnabled("SystemTools.KillProcess"))
            items.Add(new ActionMenuTreeItem("SystemTools.KillProcess", "退出进程", "\uE0DE"));
        if (config.IsActionEnabled("SystemTools.ScreenShot"))
            items.Add(new ActionMenuTreeItem("SystemTools.ScreenShot", "屏幕截图", "\uEEE7"));
        if (config.IsActionEnabled("SystemTools.SetVolume"))
            items.Add(new ActionMenuTreeItem("SystemTools.SetVolume", "设置系统音量", "\uF013"));
        if (config.IsActionEnabled("SystemTools.ShowToast"))
            items.Add(new ActionMenuTreeItem("SystemTools.ShowToast", "拉起自定义Windows通知", "\uE3E4"));
        if (config.IsActionEnabled("SystemTools.DisableDevice"))
            items.Add(new ActionMenuTreeItem("SystemTools.DisableDevice", "禁用硬件设备", "\uE09F"));
        if (config.IsActionEnabled("SystemTools.EnableDevice"))
            items.Add(new ActionMenuTreeItem("SystemTools.EnableDevice", "启用硬件设备", "\uE0AD"));

        if (items.Count > 0)
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["实用工具…"].AddRange(items);
        }
    }

    private void BuildOtherMenu(MainConfigData config)
    {
        if (config.IsActionEnabled("SystemTools.FullscreenClock"))
        {
            IActionService.ActionMenuTree["SystemTools 行动"]["其他工具…"].Add(
                new ActionMenuTreeItem("SystemTools.FullscreenClock", "沉浸式时钟", "\uE4D2"));
        }
    }

    #endregion

    #region 设置页面分组

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

    #endregion

    private void OnAppStopping(object? sender, EventArgs e)
    {
        _logger?.LogInformation("[SystemTools]关闭插件SystemTools，保存配置...");
        GlobalConstants.MainConfig?.Save();
    }
}