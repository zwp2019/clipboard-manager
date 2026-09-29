using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 剪贴板写入器：快速粘贴时写入剪贴板，并标记内部操作防死循环（规范 §13、§30）。
/// 写入含重试，应对剪贴板锁定（规范 §44）。
/// </summary>
public sealed partial class ClipboardWriter : IClipboardWriter
{
    private const int MaxAttempts = 3;

    private static readonly int HtmlFormat = (int)NativeMethods.RegisterClipboardFormatW("HTML Format");
    private static readonly int PngFormat = (int)NativeMethods.RegisterClipboardFormatW("PNG");

    private readonly IInternalClipboardGuard _guard;
    private readonly ILogger<ClipboardWriter> _logger;

    public ClipboardWriter(IInternalClipboardGuard guard, ILogger<ClipboardWriter> logger)
    {
        _guard = guard;
        _logger = logger;
    }

    public async Task WriteAsync(ClipboardItem item, string operationId, CancellationToken cancellationToken)
    {
        await Task.Run(
            () =>
            {
                using var scope = _guard.BeginOwnWrite(operationId);
                var hGlobalCopies = new List<nint>();

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        FreeHandles(hGlobalCopies);
                        return;
                    }

                    if (!NativeMethods.OpenClipboard(nint.Zero))
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                        continue;
                    }

                    try
                    {
                        NativeMethods.EmptyClipboard();
                        WriteFormats(item, hGlobalCopies);
                        Log.ClipboardWritten(_logger, item.Type, operationId);
                        return;
                    }
                    catch (Exception ex)
                    {
                        // 空剪贴板后再失败：清空并释放内存句柄。
                        FreeHandles(hGlobalCopies);
                        Log.WriteAttemptFailed(_logger, ex, attempt);
                        if (attempt == MaxAttempts)
                        {
                            throw;
                        }

                        Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                    }
                    finally
                    {
                        NativeMethods.CloseClipboard();
                    }
                }
            },
            cancellationToken);
    }

    private void WriteFormats(ClipboardItem item, List<nint> handles)
    {
        switch (item.Type)
        {
            case ClipboardType.Text:
            case ClipboardType.Url:
            case ClipboardType.Code:
                SetUnicodeText(item.Text, handles);
                break;

            case ClipboardType.Html:
                SetUnicodeText(item.Text, handles);
                SetHtml(item.Html ?? item.Text ?? string.Empty, handles);
                break;

            case ClipboardType.Rtf:
                SetUnicodeText(item.Text, handles);
                SetFormat(RtfFormat, Encoding.UTF8.GetBytes(item.Rtf ?? string.Empty), handles);
                break;

            case ClipboardType.Image:
                SetImage(item.ImagePath, handles);
                break;

            case ClipboardType.File:
                SetFileDrop(item.FilePaths, handles);
                break;

            default:
                SetUnicodeText(item.Text, handles);
                break;
        }
    }

    private void SetUnicodeText(string? text, List<nint> handles)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SetFormat(NativeMethods.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"), handles);
    }

    private void SetHtml(string html, List<nint> handles)
    {
        var header = "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n";
        var bytes = Encoding.UTF8.GetBytes(header + html);
        SetFormat((uint)HtmlFormat, bytes, handles);
    }

    private void SetImage(string? imagePath, List<nint> handles)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        using var bitmap = new Bitmap(imagePath);

        // 1. PNG 格式（供现代应用直接读取）
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            SetFormat((uint)PngFormat, stream.ToArray(), handles);
        }

        // 2. HBITMAP 格式（供传统应用读取，剪贴板接管所有权后不可删除）
        var hBitmap = bitmap.GetHbitmap();
        if (NativeMethods.SetClipboardData(NativeMethods.CF_BITMAP, hBitmap) == nint.Zero)
        {
            NativeMethods.DeleteObject(hBitmap);
            Log.SetBitmapFailed(_logger, Marshal.GetLastPInvokeError());
        }
    }

    private static void SetFileDrop(string? filePathsJson, List<nint> handles)
    {
        if (string.IsNullOrEmpty(filePathsJson))
        {
            return;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(filePathsJson) ?? new List<string>();
        }
        catch
        {
            paths = Array.Empty<string>();
        }

        if (paths.Count == 0)
        {
            return;
        }

        // DROPFILES 结构 + UTF-16 路径列表 + 双 NULL 结尾
        var header = new byte[20];
        BitConverter.GetBytes(20).CopyTo(header, 0); // pFiles 偏移
        BitConverter.GetBytes(0).CopyTo(header, 4);  // x
        BitConverter.GetBytes(0).CopyTo(header, 8);  // y
        BitConverter.GetBytes(1).CopyTo(header, 12); // fNC
        BitConverter.GetBytes(1).CopyTo(header, 16); // fWide (Unicode)

        using var stream = new MemoryStream();
        stream.Write(header, 0, header.Length);
        foreach (var path in paths)
        {
            var chars = Encoding.Unicode.GetBytes(path);
            stream.Write(chars, 0, chars.Length);
            stream.Write(new byte[2], 0, 2); // 路径结束 NULL
        }

        stream.Write(new byte[2], 0, 2); // 列表结束 NULL

        var bytes = stream.ToArray();
        var hDrop = CopyToGlobal(bytes);
        handles.Add(hDrop);
        NativeMethods.SetClipboardData(NativeMethods.CF_HDROP, hDrop);
    }

    private void SetFormat(uint format, byte[] data, List<nint> handles)
    {
        if (data.Length == 0)
        {
            return;
        }

        var hMem = CopyToGlobal(data);
        handles.Add(hMem);
        if (NativeMethods.SetClipboardData(format, hMem) == nint.Zero)
        {
            Log.SetFormatFailed(_logger, format, Marshal.GetLastPInvokeError());
        }
    }

    private static nint CopyToGlobal(byte[] bytes)
    {
        var hMem = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)bytes.Length);
        var ptr = NativeMethods.GlobalLock(hMem);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return hMem;
        }
        finally
        {
            NativeMethods.GlobalUnlock(hMem);
        }
    }

    private static void FreeHandles(List<nint> handles)
    {
        foreach (var h in handles)
        {
            NativeMethods.GlobalFree(h);
        }

        handles.Clear();
    }

    private static uint RtfFormat;

    static ClipboardWriter()
    {
        RtfFormat = NativeMethods.RegisterClipboardFormatW("Rich Text Format");
    }

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Trace, Message = "Clipboard written: {Type} (operation {OperationId})")]
        public static partial void ClipboardWritten(ILogger logger, ClipboardType type, string operationId);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Clipboard write attempt {Attempt} failed")]
        public static partial void WriteAttemptFailed(ILogger logger, Exception exception, int attempt);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "SetClipboardData(CF_BITMAP) failed, error={Code}")]
        public static partial void SetBitmapFailed(ILogger logger, int code);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "SetClipboardData format {Format} failed, error={Code}")]
        public static partial void SetFormatFailed(ILogger logger, uint format, int code);
    }
}
