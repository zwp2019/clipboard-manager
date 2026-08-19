using ClipboardManager.Application;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Xunit;

namespace ClipboardManager.Tests;

public class ClipboardNormalizerTests
{
    private readonly ClipboardNormalizer _normalizer = new();

    [Fact]
    public void Normalize_Text_KeepsTextAndType()
    {
        var data = new ClipboardData
        {
            Type = ClipboardType.Text,
            Text = "hello",
            Size = 5,
            SourceApplication = "notepad",
        };

        var item = _normalizer.Normalize(data, "hash123");

        Assert.Equal(ClipboardType.Text, item.Type);
        Assert.Equal("hello", item.Text);
        Assert.Equal(5, item.Size);
        Assert.Equal("notepad", item.SourceApplication);
        Assert.Equal("hash123", item.Hash);
    }

    [Fact]
    public void Normalize_File_SerializesPaths()
    {
        var data = new ClipboardData
        {
            Type = ClipboardType.File,
            FilePaths = new[] { @"C:\a.txt", @"D:\b.txt" },
        };

        var item = _normalizer.Normalize(data, "h");

        Assert.Contains("a.txt", item.FilePaths);
        Assert.Contains("b.txt", item.FilePaths);
    }

    [Fact]
    public void Normalize_Image_DoesNotExposeBytes()
    {
        var data = new ClipboardData
        {
            Type = ClipboardType.Image,
            ImageBytes = new byte[] { 1, 2, 3 },
        };

        var item = _normalizer.Normalize(data, "h");

        Assert.Equal(ClipboardType.Image, item.Type);
        Assert.Null(item.ImagePath); // 落盘由存储服务负责
    }

    [Fact]
    public void Normalize_Url_TypePreserved()
    {
        var data = new ClipboardData { Type = ClipboardType.Url, Text = "https://example.com" };

        var item = _normalizer.Normalize(data, "h");

        Assert.Equal(ClipboardType.Url, item.Type);
        Assert.Equal("https://example.com", item.Text);
    }
}
