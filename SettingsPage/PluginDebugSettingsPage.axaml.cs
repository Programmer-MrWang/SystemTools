using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using System.Collections.Generic;
using SystemTools.ConfigHandlers;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo(
    "systemtools.settings.pluginDebug",
    "插件调试",
    "\uE2C8",
    "\uE2C8",
    true)]
public partial class PluginDebugSettingsPage : SettingsPageBase
{
    private const int DefaultAppearance1 = 0;
    private const int DefaultAppearance2 = 1;

    private static readonly string[] AppearancePresetOptions =
    [
        "默认外观 1",
        "默认外观 2"
    ];

    private int _selectedAppearancePreset;

    public LiquidGlassSettings Glass => GlobalConstants.MainConfig!.Data.AiConversationLiquidGlass;

    public LiquidGlassButtonSettings ApprovalButtonGlass =>
        GlobalConstants.MainConfig!.Data.AiConversationApprovalButtonGlass;

    public IReadOnlyList<string> AppearancePresetNames => AppearancePresetOptions;

    public int SelectedAppearancePreset
    {
        get => _selectedAppearancePreset;
        set
        {
            if (_selectedAppearancePreset == value)
            {
                return;
            }

            _selectedAppearancePreset = value;
        }
    }

    public PluginDebugSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    private void OnResetLiquidGlassClick(object? sender, RoutedEventArgs e) => Glass.Reset();

    private void OnApplyAppearancePresetClick(object? sender, RoutedEventArgs e)
    {
        Glass.CopyFrom(SelectedAppearancePreset switch
        {
            DefaultAppearance1 => CreateAppearance1(),
            DefaultAppearance2 => CreateAppearance2(),
            _ => CreateAppearance1()
        });
    }

    private static LiquidGlassSettings CreateAppearance1() => new()
    {
        CornerRadius = 32,
        BackdropRefreshIntervalMs = 5,
        BackdropZoom = 1,
        BackdropOffsetX = 0,
        BackdropOffsetY = 0,
        RefractionHeight = 18,
        RefractionAmount = 37,
        DepthEffect = true,
        ChromaticAberration = false,
        BlurRadius = 4.5,
        Vibrancy = 0.95,
        Brightness = 0.02,
        Contrast = 1,
        ExposureEv = 0.05,
        GammaPower = 1.05,
        BackdropOpacity = 1,
        TintColor = "#00000000",
        SurfaceColor = "#00000000",
        ProgressiveBlurEnabled = false,
        ProgressiveBlurStart = 0.5,
        ProgressiveBlurEnd = 1,
        ProgressiveTintColor = "#00000000",
        ProgressiveTintIntensity = 0.8,
        AdaptiveLuminanceEnabled = false,
        AdaptiveLuminanceUpdateIntervalMs = 16,
        AdaptiveLuminanceSmoothing = 0.2,
        HighlightEnabled = true,
        HighlightWidth = 1,
        HighlightBlurRadius = 1.35,
        HighlightOpacity = 0.55,
        HighlightAngle = 45,
        HighlightFalloff = 1,
        ShadowEnabled = true,
        ShadowRadius = 40,
        ShadowOffsetX = 0,
        ShadowOffsetY = 4,
        ShadowColor = "#1A000000",
        ShadowOpacity = 1,
        InnerShadowEnabled = true,
        InnerShadowRadius = 41,
        InnerShadowOffsetX = 0,
        InnerShadowOffsetY = 24,
        InnerShadowColor = "#26000000",
        InnerShadowOpacity = 1
    };

    private static LiquidGlassSettings CreateAppearance2() => new()
    {
        CornerRadius = 18,
        BackdropRefreshIntervalMs = 50,
        BackdropZoom = 1,
        BackdropOffsetX = 0,
        BackdropOffsetY = 0,
        RefractionHeight = 12,
        RefractionAmount = 24,
        DepthEffect = false,
        ChromaticAberration = false,
        BlurRadius = 2,
        Vibrancy = 1.5,
        Brightness = 0,
        Contrast = 1,
        ExposureEv = 0,
        GammaPower = 1,
        BackdropOpacity = 1,
        TintColor = "#00000000",
        SurfaceColor = "#00000000",
        ProgressiveBlurEnabled = false,
        ProgressiveBlurStart = 0.5,
        ProgressiveBlurEnd = 1,
        ProgressiveTintColor = "#FF808080",
        ProgressiveTintIntensity = 0.8,
        AdaptiveLuminanceEnabled = false,
        AdaptiveLuminanceUpdateIntervalMs = 250,
        AdaptiveLuminanceSmoothing = 0.2,
        HighlightEnabled = true,
        HighlightWidth = 0.5,
        HighlightBlurRadius = 0.25,
        HighlightOpacity = 0.5,
        HighlightAngle = 45,
        HighlightFalloff = 1,
        ShadowEnabled = true,
        ShadowRadius = 24,
        ShadowOffsetX = 0,
        ShadowOffsetY = 4,
        ShadowColor = "#1A000000",
        ShadowOpacity = 1,
        InnerShadowEnabled = false,
        InnerShadowRadius = 24,
        InnerShadowOffsetX = 0,
        InnerShadowOffsetY = 24,
        InnerShadowColor = "#26000000",
        InnerShadowOpacity = 1
    };

    private void OnDebugVoiceWakeAiClick(object? sender, RoutedEventArgs e)
    {
        var service = IAppHost.TryGetService<AiVoiceConversationService>();
        if (service is null)
        {
            ShowSimpleMessage("无法调试语音唤醒 AI", "请先启用 AI 服务并重启 ClassIsland。");
            return;
        }

        if (!service.TryStartDebugConversation())
        {
            ShowSimpleMessage(
                "无法调试语音唤醒 AI",
                service.LastError ?? "请先选择 AI 模型，或等待当前语音对话结束。");
        }
    }

    private async void ShowSimpleMessage(string title, string message)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };

        await dialog.ShowAsync(topLevel);
    }
}
