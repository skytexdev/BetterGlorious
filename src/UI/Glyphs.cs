using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using BetterGlorious.Devices;

namespace BetterGlorious.UI
{
    /// <summary>
    /// Icons from the system icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on
    /// Windows 10. Both ship with Windows and share these code points.
    /// </summary>
    static class Glyphs
    {
        public const string Mouse = "";
        public const string Keyboard = "";
        public const string Headphone = "";
        public const string Device = "";
        public const string Settings = "";
        public const string Info = "";
        public const string Refresh = "";
        public const string Usb = "";
        public const string Bluetooth = "";
        public const string Wireless = "";
        public const string Charging = "";

        static string family;

        public static string FontFamily
        {
            get
            {
                if (family == null)
                {
                    using (var fonts = new InstalledFontCollection())
                    {
                        var names = fonts.Families.Select(f => f.Name).ToList();
                        family = names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                               : names.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets"
                               : "Segoe UI Symbol";
                    }
                }
                return family;
            }
        }

        public static Font Font(float size, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return new Font(FontFamily, size, FontStyle.Regular, unit);
        }

        public static string For(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Mouse: return Mouse;
                case DeviceKind.Keyboard: return Keyboard;
                case DeviceKind.Headset: return Headphone;
                default: return Device;
            }
        }

        public static string For(Link link)
        {
            return link == Link.Usb ? Usb : link == Link.Bluetooth ? Bluetooth : Wireless;
        }
    }
}
