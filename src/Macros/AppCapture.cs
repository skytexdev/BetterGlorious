using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGlorious.Macros
{
    /// <summary>
    /// Lists open app windows with a live thumbnail and the program icon, like Discord's
    /// "share your screen" picker, so automatic profiles are picked visually instead of by exe name.
    /// </summary>
    public static class AppCapture
    {
        const int ThumbWidth = 320;

        /// <summary>JSON array of { exe, title, icon, thumb } (icon and thumb are data URLs or null).</summary>
        public static string RunningAppsJson()
        {
            var items = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe";
            var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            EnumWindows((h, l) =>
            {
                if (!IsAppWindow(h)) return true;
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                string path = ProcessPath(h);
                if (path == null) return true;
                string exe = Path.GetFileName(path);
                if (exe.Equals(self, StringComparison.OrdinalIgnoreCase) || !seen.Add(exe + "|" + title)) return true;

                string icon;
                if (!icons.TryGetValue(path, out icon)) icons[path] = icon = IconDataUrl(path);
                string thumb = IsIconic(h) ? null : Thumbnail(h);
                items.Add("{\"exe\":" + Json.Str(exe) + ",\"title\":" + Json.Str(title) +
                          ",\"icon\":" + (icon == null ? "null" : Json.Str(icon)) +
                          ",\"thumb\":" + (thumb == null ? "null" : Json.Str(thumb)) + "}");
                return items.Count < 40;
            }, IntPtr.Zero);
            return "[" + string.Join(",", items) + "]";
        }

        /// <summary>Visible, titled, not a tool window, not a cloaked background UWP frame.</summary>
        static bool IsAppWindow(IntPtr h)
        {
            if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return false;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return false;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
            int cloaked;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
            RECT r;
            GetWindowRect(h, out r);
            return r.Right - r.Left >= 120 && r.Bottom - r.Top >= 80;
        }

        static string Thumbnail(IntPtr h)
        {
            try
            {
                RECT r;
                if (!GetWindowRect(h, out r)) return null;
                int w = r.Right - r.Left, ht = r.Bottom - r.Top;
                if (w <= 0 || ht <= 0 || w > 8000 || ht > 8000) return null;
                using (var full = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(full))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = PrintWindow(h, hdc, PW_RENDERFULLCONTENT);
                        g.ReleaseHdc(hdc);
                        if (!ok) return null;
                    }
                    int tw = ThumbWidth, th = Math.Max(1, ht * ThumbWidth / w);
                    using (var small = new Bitmap(tw, th))
                    using (var g = Graphics.FromImage(small))
                    using (var ms = new MemoryStream())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(full, 0, 0, tw, th);
                        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var p = new EncoderParameters(1);
                        p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
                        small.Save(ms, jpeg, p);
                        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return null; }
        }

        static string IconDataUrl(string path)
        {
            try
            {
                using (var icon = Icon.ExtractAssociatedIcon(path))
                using (var bmp = icon.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { return null; }
        }

        static string ProcessPath(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            IntPtr proc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (proc == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(proc, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(proc); }
        }

        const int GWL_EXSTYLE = -20, GW_OWNER = 4, DWMWA_CLOAKED = 14;
        const long WS_EX_TOOLWINDOW = 0x80;
        const uint PW_RENDERFULLCONTENT = 2, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr proc, int flags, StringBuilder name, ref int size);
    }
}
