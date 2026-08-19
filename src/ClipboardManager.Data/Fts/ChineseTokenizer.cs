using System.Text.RegularExpressions;

namespace ClipboardManager.Data;

/// <summary>
/// 中文分词助手：解决 SQLite FTS5 对中文（无空格语言）无法分词的问题（规范 §18）。
/// 策略：
///  - 英文/数字/下划线按完整 token 保留（转小写）；
///  - 中文按「单字 + 相邻双字 bigram」切分并以空格连接，供 FTS5 unicode61 索引；
///  - 查询端做相同处理，保证插入与查询一致性。
/// </summary>
public static partial class ChineseTokenizer
{
    [GeneratedRegex(@"[A-Za-z0-9_]+", RegexOptions.Compiled)]
    private static partial Regex AsciiWordRegex();

    [GeneratedRegex(@"[\u2E80-\u9FFF\uF900-\uFAFF]+", RegexOptions.Compiled)]
    private static partial Regex CjkRunRegex();

    /// <summary>
    /// 将文本切分为检索 token（去重保序）。
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        foreach (Match m in AsciiWordRegex().Matches(text))
        {
            tokens.Add(m.Value.ToLowerInvariant());
        }

        foreach (Match m in CjkRunRegex().Matches(text))
        {
            var run = m.Value;
            if (run.Length == 1)
            {
                tokens.Add(run);
                continue;
            }

            for (var i = 0; i < run.Length; i++)
            {
                tokens.Add(run[i].ToString());              // 单字
            }

            for (var i = 0; i < run.Length - 1; i++)
            {
                tokens.Add(run.Substring(i, 2));            // 双字 bigram
            }
        }

        return tokens.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 生成写入 FTS 索引的规范化内容（空格分隔的 token）。
    /// </summary>
    public static string ToSearchableContent(string? text)
        => string.Join(' ', Tokenize(text));

    /// <summary>
    /// 构造 FTS5 MATCH 查询串：每个 token 做前缀查询，token 间用 OR（提高召回）。
    /// </summary>
    public static string BuildMatchQuery(string? query)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" OR ", tokens.Select(t => $"\"{EscapeFtsToken(t)}\"*"));
    }

    private static string EscapeFtsToken(string token)
    {
        // token 由字母/数字/下划线/汉字构成，此处仅防御性移除引号与通配符。
        return token.Replace("\"", string.Empty).Replace("*", string.Empty).Replace(":", string.Empty);
    }

    /// <summary>
    /// 转义 LIKE 通配符，用于短查询兜底。
    /// </summary>
    public static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
