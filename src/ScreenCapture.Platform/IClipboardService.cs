using ScreenCapture.Core.Models;

namespace ScreenCapture.Platform;

public interface IClipboardService
{
    Task SetImageAsync(
        CaptureResult capture,
        CancellationToken cancellationToken = default);
}