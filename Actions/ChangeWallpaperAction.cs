using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC", false)]
public class ChangeWallpaperAction : ActionBase<ChangeWallpaperSettings>
{
    private readonly ILogger<ChangeWallpaperAction> _logger;

    public ChangeWallpaperAction(ILogger<ChangeWallpaperAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        if (string.IsNullOrWhiteSpace(Settings.ImagePath))
        {
            _logger.LogWarning("图片路径为空");
            return;
        }

        if (!File.Exists(Settings.ImagePath))
        {
            _logger.LogError("图片文件不存在: {Path}", Settings.ImagePath);
            throw new FileNotFoundException("指定的图片文件不存在", Settings.ImagePath);
        }

        try
        {
            string escapedPath = Settings.ImagePath.Replace("\\", "\\\\");
            string script = $@"
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W{{[DllImport(""user32.dll"", CharSet=CharSet.Auto)]public static extern int SystemParametersInfo(int a, int b, string c, int d);}}
'@;
[W]::SystemParametersInfo(20, 0, ""{escapedPath}"", 0x01)".Trim();

            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -EncodedCommand {encodedScript}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(error) && !error.TrimStart().StartsWith("#< CLIXML"))
                {
                    _logger.LogWarning("PowerShell 错误: {Error}", error);
                }

                if (process.ExitCode == 0)
                    _logger.LogInformation("壁纸切换成功: {Path}", Settings.ImagePath);
                else
                    _logger.LogWarning("壁纸切换可能失败，退出码: {ExitCode}", process.ExitCode);
            }
            else
            {
                throw new Exception("无法启动 PowerShell 进程");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换壁纸失败");
            throw;
        }

        await base.OnInvoke();
    }
}