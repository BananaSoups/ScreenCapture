namespace ScreenCapture.Core.Models;

public sealed record CaptureRegion(
    int X,
    int Y,
    int Width,
    int Height);