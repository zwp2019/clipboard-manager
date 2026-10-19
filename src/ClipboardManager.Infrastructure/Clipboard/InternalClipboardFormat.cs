using ClipboardManager.Infrastructure.Interop;

namespace ClipboardManager.Infrastructure;

internal static class InternalClipboardFormat
{
    internal static readonly uint Id = NativeMethods.RegisterClipboardFormatW("ClipboardManager.InternalWrite.v1");
}
