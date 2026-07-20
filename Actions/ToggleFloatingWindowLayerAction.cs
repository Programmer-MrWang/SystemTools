using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;

namespace SystemTools.Actions;

/// <summary>
/// 切换悬浮窗层级行动
/// </summary>
[ActionInfo("SystemTools.ToggleFloatingWindowLayer", "切换悬浮窗层级", "\uE9A8", false)]
public class ToggleFloatingWindowLayerAction(ILogger<ToggleFloatingWindowLayerAction> logger) : ActionBase<ToggleFloatingWindowLayerSettings>
{
    private readonly ILogger<ToggleFloatingWindowLayerAction> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, int> PreviousLayers = new();

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ToggleFloatingWindowLayerAction OnInvoke 开始");

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            var config = GlobalConstants.MainConfig?.Data;
            if (config == null)
            {
                return;
            }

            // 根据设置决定是切换还是设置到指定层级
            // TargetLayer: -1=切换, 0=置底, 1=置顶
            if (Settings.TargetLayer >= 0)
            {
                if (IsRevertable)
                {
                    PreviousLayers[ActionSet.Guid] = config.FloatingWindowLayer;
                }

                service.SetWindowLayer(Settings.TargetLayer);
                _logger.LogInformation("已设置悬浮窗层级为: {Layer}", Settings.TargetLayer == 0 ? "置底" : "置顶");
            }
            else
            {
                if (IsRevertable)
                {
                    PreviousLayers[ActionSet.Guid] = config.FloatingWindowLayer;
                }

                service.ToggleWindowLayer();
                _logger.LogInformation("已切换悬浮窗层级状态");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换悬浮窗层级失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ToggleFloatingWindowLayerAction OnInvoke 完成");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (!PreviousLayers.TryRemove(ActionSet.Guid, out var previousLayer))
        {
            _logger.LogInformation("未找到层级恢复快照，跳过悬浮窗层级恢复。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            service.SetWindowLayer(previousLayer);
            _logger.LogInformation("已恢复悬浮窗层级为: {Layer}", previousLayer == 0 ? "置底" : "置顶");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复悬浮窗层级失败");
            throw;
        }
    }
}
