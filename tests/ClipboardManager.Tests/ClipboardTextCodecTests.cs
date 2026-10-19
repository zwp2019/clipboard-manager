using System.Text;
using ClipboardManager.Infrastructure;
using Xunit;

namespace ClipboardManager.Tests;

public sealed class ClipboardTextCodecTests
{
    [Theory]
    [InlineData("<pre>1. 调整 KPI、五座大山删除后重建逻辑\n2. 修复反馈主表及 formdata 😀</pre>")]
    [InlineData("<p>English &amp; 中文</p>")]
    public void HtmlUsesUtf8ByteOffsetsAndExcludesHeader(string fragment)
    {
        var bytes = ClipboardTextCodec.EncodeHtml(fragment, "plain");
        Assert.Equal(fragment, ClipboardTextCodec.DecodeHtml(bytes));
        Assert.Equal(0, bytes[^1]);
    }

    [Fact]
    public void LegacyUtf16MisdecodedHtmlIsRepairedWithoutChangingText()
    {
        const string fragment = "<pre>1. 调整 KPI、有效数据重跑保留反馈\n2. AI 质检回写保护</pre>";
        var original = ClipboardTextCodec.EncodeHtml(fragment, "plain");
        var padded = new byte[original.Length + original.Length % 2];
        original.CopyTo(padded, 0);
        var broken = Encoding.Unicode.GetString(padded);
        var repaired = ClipboardTextCodec.EncodeHtml(broken, "plain");
        Assert.Equal(fragment, ClipboardTextCodec.DecodeHtml(repaired));
    }

    [Fact]
    public void UnrecoverableHtmlFallsBackToEscapedPlainText()
    {
        var bytes = ClipboardTextCodec.EncodeHtml("损坏数据", "中文 <KPI> & AI");
        var fragment = ClipboardTextCodec.DecodeHtml(bytes);
        Assert.StartsWith("<pre>", fragment);
        Assert.Equal("中文 <KPI> & AI", System.Net.WebUtility.HtmlDecode(fragment[5..^6]));
    }

    [Fact]
    public void RtfPreservesCodePageBytesAndAddsTerminator()
    {
        var original = Encoding.Latin1.GetBytes("{\\rtf1\\ansi\\ansicpg936 " + "\xD6\xD0\xCE\xC4" + "}");
        var rtf = ClipboardTextCodec.DecodeRtf(original);
        var result = ClipboardTextCodec.EncodeRtf(rtf)!;
        Assert.Equal(original, result[..^1]);
        Assert.Equal(0, result[^1]);
    }

    [Fact]
    public void UnicodeRtfUsesUnicodeControlsInsteadOfUtf8Bytes()
    {
        var bytes = ClipboardTextCodec.EncodeRtf("{\\rtf1 中文}")!;
        var wire = Encoding.Latin1.GetString(bytes);
        Assert.Contains("\\uc1\\u20013?", wire);
        Assert.Contains("\\uc1\\u25991?", wire);
        Assert.Null(ClipboardTextCodec.EncodeRtf("invalid"));
    }
}
