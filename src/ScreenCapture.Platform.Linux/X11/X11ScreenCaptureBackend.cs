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
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayWidth(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayHeight(IntPtr display, int screen);

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
    private static extern int XDestroyImage(IntPtr image);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetPixel(
        IntPtr image,
        int x,
        int y);

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
            throw new InvalidOperationException(
                "Unable to connect to the X11 display.");

        try
        {
            var screen = XDefaultScreen(display);

            var width = XDisplayWidth(display, screen);
            var height = XDisplayHeight(display, screen);

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

        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException(
                "Capture region must have a positive size.");

        var display = XOpenDisplay(IntPtr.Zero);

        if (display == IntPtr.Zero)
            throw new InvalidOperationException(
                "Unable to connect to the X11 display.");

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
        var imageHandle = XGetImage(
            display,
            rootWindow,
            x,
            y,
            (uint)width,
            (uint)height,
            ulong.MaxValue,
            ZPixmap);

        if (imageHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                "X11 failed to capture the requested screen region.");

        try
        {
            var image = Marshal.PtrToStructure<XImage>(imageHandle);

            // Copy the raw X11 image into a managed RGBA buffer.
            var pixels = new byte[width * height * 4];

            for (var pixelY = 0; pixelY < height; pixelY++)
            {
                for (var pixelX = 0; pixelX < width; pixelX++)
                {
                    var pixel = unchecked(
                        (ulong)XGetPixel(
                            imageHandle,
                            pixelX,
                            pixelY).ToInt64());

                    var red = ExtractChannel(
                        pixel,
                        image.red_mask);

                    var green = ExtractChannel(
                        pixel,
                        image.green_mask);

                    var blue = ExtractChannel(
                        pixel,
                        image.blue_mask);

                    var offset = ((pixelY * width) + pixelX) * 4;

                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                    pixels[offset + 3] = 255;
                }
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

    private static byte ExtractChannel(
        ulong pixel,
        ulong mask)
    {
        if (mask == 0)
            return 0;

        var shift = 0;
        var shiftedMask = mask;

        while ((shiftedMask & 1) == 0)
        {
            shiftedMask >>= 1;
            shift++;
        }

        var value = (pixel & mask) >> shift;

        var maxValue = shiftedMask;

        return (byte)((value * 255) / maxValue);
    }
}
