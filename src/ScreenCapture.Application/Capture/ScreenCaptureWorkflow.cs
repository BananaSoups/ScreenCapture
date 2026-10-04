using ScreenCapture.Application.Storage;
using ScreenCapture.Core.Models;
using ScreenCapture.Platform;

namespace ScreenCapture.Application.Capture;

public sealed class ScreenCaptureWorkflow
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IClipboardService _clipboardService;
    private readonly CaptureStorageService _storageService;

    public ScreenCaptureWorkflow(
        IScreenCaptureService screenCaptureService,
        IClipboardService clipboardService,
        CaptureStorageService storageService)
    {
        _screenCaptureService =
            screenCaptureService;

        _clipboardService =
            clipboardService;

        _storageService =
            storageService;
    }

    public async Task<CaptureResult> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var capture =
            await _screenCaptureService.CaptureScreenAsync(
                cancellationToken);

        var path =
            await _storageService.SaveAsync(
                capture,
                cancellationToken);

        await _clipboardService.SetImageAsync(
            capture,
            cancellationToken);

        Console.WriteLine(
            $"Full screen capture saved: {path}");

        Console.WriteLine(
            $"Size: {capture.Width}x{capture.Height}");

        return capture;
    }
}