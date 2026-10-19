using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ClipboardManager.Contracts;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ClipboardManager.Infrastructure;

/// <summary>Offline Windows OCR with language selection and overlapping full-resolution tiles.</summary>
public sealed class ImageTextService : IImageTextService
{
    public Task<string> ExtractAsync(string path, CancellationToken ct, string? languageTag = null, bool enhanceSmallText = false)
        => Task.Run(() => RecognizeAsync(path, languageTag, enhanceSmallText, ct), ct);

    private static async Task<string> RecognizeAsync(string path, string? languageTag, bool enhance, CancellationToken ct)
    {
        var engine = languageTag is not null ? OcrEngine.TryCreateFromLanguage(new Language(languageTag))
            : OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null) throw new InvalidOperationException("未安装所选 OCR 语言，请在 Windows 语言设置中添加对应的文字识别语言包。");
        var english = languageTag is null && !engine.RecognizerLanguage.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? OcrEngine.TryCreateFromLanguage(new Language("en-US")) : null;
        using var source = new Bitmap(path);
        // Respect orientation before computing tiles (screenshots normally have none).
        if (source.PropertyIdList.Contains(0x0112))
        {
            var orientation = source.GetPropertyItem(0x0112)?.Value;
            if (orientation is { Length: >= 2 }) source.RotateFlip(BitConverter.ToUInt16(orientation, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone,
            });
        }
        var words = new List<OcrTextWord>();
        foreach (var tile in OcrImageLayout.Tiles(source.Width, source.Height, (int)Math.Min(2560, OcrEngine.MaxImageDimension), enhance))
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = PrepareTile(source, tile);
            var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
            if (english is not null)
            {
                var letters = result.Text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
                var chinese = result.Text.Count(c => c is >= '\u3400' and <= '\u9fff');
                if (letters >= 10 && chinese == 0) result = await english.RecognizeAsync(bitmap).AsTask(ct);
            }
            foreach (var word in result.Lines.SelectMany(line => line.Words))
            {
                var box = word.BoundingRect;
                var x = tile.X + box.X / tile.Scale; var y = tile.Y + box.Y / tile.Scale;
                var width = box.Width / tile.Scale; var height = box.Height / tile.Scale;
                // Keep each overlapping word once, using its center in source coordinates.
                if (tile.Owns(x + width / 2, y + height / 2)) words.Add(new OcrTextWord(word.Text, x, y, width, height));
            }
        }
        ct.ThrowIfCancellationRequested();
        return OcrImageLayout.Merge(words);
    }

    private static SoftwareBitmap PrepareTile(Bitmap source, OcrTile tile)
    {
        var width = tile.Width * tile.Scale; var height = tile.Height * tile.Scale;
        using var prepared = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(prepared))
        {
            // Composite transparent pixels onto white instead of recognizing black alpha edges.
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height), tile.X, tile.Y, tile.Width, tile.Height, GraphicsUnit.Pixel);
        }
        var data = prepared.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[checked(width * height * 4)];
            for (var y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * width * 4, width * 4);
            return SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        }
        finally { prepared.UnlockBits(data); }
    }
}
