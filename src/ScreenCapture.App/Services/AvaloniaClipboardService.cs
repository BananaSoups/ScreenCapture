using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScreenCapture.App.Views;
using ScreenCapture.Core.Models;
using ScreenCapture.Platform;
using Avalonia.Input.Platform;

namespace ScreenCapture.App.Services;

public sealed class AvaloniaClipboardService
    : IClipboardService,
      IDisposable
{
    private readonly MainWindow _mainWindow;

    private readonly object _sync = new();

    private WriteableBitmap? _clipboardBitmap;
    private bool _disposed;

    public AvaloniaClipboardService(
        MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task SetImageAsync(
        CaptureResult capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        cancellationToken.ThrowIfCancellationRequested();

        if (capture.Width <= 0 ||
            capture.Height <= 0)
        {
            throw new ArgumentException(
                "Capture dimensions must be positive.",
                nameof(capture));
        }

        var expectedLength =
            checked(capture.Width *
                    capture.Height *
                    4);

        if (capture.PixelData.Length !=
            expectedLength)
        {
            throw new ArgumentException(
                "Pixel data does not match the capture dimensions.",
                nameof(capture));
        }

        ThrowIfDisposed();

        var clipboard =
            TopLevel.GetTopLevel(_mainWindow)?.Clipboard;

        if (clipboard is null)
        {
            throw new InvalidOperationException(
                "Avalonia clipboard is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bitmap =
            CreateBitmap(capture);

        cancellationToken.ThrowIfCancellationRequested();

        WriteableBitmap? previousBitmap;

        lock (_sync)
        {
            ThrowIfDisposed();

            previousBitmap =
                _clipboardBitmap;

            _clipboardBitmap =
                bitmap;
        }

        try
        {
            await clipboard.SetBitmapAsync(
                bitmap);

            await clipboard.FlushAsync();
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(
                    _clipboardBitmap,
                    bitmap))
                {
                    _clipboardBitmap =
                        previousBitmap;
                }
            }

            bitmap.Dispose();

            throw;
        }

        previousBitmap?.Dispose();
    }

    private static WriteableBitmap CreateBitmap(
        CaptureResult capture)
    {
        var bitmap =
            new WriteableBitmap(
                new PixelSize(
                    capture.Width,
                    capture.Height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Opaque);

        using var framebuffer =
            bitmap.Lock();

        var sourceRowBytes =
            capture.Width * 4;

        for (var y = 0;
             y < capture.Height;
             y++)
        {
            Marshal.Copy(
                capture.PixelData,
                y * sourceRowBytes,
                framebuffer.Address +
                    y * framebuffer.RowBytes,
                sourceRowBytes);
        }

        return bitmap;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(AvaloniaClipboardService));
        }
    }

    public void Dispose()
    {
        WriteableBitmap? bitmap;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;

            bitmap =
                _clipboardBitmap;

            _clipboardBitmap = null;
        }

        bitmap?.Dispose();
    }
}
