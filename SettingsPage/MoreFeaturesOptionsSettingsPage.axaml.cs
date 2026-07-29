using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Linq;
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
    public ObservableCollection<string> AvailableAiModels { get; } = [];

    public MoreFeaturesOptionsSettingsPage()
    {
        InitializeComponent();
        DataContext = this;

        if (!string.IsNullOrWhiteSpace(Config.AiModel))
        {
            AvailableAiModels.Add(Config.AiModel);
        }
    }

    private async void AiServiceToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch)
        {
            return;
        }

        if (toggleSwitch.IsChecked != true)
        {
            Config.EnableAiService = false;
            GlobalConstants.MainConfig?.Save();
            RestartClassIsland();
            return;
        }

        toggleSwitch.IsEnabled = false;
        var accepted = await ShowAiServiceAgreementAsync();
        toggleSwitch.IsEnabled = true;
        if (!accepted)
        {
            toggleSwitch.IsChecked = false;
            return;
        }

        Config.EnableAiService = true;
        GlobalConstants.MainConfig?.Save();
        RestartClassIsland();
    }

    private async Task<bool> ShowAiServiceAgreementAsync()
    {
        var agreementCheckBox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "我已阅读本协议，自愿承担使用AI带来的不确定风险",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 520
            }
        };
        var dialog = new FAContentDialog
        {
            Title = "AI 服务使用协议",
            Content = new StackPanel
            {
                Spacing = 16,
                MaxWidth = 540,
                Children =
                {
                    new TextBlock
                    {
                        Text = "此“AI 服务”是由SystemTools插件提供的外接 API Key 的AI辅助功能，与ClassIsland软件无关；\n" +
                               "AI的回复和相关服务由对应提供商提供，与本插件及开发者无关；\n" +
                               "须知应当正确使用AI，合理规避不确定性风险，明辨AI提供的相关回复。",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    agreementCheckBox
                }
            },
            CloseButtonText = "取消",
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };

        agreementCheckBox.IsCheckedChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = agreementCheckBox.IsChecked == true;

        return await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary;
    }

    private async void GetAiModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "正在获取...";

        try
        {
            var service = ClassIsland.Shared.IAppHost.GetService<IOpenAiCompatibleService>();
            var models = await service.GetModelsAsync();
            if (models.Count == 0)
            {
                await ShowAiMessageAsync("未找到模型", "供应商返回了空的模型列表。");
                return;
            }

            var previousModel = Config.AiModel;
            AvailableAiModels.Clear();
            foreach (var model in models)
            {
                AvailableAiModels.Add(model);
            }

            Config.AiModel = models.Contains(previousModel, StringComparer.Ordinal)
                ? previousModel
                : models[0];
            GlobalConstants.MainConfig?.Save();

            await ShowAiMessageAsync("获取成功", $"已获取 {models.Count} 个可用模型。");
        }
        catch (Exception ex)
        {
            await ShowAiMessageAsync("获取模型失败", ex.Message);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = Config.EnableAiService;
        }
    }

    private static async Task ShowAiMessageAsync(string title, string message)
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


    private void RestartClassIsland()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath?.Replace(".dll", ".exe"),
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add("-m");

            var args = Environment.GetCommandLineArgs().ToList();
            args.RemoveAt(0);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            System.Diagnostics.Process.Start(startInfo);
            ClassIsland.Core.AppBase.Current.Stop();
        }
        catch
        {
            // Silently fail if restart is not possible
        }
    }


}
