namespace ScreenCapture.Core.Models;

public sealed class CaptureResult
{
    public required byte[] PixelData { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
}
