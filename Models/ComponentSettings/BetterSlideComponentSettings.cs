using ClassIsland.Core.Abstractions.Models;
using ClassIsland.Core.Models.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SystemTools.Models.ComponentSettings;

public partial class BetterSlideComponentSettings : ObservableObject, IComponentContainerSettings
{
    [ObservableProperty] private ObservableCollection<ClassIsland.Core.Models.Components.ComponentSettings> _children = new();

    /// <summary>
    /// 0 - 循环; 1 - 随机; 2 - 往复
    /// </summary>
    [ObservableProperty] private int _slideMode = 0;

    [ObservableProperty] private bool _isAnimationEnabled = true;

    /// <summary>
    /// 0 - 翻页; 1 - 闪烁
    /// </summary>
    [ObservableProperty] private int _animationMode = 0;

    [ObservableProperty] private bool _showProgressBar = true;

    [ObservableProperty] private ObservableCollection<ComponentDurationSetting> _componentDurations = [];

    public double GetDurationSecondsFor(int index)
    {
        if (index < 0 || index >= ComponentDurations.Count)
        {
            return 5;
        }

        return ComponentDurations[index].DurationSeconds;
    }

    public void EnsureDurationEntries()
    {
        while (ComponentDurations.Count < Children.Count)
        {
            ComponentDurations.Add(new ComponentDurationSetting());
        }

        while (ComponentDurations.Count > Children.Count)
        {
            ComponentDurations.RemoveAt(ComponentDurations.Count - 1);
        }

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var title = string.IsNullOrWhiteSpace(child.NameCache)
                ? child.AssociatedComponentInfo.Name
                : child.NameCache;

            if (string.IsNullOrWhiteSpace(title))
            {
                title = $"组件 {i + 1}";
            }

            ComponentDurations[i].ComponentTitle = title;
            ComponentDurations[i].ComponentId = child.Id;

            if (ComponentDurations[i].DurationSeconds <= 0)
            {
                ComponentDurations[i].DurationSeconds = 5;
            }
        }
    }
}

public partial class ComponentDurationSetting : ObservableObject
{
    [ObservableProperty] private string _componentTitle = string.Empty;

    [ObservableProperty] private string _componentId = string.Empty;

    [ObservableProperty] private double _durationSeconds = 5;
}
