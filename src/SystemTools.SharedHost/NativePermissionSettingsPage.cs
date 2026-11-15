using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;

namespace SystemTools;

[SettingsPageInfo("SystemTools.Native", "系统功能与权限")]
public sealed class NativePermissionSettingsPage : SettingsPageBase
{
    public NativePermissionSettingsPage(NativeDesktopServices services)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "系统功能与权限", FontSize = 24 });
        Add("系统音量", services.Volume);
        Add("空闲检测", services.Idle);
        Add("屏幕截图", services.ScreenCapture);
        Add("USB 设备", services.UsbDevices);
        Add("键盘输入", services.Keyboard);
        Add("全局热键", services.Hotkeys);
        Add("窗口操作", services.Windows);
        Add("桌面壁纸", services.Wallpaper);
        Add("排除自身的背景捕获", services.Advanced.Background);
        var theme = new CheckBox { Content = "根据主界面背后亮度调整主题", IsChecked = Shared.GlobalConstants.MainConfig!.Data.AutoSwitchClassIslandTheme };
        theme.IsEnabled = services.Advanced.Background?.Probe().CanExecute == true;
        theme.IsCheckedChanged += (_, _) => { Shared.GlobalConstants.MainConfig!.Data.AutoSwitchClassIslandTheme = theme.IsChecked == true; Shared.GlobalConstants.MainConfig.Save(); };
        panel.Children.Add(theme);
        Add("文字识别", services.Advanced.TextRecognition);
        var occlusion = new CheckBox { Content = "遮挡文字时隐藏主界面（无文字后自动恢复）", IsChecked = Shared.GlobalConstants.MainConfig!.Data.AutoHideMainWindowWhenOccluded };
        occlusion.IsEnabled = services.Advanced.Background?.Probe().CanExecute == true && services.Advanced.TextRecognition?.Probe().CanExecute == true;
        occlusion.IsCheckedChanged += (_, _) => { Shared.GlobalConstants.MainConfig!.Data.AutoHideMainWindowWhenOccluded = occlusion.IsChecked == true; Shared.GlobalConstants.MainConfig.Save(); };
        panel.Children.Add(occlusion);
        Add("原生窗口材质", services.Advanced.Material);
        var floatingGlass = new CheckBox { Content = "悬浮窗使用系统玻璃材质", IsChecked = Shared.GlobalConstants.MainConfig!.Data.FloatingWindowAppearanceStyle == 1 };
        floatingGlass.IsEnabled = services.Advanced.Material?.Probe().CanExecute == true;
        floatingGlass.IsCheckedChanged += (_, _) => { Shared.GlobalConstants.MainConfig!.Data.FloatingWindowAppearanceStyle = floatingGlass.IsChecked == true ? 1 : 0; Shared.GlobalConstants.MainConfig.Save(); };
        var chatGlass = new CheckBox { Content = "对话窗口使用系统玻璃材质", IsChecked = Shared.GlobalConstants.MainConfig!.Data.AiConversationFloatingWindowStyle == 1 };
        chatGlass.IsEnabled = floatingGlass.IsEnabled;
        chatGlass.IsCheckedChanged += (_, _) => { Shared.GlobalConstants.MainConfig!.Data.AiConversationFloatingWindowStyle = chatGlass.IsChecked == true ? 1 : 0; Shared.GlobalConstants.MainConfig.Save(); };
        panel.Children.Add(floatingGlass); panel.Children.Add(chatGlass);
        Content = new ScrollViewer { Content = panel };
        void Add(string name, INativeCapability? capability)
        {
            if (capability is null) return;
            var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            void Refresh() { var value = capability.Probe(); status.Text = name + "：" + (value.CanExecute ? "可用" : value.Reason); }
            Refresh(); panel.Children.Add(status);
            var refresh = new Button { Content = "重新检查" };
            refresh.Click += (_, _) => Refresh(); panel.Children.Add(refresh);
            if (capability is IPermissionRequest permission)
            {
                var request = new Button { Content = "为“" + name + "”申请系统权限" };
                request.Click += (_, _) => { permission.RequestAccess(); Refresh(); };
                panel.Children.Add(request);
            }
        }
    }
}
