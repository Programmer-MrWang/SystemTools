using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SystemTools.Services;

namespace SystemTools.Controls;

/// <summary>
/// 「导出课程档案为 Excel 表格」的选项内容，供设置页在窗口内的对话框中显示。
/// </summary>
public partial class ProfileExcelExportOptionsControl : UserControl
{
    public ProfileExcelExportOptionsControl()
    {
        InitializeComponent();
    }

    /// <summary>用档案信息初始化界面并预置勾选项。</summary>
    public void Initialize(ProfileExportSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (ProfileNameTextBlock != null)
        {
            ProfileNameTextBlock.Text =
                $"档案：{System.IO.Path.GetFileName(source.FilePath)}　共 {source.Profile.ClassPlans.Count} 张课表、" +
                $"{source.Profile.TimeLayouts.Count} 个时间表、{source.Profile.Subjects.Count} 个科目";
        }

        // 默认只勾选「课表」，其余内容由用户按需选择。
        SetChecked(ClassPlanCheckBox, source.Profile.ClassPlans.Count > 0);
        SetChecked(TimeLayoutCheckBox, false);
        SetChecked(SubjectCheckBox, false);

        UpdateStorageModeVisibility();
    }

    /// <summary>
    /// 读取当前选择。当未选择任何内容时返回 <c>false</c> 并显示提示。
    /// </summary>
    public bool TryBuildOptions(out ProfileExcelExportOptions options)
    {
        options = new ProfileExcelExportOptions
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
            return false;
        }

        SetMessage(null);
        return true;
    }

    private CheckBox? ClassPlanCheckBox => this.FindControl<CheckBox>("ClassPlanCheckBoxElement");

    private CheckBox? TimeLayoutCheckBox => this.FindControl<CheckBox>("TimeLayoutCheckBoxElement");

    private CheckBox? SubjectCheckBox => this.FindControl<CheckBox>("SubjectCheckBoxElement");

    private RadioButton? XlsxRadioButton => this.FindControl<RadioButton>("XlsxRadioButtonElement");

    private RadioButton? MultipleFilesModeRadioButton =>
        this.FindControl<RadioButton>("MultipleFilesModeRadioButtonElement");

    private StackPanel? StorageModePanel => this.FindControl<StackPanel>("StorageModePanelElement");

    private TextBlock? MessageTextBlock => this.FindControl<TextBlock>("MessageTextBlockElement");

    private TextBlock? ProfileNameTextBlock => this.FindControl<TextBlock>("ProfileNameTextBlockElement");

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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

    private void SetMessage(string? message)
    {
        if (MessageTextBlock == null)
        {
            return;
        }

        MessageTextBlock.Text = message ?? "";
        MessageTextBlock.IsVisible = !string.IsNullOrEmpty(message);
    }

    private static void SetChecked(CheckBox? checkBox, bool isChecked)
    {
        if (checkBox != null)
        {
            checkBox.IsChecked = isChecked;
        }
    }
}
