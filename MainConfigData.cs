using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SystemTools.ConfigHandlers;

public class MainConfigData : INotifyPropertyChanged
{
    public event EventHandler? RestartPropertyChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    bool _enableExperimentalFeatures;

    [JsonPropertyName("enableExperimentalFeatures")]
    public bool EnableExperimentalFeatures
    {
        get => _enableExperimentalFeatures;
        set
        {
            if (value == _enableExperimentalFeatures) return;
            _enableExperimentalFeatures = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    bool _enableFfmpegFeatures;

    [JsonPropertyName("enableFfmpegFeatures")]
    public bool EnableFfmpegFeatures
    {
        get => _enableFfmpegFeatures;
        set
        {
            if (value == _enableFfmpegFeatures) return;
            _enableFfmpegFeatures = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}