using System.Drawing;
using System.Drawing.Imaging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// DIB（Device Independent Bitmap）解码：将 CF_DIB 内存数据转换为 PNG 字节（规范 §21）。
/// 支持 24/32 bpp 的 BI_RGB 与 BI_BITFIELDS。
/// </summary>
internal static class DibDecoder
{
    public static byte[] ToPng(byte[] dib)
    {
        var offset = 0;

        // 部分来源可能附带 BITMAPFILEHEADER（"BM"），跳过 14 字节文件头。
        var hasFileHeader = dib.Length >= 2 && dib[0] == (byte)'B' && dib[1] == (byte)'M';
        if (hasFileHeader)
        {
            offset = 14;
        }

        var headerSize = BitConverter.ToInt32(dib, offset);          // biSize
        var width = BitConverter.ToInt32(dib, offset + 4);           // biWidth
        var height = BitConverter.ToInt32(dib, offset + 8);          // biHeight
        var bitsPerPixel = BitConverter.ToInt16(dib, offset + 14);   // biBitCount
        var compression = BitConverter.ToInt32(dib, offset + 16);    // biCompression (0=BI_RGB, 3=BI_BITFIELDS)

        if (width <= 0 || height == 0 || (bitsPerPixel != 24 && bitsPerPixel != 32))
        {
            throw new InvalidDataException(
                $"Unsupported DIB format: {width}x{height}, {bitsPerPixel}bpp, compression={compression}");
        }

        // 像素数据起始：头部 + 调色板（+ BITFIELDS 掩码 12 字节）。
        var headerPlusPalette = offset + headerSize;
        if (compression == 3 && bitsPerPixel == 32)
        {
            headerPlusPalette += 12; // 三色掩码
        }

        var pixelOffset = hasFileHeader
            ? BitConverter.ToInt32(dib, 10) // bfOffBits
            : headerPlusPalette;

        var topDown = height < 0;
        var absHeight = Math.Abs(height);
        var dibStride = ((width * bitsPerPixel + 31) / 32) * 4;
        var dibPixelCount = dibStride * absHeight;

        if (pixelOffset + dibPixelCount > dib.Length)
        {
            throw new InvalidDataException("DIB pixel data is truncated.");
        }

        using var bitmap = new Bitmap(width, absHeight, PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, width, absHeight),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                var target = (byte*)bitmapData.Scan0;
                var targetStride = bitmapData.Stride;
                var bytesPerPixel = bitsPerPixel / 8;

                for (var y = 0; y < absHeight; y++)
                {
                    var sourceRow = topDown ? y : absHeight - 1 - y;
                    var source = pixelOffset + sourceRow * dibStride;
                    var dest = y * targetStride;

                    for (var x = 0; x < width; x++)
                    {
                        var s = source + x * bytesPerPixel;
                        var d = dest + x * 4;
                        target[d + 0] = dib[s + 0];     // B
                        target[d + 1] = dib[s + 1];     // G
                        target[d + 2] = dib[s + 2];     // R
                        target[d + 3] = bytesPerPixel == 4 ? dib[s + 3] : (byte)255; // A
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
