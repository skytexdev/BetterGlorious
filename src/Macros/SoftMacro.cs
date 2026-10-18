using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGlorious.Macros
{
    public enum SoftEvent { Key, Mouse, Wheel }

    public sealed class SoftStep
    {
        public SoftEvent Kind;
        public int Delay;   // ms to wait before this step
        public int Code;    // Key: HID usage; Mouse: buttons held after the step (L1 R2 M4 Back8 Fwd16); Wheel: notches (+up)
        public bool IsUp;   // Key: release
    }

    /// <summary>
    /// A macro played by the PC. Stored by the UI as JSON:
    /// { id, name, trigger (0-11 = F13-F24, or null), mode ("once"|"hold"|"toggle"|"repeat"),
    ///   repeat, minHold, gap, events: [{ delay, event: { type: "key"|"mod"|"mouse"|"wheel", ... } }] }
    /// Events use the same shape as onboard macros (see web/src/hid.ts).
    /// </summary>
    public sealed class SoftMacro
    {
        public int Id;
        public string Name = "";
        public int? Trigger;
        public string Mode = "once";
        public int RepeatCount = 1;
        public int MinHoldMs = 30;
        public int GapMs;      // wait after the last step, before the next repetition
        public int JitterMs;   // each delay varies randomly by up to ± this much
        public List<SoftStep> Events = new List<SoftStep>();

        // Modifier bits as recorded (L/R Ctrl, Shift, Alt, Win) -> HID usages E0..E7.
        static readonly Dictionary<int, int> ModUsage = new Dictionary<int, int>
        {
            { 0x01, 0xE0 }, { 0x02, 0xE1 }, { 0x04, 0xE2 }, { 0x08, 0xE3 },
            { 0x10, 0xE4 }, { 0x20, 0xE5 }, { 0x40, 0xE6 }, { 0x80, 0xE7 },
        };

        public static List<SoftMacro> ParseAll(string json)
        {
            var list = new List<SoftMacro>();
            var arr = BetterGlorious.Json.Parse(json) as List<object>;
            if (arr == null) return list;
            foreach (Dictionary<string, object> o in arr.OfType<Dictionary<string, object>>())
            {
                var m = new SoftMacro
                {
                    Id = Int(o, "id"),
                    Name = o.ContainsKey("name") ? o["name"] as string ?? "" : "",
                    Trigger = o.ContainsKey("trigger") && o["trigger"] != null ? (int?)Int(o, "trigger") : null,
                    Mode = o.ContainsKey("mode") ? o["mode"] as string ?? "once" : "once",
                    RepeatCount = o.ContainsKey("repeat") ? Math.Max(1, Int(o, "repeat")) : 1,
                    MinHoldMs = o.ContainsKey("minHold") ? Math.Max(0, Int(o, "minHold")) : 30,
                    GapMs = o.ContainsKey("gap") ? Math.Max(0, Int(o, "gap")) : 0,
                    JitterMs = o.ContainsKey("jitter") ? Math.Max(0, Int(o, "jitter")) : 0,
                };
                var events = o.ContainsKey("events") ? o["events"] as List<object> : null;
                if (events != null)
                    foreach (Dictionary<string, object> t in events.OfType<Dictionary<string, object>>())
                    {
                        var e = t.ContainsKey("event") ? t["event"] as Dictionary<string, object> : null;
                        if (e == null) continue;
                        string type = e["type"] as string;
                        var step = new SoftStep { Delay = Math.Max(0, Int(t, "delay")) };
                        bool down = e.ContainsKey("down") && Convert.ToBoolean(e["down"]);
                        if (type == "key") { step.Kind = SoftEvent.Key; step.Code = Int(e, "usage"); step.IsUp = !down; }
                        else if (type == "mod")
                        {
                            int usage;
                            if (!ModUsage.TryGetValue(Int(e, "bit"), out usage)) continue;
                            step.Kind = SoftEvent.Key; step.Code = usage; step.IsUp = !down;
                        }
                        else if (type == "mouse") { step.Kind = SoftEvent.Mouse; step.Code = Int(e, "mask"); }
                        else if (type == "wheel") { step.Kind = SoftEvent.Wheel; step.Code = Int(e, "notches"); }
                        else continue;
                        m.Events.Add(step);
                    }
                list.Add(m);
            }
            return list;
        }

        static int Int(Dictionary<string, object> o, string k)
        {
            object v;
            return o.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : 0;
        }
    }

    /// <summary>HID keyboard usage -> PS/2 set 1 scan code, as SendInput expects.</summary>
    static class Scancodes
    {
        static readonly Dictionary<int, int> Map = new Dictionary<int, int>();
        static readonly HashSet<int> Extended = new HashSet<int>();

        static Scancodes()
        {
            int[] letters = { 0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C };
            for (int i = 0; i < 26; i++) Map[0x04 + i] = letters[i];          // A..Z
            for (int i = 0; i < 9; i++) Map[0x1E + i] = 0x02 + i;             // 1..9
            Map[0x27] = 0x0B;                                                  // 0
            Add(0x28, 0x1C); Add(0x29, 0x01); Add(0x2A, 0x0E); Add(0x2B, 0x0F); Add(0x2C, 0x39);
            Add(0x2D, 0x0C); Add(0x2E, 0x0D); Add(0x2F, 0x1A); Add(0x30, 0x1B); Add(0x31, 0x2B);
            Add(0x33, 0x27); Add(0x34, 0x28); Add(0x35, 0x29); Add(0x36, 0x33); Add(0x37, 0x34);
            Add(0x38, 0x35); Add(0x39, 0x3A); Add(0x64, 0x56);
            for (int i = 0; i < 10; i++) Map[0x3A + i] = 0x3B + i;            // F1..F10
            Add(0x44, 0x57); Add(0x45, 0x58);                                  // F11, F12
            for (int i = 0; i < 12; i++) Map[0x68 + i] = new[] { 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x76 }[i]; // F13..F24
            Add(0x46, 0x37, true); Add(0x47, 0x46); Add(0x48, 0x45);
            Add(0x49, 0x52, true); Add(0x4A, 0x47, true); Add(0x4B, 0x49, true); Add(0x4C, 0x53, true);
            Add(0x4D, 0x4F, true); Add(0x4E, 0x51, true); Add(0x4F, 0x4D, true); Add(0x50, 0x4B, true);
            Add(0x51, 0x50, true); Add(0x52, 0x48, true); Add(0x65, 0x5D, true);
            Add(0x53, 0x45); Add(0x54, 0x35, true); Add(0x55, 0x37); Add(0x56, 0x4A); Add(0x57, 0x4E); Add(0x58, 0x1C, true);
            int[] pad = { 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49 };
            for (int i = 0; i < 9; i++) Map[0x59 + i] = pad[i];               // Num 1..9
            Add(0x62, 0x52); Add(0x63, 0x53);                                  // Num 0, Num .
            Add(0xE0, 0x1D); Add(0xE1, 0x2A); Add(0xE2, 0x38); Add(0xE3, 0x5B, true);
            Add(0xE4, 0x1D, true); Add(0xE5, 0x36); Add(0xE6, 0x38, true); Add(0xE7, 0x5C, true);
        }

        static void Add(int usage, int sc, bool ext = false)
        {
            Map[usage] = sc;
            if (ext) Extended.Add(usage);
        }

        public static bool TryGet(int usage, out int scan, out bool extended)
        {
            extended = Extended.Contains(usage);
            return Map.TryGetValue(usage, out scan);
        }
    }
}
