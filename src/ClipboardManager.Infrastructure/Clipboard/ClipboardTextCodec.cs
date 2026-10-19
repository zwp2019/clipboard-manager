using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardManager.Infrastructure;

/// <summary>Clipboard HTML uses UTF-8 byte offsets; RTF keeps its original 8-bit bytes.</summary>
internal static class ClipboardTextCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string DecodeHtml(byte[] bytes)
    {
        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        var start = Offset(header, "StartFragment");
        var end = Offset(header, "EndFragment");
        if (start >= 0 && end >= start && end <= bytes.Length)
            return StrictUtf8.GetString(bytes, start, end - start).TrimEnd('\0');
        var html = StrictUtf8.GetString(bytes).TrimEnd('\0');
        var marker = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        var finish = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0 && finish >= marker + 20) return html[(marker + 20)..finish];
        var tag = html.IndexOf('<');
        return tag >= 0 ? html[tag..] : html;
    }

    internal static byte[] EncodeHtml(string html, string? plainText)
    {
        // Older releases decoded UTF-8 as UTF-16. Repair only if the reverse
        // conversion produces valid UTF-8 HTML; otherwise use the intact plain text.
        if (!html.TrimStart().StartsWith('<'))
        {
            try { html = DecodeHtml(Encoding.Unicode.GetBytes(html)); }
            catch (DecoderFallbackException) { html = string.Empty; }
        }
        if (!html.TrimStart().StartsWith('<'))
            html = "<pre>" + System.Net.WebUtility.HtmlEncode(plainText ?? string.Empty) + "</pre>";
        const string template = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        var start = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, template, 0, 0, 0, 0));
        var fragment = start + Encoding.UTF8.GetByteCount(prefix);
        var fragmentEnd = fragment + Encoding.UTF8.GetByteCount(html);
        var end = fragmentEnd + Encoding.UTF8.GetByteCount(suffix);
        var header = string.Format(CultureInfo.InvariantCulture, template, start, end, fragment, fragmentEnd);
        return Encoding.UTF8.GetBytes(header + prefix + html + suffix + "\0");
    }

    internal static string DecodeRtf(byte[] bytes) => Encoding.Latin1.GetString(bytes).TrimEnd('\0');

    internal static byte[]? EncodeRtf(string? rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return null;
        if (!rtf.StartsWith("{\\rtf", StringComparison.Ordinal)) rtf = DecodeRtf(Encoding.Unicode.GetBytes(rtf));
        if (!rtf.StartsWith("{\\rtf", StringComparison.Ordinal)) return null;
        var escaped = new StringBuilder();
        foreach (var character in rtf.TrimEnd('\0'))
        {
            if (character <= 255) escaped.Append(character);
            else escaped.Append("\\uc1\\u").Append(unchecked((short)character).ToString(CultureInfo.InvariantCulture)).Append('?');
        }
        return Encoding.Latin1.GetBytes(escaped.Append('\0').ToString());
    }

    private static int Offset(string header, string field)
    {
        var match = Regex.Match(header, @"(?:^|\n)" + field + @":\s*(\d+)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }
}
