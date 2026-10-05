using System.Runtime.InteropServices;
using ScreenCapture.Platform;

namespace ScreenCapture.Platform.Linux.X11;

public sealed class X11GlobalHotkeyService : IGlobalHotkeyService
{
    private const int KeyPress = 2;

    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;
    private const uint ControlMask = 1 << 2;
    private const uint Mod2Mask = 1 << 4;

    private const uint AltMask = 1 << 3;
    private const uint SuperMask = 1 << 6;

    private const int GrabModeAsync = 1;

    private readonly object _sync = new();
    private readonly List<HotkeyRegistration> _hotkeys = new();
    private readonly ManualResetEventSlim _displayReady = new(false);
    private readonly AutoResetEvent _registrationChanged = new(false);

    private CancellationTokenSource? _cancellation;
    private Task? _eventLoopTask;
    private IntPtr _display;
    private IntPtr _rootWindow;
    private bool _initialized;
    private bool _registrationApplied;
    
    // Keep a reference to prevent GC collection of the delegate passed to unmanaged code
    private static readonly XErrorHandler _errorHandler = HandleXError;

    public Task InitializeAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        // Register custom X11 error handler to prevent process crashes on BadAccess (hotkey conflict)
        XSetErrorHandler(_errorHandler);

        _cancellation = new CancellationTokenSource();
        _eventLoopTask = Task.Run(() => EventLoop(_cancellation.Token));

        _displayReady.Wait();

        if (_display == IntPtr.Zero)
        {
            _cancellation.Cancel();
            throw new InvalidOperationException("Unable to open the X11 display for global hotkeys.");
        }

        _initialized = true;
        return Task.CompletedTask;
    }

    public Task RegisterAsync(string accelerator, Func<Task> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accelerator);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_initialized)
        {
            throw new InvalidOperationException("The global hotkey service has not been initialized.");
        }

        var hotkey = ParseAccelerator(accelerator);

        lock (_sync)
        {
            if (_hotkeys.Any(existing => existing.Keycode == hotkey.Keycode && existing.Modifiers == hotkey.Modifiers))
            {
                throw new InvalidOperationException($"The global hotkey '{accelerator}' is already registered.");
            }

            _hotkeys.Add(new HotkeyRegistration(accelerator, hotkey.Keycode, hotkey.Modifiers, callback));
        }

        _registrationChanged.Set();
        return Task.CompletedTask;
    }

    public Task UnregisterAllAsync()
    {
        lock (_sync)
        {
            _hotkeys.Clear();
        }

        _registrationChanged.Set();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!_initialized)
            return;

        _initialized = false;
        _cancellation?.Cancel();
        _registrationChanged.Set();

        if (_eventLoopTask is not null)
        {
            try
            {
                await _eventLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _eventLoopTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
        _displayReady.Reset();
    }

    private void EventLoop(CancellationToken cancellationToken)
    {
        var display = XOpenDisplay(IntPtr.Zero);

        if (display == IntPtr.Zero)
        {
            _displayReady.Set();
            Console.Error.WriteLine("Unable to open the X11 display for global hotkeys.");
            return;
        }

        _display = display;
        _rootWindow = XDefaultRootWindow(display);

        if (_rootWindow == IntPtr.Zero)
        {
            _display = IntPtr.Zero;
            XCloseDisplay(display);
            _displayReady.Set();
            Console.Error.WriteLine("Unable to obtain the X11 root window.");
            return;
        }

        _displayReady.Set();
        Console.WriteLine("X11 global hotkey event loop started.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_registrationApplied)
                {
                    ApplyRegistrations();
                    _registrationApplied = true;
                }

                if (XPending(display) <= 0)
                {
                    _registrationChanged.WaitOne(10);
                    if (_registrationChanged.WaitOne(0))
                    {
                        _registrationApplied = false;
                    }
                    continue;
                }

                var eventBuffer = Marshal.AllocHGlobal(192);

                try
                {
                    XNextEvent(display, eventBuffer);
                    var eventType = Marshal.ReadInt32(eventBuffer);

                    if (eventType != KeyPress)
                        continue;

                    var state = unchecked((uint)Marshal.ReadInt32(eventBuffer, 80));
                    var keycode = unchecked((uint)Marshal.ReadInt32(eventBuffer, 84));

                    HotkeyRegistration? registration = null;

                    lock (_sync)
                    {
                        foreach (var hotkey in _hotkeys)
                        {
                            if (hotkey.Keycode != keycode)
                                continue;

                            if (!ModifiersMatch(state, hotkey.Modifiers))
                                continue;

                            registration = hotkey;
                            break;
                        }
                    }

                    if (registration is not null)
                    {
                        Console.WriteLine($"Global hotkey triggered: {registration.Accelerator}");
                        _ = ExecuteCallbackAsync(registration.Callback);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(eventBuffer);
                }
            }
        }
        finally
        {
            RemoveAllX11Grabs();
            XCloseDisplay(display);
            _display = IntPtr.Zero;
            _rootWindow = IntPtr.Zero;
            Console.WriteLine("X11 global hotkey event loop stopped.");
        }
    }

    private void ApplyRegistrations()
    {
        if (_display == IntPtr.Zero || _rootWindow == IntPtr.Zero)
            return;

        RemoveAllX11Grabs();

        HotkeyRegistration[] registrations;
        lock (_sync)
        {
            registrations = _hotkeys.ToArray();
        }

        foreach (var registration in registrations)
        {
            foreach (var modifierMask in GetGrabModifierMasks(registration.Modifiers))
            {
                XGrabKey(_display, registration.Keycode, modifierMask, _rootWindow, false, GrabModeAsync, GrabModeAsync);
            }

            Console.WriteLine($"Registered global hotkey '{registration.Accelerator}' keycode={registration.Keycode}");
        }

        XSync(_display, false);
    }

    private void RemoveAllX11Grabs()
    {
        if (_display == IntPtr.Zero || _rootWindow == IntPtr.Zero)
            return;

        HotkeyRegistration[] registrations;
        lock (_sync)
        {
            registrations = _hotkeys.ToArray();
        }

        foreach (var registration in registrations)
        {
            foreach (var modifierMask in GetGrabModifierMasks(registration.Modifiers))
            {
                XUngrabKey(_display, registration.Keycode, modifierMask, _rootWindow);
            }
        }

        XSync(_display, false);
    }

    private static IEnumerable<uint> GetGrabModifierMasks(uint modifiers)
    {
        yield return modifiers;
        yield return modifiers | LockMask;
        yield return modifiers | Mod2Mask;
        yield return modifiers | LockMask | Mod2Mask;
    }

    private static bool ModifiersMatch(uint state, uint requiredModifiers)
    {
        const uint IgnoredModifiers = LockMask | Mod2Mask;
        var actual = state & ~IgnoredModifiers;
        var required = requiredModifiers & ~IgnoredModifiers;
        return actual == required;
    }

    private ParsedHotkey ParseAccelerator(string accelerator)
    {
        var parts = accelerator.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        string? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ControlMask;
                    break;
                case "shift":
                    modifiers |= ShiftMask;
                    break;
                case "alt":
                    modifiers |= AltMask;
                    break;
                case "super":
                case "meta":
                case "win":
                case "windows":
                    modifiers |= SuperMask;
                    break;
                default:
                    if (key is not null)
                    {
                        throw new FormatException($"Multiple key names found in accelerator '{accelerator}'.");
                    }
                    key = part;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new FormatException($"No key was found in accelerator '{accelerator}'.");
        }

        var keycode = KeyNameToKeycode(key);
        if (keycode == 0)
        {
            throw new FormatException($"Unable to resolve key '{key}' on X11.");
        }

        return new ParsedHotkey(keycode, modifiers);
    }

    private uint KeyNameToKeycode(string key)
    {
        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException("X11 display is not available.");
        }

        // Use XStringToKeysym instead of a manual switch statement
        var keysym = XStringToKeysym(key);
        if (keysym == 0)
        {
            // Fallback for single characters if needed, or handle case sensitivity
            keysym = XStringToKeysym(key.ToLowerInvariant());
        }

        if (keysym == 0)
            return 0;

        return XKeysymToKeycode(_display, keysym);
    }

    private static async Task ExecuteCallbackAsync(Func<Task> callback)
    {
        try
        {
            await callback();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Global hotkey callback failed: {ex}");
        }
    }

    private static int HandleXError(IntPtr display, IntPtr errorEvent)
    {
        // Suppress BadAccess errors (error code 10) which happen when a hotkey is already grabbed by another app
        // Returning 0 tells Xlib to ignore the error instead of crashing the process.
        return 0;
    }

    private sealed record HotkeyRegistration(string Accelerator, uint Keycode, uint Modifiers, Func<Task> Callback);
    private readonly record struct ParsedHotkey(uint Keycode, uint Modifiers);

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XGrabKey(IntPtr display, uint keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XUngrabKey(IntPtr display, uint keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong XStringToKeysym(string keyName);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
}