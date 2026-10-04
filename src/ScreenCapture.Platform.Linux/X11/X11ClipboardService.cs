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
    private const int AnyPropertyType = 0;
    private const int Success = 0;
    private const long CurrentTime = 0;

    private readonly object _sync = new();
    private readonly AutoResetEvent _updateEvent = new(false);
    private readonly Thread _clipboardThread;
    private readonly TaskCompletionSource<bool> _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private byte[]? _pngData;
    private bool _clipboardUpdatePending;
    private bool _disposed;
    private IntPtr _display;
    private IntPtr _window;

    private IntPtr _clipboardAtom;
    private IntPtr _targetsAtom;
    private IntPtr _multipleAtom;
    private IntPtr _timestampAtom;
    private IntPtr _saveTargetsAtom;
    private IntPtr _imagePngAtom;
    private IntPtr _atomAtom;
    private IntPtr _integerAtom;

    private uint _clipboardTimestamp;

    public X11ClipboardService()
    {
        _clipboardThread = new Thread(ClipboardThreadMain)
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

        var png = PngEncoder.EncodeRgba(
            capture.PixelData,
            capture.Width,
            capture.Height);

        lock (_sync)
        {
            ThrowIfDisposed();
            _pngData = png;
            _clipboardUpdatePending = true;
        }

        await _initialization.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _updateEvent.Set();
    }

    private void ClipboardThreadMain()
    {
        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to connect to the X11 display.");
            }

            _clipboardAtom = GetAtom("CLIPBOARD");
            _targetsAtom = GetAtom("TARGETS");
            _multipleAtom = GetAtom("MULTIPLE");
            _timestampAtom = GetAtom("TIMESTAMP");
            _saveTargetsAtom = GetAtom("SAVE_TARGETS");
            _imagePngAtom = GetAtom("image/png");
            _atomAtom = GetAtom("ATOM");
            _integerAtom = GetAtom("INTEGER");

            _window = XCreateSimpleWindow(
                _display,
                XDefaultRootWindow(_display),
                0, 0, 1, 1, 0,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_window == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create the X11 clipboard owner window.");
            }

            XSelectInput(_display, _window, PropertyChangeMask);
            XMapWindow(_display, _window);
            XFlush(_display);

            _initialization.TrySetResult(true);

            while (!_disposed)
            {
                ProcessPendingUpdate();

                while (!_disposed && XPending(_display) > 0)
                {
                    XNextEvent(_display, out var xEvent);
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
                    XDestroyWindow(_display, _window);
                    _window = IntPtr.Zero;
                }

                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
    }

    private IntPtr GetAtom(string name) => XInternAtom(_display, name, false);

    private void ProcessPendingUpdate()
    {
        if (_display == IntPtr.Zero || _window == IntPtr.Zero) return;

        lock (_sync)
        {
            if (!_clipboardUpdatePending || _pngData is null) return;

            XSetSelectionOwner(_display, _clipboardAtom, _window, CurrentTime);
            XFlush(_display);

            if (XGetSelectionOwner(_display, _clipboardAtom) == _window)
            {
                _clipboardUpdatePending = false;
                _clipboardTimestamp = unchecked((uint)Environment.TickCount64);
            }
        }
    }

    private void ProcessEvent(XEvent xEvent)
    {
        switch (xEvent.Type)
        {
            case SelectionRequest:
                HandleSelectionRequest(xEvent.SelectionRequest);
                break;
            case SelectionClear:
            case PropertyNotify:
                break;
        }
    }

    private void HandleSelectionRequest(XSelectionRequestEvent request)
    {
        var property = request.Property == IntPtr.Zero ? request.Target : request.Property;
        var success = false;

        if (request.Target == _targetsAtom)
        {
            success = HandleTargetsRequest(request.Requestor, property);
        }
        else if (request.Target == _multipleAtom)
        {
            success = HandleMultipleRequest(request.Requestor, property);
        }
        else if (request.Target == _timestampAtom)
        {
            success = HandleTimestampRequest(request.Requestor, property);
        }
        else if (request.Target == _saveTargetsAtom)
        {
            success = true;
        }
        else if (request.Target == _imagePngAtom)
        {
            success = HandlePngRequest(request.Requestor, property);
        }

        var notify = new XEvent
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
                Property = success ? property : IntPtr.Zero,
                Time = request.Time
            }
        };

        XSendEvent(_display, request.Requestor, false, 0, ref notify);
        XFlush(_display);
    }

    private bool HandleTargetsRequest(IntPtr requestor, IntPtr property)
    {
        var targets = new[] { _targetsAtom, _multipleAtom, _timestampAtom, _saveTargetsAtom, _imagePngAtom };
        var targetData = new byte[targets.Length * 4];

        for (var i = 0; i < targets.Length; i++)
        {
            var value = unchecked((uint)targets[i].ToInt64());
            BitConverter.GetBytes(value).CopyTo(targetData, i * 4);
        }

        XChangeProperty(_display, requestor, property, _atomAtom, 32, PropModeReplace, targetData, targets.Length);
        return true;
    }

    private bool HandleTimestampRequest(IntPtr requestor, IntPtr property)
    {
        var timestamp = new byte[4];
        BitConverter.GetBytes(_clipboardTimestamp).CopyTo(timestamp, 0);

        XChangeProperty(_display, requestor, property, _integerAtom, 32, PropModeReplace, timestamp, 1);
        return true;
    }

    private bool HandlePngRequest(IntPtr requestor, IntPtr property)
    {
        byte[]? png;
        lock (_sync)
        {
            png = _pngData;
        }

        if (png is null) return false;

        XChangeProperty(_display, requestor, property, _imagePngAtom, 8, PropModeReplace, png, png.Length);
        return true;
    }

    private bool HandleMultipleRequest(IntPtr requestor, IntPtr property)
    {
        if (property == IntPtr.Zero) return false;

        var multipleData = GetPropertyData(requestor, property, out var actualType, out var actualFormat);
        if (multipleData is null || actualType != _atomAtom || actualFormat != 32 || multipleData.Length % 8 != 0)
        {
            return false;
        }

        var pairCount = multipleData.Length / 8;
        var pairs = new uint[pairCount];
        Buffer.BlockCopy(multipleData, 0, pairs, 0, multipleData.Length);

        var resultPairs = new byte[multipleData.Length];

        for (var i = 0; i < pairCount; i++)
        {
            var target = new IntPtr(unchecked((long)pairs[i]));
            var outputProperty = new IntPtr(unchecked((long)pairs[i + 1]));

            if (!HandleMultipleTarget(requestor, target, outputProperty))
            {
                pairs[i + 1] = 0;
            }

            i++;
        }

        Buffer.BlockCopy(pairs, 0, resultPairs, 0, resultPairs.Length);
        XChangeProperty(_display, requestor, property, _atomAtom, 32, PropModeReplace, resultPairs, pairCount);
        return true;
    }

    private bool HandleMultipleTarget(IntPtr requestor, IntPtr target, IntPtr property)
    {
        if (target == _targetsAtom) return HandleTargetsRequest(requestor, property);
        if (target == _timestampAtom) return HandleTimestampRequest(requestor, property);
        if (target == _saveTargetsAtom) return true;
        if (target == _imagePngAtom) return HandlePngRequest(requestor, property);
        return false;
    }

    private byte[]? GetPropertyData(IntPtr window, IntPtr property, out IntPtr actualType, out int actualFormat)
    {
        actualType = IntPtr.Zero;
        actualFormat = 0;

        var result = XGetWindowProperty(
            _display, window, property, 0, long.MaxValue / 4, false,
            AnyPropertyType, out actualType, out actualFormat,
            out var itemCount, out var bytesAfter, out var data);

        if (result != Success) return null;

        try
        {
            if (data == IntPtr.Zero || itemCount == IntPtr.Zero) return Array.Empty<byte>();

            var bytesPerItem = actualFormat switch
            {
                8 => 1,
                16 => 2,
                32 => 4,
                _ => 0
            };

            if (bytesPerItem == 0) return null;

            var totalBytes = checked(itemCount.ToInt64() * bytesPerItem);
            if (totalBytes > int.MaxValue) return null;

            var managedData = new byte[(int)totalBytes];
            Marshal.Copy(data, managedData, 0, managedData.Length);
            return managedData;
        }
        finally
        {
            if (data != IntPtr.Zero) XFree(data);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(X11ClipboardService));
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _updateEvent.Set();

        if (_clipboardThread.IsAlive && !ReferenceEquals(Thread.CurrentThread, _clipboardThread))
        {
            _clipboardThread.Join(1000);
        }

        _updateEvent.Dispose();
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string atomName, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, IntPtr border, IntPtr background);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);
    [DllImport("libX11.so.6")] private static extern int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, long time);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, out XEvent eventReturn);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, byte[] data, int nelements);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long longOffset, long longLength, bool delete, IntPtr requestedType, out IntPtr actualTypeReturn, out int actualFormatReturn, out IntPtr nitemsReturn, out IntPtr bytesAfterReturn, out IntPtr propReturn);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long eventMask, ref XEvent sendEvent);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);

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
        [FieldOffset(0)] public int Type;
        [FieldOffset(0)] public XSelectionRequestEvent SelectionRequest;
        [FieldOffset(0)] public XSelectionEvent SelectionNotify;
        [FieldOffset(0)] public XPropertyEvent Property;
    }
}