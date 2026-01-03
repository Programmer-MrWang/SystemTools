using AvaloniaEdit.Utils;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Actions;
using SystemTools.Controls;

namespace SystemTools;

public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        //行动树
        IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup("SystemTools 行动", "\uE079")); 
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("模拟操作…", "\uEA0B"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("显示设置…", "\uF397"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("电源选项…", "\uEDE8"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("文件操作…", "\uE759"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("系统个性化…", "\uF42F"));
        IActionService.ActionMenuTree["SystemTools 行动"].Add(new ActionMenuTreeGroup("实用工具…", "\uE352"));

        IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].Add(new ActionMenuTreeGroup("常用模拟键", "\uEA0B"));

        IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"].AddRange([
            new ActionMenuTreeItem("SystemTools.SimulateKeyboard", "模拟键盘", "\uEA0F"),
            new ActionMenuTreeItem("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1"),
            new ActionMenuTreeItem("SystemTools.TypeContent", "键入内容", "\uE4BE"),
            new ActionMenuTreeItem("SystemTools.WindowOperation", "窗口操作", "\uF4B3")
            //new ActionMenuTreeItem("SystemTools.ClickSimulation", "模拟点击", "\uE5C1")
        ]);

            IActionService.ActionMenuTree["SystemTools 行动"]["模拟操作…"]["常用模拟键"].AddRange([
                new ActionMenuTreeItem("SystemTools.AltF4", "按下 Alt+F4", "\uEA0B"),
                new ActionMenuTreeItem("SystemTools.AltTab", "按下 Alt+Tab", "\uEA0B"),
                new ActionMenuTreeItem("SystemTools.EnterKey", "按下 Enter 键", "\uEA0B"),
                new ActionMenuTreeItem("SystemTools.EscKey", "按下 Esc 键", "\uEA0B"),
                new ActionMenuTreeItem("SystemTools.F11Key", "按下 F11 键", "\uEA0B")
            ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["显示设置…"].AddRange([
            new ActionMenuTreeItem("SystemTools.CloneDisplay", "复制屏幕", "\uE635"),
            new ActionMenuTreeItem("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647"),
            new ActionMenuTreeItem("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE62F"),
            new ActionMenuTreeItem("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE641"),
            new ActionMenuTreeItem("SystemTools.BlackScreenHtml", "黑屏html", "\uE643")
        ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["电源选项…"].AddRange([
            new ActionMenuTreeItem("SystemTools.Shutdown", "计时关机", "\uE4C4"),
            new ActionMenuTreeItem("SystemTools.CancelShutdown", "取消关机计划", "\uE4CC"),
            new ActionMenuTreeItem("SystemTools.LockScreen", "锁定屏幕", "\uEAF0")
        ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["文件操作…"].AddRange([
            new ActionMenuTreeItem("SystemTools.Copy", "复制", "\uE6AB"),
            new ActionMenuTreeItem("SystemTools.Move", "移动", "\uE6E7"),
            new ActionMenuTreeItem("SystemTools.Delete", "删除", "\uE61D")
        ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["系统个性化…"].AddRange([
            new ActionMenuTreeItem("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC"),
            new ActionMenuTreeItem("SystemTools.SwitchTheme", "切换主题色", "\uF42F")
        ]);

        IActionService.ActionMenuTree["SystemTools 行动"]["实用工具…"].AddRange([
            new ActionMenuTreeItem("SystemTools.FullscreenClock", "沉浸式时钟", "\uE4D2")
        ]);

        //模拟操作…
        services.AddAction<EnterKeyAction>();
        services.AddAction<EscAction>();
        services.AddAction<AltF4Action>();
        services.AddAction<AltTabAction>();
        services.AddAction<F11Action>();

        services.AddAction<SimulateKeyboardAction, SimulateKeyboardSettingsControl>();
        services.AddAction<SimulateMouseAction, SimulateMouseSettingsControl>();
        services.AddAction<TypeContentAction, TypeContentSettingsControl>();
        services.AddAction<WindowOperationAction, WindowOperationSettingsControl>();
        //services.AddAction<ClickSimulationAction, ClickSimulationSettingsControl>();

        //显示设置…
        services.AddAction<CloneDisplayAction>();
        services.AddAction<ExtendDisplayAction>();
        services.AddAction<InternalDisplayAction>();
        services.AddAction<ExternalDisplayAction>();
        services.AddAction<BlackScreenHtmlAction>();

        //电源选项…
        services.AddAction<ShutdownAction, ShutdownSettingsControl>();
        services.AddAction<LockScreenAction>();
        services.AddAction<CancelShutdownAction>();

        //文件操作…
        services.AddAction<CopyAction, CopySettingsControl>();
        services.AddAction<MoveAction, MoveSettingsControl>();
        services.AddAction<DeleteAction, DeleteSettingsControl>();

        //系统个性化…
        services.AddAction<ChangeWallpaperAction, ChangeWallpaperSettingsControl>();
        services.AddAction<SwitchThemeAction, ThemeSettingsControl>();

        //实用工具…
        services.AddAction<FullscreenClockAction, FullscreenClockSettingsControl>();    


    }
}