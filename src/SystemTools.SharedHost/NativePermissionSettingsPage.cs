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
