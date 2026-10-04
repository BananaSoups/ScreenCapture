using ScreenCapture.Core.Models;

namespace ScreenCapture.Platform.Linux;

public interface ILinuxScreenCaptureBackend
{
    Task<CaptureResult> CaptureScreenAsync(
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureRegionAsync(
        CaptureRegion region,
        CancellationToken cancellationToken = default);
}
