using SkiaSharp;

namespace Games.Horse.Generators;

public static class GifEncoder
{
    public static byte[] RenderFramesToGif(byte[][] pngBuffers, int width, int height)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write("GIF89a"u8);

        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0xF7);
        writer.Write((byte)0);
        writer.Write((byte)0);

        var palette = BuildPalette();
        writer.Write(palette);

        writer.Write((byte)0x21);
        writer.Write((byte)0xFF);
        writer.Write((byte)11);
        writer.Write("NETSCAPE2.0"u8);
        writer.Write((byte)3);
        writer.Write((byte)1);
        writer.Write((ushort)0);
        writer.Write((byte)0);

        foreach (var pngBuffer in pngBuffers)
        {
            using var bitmap = SKBitmap.Decode(pngBuffer);
            if (bitmap == null) continue;

            var resized = bitmap.Width != width || bitmap.Height != height
                ? bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default)
                : bitmap;

            writer.Write((byte)0x21);
            writer.Write((byte)0xF9);
            writer.Write((byte)4);
            writer.Write((byte)0x00);
            writer.Write((ushort)6);
            writer.Write((byte)0);
            writer.Write((byte)0);

            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0);

            writer.Write((byte)8);

            var pixels = QuantizeFrame(resized, width, height);
            var lzwData = LzwEncode(pixels, 8);

            var offset = 0;
            while (offset < lzwData.Length)
            {
                var blockSize = Math.Min(255, lzwData.Length - offset);
                writer.Write((byte)blockSize);
                writer.Write(lzwData.AsSpan(offset, blockSize));
                offset += blockSize;
            }
            writer.Write((byte)0);

            if (!ReferenceEquals(resized, bitmap))
                resized.Dispose();
        }

        writer.Write((byte)0x3B);
        writer.Flush();

        return ms.ToArray();
    }

    private static byte[] BuildPalette()
    {
        var palette = new byte[256 * 3];
        var idx = 0;
        for (var r = 0; r < 6; r++)
        for (var g = 0; g < 6; g++)
        for (var b = 0; b < 6; b++)
        {
            palette[idx++] = (byte)(r * 51);
            palette[idx++] = (byte)(g * 51);
            palette[idx++] = (byte)(b * 51);
        }
        for (var i = 216; i < 256; i++)
        {
            var v = (byte)((i - 216) * 6 + 3);
            palette[idx++] = v;
            palette[idx++] = v;
            palette[idx++] = v;
        }
        return palette;
    }

    private static byte[] QuantizeFrame(SKBitmap bitmap, int width, int height)
    {
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var ri = Math.Min(5, color.Red / 43);
            var gi = Math.Min(5, color.Green / 43);
            var bi = Math.Min(5, color.Blue / 43);
            pixels[y * width + x] = (byte)(ri * 36 + gi * 6 + bi);
        }
        return pixels;
    }

    private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
    {
        var clearCode = 1 << minCodeSize;
        var eoiCode = clearCode + 1;
        var codeSize = minCodeSize + 1;
        var nextCode = eoiCode + 1;

        var table = new Dictionary<string, int>();
        for (var i = 0; i < clearCode; i++)
            table[((char)i).ToString()] = i;

        using var output = new MemoryStream();
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        void WriteBits(int code, int size)
        {
            bitBuffer |= code << bitsInBuffer;
            bitsInBuffer += size;
            while (bitsInBuffer >= 8)
            {
                output.WriteByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitsInBuffer -= 8;
            }
        }

        WriteBits(clearCode, codeSize);

        if (pixels.Length == 0)
        {
            WriteBits(eoiCode, codeSize);
            if (bitsInBuffer > 0) output.WriteByte((byte)(bitBuffer & 0xFF));
            return output.ToArray();
        }

        var current = ((char)pixels[0]).ToString();

        for (var i = 1; i < pixels.Length; i++)
        {
            var next = current + (char)pixels[i];
            if (table.ContainsKey(next))
            {
                current = next;
            }
            else
            {
                WriteBits(table[current], codeSize);
                if (nextCode < 4096)
                {
                    table[next] = nextCode++;
                    if (nextCode > (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }
                else
                {
                    WriteBits(clearCode, codeSize);
                    table.Clear();
                    for (var j = 0; j < clearCode; j++)
                        table[((char)j).ToString()] = j;
                    nextCode = eoiCode + 1;
                    codeSize = minCodeSize + 1;
                }
                current = ((char)pixels[i]).ToString();
            }
        }

        WriteBits(table[current], codeSize);
        WriteBits(eoiCode, codeSize);
        if (bitsInBuffer > 0) output.WriteByte((byte)(bitBuffer & 0xFF));

        return output.ToArray();
    }
}
