using OpenCvSharp;
using System;
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

    private void CaptureLoop()
    {
        using var frame = new Mat();
        
        while (_cts?.IsCancellationRequested == false)
        {
            if (_capture?.Read(frame) == true && !frame.Empty())
            {
                FrameCaptured?.Invoke(this, frame.Clone());
            }
            Thread.Sleep(33);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _captureTask?.Wait(500);
        
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
        catch { /* 防止硬件已被拔出等异常导致报错 */ }
        finally
        {
            _capture = null;
        }
    }


    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}