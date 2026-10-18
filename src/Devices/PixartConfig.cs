using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGlorious.Hid;

namespace BetterGlorious.Devices
{
    /// <summary>
    /// Settings of the PixArt wireless mice (Model O2 / D2 / I2 / O2 Mini Wireless), sent the same
    /// way Glorious CORE sends them. The firmware has no getters, so the app remembers the values
    /// (Settings.DeviceState) and always writes complete blocks.
    ///
    /// Header: cable "03 cmd FA ...", receiver "03 cmd FB chunk ...", Bluetooth "05 cmd chunk ...".
    /// Commands: 01 profile, 02 lighting, 04 performance, 06 standby.
    /// </summary>
    public sealed class PixartConfig : DeviceConfig
    {
        public int Profile = 1;
        public List<int> Dpi = new List<int> { 400, 800, 1600, 3200 };
        public List<int> DpiColors = new List<int> { 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00 };
        public int ActiveStage = 1;
        public int PollingHz = 1000;
        public int DebounceMs = 2;
        public int LiftOff;            // 0 low (1 mm), 1 high (2 mm)
        public bool MotionSync;
        public int SleepMinutes = 7;   // 0 = never
        public int Effect = 1;
        public int Speed = 50;         // 0-100
        public int Brightness = 100;   // 0-100
        public int EffectColor = 0xFF0000;

        public const int MaxStages = 6, MinDpi = 100, MaxDpi = 26000;
        static readonly Dictionary<int, byte> Polling = new Dictionary<int, byte> { { 125, 1 }, { 250, 2 }, { 500, 3 }, { 1000, 4 } };

        // ---- Writes ------------------------------------------------------------------------

        public static bool WritePerformance(HidConnection c, Link link, PixartConfig s)
        {
            var d = new byte[64];
            d[0] = (byte)Math.Max(0, Math.Min(s.Dpi.Count - 1, s.ActiveStage));
            d[1] = (byte)s.Dpi.Count;
            d[2] = (byte)(s.LiftOff == 0 ? 1 : 2);
            d[3] = (byte)s.DebounceMs;
            byte code;
            d[4] = Polling.TryGetValue(s.PollingHz, out code) ? code : (byte)4;
            d[5] = (byte)(s.MotionSync ? 1 : 0);
            bool cable = link == Link.Usb;
            for (int i = 0; i < s.Dpi.Count; i++)
            {
                int at = cable ? 12 + i * 8 : 6 + i * 5;
                int v = Math.Max(MinDpi, Math.Min(MaxDpi, s.Dpi[i])) / 50;
                int rgb = i < s.DpiColors.Count ? s.DpiColors[i] : 0xFFFFFF;
                d[at] = (byte)v; d[at + 1] = (byte)(v >> 8);
                d[at + 2] = (byte)(rgb >> 16); d[at + 3] = (byte)(rgb >> 8); d[at + 4] = (byte)rgb;
            }
            byte prof = (byte)s.Profile;
            if (cable) return Send(c, Header(0x03, 0x04, 0xFA, prof), 4, d, 0, 60, 150);
            // Wireless: chunk 0 carries 11 bytes, the rest 10 each.
            int[][] spans = { new[] { 0, 11 }, new[] { 11, 10 }, new[] { 21, 10 }, new[] { 31, 10 } };
            for (int j = 0; j < spans.Length; j++)
            {
                byte[] head = link == Link.Bluetooth ? new byte[] { 0x05, 0x04, (byte)j, prof } : new byte[] { 0x03, 0x04, 0xFB, (byte)j, prof };
                if (!Send(c, head, head.Length, d, spans[j][0], spans[j][1], link == Link.Bluetooth ? 30 : 150)) return false;
            }
            return true;
        }

        public static bool WriteLighting(HidConnection c, Link link, PixartConfig s)
        {
            int rate = s.Speed > 4 ? s.Speed * 20 / 100 : 1;
            int bright = s.Brightness <= 0 ? 0 : Math.Max(1, s.Brightness * 20 / 100);
            bool cable = link == Link.Usb;
            var d = new byte[40];
            d[0] = (byte)rate; d[1] = (byte)bright; d[2] = 1; d[3] = (byte)rate; d[4] = (byte)bright;
            int at = cable ? 11 : 5;
            d[at] = (byte)(s.EffectColor >> 16); d[at + 1] = (byte)(s.EffectColor >> 8); d[at + 2] = (byte)s.EffectColor;
            byte prof = (byte)s.Profile, eff = (byte)s.Effect;
            if (cable) return Send(c, new byte[] { 0x03, 0x02, 0xFA, prof, eff }, 5, d, 0, 29, 60);
            if (link == Link.Bluetooth)
                return Send(c, new byte[] { 0x05, 0x02, 0, prof, eff }, 5, d, 0, 11, 30)
                    && Send(c, new byte[] { 0x05, 0x02, 1, prof, eff }, 5, d, 11, 12, 30);
            int[][] spans = { new[] { 0, 8 }, new[] { 8, 9 }, new[] { 17, 9 } };
            for (int j = 0; j < spans.Length; j++)
                if (!Send(c, new byte[] { 0x03, 0x02, 0xFB, (byte)j, prof, eff }, 6, d, spans[j][0], spans[j][1], 50)) return false;
            return true;
        }

        public static bool WriteStandby(HidConnection c, Link link, PixartConfig s)
        {
            byte m = (byte)(s.SleepMinutes <= 0 ? 0xFF : Math.Min(15, s.SleepMinutes));
            var buf = link == Link.Bluetooth ? new byte[] { 0x05, 0x06, m } : new byte[] { 0x03, 0x06, (byte)(link == Link.Usb ? 0xFA : 0xFB), m };
            bool ok = c.SetFeature(buf);
            Thread.Sleep(60);
            return ok;
        }

        public static bool WriteProfile(HidConnection c, Link link, int profile)
        {
            var buf = link == Link.Bluetooth ? new byte[] { 0x05, 0x01, (byte)profile } : new byte[] { 0x03, 0x01, (byte)(link == Link.Usb ? 0xFA : 0xFB), (byte)profile };
            bool ok = c.SetFeature(buf);
            Thread.Sleep(300);
            return ok;
        }

        public static bool WriteAll(HidConnection c, Link link, PixartConfig s)
        {
            return WriteProfile(c, link, s.Profile) && WritePerformance(c, link, s) && WriteLighting(c, link, s) && WriteStandby(c, link, s);
        }

        static byte[] Header(params byte[] b) { return b; }

        static bool Send(HidConnection c, byte[] head, int dataAt, byte[] data, int from, int count, int delayMs)
        {
            var buf = new byte[64];
            Array.Copy(head, buf, head.Length);
            Array.Copy(data, from, buf, dataAt, Math.Min(count, Math.Min(data.Length - from, 64 - dataAt)));
            bool ok = c.SetFeature(buf);
            Thread.Sleep(delayMs);
            return ok;
        }

        // ---- State -------------------------------------------------------------------------

        public override DeviceConfig Clone()
        {
            var c = (PixartConfig)MemberwiseClone();
            c.Dpi = new List<int>(Dpi);
            c.DpiColors = new List<int>(DpiColors);
            return c;
        }

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(";", new[]
            {
                "profile=" + Profile, "dpi=" + string.Join(",", Dpi), "colors=" + string.Join(",", DpiColors.Select(x => x.ToString("x6"))),
                "active=" + ActiveStage, "poll=" + PollingHz, "debounce=" + DebounceMs, "lod=" + LiftOff, "msync=" + (MotionSync ? 1 : 0),
                "sleep=" + SleepMinutes, "effect=" + Effect, "speed=" + Speed, "bright=" + Brightness, "color=" + EffectColor.ToString("x6", inv),
            });
        }

        public static PixartConfig Parse(string s)
        {
            var c = new PixartConfig();
            if (string.IsNullOrEmpty(s)) return c;
            try
            {
                foreach (var part in s.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                    switch (k)
                    {
                        case "profile": c.Profile = int.Parse(v); break;
                        case "dpi": c.Dpi = v.Split(',').Select(int.Parse).ToList(); break;
                        case "colors": c.DpiColors = v.Split(',').Select(x => Convert.ToInt32(x, 16)).ToList(); break;
                        case "active": c.ActiveStage = int.Parse(v); break;
                        case "poll": c.PollingHz = int.Parse(v); break;
                        case "debounce": c.DebounceMs = int.Parse(v); break;
                        case "lod": c.LiftOff = int.Parse(v); break;
                        case "msync": c.MotionSync = v == "1"; break;
                        case "sleep": c.SleepMinutes = int.Parse(v); break;
                        case "effect": c.Effect = int.Parse(v); break;
                        case "speed": c.Speed = int.Parse(v); break;
                        case "bright": c.Brightness = int.Parse(v); break;
                        case "color": c.EffectColor = Convert.ToInt32(v, 16); break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("pixart state", ex); }
            return c;
        }

        public override string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"profile\":").Append(Profile);
            sb.Append(",\"firmware\":\"\"");
            sb.Append(",\"dpi\":[").Append(string.Join(",", Dpi)).Append(']');
            sb.Append(",\"dpiColors\":[").Append(string.Join(",", DpiColors.Select(x => Json.Str("#" + x.ToString("x6"))))).Append(']');
            sb.Append(",\"activeStage\":").Append(ActiveStage);
            sb.Append(",\"pollingHz\":").Append(PollingHz);
            sb.Append(",\"debounceMs\":").Append(DebounceMs);
            sb.Append(",\"liftOff\":").Append(LiftOff);
            sb.Append(",\"motionSync\":").Append(Json.Bool(MotionSync));
            sb.Append(",\"sleepSeconds\":").Append(SleepMinutes <= 0 ? 0 : SleepMinutes * 60);
            sb.Append(",\"effect\":").Append(Effect);
            sb.Append(",\"effectSpeed\":").Append(Speed);
            sb.Append(",\"effectColor\":").Append(Json.Str("#" + EffectColor.ToString("x6")));
            sb.Append(",\"maxStages\":").Append(MaxStages);
            sb.Append(",\"minDpi\":").Append(MinDpi);
            sb.Append(",\"maxDpi\":").Append(MaxDpi);
            sb.Append(",\"buttons\":{},\"macros\":{},\"maxMacros\":0");
            sb.Append(",\"features\":{\"readback\":false,\"buttons\":false,\"macros\":false,\"motionSync\":true,\"profiles\":3,\"sleepMinutes\":[],\"sleepRange\":[1,15],\"sleepNever\":true,\"liftOffLabels\":[\"1 mm\",\"2 mm\"]}");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
