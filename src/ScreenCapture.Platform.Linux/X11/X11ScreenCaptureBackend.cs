using System.Runtime.InteropServices;
using ScreenCapture.Core.Models;

namespace ScreenCapture.Platform.Linux.X11;

public sealed class X11ScreenCaptureBackend : ILinuxScreenCaptureBackend
{
    private const int ZPixmap = 2;

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(
        IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayWidth(
        IntPtr display,
        int screen);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayHeight(
        IntPtr display,
        int screen);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong planeMask,
        int format);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(
        IntPtr image);

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
    }

    public Task<CaptureResult> CaptureScreenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var display = XOpenDisplay(IntPtr.Zero);

        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Unable to connect to the X11 display.");
        }

        try
        {
            var screen =
                XDefaultScreen(display);

            var width =
                XDisplayWidth(display, screen);

            var height =
                XDisplayHeight(display, screen);

            return Task.FromResult(
                Capture(
                    display,
                    XDefaultRootWindow(display),
                    0,
                    0,
                    width,
                    height));
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public Task<CaptureResult> CaptureRegionAsync(
        CaptureRegion region,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (region.Width <= 0 ||
            region.Height <= 0)
        {
            throw new ArgumentException(
                "Capture region must have a positive size.");
        }

        var display =
            XOpenDisplay(IntPtr.Zero);

        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Unable to connect to the X11 display.");
        }

        try
        {
            return Task.FromResult(
                Capture(
                    display,
                    XDefaultRootWindow(display),
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height));
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static CaptureResult Capture(
        IntPtr display,
        IntPtr rootWindow,
        int x,
        int y,
        int width,
        int height)
    {
        var imageHandle =
            XGetImage(
                display,
                rootWindow,
                x,
                y,
                (uint)width,
                (uint)height,
                ulong.MaxValue,
                ZPixmap);

        if (imageHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "X11 failed to capture the requested screen region.");
        }

        try
        {
            var image =
                Marshal.PtrToStructure<XImage>(
                    imageHandle);

            if (image.data == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "X11 returned an image without pixel data.");
            }

            var pixels =
                new byte[width * height * 4];

            // Fast path for the normal 32-bit X11 framebuffer.
            //
            // Instead of calling XGetPixel() once per pixel,
            // copy the entire XImage into managed memory first.
            if (image.bits_per_pixel == 32)
            {
                Copy32BitImage(
                    image,
                    width,
                    height,
                    pixels);
            }
            else
            {
                // Fallback for unusual X11 pixel formats.
                CopyUsingXGetPixel(
                    imageHandle,
                    image,
                    width,
                    height,
                    pixels);
            }

            return new CaptureResult
            {
                PixelData = pixels,
                Width = width,
                Height = height,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            XDestroyImage(imageHandle);
        }
    }

    private static void Copy32BitImage(
        XImage image,
        int width,
        int height,
        byte[] destination)
    {
        var sourceSize =
            image.bytes_per_line * height;

        var source =
            new byte[sourceSize];

        Marshal.Copy(
            image.data,
            source,
            0,
            source.Length);

        var redShift =
            GetMaskShift(image.red_mask);

        var greenShift =
            GetMaskShift(image.green_mask);

        var blueShift =
            GetMaskShift(image.blue_mask);

        var redMax =
            GetMaskValue(image.red_mask, redShift);

        var greenMax =
            GetMaskValue(image.green_mask, greenShift);

        var blueMax =
            GetMaskValue(image.blue_mask, blueShift);

        var littleEndian =
            image.byte_order == 0;

        for (var y = 0; y < height; y++)
        {
            var sourceRow =
                y * image.bytes_per_line;

            var destinationRow =
                y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var sourceOffset =
                    sourceRow + (x * 4);

                uint pixel;

                if (littleEndian)
                {
                    pixel =
                        (uint)(
                            source[sourceOffset] |
                            (source[sourceOffset + 1] << 8) |
                            (source[sourceOffset + 2] << 16) |
                            (source[sourceOffset + 3] << 24));
                }
                else
                {
                    pixel =
                        (uint)(
                            (source[sourceOffset] << 24) |
                            (source[sourceOffset + 1] << 16) |
                            (source[sourceOffset + 2] << 8) |
                            source[sourceOffset + 3]);
                }

                var red =
                    ScaleChannel(
                        (pixel & image.red_mask) >>
                        redShift,
                        redMax);

                var green =
                    ScaleChannel(
                        (pixel & image.green_mask) >>
                        greenShift,
                        greenMax);

                var blue =
                    ScaleChannel(
                        (pixel & image.blue_mask) >>
                        blueShift,
                        blueMax);

                var destinationOffset =
                    destinationRow + (x * 4);

                destination[destinationOffset] =
                    red;

                destination[destinationOffset + 1] =
                    green;

                destination[destinationOffset + 2] =
                    blue;

                destination[destinationOffset + 3] =
                    255;
            }
        }
    }

    private static void CopyUsingXGetPixel(
        IntPtr imageHandle,
        XImage image,
        int width,
        int height,
        byte[] destination)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel =
                    unchecked(
                        (ulong)XGetPixel(
                            imageHandle,
                            x,
                            y).ToInt64());

                var red =
                    ExtractChannel(
                        pixel,
                        image.red_mask);

                var green =
                    ExtractChannel(
                        pixel,
                        image.green_mask);

                var blue =
                    ExtractChannel(
                        pixel,
                        image.blue_mask);

                var offset =
                    ((y * width) + x) * 4;

                destination[offset] =
                    red;

                destination[offset + 1] =
                    green;

                destination[offset + 2] =
                    blue;

                destination[offset + 3] =
                    255;
            }
        }
    }

    private static int GetMaskShift(
        ulong mask)
    {
        if (mask == 0)
            return 0;

        var shift = 0;

        while ((mask & 1) == 0)
        {
            mask >>= 1;
            shift++;
        }

        return shift;
    }

    private static ulong GetMaskValue(
        ulong mask,
        int shift)
    {
        return mask >> shift;
    }

    private static byte ScaleChannel(
        ulong value,
        ulong maxValue)
    {
        if (maxValue == 0)
            return 0;

        return (byte)(
            (value * 255 + maxValue / 2) /
            maxValue);
    }

    private static byte ExtractChannel(
        ulong pixel,
        ulong mask)
    {
        if (mask == 0)
            return 0;

        var shift =
            GetMaskShift(mask);

        var value =
            (pixel & mask) >> shift;

        var maxValue =
            GetMaskValue(mask, shift);

        return ScaleChannel(
            value,
            maxValue);
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetPixel(
        IntPtr image,
        int x,
        int y);
}