using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Settings;
using WinForms = System.Windows.Forms;

namespace SystemTools.Controls;

public class ClickSimulationSettingsControl : ActionSettingsControlBase<ClickSimulationSettings>
{
    private readonly string _filePath;
    private Avalonia.Controls.TextBlock _statusText;
    private bool _isRecording = false;
    private TaskCompletionSource<(int X, int Y)> _clickTcs;
    private Avalonia.Controls.TextBox _coordinateTextBox;

    public ClickSimulationSettingsControl()
    {
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "click.json");

        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "模拟鼠标点击",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var recordButton = new Avalonia.Controls.Button
        {
            Content = "录制鼠标点击位置…",
            Width = 200,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        recordButton.Click += async (s, e) => await StartRecordingAsync();

        panel.Children.Add(recordButton);

        _statusText = new Avalonia.Controls.TextBlock
        {
            Text = "",
            Foreground = Avalonia.Media.Brushes.Gray
        };
        panel.Children.Add(_statusText);

        _coordinateTextBox = new Avalonia.Controls.TextBox
        {
            Watermark = "等待录制坐标...",
            IsReadOnly = true,
            Height = 40
        };
        panel.Children.Add(_coordinateTextBox);

        LoadExistingCoordinates();

        Content = panel;
    }

    private void LoadExistingCoordinates()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<ClickSimulationSettings>(json);
                if (settings != null && (settings.X != 0 || settings.Y != 0))
                {
                    _coordinateTextBox.Text = $"已录制坐标: X={settings.X}, Y={settings.Y}";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载 click.json 失败: {ex.Message}");
        }
    }

    private async Task StartRecordingAsync()
    {
        _isRecording = true;
        _statusText.Text = "请在5秒内点击屏幕任意位置...";
        _statusText.Foreground = Avalonia.Media.Brushes.Red;

        _clickTcs = new TaskCompletionSource<(int X, int Y)>();

        var captureForm = new WinForms.Form
        {
            Width = WinForms.Screen.PrimaryScreen.Bounds.Width,
            Height = WinForms.Screen.PrimaryScreen.Bounds.Height,
            ShowInTaskbar = false,
            Opacity = 0.01,
            FormBorderStyle = WinForms.FormBorderStyle.None,
            WindowState = WinForms.FormWindowState.Maximized,
            TopMost = true
        };

        WinForms.MouseEventHandler mouseHandler = null;
        mouseHandler = (sender, e) =>
        {
            if (_isRecording)
            {
                _clickTcs.SetResult((e.X, e.Y));
                captureForm.MouseDown -= mouseHandler;
                captureForm.Close();
            }
        };

        captureForm.MouseDown += mouseHandler;
        captureForm.Show();

        var clickTask = _clickTcs.Task;
        var timeoutTask = Task.Delay(5000);
        var completedTask = await Task.WhenAny(clickTask, timeoutTask);

        if (completedTask == clickTask)
        {
            var (x, y) = await clickTask;
            await FinishRecordingAsync(x, y);
        }
        else
        {
            _statusText.Text = "录制超时，请重试";
            _statusText.Foreground = Avalonia.Media.Brushes.Orange;
        }

        captureForm.Dispose();
        _isRecording = false;
    }

    private async Task FinishRecordingAsync(int x, int y)
    {
        _statusText.Text = $"已录制: X={x}, Y={y}";
        _statusText.Foreground = Avalonia.Media.Brushes.Green;

        var settings = new ClickSimulationSettings
        {
            X = x,
            Y = y
        };

        _coordinateTextBox.Text = $"已录制坐标: X={x}, Y={y}";

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }
}