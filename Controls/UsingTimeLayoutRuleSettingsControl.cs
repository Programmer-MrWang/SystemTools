using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using SystemTools.Rules;

namespace SystemTools.Controls;

public class UsingTimeLayoutRuleSettingsControl : RuleSettingsControlBase<UsingTimeLayoutRuleSettings>
{
    private readonly IProfileService _profileService;
    private readonly ComboBox _comboBox;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<Option> _options = [];

    public UsingTimeLayoutRuleSettingsControl()
    {
        _profileService = IAppHost.GetService<IProfileService>();
        var panel = new StackPanel { Spacing = 10 };

        _comboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _comboBox.SelectionChanged += (_, _) =>
        {
            if (_comboBox.SelectedItem is Option option)
            {
                Settings.TimeLayoutId = option.Id.ToString();
            }
        };
        panel.Children.Add(_comboBox);
        Content = panel;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshItems();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        RefreshItems();
        _refreshTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshItems()
    {
        var next = _profileService.Profile.TimeLayouts
            .Select(x => new Option(x.Key, x.Value.Name))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!HasSameItems(_options, next))
        {
            _options.Clear();
            _options.AddRange(next);
            _comboBox.ItemsSource = null;
            _comboBox.ItemsSource = _options;
        }

        SelectCurrentOrFirst();
    }

    private void SelectCurrentOrFirst()
    {
        if (Guid.TryParse(Settings.TimeLayoutId, out var selectedId))
        {
            var selected = _options.FirstOrDefault(x => x.Id == selectedId);
            if (selected != null)
            {
                _comboBox.SelectedItem = selected;
                return;
            }
        }

        var first = _options.FirstOrDefault();
        if (first != null)
        {
            _comboBox.SelectedItem = first;
            Settings.TimeLayoutId = first.Id.ToString();
        }
    }

    private static bool HasSameItems(IReadOnlyList<Option> oldList, IReadOnlyList<Option> newList)
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

    private sealed record Option(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
