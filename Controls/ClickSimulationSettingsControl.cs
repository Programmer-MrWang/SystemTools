/*using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using System.Threading.Tasks;
using SystemTools.Settings;
using WinForms = System.Windows.Forms;

namespace SystemTools.Controls;

public class ClickSimulationSettingsControl : ActionSettingsControlBase<ClickSimulationSettings>
{
    private TextBlock _statusText;
    private TextBox _coordinateTextBox;

    public ClickSimulationSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "模拟鼠标点击",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var recordButton = new Button
        {
            Content = "录制鼠标点击位置…",
            Width = 200,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        recordButton.Click += async (s, e) => await StartRecordingAsync();

        panel.Children.Add(recordButton);

        _statusText = new TextBlock
        {
            Text = "",
            Foreground = Avalonia.Media.Brushes.Gray
        };
        panel.Children.Add(_statusText);

        _coordinateTextBox = new TextBox
        {
            Watermark = "等待录制坐标...",
            IsReadOnly = true,
            Height = 40
        };
        panel.Children.Add(_coordinateTextBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateCoordinateDisplay();
    }

    private void UpdateCoordinateDisplay()
    {
        if (Settings.X != 0 || Settings.Y != 0)
        {
            _coordinateTextBox.Text = $"已录制坐标: X={Settings.X}, Y={Settings.Y}";
        }
    }
    private async Task StartRecordingAsync()
    {
        _statusText.Text = "请在5秒内点击屏幕任意位置...";
        _statusText.Foreground = Avalonia.Media.Brushes.Red;

        var tcs = new TaskCompletionSource<(int X, int Y)>();

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
            tcs.SetResult((e.X, e.Y));
            captureForm.MouseDown -= mouseHandler;
            captureForm.Close();
        };

        captureForm.MouseDown += mouseHandler;
        captureForm.Show();

        try
        {
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

            if (completedTask == tcs.Task)
            {
                var (x, y) = await tcs.Task;

                Settings.X = x;
                Settings.Y = y;

                _coordinateTextBox.Text = $"已录制坐标: X={x}, Y={y}";
                _statusText.Text = $"已录制: X={x}, Y={y}";
                _statusText.Foreground = Avalonia.Media.Brushes.Green;
            }
            else
            {
                _statusText.Text = "录制超时，请重试";
                _statusText.Foreground = Avalonia.Media.Brushes.Orange;
            }
        }
        finally
        {
            captureForm.Dispose();
        }
    }
}*/