using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BetterGlorious.Devices;

namespace BetterGlorious.UI
{
    /// <summary>
    /// Draws the tray icon: the device glyph (mouse / keyboard / headset) tinted with the battery
    /// color behind the percentage, and a level bar along the bottom.
    /// </summary>
    static class IconRenderer
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static Icon Draw(int percent, Color color, DeviceKind kind)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float barH = Math.Max(2f, size / 8f);
                float area = size - barH;
                DrawBar(g, size, barH, percent, color, kind);

                // Percentage in the battery color, with a thin dark outline for light taskbars.
                string text = percent < 0 ? "?" : percent >= 100 ? "F" : percent.ToString();
                float px = text.Length >= 2 ? area * 1.0f : area * 1.08f;
                using (var family = new FontFamily("Segoe UI"))
                using (var path = new GraphicsPath())
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    path.AddString(text, family, (int)FontStyle.Bold, px, new RectangleF(-size, 0, size * 3, area + 1), sf);
                    var bounds = path.GetBounds();
                    float scale = Math.Min(1f, (size - 0.5f) / bounds.Width);
                    using (var m = new Matrix())
                    {
                        m.Translate(size / 2f, 0);
                        m.Scale(scale, 1);
                        m.Translate(-size / 2f, 0);
                        path.Transform(m);
                    }
                    using (var outline = new Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1f, size / 16f)) { LineJoin = LineJoin.Round })
                        g.DrawPath(outline, path);
                    using (var brush = new SolidBrush(color))
                        g.FillPath(brush, path);
                }

                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        /// <summary>
        /// Level bar along the bottom. Its shape tells devices apart at 16 px:
        /// solid for mice, key-like segments for keyboards, dots for headsets.
        /// </summary>
        static void DrawBar(Graphics g, int size, float barH, int percent, Color color, DeviceKind kind)
        {
            float y = size - barH;
            float filled = percent > 0 ? size * percent / 100f : 0;
            using (var track = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
            using (var fill = new SolidBrush(color))
            {
                if (kind == DeviceKind.Mouse || kind == DeviceKind.Other || kind == DeviceKind.Mousepad)
                {
                    g.FillRectangle(track, 0, y, size, barH);
                    g.FillRectangle(fill, 0, y, filled, barH);
                    return;
                }
                int segments = kind == DeviceKind.Keyboard ? 4 : 5;
                float gap = Math.Max(1f, size / 16f);
                float w = (size - gap * (segments - 1)) / segments;
                for (int i = 0; i < segments; i++)
                {
                    float x = i * (w + gap);
                    var b = x + w / 2 <= filled ? fill : track;
                    if (kind == DeviceKind.Keyboard) g.FillRectangle(b, x, y, w, barH);
                    else g.FillEllipse(b, x + (w - barH) / 2, y, barH, barH);
                }
            }
        }

        /// <summary>App icon: a rounded battery glyph, used for the window and the "no device" tray state.</summary>
        public static Icon AppIcon(int size)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // Same mark as the panel: dark mouse on an ice-blue rounded tile, lit wheel.
                using (var tile = Rounded(new RectangleF(0, 0, size - 1, size - 1), size * 0.28f))
                using (var grad = new LinearGradientBrush(new PointF(0, 0), new PointF(size, size), Color.FromArgb(139, 224, 255), Color.FromArgb(47, 159, 232)))
                    g.FillPath(grad, tile);
                float mw = size * 0.44f, mh = size * 0.62f, mx = (size - mw) / 2, my = size * 0.2f;
                using (var body = Rounded(new RectangleF(mx, my, mw, mh), mw / 2))
                using (var b = new SolidBrush(Color.FromArgb(16, 18, 22)))
                    g.FillPath(b, body);
                using (var wheel = new SolidBrush(Color.FromArgb(139, 224, 255)))
                    g.FillRectangle(wheel, size / 2f - size * 0.035f, my + mh * 0.14f, size * 0.07f, mh * 0.18f);
                IntPtr hi = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(hi).Clone();
                DestroyIcon(hi);
                return icon;
            }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
