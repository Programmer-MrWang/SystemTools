using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Helpers.UI;
using System;
using System.IO;
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

    private async void ExportProfileExcelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        // 避免在导出过程中重复触发。
        var button = sender as Button;
        if (button != null)
        {
            button.IsEnabled = false;
        }

        try
        {
            var exportedPath = await RunProfileExcelExportAsync(owner);
            if (!string.IsNullOrWhiteSpace(exportedPath))
            {
                this.ShowSuccessToast($"已导出到 {exportedPath}");
            }
        }
        catch (Exception ex)
        {
            await ShowMemoryCleanupMessageAsync("导出失败", ex.Message);
        }
        finally
        {
            if (button != null)
            {
                button.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// 在设置窗口内显示导出选项对话框，随后选择输出位置并写出文件。
    /// </summary>
    /// <returns>导出成功时保存的文件或文件夹路径，否则为 <c>null</c>。</returns>
    private async Task<string?> RunProfileExcelExportAsync(Window owner)
    {
        var exporter = IAppHost.GetService<ClassIslandProfileExcelExporter>();

        ProfileExportSource source;
        try
        {
            source = exporter.LoadCurrentProfile();
        }
        catch (Exception ex)
        {
            await ShowMemoryCleanupMessageAsync("无法读取档案", ex.Message);
            return null;
        }

        var optionsControl = new Controls.ProfileExcelExportOptionsControl();
        optionsControl.Initialize(source);
        ProfileExcelExportOptions? options = null;

        var dialog = new FAContentDialog
        {
            Title = "导出课程档案为 Excel 表格",
            Content = optionsControl,
            PrimaryButtonText = "导出",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary
        };

        // 校验不通过时阻止对话框关闭，让用户继续调整选择。
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!optionsControl.TryBuildOptions(out var built))
            {
                args.Cancel = true;
                return;
            }

            options = built;
        };

        if (await dialog.ShowAsync(owner) != FAContentDialogResult.Primary || options == null)
        {
            return null;
        }

        // 对话框关闭后再选择输出位置，避免在对话内容之上叠加系统文件选择框。
        return await WriteExportFilesAsync(owner, exporter, source, options);
    }

    private async Task<string?> WriteExportFilesAsync(
        Window owner,
        ClassIslandProfileExcelExporter exporter,
        ProfileExportSource source,
        ProfileExcelExportOptions options)
    {
        var extension = options.UseXlsx ? ".xlsx" : ".xls";
        var fileType = extension == ".xlsx"
            ? new FilePickerFileType("Excel 工作簿") { Patterns = ["*.xlsx"] }
            : new FilePickerFileType("Excel 97-2003 工作簿") { Patterns = ["*.xls"] };

        var filePicker = ClassIsland.Platforms.Abstraction.PlatformServices.FilePickerService;
        var useSeparateFiles = options.SeparateFiles && options.TargetCount >= 2;

        if (useSeparateFiles)
        {
            var folders = await filePicker.OpenFoldersPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择保存导出文件的文件夹",
                AllowMultiple = false
            }, owner);
            if (folders.Count == 0 || string.IsNullOrWhiteSpace(folders[0]) || filePicker.IsBookmark(folders[0]))
            {
                return null;
            }

            var folder = Path.Combine(folders[0], ClassIslandProfileExcelExporter.BuildSuggestedFileName(source));
            var files = await Task.Run(() => exporter.BuildExportFiles(source, options));
            Directory.CreateDirectory(folder);
            foreach (var file in files)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, file.FileName), file.Content);
            }

            return folder;
        }

        var path = await filePicker.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存课表表格文件",
            SuggestedFileName = ClassIslandProfileExcelExporter.BuildSuggestedFileName(source) + extension,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [fileType],
            ShowOverwritePrompt = true
        }, owner);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            path += extension;
        }

        var content = await Task.Run(() => exporter.BuildWorkbook(source, options));
        await File.WriteAllBytesAsync(path, content);
        return path;
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

    private void WallpaperAsAccentColorToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            Config.WallpaperAsAccentColorSource = toggleSwitch.IsChecked == true;
        }

        ClassIsland.Shared.IAppHost.GetService<WallpaperAccentColorService>().ApplyConfig();
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
