using System.Security.Cryptography;
using System.Text;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;

namespace ClipboardManager.Application;

/// <summary>
/// 哈希服务：SHA-256 内容指纹（规范 §14）。
/// 文本/HTML/RTF 取文本内容；图片取原始字节；文件取排序后的路径与元数据。
/// </summary>
public sealed class ClipboardHashService : IClipboardHashService
{
    public string ComputeHash(ClipboardData data)
    {
        return data.Type switch
        {
            ClipboardType.Image => HashBytes(data.ImageBytes),
            ClipboardType.File => HashString(
                string.Join('\n', data.FilePaths?.OrderBy(x => x, StringComparer.Ordinal) ?? Array.Empty<string>())),
            _ => HashString(data.Text ?? data.Html ?? data.Rtf ?? string.Empty),
        };
    }

    private static string HashString(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty))).ToLowerInvariant();

    private static string HashBytes(byte[]? bytes)
        => bytes is null or { Length: 0 }
            ? HashString(string.Empty)
            : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
