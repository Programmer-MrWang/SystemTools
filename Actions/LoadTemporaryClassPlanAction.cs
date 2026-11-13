using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using SystemTools.Common.Automation;
using System.Threading.Tasks;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.LoadTemporaryClassPlan", "加载临时课表", "\uE6A1", false)]
public class LoadTemporaryClassPlanAction(
    ILogger<LoadTemporaryClassPlanAction> logger,
    IProfileService profileService,
    IExactTimeService exactTimeService) : ActionBase<LoadTemporaryClassPlanSettings>
{
    private readonly ILogger<LoadTemporaryClassPlanAction> _logger = logger;
    private readonly IProfileService _profileService = profileService;
    private readonly IExactTimeService _exactTimeService = exactTimeService;

    private static readonly ActionRestoreStore<TempClassPlanSnapshot> PreviousSnapshots = new();

    protected override async Task OnInvoke()
    {
        if (!Guid.TryParse(Settings.ClassPlanId, out var classPlanId))
        {
            _logger.LogWarning("加载临时课表失败：课表ID无效。ClassPlanId={ClassPlanId}", Settings.ClassPlanId);
            return;
        }

        if (!_profileService.Profile.ClassPlans.TryGetValue(classPlanId, out var classPlan) || classPlan.IsOverlay)
        {
            _logger.LogWarning("加载临时课表失败：未找到课表或课表不可用。ClassPlanId={ClassPlanId}", classPlanId);
            return;
        }

        if (IsRevertable)
        {
            PreviousSnapshots.Capture(ActionSet.Guid, ActionItem, _profileService.Profile.Id.ToString(), new TempClassPlanSnapshot(
                _profileService.Profile.Id,
                _profileService.Profile.TempClassPlanId,
                _profileService.Profile.TempClassPlanSetupTime));
        }

        _profileService.Profile.TempClassPlanId = classPlanId;
        _profileService.Profile.TempClassPlanSetupTime = _exactTimeService.GetCurrentLocalDateTime();
        _profileService.SaveProfile();
        _logger.LogInformation("已加载临时课表：{ClassPlanName} ({ClassPlanId})", classPlan.Name, classPlanId);
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动加载临时课表", "\uE9FB", "")
            });


        await base.OnInvoke();
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (PreviousSnapshots.TryRestore(ActionSet.Guid, ActionItem, out var snapshot) &&
            _profileService.Profile.Id == snapshot.ProfileId)
        {
            _profileService.Profile.TempClassPlanId = snapshot.TempClassPlanId;
            _profileService.Profile.TempClassPlanSetupTime = snapshot.TempClassPlanSetupTime;
            _profileService.SaveProfile();
            _logger.LogInformation("已恢复临时课表为触发前状态。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        _logger.LogInformation("此行动尚无需恢复，或原档案已切换。ActionSet={ActionSetGuid}", ActionSet.Guid);
    }

    private readonly record struct TempClassPlanSnapshot(Guid ProfileId, Guid? TempClassPlanId, DateTime TempClassPlanSetupTime);
}
