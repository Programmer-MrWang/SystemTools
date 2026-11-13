using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace SystemTools.Platforms.Windows;

[ActionInfo("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E", false)]
internal sealed class UnavailableCameraAction : ActionBase
{
    public UnavailableCameraAction() { }

    protected override Task OnInvoke() => throw new InvalidOperationException(
        "摄像头抓拍尚未准备好。请在 SystemTools 设置中启用该功能并安装所需组件，然后重启 ClassIsland。");
}

