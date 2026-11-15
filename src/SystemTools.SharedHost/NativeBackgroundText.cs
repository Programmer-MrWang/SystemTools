using SystemTools.Contracts;
using Avalonia.Threading;
using SystemTools.Common.Layout;

namespace SystemTools;

public sealed partial class NativeBackgroundAppearance
{
    private readonly TextOcclusionPolicy _occlusion = new();
    private bool _hidWindow;
    partial void VisibilityChanged(string property)
    {
        if (_hidWindow && property == "IsMainWindowVisible")
        {
            _hidWindow = false; _occlusion.Reset();
            config.Data.AutoHideMainWindowWhenOccluded = false; config.Save();
        }
    }
    partial void RestoreVisibility()
    {
        if (_hidWindow && _settings.GetMainWindowVisible() == false) _settings.SetMainWindowVisible(true);
        _hidWindow = false; _occlusion.Reset();
    }
    private partial async Task ProcessTextAsync(BackgroundFrame frame, CancellationToken token)
    {
        if (!config.Data.AutoHideMainWindowWhenOccluded)
        { await Dispatcher.UIThread.InvokeAsync(RestoreVisibility); return; }
        var recognition = native.Advanced.TextRecognition ?? throw new InvalidOperationException("当前平台未接入可靠的 OCR 避让。");
        var state = recognition.Probe(); if (!state.CanExecute) throw new NativeFeatureException(state.State, state.Reason);
        var boxes = await recognition.RecognizeAsync(frame, token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || !config.Data.AutoHideMainWindowWhenOccluded) return;
            if (_occlusion.Observe(frame.Region, boxes))
            {
                if (!_hidWindow && _settings.GetMainWindowVisible() == true)
                    _hidWindow = _settings.SetMainWindowVisible(false);
            }
            else
            {
                if (_hidWindow && _settings.GetMainWindowVisible() == false) _settings.SetMainWindowVisible(true);
                _hidWindow = false;
            }
        });
    }
}
