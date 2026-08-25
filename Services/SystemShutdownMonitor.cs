using System;
using System.Threading;
using System.Windows.Forms;

namespace SystemTools.Services;

public sealed class SystemShutdownMonitor : NativeWindow, IDisposable
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;

    internal const string WindowCaption = "SystemTools.SystemShutdownMonitor";

    private int _isSessionEnding;
    private int _isStarted;

    public bool IsSessionEnding => Volatile.Read(ref _isSessionEnding) != 0;

    public void Start()
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
        {
            return;
        }

        try
        {
            CreateHandle(new CreateParams
            {
                Caption = WindowCaption
            });
        }
        catch
        {
            Volatile.Write(ref _isStarted, 0);
            throw;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmQueryEndSession:
                Volatile.Write(ref _isSessionEnding, 1);
                break;
            case WmEndSession:
                Volatile.Write(ref _isSessionEnding, m.WParam != IntPtr.Zero ? 1 : 0);
                break;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isStarted, 0) == 0)
        {
            return;
        }

        DestroyHandle();
    }
}
