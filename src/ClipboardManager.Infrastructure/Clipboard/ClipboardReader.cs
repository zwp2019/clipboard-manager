using System.Runtime.InteropServices;
using System.Text;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 剪贴板读取器：读取 Text / HTML / RTF / URL / File / Image（规范 §11、§65）。
/// 含重试机制，应对剪贴板被其他应用锁定的场景（规范 §44）。
/// </summary>
public sealed partial class ClipboardReader : IClipboardReader
{
    private const int MaxAttempts = 4;

    private static readonly int HtmlFormat = (int)NativeMethods.RegisterClipboardFormatW("HTML Format");
    private static readonly int RtfFormat = (int)NativeMethods.RegisterClipboardFormatW("Rich Text Format");
    private static readonly int PngFormat = (int)NativeMethods.RegisterClipboardFormatW("PNG");
    private static readonly int ImagePngFormat = (int)NativeMethods.RegisterClipboardFormatW("image/png");
    private static readonly int JpegFormat = (int)NativeMethods.RegisterClipboardFormatW("JFIF");
    private static readonly uint[] ConfidentialFormats =
    {
        NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"),
        NativeMethods.RegisterClipboardFormatW("Clipboard Viewer Ignore"),
        NativeMethods.RegisterClipboardFormatW("application/x-keepassxc-secret"),
    };

    private readonly ILogger<ClipboardReader> _logger;
    private readonly IInternalClipboardGuard _guard;

    public ClipboardReader(ILogger<ClipboardReader> logger, IInternalClipboardGuard guard)
    {
        _logger = logger;
        _guard = guard;
    }

    public Task<ClipboardData?> ReadAsync(CancellationToken cancellationToken)
        => Task.Run(() => ReadCore(cancellationToken), cancellationToken);

    private ClipboardData? ReadCore(CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (!NativeMethods.OpenClipboard(nint.Zero))
            {
                // 其他程序正在使用剪贴板（规范 §44）：短暂延迟后重试。
                lastError = new InvalidOperationException(
                    $"OpenClipboard failed (attempt {attempt}), error={Marshal.GetLastPInvokeError()}");
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                continue;
            }

            try
            {
                return ReadFormats();
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        Log.ReadFailed(_logger, lastError, MaxAttempts);
        return null;
    }

    private ClipboardData? ReadFormats()
    {
        if (ConfidentialFormats.Any(NativeMethods.IsClipboardFormatAvailable)) return null;
        var marker = ReadUtf16Text(InternalClipboardFormat.Id);
        // Read the marker and payload under the same OpenClipboard lock. The writer
        // may already have returned by the time WM_CLIPBOARDUPDATE is delivered.
        if (_guard.IsOwnClipboardMarker(marker)) return new ClipboardData { InternalWriteMarker = marker };
        // TextBox copy/context-menu actions use WinUI's writer rather than ours.
        // Identify their actual owner instead of guessing from the foreground app.
        var owner = NativeMethods.GetClipboardOwner();
        if (owner != nint.Zero)
        {
            if (NativeMethods.GetWindowThreadProcessId(owner, out var processId) != 0 && processId == Environment.ProcessId)
                return new ClipboardData { InternalWriteMarker = _guard.CreateClipboardMarker("native-control-copy") };
        }
        var text = ReadUtf16Text(NativeMethods.CF_UNICODETEXT);
        var html = ReadFormatText(HtmlFormat);
        var rtf = ReadFormatText(RtfFormat);
        var filePaths = ReadFileDrop();
        var imageBytes = ReadImageBytes();
        var (imageExtension, imageSize) = imageBytes is not null
            ? (DetectImageExtension(imageBytes), imageBytes.LongLength)
            : (null, 0L);

        // 类型判定优先级：File > Image > Html > Rtf > Url > Text
        ClipboardType type;
        if (filePaths is { Count: > 0 })
        {
            type = ClipboardType.File;
        }
        else if (imageBytes is not null)
        {
            type = ClipboardType.Image;
        }
        else if (!string.IsNullOrEmpty(html))
        {
            type = ClipboardType.Html;
        }
        else if (!string.IsNullOrEmpty(rtf))
        {
            type = ClipboardType.Rtf;
        }
        else if (!string.IsNullOrEmpty(text) && IsUrl(text!))
        {
            type = ClipboardType.Url;
        }
        else if (!string.IsNullOrEmpty(text))
        {
            type = ClipboardType.Text;
        }
        else
        {
            return null;
        }

        return new ClipboardData
        {
            InternalWriteMarker = marker,
            Type = type,
            Text = string.IsNullOrEmpty(text) ? null : text,
            Html = string.IsNullOrEmpty(html) ? null : html,
            Rtf = string.IsNullOrEmpty(rtf) ? null : rtf,
            FilePaths = filePaths,
            ImageBytes = imageBytes,
            ImageExtension = imageExtension,
            Size = ComputeSize(type, text, html, rtf, filePaths, imageSize),
        };
    }

    private static string? ReadUtf16Text(uint format)
    {
        if (!NativeMethods.IsClipboardFormatAvailable(format))
        {
            return null;
        }

        var hGlobal = NativeMethods.GetClipboardData(format);
        if (hGlobal == nint.Zero)
        {
            return null;
        }

        var ptr = NativeMethods.GlobalLock(hGlobal);
        try
        {
            return ptr == nint.Zero ? null : Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }
    }

    private static string? ReadFormatText(int format)
    {
        if (format == 0 || !NativeMethods.IsClipboardFormatAvailable((uint)format))
        {
            return null;
        }

        var hGlobal = NativeMethods.GetClipboardData((uint)format);
        if (hGlobal == nint.Zero)
        {
            return null;
        }

        var ptr = NativeMethods.GlobalLock(hGlobal);
        try
        {
            if (ptr == nint.Zero)
            {
                return null;
            }

            var size = checked((int)NativeMethods.GlobalSize(hGlobal));
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            try { return format == HtmlFormat ? ClipboardTextCodec.DecodeHtml(bytes) : ClipboardTextCodec.DecodeRtf(bytes); }
            catch (DecoderFallbackException) { return null; }
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }
    }

    private static List<string>? ReadFileDrop()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
        {
            return null;
        }

        var hDrop = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
        if (hDrop == nint.Zero)
        {
            return null;
        }

        var ptr = NativeMethods.GlobalLock(hDrop);
        try
        {
            if (ptr == nint.Zero)
            {
                return null;
            }

            var size = (int)NativeMethods.GlobalSize(hDrop);
            var raw = new byte[size];
            Marshal.Copy(ptr, raw, 0, size);

            // DROPFILES 头部 20 字节：pFiles(4) x(4) y(4) fNC(4) fWide(4)
            var offset = BitConverter.ToInt32(raw, 0);
            var isWide = BitConverter.ToInt32(raw, 16) != 0;
            var paths = new List<string>();

            if (isWide)
            {
                var i = offset;
                while (i + 1 < raw.Length)
                {
                    var codeUnit = BitConverter.ToUInt16(raw, i);
                    if (codeUnit == 0)
                    {
                        break;
                    }

                    var sb = new StringBuilder();
                    while (codeUnit != 0)
                    {
                        sb.Append((char)codeUnit);
                        i += 2;
                        if (i + 1 >= raw.Length)
                        {
                            break;
                        }

                        codeUnit = BitConverter.ToUInt16(raw, i);
                    }

                    var path = sb.ToString();
                    if (path.Length > 0)
                    {
                        paths.Add(path);
                    }

                    i += 2; // 跳过结尾 NULL
                }
            }
            else
            {
                // ANSI 路径
                var i = offset;
                while (i < raw.Length)
                {
                    if (raw[i] == 0)
                    {
                        break;
                    }

                    var end = Array.IndexOf(raw, (byte)0, i);
                    if (end < 0)
                    {
                        end = raw.Length;
                    }

                    var path = Encoding.UTF8.GetString(raw, i, end - i);
                    if (path.Length > 0)
                    {
                        paths.Add(path);
                    }

                    i = end + 1;
                }
            }

            return paths;
        }
        finally
        {
            NativeMethods.GlobalUnlock(hDrop);
        }
    }

    private byte[]? ReadImageBytes()
    {
        // 优先读取 PNG / JPEG 原生格式（多数应用复制图片时提供）。
        foreach (var format in new[] { PngFormat, ImagePngFormat, JpegFormat })
        {
            if (format == 0)
            {
                continue;
            }

            if (!NativeMethods.IsClipboardFormatAvailable((uint)format))
            {
                continue;
            }

            var bytes = ReadRawData((uint)format);
            if (bytes is { Length: > 0 })
            {
                return bytes;
            }
        }

        // 兜底：CF_DIB（BITMAPINFOHEADER + 像素）→ 解码为 PNG。
        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB))
        {
            var dib = ReadRawData(NativeMethods.CF_DIB);
            if (dib is { Length: > 0 })
            {
                try
                {
                    return DibDecoder.ToPng(dib);
                }
                catch (Exception ex)
                {
                    Log.DibDecodeFailed(_logger, ex);
                }
            }
        }

        return null;
    }

    private static byte[]? ReadRawData(uint format)
    {
        var hGlobal = NativeMethods.GetClipboardData(format);
        if (hGlobal == nint.Zero)
        {
            return null;
        }

        var ptr = NativeMethods.GlobalLock(hGlobal);
        try
        {
            if (ptr == nint.Zero)
            {
                return null;
            }

            var size = (int)NativeMethods.GlobalSize(hGlobal);
            if (size <= 0)
            {
                return null;
            }

            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }
    }

    private static bool IsUrl(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "jpg";
        }

        return "png";
    }

    private static long ComputeSize(
        ClipboardType type,
        string? text,
        string? html,
        string? rtf,
        IReadOnlyList<string>? filePaths,
        long imageSize)
    {
        if (type == ClipboardType.Image)
        {
            return imageSize;
        }

        var total = 0L;
        if (!string.IsNullOrEmpty(text))
        {
            total += Encoding.UTF8.GetByteCount(text);
        }

        if (!string.IsNullOrEmpty(html))
        {
            total += Encoding.UTF8.GetByteCount(html);
        }

        if (!string.IsNullOrEmpty(rtf))
        {
            total += Encoding.UTF8.GetByteCount(rtf);
        }

        if (filePaths is not null)
        {
            total += filePaths.Sum(p => Encoding.UTF8.GetByteCount(p));
        }

        return total;
    }

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Clipboard read failed after {MaxAttempts} attempts")]
        public static partial void ReadFailed(ILogger logger, Exception? exception, int maxAttempts);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to decode DIB image")]
        public static partial void DibDecodeFailed(ILogger logger, Exception exception);
    }
}
