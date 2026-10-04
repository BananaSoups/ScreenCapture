using ScreenCapture.Core.Models;

namespace ScreenCapture.Platform;

public interface IScreenCaptureService
{
    Task<CaptureResult> CaptureScreenAsync(
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureRegionAsync(
        CaptureRegion region,
        CancellationToken cancellationToken = default);
}
