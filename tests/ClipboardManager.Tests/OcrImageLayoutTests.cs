using ClipboardManager.Infrastructure;
using Xunit;

namespace ClipboardManager.Tests;

public sealed class OcrImageLayoutTests
{
    [Theory]
    [InlineData(800, 20000, false)]
    [InlineData(12000, 1000, false)]
    [InlineData(700, 10000, true)]
    [InlineData(1, 1, true)]
    public void TilesPreserveEverySourceRegionAndStayWithinEngineLimit(int width, int height, bool enhance)
    {
        var tiles = OcrImageLayout.Tiles(width, height, 2560, enhance).ToArray();
        Assert.Equal((long)width * height, tiles.Sum(t => (long)t.KeepWidth * t.KeepHeight));
        Assert.All(tiles, t =>
        {
            Assert.InRange(t.Width * t.Scale, 1, 2560);
            Assert.InRange(t.Height * t.Scale, 1, 2560);
            Assert.True(t.X >= 0 && t.Y >= 0 && t.X + t.Width <= width && t.Y + t.Height <= height);
        });
        Assert.Single(tiles, t => t.Owns(width - 0.5, height - 0.5));
        foreach (var tile in tiles)
            Assert.Single(tiles, t => t.Owns(tile.KeepX + 0.5, tile.KeepY + 0.5));
    }

    [Fact]
    public void OverlapRetainsEachWordOnceAndKeepsTheLastLine()
    {
        var tiles = OcrImageLayout.Tiles(800, 10000, 2560, true).ToArray();
        foreach (var tile in tiles.Skip(1))
            Assert.Single(tiles, t => t.Owns(100, tile.KeepY));
        Assert.Single(tiles, t => t.Owns(100, 9999));
    }

    [Fact]
    public void MergeRestoresLineAndWordOrderWithoutInventingCorrections()
    {
        var text = OcrImageLayout.Merge(new[]
        {
            new OcrTextWord("COPY", 160, 10, 50, 20),
            new OcrTextWord("最后一行", 10, 9000, 70, 18),
            new OcrTextWord("Everything", 10, 9, 90, 20),
            new OcrTextWord("you", 110, 10, 30, 20),
        });
        Assert.Equal("Everything you COPY" + Environment.NewLine + "最后一行", text);
    }

    [Fact]
    public void MergeUsesGeometryToJoinFragmentsWithinAnEnglishWord()
    {
        Assert.Equal("you copy", OcrImageLayout.Merge(new[]
        {
            new OcrTextWord("yo", 0, 0, 20, 20), new OcrTextWord("u", 22, 0, 10, 20),
            new OcrTextWord("co", 44, 0, 20, 20), new OcrTextWord("py", 66, 0, 20, 20),
        }));
    }

    [Fact]
    public void MergeJoinsChineseWordsWithoutExtraSpaces()
    {
        Assert.Equal("内容预览", OcrImageLayout.Merge(new[]
        {
            new OcrTextWord("内容", 0, 0, 30, 18), new OcrTextWord("预览", 32, 1, 30, 18),
        }));
    }
}
