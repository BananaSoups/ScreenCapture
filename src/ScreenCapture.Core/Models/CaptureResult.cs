namespace ScreenCapture.Core.Models;

public sealed class CaptureResult
{
    public required byte[] ImageData { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
}