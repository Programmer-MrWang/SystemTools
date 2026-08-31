using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Threading.Tasks;
using SystemTools.ConfigHandlers;
using SystemTools.Services;
using SystemTools.Shared;

using ClassIsland.Shared;
namespace SystemTools;

[SettingsPageInfo("systemtools.settings.more", "更多功能选项…", "\uE28E", "\uE28E", true)]
public partial class MoreFeaturesOptionsSettingsPage : SettingsPageBase
{
    public MainConfigData Config => GlobalConstants.MainConfig!.Data;

    public MoreFeaturesOptionsSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void AutoMatchThemeToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.AutoSwitchClassIslandTheme = toggleSwitch.IsChecked == true;
        }

        var service = ClassIsland.Shared.IAppHost.GetService<AdaptiveThemeSyncService>();
        service.ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }

    private void AutoOpenUsbToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.AutoOpenUsbDriveOnInsert = toggleSwitch.IsChecked == true;
        }

        var service = ClassIsland.Shared.IAppHost.GetService<UsbAutoPlayService>();
        service.ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }

    private void VirtualAfterSchoolToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.VirtualAfterSchoolEnabled = toggleSwitch.IsChecked == true;
        }

        ClassIsland.Shared.IAppHost.GetService<VirtualAfterSchoolService>().ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }

    private void AutoHideMainWindowOnTextToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.AutoHideMainWindowWhenOccluded = toggleSwitch.IsChecked == true;
        }

        ClassIsland.Shared.IAppHost.GetService<MainWindowTextOcclusionService>().ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }

    private void AutoCleanupMemoryToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.AutoCleanupClassIslandMemory = toggleSwitch.IsChecked == true;
        }

        var service = ClassIsland.Shared.IAppHost.GetService<ClassIslandMemoryAutoCleanupService>();
        service.ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }

    private async void AutoCleanupSystemMemoryToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch)
        {
            return;
        }

        Config.AutoCleanupSystemMemory = toggleSwitch.IsChecked == true;

        var service = ClassIsland.Shared.IAppHost.GetService<SystemMemoryCleanupService>();
        service.ApplyConfig();
        GlobalConstants.MainConfig?.Save();

        if (Config.AutoCleanupSystemMemory && !service.IsRunningAsAdministrator)
        {
            await ShowMemoryCleanupMessageAsync(
                "需要管理员权限",
                "开关设置已保存，但当前 ClassIsland 未以管理员身份运行，本次运行不会自动清理。请以管理员身份重启 ClassIsland 后使用此功能。");
        }
    }

    private async void CleanSystemMemoryNow_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var service = ClassIsland.Shared.IAppHost.GetService<SystemMemoryCleanupService>();
        if (!service.IsRunningAsAdministrator)
        {
            await ShowMemoryCleanupMessageAsync(
                "需要管理员权限",
                "请先以管理员身份重启 ClassIsland，再执行一键清理。");
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "正在清理…";

        try
        {
            var result = await service.CleanupNowAsync();
            var memoryChange = result.BeforeMemoryLoadPercent is int before && result.AfterMemoryLoadPercent is int after
                ? $"物理内存占用：{before}% → {after}%\n"
                : string.Empty;
            var failureDetails = result.Failures.Count > 0
                ? $"\n\n未成功的项目：\n- {string.Join("\n- ", result.Failures)}"
                : string.Empty;

            await ShowMemoryCleanupMessageAsync(
                result.Succeeded ? "清理完成" : "清理未完全成功",
                $"{memoryChange}可用物理内存增加：{FormatByteSize(result.AvailableMemoryIncreaseBytes)}{failureDetails}");
        }
        catch (Exception ex)
        {
            await ShowMemoryCleanupMessageAsync("清理失败", ex.Message);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private static async Task ShowMemoryCleanupMessageAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };

        await dialog.ShowAsync();
    }

    private static string FormatByteSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }


}
