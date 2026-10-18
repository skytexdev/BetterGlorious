using System.Collections.Generic;

namespace BetterGlorious.Devices
{
    public sealed class CatalogEntry
    {
        public readonly string Id;
        public readonly string Name;
        public readonly DeviceKind Kind;
        public readonly BatteryFamily Family;
        public readonly Link Link;
        public readonly ushort Vid, Pid;
        public readonly ushort CmdPage, CmdUsage;
        public readonly ushort ListenPage, ListenUsage;

        public CatalogEntry(string id, string name, DeviceKind kind, BatteryFamily family, Link link,
            int vid, int pid, int cmdPage, int cmdUsage, int listenPage, int listenUsage)
        {
            Id = id; Name = name; Kind = kind; Family = family; Link = link;
            Vid = (ushort)vid; Pid = (ushort)pid;
            CmdPage = (ushort)cmdPage; CmdUsage = (ushort)cmdUsage;
            ListenPage = (ushort)listenPage; ListenUsage = (ushort)listenUsage;
        }
    }

    public static partial class Catalog
    {
        /// <summary>Vendor IDs used by Glorious products and their OEM chips.</summary>
        public static readonly HashSet<ushort> Vendors = new HashSet<ushort>
        {
            0x258A, // Sinowealth (Model O/D wireless, Pro series)
            0x093A, // PixArt (Model O2/D2/I2 wireless)
            0x3794, // Glorious (O3/D3, GMMK 3 variants)
            0x342D, // Glorious (GMMK 3)
            0x320F, // Wired O2/D2/I2, GMMK
            0x22D4, // Model I
        };

        /// <summary>
        /// Entries added by hand: IDs that older CORE builds or verified hardware use but the
        /// generated table lacks.
        /// </summary>
        static readonly CatalogEntry[] Extra =
        {
            new CatalogEntry("ModelD2ProDongleKit", "Model D2 Pro (Dongle Kit)", DeviceKind.Mouse, BatteryFamily.Sinowealth, Link.Receiver, 0x258A, 0x2037, 0xFFFF, 0x00, 0x0C, 0x01),
            new CatalogEntry("ModelO2ProDongleKit", "Model O2 Pro (Dongle Kit)", DeviceKind.Mouse, BatteryFamily.Sinowealth, Link.Receiver, 0x258A, 0x2038, 0xFFFF, 0x00, 0x0C, 0x01),
            new CatalogEntry("GMMKNumpad", "GMMK Numpad", DeviceKind.Keyboard, BatteryFamily.GmmkNumpad, Link.Bluetooth, 0x093A, 0x2860, 0xFF01, 0x01, 0xFF01, 0x01),
        };

        static Dictionary<long, CatalogEntry> byId;

        public static CatalogEntry Find(ushort vid, ushort pid)
        {
            if (byId == null)
            {
                var map = new Dictionary<long, CatalogEntry>();
                foreach (var e in Generated) map[Key(e.Vid, e.Pid)] = e;
                foreach (var e in Extra) if (!map.ContainsKey(Key(e.Vid, e.Pid))) map[Key(e.Vid, e.Pid)] = e;
                byId = map;
            }
            CatalogEntry entry;
            return byId.TryGetValue(Key(vid, pid), out entry) ? entry : null;
        }

        public static IEnumerable<CatalogEntry> All
        {
            get
            {
                foreach (var e in Generated) yield return e;
                foreach (var e in Extra) yield return e;
            }
        }

        static long Key(ushort vid, ushort pid) { return ((long)vid << 16) | pid; }
    }
}
