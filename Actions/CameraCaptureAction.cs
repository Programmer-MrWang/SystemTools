using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Media;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E", false)]
public class CameraCaptureAction(ILogger<CameraCaptureAction> logger, CameraCaptureContext capture) : ActionBase<CameraCaptureSettings>
{
    protected override async Task OnInvoke()
    {
        if (string.IsNullOrWhiteSpace(Settings.SaveFolder)) throw new ArgumentException("请选择图片保存文件夹。");
        if (string.IsNullOrWhiteSpace(Settings.DeviceName)) throw new ArgumentException("请选择摄像头设备。");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, capture.Stopping);
        var path = await MediaFiles.CaptureAsync(capture.Camera, Settings.DeviceName, Settings.SaveFolder, stop.Token);
        logger.LogInformation("摄像头抓拍已保存至 {Path}", path);
    }
}
