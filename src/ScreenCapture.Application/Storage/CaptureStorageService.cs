using ScreenCapture.Core.Imaging;
using ScreenCapture.Core.Models;

namespace ScreenCapture.Application.Storage;

public sealed class CaptureStorageService
{
    public async Task<string> SaveAsync(
        CaptureResult capture,
        CancellationToken cancellationToken = default)
    {
        var pngData =
            PngEncoder.EncodeRgba(
                capture.PixelData,
                capture.Width,
                capture.Height);

        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures),
                "ScreenCapture");

        Directory.CreateDirectory(directory);

        var filename =
            $"Screenshot_{capture.CapturedAt:yyyy-MM-dd_HH-mm-ss}.png";

        var path =
            Path.Combine(
                directory,
                filename);

        await File.WriteAllBytesAsync(
            path,
            pngData,
            cancellationToken);

        Console.WriteLine(
            $"Screenshot saved: {path}");

        Console.WriteLine(
            $"Size: {capture.Width}x{capture.Height}");

        return path;
    }
}