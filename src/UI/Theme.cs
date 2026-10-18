using System.Drawing;
using BetterGlorious.Devices;

namespace BetterGlorious.UI
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(18, 19, 23);
        public static readonly Color Side = Color.FromArgb(24, 25, 30);
        public static readonly Color Card = Color.FromArgb(32, 34, 40);
        public static readonly Color CardHover = Color.FromArgb(38, 40, 47);
        public static readonly Color Line = Color.FromArgb(48, 50, 58);
        public static readonly Color Text = Color.FromArgb(236, 237, 240);
        public static readonly Color SubText = Color.FromArgb(150, 155, 165);
        public static readonly Color Accent = Color.FromArgb(94, 140, 255);
        public static readonly Color Gray = Color.FromArgb(120, 124, 132);
        public static readonly Color Green = Color.FromArgb(70, 200, 110);
        public static readonly Color Yellow = Color.FromArgb(240, 185, 40);
        public static readonly Color Red = Color.FromArgb(240, 70, 60);
        public static readonly Color Blue = Color.FromArgb(40, 170, 255);
        public static readonly Color Badge = Color.FromArgb(70, 56, 20);
        public static readonly Color BadgeText = Color.FromArgb(240, 190, 90);

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        /// <summary>Battery color: blue charging, then green / yellow / red by level; gray when stale.</summary>
        public static Color ForLevel(int percent, ChargeState charge, bool stale, int lowThreshold)
        {
            if (percent < 0 || stale) return Gray;
            if (charge == ChargeState.Charging) return Blue;
            if (percent <= lowThreshold) return Red;
            if (percent <= 50) return Yellow;
            return Green;
        }
    }
}
