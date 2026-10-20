using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ClipboardManager.Domain;

namespace ClipboardManager.Infrastructure.Notifications;

/// <summary>Draws a compact floating copy confirmation, including its transparent shadow margin.</summary>
internal static class CopyToastRenderer
{
    internal static Bitmap Render(string preview, ClipboardType type, bool success, bool dark, float scale, bool highContrast = false)
    {
        const float cardWidth = 352;
        const float cardHeight = 76;
        const float margin = 16;
        var bitmap = new Bitmap((int)Math.Ceiling((cardWidth + margin * 2) * scale),
            (int)Math.Ceiling((cardHeight + margin * 2) * scale), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.ScaleTransform(scale, scale);

        var card = new RectangleF(margin, margin, cardWidth, cardHeight);
        if (!highContrast)
        {
            // Several low-opacity contours create a soft shadow without a dark rim.
            for (var spread = 14; spread >= 1; spread--)
            {
                var shadow = card;
                shadow.Offset(0, 4);
                shadow.Inflate(spread * 0.65f, spread * 0.65f);
                using var path = RoundedRect(shadow, 20 + spread * 0.4f);
                using var brush = new SolidBrush(Color.FromArgb(dark ? 3 : 2, 12, 17, 27));
                graphics.FillPath(brush, path);
            }
        }

        using var surface = RoundedRect(card, 20);
        var top = highContrast ? SystemColors.Window : dark ? Color.FromArgb(249, 51, 52, 57) : Color.FromArgb(251, 255, 255, 255);
        var bottom = highContrast ? SystemColors.Window : dark ? Color.FromArgb(245, 38, 39, 44) : Color.FromArgb(246, 247, 248, 251);
        using (var fill = new LinearGradientBrush(card, top, bottom, LinearGradientMode.Vertical))
            graphics.FillPath(fill, surface);
        using (var border = new Pen(highContrast ? SystemColors.WindowText : dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(27, 59, 67, 82), highContrast ? 1.5f : 0.75f))
            graphics.DrawPath(border, surface);

        var primary = highContrast ? SystemColors.WindowText : dark ? Color.FromArgb(246, 246, 248) : Color.FromArgb(36, 38, 45);
        var secondary = highContrast ? SystemColors.WindowText : dark ? Color.FromArgb(180, 183, 193) : Color.FromArgb(112, 116, 128);
        var tile = new RectangleF(card.Left + 16, card.Top + 18, 40, 40);
        var (accent, foreground, glyph) = Icon(type, success);
        if (highContrast) { accent = SystemColors.Highlight; foreground = SystemColors.HighlightText; }
        using (var path = RoundedRect(tile, 12))
        using (var brush = new LinearGradientBrush(tile, Lighten(accent, 0.12f), accent, LinearGradientMode.Vertical))
            graphics.FillPath(brush, path);
        using (var glyphFont = new Font("Segoe MDL2 Assets", 19, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var glyphBrush = new SolidBrush(foreground))
        using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            graphics.DrawString(glyph, glyphFont, glyphBrush, tile, centered);

        if (success)
        {
            var badge = new RectangleF(tile.Right - 12, tile.Bottom - 12, 16, 16);
            using (var brush = new SolidBrush(highContrast ? SystemColors.Window : dark ? Color.FromArgb(43, 44, 49) : Color.White))
                graphics.FillEllipse(brush, badge.X - 2, badge.Y - 2, 20, 20);
            using (var brush = new SolidBrush(highContrast ? SystemColors.Highlight : Color.FromArgb(48, 164, 108)))
                graphics.FillEllipse(brush, badge);
            using var check = new Pen(highContrast ? SystemColors.HighlightText : Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            graphics.DrawLines(check, [new PointF(badge.X + 4, badge.Y + 8), new PointF(badge.X + 7, badge.Y + 11), new PointF(badge.X + 12, badge.Y + 5.5f)]);
        }

        var textX = tile.Right + 14;
        var textRight = card.Right - 18;
        using var titleFont = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var previewFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        using var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        graphics.DrawString(success ? "已复制" : "复制失败", titleFont, primaryBrush,
            new RectangleF(textX, card.Top + 16, textRight - textX - (success ? 63 : 0), 20), format);
        graphics.DrawString(preview, previewFont, secondaryBrush,
            new RectangleF(textX, card.Top + 39, textRight - textX, 18), format);

        if (success)
        {
            var keycap = new RectangleF(textRight - 53, card.Top + 17, 53, 19);
            using (var path = RoundedRect(keycap, 5))
            {
                using var fill = new SolidBrush(highContrast ? SystemColors.Window : dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(12, 48, 56, 75));
                using var stroke = new Pen(highContrast ? SystemColors.WindowText : dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(17, 48, 56, 75), 0.7f);
                graphics.FillPath(fill, path);
                graphics.DrawPath(stroke, path);
            }
            using var keyFont = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
            using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("Ctrl + V", keyFont, secondaryBrush, keycap, centered);
        }
        return bitmap;
    }

    private static (Color Accent, Color Foreground, string Glyph) Icon(ClipboardType type, bool success)
    {
        if (!success) return (Color.FromArgb(237, 174, 63), Color.FromArgb(104, 66, 5), "\uE7BA");
        return type switch
        {
            ClipboardType.Image => (Color.FromArgb(231, 110, 130), Color.White, "\uEB9F"),
            ClipboardType.Url => (Color.FromArgb(90, 155, 226), Color.White, "\uE774"),
            ClipboardType.Code => (Color.FromArgb(143, 121, 206), Color.White, "\uE943"),
            ClipboardType.File => (Color.FromArgb(102, 166, 123), Color.White, "\uE8B7"),
            _ => (Color.FromArgb(239, 196, 83), Color.FromArgb(111, 79, 13), "\uE8A5"),
        };
    }

    private static Color Lighten(Color color, float amount) => Color.FromArgb(
        (int)(color.R + (255 - color.R) * amount), (int)(color.G + (255 - color.G) * amount), (int)(color.B + (255 - color.B) * amount));

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
