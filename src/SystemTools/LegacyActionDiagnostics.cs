using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Contracts;

namespace SystemTools;

internal static class LegacyActionDiagnostics
{
    internal static void Register(IServiceCollection services)
    {
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ActionFlowExecutionConfirmation")) services.AddAction<Unavailable0>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AdjustScreenBrightness")) services.AddAction<Unavailable1>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AdvancedShutdown")) services.AddAction<Unavailable2>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AltF4")) services.AddAction<Unavailable3>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AltTab")) services.AddAction<Unavailable4>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AutoHideMainWindowWhenOccluded")) services.AddAction<Unavailable5>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AutoOpenUsbDriveOnInsert")) services.AddAction<Unavailable6>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.AutoSwitchClassIslandTheme")) services.AddAction<Unavailable7>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.BackgroundPlayAudio")) services.AddAction<Unavailable8>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.BlackScreenHtml")) services.AddAction<Unavailable9>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.CancelShutdown")) services.AddAction<Unavailable10>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ChangeWallpaper")) services.AddAction<Unavailable11>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ClearAllNotifications")) services.AddAction<Unavailable12>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.CloneDisplay")) services.AddAction<Unavailable13>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.Copy")) services.AddAction<Unavailable14>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.CtrlZ")) services.AddAction<Unavailable15>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.Delete")) services.AddAction<Unavailable16>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.DisableDevice")) services.AddAction<Unavailable17>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.DisableMouse")) services.AddAction<Unavailable18>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.EnableDevice")) services.AddAction<Unavailable19>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.EnableMouse")) services.AddAction<Unavailable20>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.EnableVoiceWakeAi")) services.AddAction<Unavailable21>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.EnterKey")) services.AddAction<Unavailable22>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.EscKey")) services.AddAction<Unavailable23>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ExtendDisplay")) services.AddAction<Unavailable24>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ExternalDisplay")) services.AddAction<Unavailable25>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.F11Key")) services.AddAction<Unavailable26>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ImmediateRestart")) services.AddAction<Unavailable27>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ImmediateShutdown")) services.AddAction<Unavailable28>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.InternalDisplay")) services.AddAction<Unavailable29>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.KillProcess")) services.AddAction<Unavailable30>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.LoadTemporaryClassPlan")) services.AddAction<Unavailable31>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.LockScreen")) services.AddAction<Unavailable32>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.Move")) services.AddAction<Unavailable33>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.OpenAppSettings")) services.AddAction<Unavailable34>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.OpenClassSwapWindow")) services.AddAction<Unavailable35>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.OpenProfileEditor")) services.AddAction<Unavailable36>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.RestartAsAdmin")) services.AddAction<Unavailable37>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ScreenShot")) services.AddAction<Unavailable38>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SetVolume")) services.AddAction<Unavailable39>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ShowAiChatDialog")) services.AddAction<Unavailable40>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ShowDesktop")) services.AddAction<Unavailable41>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ShowFloatingWindow")) services.AddAction<Unavailable42>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ShowToast")) services.AddAction<Unavailable43>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.Shutdown")) services.AddAction<Unavailable44>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SimulateKeyboard")) services.AddAction<Unavailable45>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SimulateKeyCombination")) services.AddAction<Unavailable46>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SimulateMouse")) services.AddAction<Unavailable47>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.Sleep")) services.AddAction<Unavailable48>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SwitchFloatingWindowTheme")) services.AddAction<Unavailable49>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SwitchSystemAccentColor")) services.AddAction<Unavailable50>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.SwitchTheme")) services.AddAction<Unavailable51>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ToggleFloatingWindowLayer")) services.AddAction<Unavailable52>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ToggleFloatingWindowProfile")) services.AddAction<Unavailable53>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.ToggleWorkflow")) services.AddAction<Unavailable54>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.TriggerCustomTrigger")) services.AddAction<Unavailable55>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.TypeContent")) services.AddAction<Unavailable56>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.WakeUpVoiceConversationAi")) services.AddAction<Unavailable57>();
        if (!IActionService.ActionInfos.ContainsKey("SystemTools.WindowOperation")) services.AddAction<Unavailable58>();
    }
}
[ActionInfo("SystemTools.ActionFlowExecutionConfirmation", "行动流执行确认", "", false)]
internal sealed class Unavailable0(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ActionFlowExecutionConfirmation"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AdjustScreenBrightness", "调整屏幕亮度", "", false)]
internal sealed class Unavailable1(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AdjustScreenBrightness"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AdvancedShutdown", "高级计时关机", "", false)]
internal sealed class Unavailable2(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AdvancedShutdown"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AltF4", "按下 Alt+F4", "", false)]
internal sealed class Unavailable3(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AltF4"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AltTab", "按下 Alt+Tab", "", false)]
internal sealed class Unavailable4(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AltTab"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AutoHideMainWindowWhenOccluded", "遮挡文字时隐藏主界面", "", false)]
internal sealed class Unavailable5(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AutoHideMainWindowWhenOccluded"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AutoOpenUsbDriveOnInsert", "自动播放", "", false)]
internal sealed class Unavailable6(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AutoOpenUsbDriveOnInsert"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.AutoSwitchClassIslandTheme", "自动切换 ClassIsland 主题", "", false)]
internal sealed class Unavailable7(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.AutoSwitchClassIslandTheme"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.BackgroundPlayAudio", "后台播放音频", "", false)]
internal sealed class Unavailable8(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.BackgroundPlayAudio"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.BlackScreenHtml", "黑屏html", "", false)]
internal sealed class Unavailable9(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.BlackScreenHtml"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.CancelShutdown", "取消关机计划", "", false)]
internal sealed class Unavailable10(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.CancelShutdown"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ChangeWallpaper", "切换壁纸", "", false)]
internal sealed class Unavailable11(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ChangeWallpaper"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ClearAllNotifications", "清除全部提醒", "", false)]
internal sealed class Unavailable12(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ClearAllNotifications"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.CloneDisplay", "复制屏幕", "", false)]
internal sealed class Unavailable13(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.CloneDisplay"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.Copy", "复制", "", false)]
internal sealed class Unavailable14(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.Copy"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.CtrlZ", "按下 Ctrl+Z", "", false)]
internal sealed class Unavailable15(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.CtrlZ"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.Delete", "删除", "", false)]
internal sealed class Unavailable16(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.Delete"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.DisableDevice", "禁用硬件设备", "", false)]
internal sealed class Unavailable17(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.DisableDevice"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.DisableMouse", "禁用鼠标", "", false)]
internal sealed class Unavailable18(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.DisableMouse"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.EnableDevice", "启用硬件设备", "", false)]
internal sealed class Unavailable19(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.EnableDevice"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.EnableMouse", "启用鼠标", "", false)]
internal sealed class Unavailable20(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.EnableMouse"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.EnableVoiceWakeAi", "启用语音唤醒 AI", "", false)]
internal sealed class Unavailable21(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.EnableVoiceWakeAi"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.EnterKey", "按下 Enter 键", "", false)]
internal sealed class Unavailable22(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.EnterKey"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.EscKey", "按下 Esc 键", "", false)]
internal sealed class Unavailable23(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.EscKey"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ExtendDisplay", "扩展屏幕", "", false)]
internal sealed class Unavailable24(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ExtendDisplay"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ExternalDisplay", "仅第二屏幕", "", false)]
internal sealed class Unavailable25(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ExternalDisplay"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.F11Key", "按下 F11 键", "", false)]
internal sealed class Unavailable26(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.F11Key"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ImmediateRestart", "立即重启", "", false)]
internal sealed class Unavailable27(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ImmediateRestart"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ImmediateShutdown", "立即关机", "", false)]
internal sealed class Unavailable28(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ImmediateShutdown"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.InternalDisplay", "仅电脑屏幕", "", false)]
internal sealed class Unavailable29(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.InternalDisplay"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.KillProcess", "退出进程", "", false)]
internal sealed class Unavailable30(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.KillProcess"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.LoadTemporaryClassPlan", "加载临时课表", "", false)]
internal sealed class Unavailable31(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.LoadTemporaryClassPlan"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.LockScreen", "锁定屏幕", "", false)]
internal sealed class Unavailable32(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.LockScreen"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.Move", "移动", "", false)]
internal sealed class Unavailable33(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.Move"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.OpenAppSettings", "打开应用设置", "", false)]
internal sealed class Unavailable34(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.OpenAppSettings"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.OpenClassSwapWindow", "打开换课窗口", "", false)]
internal sealed class Unavailable35(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.OpenClassSwapWindow"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.OpenProfileEditor", "打开档案编辑", "", false)]
internal sealed class Unavailable36(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.OpenProfileEditor"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.RestartAsAdmin", "重启应用为管理员身份", "", false)]
internal sealed class Unavailable37(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.RestartAsAdmin"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ScreenShot", "屏幕截图", "", false)]
internal sealed class Unavailable38(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ScreenShot"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SetVolume", "设置系统音量", "", false)]
internal sealed class Unavailable39(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SetVolume"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ShowAiChatDialog", "显示AI对话框", "", false)]
internal sealed class Unavailable40(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ShowAiChatDialog"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ShowDesktop", "显示桌面", "", false)]
internal sealed class Unavailable41(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ShowDesktop"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ShowFloatingWindow", "显示悬浮窗", "", false)]
internal sealed class Unavailable42(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ShowFloatingWindow"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ShowToast", "拉起自定义Windows通知", "", false)]
internal sealed class Unavailable43(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ShowToast"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.Shutdown", "计时关机", "", false)]
internal sealed class Unavailable44(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.Shutdown"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SimulateKeyboard", "模拟键盘", "", false)]
internal sealed class Unavailable45(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SimulateKeyboard"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SimulateKeyCombination", "模拟组合键", "", false)]
internal sealed class Unavailable46(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SimulateKeyCombination"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SimulateMouse", "模拟鼠标", "", false)]
internal sealed class Unavailable47(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SimulateMouse"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.Sleep", "睡眠", "", false)]
internal sealed class Unavailable48(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.Sleep"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SwitchFloatingWindowTheme", "切换悬浮窗主题", "", false)]
internal sealed class Unavailable49(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SwitchFloatingWindowTheme"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SwitchSystemAccentColor", "切换系统强调色", "", false)]
internal sealed class Unavailable50(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SwitchSystemAccentColor"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.SwitchTheme", "切换主题色", "", false)]
internal sealed class Unavailable51(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.SwitchTheme"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ToggleFloatingWindowLayer", "切换悬浮窗层级", "", false)]
internal sealed class Unavailable52(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ToggleFloatingWindowLayer"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ToggleFloatingWindowProfile", "切换悬浮窗配置方案", "", false)]
internal sealed class Unavailable53(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ToggleFloatingWindowProfile"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.ToggleWorkflow", "开关自动化", "", false)]
internal sealed class Unavailable54(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.ToggleWorkflow"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.TriggerCustomTrigger", "触发指定触发器", "", false)]
internal sealed class Unavailable55(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.TriggerCustomTrigger"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.TypeContent", "键入内容", "", false)]
internal sealed class Unavailable56(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.TypeContent"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.WakeUpVoiceConversationAi", "唤醒语音对话 AI", "", false)]
internal sealed class Unavailable57(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.WakeUpVoiceConversationAi"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
[ActionInfo("SystemTools.WindowOperation", "窗口操作", "", false)]
internal sealed class Unavailable58(FeatureCatalog catalog) : ActionBase
{
    protected override Task OnInvoke() { catalog.EnsureExecutable("SystemTools.WindowOperation"); throw new InvalidOperationException("功能实现未注册，请重启或重新安装插件。"); }
}
