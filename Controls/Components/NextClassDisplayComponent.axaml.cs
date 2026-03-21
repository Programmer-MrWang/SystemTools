using System;
using System.ComponentModel;
using System.Linq;
using ClassIsland.Shared.ComponentModels;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Models.Profile;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "C3E56B6B-0E01-4F3C-8F7B-9264CA2B2143",
    "下节课是",
    "",
    "显示当天下一节课的课程全名和任教老师。"
)]
public partial class NextClassDisplayComponent : ComponentBase<NextClassDisplaySettings>, INotifyPropertyChanged
{
    private const string NoMoreClassesText = "接下来已无课程";

    private readonly ILessonsService _lessonsService;
    private readonly IProfileService _profileService;
    private readonly IExactTimeService _exactTimeService;

    private string _subjectName = string.Empty;
    private string _teacherName = string.Empty;
    private string _timeRangeText = string.Empty;
    private bool _hasNextClass;

    public string PrefixText => Settings.PrefixText;

    public string EmptyStateText => NoMoreClassesText;

    public bool ShowEmptyState => !HasNextClass;

    public string SubjectName
    {
        get => _subjectName;
        private set
        {
            if (value == _subjectName) return;
            _subjectName = value;
            OnPropertyChanged(nameof(SubjectName));
        }
    }

    public string TimeRangeText
    {
        get => _timeRangeText;
        private set
        {
            if (value == _timeRangeText) return;
            _timeRangeText = value;
            OnPropertyChanged(nameof(TimeRangeText));
            OnPropertyChanged(nameof(ShouldShowTimeRange));
        }
    }

    public string TeacherName
    {
        get => _teacherName;
        private set
        {
            if (value == _teacherName) return;
            _teacherName = value;
            OnPropertyChanged(nameof(TeacherName));
            OnPropertyChanged(nameof(ShouldShowTeacherName));
        }
    }

    public bool HasNextClass
    {
        get => _hasNextClass;
        private set
        {
            if (value == _hasNextClass) return;
            _hasNextClass = value;
            OnPropertyChanged(nameof(HasNextClass));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowPrefixText));
            OnPropertyChanged(nameof(ShouldShowTimeRange));
            OnPropertyChanged(nameof(ShouldShowTeacherName));
        }
    }

    public bool ShowPrefixText => HasNextClass && !string.IsNullOrWhiteSpace(PrefixText);

    public bool ShouldShowTimeRange => HasNextClass && Settings.ShowTimeRange && !string.IsNullOrWhiteSpace(TimeRangeText);

    public bool ShouldShowTeacherName => HasNextClass && Settings.ShowTeacherName && !string.IsNullOrWhiteSpace(TeacherName);

    public new event PropertyChangedEventHandler? PropertyChanged;

    public NextClassDisplayComponent(ILessonsService lessonsService, IProfileService profileService, IExactTimeService exactTimeService)
    {
        _lessonsService = lessonsService;
        _profileService = profileService;
        _exactTimeService = exactTimeService;
        InitializeComponent();
    }

    private void NextClassDisplayComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _lessonsService.PostMainTimerTicked += OnLessonsServicePostMainTimerTicked;
        _lessonsService.PropertyChanged += OnLessonsServicePropertyChanged;
        UpdateDisplay();
    }

    private void NextClassDisplayComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _lessonsService.PostMainTimerTicked -= OnLessonsServicePostMainTimerTicked;
        _lessonsService.PropertyChanged -= OnLessonsServicePropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Settings.PrefixText) or nameof(Settings.ShowTeacherName) or nameof(Settings.ShowTimeRange))
        {
            OnPropertyChanged(nameof(PrefixText));
            OnPropertyChanged(nameof(ShowPrefixText));
            OnPropertyChanged(nameof(ShouldShowTimeRange));
            OnPropertyChanged(nameof(ShouldShowTeacherName));
        }
    }

    private void OnLessonsServicePostMainTimerTicked(object? sender, EventArgs e) => UpdateDisplay();

    private void OnLessonsServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILessonsService.CurrentClassPlan) or nameof(ILessonsService.CurrentTimeLayoutItem))
        {
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        var classPlan = _lessonsService.CurrentClassPlan;
        if (classPlan?.TimeLayout == null)
        {
            ApplyNoMoreClasses();
            return;
        }

        var now = _exactTimeService.GetCurrentLocalDateTime().TimeOfDay;
        var validLessonSlots = classPlan.TimeLayout.Layouts
            .Where(x => x.TimeType == 0)
            .ToList();

        foreach (var candidateTime in validLessonSlots)
        {
            if (candidateTime.StartTime <= now)
            {
                continue;
            }

            var candidateClassInfo = classPlan.Classes.FirstOrDefault(x => ReferenceEquals(x.CurrentTimeLayoutItem, candidateTime));
            if (candidateClassInfo == null)
            {
                continue;
            }

            if (!_profileService.Profile.Subjects.TryGetValue(candidateClassInfo.SubjectId, out var subject))
            {
                continue;
            }

            HasNextClass = true;
            SubjectName = subject.Name;
            TimeRangeText = $"{candidateTime.StartTime:hh\\:mm}-{candidateTime.EndTime:hh\\:mm}";
            TeacherName = string.IsNullOrWhiteSpace(subject.TeacherName) ? string.Empty : subject.TeacherName;
            return;
        }

        ApplyNoMoreClasses();
    }

    private void ApplyNoMoreClasses()
    {
        HasNextClass = false;
        SubjectName = string.Empty;
        TimeRangeText = string.Empty;
        TeacherName = string.Empty;
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
