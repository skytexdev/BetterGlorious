using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BetterGlorious.Devices;

namespace BetterGlorious.UI
{
    /// <summary>Owns the tray icons: one per battery device, or a single one for the lowest battery.</summary>
    sealed class TrayController : IDisposable
    {
        readonly DeviceManager manager;
        readonly Settings settings;
        readonly Action openPanel;
        readonly Dictionary<string, NotifyIcon> icons = new Dictionary<string, NotifyIcon>();
        readonly HashSet<string> warned = new HashSet<string>();
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem startupItem;
        const string IdleKey = "__idle";

        public TrayController(DeviceManager manager, Settings settings, Action openPanel, Action exit)
        {
            this.manager = manager;
            this.settings = settings;
            this.openPanel = openPanel;

            menu = new ContextMenuStrip();
            menu.Items.Add(T.Get("open"), null, delegate { openPanel(); });
            menu.Items.Add(T.Get("refresh"), null, delegate { manager.Rescan(); });
            startupItem = new ToolStripMenuItem(T.Get("startup"), null, delegate
            {
                Startup.Enabled = !Startup.Enabled;
                startupItem.Checked = Startup.Enabled;
            });
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(T.Get("exit"), null, delegate { exit(); });
            menu.Opening += delegate { startupItem.Checked = Startup.Enabled; };
        }

        /// <summary>Rebuilds icons from the current device list. Call on the UI thread.</summary>
        public void Update()
        {
            var shown = manager.Devices.Where(d => d.HasBattery && !settings.HiddenDevices.Contains(d.Key)).ToList();
            var wanted = new Dictionary<string, Tuple<Func<System.Drawing.Icon>, string>>();

            if (shown.Count == 0)
            {
                wanted[IdleKey] = Tuple.Create<Func<System.Drawing.Icon>, string>(
                    () => IconRenderer.AppIcon(Math.Max(16, SystemInformation.SmallIconSize.Width)),
                    AppInfo.Name + ": " + T.Get("no_devices").Split('\n')[0]);
            }
            else if (settings.Tray == TrayMode.LowestOnly)
            {
                var lowest = shown.OrderBy(d => d.LastPercent < 0 ? 999 : d.LastPercent).First();
                string tip = string.Join("\n", shown.Select(Describe));
                wanted["__lowest"] = Tuple.Create<Func<System.Drawing.Icon>, string>(() => Render(lowest), tip);
            }
            else
            {
                foreach (var d in shown)
                {
                    var dev = d;
                    wanted[d.Key] = Tuple.Create<Func<System.Drawing.Icon>, string>(() => Render(dev), Describe(dev));
                }
            }

            foreach (var key in icons.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                icons[key].Visible = false;
                icons[key].Dispose();
                icons.Remove(key);
            }
            foreach (var kv in wanted)
            {
                NotifyIcon ni;
                if (!icons.TryGetValue(kv.Key, out ni))
                {
                    ni = new NotifyIcon { ContextMenuStrip = menu };
                    ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) openPanel(); };
                    icons[kv.Key] = ni;
                }
                var old = ni.Icon;
                ni.Icon = kv.Value.Item1();
                ni.Text = Truncate(kv.Value.Item2, 63); // .NET Framework rejects longer tooltips
                ni.Visible = true;
                if (old != null) old.Dispose();
            }

            if (settings.Notifications) CheckLowBattery(shown);
        }

        System.Drawing.Icon Render(GloriousDevice d)
        {
            var r = d.Reading;
            bool stale = r == null || r.State != ReadState.Ok;
            var charge = r == null ? ChargeState.Unknown : r.Charge;
            var color = Theme.ForLevel(d.LastPercent, charge, stale, settings.LowBatteryPercent);
            return IconRenderer.Draw(d.LastPercent, color, d.Kind);
        }

        public static string Describe(GloriousDevice d)
        {
            var r = d.Reading;
            string s = d.Name + ": ";
            if (d.LastPercent >= 0) s += T.Percent(d.LastPercent);
            else s += T.Get("waiting");
            if (r != null)
            {
                if (r.Charge == ChargeState.Charging) s += " (" + T.Get("charging") + ")";
                else if (r.Charge == ChargeState.Full) s += " (" + T.Get("full") + ")";
                if (r.State == ReadState.Asleep) s += " (" + T.Get("asleep") + ")";
            }
            return s;
        }

        void CheckLowBattery(List<GloriousDevice> devices)
        {
            foreach (var d in devices)
            {
                var r = d.Reading;
                if (r == null || r.State != ReadState.Ok || !r.HasPercent) continue;
                bool low = r.Percent <= settings.LowBatteryPercent && r.Charge != ChargeState.Charging;
                if (low && !warned.Contains(d.Key))
                {
                    warned.Add(d.Key);
                    var ni = icons.Values.FirstOrDefault();
                    if (ni != null)
                        ni.ShowBalloonTip(6000, T.Get("low_title"), string.Format(T.Get("low_body"), d.Name, r.Percent), ToolTipIcon.Warning);
                }
                else if (!low && (r.Charge == ChargeState.Charging || r.Percent > settings.LowBatteryPercent + 5))
                    warned.Remove(d.Key);
            }
        }

        /// <summary>One-off balloon so closing the window doesn't look like quitting.</summary>
        public void Notify(string title, string body)
        {
            var ni = icons.Values.FirstOrDefault();
            if (ni != null) ni.ShowBalloonTip(5000, title, body, ToolTipIcon.Info);
        }

        static string Truncate(string s, int max) { return s.Length <= max ? s : s.Substring(0, max - 1) + "…"; }

        public void Dispose()
        {
            foreach (var ni in icons.Values) { ni.Visible = false; ni.Dispose(); }
            icons.Clear();
            menu.Dispose();
        }
    }
}
