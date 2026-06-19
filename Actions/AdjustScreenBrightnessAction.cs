using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Management;
using System.Threading.Tasks;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AdjustScreenBrightness", "调整屏幕亮度", "\uF464", false)]
public class AdjustScreenBrightnessAction(ILogger<AdjustScreenBrightnessAction> logger)
    : ActionBase<AdjustScreenBrightnessSettings>
{
    private readonly ILogger<AdjustScreenBrightnessAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        await Task.Run(() =>
        {
            try
            {
                if (Settings == null)
                {
                    _logger.LogWarning("设置为空，无法调整屏幕亮度");
                    return;
                }

                if (Settings.BrightnessPercent < 0 || Settings.BrightnessPercent > 100)
                {
                    _logger.LogWarning("亮度值 {Brightness} 超出范围 0-100", Settings.BrightnessPercent);
                    throw new ArgumentOutOfRangeException(nameof(Settings.BrightnessPercent),
                        "亮度值必须在 0-100 之间");
                }

                bool anySuccess = SetScreenBrightnessWmi(Settings.BrightnessPercent);

                if (anySuccess)
                {
                    _logger.LogInformation("屏幕亮度已设置为 {Brightness}%", Settings.BrightnessPercent);
                }
                else
                {
                    _logger.LogWarning("设置屏幕亮度失败，可能是硬件不支持、WMI 调用失败或缺少管理员权限");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调整屏幕亮度时发生错误");
                throw;
            }
        });

        await base.OnInvoke();
    }

    /// <param name="brightness">亮度值 0-100</param>
    /// <returns>是否至少有一台显示器设置成功</returns>
    private bool SetScreenBrightnessWmi(int brightness)
    {
        bool anySuccess = false;

        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            var query = new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var objects = searcher.Get();

            if (objects.Count == 0)
            {
                _logger.LogWarning("未找到支持亮度调整的显示器设备");
                return false;
            }

            foreach (ManagementObject obj in objects)
            {
                using (obj)
                {
                    try
                    {
                        ManagementBaseObject inParams = obj.GetMethodParameters("WmiSetBrightness");
                        inParams["Timeout"] = (uint)0;
                        inParams["Brightness"] = (byte)brightness;

                        ManagementBaseObject outParams = obj.InvokeMethod("WmiSetBrightness", inParams, null);

                        if (outParams != null)
                        {
                            uint returnValue = Convert.ToUInt32(outParams["returnValue"]);
                            _logger.LogInformation("显示器 {InstanceName} 亮度设置返回状态: {Status}",
                                obj["InstanceName"] ?? "Unknown", returnValue);

                            if (returnValue == 0)
                            {
                                anySuccess = true;
                            }
                            else
                            {
                                _logger.LogWarning("显示器 {InstanceName} 亮度设置失败，WMI 返回错误码: {ErrorCode}",
                                    obj["InstanceName"] ?? "Unknown", returnValue);
                            }
                        }
                    }
                    catch (ManagementException mex)
                    {
                        _logger.LogWarning(mex, "调用 WmiSetBrightness 失败（ManagementException）: {Message}", mex.Message);
                        if (mex.Message.Contains("Not supported", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("当前显示器不支持通过 WMI 调整亮度");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "调用 WmiSetBrightness 失败: {Message}", ex.Message);
                    }
                }
            }

            return anySuccess;
        }
        catch (ManagementException mex)
        {
            _logger.LogError(mex, "WMI 操作异常（ManagementException）: {Message}", mex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMI 操作异常: {Message}", ex.Message);
            return false;
        }
    }
}