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
    private readonly CaptureStorageService _storageService;

    public RegionCaptureWorkflow(
        IScreenCaptureService screenCaptureService,
        IRegionSelectionService regionSelectionService,
        CaptureStorageService storageService)
    {
        _screenCaptureService = screenCaptureService;
        _regionSelectionService = regionSelectionService;
        _storageService = storageService;
    }

    public async Task<CaptureResult?> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. Capture the entire desktop immediately.
        //
        // This is important because the region selector will
        // appear over the screen after this point. We want the
        // user to select from the frozen screenshot rather than
        // trying to capture the live desktop underneath it.
        var frozenDesktop =
            await _screenCaptureService.CaptureScreenAsync(
                cancellationToken);

        // 2. Show the frozen screenshot and let the user
        // select the region.
        var region =
            await _regionSelectionService.SelectRegionAsync(
                frozenDesktop,
                cancellationToken);

        // User pressed Escape or otherwise cancelled.
        if (region is null)
            return null;

        Console.WriteLine(
            $"Selected region: " +
            $"{region.X},{region.Y} " +
            $"{region.Width}x{region.Height}");

        // 3. Crop the selected area from the frozen screenshot.
        var result =
            ImageCropper.Crop(
                frozenDesktop,
                region);

        // 4. Save the cropped screenshot.
        await _storageService.SaveAsync(
            result,
            cancellationToken);

        Console.WriteLine(
            $"Region capture saved: " +
            $"{result.Width}x{result.Height}");

        return result;
    }
}