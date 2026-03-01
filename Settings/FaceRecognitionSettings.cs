using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class FaceRecognitionSettings : ObservableObject
{
    public string? FaceTemplate { get; set; }
    
    public double Threshold { get; set; } = 0.5;

    [JsonIgnore] private bool _operating;
    [JsonIgnore] public bool Operating { get => _operating; set => SetProperty(ref _operating, value); }

    [JsonIgnore] private bool _operationFinished;
    [JsonIgnore] public bool OperationFinished { get => _operationFinished; set => SetProperty(ref _operationFinished, value); }
}