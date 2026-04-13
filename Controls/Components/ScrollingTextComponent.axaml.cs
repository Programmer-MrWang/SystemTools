using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Styling;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "A7B3E4D1-2F8C-4B9A-9E5D-6C1B2A3F4E5D",
    " LED 文本仿真显示框",
    "\uE8FF",
    "仿 LED 风格的跑马灯文本滚动组件"
)]
public partial class ScrollingTextComponent : ComponentBase<ScrollingTextSettings>
{
    private CancellationTokenSource? _cts;

    public ScrollingTextComponent()
    {
        InitializeComponent();
    }

    private void ScrollingTextComponent_OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateMarquee();
    }

    private void ScrollingTextComponent_OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        StopAnimation();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.TextContent) ||
             e.PropertyName == nameof(Settings.ComponentWidth) ||
             e.PropertyName == nameof(Settings.ScrollSpeed))
        {
            UpdateMarquee();
        }
    }

    private void StopAnimation()
    {
        try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
        _cts = null;
    }

        private void UpdateMarquee()
    {
        StopAnimation();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (token.IsCancellationRequested) return;

                for (int i = 0; i < 5; i++)
                {
                    if (FirstTextBlock.Bounds.Width > 0 && FirstTextBlock.Bounds.Height > 0) break;
                    await Task.Delay(100, token);
                }

                double textWidth = FirstTextBlock.Bounds.Width;
                double textHeight = FirstTextBlock.Bounds.Height;
                double maxWidth = Settings.ComponentWidth;
                
                double finalWidth = Math.Min(textWidth + 24, maxWidth);
                LayoutRoot.Width = finalWidth;

                double finalHeight = Math.Max(30, textHeight + 5);
                LayoutRoot.Height = finalHeight;

                Canvas.SetLeft(ScrollingContent, 0);
                
                await Task.Yield(); 
                double topOffset = (finalHeight - 12 - textHeight) / 2;
                Canvas.SetTop(ScrollingContent, Math.Max(0, topOffset));

                if (textWidth > (finalWidth - 12))
                {
                    double totalDistance = textWidth + GapCanvas.Width;
                    double durationSec = totalDistance / Math.Max(10, Settings.ScrollSpeed);

                    var animation = new Animation
                    {
                        Duration = TimeSpan.FromSeconds(durationSec),
                        IterationCount = IterationCount.Infinite,
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Canvas.LeftProperty, 0d) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Canvas.LeftProperty, -totalDistance) } }
                        }
                    };
                    await animation.RunAsync(ScrollingContent, token);
                }
                else
                {
                    Canvas.SetLeft(ScrollingContent, (finalWidth - textWidth - 12) / 2);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }, DispatcherPriority.Loaded);
    }

}
