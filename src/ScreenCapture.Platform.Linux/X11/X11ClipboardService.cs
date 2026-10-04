using System.Runtime.InteropServices;
using ScreenCapture.Core.Imaging;
using ScreenCapture.Core.Models;
using ScreenCapture.Platform;

namespace ScreenCapture.Platform.Linux.X11;

public sealed class X11ClipboardService : IClipboardService, IDisposable
{
    private const int SelectionRequest = 30;
    private const int SelectionNotify = 31;
    private const int SelectionClear = 29;

    private const int PropertyNotify = 28;

    private const int PropertyChangeMask = 1 << 22;

    private const int PropModeReplace = 0;

    private static readonly IntPtr None = IntPtr.Zero;

    private readonly object _sync = new();

    private readonly AutoResetEvent _updateEvent = new(false);

    private readonly Thread _clipboardThread;

    private readonly TaskCompletionSource<bool> _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private byte[]? _pngData;

    private bool _disposed;

    private IntPtr _display;

    private IntPtr _window;

    private IntPtr _clipboardAtom;

    private IntPtr _targetsAtom;

    private IntPtr _imagePngAtom;

    private IntPtr _atomAtom;

    public X11ClipboardService()
    {
        _clipboardThread =
            new Thread(ClipboardThreadMain)
            {
                IsBackground = true,
                Name = "ScreenCapture X11 Clipboard"
            };

        _clipboardThread.Start();
    }

    public async Task SetImageAsync(
        CaptureResult capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        cancellationToken.ThrowIfCancellationRequested();

        var png =
            PngEncoder.EncodeRgba(
                capture.PixelData,
                capture.Width,
                capture.Height);

        lock (_sync)
        {
            ThrowIfDisposed();

            _pngData = png;
        }

        await _initialization.Task.WaitAsync(
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        _updateEvent.Set();
    }

    private void ClipboardThreadMain()
    {
        try
        {
            _display =
                XOpenDisplay(IntPtr.Zero);

            if (_display == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Unable to connect to the X11 display.");
            }

            _clipboardAtom =
                XInternAtom(
                    _display,
                    "CLIPBOARD",
                    false);

            _targetsAtom =
                XInternAtom(
                    _display,
                    "TARGETS",
                    false);

            _imagePngAtom =
                XInternAtom(
                    _display,
                    "image/png",
                    false);

            _atomAtom =
                XInternAtom(
                    _display,
                    "ATOM",
                    false);

            _window =
                XCreateSimpleWindow(
                    _display,
                    XDefaultRootWindow(_display),
                    0,
                    0,
                    1,
                    1,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);

            if (_window == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Unable to create the X11 clipboard owner window.");
            }

            XSelectInput(
                _display,
                _window,
                PropertyChangeMask);

            XMapWindow(
                _display,
                _window);

            XFlush(_display);

            _initialization.TrySetResult(true);

            while (!_disposed)
            {
                ProcessPendingUpdate();

                while (!_disposed &&
                       XPending(_display) > 0)
                {
                    XNextEvent(
                        _display,
                        out var xEvent);

                    ProcessEvent(xEvent);
                }

                _updateEvent.WaitOne(20);
            }
        }
        catch (Exception ex)
        {
            _initialization.TrySetException(ex);
        }
        finally
        {
            if (_display != IntPtr.Zero)
            {
                if (_window != IntPtr.Zero)
                {
                    XDestroyWindow(
                        _display,
                        _window);

                    _window = IntPtr.Zero;
                }

                XCloseDisplay(_display);

                _display = IntPtr.Zero;
            }
        }
    }

    private void ProcessPendingUpdate()
    {
        if (_display == IntPtr.Zero ||
            _window == IntPtr.Zero)
        {
            return;
        }

        lock (_sync)
        {
            if (_pngData is null)
                return;

            XSetSelectionOwner(
                _display,
                _clipboardAtom,
                _window,
                CurrentTime);

            XFlush(_display);
        }
    }

    private void ProcessEvent(
        XEvent xEvent)
    {
        switch (xEvent.Type)
        {
            case SelectionRequest:
                HandleSelectionRequest(
                    xEvent.SelectionRequest);
                break;

            case SelectionClear:
                // Another application took ownership
                // of the clipboard.
                break;

            case PropertyNotify:
                // Reserved for future INCR support.
                break;
        }
    }

    private void HandleSelectionRequest(
        XSelectionRequestEvent request)
    {
        var property =
            request.Property == None
                ? request.Target
                : request.Property;

        var success = false;

        if (request.Target == _targetsAtom)
        {
            var targets =
                new[]
                {
                    _targetsAtom,
                    _imagePngAtom
                };

            var targetData =
                new byte[targets.Length * 4];

            for (var i = 0;
                 i < targets.Length;
                 i++)
            {
                var value =
                    unchecked(
                        (uint)targets[i].ToInt64());

                BitConverter
                    .GetBytes(value)
                    .CopyTo(
                        targetData,
                        i * 4);
            }

            XChangeProperty(
                _display,
                request.Requestor,
                property,
                _atomAtom,
                32,
                PropModeReplace,
                targetData,
                targets.Length);

            success = true;
        }
        else if (request.Target == _imagePngAtom)
        {
            byte[]? png;

            lock (_sync)
            {
                png = _pngData;
            }

            if (png is not null)
            {
                XChangeProperty(
                    _display,
                    request.Requestor,
                    property,
                    _imagePngAtom,
                    8,
                    PropModeReplace,
                    png,
                    png.Length);

                success = true;
            }
        }

        var notify =
            new XEvent
            {
                Type = SelectionNotify,
                SelectionNotify = new XSelectionEvent
                {
                    Type = SelectionNotify,
                    Serial = IntPtr.Zero,
                    SendEvent = 1,
                    Display = _display,
                    Requestor = request.Requestor,
                    Selection = request.Selection,
                    Target = request.Target,
                    Property = success
                        ? property
                        : None,
                    Time = request.Time
                }
            };

        XSendEvent(
            _display,
            request.Requestor,
            false,
            0,
            ref notify);

        XFlush(_display);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(X11ClipboardService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _updateEvent.Set();

        if (_clipboardThread.IsAlive &&
            !ReferenceEquals(
                Thread.CurrentThread,
                _clipboardThread))
        {
            _clipboardThread.Join(1000);
        }

        _updateEvent.Dispose();
    }

    private const long CurrentTime = 0;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(
        IntPtr display,
        [MarshalAs(UnmanagedType.LPStr)]
        string atomName,
        bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateSimpleWindow(
        IntPtr display,
        IntPtr parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        IntPtr border,
        IntPtr background);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyWindow(
        IntPtr display,
        IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(
        IntPtr display,
        IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XSelectInput(
        IntPtr display,
        IntPtr window,
        long eventMask);

    [DllImport("libX11.so.6")]
    private static extern int XSetSelectionOwner(
        IntPtr display,
        IntPtr selection,
        IntPtr owner,
        long time);

    [DllImport("libX11.so.6")]
    private static extern int XPending(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(
        IntPtr display,
        out XEvent eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        byte[] data,
        int nelements);

    [DllImport("libX11.so.6")]
    private static extern int XSendEvent(
        IntPtr display,
        IntPtr window,
        bool propagate,
        long eventMask,
        ref XEvent sendEvent);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(
        IntPtr display);

    [StructLayout(LayoutKind.Sequential)]
    private struct XSelectionRequestEvent
    {
        public int Type;
        public IntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Owner;
        public IntPtr Requestor;
        public IntPtr Selection;
        public IntPtr Target;
        public IntPtr Property;
        public IntPtr Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSelectionEvent
    {
        public int Type;
        public IntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Requestor;
        public IntPtr Selection;
        public IntPtr Target;
        public IntPtr Property;
        public IntPtr Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XPropertyEvent
    {
        public int Type;
        public IntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Window;
        public IntPtr Atom;
        public int State;
        public IntPtr Time;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)]
        public int Type;

        [FieldOffset(0)]
        public XSelectionRequestEvent SelectionRequest;

        [FieldOffset(0)]
        public XSelectionEvent SelectionNotify;

        [FieldOffset(0)]
        public XPropertyEvent Property;
    }
}