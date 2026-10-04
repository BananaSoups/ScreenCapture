using ScreenCapture.Application.Storage;
using ScreenCapture.Application.UI;
using ScreenCapture.Core.Imaging;
using ScreenCapture.Core.Models;
using ScreenCapture.Platform;

namespace ScreenCapture.Application.Capture;

public sealed class RegionCaptureWorkflow
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IRegionSelectionService _regionSelectionService;
    private readonly IClipboardService _clipboardService;
    private readonly CaptureStorageService _storageService;

    public RegionCaptureWorkflow(
        IScreenCaptureService screenCaptureService,
        IRegionSelectionService regionSelectionService,
        IClipboardService clipboardService,
        CaptureStorageService storageService)
    {
        _screenCaptureService =
            screenCaptureService;

        _regionSelectionService =
            regionSelectionService;

        _clipboardService =
            clipboardService;

        _storageService =
            storageService;
    }

    public async Task<CaptureResult?> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var frozenDesktop =
            await _screenCaptureService.CaptureScreenAsync(
                cancellationToken);

        var region =
            await _regionSelectionService.SelectRegionAsync(
                frozenDesktop,
                cancellationToken);

        if (region is null)
            return null;

        Console.WriteLine(
            $"Selected region: " +
            $"{region.X},{region.Y} " +
            $"{region.Width}x" +
            $"{region.Height}");

        var result =
            ImageCropper.Crop(
                frozenDesktop,
                region);

        await _storageService.SaveAsync(
            result,
            cancellationToken);

        await _clipboardService.SetImageAsync(
            result,
            cancellationToken);

        Console.WriteLine(
            $"Region capture saved: " +
            $"{result.Width}x{result.Height}");

        return result;
    }
}