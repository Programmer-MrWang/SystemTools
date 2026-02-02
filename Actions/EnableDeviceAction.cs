using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.EnableDevice", "启用硬件设备", "\uE0AD",false)]
public class EnableDeviceAction : ActionBase<EnableDeviceSettings>
{
    private readonly ILogger<EnableDeviceAction> _logger;

    public EnableDeviceAction(ILogger<EnableDeviceAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("EnableDeviceAction OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.DeviceId))
        {
            _logger.LogWarning("设备ID为空");
            return;
        }

        try
        {
            string deviceId = Settings.DeviceId;
            string lastSixChars = deviceId.Length >= 6 ? deviceId.Substring(deviceId.Length - 6) : deviceId;
            string pluginDir = Path.GetDirectoryName(GetType().Assembly.Location) ?? "";
            string batFilePath = Path.Combine(pluginDir, $"Enable_{lastSixChars}.bat");
            string ps1FilePath = Path.Combine(pluginDir, $"Enable_{lastSixChars}.ps1");

            string batContent = $@"@echo off
:: 检查并请求管理员权限
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    powershell -Command ""Start-Process -FilePath '%~0' -Verb RunAs""
    exit /b
)

:: 以管理员身份隐藏运行 PowerShell 脚本
powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File ""%~dp0Enable_{lastSixChars}.ps1""";

            string ps1Content = $@"$device = Get-PnpDevice | Where-Object {{$_.InstanceId -eq ""{deviceId}""}}

if ($device) {{
    Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false
    Write-Host ""已启用设备: $($device.FriendlyName) [ID: $($device.InstanceId)]""
}} else {{
    Write-Host ""未找到设备ID""
}}";

            await File.WriteAllTextAsync(batFilePath, batContent);
            await File.WriteAllTextAsync(ps1FilePath, ps1Content);

            _logger.LogInformation("已生成文件: {BatFile} 和 {Ps1File}", batFilePath, ps1FilePath);

            var psi = new ProcessStartInfo
            {
                FileName = batFilePath,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Process.Start(psi);

            _logger.LogInformation("已启动批处理文件");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用设备失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("EnableDeviceAction OnInvoke 完成");
    }
}