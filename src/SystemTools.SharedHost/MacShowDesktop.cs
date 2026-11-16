using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Common.Automation;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools;

[ActionInfo("SystemTools.ShowDesktop", "显示桌面", "\uE079", false)]
public sealed class MacShowDesktopAction(NativeDesktopServices native, FeatureCatalog catalog, MainConfigHandler config) : ActionBase<ShortcutKeyNotificationSettings>
{
    internal static Capability Probe(IKeyboardInput? keyboard, MainConfigData config)
    {
        if (keyboard is not ISystemShortcutInput shortcut)
            return new(CapabilityState.Unsupported, "当前 Mac 输入后端不支持系统快捷键，请安装完整的新版本插件。");
        try { MacSystemShortcut.Validate(config.MacShowDesktopShortcut); }
        catch (ArgumentException error) { return new(CapabilityState.Disabled, error.Message); }
        return shortcut.ProbeShortcut();
    }

    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        var keys = MacSystemShortcut.Validate(config.Data.MacShowDesktopShortcut);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await PortableKeyboard.ExecuteSystemShortcutAsync(native.Keyboard!, keys, stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "已发送显示桌面快捷键");
    }
}

public sealed class MacShowDesktopActionSettingsControl : ActionSettingsControlBase<ShortcutKeyNotificationSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var notify = new CheckBox { Content = "当发送快捷键时发出提醒", IsChecked = Settings.NotifyOnExecute };
        notify.IsCheckedChanged += (_, _) => Settings.NotifyOnExecute = notify.IsChecked == true;
        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "请先在插件的“系统功能与权限”页保存与 macOS“显示桌面”一致的快捷键，并允许辅助功能权限。重复执行可能恢复窗口。",
                    TextWrapping = TextWrapping.Wrap
                },
                notify
            }
        };
    }
}

internal sealed class MacDesktopShortcutEditor : UserControl
{
    internal MacDesktopShortcutEditor(MainConfigHandler config)
    {
        var stored = config.Data.MacShowDesktopShortcut ?? [];
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Mac 显示桌面快捷键", FontSize = 18 });
        panel.Children.Add(new TextBlock
        {
            Text = "请与系统设置 → 键盘 → 键盘快捷键 → 调度中心中的“显示桌面”保持一致。默认未配置；保存后立即生效。",
            TextWrapping = TextWrapping.Wrap
        });
        var modifiers = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        var choices = new Dictionary<string, CheckBox>();
        foreach (var key in MacSystemShortcut.Modifiers)
        {
            var choice = new CheckBox
            {
                Content = key switch { "Alt" => "Option ⌥", "Meta" => "Command ⌘", "Control" => "Control ⌃", "Shift" => "Shift ⇧", _ => "Fn / 🌐" },
                IsChecked = stored.Contains(key)
            };
            choices.Add(key, choice); modifiers.Children.Add(choice);
        }
        panel.Children.Add(modifiers);
        var main = new ComboBox
        {
            ItemsSource = MacSystemShortcut.MainKeys.ToArray(),
            PlaceholderText = "选择普通按键",
            SelectedItem = stored.FirstOrDefault(key => MacSystemShortcut.MainKeys.Contains(key)),
            MinWidth = 200
        };
        panel.Children.Add(main);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void ShowStored() => status.Text = config.Data.MacShowDesktopShortcut is { Count: > 0 } saved
            ? "当前已保存：" + string.Join(" + ", saved.Select(key => key switch { "Alt" => "Option", "Meta" => "Command", _ => key }))
            : "尚未配置；显示桌面行动会提示先配置快捷键。";
        bool Save(List<string> keys)
        {
            var previous = config.Data.MacShowDesktopShortcut ?? [];
            config.Data.MacShowDesktopShortcut = keys;
            try { config.Save(); ShowStored(); return true; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                config.Data.MacShowDesktopShortcut = previous;
                status.Text = "保存未完成，原配置已保留；请检查配置文件是否被其他程序修改。";
                return false;
            }
        }
        var save = new Button { Content = "保存显示桌面快捷键" };
        save.Click += (_, _) =>
        {
            try
            {
                var keys = choices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
                if (main.SelectedItem is string key) keys.Add(key);
                Save(MacSystemShortcut.Validate(keys).ToList());
            }
            catch (ArgumentException error) { status.Text = error.Message; }
        };
        var clear = new Button { Content = "清除快捷键配置" };
        clear.Click += (_, _) =>
        {
            if (!Save([])) return;
            foreach (var choice in choices.Values) choice.IsChecked = false;
            main.SelectedIndex = -1;
        };
        panel.Children.Add(save); panel.Children.Add(clear); panel.Children.Add(status);
        panel.Children.Add(new TextBlock
        {
            Text = "需要允许 ClassIsland 使用辅助功能。执行前请松开键盘按键；重复执行可能按系统行为恢复窗口。此处只保存快捷键，不会修改系统快捷键设置。",
            TextWrapping = TextWrapping.Wrap
        });
        ShowStored(); Content = panel;
    }
}
