using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGlorious.Hid;

namespace BetterGlorious.Devices
{
    /// <summary>
    /// Read/write onboard settings of Sinowealth mice (Model O/D wireless family).
    /// Frame: feature report 0, [3]=02 target mouse, [4]=length, [5]=class, [6]=command, [7..]=payload.
    /// Setters use the command number; getters the same number with bit 7 set (01/01 set DPI, 01/81 get DPI).
    /// Every reply starts with a status byte at [1]: A1 ok, A0 busy, A2 failed, A4 mouse unreachable.
    /// </summary>
    public sealed class SinowealthConfig : DeviceConfig
    {
        public int Profile = 1;
        public string Firmware = "";
        public List<int> Dpi = new List<int>();
        public List<int> DpiColors = new List<int>();   // 0xRRGGBB per stage
        public int ActiveStage;                         // 0-based
        public int PollingHz;
        public int DebounceMs;
        public int LiftOff;                             // 0 = low, 1 = high
        public int SleepSeconds;                        // 0xFFFF = never
        public int Effect;                              // see Effects
        public int EffectSpeed;                         // raw device delay: 1 fast .. 21 slow, Rave/Wave 10 .. 210
        public int EffectColor;                         // first color, 0xRRGGBB
        public List<int> EffectColors = new List<int>(); // every color the effect uses (see ColorCount)

        /// <summary>
        /// How many colors each effect takes, as Glorious CORE sends them: Breathing cycles through six,
        /// Rave alternates two, Static / Breathing (single color) use one, the rest pick their own.
        /// </summary>
        public static int ColorCount(int effect)
        {
            switch (effect) { case 3: return 7; case 7: return 2; case 4: case 5: return 1; default: return 0; }
        }

        /// <summary>The fixed colors CORE 2 sends for Breathing and Rave (neither has a color picker).</summary>
        public static readonly int[] DefaultBreathing = { 0xFF0000, 0xFFA500, 0xFFFF00, 0x00FF00, 0x007FFF, 0x0000FF, 0x8B00FF };
        public static readonly int[] DefaultRave = { 0xFF0000, 0x0000FF };

        /// <summary>
        /// CORE's speed slider (1-100, fast = high) to the firmware's delay byte. Rave and Wave use a
        /// scale ten times finer: (105 - s) * 2 instead of (105 - s) / 5 (PrepareLEDEffectAndColorsBuffer).
        /// </summary>
        public static int SpeedToDevice(int effect, int ui)
        {
            ui = Math.Max(1, Math.Min(100, ui));
            return effect == 7 || effect == 8 ? (105 - ui) * 2 : Math.Max(1, (105 - ui) / 5);
        }

        public static int SpeedFromDevice(int effect, int raw)
        {
            int ui = effect == 7 || effect == 8 ? 105 - raw / 2 : 105 - raw * 5;
            return Math.Max(1, Math.Min(100, ui));
        }
        /// <summary>Button id -> 7 action bytes (see docs/PROTOCOLS.md). Only buttons the mouse has.</summary>
        public SortedDictionary<int, byte[]> Buttons = new SortedDictionary<int, byte[]>();
        /// <summary>Macro id -> event stream as stored on the mouse.</summary>
        public SortedDictionary<int, byte[]> Macros = new SortedDictionary<int, byte[]>();

        /// <summary>Button ids the Model O/D family firmware uses: L, R, M, Back, Fwd, wheel up/down, DPI.</summary>
        public static readonly int[] ButtonIds = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x10, 0x11, 0x14 };
        public const int MaxMacros = 8;

        public const int MaxStages = 6;
        public const int MinDpi = 100;

        /// <summary>Catalog key, e.g. "ModelO2Pro4k8kWireless". Some models take different commands (see Pro).</summary>
        public string Model = "";
        /// <summary>O2 / D2 Pro and Series 2 Pro: 26000 DPI, polling over 01/0A, lift-off over 01/0B, Motion Sync.</summary>
        public bool Pro { get { return Model.StartsWith("ModelO2Pro") || Model.StartsWith("ModelD2Pro") || Model.StartsWith("SeriesTwoPro"); } }
        bool HighPolling { get { return Model.Contains("4k8k") || Model.StartsWith("SeriesTwoPro"); } }
        public int MaxDpi { get { return Pro ? 26000 : 19000; } }
        public bool MotionSync;
        /// <summary>Polling rates CORE offers for this model (wired); 8000 Hz wireless is Series 2 Pro only.</summary>
        public int[] PollingRates { get { return HighPolling ? new[] { 125, 250, 500, 1000, 2000, 4000, 8000 } : new[] { 125, 250, 500, 1000 }; } }
        /// <summary>Model O Wireless can light differently on cable and wireless (CORE's "separate" option).</summary>
        public bool SeparateBrightness { get { return Model == "ModelOWireless"; } }

        static readonly Dictionary<int, byte> PollingCodes = new Dictionary<int, byte> { { 125, 0x08 }, { 250, 0x04 }, { 500, 0x02 }, { 1000, 0x01 }, { 2000, 0x20 }, { 4000, 0x40 }, { 8000, 0x80 } };

        // ---- Reading -----------------------------------------------------------------------

        /// <summary>
        /// Reads everything. With <paramref name="known"/> (the last full read) the button map and the
        /// macros are taken from it instead of re-read, which cuts a ~2 s read to ~0.1 s; the button
        /// map is still re-read when the profile changed, since it is stored per profile.
        /// </summary>
        public static SinowealthConfig Read(HidConnection c, SinowealthConfig known = null, string model = null)
        {
            var cfg = new SinowealthConfig { Model = model ?? (known != null ? known.Model : "") };
            var r = Get(c, 0, 0x85, 1);
            if (r == null) return null; // mouse asleep or not answering
            cfg.Profile = Math.Max(1, (int)r[7]);
            byte p = (byte)cfg.Profile;

            r = Get(c, 0, 0x81, 3);
            if (r != null) cfg.Firmware = r[7] + "." + r[8] + "." + r[9] + "." + r[10];

            r = Get(c, 1, 0x81, 2 + MaxStages * 4, p);
            if (r != null)
            {
                int n = Math.Min(MaxStages, (int)r[8]);
                for (int i = 0; i < n; i++) cfg.Dpi.Add((r[9 + i * 4] << 8) | r[10 + i * 4]);
            }

            r = Get(c, 2, 0x81, 1 + MaxStages * 3, p);
            for (int i = 0; i < cfg.Dpi.Count; i++)
                cfg.DpiColors.Add(r == null ? 0xFFFFFF : (r[8 + i * 3] << 16) | (r[9 + i * 3] << 8) | r[10 + i * 3]);

            r = Get(c, 1, 0x82, 2, p);
            if (r != null) cfg.ActiveStage = Math.Max(0, Math.Min(cfg.Dpi.Count - 1, r[8] - 1));

            r = Get(c, 1, 0x80, 1);
            if (r != null) cfg.PollingHz = PollingCodes.Where(kv => kv.Value == r[7]).Select(kv => kv.Key).FirstOrDefault();

            r = Get(c, 0, 0x88, 2, p);
            if (r != null) cfg.DebounceMs = r[8];

            r = Get(c, 1, 0x88, 1);
            if (r != null) cfg.LiftOff = r[7];
            if (cfg.Pro)
            {
                // Pro models keep lift-off and Motion Sync per profile (getters 01/8B and 01/89).
                r = Get(c, 1, 0x8B, 2, p);
                if (r != null) cfg.LiftOff = r[8];
                r = Get(c, 1, 0x89, 2, p);
                if (r != null) cfg.MotionSync = r[8] != 0;
            }

            r = Get(c, 0, 0x87, 2);
            if (r != null) cfg.SleepSeconds = (r[7] << 8) | r[8];

            r = Get(c, 2, 0x80, 5 + 6 * 3, p);
            if (r != null)
            {
                cfg.Effect = r[9];
                cfg.EffectSpeed = r[11];
                for (int i = 0; i < ColorCount(cfg.Effect); i++)
                    cfg.EffectColors.Add((r[12 + i * 3] << 16) | (r[13 + i * 3] << 8) | r[14 + i * 3]);
                // Effects without a color leave the bytes empty; offer red as the starting color then.
                cfg.EffectColor = cfg.EffectColors.Count > 0 && (cfg.EffectColors[0] & 0xFEFFFF) != 0 ? cfg.EffectColors[0] : 0xFF0000;
            }

            if (known != null)
            {
                var copy = (SinowealthConfig)known.Clone();
                if (known.Profile == cfg.Profile) cfg.Buttons = copy.Buttons;
                cfg.Macros = copy.Macros;
                if (known.Profile == cfg.Profile) return cfg;
            }
            foreach (int id in ButtonIds)
            {
                r = Get(c, 3, 0x80, 10, p, (byte)id);
                if (r == null) continue;
                var action = new byte[7];
                Array.Copy(r, 10, action, 0, 7);
                cfg.Buttons[id] = action;
            }

            for (int id = 1; id <= MaxMacros && known == null; id++)
            {
                var data = ReadMacro(c, id);
                if (data != null) cfg.Macros[id] = data;
            }
            return cfg;
        }

        /// <summary>Macro header 04/81 gives the length (A2 = empty slot); 04/83 reads it 51 bytes at a time.</summary>
        static byte[] ReadMacro(HidConnection c, int id)
        {
            byte[] h = null;
            for (int attempt = 0; attempt < 3 && h == null; attempt++)
            {
                var r0 = c.Transact(Frame(4, 0x81, 6, new[] { (byte)(id >> 8), (byte)id }), 70);
                if (r0 == null || r0[1] == 0xA2 || r0[1] == 0xA4) return null; // A2: empty slot
                if (r0[1] == 0xA1) h = r0;
            }
            if (h == null) return null;
            int len = (h[11] << 8) | h[12];
            if (len <= 0 || len > 4096) return null;
            var data = new byte[len];
            for (int off = 0; off < len; off += 51)
            {
                int chunk = Math.Min(51, len - off);
                var r = Get(c, 4, 0x83, 0x3A, (byte)(id >> 8), (byte)id, 0, 0, (byte)(off >> 8), (byte)off, (byte)chunk);
                if (r == null) return null;
                Array.Copy(r, 14, data, off, chunk);
            }
            return data;
        }

        public override DeviceConfig Clone()
        {
            var c = (SinowealthConfig)MemberwiseClone();
            c.Dpi = new List<int>(Dpi);
            c.DpiColors = new List<int>(DpiColors);
            c.EffectColors = new List<int>(EffectColors);
            c.Buttons = new SortedDictionary<int, byte[]>(Buttons.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone()));
            c.Macros = new SortedDictionary<int, byte[]>(Macros.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone()));
            return c;
        }

        /// <summary>Writes every setting in <paramref name="target"/> that differs from <paramref name="current"/>.</summary>
        public static bool Restore(HidConnection c, SinowealthConfig current, SinowealthConfig target)
        {
            bool ok = true;
            if (current.Profile != target.Profile) ok &= SetProfile(c, target.Profile);
            int p = target.Profile;
            if (!current.Dpi.SequenceEqual(target.Dpi)) ok &= SetDpi(c, p, target.Dpi);
            if (!current.DpiColors.SequenceEqual(target.DpiColors)) ok &= SetDpiColors(c, p, target.DpiColors);
            if (current.ActiveStage != target.ActiveStage) ok &= SetActiveStage(c, p, target.ActiveStage);
            if (current.PollingHz != target.PollingHz) ok &= target.SetPollingFor(c, target.PollingHz);
            if (current.DebounceMs != target.DebounceMs) ok &= SetDebounce(c, p, target.DebounceMs);
            if (current.LiftOff != target.LiftOff) ok &= target.SetLiftOffFor(c, target.LiftOff);
            if (target.Pro && current.MotionSync != target.MotionSync) ok &= target.SetMotionSync(c, target.MotionSync);
            if (current.SleepSeconds != target.SleepSeconds) ok &= SetSleep(c, target.SleepSeconds);
            if (current.Effect != target.Effect || current.EffectSpeed != target.EffectSpeed || !current.EffectColors.SequenceEqual(target.EffectColors))
                ok &= SetEffect(c, p, target.Effect, target.EffectSpeed, target.EffectColors);
            foreach (var kv in target.Buttons)
            {
                byte[] now;
                if (!current.Buttons.TryGetValue(kv.Key, out now) || !now.SequenceEqual(kv.Value)) ok &= SetButton(c, p, kv.Key, kv.Value);
            }
            foreach (var id in current.Macros.Keys.Union(target.Macros.Keys).ToList())
            {
                byte[] a, b;
                current.Macros.TryGetValue(id, out a);
                target.Macros.TryGetValue(id, out b);
                if (b == null) { if (a != null) DeleteMacro(c, id); }
                else if (a == null || !a.SequenceEqual(b)) ok &= SetMacro(c, id, b);
            }
            return ok;
        }

        // ---- Writing -----------------------------------------------------------------------

        public static bool SetDpi(HidConnection c, int profile, IList<int> stages)
        {
            var payload = new List<byte> { (byte)profile, (byte)stages.Count };
            foreach (int d in stages)
            {
                int v = Math.Max(MinDpi, Math.Min(26000, d)); // the UI keeps each model within its own maximum
                payload.AddRange(new[] { (byte)(v >> 8), (byte)v, (byte)(v >> 8), (byte)v }); // X then Y
            }
            return Set(c, 1, 0x01, payload.ToArray());
        }

        public static bool SetDpiColors(HidConnection c, int profile, IList<int> colors)
        {
            var payload = new byte[1 + MaxStages * 3];
            payload[0] = (byte)profile;
            for (int i = 0; i < colors.Count && i < MaxStages; i++)
            {
                payload[1 + i * 3] = (byte)(colors[i] >> 16);
                payload[2 + i * 3] = (byte)(colors[i] >> 8);
                payload[3 + i * 3] = (byte)colors[i];
            }
            return Set(c, 2, 0x01, payload);
        }

        public static bool SetActiveStage(HidConnection c, int profile, int stage)
        {
            return Set(c, 1, 0x02, (byte)profile, (byte)(stage + 1));
        }

        public static bool SetPolling(HidConnection c, int hz)
        {
            byte code;
            return PollingCodes.TryGetValue(hz, out code) && Set(c, 1, 0x00, code);
        }

        /// <summary>
        /// O2 / D2 Pro and Series 2 Pro (CORE: 01/0A [profile, wired, wireless]). Wireless tops out at
        /// 4000 Hz except on the Series 2 Pro.
        /// </summary>
        public bool SetPollingFor(HidConnection c, int hz)
        {
            if (!Pro) return SetPolling(c, hz);
            byte wired, wireless;
            if (!PollingCodes.TryGetValue(hz, out wired)) return false;
            int wl = Model.StartsWith("SeriesTwoPro") ? hz : Math.Min(hz, Model.Contains("4k8k") ? 4000 : 1000);
            PollingCodes.TryGetValue(wl, out wireless);
            return Set(c, 1, 0x0A, (byte)Profile, wired, wireless);
        }

        /// <summary>Lift-off: Pro models take it per profile over 01/0B.</summary>
        public bool SetLiftOffFor(HidConnection c, int lod)
        {
            return Pro ? Set(c, 1, 0x0B, (byte)Profile, (byte)(lod == 0 ? 0 : 1)) : SetLiftOff(c, lod);
        }

        /// <summary>Motion Sync (Pro models): 01/09 [profile, on].</summary>
        public bool SetMotionSync(HidConnection c, bool on)
        {
            return Pro && Set(c, 1, 0x09, (byte)Profile, (byte)(on ? 1 : 0));
        }

        public static bool SetDebounce(HidConnection c, int profile, int ms)
        {
            return Set(c, 0, 0x08, (byte)profile, (byte)Math.Max(0, Math.Min(32, ms)));
        }

        public static bool SetLiftOff(HidConnection c, int lod)
        {
            return Set(c, 1, 0x08, (byte)(lod == 0 ? 0 : 1));
        }

        public static bool SetSleep(HidConnection c, int seconds)
        {
            int s = seconds <= 0 ? 0xFFFF : Math.Min(0xFFFE, seconds);
            return Set(c, 0, 0x07, (byte)(s >> 8), (byte)s);
        }

        /// <summary>
        /// Effect write as CORE's ModelOSeries.SetLEDEffect does it: [profile, FF (all zones), effect,
        /// 0 (direction), speed, RGB × ColorCount(effect)]. Seamless breathing carries a fixed red.
        /// A pure black color is sent as 01 00 00, since 00 00 00 means "slot unused".
        /// </summary>
        public static bool SetEffect(HidConnection c, int profile, int effect, int speed, IList<int> colors)
        {
            var payload = new List<byte> { (byte)profile, 0xFF, (byte)effect, 0x00, (byte)Math.Max(1, Math.Min(255, speed)) };
            int n = ColorCount(effect);
            if (effect == 2) payload.AddRange(new byte[] { 0xFF, 0x00, 0x00 });
            // Breathing and Rave always get CORE's palette; Static / Breathing (single color) get the picked color.
            int[] fixedColors = effect == 3 ? DefaultBreathing : effect == 7 ? DefaultRave : null;
            for (int i = 0; i < n; i++)
            {
                int rgb = fixedColors != null ? fixedColors[i] : colors != null && i < colors.Count ? colors[i] : 0xFF0000;
                if (rgb == 0) rgb = 0x010000;
                payload.Add((byte)(rgb >> 16)); payload.Add((byte)(rgb >> 8)); payload.Add((byte)rgb);
            }
            return Set(c, 2, 0x00, payload.ToArray());
        }

        public static bool SetProfile(HidConnection c, int profile)
        {
            return Set(c, 0, 0x05, (byte)Math.Max(1, Math.Min(3, profile)));
        }

        /// <summary>Brightness 0-100 % for cable (mode 1) and wireless (mode 0), as CORE's SetLEDBright. Not readable back.</summary>
        public static bool SetBrightness(HidConnection c, int wired, int wireless)
        {
            Func<int, byte> b = p => (byte)(Math.Max(0, Math.Min(100, p)) * 255 / 100); // truncated, like CORE
            return Set(c, 2, 0x02, 1, b(wired)) & Set(c, 2, 0x02, 0, b(wireless));
        }

        public static bool SetBrightness(HidConnection c, int percent) { return SetBrightness(c, percent, percent); }

        public static bool SetButton(HidConnection c, int profile, int button, byte[] action)
        {
            var payload = new byte[10];
            payload[0] = (byte)profile; payload[1] = (byte)button;
            Array.Copy(action, 0, payload, 3, Math.Min(7, action.Length));
            return Set(c, 3, 0x00, payload);
        }

        /// <summary>Replaces macro <paramref name="id"/>: delete, header with the length, then 51-byte pages.</summary>
        public static bool SetMacro(HidConnection c, int id, byte[] events)
        {
            byte hi = (byte)(id >> 8), lo = (byte)id;
            Set(c, 4, 0x02, hi, lo); // delete; an empty slot answers A2, which is fine
            if (events == null || events.Length == 0) return true;
            if (!Set(c, 4, 0x01, hi, lo, 0, 0, (byte)(events.Length >> 8), (byte)events.Length)) return false;
            for (int off = 0; off < events.Length; off += 51)
            {
                var page = new byte[58];
                page[0] = hi; page[1] = lo;
                page[4] = (byte)(off >> 8); page[5] = (byte)off;
                page[6] = 51;
                Array.Copy(events, off, page, 7, Math.Min(51, events.Length - off));
                if (!SetRaw(c, 4, 0x03, 0x3A, page)) return false;
            }
            return true;
        }

        public static bool DeleteMacro(HidConnection c, int id)
        {
            Set(c, 4, 0x02, (byte)(id >> 8), (byte)id);
            return true;
        }

        // ---- Transport ---------------------------------------------------------------------

        static byte[] Frame(byte cls, byte cmd, int len, byte[] payload)
        {
            var b = new byte[65];
            b[3] = 0x02; b[4] = (byte)len; b[5] = cls; b[6] = cmd;
            Array.Copy(payload, 0, b, 7, Math.Min(payload.Length, 58));
            return b;
        }

        /// <summary>Getter with retries; returns the reply or null if the mouse never said A1.</summary>
        static byte[] Get(HidConnection c, byte cls, byte cmd, int len, params byte[] payload)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var r = c.Transact(Frame(cls, cmd, len, payload), 60 + attempt * 60);
                if (r == null) return null;
                if (r[1] == 0xA1 && r[6] == cmd) return r;
                if (r[1] == 0xA4) return null;
            }
            return null;
        }

        /// <summary>Setter, then poll the status until the mouse acknowledges (or give up).</summary>
        static bool Set(HidConnection c, byte cls, byte cmd, params byte[] payload)
        {
            return SetRaw(c, cls, cmd, payload.Length, payload);
        }

        static bool SetRaw(HidConnection c, byte cls, byte cmd, int len, byte[] payload)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var r = c.Transact(Frame(cls, cmd, len, payload), 80 + attempt * 60);
                if (r == null) return false;
                if (r[1] == 0xA1) return true;
                if (r[1] == 0xA4) return false;
                Thread.Sleep(40);
            }
            return false;
        }

        public override string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"profile\":").Append(Profile);
            sb.Append(",\"firmware\":").Append(Json.Str(Firmware));
            sb.Append(",\"dpi\":[").Append(string.Join(",", Dpi)).Append(']');
            sb.Append(",\"dpiColors\":[").Append(string.Join(",", DpiColors.Select(Hex))).Append(']');
            sb.Append(",\"activeStage\":").Append(ActiveStage);
            sb.Append(",\"pollingHz\":").Append(PollingHz);
            sb.Append(",\"debounceMs\":").Append(DebounceMs);
            sb.Append(",\"liftOff\":").Append(LiftOff);
            sb.Append(",\"sleepSeconds\":").Append(SleepSeconds);
            sb.Append(",\"effect\":").Append(Effect);
            sb.Append(",\"effectSpeed\":").Append(SpeedFromDevice(Effect, EffectSpeed)); // the UI speaks CORE's 1-100
            sb.Append(",\"effectColor\":").Append(Hex(EffectColor));
            sb.Append(",\"effectColors\":[").Append(string.Join(",", EffectColors.Select(Hex))).Append(']');
            sb.Append(",\"maxStages\":").Append(MaxStages);
            sb.Append(",\"minDpi\":").Append(MinDpi);
            sb.Append(",\"maxDpi\":").Append(MaxDpi);
            sb.Append(",\"buttons\":{").Append(string.Join(",", Buttons.Select(kv => Json.Str(kv.Key.ToString()) + ":" + Bytes(kv.Value)))).Append('}');
            sb.Append(",\"macros\":{").Append(string.Join(",", Macros.Select(kv => Json.Str(kv.Key.ToString()) + ":" + Bytes(kv.Value)))).Append('}');
            sb.Append(",\"maxMacros\":").Append(MaxMacros);
            sb.Append(",\"motionSync\":").Append(Json.Bool(MotionSync));
            sb.Append(",\"features\":{\"readback\":true,\"buttons\":true,\"macros\":true,\"motionSync\":").Append(Json.Bool(Pro));
            sb.Append(",\"profiles\":3,\"sleepMinutes\":[],\"sleepRange\":[1,15],\"sleepNever\":false");
            sb.Append(",\"pollingRates\":[").Append(string.Join(",", PollingRates)).Append(']');
            sb.Append(",\"liftOffLabels\":[\"1 mm\",\"2 mm\"]");
            sb.Append(",\"separateBrightness\":").Append(Json.Bool(SeparateBrightness)).Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        static string Bytes(byte[] b) { return "[" + string.Join(",", b.Select(x => x.ToString())) + "]"; }

        static string Hex(int rgb) { return Json.Str("#" + (rgb & 0xFFFFFF).ToString("x6")); }

        public static int ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return Convert.ToInt32(s.TrimStart('#'), 16);
        }
    }
}
