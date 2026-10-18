using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BetterGlorious.Hid;

namespace BetterGlorious.Devices
{
    /// <summary>One physical product, possibly reachable over several links at once (cable + dongle).</summary>
    public sealed class GloriousDevice
    {
        public string Key;
        public string Name;
        public DeviceKind Kind;
        public BatteryFamily Family;
        public readonly Dictionary<Link, DeviceLink> Links = new Dictionary<Link, DeviceLink>();
        public BatteryReading Reading;   // last thing we heard
        public int LastPercent = -1;     // last real percentage, kept while the device sleeps
        public Link? ReadLink;           // the link the last reading came over, i.e. the one in use
        public DeviceConfig Config;      // onboard settings, when the family supports them
        public DeviceConfig Original;    // settings as first seen this session, for "restore"
        public readonly List<DeviceConfig> History = new List<DeviceConfig>(); // before each change, for "undo"

        public bool Configurable { get { return Family == BatteryFamily.Sinowealth || Family == BatteryFamily.PixartV2 || Family == BatteryFamily.Gmmk3He; } }

        /// <summary>Families whose settings can't be read back, so the app remembers what it last wrote.</summary>
        public bool RemembersState { get { return Family == BatteryFamily.PixartV2 || Family == BatteryFamily.Gmmk3He; } }

        public BatteryProtocol Protocol { get { return BatteryProtocol.For(Family); } }
        public bool HasBattery { get { return Protocol != null; } }
        public bool Verified { get { return Protocol != null && Protocol.Verified; } }

        public Link? ActiveLink
        {
            get
            {
                foreach (var l in new[] { Link.Usb, Link.Receiver, Link.Bluetooth })
                    if (Links.ContainsKey(l)) return l;
                return null;
            }
        }
    }

    public sealed class DeviceLink
    {
        public CatalogEntry Entry;
        public HidInterface Command;   // collection for feature requests
        public HidInterface Listen;    // collection for unsolicited input reports
        internal HidConnection CommandConn;
        internal readonly object Tx = new object(); // one request/response exchange at a time
        internal Thread Listener;
        internal volatile bool StopListening;
    }

    /// <summary>
    /// Finds Glorious devices, polls their batteries on a worker thread and listens for
    /// battery reports they push on their own. <see cref="Changed"/> fires on a worker thread.
    /// </summary>
    public sealed class DeviceManager : IDisposable
    {
        readonly object sync = new object();
        Dictionary<string, GloriousDevice> devices = new Dictionary<string, GloriousDevice>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread worker;
        volatile bool stopping;
        volatile bool rescanRequested = true;
        public TimeSpan PollInterval = TimeSpan.FromSeconds(60);

        public event Action Changed;
        /// <summary>A button bound to "launch program" was pressed: (device key, button id). Worker thread.</summary>
        public event Action<string, int> LaunchPressed;
        /// <summary>Persistence for families whose settings can't be read back (key -> serialized state).</summary>
        public Func<string, string> LoadState = k => null;
        public Action<string, string> SaveState = (k, v) => { };

        public void Start()
        {
            worker = new Thread(Run) { IsBackground = true, Name = "DeviceManager" };
            worker.Start();
        }

        /// <summary>Snapshot for the UI.</summary>
        public List<GloriousDevice> Devices
        {
            get { lock (sync) return devices.Values.OrderBy(d => d.Kind).ThenBy(d => d.Name).ToList(); }
        }

        /// <summary>Re-enumerate (USB plug/unplug) and poll right away.</summary>
        public void Rescan() { rescanRequested = true; wake.Set(); }

        public void PollNow() { wake.Set(); }

        void Run()
        {
            while (!stopping)
            {
                try
                {
                    if (rescanRequested) { rescanRequested = false; Enumerate(); }
                    PollAll();
                }
                catch (Exception ex) { Log.Error("poll loop", ex); }
                wake.WaitOne(PollInterval);
            }
        }

        void Enumerate()
        {
            var found = new Dictionary<string, GloriousDevice>();
            var interfaces = HidInterface.Enumerate(v => Catalog.Vendors.Contains(v));

            foreach (var group in interfaces.GroupBy(i => ((int)i.VendorId << 16) | i.ProductId))
            {
                var first = group.First();
                var entry = Catalog.Find(first.VendorId, first.ProductId) ?? GuessEntry(group);
                if (entry == null) continue;

                var link = new DeviceLink
                {
                    Entry = entry,
                    Command = group.FirstOrDefault(i => i.UsagePage == entry.CmdPage && i.Usage == entry.CmdUsage && (i.FeatureLength > 0 || i.OutputLength > 0)),
                    Listen = group.FirstOrDefault(i => i.UsagePage == entry.ListenPage && i.Usage == entry.ListenUsage && i.InputLength > 0),
                };

                GloriousDevice dev;
                if (!found.TryGetValue(entry.Id, out dev))
                {
                    dev = new GloriousDevice { Key = entry.Id, Name = entry.Name, Kind = entry.Kind, Family = entry.Family };
                    found[entry.Id] = dev;
                }
                if (!dev.Links.ContainsKey(entry.Link)) dev.Links[entry.Link] = link;
            }

            lock (sync)
            {
                // Keep readings for devices that are still here; shut down links that vanished.
                foreach (var old in devices.Values)
                {
                    GloriousDevice now;
                    found.TryGetValue(old.Key, out now);
                    if (now != null) { now.Reading = old.Reading; now.LastPercent = old.LastPercent; now.Config = old.Config; now.Original = old.Original; now.History.AddRange(old.History); if (old.ReadLink.HasValue && now.Links.ContainsKey(old.ReadLink.Value)) now.ReadLink = old.ReadLink; }
                    foreach (var kv in old.Links)
                    {
                        DeviceLink same;
                        if (now != null && now.Links.TryGetValue(kv.Key, out same) && SamePaths(same, kv.Value))
                        {
                            same.CommandConn = kv.Value.CommandConn;
                            same.Listener = kv.Value.Listener;
                            kv.Value.CommandConn = null;
                            kv.Value.Listener = null;
                        }
                        else Close(kv.Value);
                    }
                }
                devices = found;
                foreach (var dev in devices.Values)
                    foreach (var link in dev.Links.Values)
                        StartListener(dev, link);
            }
            Log.Info("found " + found.Count + " device(s): " + string.Join(", ", found.Values.Select(d => d.Name + " [" + string.Join("+", d.Links.Keys) + "]")));
            Raise();
        }

        /// <summary>Unknown 258A product with the classic vendor collection: treat it as a Sinowealth mouse.</summary>
        static CatalogEntry GuessEntry(IEnumerable<HidInterface> group)
        {
            var cmd = group.FirstOrDefault(i => i.VendorId == 0x258A && i.UsagePage == 0xFFFF && i.Usage == 0 && i.FeatureLength >= 65);
            if (cmd == null) return null;
            string id = string.Format("Unknown258A_{0:X4}", cmd.ProductId);
            return new CatalogEntry(id, string.Format("Glorious mouse ({0:X4}:{1:X4})", cmd.VendorId, cmd.ProductId),
                DeviceKind.Mouse, BatteryFamily.Sinowealth, Link.Receiver, cmd.VendorId, cmd.ProductId, 0xFFFF, 0x00, 0x0C, 0x01);
        }

        static bool SamePaths(DeviceLink a, DeviceLink b)
        {
            return PathOf(a.Command) == PathOf(b.Command) && PathOf(a.Listen) == PathOf(b.Listen);
        }

        static string PathOf(HidInterface i) { return i == null ? null : i.Path; }

        void PollAll()
        {
            foreach (var dev in Devices)
            {
                var proto = dev.Protocol;
                if (proto == null) continue;

                BatteryReading reading = null;
                Link? readLink = null;
                foreach (var l in new[] { Link.Usb, Link.Receiver, Link.Bluetooth })
                {
                    DeviceLink link;
                    if (!dev.Links.TryGetValue(l, out link) || link.Command == null) continue;
                    BatteryReading r = null;
                    try
                    {
                        if (link.CommandConn == null) link.CommandConn = HidConnection.Open(link.Command);
                        if (link.CommandConn != null) lock (link.Tx) r = proto.Query(link.CommandConn, link.Entry);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("query " + dev.Name + " via " + l, ex);
                        if (link.CommandConn != null) { link.CommandConn.Dispose(); link.CommandConn = null; }
                    }
                    if (r == null) continue;
                    // An asleep answer from the dongle is only useful if nothing better follows.
                    if (r.State == ReadState.Ok || reading == null) { reading = r; readLink = l; }
                    if (r.State == ReadState.Ok) break;
                }
                if (reading != null) Apply(dev, reading, readLink);
                if (dev.Configurable && dev.Config == null && (dev.RemembersState || (reading != null && reading.State == ReadState.Ok))) LoadConfig(dev);
                else if (dev.Reading == null && dev.Links.ContainsKey(Link.Usb) && proto.CableMeansCharging)
                    Apply(dev, new BatteryReading { Charge = ChargeState.Charging, State = ReadState.NotReady }, Link.Usb);
            }
            Raise();
        }

        void Apply(GloriousDevice dev, BatteryReading r, Link? via)
        {
            lock (sync)
            {
                if (via.HasValue && r.State == ReadState.Ok) dev.ReadLink = via;
                var proto = dev.Protocol;
                bool cable = dev.Links.ContainsKey(Link.Usb);
                if (proto != null && proto.CableMeansCharging && cable && r.Charge != ChargeState.Full)
                    r.Charge = ChargeState.Charging;
                // On a cable the battery is always topped up; firmware just stops saying "charging" at 100.
                if (cable && r.HasPercent && (r.Charge == ChargeState.Discharging || r.Charge == ChargeState.Unknown))
                    r.Charge = r.Percent >= 100 ? ChargeState.Full : ChargeState.Charging;
                if (r.HasPercent) dev.LastPercent = r.Percent;
                dev.Reading = r;
            }
        }

        /// <summary>The link to talk to for settings: the one in use, opened on demand.</summary>
        static DeviceLink ConfigLink(GloriousDevice dev)
        {
            DeviceLink link = null;
            if (dev.ReadLink.HasValue) dev.Links.TryGetValue(dev.ReadLink.Value, out link);
            if (link == null) link = dev.Links.Values.FirstOrDefault(l => l.Command != null);
            if (link == null || link.Command == null) return null;
            if (link.CommandConn == null) link.CommandConn = HidConnection.Open(link.Command);
            return link.CommandConn == null ? null : link;
        }

        void LoadConfig(GloriousDevice dev, bool quick = false)
        {
            try
            {
                DeviceConfig cfg;
                if (dev.Family == BatteryFamily.PixartV2) cfg = PixartConfig.Parse(LoadState(dev.Key));
                else if (dev.Family == BatteryFamily.Gmmk3He) cfg = Gmmk3HeConfig.Parse(LoadState(dev.Key));
                else
                {
                    var link = ConfigLink(dev);
                    if (link == null) return;
                    lock (link.Tx) cfg = SinowealthConfig.Read(link.CommandConn, quick ? dev.Config as SinowealthConfig : null, dev.Key);
                }
                if (cfg != null) lock (sync) { dev.Config = cfg; if (dev.Original == null) dev.Original = cfg.Clone(); }
            }
            catch (Exception ex) { Log.Error("read config " + dev.Name, ex); }
        }

        /// <summary>
        /// Runs a settings write on a worker thread, then re-reads the settings so the UI shows
        /// what the device actually stored. <paramref name="done"/> gets true on success.
        /// </summary>
        public void Configure(string key, Func<HidConnection, Link, DeviceConfig, bool> write, Action<bool> done, bool touchesButtons = true, string coalesce = null)
        {
            Configure(key, write, done, true, null, !touchesButtons, coalesce);
        }

        /// <summary>Switch the onboard profile without touching undo history (automatic profiles).</summary>
        public void SwitchProfile(string key, int profile)
        {
            Configure(key, (c, l, cfg) =>
            {
                var sw = cfg as SinowealthConfig;
                if (sw != null) return sw.Profile == profile || SinowealthConfig.SetProfile(c, profile);
                var px = cfg as PixartConfig;
                if (px != null) { px.Profile = profile; return PixartConfig.WriteProfile(c, l, profile); }
                var kb = cfg as Gmmk3HeConfig;
                if (kb == null) return false;
                kb.Profile = profile;
                return Gmmk3HeConfig.WriteProfile(c, l, kb);
            }, null, false, null, false, "profile");
        }

        /// <summary>Undo the last change (or, with <paramref name="all"/>, everything since the app first read the device).</summary>
        public void Revert(string key, bool all, Action<bool> done)
        {
            GloriousDevice dev;
            lock (sync) devices.TryGetValue(key, out dev);
            if (dev == null) return;
            DeviceConfig target;
            lock (sync)
            {
                if (all) { target = dev.Original; dev.History.Clear(); }
                else
                {
                    if (dev.History.Count == 0) return;
                    target = dev.History[dev.History.Count - 1];
                    dev.History.RemoveAt(dev.History.Count - 1);
                }
            }
            if (target == null) return;
            Configure(key, (c, l, cfg) =>
            {
                var sw = target as SinowealthConfig;
                if (sw != null) return SinowealthConfig.Restore(c, (SinowealthConfig)cfg, sw);
                var he = target as Gmmk3HeConfig;
                return he != null ? Gmmk3HeConfig.WriteAll(c, l, he) : PixartConfig.WriteAll(c, l, (PixartConfig)target);
            }, done, false, target.Clone());
        }

        /// <summary>One pending settings write.</summary>
        sealed class ConfigJob
        {
            public string Coalesce;   // writes with the same key replace each other while still queued
            public Func<HidConnection, Link, DeviceConfig, bool> Write;
            public readonly List<Action<bool>> Done = new List<Action<bool>>();
            public bool Remember;
            public DeviceConfig ReplaceWith;
            public bool FullRead;
        }

        // Per device: writes run one at a time, in the order the user made them, on one worker.
        readonly Dictionary<string, LinkedList<ConfigJob>> queues = new Dictionary<string, LinkedList<ConfigJob>>();
        readonly HashSet<string> draining = new HashSet<string>();

        void Configure(string key, Func<HidConnection, Link, DeviceConfig, bool> write, Action<bool> done, bool remember, DeviceConfig replaceWith, bool quickRead = false, string coalesce = null)
        {
            lock (queues)
            {
                LinkedList<ConfigJob> q;
                if (!queues.TryGetValue(key, out q)) queues[key] = q = new LinkedList<ConfigJob>();
                // A newer value for the same setting replaces the one still waiting (dragging, fast clicks).
                var same = coalesce == null ? null : q.FirstOrDefault(j => j.Coalesce == coalesce);
                if (same != null)
                {
                    same.Write = write;
                    same.FullRead |= !quickRead;
                    if (done != null) same.Done.Add(done);
                }
                else
                {
                    var job = new ConfigJob { Coalesce = coalesce, Write = write, Remember = remember, ReplaceWith = replaceWith, FullRead = !quickRead };
                    if (done != null) job.Done.Add(done);
                    q.AddLast(job);
                }
                if (draining.Add(key)) ThreadPool.QueueUserWorkItem(_ => Drain(key));
            }
        }

        void Drain(string key)
        {
            var finished = new List<KeyValuePair<Action<bool>, bool>>();
            bool fullRead = false;
            while (true)
            {
                ConfigJob job;
                lock (queues)
                {
                    var q = queues[key];
                    if (q.Count == 0) break;
                    job = q.First.Value;
                    q.RemoveFirst();
                }
                bool ok = false;
                try
                {
                    GloriousDevice dev;
                    lock (sync) devices.TryGetValue(key, out dev);
                    var link = dev == null ? null : ConfigLink(dev);
                    if (link != null && dev.Config != null)
                    {
                        if (job.Remember)
                            lock (sync)
                            {
                                dev.History.Add(dev.Config.Clone());
                                if (dev.History.Count > 50) dev.History.RemoveAt(0);
                            }
                        lock (link.Tx) ok = job.Write(link.CommandConn, link.Entry.Link, dev.Config);
                        Log.Info("configure " + dev.Name + " " + (job.Coalesce ?? "-") + ": " + (ok ? "ok" : "failed"));
                        if (dev.Family != BatteryFamily.Sinowealth)
                        {
                            if (ok && job.ReplaceWith != null) lock (sync) dev.Config = job.ReplaceWith;
                            var px = dev.Config as PixartConfig;
                            if (px != null) SaveState(dev.Key, px.Serialize());
                            var he = dev.Config as Gmmk3HeConfig;
                            if (he != null) SaveState(dev.Key, he.Serialize());
                        }
                    }
                }
                catch (Exception ex) { Log.Error("configure " + key, ex); }
                fullRead |= job.FullRead;
                foreach (var d in job.Done) finished.Add(new KeyValuePair<Action<bool>, bool>(d, ok));

                // Read back once the burst is over, so the UI never sees an in-between value.
                bool idle;
                lock (queues) idle = queues[key].Count == 0;
                if (idle)
                {
                    GloriousDevice dev;
                    lock (sync) devices.TryGetValue(key, out dev);
                    if (dev != null && dev.Family == BatteryFamily.Sinowealth) LoadConfig(dev, !fullRead);
                    fullRead = false;
                    foreach (var f in finished) { try { f.Key(f.Value); } catch (Exception ex) { Log.Error("configure done", ex); } }
                    finished.Clear();
                    Raise();
                }
            }
            lock (queues)
            {
                draining.Remove(key);
                if (queues[key].Count > 0 && draining.Add(key)) ThreadPool.QueueUserWorkItem(_ => Drain(key));
            }
        }

        void StartListener(GloriousDevice dev, DeviceLink link)
        {
            var proto = dev.Protocol;
            if (proto == null || link.Listen == null || link.Listener != null || !proto.Listens(link.Entry.Link)) return;
            link.StopListening = false;
            var t = new Thread(() => Listen(dev.Key, link, proto)) { IsBackground = true, Name = "Listen " + dev.Name };
            link.Listener = t;
            t.Start();
        }

        void Listen(string key, DeviceLink link, BatteryProtocol proto)
        {
            HidConnection conn = null;
            try
            {
                conn = HidConnection.Open(link.Listen);
                if (conn == null) return;
                while (!link.StopListening && !stopping)
                {
                    var report = conn.ReadInput(1000);
                    if (report == null) continue;
                    int launch = proto.ParseLaunch(report);
                    if (launch >= 0)
                    {
                        var lp = LaunchPressed;
                        if (lp != null) lp(key, launch);
                        continue;
                    }
                    var r = proto.ParseInput(report, link.Entry);
                    if (r == null) continue;
                    GloriousDevice dev;
                    lock (sync) devices.TryGetValue(key, out dev);
                    if (dev == null) break;
                    Apply(dev, r, link.Entry.Link);
                    Raise();
                }
            }
            catch (Exception ex) { Log.Error("listener " + key, ex); }
            finally { if (conn != null) conn.Dispose(); }
        }

        static void Close(DeviceLink link)
        {
            link.StopListening = true;
            if (link.CommandConn != null) { link.CommandConn.Dispose(); link.CommandConn = null; }
        }

        void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        /// <summary>Plain-text dump of every Glorious HID collection, for bug reports.</summary>
        public static string Diagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine(AppInfo.Name + " " + AppInfo.Version + " diagnostics, " + DateTime.Now.ToString("u"));
            sb.AppendLine("OS: " + Environment.OSVersion);
            foreach (var i in HidInterface.Enumerate(v => Catalog.Vendors.Contains(v)).OrderBy(i => i.VendorId).ThenBy(i => i.ProductId))
            {
                var e = Catalog.Find(i.VendorId, i.ProductId);
                sb.AppendLine(i + "  -> " + (e == null ? "not in catalog" : e.Name + " / " + e.Family + " / " + e.Link));
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
            lock (sync)
                foreach (var d in devices.Values)
                    foreach (var l in d.Links.Values) Close(l);
        }
    }
}
