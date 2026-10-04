using ScreenCapture.Core.Models;
using ScreenCapture.Platform;

namespace ScreenCapture.Platform.Linux;

public sealed class LinuxScreenCaptureService : IScreenCaptureService
{
    private readonly ILinuxScreenCaptureBackend _backend;

    public LinuxScreenCaptureService(ILinuxScreenCaptureBackend backend)
    {
        _backend = backend;
    }

    public Task<CaptureResult> CaptureScreenAsync(
        CancellationToken cancellationToken = default)
    {
        return _backend.CaptureScreenAsync(cancellationToken);
    }

    public Task<CaptureResult> CaptureRegionAsync(
        CaptureRegion region,
        CancellationToken cancellationToken = default)
    {
        return _backend.CaptureRegionAsync(region, cancellationToken);
    }
}
