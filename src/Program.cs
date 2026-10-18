using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using BetterGlorious.Devices;
using BetterGlorious.UI;

namespace BetterGlorious
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Contains("--dump-config")) { DumpConfig(); return; }
            MigrateFromNoCore();

            bool created;
            using (var mutex = new Mutex(true, AppInfo.Name + ".Instance", out created))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.Name + ".Show"))
            {
                if (!created)
                {
                    // Already running: ask that instance to open its panel.
                    showSignal.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App(args.Contains("--tray"), showSignal));
            }
        }

        /// <summary>The app used to be called NoCore: carry its settings and autostart entry over once.</summary>
        static void MigrateFromNoCore()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string oldIni = System.IO.Path.Combine(appData, "NoCore", "settings.ini");
                string newIni = System.IO.Path.Combine(AppInfo.DataDir, "settings.ini");
                if (System.IO.File.Exists(oldIni) && !System.IO.File.Exists(newIni)) System.IO.File.Copy(oldIni, newIni);

                using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (run != null && run.GetValue("NoCore") != null)
                    {
                        run.DeleteValue("NoCore", false);
                        Startup.Enabled = true;
                    }
            }
            catch (Exception ex) { Log.Error("migrate from NoCore", ex); }
        }

        /// <summary>Diagnostics: read the onboard settings of every Sinowealth mouse into %APPDATA%\BetterGlorious\config-dump.txt.</summary>
        static void DumpConfig()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var i in BetterGlorious.Hid.HidInterface.Enumerate(v => v == 0x258A).Where(i => i.UsagePage == 0xFFFF && i.Usage == 0 && i.FeatureLength >= 65))
            {
                sb.AppendLine(i.ToString());
                using (var c = BetterGlorious.Hid.HidConnection.Open(i))
                {
                    if (c == null) { sb.AppendLine("  open failed"); continue; }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var cfg = SinowealthConfig.Read(c);
                    sb.AppendLine("  read in " + sw.ElapsedMilliseconds + " ms: " + (cfg == null ? "no answer" : cfg.ToJson()));
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppInfo.DataDir, "config-dump.txt"), sb.ToString());
        }
    }

    /// <summary>Wires the device manager, tray icons and panel together.</summary>
    sealed class App : ApplicationContext
    {
        readonly Settings settings = Settings.Load();
        readonly DeviceManager manager = new DeviceManager();
        readonly TrayController tray;
        readonly DeviceWatcher watcher;
        readonly BetterGlorious.Macros.MacroEngine macros;
        readonly BetterGlorious.Macros.ProfileSwitcher profiles;
        readonly Control ui = new Control();
        WebPanel panel;
        volatile bool exiting;

        public App(bool startHidden, EventWaitHandle showSignal)
        {
            T.Init(settings.Language);
            ui.CreateControl();

            tray = new TrayController(manager, settings, ShowPanel, Exit);
            manager.PollInterval = TimeSpan.FromSeconds(settings.PollSeconds);
            manager.LoadState = k => { string v; return settings.DeviceState.TryGetValue(k, out v) ? v : null; };
            manager.SaveState = (k, v) => { lock (settings) { settings.DeviceState[k] = v; settings.Save(); } };
            manager.Changed += () => Post(OnDevicesChanged);
            manager.LaunchPressed += (key, button) => Post(() => Launch(key, button));
            watcher = new DeviceWatcher(manager.Rescan);
            macros = new BetterGlorious.Macros.MacroEngine();
            profiles = new BetterGlorious.Macros.ProfileSwitcher(
                k => { string v; return settings.AutoProfiles.TryGetValue(k, out v) ? v : null; },
                () => manager.Devices.Where(d => d.Configurable && d.Config != null).Select(d => d.Key),
                (k, p) => manager.SwitchProfile(k, p));
            profiles.Changed += () => { if (panel != null && panel.Visible) panel.PushState(); };

            var t = new Thread(() =>
            {
                while (!exiting)
                    if (showSignal.WaitOne(500) && !exiting) Post(ShowPanel);
            }) { IsBackground = true };
            t.Start();

            UpdateChecker.Found += () => Post(() =>
            {
                tray.Notify(string.Format(T.Get("update_title"), "v" + UpdateChecker.LatestVersion), T.Get("update_body"));
                if (panel != null) panel.PushState();
            });
            UpdateChecker.Start();

            tray.Update();
            manager.Start();
            if (!startHidden) ShowPanel();
        }

        void Post(Action a)
        {
            if (exiting || !ui.IsHandleCreated) return;
            try { ui.BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        /// <summary>A mouse button bound to "launch" was pressed: start what the user picked for it.</summary>
        void Launch(string key, int button)
        {
            string target;
            if (!settings.Launch.TryGetValue(key + "/" + button, out target)) return;
            try { System.Diagnostics.Process.Start(target); }
            catch (Exception ex) { Log.Error("launch " + target, ex); }
        }

        void OnDevicesChanged()
        {
            tray.Update();
            if (panel != null && panel.Visible) panel.PushState();
        }

        void OnSettingsChanged()
        {
            manager.PollInterval = TimeSpan.FromSeconds(settings.PollSeconds);
            tray.Update();
            if (panel != null) panel.PushState();
        }

        void ShowPanel()
        {
            if (panel == null || panel.IsDisposed)
            {
                panel = new WebPanel(manager, settings, macros, profiles, OnSettingsChanged);
                panel.HiddenToTray += () =>
                {
                    if (settings.CloseHintShown) return;
                    settings.CloseHintShown = true;
                    settings.Save();
                    tray.Notify(T.Get("bg_title"), T.Get("bg_body"));
                };
            }
            panel.Show();
            if (panel.WindowState == FormWindowState.Minimized) panel.WindowState = FormWindowState.Normal;
            panel.Activate();
            panel.PushState();
        }

        void Exit()
        {
            exiting = true;
            if (panel != null) { panel.AllowClose = true; panel.Close(); }
            tray.Dispose();
            watcher.Dispose();
            macros.Dispose();
            profiles.Dispose();
            manager.Dispose();
            ExitThread();
        }
    }

    /// <summary>Hidden top-level window that receives WM_DEVICECHANGE and debounces it.</summary>
    sealed class DeviceWatcher : NativeWindow, IDisposable
    {
        const int WM_DEVICECHANGE = 0x0219;
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 1500 };

        public DeviceWatcher(Action onChange)
        {
            debounce.Tick += delegate { debounce.Stop(); onChange(); };
            CreateHandle(new CreateParams { Caption = AppInfo.Name + ".Watcher" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE) { debounce.Stop(); debounce.Start(); }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            debounce.Dispose();
            DestroyHandle();
        }
    }
}
