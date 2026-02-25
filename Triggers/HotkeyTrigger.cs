using System;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.HotkeyTrigger", "按下自定义热键时", "\uEA0B")]
public class HotkeyTrigger : TriggerBase<HotkeyTriggerConfig>
{
    private readonly ILogger<HotkeyTrigger> _logger;
    private readonly IHotkeyService _hotkeyService;
    private int _hotkeyId = -1;

    public HotkeyTrigger(ILogger<HotkeyTrigger> logger, IHotkeyService hotkeyService)
    {
        _logger = logger;
        _hotkeyService = hotkeyService;
    }

    public override void Loaded()
    {
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        Settings.PropertyChanged += OnSettingsPropertyChanged;

        RegisterHotkey();
    }

    public override void UnLoaded()
    {
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;

        UnregisterHotkey();

        _logger.LogDebug("热键触发器已卸载");
    }

    private void RegisterHotkey()
    {
        UnregisterHotkey();

        var modifiers = Settings.ModifierKeys;
        var vk = Settings.VirtualKey;

        if (vk == 0)
        {
            vk = 0x78;
            modifiers = 0;
        }

        _hotkeyId = _hotkeyService.RegisterHotkey(modifiers, vk);

        _logger.LogInformation("热键触发器已加载，监听: {Hotkey}",
            _hotkeyService.GetHotkeyDisplay(modifiers, vk));
    }

    private void UnregisterHotkey()
    {
        if (_hotkeyId >= 0)
        {
            _hotkeyService.UnregisterHotkey(_hotkeyId);
            _hotkeyId = -1;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 当热键相关属性变更时，重新注册
        if (e.PropertyName == nameof(HotkeyTriggerConfig.ModifierKeys) ||
            e.PropertyName == nameof(HotkeyTriggerConfig.VirtualKey))
        {
            _logger.LogInformation("热键配置已变更，重新注册热键");
            RegisterHotkey();
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        // 检查是否是注册的热键
        if (e.ModifierKeys != Settings.ModifierKeys || e.VirtualKey != Settings.VirtualKey)
            return;

        // 防抖动
        if (DateTime.Now - Settings.LastTriggered < TimeSpan.FromMilliseconds(500))
            return;

        Settings.LastTriggered = DateTime.Now;
        _logger.LogInformation("热键 {Hotkey} 按下，触发自动化", Settings.HotkeyDisplay);
        Trigger();
    }
}