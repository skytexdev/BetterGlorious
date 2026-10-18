using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BetterGlorious
{
    public enum TrayMode { PerDevice, LowestOnly }

    /// <summary>User settings, stored as key=value lines in %APPDATA%\BetterGlorious\settings.ini.</summary>
    public sealed class Settings
    {
        public int PollSeconds = 60;
        public int LowBatteryPercent = 20;
        public bool Notifications = true;
        public TrayMode Tray = TrayMode.PerDevice;
        public string Language = "auto";
        public bool MinimizeToTray = true;
        public bool CloseHintShown;
        public readonly HashSet<string> HiddenDevices = new HashSet<string>();
        /// <summary>Product colorway per device ("black"/"white"); firmware can't report it.</summary>
        public readonly Dictionary<string, string> DeviceColors = new Dictionary<string, string>();
        /// <summary>Last brightness set per device (the firmware can't report it).</summary>
        public readonly Dictionary<string, int> Brightness = new Dictionary<string, int>();
        /// <summary>"deviceKey/buttonId" -> program, file or URL started when that button is pressed.</summary>
        public readonly Dictionary<string, string> Launch = new Dictionary<string, string>();
        /// <summary>Remembered onboard settings for devices that can't report them (PixArt mice).</summary>
        public readonly Dictionary<string, string> DeviceState = new Dictionary<string, string>();
        /// <summary>Per device: automatic profile rules as JSON (see ProfileSwitcher).</summary>
        public readonly Dictionary<string, string> AutoProfiles = new Dictionary<string, string>();

        static string FilePath { get { return Path.Combine(AppInfo.DataDir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k.StartsWith("launch.")) { s.Launch[k.Substring(7)] = v; continue; }
                    if (k.StartsWith("state.")) { s.DeviceState[k.Substring(6)] = v; continue; }
                    if (k.StartsWith("autoprofile.")) { s.AutoProfiles[k.Substring(12)] = v; continue; }
                    int n;
                    switch (k)
                    {
                        case "poll_seconds": if (int.TryParse(v, out n)) s.PollSeconds = Math.Max(15, n); break;
                        case "low_battery": if (int.TryParse(v, out n)) s.LowBatteryPercent = Math.Max(0, Math.Min(100, n)); break;
                        case "notifications": s.Notifications = v == "1"; break;
                        case "tray": s.Tray = v == "lowest" ? TrayMode.LowestOnly : TrayMode.PerDevice; break;
                        case "language": s.Language = v; break;
                        case "minimize_to_tray": s.MinimizeToTray = v == "1"; break;
                        case "close_hint_shown": s.CloseHintShown = v == "1"; break;
                        case "colors":
                            foreach (var pair in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var kv = pair.Split(':');
                                if (kv.Length == 2) s.DeviceColors[kv[0].Trim()] = kv[1].Trim();
                            }
                            break;
                        case "brightness":
                            foreach (var pair in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var kv = pair.Split(':');
                                if (kv.Length == 2 && int.TryParse(kv[1], out n)) s.Brightness[kv[0].Trim()] = n;
                            }
                            break;
                        case "hidden":
                            foreach (var id in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) s.HiddenDevices.Add(id.Trim());
                            break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("settings load", ex); }
            return s;
        }

        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "poll_seconds=" + PollSeconds.ToString(CultureInfo.InvariantCulture),
                    "low_battery=" + LowBatteryPercent.ToString(CultureInfo.InvariantCulture),
                    "notifications=" + (Notifications ? "1" : "0"),
                    "tray=" + (Tray == TrayMode.LowestOnly ? "lowest" : "per_device"),
                    "language=" + Language,
                    "minimize_to_tray=" + (MinimizeToTray ? "1" : "0"),
                    "close_hint_shown=" + (CloseHintShown ? "1" : "0"),
                    "hidden=" + string.Join(",", HiddenDevices),
                    "colors=" + string.Join(",", DeviceColors.Select(kv => kv.Key + ":" + kv.Value)),
                    "brightness=" + string.Join(",", Brightness.Select(kv => kv.Key + ":" + kv.Value)),
                };
                lines.AddRange(Launch.Select(kv => "launch." + kv.Key + "=" + kv.Value));
                lines.AddRange(DeviceState.Select(kv => "state." + kv.Key + "=" + kv.Value));
                lines.AddRange(AutoProfiles.Select(kv => "autoprofile." + kv.Key + "=" + kv.Value));
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex) { Log.Error("settings save", ex); }
        }
    }

    /// <summary>"Start with Windows" via the per-user Run key.</summary>
    public static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppInfo.Name) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(AppInfo.Name, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(AppInfo.Name, false);
                }
            }
        }
    }
}
