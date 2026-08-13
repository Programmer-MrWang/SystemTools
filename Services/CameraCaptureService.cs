using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public class CameraCaptureService : IDisposable
{
    private static readonly object CameraReservationLock = new();
    private static readonly HashSet<int> ReservedCameraIndexes = [];

    private readonly object _lifecycleLock = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private int? _reservedCameraIndex;
    private bool _isStopping;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
            {
                return !_disposed &&
                       !_isStopping &&
                       _captureTask is { IsCompleted: false } &&
                       (_capture?.IsOpened() ?? false);
            }
        }
    }
    
    public event EventHandler<Mat>? FrameCaptured;

    public bool Start(int cameraIndex = 0, int width = 640, int height = 480)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!StopCore() || !TryReserveCamera(cameraIndex))
            {
                return false;
            }

            VideoCapture? capture = null;
            CancellationTokenSource? pendingCts = null;
            var reservationOwnedByFields = false;
            try
            {
                var openedCapture = new VideoCapture(cameraIndex);
                capture = openedCapture;
                if (!openedCapture.IsOpened())
                {
                    capture.Dispose();
                    ReleaseCameraReservation(cameraIndex);
                    return false;
                }

                openedCapture.FrameWidth = width;
                openedCapture.FrameHeight = height;

                var cts = new CancellationTokenSource();
                pendingCts = cts;
                var token = cts.Token;
                var captureTask = Task.Run(() => CaptureLoop(openedCapture, token), token);

                _capture = capture;
                _cts = cts;
                _captureTask = captureTask;
                _reservedCameraIndex = cameraIndex;
                reservationOwnedByFields = true;
                pendingCts = null;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start camera: {ex.Message}");
                pendingCts?.Dispose();
                capture?.Dispose();
                if (!reservationOwnedByFields)
                {
                    ReleaseCameraReservation(cameraIndex);
                }
                return false;
            }
        }
    }

    private void CaptureLoop(VideoCapture capture, CancellationToken cancellationToken)
    {
        using var frame = new Mat();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (capture.Read(frame))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (frame.Empty())
                    {
                        continue;
                    }

                    var handler = FrameCaptured;
                    if (handler != null)
                    {
                        var capturedFrame = frame.Clone();
                        try
                        {
                            // The subscriber takes ownership of this cloned frame.
                            handler(this, capturedFrame);
                        }
                        catch
                        {
                            capturedFrame.Dispose();
                            throw;
                        }
                    }
                }

                if (cancellationToken.WaitHandle.WaitOne(33))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Camera capture loop stopped unexpectedly: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopCore();
        }
    }

    private bool StopCore()
    {
        if (_isStopping)
        {
            return false;
        }

        if (_capture == null && _captureTask == null && _cts == null)
        {
            return true;
        }

        var cts = _cts;
        var captureTask = _captureTask;
        var capture = _capture;
        var cameraIndex = _reservedCameraIndex;

        _cts = null;
        _captureTask = null;
        _capture = null;
        _reservedCameraIndex = null;

        cts?.Cancel();

        var captureTaskCompleted = captureTask == null;
        try
        {
            // Do not release the native capture object until Read() and the
            // frame callback have both returned.
            captureTaskCompleted = captureTask?.Wait(1000) ?? true;
        }
        catch (AggregateException ex)
        {
            captureTaskCompleted = true;
            if (ex.InnerExceptions.Any(inner => inner is not OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"Failed while stopping camera capture: {ex.Flatten().Message}");
            }
        }

        if (!captureTaskCompleted && captureTask != null)
        {
            _isStopping = true;
            _ = captureTask.ContinueWith(completedTask =>
            {
                _ = completedTask.Exception;
                ReleaseCapture(capture, cts, cameraIndex);
                lock (_lifecycleLock)
                {
                    _isStopping = false;
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }

        ReleaseCapture(capture, cts, cameraIndex);
        return true;
    }

    private static void ReleaseCapture(VideoCapture? capture, CancellationTokenSource? cts, int? cameraIndex)
    {
        try
        {
            if (capture?.IsOpened() == true)
            {
                capture.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to release camera: {ex.Message}");
        }
        finally
        {
            capture?.Dispose();
            cts?.Dispose();
            if (cameraIndex is { } index)
            {
                ReleaseCameraReservation(index);
            }
        }
    }

    private static bool TryReserveCamera(int cameraIndex)
    {
        lock (CameraReservationLock)
        {
            return ReservedCameraIndexes.Add(cameraIndex);
        }
    }

    private static void ReleaseCameraReservation(int cameraIndex)
    {
        lock (CameraReservationLock)
        {
            ReservedCameraIndexes.Remove(cameraIndex);
        }
    }


    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
        }

        GC.SuppressFinalize(this);
    }
}
