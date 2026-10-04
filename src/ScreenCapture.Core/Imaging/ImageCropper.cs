using ScreenCapture.Core.Models;

namespace ScreenCapture.Core.Imaging;

public static class ImageCropper
{
    public static CaptureResult Crop(
        CaptureResult source,
        CaptureRegion region)
    {
        if (region.Width <= 0 ||
            region.Height <= 0)
        {
            throw new ArgumentException(
                "Crop region must have a positive size.",
                nameof(region));
        }

        if (region.X < 0 ||
            region.Y < 0 ||
            region.X + region.Width > source.Width ||
            region.Y + region.Height > source.Height)
        {
            throw new ArgumentException(
                "Crop region is outside the source image.",
                nameof(region));
        }

        var croppedPixels =
            new byte[region.Width * region.Height * 4];

        for (var y = 0; y < region.Height; y++)
        {
            var sourceOffset =
                ((region.Y + y) * source.Width + region.X) * 4;

            var destinationOffset =
                y * region.Width * 4;

            Buffer.BlockCopy(
                source.PixelData,
                sourceOffset,
                croppedPixels,
                destinationOffset,
                region.Width * 4);
        }

        return new CaptureResult
        {
            PixelData = croppedPixels,
            Width = region.Width,
            Height = region.Height,
            CapturedAt = source.CapturedAt
        };
    }
}
