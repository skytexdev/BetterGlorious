using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BetterGlorious.Macros
{
    /// <summary>
    /// Switches the mouse's onboard profile to match the app in the foreground
    /// ("Minecraft → profile 2, desktop → profile 1"). Rules per device are stored by the UI as JSON:
    /// { enabled, fallback (profile or 0 = leave as is), rules: [{ exe, title, profile, label }] }.
    /// A rule matches when its exe name equals the foreground process (case-insensitive) and, if it
    /// has a title, the window title contains it. Must be created on the UI thread.
    /// </summary>
    public sealed class ProfileSwitcher : IDisposable
    {
        readonly Func<string, string> loadRules;            // device key -> rules JSON
        readonly Func<IEnumerable<string>> deviceKeys;      // devices that support profiles
        readonly Action<string, int> setProfile;            // switch device to profile (1-3)
        readonly WinEventDelegate proc;
        readonly Timer debounce = new Timer { Interval = 400 };
        readonly Dictionary<string, int> applied = new Dictionary<string, int>();
        IntPtr hook;

        /// <summary>The foreground app as last seen, for the UI ("Now: javaw.exe — Minecraft 1.21").</summary>
        public string CurrentExe { get; private set; }
        public string CurrentTitle { get; private set; }
        public event Action Changed;

        public ProfileSwitcher(Func<string, string> loadRules, Func<IEnumerable<string>> deviceKeys, Action<string, int> setProfile)
        {
            this.loadRules = loadRules;
            this.deviceKeys = deviceKeys;
            this.setProfile = setProfile;
            proc = OnWinEvent;
            debounce.Tick += delegate { debounce.Stop(); Evaluate(); };
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            debounce.Stop();
            debounce.Start();
        }

        /// <summary>Re-check now (rules changed, device connected).</summary>
        public void Refresh()
        {
            applied.Clear();
            Evaluate();
        }

        void Evaluate()
        {
            string exe, title;
            Foreground(out exe, out title);
            // Our own panel taking focus shouldn't count as "back on the desktop".
            if (string.Equals(exe, Process.GetCurrentProcess().ProcessName + ".exe", StringComparison.OrdinalIgnoreCase)) return;
            CurrentExe = exe;
            CurrentTitle = title;

            foreach (string key in deviceKeys().ToList())
            {
                var rules = Parse(loadRules(key));
                if (rules == null || !rules.Enabled) continue;
                int target = rules.Fallback;
                foreach (var r in rules.Rules)
                {
                    if (!string.Equals(r.Exe, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(r.Title) && (title ?? "").IndexOf(r.Title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    target = r.Profile;
                    break;
                }
                int last;
                if (target < 1 || target > 3 || (applied.TryGetValue(key, out last) && last == target)) continue;
                applied[key] = target;
                Log.Info("auto profile: " + key + " -> " + target + " (" + exe + ")");
                setProfile(key, target);
            }
            var h = Changed;
            if (h != null) h();
        }

        sealed class RuleSet
        {
            public bool Enabled;
            public int Fallback;
            public List<Rule> Rules = new List<Rule>();
        }

        sealed class Rule { public string Exe, Title; public int Profile; }

        static RuleSet Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var o = Json.Parse(json) as Dictionary<string, object>;
                if (o == null) return null;
                var r = new RuleSet
                {
                    Enabled = o.ContainsKey("enabled") && Convert.ToBoolean(o["enabled"]),
                    Fallback = o.ContainsKey("fallback") && o["fallback"] != null ? Convert.ToInt32(o["fallback"]) : 0,
                };
                var list = o.ContainsKey("rules") ? o["rules"] as List<object> : null;
                if (list != null)
                    foreach (Dictionary<string, object> x in list.OfType<Dictionary<string, object>>())
                        r.Rules.Add(new Rule
                        {
                            Exe = x.ContainsKey("exe") ? x["exe"] as string : null,
                            Title = x.ContainsKey("title") ? x["title"] as string : null,
                            Profile = x.ContainsKey("profile") ? Convert.ToInt32(x["profile"]) : 0,
                        });
                return r;
            }
            catch (Exception ex) { Log.Error("profile rules", ex); return null; }
        }

        static void Foreground(out string exe, out string title)
        {
            exe = ""; title = "";
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            title = sb.ToString();
            exe = ExeOf(hwnd);
        }

        static string ExeOf(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            try { using (var p = Process.GetProcessById((int)pid)) return p.ProcessName + ".exe"; }
            catch { return ""; }
        }

        /// <summary>Visible top-level windows with a title, for the "pick a running app" list.</summary>
        public static List<KeyValuePair<string, string>> RunningApps()
        {
            var result = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string self = Process.GetCurrentProcess().ProcessName + ".exe";
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                string exe = ExeOf(h);
                if (exe == "" || exe.Equals(self, StringComparison.OrdinalIgnoreCase) || exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && sb.ToString() == "Program Manager") return true;
                if (seen.Add(exe + "|" + sb)) result.Add(new KeyValuePair<string, string>(exe, sb.ToString()));
                return true;
            }, IntPtr.Zero);
            return result.OrderBy(kv => kv.Key).ToList();
        }

        public void Dispose()
        {
            debounce.Dispose();
            if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
        }

        const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    }
}
