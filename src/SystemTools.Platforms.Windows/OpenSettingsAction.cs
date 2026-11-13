using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;

namespace SystemTools.Platforms.Windows;

[ActionInfo("SystemTools.OpenAppSettings", "打开应用设置", "\uEF27", false)]
public sealed class OpenSettingsAction(IUriNavigationService navigation) : ActionBase
{
    protected override Task OnInvoke()
    {
        navigation.NavigateWrapped(new Uri("classisland://app/settings"));
        return Task.CompletedTask;
    }
}
