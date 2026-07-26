using System;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.MainWindowClickTrigger", "点击主界面时", "\uE5C3")]
public sealed class MainWindowClickTrigger(
    MainWindowClickService clickService,
    ILogger<MainWindowClickTrigger> logger) : TriggerBase<MainWindowClickTriggerConfig>
{
    public override void Loaded()
    {
        clickService.Subscribe(OnMainWindowClicked);
    }

    public override void UnLoaded()
    {
        clickService.Unsubscribe(OnMainWindowClicked);
    }

    private void OnMainWindowClicked(object? sender, EventArgs e)
    {
        logger.LogInformation("检测到用户点击主界面，触发自动化");
        Trigger();
    }
}
