using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Models.Profile;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using SystemTools.ConfigHandlers;

namespace SystemTools.Services;

public sealed class VirtualAfterSchoolService(
    MainConfigHandler configHandler,
    ILessonsService lessonsService,
    IExactTimeService exactTimeService,
    ILogger<VirtualAfterSchoolService> logger)
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherTimer _monitorTimer = new() { Interval = MonitorInterval };
    private readonly Stopwatch _activeStopwatch = new();
    private DateTime? _lastObservedSoftwareTime;
    private DateOnly? _lastTriggeredDate;
    private TimeSpan _observedTriggerTime;
    private int _activeDurationSeconds;
    private bool _isStarted;
    private bool _isVirtualStateActive;

    public bool IsVirtualStateActive => _isVirtualStateActive;

    public void Start()
    {
        if (_isStarted)
        {
            ApplyConfig();
            return;
        }

        _isStarted = true;
        _monitorTimer.Tick += OnMonitorTick;
        configHandler.Data.PropertyChanged += OnConfigPropertyChanged;
        ApplyConfig();
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        _monitorTimer.Stop();
        _monitorTimer.Tick -= OnMonitorTick;
        configHandler.Data.PropertyChanged -= OnConfigPropertyChanged;
        EndVirtualState(resumeLessonsTimer: false);
        _lastObservedSoftwareTime = null;
    }

    public void ApplyConfig()
    {
        if (!_isStarted)
        {
            return;
        }

        var config = configHandler.Data;
        if (!config.VirtualAfterSchoolEnabled)
        {
            _monitorTimer.Stop();
            EndVirtualState();
            _lastObservedSoftwareTime = null;
            return;
        }

        var now = exactTimeService.GetCurrentLocalDateTime();
        if (_observedTriggerTime != config.VirtualAfterSchoolTriggerTime)
        {
            _observedTriggerTime = config.VirtualAfterSchoolTriggerTime;
            _lastTriggeredDate = null;
        }
        _lastObservedSoftwareTime = now;

        if (!_monitorTimer.IsEnabled)
        {
            _monitorTimer.Start();
        }
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainConfigData.VirtualAfterSchoolEnabled)
            or nameof(MainConfigData.VirtualAfterSchoolTriggerTime))
        {
            ApplyConfig();
        }
    }

    private void OnMonitorTick(object? sender, EventArgs e)
    {
        try
        {
            if (_isVirtualStateActive)
            {
                if (_activeStopwatch.Elapsed >= TimeSpan.FromSeconds(_activeDurationSeconds))
                {
                    EndVirtualState();
                    return;
                }

                if (lessonsService.IsTimerRunning)
                {
                    lessonsService.StopMainTimer();
                }
                WriteVirtualAfterSchoolState();
                InvokeLessonsServiceEvent("PreMainTimerTicked");
                WriteVirtualAfterSchoolState();
                InvokeLessonsServiceEvent("PostMainTimerTicked");
                WriteVirtualAfterSchoolState();
                if (lessonsService.IsTimerRunning)
                {
                    lessonsService.StopMainTimer();
                }
                return;
            }

            if (!configHandler.Data.VirtualAfterSchoolEnabled)
            {
                ApplyConfig();
                return;
            }

            var now = exactTimeService.GetCurrentLocalDateTime();
            var previous = _lastObservedSoftwareTime ?? now;
            _lastObservedSoftwareTime = now;

            if (now <= previous)
            {
                return;
            }

            var target = now.Date + configHandler.Data.VirtualAfterSchoolTriggerTime;
            if (previous < target && target <= now && _lastTriggeredDate != DateOnly.FromDateTime(target))
            {
                _lastTriggeredDate = DateOnly.FromDateTime(target);
                BeginVirtualState(target);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "虚拟放学状态监视失败，将恢复 ClassIsland 课程状态识别。");
            EndVirtualState();
        }
    }

    private void BeginVirtualState(DateTime scheduledTime)
    {
        if (_isVirtualStateActive)
        {
            return;
        }

        _isVirtualStateActive = true;
        _activeDurationSeconds = Math.Clamp(
            configHandler.Data.VirtualAfterSchoolDurationSeconds,
            1,
            7200);
        _activeStopwatch.Restart();

        lessonsService.StopMainTimer();

        var stateChanged = lessonsService.CurrentState != TimeState.AfterSchool;
        WriteVirtualAfterSchoolState();

        if (stateChanged)
        {
            InvokeLessonsServiceMethod("DebugTriggerOnStateChanged");
        }
        InvokeLessonsServiceMethod("DebugTriggerOnAfterSchool");
        WriteVirtualAfterSchoolState();
        SetLessonsServiceProperty("CurrentOverlayEventStatus", TimeState.AfterSchool);

        logger.LogInformation(
            "虚拟放学状态已触发。ScheduledSoftwareTime={ScheduledSoftwareTime}, DurationSeconds={DurationSeconds}",
            scheduledTime,
            _activeDurationSeconds);
    }

    private void WriteVirtualAfterSchoolState()
    {
        lessonsService.CurrentSelectedIndex = -1;
        lessonsService.CurrentState = TimeState.AfterSchool;
        lessonsService.CurrentSubject = Subject.Fallback;
        lessonsService.NextClassSubject = Subject.Fallback;
        lessonsService.CurrentTimeLayoutItem = TimeLayoutItem.Empty;
        lessonsService.NextClassTimeLayoutItem = TimeLayoutItem.Empty;
        lessonsService.NextBreakingTimeLayoutItem = TimeLayoutItem.Empty;
        lessonsService.OnClassLeftTime = TimeSpan.Zero;
        lessonsService.OnBreakingTimeLeftTime = TimeSpan.Zero;
        lessonsService.IsLessonConfirmed = false;
        lessonsService.IsClassPlanLoaded = lessonsService.CurrentClassPlan?.TimeLayout != null;
    }

    private void EndVirtualState(bool resumeLessonsTimer = true)
    {
        if (!_isVirtualStateActive)
        {
            return;
        }

        _isVirtualStateActive = false;
        _activeStopwatch.Reset();

        // 保留 AfterSchool 作为上一个事件状态。恢复后的首个课程 Tick 会据此
        // 发布真实状态的变更事件，并重新填充全部课程字段。
        SetLessonsServiceProperty("CurrentOverlayEventStatus", TimeState.AfterSchool);
        if (resumeLessonsTimer)
        {
            lessonsService.StartMainTimer();
        }
        _lastObservedSoftwareTime = exactTimeService.GetCurrentLocalDateTime();
        logger.LogInformation(
            resumeLessonsTimer
                ? "虚拟放学状态已结束，ClassIsland 课程状态识别已恢复。"
                : "虚拟放学状态已结束，应用正在退出，课程计时器保持停止。");
    }

    private void InvokeLessonsServiceMethod(string methodName)
    {
        try
        {
            var method = lessonsService.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                logger.LogWarning("无法调用 ClassIsland 课程服务方法 {MethodName}。", methodName);
                return;
            }
            method.Invoke(lessonsService, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "调用 ClassIsland 课程服务方法 {MethodName} 失败。", methodName);
        }
    }

    private void SetLessonsServiceProperty(string propertyName, object value)
    {
        try
        {
            var property = lessonsService.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite != true)
            {
                logger.LogWarning("无法写入 ClassIsland 课程服务属性 {PropertyName}。", propertyName);
                return;
            }
            property.SetValue(lessonsService, value);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "写入 ClassIsland 课程服务属性 {PropertyName} 失败。", propertyName);
        }
    }

    private void InvokeLessonsServiceEvent(string eventName)
    {
        var type = lessonsService.GetType();
        while (type != null)
        {
            var field = type.GetField(
                eventName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.GetValue(lessonsService) is EventHandler handler)
            {
                foreach (EventHandler subscriber in handler.GetInvocationList())
                {
                    try
                    {
                        subscriber.Invoke(lessonsService, EventArgs.Empty);
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            exception,
                            "虚拟放学状态补发 {EventName} 时，订阅者执行失败。",
                            eventName);
                    }
                }
                return;
            }
            type = type.BaseType;
        }
    }
}
