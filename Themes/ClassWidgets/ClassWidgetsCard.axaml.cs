using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared;

namespace SystemTools.Themes.ClassWidgets;

/// <summary>
/// A ClassWidgets-style facade around a built-in ClassIsland component.
/// Host component types are resolved at runtime because the plugin SDK does not
/// reference the concrete ClassIsland application assembly at compile time.
/// </summary>
public partial class ClassWidgetsCard : UserControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<object?> HostedContentProperty =
        AvaloniaProperty.Register<ClassWidgetsCard, object?>(nameof(HostedContent));

    public static readonly StyledProperty<string?> ComponentNameProperty =
        AvaloniaProperty.Register<ClassWidgetsCard, string?>(nameof(ComponentName));

    private readonly DispatcherTimer _rotationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(7)
    };

    private object? _clockContent;
    private ILessonsService? _clockLessonsService;
    private object? _scheduleContent;
    private object? _weatherContent;
    private ILessonsService? _scheduleLessonsService;
    private IWeatherService? _weatherService;
    private object? _weatherSettingsService;
    private INotifyPropertyChanged? _clockNotifier;
    private INotifyPropertyChanged? _scheduleNotifier;
    private INotifyPropertyChanged? _weatherNotifier;
    private INotifyPropertyChanged? _weatherSettingsNotifier;
    private IExactTimeService? _exactTimeService;
    private CancellationTokenSource? _clockHeaderTransitionCts;
    private CancellationTokenSource? _weatherTransitionCts;
    private ClassIsland.Core.Controls.ComponentPresenter? _hostPresenter;
    private bool _isAttached;
    private bool _rotationState;
    private bool _hasWeatherRain;

    private bool _isClock;
    private bool _isSchedule;
    private bool _isScheduleOnClass;
    private bool _isScheduleBreaking;
    private bool _isScheduleNoCourse = true;
    private bool _isWeather;
    private bool _isGeneric = true;
    private bool _isWeatherRainDisplay;
    private double _weatherContentOpacity = 1;
    private double _headerOpacity = 0.78;
    private string _headerText = "组件";
    private string _clockDisplayText = "--:--:--";
    private string _scheduleDisplayText = "当前无课程";
    private string _scheduleSecondaryText = "暂无课程";
    private string _scheduleIconGlyph = "\uE007";
    private string _weatherCode = "99";
    private string _weatherTemperatureText = "--°";
    private string _weatherConditionText = "天气";
    private string _weatherIconGlyph = "\uE091";
    private string _rainDisplayText = "--";
    private string _rainHeaderText = "即将下雨";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public object? HostedContent
    {
        get => GetValue(HostedContentProperty);
        set => SetValue(HostedContentProperty, value);
    }

    // The host's MainContentPresenter already owns the component control.
    // Specialized cards read its state, while unknown component types fall
    // back to the original control without giving it a second visual parent.
    public object? GenericHostedContent => IsGeneric ? HostedContent : null;

    public string? ComponentName
    {
        get => GetValue(ComponentNameProperty);
        set => SetValue(ComponentNameProperty, value);
    }

    public bool IsClock
    {
        get => _isClock;
        private set => SetField(ref _isClock, value);
    }

    public bool IsSchedule
    {
        get => _isSchedule;
        private set => SetField(ref _isSchedule, value);
    }

    public bool IsScheduleOnClass
    {
        get => _isScheduleOnClass;
        private set => SetField(ref _isScheduleOnClass, value);
    }

    public bool IsScheduleBreaking
    {
        get => _isScheduleBreaking;
        private set => SetField(ref _isScheduleBreaking, value);
    }

    public bool IsScheduleNoCourse
    {
        get => _isScheduleNoCourse;
        private set => SetField(ref _isScheduleNoCourse, value);
    }

    public bool IsWeather
    {
        get => _isWeather;
        private set => SetField(ref _isWeather, value);
    }

    public bool IsGeneric
    {
        get => _isGeneric;
        private set
        {
            if (SetField(ref _isGeneric, value))
            {
                OnPropertyChanged(nameof(GenericHostedContent));
            }
        }
    }

    public bool IsWeatherRainDisplay
    {
        get => _isWeatherRainDisplay;
        private set => SetField(ref _isWeatherRainDisplay, value);
    }

    public bool HasWeatherRain
    {
        get => _hasWeatherRain;
        private set => SetField(ref _hasWeatherRain, value);
    }

    public string HeaderText
    {
        get => _headerText;
        private set => SetField(ref _headerText, value);
    }

    public double HeaderOpacity
    {
        get => _headerOpacity;
        private set => SetField(ref _headerOpacity, value);
    }

    public double WeatherContentOpacity
    {
        get => _weatherContentOpacity;
        private set => SetField(ref _weatherContentOpacity, value);
    }

    public string ClockDisplayText
    {
        get => _clockDisplayText;
        private set => SetField(ref _clockDisplayText, value);
    }

    public string ScheduleDisplayText
    {
        get => _scheduleDisplayText;
        private set => SetField(ref _scheduleDisplayText, value);
    }

    public string ScheduleSecondaryText
    {
        get => _scheduleSecondaryText;
        private set => SetField(ref _scheduleSecondaryText, value);
    }

    public string ScheduleIconGlyph
    {
        get => _scheduleIconGlyph;
        private set => SetField(ref _scheduleIconGlyph, value);
    }

    public string WeatherCode
    {
        get => _weatherCode;
        private set => SetField(ref _weatherCode, value);
    }

    public string WeatherTemperatureText
    {
        get => _weatherTemperatureText;
        private set => SetField(ref _weatherTemperatureText, value);
    }

    public string WeatherConditionText
    {
        get => _weatherConditionText;
        private set => SetField(ref _weatherConditionText, value);
    }

    public string WeatherIconGlyph
    {
        get => _weatherIconGlyph;
        private set => SetField(ref _weatherIconGlyph, value);
    }

    public string RainDisplayText
    {
        get => _rainDisplayText;
        private set => SetField(ref _rainDisplayText, value);
    }

    public string RainHeaderText
    {
        get => _rainHeaderText;
        private set => SetField(ref _rainHeaderText, value);
    }

    public ClassWidgetsCard()
    {
        InitializeComponent();
        _rotationTimer.Tick += RotationTimerOnTick;
        this.GetObservable(HostedContentProperty).Subscribe(_ => RebindHostedContent());
        this.GetObservable(ComponentNameProperty).Subscribe(_ => UpdateGenericHeader());
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        AttachHostPresenter();
        RebindHostedContent();
        UpdateTimerState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _rotationTimer.Stop();
        CancelClockHeaderTransition();
        CancelWeatherTransition();
        DetachHostPresenter();
        DetachHostedContent();
        ReleaseGenericHostedContent();
    }

    private void AttachHostPresenter()
    {
        var presenter = this.GetVisualAncestors()
            .OfType<ClassIsland.Core.Controls.ComponentPresenter>()
            .FirstOrDefault()
            ?? this.GetLogicalAncestors()
                .OfType<ClassIsland.Core.Controls.ComponentPresenter>()
                .FirstOrDefault();
        if (ReferenceEquals(presenter, _hostPresenter))
        {
            SyncHostedContentFromPresenter();
            return;
        }

        DetachHostPresenter();
        _hostPresenter = presenter;
        if (_hostPresenter == null)
        {
            return;
        }

        _hostPresenter.PropertyChanged += HostPresenterOnPropertyChanged;
        SyncHostedContentFromPresenter();
    }

    private void DetachHostPresenter()
    {
        if (_hostPresenter != null)
        {
            _hostPresenter.PropertyChanged -= HostPresenterOnPropertyChanged;
            _hostPresenter = null;
        }
    }

    private void HostPresenterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(ClassIsland.Core.Controls.ComponentPresenter.PresentingContent))
        {
            SyncHostedContentFromPresenter();
        }
    }

    private void SyncHostedContentFromPresenter()
    {
        if (_hostPresenter != null)
        {
            HostedContent = _hostPresenter.PresentingContent;
        }
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        // Force the fallback presenter to refresh before the host reparents its
        // component during a theme reload.
        ReleaseGenericHostedContent();
        base.OnDetachedFromLogicalTree(e);
    }

    private void RebindHostedContent()
    {
        DetachHostedContent();

        _clockContent = IsComponent(HostedContent, "ClockComponent") ? HostedContent : null;
        _scheduleContent = IsComponent(HostedContent, "ScheduleComponent") ? HostedContent : null;
        _weatherContent = IsComponent(HostedContent, "WeatherComponent") ? HostedContent : null;

        IsClock = _clockContent != null;
        IsSchedule = _scheduleContent != null;
        IsWeather = _weatherContent != null;
        IsGeneric = !IsClock && !IsSchedule && !IsWeather;
        OnPropertyChanged(nameof(GenericHostedContent));
        _rotationState = false;

        if (_clockContent != null)
        {
            _clockLessonsService = Read(_clockContent, "LessonsService") as ILessonsService;
            _clockNotifier = AsNotifier(_clockContent);
            if (_clockNotifier != null)
            {
                _clockNotifier.PropertyChanged += HostedComponentOnPropertyChanged;
            }

            // The themed card replaces the host ClockComponent in the visual
            // tree. Subscribe to the same host timer that normally drives it,
            // so the card keeps updating without re-parenting the component.
            if (_clockLessonsService != null)
            {
                _clockLessonsService.PostMainTimerTicked += LessonsServiceOnTick;
            }

            _exactTimeService = IAppHost.TryGetService<IExactTimeService>();
        }

        if (_scheduleContent != null)
        {
            _scheduleLessonsService = Read(_scheduleContent, "LessonsService") as ILessonsService;
            _scheduleNotifier = AsNotifier(_scheduleContent);
            if (_scheduleNotifier != null)
            {
                _scheduleNotifier.PropertyChanged += HostedComponentOnPropertyChanged;
            }

            if (_scheduleLessonsService != null)
            {
                _scheduleLessonsService.PropertyChanged += HostedComponentOnPropertyChanged;
                _scheduleLessonsService.PostMainTimerTicked += LessonsServiceOnTick;
                _scheduleLessonsService.CurrentTimeStateChanged += LessonsServiceOnStateChanged;
            }
        }

        if (_weatherContent != null)
        {
            _weatherService = Read(_weatherContent, "WeatherService") as IWeatherService;
            _weatherSettingsService = Read(_weatherContent, "SettingsService");
            _weatherNotifier = AsNotifier(_weatherContent);
            _weatherSettingsNotifier = AsNotifier(Read(_weatherSettingsService, "Settings"));

            if (_weatherNotifier != null)
            {
                _weatherNotifier.PropertyChanged += HostedComponentOnPropertyChanged;
            }

            if (_weatherService != null)
            {
                _weatherService.PropertyChanged += HostedComponentOnPropertyChanged;
            }

            if (_weatherSettingsNotifier != null)
            {
                _weatherSettingsNotifier.PropertyChanged += HostedComponentOnPropertyChanged;
            }

        }

        UpdatePresentation();
        UpdateTimerState();
    }

    private void DetachHostedContent()
    {
        CancelClockHeaderTransition();
        CancelWeatherTransition();
        if (_clockNotifier != null)
        {
            _clockNotifier.PropertyChanged -= HostedComponentOnPropertyChanged;
            _clockNotifier = null;
        }

        if (_clockLessonsService != null)
        {
            _clockLessonsService.PostMainTimerTicked -= LessonsServiceOnTick;
            _clockLessonsService = null;
        }

        if (_scheduleNotifier != null)
        {
            _scheduleNotifier.PropertyChanged -= HostedComponentOnPropertyChanged;
            _scheduleNotifier = null;
        }

        if (_scheduleLessonsService != null)
        {
            _scheduleLessonsService.PropertyChanged -= HostedComponentOnPropertyChanged;
            _scheduleLessonsService.PostMainTimerTicked -= LessonsServiceOnTick;
            _scheduleLessonsService.CurrentTimeStateChanged -= LessonsServiceOnStateChanged;
            _scheduleLessonsService = null;
        }

        if (_weatherNotifier != null)
        {
            _weatherNotifier.PropertyChanged -= HostedComponentOnPropertyChanged;
            _weatherNotifier = null;
        }

        if (_weatherService != null)
        {
            _weatherService.PropertyChanged -= HostedComponentOnPropertyChanged;
            _weatherService = null;
        }

        if (_weatherSettingsNotifier != null)
        {
            _weatherSettingsNotifier.PropertyChanged -= HostedComponentOnPropertyChanged;
            _weatherSettingsNotifier = null;
        }

        _clockContent = null;
        _exactTimeService = null;
        _scheduleContent = null;
        _weatherContent = null;
        _weatherSettingsService = null;
        HasWeatherRain = false;
        IsWeatherRainDisplay = false;
        UpdateTimerState();
    }

    private void ReleaseGenericHostedContent()
    {
        OnPropertyChanged(nameof(GenericHostedContent));
    }

    private void HostedComponentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestPresentationUpdate();
    }

    private void LessonsServiceOnTick(object? sender, EventArgs e) => RequestPresentationUpdate();

    private void LessonsServiceOnStateChanged(object? sender, EventArgs e) => RequestPresentationUpdate();

    private async void RotationTimerOnTick(object? sender, EventArgs e)
    {
        if (IsClock)
        {
            await RotateClockHeaderAsync();
            return;
        }

        if (IsWeather)
        {
            await RotateWeatherAsync();
        }
    }

    private async Task RotateClockHeaderAsync()
    {
        CancelClockHeaderTransition();
        var cts = new CancellationTokenSource();
        _clockHeaderTransitionCts = cts;

        try
        {
            HeaderOpacity = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(180), cts.Token);
            if (!_isAttached || !IsClock)
            {
                return;
            }

            _rotationState = !_rotationState;
            UpdateClock();
            await Task.Delay(TimeSpan.FromMilliseconds(20), cts.Token);
            HeaderOpacity = 0.78;
        }
        catch (OperationCanceledException)
        {
            // Detaching or rebinding a card cancels its in-flight cross-fade.
        }
        finally
        {
            if (ReferenceEquals(_clockHeaderTransitionCts, cts))
            {
                _clockHeaderTransitionCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelClockHeaderTransition()
    {
        _clockHeaderTransitionCts?.Cancel();
        _clockHeaderTransitionCts = null;
        HeaderOpacity = 0.78;
    }

    private async Task RotateWeatherAsync()
    {
        if (!HasWeatherRain)
        {
            return;
        }

        CancelWeatherTransition();
        var cts = new CancellationTokenSource();
        _weatherTransitionCts = cts;

        try
        {
            WeatherContentOpacity = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(180), cts.Token);
            if (!_isAttached || !IsWeather || !HasWeatherRain)
            {
                return;
            }

            _rotationState = !_rotationState;
            UpdateWeather();
            await Task.Delay(TimeSpan.FromMilliseconds(20), cts.Token);
            WeatherContentOpacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Weather updates and theme reloads cancel the in-flight cross-fade.
        }
        finally
        {
            if (ReferenceEquals(_weatherTransitionCts, cts))
            {
                _weatherTransitionCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelWeatherTransition()
    {
        _weatherTransitionCts?.Cancel();
        _weatherTransitionCts = null;
        WeatherContentOpacity = 1;
    }

    private void RequestPresentationUpdate()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdatePresentation();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdatePresentation);
        }
    }

    private void UpdatePresentation()
    {
        if (IsClock)
        {
            UpdateClock();
        }
        else if (IsSchedule)
        {
            UpdateSchedule();
        }
        else if (IsWeather)
        {
            UpdateWeather();
        }
        else
        {
            UpdateGenericHeader();
        }

        UpdateTimerState();
    }

    private void UpdateGenericHeader()
    {
        if (!IsGeneric)
        {
            return;
        }

        var hostName = ReadHostComponentName();
        HeaderText = !string.IsNullOrWhiteSpace(ComponentName)
            ? ComponentName!
            : string.IsNullOrWhiteSpace(hostName) ? "组件" : hostName;
    }

    private void UpdateClock()
    {
        if (_clockContent == null)
        {
            return;
        }

        var settings = Read(_clockContent, "Settings");
        var showRealTime = ReadBool(Read(settings, "ShowRealTime"), false);
        var now = showRealTime
            ? DateTime.Now
            : _exactTimeService?.GetCurrentLocalDateTime() ?? DateTime.Now;
        var showSeconds = ReadBool(Read(_clockContent, "Settings.ShowSeconds"), true);
        ClockDisplayText = now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        HeaderText = _rotationState ? FormatChineseDate(now) : FormatChineseWeekday(now);
    }

    private void UpdateSchedule()
    {
        if (_scheduleLessonsService == null)
        {
            return;
        }

        HeaderText = "当前活动";
        switch (_scheduleLessonsService.CurrentState)
        {
            case TimeState.OnClass:
                var currentSubjectName = _scheduleLessonsService.CurrentSubject?.Name;
                if (!string.IsNullOrWhiteSpace(currentSubjectName))
                {
                    SetScheduleVisualState(onClass: true, breaking: false);
                    ScheduleIconGlyph = "\uE4CA";
                    ScheduleDisplayText = currentSubjectName;
                    ScheduleSecondaryText = "正在进行";
                }
                else
                {
                    SetNoCourseSchedule("当前无课程", "暂无课程");
                }
                break;
            case TimeState.PrepareOnClass:
                SetScheduleVisualState(onClass: false, breaking: false);
                ScheduleIconGlyph = "\uE007";
                ScheduleDisplayText = _scheduleLessonsService.NextClassSubject?.Name ?? "接下来暂无课程";
                ScheduleSecondaryText = "即将进行";
                break;
            case TimeState.Breaking:
                SetScheduleVisualState(onClass: false, breaking: true);
                ScheduleIconGlyph = "\uE003";
                ScheduleDisplayText = "课间休息";
                ScheduleSecondaryText = "休息中";
                break;
            case TimeState.AfterSchool:
                SetNoCourseSchedule("当前无课程", "今日课程已结束");
                break;
            default:
                SetNoCourseSchedule("当前无课程", "暂无课程");
                break;
        }
    }

    private void SetNoCourseSchedule(string displayText, string secondaryText)
    {
        SetScheduleVisualState(onClass: false, breaking: false);
        ScheduleIconGlyph = "\uE007";
        ScheduleDisplayText = displayText;
        ScheduleSecondaryText = secondaryText;
    }

    private void SetScheduleVisualState(bool onClass, bool breaking)
    {
        IsScheduleOnClass = onClass;
        IsScheduleBreaking = breaking;
        IsScheduleNoCourse = !onClass && !breaking;
    }

    private void UpdateWeather()
    {
        if (_weatherContent == null)
        {
            return;
        }

        var settings = Read(_weatherSettingsService, "Settings");
        var info = Read(settings, "LastWeatherInfo");
        var current = Read(info, "Current");
        var weatherCode = ReadString(Read(current, "Weather"), "99");
        var temperature = Read(current, "Temperature");
        var rainMinutes = ReadInt(Read(info, "Minutely.Precipitation.RainRemainingMinutes"));

        WeatherCode = weatherCode;
        WeatherTemperatureText = $"{ReadString(Read(temperature, "Value"))}{ReadString(Read(temperature, "Unit"))}";
        try
        {
            WeatherConditionText = _weatherService?.GetWeatherTextByCode(weatherCode) ?? "天气";
        }
        catch
        {
            WeatherConditionText = "天气";
        }

        WeatherIconGlyph = GetLucideWeatherGlyph(weatherCode);
        HasWeatherRain = rainMinutes != 0;
        RainHeaderText = rainMinutes < 0 ? "正在下雨" : "即将下雨";

        if (HasWeatherRain)
        {
            IsWeatherRainDisplay = _rotationState;
            RainDisplayText = rainMinutes > 0
                ? $"{rainMinutes}分钟后"
                : $"还将持续{Math.Abs(rainMinutes)}分钟";
        }
        else
        {
            _rotationState = false;
            IsWeatherRainDisplay = false;
            RainDisplayText = "";
            CancelWeatherTransition();
        }

        HeaderText = IsWeatherRainDisplay
            ? RainHeaderText
            : CleanCityName(ReadString(Read(settings, "CityName")));
    }

    private void UpdateTimerState()
    {
        var shouldRun = _isAttached && (IsClock || (IsWeather && HasWeatherRain));
        if (shouldRun)
        {
            var interval = IsClock ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(10);
            if (_rotationTimer.Interval != interval)
            {
                _rotationTimer.Stop();
                _rotationTimer.Interval = interval;
            }

            _rotationTimer.Start();
        }
        else
        {
            _rotationTimer.Stop();
        }
    }

    private static bool IsComponent(object? value, string name) =>
        value?.GetType().Name.Equals(name, StringComparison.Ordinal) == true;

    private static INotifyPropertyChanged? AsNotifier(object? value) => value as INotifyPropertyChanged;

    private static object? Read(object? source, string path)
    {
        var current = source;
        foreach (var segment in path.Split('.'))
        {
            if (current == null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    private static string ReadString(object? value, string fallback = "") =>
        value?.ToString() ?? fallback;

    private static int ReadInt(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            _ when int.TryParse(value.ToString(), out var number) => number,
            _ => 0
        };
    }

    private static bool ReadBool(object? value, bool fallback)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        return bool.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
    }

    private string ReadHostComponentName()
    {
        for (Visual? current = this; current != null; current = current.GetVisualParent())
        {
            if (!string.Equals(current.GetType().Name, "ComponentPresenter", StringComparison.Ordinal))
            {
                continue;
            }

            var settings = Read(current, "Settings");
            var name = ReadString(Read(settings, "AssociatedComponentInfo.Name"));
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var cachedName = ReadString(Read(settings, "NameCache"));
            if (!string.IsNullOrWhiteSpace(cachedName))
            {
                return cachedName;
            }
        }

        return string.Empty;
    }

    private static string CleanCityName(string cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return "天气";
        }

        var value = cityName.Trim();
        var parenIndex = value.IndexOfAny(['(', '（']);
        return (parenIndex > 0 ? value[..parenIndex] : value).Trim();
    }

    private static string FormatChineseWeekday(DateTime value) => value.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    private static string FormatChineseDate(DateTime value) => $"{value.Month}月{value.Day}日";

    private static string GetLucideWeatherGlyph(string code) => code switch
    {
        "0" => "\uE2B1",
        "1" => "\uE216",
        "2" => "\uE217",
        "3" => "\uE2FB",
        "4" => "\uE090",
        "5" or "19" => "\uE08F",
        "6" or "14" or "15" or "16" or "17" or "26" or "27" or "28" or "34" or "302" => "\uE094",
        "7" => "\uE08E",
        "8" or "9" or "21" or "22" or "301" => "\uE092",
        "10" or "11" or "12" or "23" or "24" or "25" or "32" => "\uE093",
        "13" => "\uE376",
        "18" or "35" => "\uE214",
        "20" or "31" or "33" => "\uE218",
        "29" or "30" or "53" => "\uE0F4",
        _ => "\uE091"
    };

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
