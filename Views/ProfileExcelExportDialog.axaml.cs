using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassIsland.Core.Controls;
using ClassIsland.Platforms.Abstraction;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Views;

/// <summary>
/// 导出课程档案为 Excel 表格的选项对话框。
/// </summary>
public partial class ProfileExcelExportDialog : MyWindow
{
    private readonly ILogger<ProfileExcelExportDialog>? _logger =
        IAppHost.TryGetService<ILogger<ProfileExcelExportDialog>>();

    private readonly ClassIslandProfileExcelExporter _exporter =
        IAppHost.GetService<ClassIslandProfileExcelExporter>();

    private ProfileExportSource? _source;
    private bool _isExporting;

    /// <summary>导出成功时保存的文件或文件夹路径，否则为 <c>null</c>。</summary>
    public string? ExportedFilePath { get; private set; }

    public ProfileExcelExportDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private CheckBox? ClassPlanCheckBox => this.FindControl<CheckBox>("ClassPlanCheckBoxElement");

    private CheckBox? TimeLayoutCheckBox => this.FindControl<CheckBox>("TimeLayoutCheckBoxElement");

    private CheckBox? SubjectCheckBox => this.FindControl<CheckBox>("SubjectCheckBoxElement");

    private RadioButton? XlsxRadioButton => this.FindControl<RadioButton>("XlsxRadioButtonElement");

    private RadioButton? XlsRadioButton => this.FindControl<RadioButton>("XlsRadioButtonElement");

    private Border? StorageModePanel => this.FindControl<Border>("StorageModePanelElement");

    private RadioButton? SingleFileModeRadioButton =>
        this.FindControl<RadioButton>("SingleFileModeRadioButtonElement");

    private RadioButton? MultipleFilesModeRadioButton =>
        this.FindControl<RadioButton>("MultipleFilesModeRadioButtonElement");

    private Button? ExportButton => this.FindControl<Button>("ExportButtonElement");

    private Button? CancelButton => this.FindControl<Button>("CancelButtonElement");

    private TextBlock? MessageTextBlock => this.FindControl<TextBlock>("MessageTextBlockElement");

    private TextBlock? ProfileNameTextBlock => this.FindControl<TextBlock>("ProfileNameTextBlockElement");

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (ExportButton != null)
        {
            ExportButton.Click += OnExportButtonClick;
        }

        if (CancelButton != null)
        {
            CancelButton.Click += (_, _) => Close();
        }

        try
        {
            _source = _exporter.LoadCurrentProfile();
            if (ProfileNameTextBlock != null)
            {
                ProfileNameTextBlock.Text =
                    $"档案：{Path.GetFileName(_source.FilePath)}　共 {_source.Profile.ClassPlans.Count} 张课表、" +
                    $"{_source.Profile.TimeLayouts.Count} 个时间表、{_source.Profile.Subjects.Count} 个科目";
            }

            // 默认只勾选「课表」，其余内容由用户按需选择。
            SetChecked(ClassPlanCheckBox, _source.Profile.ClassPlans.Count > 0);
            SetChecked(TimeLayoutCheckBox, false);
            SetChecked(SubjectCheckBox, false);

            UpdateStorageModeVisibility();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SystemTools]无法读取 ClassIsland 档案");
            _source = null;
            SetMessage($"无法读取 ClassIsland 档案：{ex.Message}");
            if (ExportButton != null)
            {
                ExportButton.IsEnabled = false;
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (ExportButton != null)
        {
            ExportButton.Click -= OnExportButtonClick;
        }
    }

    /// <summary>勾选两项及以上内容时才显示「导出方式」选择。</summary>
    private void ExportTargetCheckBox_OnChanged(object? sender, RoutedEventArgs e) =>
        UpdateStorageModeVisibility();

    private void UpdateStorageModeVisibility()
    {
        if (StorageModePanel != null)
        {
            StorageModePanel.IsVisible = GetSelectedTargetCount() >= 2;
        }
    }

    private int GetSelectedTargetCount() =>
        (ClassPlanCheckBox?.IsChecked == true ? 1 : 0)
        + (TimeLayoutCheckBox?.IsChecked == true ? 1 : 0)
        + (SubjectCheckBox?.IsChecked == true ? 1 : 0);

    private async void OnExportButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isExporting)
        {
            return;
        }

        if (_source == null)
        {
            SetMessage("未能读取档案，无法导出。");
            return;
        }

        var options = new ProfileExcelExportOptions
        {
            ExportClassPlan = ClassPlanCheckBox?.IsChecked == true,
            ExportTimeLayout = TimeLayoutCheckBox?.IsChecked == true,
            ExportSubject = SubjectCheckBox?.IsChecked == true,
            UseXlsx = XlsxRadioButton?.IsChecked != false,
            SeparateFiles = MultipleFilesModeRadioButton?.IsChecked == true
        };

        if (!options.HasAnyTarget)
        {
            SetMessage("请至少选择一项要导出的内容。");
            return;
        }

        // 先确定输出目标：单文件选择保存路径，多文件选择保存文件夹。
        var useSeparateFiles = options.SeparateFiles && options.TargetCount >= 2;
        var destination = useSeparateFiles
            ? await PickFolderAsync()
            : await PickSavePathAsync(options.UseXlsx ? ".xlsx" : ".xls", _source, options.UseXlsx);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        SetExportingState(true);

        try
        {
            var files = await Task.Run(() => _exporter.BuildExportFiles(_source, options));
            if (useSeparateFiles)
            {
                Directory.CreateDirectory(destination);
                foreach (var file in files)
                {
                    await File.WriteAllBytesAsync(Path.Combine(destination, file.FileName), file.Content);
                }

                ExportedFilePath = destination;
                _logger?.LogInformation("[SystemTools]已导出课程档案（{Count} 个文件）到 {Path}", files.Count, destination);
            }
            else
            {
                await File.WriteAllBytesAsync(destination, files[0].Content);
                ExportedFilePath = destination;
                _logger?.LogInformation("[SystemTools]已导出课程档案到 {Path}", destination);
            }

            Close();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SystemTools]导出课程档案失败");
            SetMessage($"导出失败：{ex.Message}");
        }
        finally
        {
            SetExportingState(false);
        }
    }

    private void SetExportingState(bool isExporting)
    {
        _isExporting = isExporting;
        if (ExportButton != null)
        {
            ExportButton.IsEnabled = !isExporting;
        }

        if (CancelButton != null)
        {
            CancelButton.IsEnabled = !isExporting;
        }

        if (isExporting)
        {
            SetMessage("正在生成表格…", isError: false);
        }
    }

    private async Task<string?> PickSavePathAsync(string extension, ProfileExportSource source, bool useXlsx)
    {
        try
        {
            var descriptor = CreateFilePickerFileType(extension);
            var suggestedName = ClassIslandProfileExcelExporter.BuildSuggestedFileName(source) + extension;

            // 本插件仅面向 Windows，文件选取服务会返回可直接写入的真实路径。
            var path = await PlatformServices.FilePickerService.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "保存课表表格文件",
                    SuggestedFileName = suggestedName,
                    DefaultExtension = extension.TrimStart('.'),
                    FileTypeChoices = [descriptor],
                    ShowOverwritePrompt = true
                },
                this);

            return string.IsNullOrWhiteSpace(path) ? null : EnsureExtension(path, extension);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SystemTools]无法打开保存对话框");
            SetMessage($"无法打开保存对话框：{ex.Message}");
            return null;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        try
        {
            var folders = await PlatformServices.FilePickerService.OpenFoldersPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择保存导出文件的文件夹",
                    AllowMultiple = false
                },
                this);

            var folder = folders.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(folder) || PlatformServices.FilePickerService.IsBookmark(folder))
            {
                return null;
            }

            return Path.Combine(folder, ClassIslandProfileExcelExporter.BuildSuggestedFileName(_source!));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SystemTools]无法打开文件夹选择对话框");
            SetMessage($"无法打开文件夹选择对话框：{ex.Message}");
            return null;
        }
    }

    private static FilePickerFileType CreateFilePickerFileType(string extension) => extension == ".xlsx"
        ? new FilePickerFileType("Excel 工作簿") { Patterns = ["*.xlsx"] }
        : new FilePickerFileType("Excel 97-2003 工作簿") { Patterns = ["*.xls"] };

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    /// <summary>
    /// 显示提示信息。错误提示使用错误色（红色），进度类提示使用次要文字色。
    /// </summary>
    private void SetMessage(string message, bool isError = true)
    {
        if (MessageTextBlock == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            MessageTextBlock.Text = message;
            var key = isError ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush";
            if (MessageTextBlock.TryFindResource(key, out var brush) && brush is Avalonia.Media.IBrush typedBrush)
            {
                MessageTextBlock.Foreground = typedBrush;
            }
        });
    }

    private static void SetChecked(CheckBox? checkBox, bool isChecked)
    {
        if (checkBox != null)
        {
            checkBox.IsChecked = isChecked;
        }
    }
}
