namespace ScreenCapture.Platform;

public interface IClipboardService
{
    Task SetImageAsync(
        byte[] imageData,
        CancellationToken cancellationToken = default);
}