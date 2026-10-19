using ClipboardManager.Data;
using Xunit;

namespace ClipboardManager.Tests;

public class ChineseTokenizerTests
{
    [Fact]
    public void Tokenize_AsciiWord_PreservedLowercase()
    {
        var tokens = ChineseTokenizer.Tokenize("RabbitMQ Consumer");

        Assert.Contains("rabbitmq", tokens);
        Assert.Contains("consumer", tokens);
        Assert.DoesNotContain("RabbitMQ", tokens);
    }

    [Fact]
    public void Tokenize_Cjk_ProducesUnigramAndBigram()
    {
        var tokens = ChineseTokenizer.Tokenize("哈希");

        Assert.Contains("哈", tokens);
        Assert.Contains("希", tokens);
        Assert.Contains("哈希", tokens);
    }

    [Fact]
    public void Tokenize_MixedChineseEnglish_Works()
    {
        var tokens = ChineseTokenizer.Tokenize("Redis 缓存与 MySQL 事务");

        Assert.Contains("redis", tokens);
        Assert.Contains("mysql", tokens);
        Assert.Contains("缓存", tokens);
        Assert.Contains("事务", tokens);
    }

    [Fact]
    public void Tokenize_Empty_ReturnsEmpty()
    {
        Assert.Empty(ChineseTokenizer.Tokenize(null));
        Assert.Empty(ChineseTokenizer.Tokenize("   "));
    }

    [Fact]
    public void ToSearchableContent_SpaceSeparated()
    {
        var content = ChineseTokenizer.ToSearchableContent("你好 world");

        Assert.Equal("你 好 你好 world", content);
    }

    [Fact]
    public void BuildMatchQuery_SingleToken_Prefix()
    {
        var query = ChineseTokenizer.BuildMatchQuery("rabbit");

        Assert.Equal("\"rabbit\"*", query);
    }

    [Fact]
    public void BuildMatchQuery_Cjk_AndCombined()
    {
        var query = ChineseTokenizer.BuildMatchQuery("哈希");

        Assert.Contains("\"哈希\"*", query);
        Assert.Contains(" AND ", query);
    }

    [Fact]
    public void EscapeLike_HandlesWildcards()
    {
        var escaped = ChineseTokenizer.EscapeLike("100%_done");

        Assert.Equal(@"100\%\_done", escaped);
    }
}
