using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGlorious.Hid;

namespace BetterGlorious.Devices
{
    /// <summary>
    /// Settings of the GMMK 3 / 3 PRO HE keyboards, sent the way Glorious CORE 2 sends them
    /// (GMMK3HEDeviceHandler). Untested on hardware.
    ///
    /// Cable and 2.4 GHz: feature report 0, 65 bytes, command from [1]; checksum = 0xFF - (sum &amp; 0xFF)
    /// of [1..7] at [8]. Bluetooth: output report [06 55 cmd...]. Commands: 04 report rate,
    /// 05 profile, 07/08 main/side lighting, 11 debounce, 12 sleep timers. Only the profile and the
    /// sleep timer have getters, so the app remembers the rest (Settings.DeviceState).
    /// </summary>
    public sealed class Gmmk3HeConfig : DeviceConfig
    {
        public int Profile = 1;        // 1-3 (device: 0-2)
        public int PollingHz = 1000;
        public int DebounceMs = 16;    // CORE offers 0, 2, 8, 16
        public int SleepMinutes = 7;   // 0 = never
        public int Effect = 1;         // device effect value, see Effects
        public int Speed = 50;         // 0-100
        public int Brightness = 100;   // 0-100
        public int EffectColor = 0xFF0000;
        public bool Custom;            // false = the effect's own colors ("chromatic", CORE's default), true = EffectColor

        public static readonly int[] PollingRates = { 125, 250, 500, 1000, 2000, 4000, 8000 };
        static readonly int[] RateCodes = { 6, 5, 4, 3, 2, 1, 0 };
        public static readonly int[] DebounceOptions = { 0, 2, 8, 16 };
        public static readonly int[] SleepOptions = { 1, 3, 5, 7, 10, 15, 30 };

        /// <summary>Device value → UI name key (PresetLightingEffectData_Keyboard_V1 through MapEffectValue).</summary>
        public static readonly string[] Effects =
        {
            "kbOff", "kbGlorious", "kbWave1", "kbWave2", "kbSpiral", "kbAcid", "kbBreathing", "kbStatic", "kbRipple",
            "kbPassTrace", "kbFastRun", "kbMatrix2", "kbMatrix3", "kbRainbow", "kbHeartbeat", "kbDigital", "kbKamehameha",
            "kbPingpong", "kbSurmount",
        };
        /// <summary>Every effect except Off, Glorious and Acid can be switched to a custom color.</summary>
        static bool UsesColor(int effect) { return effect != 0 && effect != 1 && effect != 5; }
        /// <summary>CORE hides Surmount (18) from its list.</summary>
        const int VisibleEffects = 18;

        // ---- Writes ------------------------------------------------------------------------

        static byte Checksum(byte[] buf, int from, int to)
        {
            int sum = 0;
            for (int i = from; i <= to; i++) sum += buf[i];
            return (byte)(0xFF - (sum & 0xFF));
        }

        static bool Send(HidConnection c, Link link, byte[] buf)
        {
            bool ok;
            if (link == Link.Bluetooth)
            {
                var bt = new byte[66];
                bt[0] = 0x06; bt[1] = 0x55;
                Array.Copy(buf, 1, bt, 2, 64);
                ok = c.SetOutput(bt);
            }
            else ok = c.SetFeature(buf);
            Thread.Sleep(550); // CORE waits this long after every write
            return ok;
        }

        static byte[] Frame(params byte[] cmd)
        {
            var buf = new byte[65];
            Array.Copy(cmd, 0, buf, 1, cmd.Length);
            buf[8] = Checksum(buf, 1, 7);
            return buf;
        }

        public static bool WritePolling(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            int i = Array.IndexOf(PollingRates, s.PollingHz);
            return Send(c, link, Frame(0x04, (byte)(s.Profile - 1), (byte)(i < 0 ? 3 : RateCodes[i])));
        }

        public static bool WriteDebounce(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            return Send(c, link, Frame(0x11, 0, (byte)Math.Round(s.DebounceMs / 5.0, MidpointRounding.AwayFromZero)));
        }

        public static bool WriteSleep(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            var buf = Frame(0x12);
            int seconds = s.SleepMinutes <= 0 ? 0 : s.SleepMinutes * 60;
            // One report carries the timers of every link: sleep then deep sleep (always off).
            int[][] slots = { new[] { 9, 13 }, new[] { 11, 15 }, new[] { 17, 19 } }; // Bluetooth, 2.4 GHz, cable
            foreach (var slot in slots)
            {
                buf[slot[0]] = (byte)seconds; buf[slot[0] + 1] = (byte)(seconds >> 8);
                buf[slot[1]] = 0xFF; buf[slot[1] + 1] = 0xFF;
            }
            return Send(c, link, buf);
        }

        public static bool WriteLighting(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            int speed = 20 - (int)Math.Ceiling(s.Speed / 100.0 * 20);
            int bright = (int)Math.Ceiling(s.Brightness / 100.0 * 20);
            int option = s.Custom && UsesColor(s.Effect) ? 9 : 8; // 9 = the chosen color, 8 = the effect's own colors
            foreach (byte cmd in new byte[] { 0x07, 0x08 }) // main lighting, then the side strip
            {
                var buf = new byte[65];
                buf[1] = cmd; buf[2] = (byte)s.Effect; buf[3] = (byte)speed; buf[4] = (byte)bright; buf[5] = (byte)option;
                buf[6] = (byte)(s.EffectColor >> 16); buf[7] = (byte)(s.EffectColor >> 8); buf[8] = (byte)s.EffectColor;
                buf[9] = Checksum(buf, 1, 8);
                if (!Send(c, link, buf)) return false;
            }
            return true;
        }

        /// <summary>Switching profile does not bring its lighting back, so CORE re-sends it; so do we.</summary>
        public static bool WriteProfile(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            return Send(c, link, Frame(0x05, (byte)(s.Profile - 1), 0)) && WriteLighting(c, link, s);
        }

        public static bool WriteAll(HidConnection c, Link link, Gmmk3HeConfig s)
        {
            return WriteProfile(c, link, s) && WritePolling(c, link, s) && WriteDebounce(c, link, s) && WriteSleep(c, link, s);
        }

        // ---- State -------------------------------------------------------------------------

        public override DeviceConfig Clone() { return (Gmmk3HeConfig)MemberwiseClone(); }

        public string Serialize()
        {
            return string.Join(";", new[]
            {
                "profile=" + Profile, "poll=" + PollingHz, "debounce=" + DebounceMs, "sleep=" + SleepMinutes,
                "effect=" + Effect, "custom=" + (Custom ? 1 : 0), "speed=" + Speed, "bright=" + Brightness, "color=" + EffectColor.ToString("x6", CultureInfo.InvariantCulture),
            });
        }

        public static Gmmk3HeConfig Parse(string s)
        {
            var c = new Gmmk3HeConfig();
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
                        case "profile": c.Profile = Math.Max(1, Math.Min(3, int.Parse(v))); break;
                        case "poll": c.PollingHz = int.Parse(v); break;
                        case "debounce": c.DebounceMs = int.Parse(v); break;
                        case "sleep": c.SleepMinutes = int.Parse(v); break;
                        case "effect": c.Effect = int.Parse(v); break;
                        case "speed": c.Speed = int.Parse(v); break;
                        case "custom": c.Custom = v == "1"; break;
                        case "bright": c.Brightness = int.Parse(v); break;
                        case "color": c.EffectColor = Convert.ToInt32(v, 16); break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("gmmk3 he state", ex); }
            return c;
        }

        public override string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"profile\":").Append(Profile);
            sb.Append(",\"firmware\":\"\"");
            sb.Append(",\"dpi\":[],\"dpiColors\":[],\"activeStage\":0");
            sb.Append(",\"pollingHz\":").Append(PollingHz);
            sb.Append(",\"debounceMs\":").Append(DebounceMs);
            sb.Append(",\"liftOff\":0,\"motionSync\":false");
            sb.Append(",\"sleepSeconds\":").Append(SleepMinutes <= 0 ? 0 : SleepMinutes * 60);
            sb.Append(",\"effect\":").Append(Effect);
            sb.Append(",\"effectSpeed\":").Append(Speed);
            sb.Append(",\"effectColor\":").Append(Json.Str("#" + EffectColor.ToString("x6")));
            sb.Append(",\"effectCustom\":").Append(Json.Bool(Custom));
            sb.Append(",\"maxStages\":0,\"minDpi\":0,\"maxDpi\":0");
            sb.Append(",\"buttons\":{},\"macros\":{},\"maxMacros\":0");
            sb.Append(",\"features\":{\"readback\":false,\"buttons\":false,\"macros\":false,\"motionSync\":false,\"profiles\":3");
            sb.Append(",\"dpi\":false,\"liftOff\":false,\"keyboard\":true");
            sb.Append(",\"pollingRates\":[").Append(string.Join(",", PollingRates)).Append(']');
            sb.Append(",\"debounceOptions\":[").Append(string.Join(",", DebounceOptions)).Append(']');
            sb.Append(",\"sleepMinutes\":[],\"sleepRange\":[1,30]");
            sb.Append(",\"sleepNever\":true");
            sb.Append(",\"effects\":[").Append(string.Join(",", Effects.Take(VisibleEffects).Select((k, i) => "{\"id\":" + i + ",\"key\":" + Json.Str(k) + ",\"color\":" + Json.Bool(UsesColor(i)) + "}"))).Append(']');
            sb.Append("}}");
            return sb.ToString();
        }
    }
}
