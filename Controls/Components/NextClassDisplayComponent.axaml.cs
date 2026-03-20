using System;
using System.ComponentModel;
using System.Linq;
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

    private string _courseName = NoMoreClassesText;
    private string _teacherName = string.Empty;
    private bool _hasNextClass;

    public string PrefixText => Settings.PrefixText;

    public string CourseName
    {
        get => _courseName;
        private set
        {
            if (value == _courseName) return;
            _courseName = value;
            OnPropertyChanged(nameof(CourseName));
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
            OnPropertyChanged(nameof(ShouldShowTeacherName));
        }
    }

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
        if (e.PropertyName is nameof(Settings.PrefixText) or nameof(Settings.ShowTeacherName))
        {
            OnPropertyChanged(nameof(PrefixText));
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
        var nextClassTime = classPlan.ValidTimeLayoutItems
            .Where(x => x.TimeType == 0 && x.StartTime > now)
            .OrderBy(x => x.StartTime)
            .FirstOrDefault();

        if (nextClassTime == null)
        {
            ApplyNoMoreClasses();
            return;
        }

        var classInfo = classPlan.Classes.FirstOrDefault(x => ReferenceEquals(x.CurrentTimeLayoutItem, nextClassTime) || x.CurrentTimeLayoutItem == nextClassTime);
        if (classInfo == null || !_profileService.Profile.Subjects.TryGetValue(classInfo.SubjectId, out var subject))
        {
            ApplyNoMoreClasses();
            return;
        }

        HasNextClass = true;
        CourseName = string.IsNullOrWhiteSpace(subject.Name) ? "未命名课程" : subject.Name;
        TeacherName = string.IsNullOrWhiteSpace(subject.TeacherName) ? string.Empty : $"（{subject.TeacherName}）";
    }

    private void ApplyNoMoreClasses()
    {
        HasNextClass = false;
        CourseName = NoMoreClassesText;
        TeacherName = string.Empty;
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
