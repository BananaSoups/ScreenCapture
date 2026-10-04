using ScreenCapture.Core.Models;

namespace ScreenCapture.Application.UI;

public interface IRegionSelectionService
{
    Task<CaptureRegion?> SelectRegionAsync(
        CaptureResult frozenDesktop,
        CancellationToken cancellationToken = default);
}
