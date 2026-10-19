namespace ClipboardManager.Infrastructure;

internal readonly record struct OcrTile(int X, int Y, int Width, int Height, int KeepX, int KeepY, int KeepWidth, int KeepHeight, int Scale)
{
    internal bool Owns(double x, double y) => x >= KeepX && x < KeepX + KeepWidth && y >= KeepY && y < KeepY + KeepHeight;
}
internal readonly record struct OcrTextWord(string Text, double X, double Y, double Width, double Height);

internal static class OcrImageLayout
{
    internal static IEnumerable<OcrTile> Tiles(int width, int height, int maxDimension, bool enhance)
    {
        if (width <= 0 || height <= 0 || maxDimension < 32) throw new ArgumentOutOfRangeException(nameof(width));
        var scale = enhance ? 2 : 1;
        var limit = maxDimension / scale;
        var overlap = Math.Min(64, limit / 4);
        var step = limit - 2 * overlap;
        for (var y = 0; y < height; y += step)
        for (var x = 0; x < width; x += step)
        {
            var left = Math.Max(0, x - overlap); var top = Math.Max(0, y - overlap);
            var right = Math.Min(width, x + step + overlap); var bottom = Math.Min(height, y + step + overlap);
            yield return new OcrTile(left, top, right - left, bottom - top, x, y, Math.Min(step, width - x), Math.Min(step, height - y), scale);
        }
    }

    internal static string Merge(IEnumerable<OcrTextWord> words)
    {
        var lines = new List<List<OcrTextWord>>();
        foreach (var word in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.Y + w.Height / 2).ThenBy(w => w.X))
        {
            var center = word.Y + word.Height / 2;
            var line = lines.LastOrDefault(group => Math.Abs(group.Average(w => w.Y + w.Height / 2) - center) <= Math.Min(group.Average(w => w.Height), word.Height) * 0.55);
            if (line is null) { line = new List<OcrTextWord>(); lines.Add(line); }
            line.Add(word);
        }
        return string.Join(Environment.NewLine, lines.OrderBy(group => group.Min(w => w.Y)).Select(group => Join(group.OrderBy(w => w.X))));
    }
    private static string Join(IEnumerable<OcrTextWord> words)
    {
        var result = new System.Text.StringBuilder();
        OcrTextWord? previous = null;
        foreach (var word in words)
        {
            var text = word.Text.Trim();
            var adjacentFragment = previous is { } before && word.X - (before.X + before.Width) <= Math.Min(before.Height, word.Height) * 0.18;
            if (result.Length > 0 && !adjacentFragment && !(IsCjk(result[^1]) && IsCjk(text[0]))) result.Append(' ');
            result.Append(text);
            previous = word;
        }
        return result.ToString();
    }
    private static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9fff';
}
