using System.IO.Compression;
using System.Buffers.Binary;

namespace ScreenCapture.Core.Imaging;

public static class PngEncoder
{
    private static readonly byte[] PngSignature =
    [
        137, 80, 78, 71, 13, 10, 26, 10
    ];

    public static byte[] EncodeRgba(
        byte[] rgba,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException();

        if (rgba.Length != width * height * 4)
            throw new ArgumentException(
                "RGBA buffer size does not match the supplied dimensions.",
                nameof(rgba));

        using var output = new MemoryStream();

        output.Write(PngSignature);

        // IHDR
        Span<byte> header = stackalloc byte[13];

        BinaryPrimitives.WriteInt32BigEndian(
            header[0..4],
            width);

        BinaryPrimitives.WriteInt32BigEndian(
            header[4..8],
            height);

        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        header[10] = 0; // compression
        header[11] = 0; // filter
        header[12] = 0; // interlace

        WriteChunk(output, "IHDR", header);

        // Each scanline starts with a filter byte.
        var scanlineSize = width * 4 + 1;
        var raw = new byte[scanlineSize * height];

        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * width * 4;
            var targetOffset = y * scanlineSize;

            raw[targetOffset] = 0; // No filter

            Buffer.BlockCopy(
                rgba,
                sourceOffset,
                raw,
                targetOffset + 1,
                width * 4);
        }

        // IDAT
        using var compressed = new MemoryStream();

        using (var deflate = new ZLibStream(
                   compressed,
                   CompressionLevel.Fastest,
                   leaveOpen: true))
        {
            deflate.Write(raw);
        }

        WriteChunk(
            output,
            "IDAT",
            compressed.ToArray());

        // IEND
        WriteChunk(
            output,
            "IEND",
            []);

        return output.ToArray();
    }

    private static void WriteChunk(
        Stream stream,
        string type,
        ReadOnlySpan<byte> data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

        Span<byte> length = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(
            length,
            data.Length);

        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = new Crc32();

        crc.Append(typeBytes);
        crc.Append(data);

        Span<byte> crcBytes = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(
            crcBytes,
            crc.GetCurrentHashAsUInt32());

        stream.Write(crcBytes);
    }

    private sealed class Crc32
    {
        private uint _value = 0xFFFFFFFF;

        public void Append(ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
            {
                _value ^= value;

                for (var bit = 0; bit < 8; bit++)
                {
                    _value = (_value & 1) != 0
                        ? (_value >> 1) ^ 0xEDB88320
                        : _value >> 1;
                }
            }
        }

        public uint GetCurrentHashAsUInt32()
        {
            return ~_value;
        }
    }
}
