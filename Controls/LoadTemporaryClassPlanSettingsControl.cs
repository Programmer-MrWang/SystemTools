using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;
using System;
using System.Collections.Generic;
using System.Linq;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class LoadTemporaryClassPlanSettingsControl : ActionSettingsControlBase<LoadTemporaryClassPlanSettings>
{
    private readonly IProfileService _profileService;
    private readonly ComboBox _comboBox;
    private readonly List<ClassPlanOption> _options = [];
    private readonly DispatcherTimer _refreshTimer;

    public LoadTemporaryClassPlanSettingsControl()
    {
        _profileService = IAppHost.GetService<IProfileService>();

        var panel = new StackPanel { Spacing = 10, Margin = new(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "选择要加载的临时课表：",
        });

        _comboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _comboBox.SelectionChanged += (_, _) =>
        {
            if (_comboBox.SelectedItem is ClassPlanOption option)
            {
                Settings.ClassPlanId = option.Id.ToString();
            }
        };
        panel.Children.Add(_comboBox);

        panel.Children.Add(new TextBlock
        {
            Text = "该行动支持恢复。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        Content = panel;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshClassPlanList();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        RefreshClassPlanList();
        _refreshTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshClassPlanList()
    {
        var classPlans = _profileService.Profile.ClassPlans
            .Where(x => !x.Value.IsOverlay)
            .Select(x => new ClassPlanOption(x.Key, x.Value.Name))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (HasSameItems(_options, classPlans))
        {
            EnsureSelectedItem();
            return;
        }

        _options.Clear();
        _options.AddRange(classPlans);
        _comboBox.ItemsSource = null;
        _comboBox.ItemsSource = _options;
        EnsureSelectedItem();
    }

    private void EnsureSelectedItem()
    {
        if (Guid.TryParse(Settings.ClassPlanId, out var selectedId))
        {
            var option = _options.FirstOrDefault(x => x.Id == selectedId);
            if (option != null)
            {
                _comboBox.SelectedItem = option;
                return;
            }
        }

        var first = _options.FirstOrDefault();
        if (first != null)
        {
            _comboBox.SelectedItem = first;
            Settings.ClassPlanId = first.Id.ToString();
        }
    }

    private static bool HasSameItems(IReadOnlyList<ClassPlanOption> oldList, IReadOnlyList<ClassPlanOption> newList)
    {
        if (oldList.Count != newList.Count) return false;
        for (var i = 0; i < oldList.Count; i++)
        {
            if (oldList[i].Id != newList[i].Id || oldList[i].Name != newList[i].Name)
            {
                return false;
            }
        }
        return true;
    }

    private sealed record ClassPlanOption(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
