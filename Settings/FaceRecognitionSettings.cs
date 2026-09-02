using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class FaceRecognitionSettings : ObservableObject
{
    private string? _faceTemplate;
    private string _statusMessage = string.Empty;
    private string _captureButtonText = "捕获并保存人脸";
    private string _cameraPlaceholderText = "正在准备摄像头画面";
    private bool _hasError;
    private bool _canRetry;
    private bool _cameraReady;

    /// <summary>
    /// 已录入的人脸特征
    /// </summary>
    public string? FaceTemplate
    {
        get => _faceTemplate;
        set => SetProperty(ref _faceTemplate, value);
    }
    
    public double Threshold { get; set; } = 0.5;

    [JsonIgnore]
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    [JsonIgnore]
    public string CaptureButtonText
    {
        get => _captureButtonText;
        set => SetProperty(ref _captureButtonText, value);
    }

    [JsonIgnore]
    public string CameraPlaceholderText
    {
        get => _cameraPlaceholderText;
        set => SetProperty(ref _cameraPlaceholderText, value);
    }

    [JsonIgnore]
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    [JsonIgnore]
    public bool CanRetry
    {
        get => _canRetry;
        set => SetProperty(ref _canRetry, value);
    }

    [JsonIgnore]
    public bool CameraReady
    {
        get => _cameraReady;
        set => SetProperty(ref _cameraReady, value);
    }

    [JsonIgnore] private bool _operating;
    [JsonIgnore] public bool Operating { get => _operating; set => SetProperty(ref _operating, value); }

    [JsonIgnore] private bool _operationFinished;
    [JsonIgnore] public bool OperationFinished { get => _operationFinished; set => SetProperty(ref _operationFinished, value); }
}
