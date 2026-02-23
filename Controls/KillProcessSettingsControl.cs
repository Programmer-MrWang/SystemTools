using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class KillProcessSettingsControl : ActionSettingsControlBase<KillProcessSettings>
{
    private TextBox _processNameBox;
    private Button _viewProcessesButton;

    public KillProcessSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "退出进程",
            FontWeight = FontWeight.Bold,
            FontSize = 14
        });

        panel.Children.Add(new TextBlock
        {
            Text = "进程名:",
            FontWeight = FontWeight.Bold,
            Margin = new(0, 5, 0, 0)
        });

        _processNameBox = new TextBox
        {
            Watermark = "输入进程名（如: notepad）"
        };
        panel.Children.Add(_processNameBox);

        var warningPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new(0, 10, 0, 0)
        };

        warningPanel.Children.Add(new TextBlock
        {
            Text = "\uEA39",
            FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = Brushes.Orange
        });

        warningPanel.Children.Add(new TextBlock
        {
            Text = "警告：请勿终止系统重要进程 如 explorer.exe、System 等",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Orange,
            FontWeight = FontWeight.Medium
        });

        panel.Children.Add(warningPanel);

        _viewProcessesButton = new Button
        {
            Content = "查看正在运行的进程",
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(0, 10, 0, 0)
        };
        _viewProcessesButton.Click += async (s, e) => await ShowProcessList();
        panel.Children.Add(_viewProcessesButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _processNameBox[!TextBox.TextProperty] = new Avalonia.Data.Binding(nameof(Settings.ProcessName))
        {
            Source = Settings
        };
    }

    private async Task ShowProcessList()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tasklist",
                Arguments = "/fo table /nh",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) throw new Exception("无法启动 tasklist 进程");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                await ShowErrorDialog("获取进程列表失败", error);
                return;
            }

            await ShowProcessListWindow(output);
        }
        catch (Exception ex)
        {
            await ShowErrorDialog("获取进程列表失败", ex.Message);
        }
    }

    private async Task ShowErrorDialog(string title, string message)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            var window = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Margin = new(10),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new Button
                        {
                            Content = "确定",
                            Width = 100,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            await window.ShowDialog((Window)topLevel);
        }
    }

    private async Task ShowProcessListWindow(string processList)
    {
        var window = new Window
        {
            Title = "正在运行的进程",
            Width = 900,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var textBox = new TextBox
        {
            Text = processList,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("Consolas, monospace"),
            FontSize = 12
        };

        // 设置滚动条
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var copyButton = new Button
        {
            Content = "复制全部",
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(10)
        };
        copyButton.Click += async (s, e) =>
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Clipboard.SetTextAsync(processList);
            }
        };

        var dockPanel = new DockPanel();
        DockPanel.SetDock(copyButton, Dock.Top);
        dockPanel.Children.Add(copyButton);
        dockPanel.Children.Add(textBox);

        window.Content = dockPanel;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            await window.ShowDialog((Window)topLevel);
        }
    }
}