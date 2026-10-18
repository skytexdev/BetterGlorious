using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using BetterGlorious.Devices;
using BetterGlorious.Hid;

namespace BetterGlorious.UI
{
    /// <summary>
    /// The panel. The UI itself is HTML/CSS/JS in the ui\ folder, rendered by WebView2;
    /// this form pushes device state to it as JSON and executes the commands it sends back.
    /// Closing hides it to the tray.
    /// </summary>
    sealed class WebPanel : Form
    {
        const string Host = "app.betterglorious";
        static readonly Color Background = Color.FromArgb(11, 11, 13);

        readonly DeviceManager manager;
        readonly Settings settings;
        readonly Macros.MacroEngine macros;
        readonly Macros.ProfileSwitcher profiles;
        readonly Action settingsChanged;
        readonly WebView2 web = new WebView2();
        bool ready;
        public bool AllowClose;
        /// <summary>Raised when the window goes to the tray (close or minimize).</summary>
        public event Action HiddenToTray;

        public WebPanel(DeviceManager manager, Settings settings, Macros.MacroEngine macros, Macros.ProfileSwitcher profiles, Action settingsChanged)
        {
            this.manager = manager;
            this.settings = settings;
            this.macros = macros;
            this.profiles = profiles;
            this.settingsChanged = settingsChanged;

            Text = AppInfo.Name;
            Icon = IconRenderer.AppIcon(32);
            BackColor = Background;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1080, 700);
            MinimumSize = new Size(820, 560);

            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Background;
            Controls.Add(web);
            InitWeb();
        }

        async void InitWeb()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppInfo.DataDir, "WebView2"));
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                var s = core.Settings;
                s.AreDevToolsEnabled = Debugger.IsAttached || Environment.GetCommandLineArgs().Contains("--devtools");
                s.AreDefaultContextMenusEnabled = false;
                s.IsStatusBarEnabled = false;
                s.IsZoomControlEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = false;

                core.SetVirtualHostNameToFolderMapping(Host, UiFolder, CoreWebView2HostResourceAccessKind.Deny);
                core.WebMessageReceived += OnMessage;
                core.NewWindowRequested += (o, e) => { e.Handled = true; OpenUrl(e.Uri); };
                // --page=<hash> opens a page directly, e.g. --page=device=ModelOWireless/performance
                string page = Environment.GetCommandLineArgs().Where(a => a.StartsWith("--page=")).Select(a => a.Substring(7)).FirstOrDefault();
                core.Navigate("https://" + Host + "/index.html?lang=" + Uri.EscapeDataString(T.Code) + (page != null ? "#" + Uri.EscapeDataString(page).Replace("%3D", "=").Replace("%2F", "/") : ""));
            }
            catch (Exception ex)
            {
                Log.Error("webview init", ex);
                MessageBox.Show(this, "WebView2 could not start: " + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string UiFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui"); }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = Json.Parse(e.WebMessageAsJson) as Dictionary<string, object>; }
            catch { return; }
            if (msg == null) return;
            string cmd = msg.ContainsKey("cmd") ? msg["cmd"] as string : null;
            switch (cmd)
            {
                case "ready": ready = true; PushState(); break;
                case "refresh": manager.Rescan(); break;
                case "setting": ApplySetting(msg["key"] as string, msg["value"]); break;
                case "tray":
                    {
                        string key = msg["key"] as string;
                        if (Convert.ToBoolean(msg["value"])) settings.HiddenDevices.Remove(key);
                        else settings.HiddenDevices.Add(key);
                        settings.Save(); settingsChanged(); PushState();
                        break;
                    }
                case "color":
                    {
                        string key = msg["key"] as string, value = msg["value"] as string;
                        if (key != null && value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9]{1,24}$")) settings.DeviceColors[key] = value;
                        settings.Save(); PushState();
                        break;
                    }
                case "cfg": Configure(msg["key"] as string, msg["field"] as string, msg["value"], msg.ContainsKey("id") ? Convert.ToInt32(msg["id"]) : 0); break;
                case "undo":
                case "restore":
                    manager.Revert(msg["key"] as string, cmd == "restore", ok =>
                    {
                        BeginInvoke((Action)(() => Send("toast", Json.Str(T.Get(ok ? (cmd == "restore" ? "restored" : "undone") : "save_failed")))));
                    });
                    break;
                case "launch":
                    {
                        // Bind a button to "launch": remember the target here, tell the mouse to report the press.
                        string key = msg["key"] as string, target = msg["target"] as string;
                        int button = Convert.ToInt32(msg["button"]);
                        if (string.IsNullOrEmpty(target)) settings.Launch.Remove(key + "/" + button);
                        else settings.Launch[key + "/" + button] = target;
                        settings.Save();
                        if (!string.IsNullOrEmpty(target))
                            Configure(key, "button", new Dictionary<string, object> { { "id", (double)button }, { "action", new List<object> { 5.0, 2.0, (double)button, 15.0 } } });
                        PushState();
                        break;
                    }
                case "pickProgram":
                    {
                        using (var dlg = new OpenFileDialog { Filter = "Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", Title = AppInfo.Name })
                            if (dlg.ShowDialog(this) == DialogResult.OK)
                                Send("picked", "{\"button\":" + Convert.ToInt32(msg["button"]) + ",\"path\":" + Json.Str(dlg.FileName) + "}");
                        break;
                    }
                case "softMacros": macros.Save(msg["json"] as string ?? "[]"); PushState(); break;
                case "autoProfile":
                    {
                        string key = msg["key"] as string, json = msg["json"] as string;
                        if (key != null && json != null) { settings.AutoProfiles[key] = json; settings.Save(); profiles.Refresh(); }
                        PushState();
                        break;
                    }
                case "runningApps":
                    // Thumbnails take a moment: capture off the UI thread, then reply.
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        string json = Macros.AppCapture.RunningAppsJson();
                        BeginInvoke((Action)(() => Send("apps", json)));
                    });
                    break;
                case "testMacro": macros.Test(Convert.ToInt32(msg["id"])); break;
                case "copyDiagnostics":
                    try
                    {
                        Clipboard.SetText(DeviceManager.Diagnostics() + "\nDevices:\n" + string.Join("\n",
                            manager.Devices.Select(d => "  " + TrayController.Describe(d) + " [" + d.Family + ", " + string.Join("+", d.Links.Keys) + "]")));
                        Send("toast", Json.Str(T.Get("diag_copied")));
                    }
                    catch (Exception ex) { Send("toast", Json.Str(ex.Message)); }
                    break;
                case "openLogs": Process.Start("explorer.exe", "\"" + AppInfo.DataDir + "\""); break;
                case "openUrl": OpenUrl(msg["url"] as string); break;
            }
        }

        void ApplySetting(string key, object value)
        {
            switch (key)
            {
                case "startup": Startup.Enabled = Convert.ToBoolean(value); break;
                case "notifications": settings.Notifications = Convert.ToBoolean(value); break;
                case "pollSeconds": settings.PollSeconds = Math.Max(15, Convert.ToInt32(value)); break;
                case "lowBattery": settings.LowBatteryPercent = Math.Max(5, Math.Min(50, Convert.ToInt32(value))); break;
                case "tray": settings.Tray = (value as string) == "lowest" ? TrayMode.LowestOnly : TrayMode.PerDevice; break;
                case "language":
                    settings.Language = value as string ?? "auto";
                    T.Init(settings.Language);
                    settings.Save();
                    settingsChanged();
                    // Reload the page in the new language, staying on Settings.
                    if (web.CoreWebView2 != null)
                        web.CoreWebView2.Navigate("https://" + Host + "/index.html?lang=" + Uri.EscapeDataString(T.Code) + "#settings");
                    return;
                case "minimizeToTray": settings.MinimizeToTray = Convert.ToBoolean(value); break;
                default: return;
            }
            settings.Save();
            settingsChanged();
            PushState();
        }

        /// <summary>
        /// One settings change from the page. Queued per device (see DeviceManager); when it has been
        /// written and read back, the page gets "cfgDone" with the same id so it can drop its preview.
        /// </summary>
        void Configure(string key, string field, object value, int requestId = 0)
        {
            Func<HidConnection, Link, DeviceConfig, bool> write = null;
            int[] defaults = { 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF };
            switch (field)
            {
                case "dpi":
                    {
                        var stages = ((List<object>)value).Select(Convert.ToInt32).ToList();
                        write = (c, l, cfg) =>
                        {
                            var sw = cfg as SinowealthConfig;
                            if (sw != null)
                            {
                                var colors = sw.DpiColors.ToList();
                                while (colors.Count < stages.Count) colors.Add(defaults[colors.Count % defaults.Length]);
                                bool ok = SinowealthConfig.SetDpi(c, sw.Profile, stages);
                                if (ok && stages.Count != sw.Dpi.Count) ok = SinowealthConfig.SetDpiColors(c, sw.Profile, colors.Take(stages.Count).ToList());
                                if (ok && sw.ActiveStage >= stages.Count) ok = SinowealthConfig.SetActiveStage(c, sw.Profile, stages.Count - 1);
                                return ok;
                            }
                            var px = (PixartConfig)cfg;
                            px.Dpi = stages;
                            while (px.DpiColors.Count < stages.Count) px.DpiColors.Add(defaults[px.DpiColors.Count % defaults.Length]);
                            if (px.ActiveStage >= stages.Count) px.ActiveStage = stages.Count - 1;
                            return PixartConfig.WritePerformance(c, l, px);
                        };
                        break;
                    }
                case "dpiColor":
                    {
                        var v = (Dictionary<string, object>)value;
                        int index = Convert.ToInt32(v["index"]);
                        int color = SinowealthConfig.ParseHex(v["color"] as string);
                        write = (c, l, cfg) =>
                        {
                            var sw = cfg as SinowealthConfig;
                            var colors = (sw != null ? sw.DpiColors : ((PixartConfig)cfg).DpiColors).ToList();
                            if (index < 0 || index >= colors.Count) return false;
                            colors[index] = color;
                            if (sw != null) return SinowealthConfig.SetDpiColors(c, sw.Profile, colors);
                            ((PixartConfig)cfg).DpiColors = colors;
                            return PixartConfig.WritePerformance(c, l, (PixartConfig)cfg);
                        };
                        break;
                    }
                case "activeStage":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => SinowealthConfig.SetActiveStage(c, sw.Profile, v), (c, l, px) => { px.ActiveStage = v; return PixartConfig.WritePerformance(c, l, px); });
                        break;
                    }
                case "polling":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => sw.SetPollingFor(c, v), (c, l, px) => { px.PollingHz = v; return PixartConfig.WritePerformance(c, l, px); },
                            (c, l, kb) => { kb.PollingHz = v; return Gmmk3HeConfig.WritePolling(c, l, kb); });
                        break;
                    }
                case "debounce":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => SinowealthConfig.SetDebounce(c, sw.Profile, v), (c, l, px) => { px.DebounceMs = v; return PixartConfig.WritePerformance(c, l, px); },
                            (c, l, kb) => { kb.DebounceMs = v; return Gmmk3HeConfig.WriteDebounce(c, l, kb); });
                        break;
                    }
                case "liftOff":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => sw.SetLiftOffFor(c, v), (c, l, px) => { px.LiftOff = v; return PixartConfig.WritePerformance(c, l, px); });
                        break;
                    }
                case "motionSync":
                    {
                        bool v = Convert.ToBoolean(value);
                        write = Either((c, sw) => sw.SetMotionSync(c, v), (c, l, px) => { px.MotionSync = v; return PixartConfig.WritePerformance(c, l, px); });
                        break;
                    }
                case "sleep":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => SinowealthConfig.SetSleep(c, v), (c, l, px) => { px.SleepMinutes = v <= 0 ? 0 : Math.Max(1, v / 60); return PixartConfig.WriteStandby(c, l, px); },
                            (c, l, kb) => { kb.SleepMinutes = v <= 0 ? 0 : Math.Max(1, v / 60); return Gmmk3HeConfig.WriteSleep(c, l, kb); });
                        break;
                    }
                case "profile":
                    {
                        int v = Convert.ToInt32(value);
                        write = Either((c, sw) => SinowealthConfig.SetProfile(c, v), (c, l, px) => { px.Profile = v; return PixartConfig.WriteProfile(c, l, v); },
                            (c, l, kb) => { kb.Profile = v; return Gmmk3HeConfig.WriteProfile(c, l, kb); });
                        break;
                    }
                case "brightness":
                    {
                        int v = Convert.ToInt32(value);
                        settings.Brightness[key] = v; settings.Save();
                        int wl;
                        int wireless = settings.Brightness.TryGetValue(key + "#wireless", out wl) ? wl : v;
                        write = Either((c, sw) => SinowealthConfig.SetBrightness(c, v, wireless), (c, l, px) => { px.Brightness = v; return PixartConfig.WriteLighting(c, l, px); },
                            (c, l, kb) => { kb.Brightness = v; return Gmmk3HeConfig.WriteLighting(c, l, kb); });
                        break;
                    }
                case "brightnessWireless":
                    {
                        // -1 = follow the cable brightness (CORE's "separate" option off)
                        int v = Convert.ToInt32(value);
                        int wired;
                        if (!settings.Brightness.TryGetValue(key, out wired)) wired = 100;
                        if (v < 0) settings.Brightness.Remove(key + "#wireless"); else settings.Brightness[key + "#wireless"] = v;
                        settings.Save();
                        int wireless = v < 0 ? wired : v;
                        write = Either((c, sw) => SinowealthConfig.SetBrightness(c, wired, wireless), null);
                        break;
                    }
                case "effect":
                    {
                        var v = (Dictionary<string, object>)value;
                        int effect = Convert.ToInt32(v["effect"]), speed = Convert.ToInt32(v["speed"]);
                        int color = SinowealthConfig.ParseHex(v["color"] as string);
                        var colors = v.ContainsKey("colors") && v["colors"] is List<object>
                            ? ((List<object>)v["colors"]).Select(x => SinowealthConfig.ParseHex(x as string)).ToList()
                            : new List<int> { color };
                        bool custom = v.ContainsKey("custom") && Convert.ToBoolean(v["custom"]);
                        write = Either((c, sw) => SinowealthConfig.SetEffect(c, sw.Profile, effect, SinowealthConfig.SpeedToDevice(effect, speed), colors), (c, l, px) =>
                        {
                            px.Effect = effect;
                            px.EffectColor = color;
                            px.Speed = Math.Max(1, Math.Min(100, speed)); // CORE's 1-100 slider value
                            return PixartConfig.WriteLighting(c, l, px);
                        }, (c, l, kb) =>
                        {
                            kb.Effect = effect;
                            kb.EffectColor = color;
                            kb.Speed = Math.Max(1, Math.Min(100, speed));
                            kb.Custom = custom;
                            return Gmmk3HeConfig.WriteLighting(c, l, kb);
                        });
                        break;
                    }
                case "button":
                    {
                        var v = (Dictionary<string, object>)value;
                        int id = Convert.ToInt32(v["id"]);
                        byte[] action = ((List<object>)v["action"]).Select(Convert.ToByte).ToArray();
                        write = Either((c, sw) => SinowealthConfig.SetButton(c, sw.Profile, id, action), null);
                        break;
                    }
                case "macro":
                    {
                        var v = (Dictionary<string, object>)value;
                        int id = Convert.ToInt32(v["id"]);
                        byte[] events = ((List<object>)v["events"]).Select(Convert.ToByte).ToArray();
                        write = Either((c, sw) => SinowealthConfig.SetMacro(c, id, events), null);
                        break;
                    }
                case "deleteMacro": { int v = Convert.ToInt32(value); write = Either((c, sw) => SinowealthConfig.DeleteMacro(c, v), null); break; }
            }
            if (write == null || key == null) return;
            string coalesce = field;
            var dict = value as Dictionary<string, object>;
            if (field == "button" && dict != null) coalesce = "button:" + dict["id"];
            else if (field == "dpiColor" && dict != null) coalesce = "dpiColor:" + dict["index"];
            else if (field == "macro" || field == "deleteMacro") coalesce = null;
            manager.Configure(key, write, ok => OnUi(() =>
            {
                if (!ok) Send("toast", Json.Str(T.Get("save_failed")));
                PushState(); // fresh state first, then the ack, so the page never falls back to an old value
                if (requestId != 0) Send("cfgDone", "{\"id\":" + requestId + ",\"ok\":" + Json.Bool(ok) + "}");
            }), field == "button" || field == "macro" || field == "deleteMacro", coalesce);
        }

        void OnUi(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }
            catch (InvalidOperationException) { } // window closing
        }

        /// <summary>Routes one change to the right family; null for a family means "not supported".</summary>
        static Func<HidConnection, Link, DeviceConfig, bool> Either(Func<HidConnection, SinowealthConfig, bool> sino, Func<HidConnection, Link, PixartConfig, bool> pixart, Func<HidConnection, Link, Gmmk3HeConfig, bool> he = null)
        {
            return (c, l, cfg) =>
            {
                var sw = cfg as SinowealthConfig;
                if (sw != null) return sino != null && sino(c, sw);
                var px = cfg as PixartConfig;
                if (px != null) return pixart != null && pixart(c, l, px);
                var kb = cfg as Gmmk3HeConfig;
                return kb != null && he != null && he(c, l, kb);
            };
        }

        static void OpenUrl(string url)
        {
            if (url != null && (url.StartsWith("https://") || url.StartsWith("http://")))
                Process.Start(url);
        }

        /// <summary>Sends the full state to the page. Call on the UI thread.</summary>
        public void PushState()
        {
            if (!ready || web.CoreWebView2 == null) return;
            Send("state", BuildState());
        }

        void Send(string type, string payloadJson)
        {
            if (web.CoreWebView2 == null) return;
            web.CoreWebView2.PostWebMessageAsJson("{\"type\":" + Json.Str(type) + ",\"data\":" + payloadJson + "}");
        }

        string BuildState()
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":").Append(Json.Str(AppInfo.Version));
            sb.Append(",\"update\":").Append(UpdateChecker.Available ? "{\"version\":" + Json.Str(UpdateChecker.LatestVersion) + ",\"url\":" + Json.Str(UpdateChecker.ReleaseUrl) + "}" : "null");
            sb.Append(",\"settings\":{");
            sb.Append("\"startup\":").Append(Json.Bool(Startup.Enabled));
            sb.Append(",\"notifications\":").Append(Json.Bool(settings.Notifications));
            sb.Append(",\"pollSeconds\":").Append(settings.PollSeconds);
            sb.Append(",\"lowBattery\":").Append(settings.LowBatteryPercent);
            sb.Append(",\"tray\":").Append(Json.Str(settings.Tray == TrayMode.LowestOnly ? "lowest" : "per_device"));
            sb.Append(",\"language\":").Append(Json.Str(settings.Language));
            sb.Append(",\"minimizeToTray\":").Append(Json.Bool(settings.MinimizeToTray));
            sb.Append("},\"softMacros\":").Append(string.IsNullOrEmpty(macros.Json) ? "[]" : macros.Json);
            sb.Append(",\"foreground\":{\"exe\":").Append(Json.Str(profiles.CurrentExe ?? "")).Append(",\"title\":").Append(Json.Str(profiles.CurrentTitle ?? "")).Append('}');
            sb.Append(",\"devices\":[");
            bool first = true;
            foreach (var d in manager.Devices)
            {
                if (!first) sb.Append(',');
                first = false;
                var r = d.Reading;
                sb.Append('{');
                sb.Append("\"key\":").Append(Json.Str(d.Key));
                sb.Append(",\"name\":").Append(Json.Str(d.Name));
                sb.Append(",\"kind\":").Append(Json.Str(d.Kind.ToString().ToLowerInvariant()));
                sb.Append(",\"family\":").Append(Json.Str(d.Family.ToString()));
                sb.Append(",\"hasBattery\":").Append(Json.Bool(d.HasBattery));
                sb.Append(",\"verified\":").Append(Json.Bool(d.Verified));
                sb.Append(",\"inTray\":").Append(Json.Bool(!settings.HiddenDevices.Contains(d.Key)));
                sb.Append(",\"links\":[").Append(string.Join(",", d.Links.Keys.OrderBy(k => k).Select(k => Json.Str(k.ToString().ToLowerInvariant())))).Append(']');
                sb.Append(",\"activeLink\":").Append(d.ReadLink.HasValue ? Json.Str(d.ReadLink.Value.ToString().ToLowerInvariant()) : "null");
                string color;
                sb.Append(",\"color\":").Append(Json.Str(settings.DeviceColors.TryGetValue(d.Key, out color) ? color : ""));
                sb.Append(",\"configurable\":").Append(Json.Bool(d.Configurable));
                int bright;
                sb.Append(",\"brightness\":").Append(settings.Brightness.TryGetValue(d.Key, out bright) ? bright.ToString() : "null");
                int brightWl;
                sb.Append(",\"brightnessWireless\":").Append(settings.Brightness.TryGetValue(d.Key + "#wireless", out brightWl) ? brightWl.ToString() : "null");
                string prefix = d.Key + "/";
                sb.Append(",\"launch\":{").Append(string.Join(",", settings.Launch.Where(kv => kv.Key.StartsWith(prefix)).Select(kv => Json.Str(kv.Key.Substring(prefix.Length)) + ":" + Json.Str(kv.Value)))).Append('}');
                sb.Append(",\"config\":").Append(d.Config == null ? "null" : d.Config.ToJson());
                string auto;
                sb.Append(",\"autoProfile\":").Append(settings.AutoProfiles.TryGetValue(d.Key, out auto) ? auto : "null");
                sb.Append(",\"canUndo\":").Append(Json.Bool(d.History.Count > 0));
                sb.Append(",\"canRestore\":").Append(Json.Bool(d.Original != null && d.History.Count > 0));
                sb.Append(",\"percent\":").Append(d.LastPercent);
                sb.Append(",\"charge\":").Append(Json.Str(r == null ? "unknown" : r.Charge.ToString().ToLowerInvariant()));
                sb.Append(",\"state\":").Append(Json.Str(r == null ? "waiting" : r.State.ToString().ToLowerInvariant()));
                sb.Append(",\"time\":").Append(Json.Str(r == null ? "" : r.Time.ToString("HH:mm")));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));          // dark title bar
            int caption = Background.R | (Background.G << 8) | (Background.B << 16);
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));     // caption color = page (Windows 11)
            int border = caption;
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));      // border color
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && settings.MinimizeToTray && Visible)
            {
                Hide();
                WindowState = FormWindowState.Normal;
                var h = HiddenToTray;
                if (h != null) h();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                var h = HiddenToTray;
                if (h != null) h();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
