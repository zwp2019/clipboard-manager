using ClipboardManager.Application;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Xunit;

namespace ClipboardManager.Tests;

public class ClipboardHashServiceTests
{
    private readonly ClipboardHashService _hash = new();

    [Fact]
    public void SameText_SameHash()
    {
        var a = new ClipboardData { Type = ClipboardType.Text, Text = "hello world", Size = 11 };
        var b = new ClipboardData { Type = ClipboardType.Text, Text = "hello world", Size = 11 };

        Assert.Equal(_hash.ComputeHash(a), _hash.ComputeHash(b));
    }

    [Fact]
    public void DifferentText_DifferentHash()
    {
        var a = new ClipboardData { Type = ClipboardType.Text, Text = "hello" };
        var b = new ClipboardData { Type = ClipboardType.Text, Text = "world" };

        Assert.NotEqual(_hash.ComputeHash(a), _hash.ComputeHash(b));
    }

    [Fact]
    public void TextVsHtml_DifferentHash()
    {
        var text = new ClipboardData { Type = ClipboardType.Text, Text = "abc" };
        var html = new ClipboardData { Type = ClipboardType.Html, Html = "<p>abc</p>", Text = "abc" };

        Assert.NotEqual(_hash.ComputeHash(text), _hash.ComputeHash(html));
    }

    [Fact]
    public void SameImageBytes_SameHash()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var a = new ClipboardData { Type = ClipboardType.Image, ImageBytes = bytes };
        var b = new ClipboardData { Type = ClipboardType.Image, ImageBytes = bytes };

        Assert.Equal(_hash.ComputeHash(a), _hash.ComputeHash(b));
    }

    [Fact]
    public void FilePaths_OrderIndependentHash()
    {
        var a = new ClipboardData { Type = ClipboardType.File, FilePaths = new[] { "b.txt", "a.txt" } };
        var b = new ClipboardData { Type = ClipboardType.File, FilePaths = new[] { "a.txt", "b.txt" } };

        Assert.Equal(_hash.ComputeHash(a), _hash.ComputeHash(b));
    }

    [Fact]
    public void HashLength_IsSha256Hex()
    {
        var data = new ClipboardData { Type = ClipboardType.Text, Text = "x" };

        Assert.Equal(64, _hash.ComputeHash(data).Length);
    }
}
