using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public class CameraCaptureService : IDisposable
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public bool IsRunning => _capture?.IsOpened() ?? false;
    
    public event EventHandler<Mat>? FrameCaptured;

    public bool Start(int cameraIndex = 0, int width = 640, int height = 480)
    {
        Stop();
        
        _capture = new VideoCapture(cameraIndex);
        if (!_capture.IsOpened()) return false;

        _capture.FrameWidth = width;
        _capture.FrameHeight = height;
        
        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(CaptureLoop, _cts.Token);
        
        return true;
    }

    private async Task CaptureLoop()
    {
        using var frame = new Mat();
        var token = _cts?.Token ?? CancellationToken.None;
        
        while (!token.IsCancellationRequested)
        {
            if (_capture?.Read(frame) == true && !frame.Empty() && FrameCaptured != null)
            {
                try
                {
                    // 由服务层管理原始帧生命周期；订阅方如需跨线程/跨作用域使用应自行 Clone。
                    using var snapshot = frame.Clone();
                    FrameCaptured.Invoke(this, snapshot);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemTools] FrameCaptured 处理异常: {ex}");
                }
            }

            try
            {
                await Task.Delay(33, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _captureTask?.Wait(1000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemTools] 等待摄像头采集任务结束失败: {ex}");
        }
        
        try
        {
            if (_capture != null)
            {
                if (_capture.IsOpened())
                {
                    _capture.Release();
                }
                _capture.Dispose();
            }
        }
        catch (Exception ex)
        {
            // 防止硬件已被拔出等异常导致报错
            Debug.WriteLine($"[SystemTools] 停止摄像头时出现异常: {ex}");
        }
        finally
        {
            _capture = null;
            _captureTask = null;
            _cts?.Dispose();
            _cts = null;
        }
    }


    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
