using System.Threading;
using BetterGlorious.Hid;

namespace BetterGlorious.Devices
{
    /// <summary>
    /// Battery protocol for one device family. Byte indexes below are as Windows returns
    /// feature/input reports: index 0 is always the report ID. See docs/PROTOCOLS.md.
    /// </summary>
    public abstract class BatteryProtocol
    {
        /// <summary>True once someone has confirmed this family on real hardware.</summary>
        public abstract bool Verified { get; }

        /// <summary>Actively asks the device. Null means "no answer"; the caller may try another link.</summary>
        public abstract BatteryReading Query(HidConnection cmd, CatalogEntry entry);

        /// <summary>Parses an unsolicited input report. Null if the report is not about battery.</summary>
        public virtual BatteryReading ParseInput(byte[] r, CatalogEntry entry) { return null; }

        /// <summary>Whether input reports on the listen collection carry battery updates.</summary>
        public virtual bool Listens(Link link) { return false; }

        /// <summary>Button id from a "launch program" button press report, or -1.</summary>
        public virtual int ParseLaunch(byte[] r) { return -1; }

        /// <summary>Whether a present USB cable alone means "charging" (device never says so itself).</summary>
        public virtual bool CableMeansCharging { get { return false; } }

        public static BatteryProtocol For(BatteryFamily family)
        {
            switch (family)
            {
                case BatteryFamily.Sinowealth: return Sinowealth;
                case BatteryFamily.PixartV2: return PixartV2;
                case BatteryFamily.ModelV3: return ModelV3;
                case BatteryFamily.Gmmk3He: return Gmmk3He;
                case BatteryFamily.Gmmk3: return Gmmk3;
                case BatteryFamily.GmmkNumpad: return GmmkNumpad;
                default: return null;
            }
        }

        static readonly BatteryProtocol Sinowealth = new SinowealthProtocol();
        static readonly BatteryProtocol PixartV2 = new PixartV2Protocol();
        static readonly BatteryProtocol ModelV3 = new ModelV3Protocol();
        static readonly BatteryProtocol Gmmk3He = new Gmmk3HeProtocol();
        static readonly BatteryProtocol Gmmk3 = new Gmmk3Protocol();
        static readonly BatteryProtocol GmmkNumpad = new GmmkNumpadProtocol();

        protected static bool InRange(int v) { return v >= 0 && v <= 100; }
    }

    /// <summary>
    /// Model O/D/O-/D- Wireless, O/D Pro, Series One/Two Pro, O2/D2 Pro (VID 258A).
    /// Feature report 0: [3]=02 target mouse, [4]=02 length, [6]=83 battery.
    /// Reply: [1] status (A1 ok, A4 asleep, A0 busy, A2 fail), [6] echo 83, [7] charging, [8] percent.
    /// </summary>
    sealed class SinowealthProtocol : BatteryProtocol
    {
        public override bool Verified { get { return true; } }

        // The listen collection only carries button events for this family (01 0F <button>).
        public override bool Listens(Link link) { return true; }

        public override int ParseLaunch(byte[] r)
        {
            return r.Length >= 3 && r[0] == 0x01 && r[1] == 0x0F ? r[2] : -1;
        }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var req = new byte[65];
                req[3] = 0x02; req[4] = 0x02; req[6] = 0x83;
                var r = cmd.Transact(req, 100 + attempt * 60);
                if (r == null || r.Length < 9) return null;
                if (r[1] == 0xA4) return BatteryReading.Asleep();
                if (r[1] == 0xA1 && r[6] == 0x83)
                {
                    var charge = r[7] == 1 ? ChargeState.Charging
                               : r[7] == 2 || r[7] == 3 ? ChargeState.Full
                               : ChargeState.Discharging;
                    return BatteryReading.Of(r[8] == 0 ? 1 : r[8], charge);
                }
                // A0 busy / A2 fail / stale echo: give the radio a moment and retry.
            }
            return null;
        }
    }

    /// <summary>
    /// Model O2 / D2 / I2 / O2 Mini Wireless (PixArt, VID 093A).
    /// Query: feature 3 "03 08 FA 14" over cable, "03 08 FB 14" via dongle, feature 5 "05 08 14" over BT.
    /// Reply: [2]=14, [3] percent (FF = not ready), [4] charging.
    /// The dongle also pushes input report 06 FB pct charging.
    /// </summary>
    sealed class PixartV2Protocol : BatteryProtocol
    {
        public override bool Verified { get { return false; } }
        public override bool CableMeansCharging { get { return true; } }
        public override bool Listens(Link link) { return link != Link.Usb; }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            byte[] req = entry.Link == Link.Bluetooth
                ? new byte[] { 0x05, 0x08, 0x14 }
                : new byte[] { 0x03, 0x08, (byte)(entry.Link == Link.Usb ? 0xFA : 0xFB), 0x14 };
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var r = cmd.Transact(req, 50 + attempt * 100);
                if (r == null || r.Length < 5) return null;
                if (r[2] != 0x14) return null;
                if (r[3] == 0xFF) continue;
                return BatteryReading.Of(r[3] == 0 ? 1 : r[3], r[4] != 0 ? ChargeState.Charging : ChargeState.Discharging);
            }
            return new BatteryReading { State = ReadState.NotReady };
        }

        public override BatteryReading ParseInput(byte[] r, CatalogEntry entry)
        {
            if (r.Length >= 4 && r[0] == 0x06 && r[1] == 0xFB && InRange(r[2]))
                return BatteryReading.Of(r[2], r[3] != 0 ? ChargeState.Charging : ChargeState.Discharging);
            return null;
        }
    }

    /// <summary>
    /// Model O3 / D3 Wireless (VID 3794). Feature 3: [1]=BB device, [2]=82 battery info.
    /// Reply over cable: [7] charging, [8] percent. Through the receiver the reply is
    /// wrapped and shifted by two bytes.
    /// </summary>
    sealed class ModelV3Protocol : BatteryProtocol
    {
        public override bool Verified { get { return false; } }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            var req = new byte[] { 0x03, 0xBB, 0x82, 0x00 };
            bool viaReceiver = entry.Link == Link.Receiver;
            var r = cmd.Transact(req, viaReceiver ? 200 : 50);
            if (r == null || r.Length < 11) return null;
            int chg = viaReceiver ? r[9] : r[7];
            int pct = viaReceiver ? r[10] : r[8];
            if (!InRange(pct)) return null;
            return BatteryReading.Of(pct, chg == 1 ? ChargeState.Charging : ChargeState.Discharging);
        }
    }

    /// <summary>
    /// GMMK 3 / 3 PRO HE (VID 342D / 3794). Feature 0: [1]=83, [8]=7C checksum (FF-83).
    /// Reply: [1]=83, [2] percent, [3] state (0 discharging, 1 charging, 2 full).
    /// The primary 2.4 GHz receiver instead takes [1]=F7 and replies [2] percent.
    /// </summary>
    sealed class Gmmk3HeProtocol : BatteryProtocol
    {
        public override bool Verified { get { return false; } }
        public override bool Listens(Link link) { return true; }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            bool primaryReceiver = entry.Link == Link.Receiver && entry.Vid == 0x342D;
            if (entry.Link == Link.Bluetooth) return null; // BT path uses output reports; listener covers it.
            var req = new byte[65];
            if (primaryReceiver) req[1] = 0xF7;
            else { req[1] = 0x83; req[8] = 0x7C; }
            var r = cmd.Transact(req, 150);
            if (r == null || r.Length < 4) return null;
            if (primaryReceiver)
                return InRange(r[2]) ? BatteryReading.Of(r[2], ChargeState.Discharging) : null;
            if (r[1] != 0x83 || !InRange(r[2])) return null;
            return BatteryReading.Of(r[2], State(r[3]));
        }

        public override BatteryReading ParseInput(byte[] r, CatalogEntry entry)
        {
            // USB/receiver: 05 83 pct state. Bluetooth: 06 xx 83 pct state.
            if (r.Length >= 4 && r[0] == 0x05 && r[1] == 0x83 && InRange(r[2]))
                return BatteryReading.Of(r[2], State(r[3]));
            if (r.Length >= 5 && r[0] == 0x06 && r[2] == 0x83 && InRange(r[3]))
                return BatteryReading.Of(r[3], State(r[4]));
            return null;
        }

        static ChargeState State(byte b)
        {
            return b == 1 ? ChargeState.Charging : b == 2 ? ChargeState.Full : ChargeState.Discharging;
        }
    }

    /// <summary>
    /// GMMK 3 PRO Wireless, non-HE (VID 342D). Cable: feature 7 "07 14", reply [2] percent
    /// (FF = on USB, level unknown). Receiver/BT push event 0x80 with the level.
    /// </summary>
    sealed class Gmmk3Protocol : BatteryProtocol
    {
        public override bool Verified { get { return false; } }
        public override bool CableMeansCharging { get { return true; } }
        public override bool Listens(Link link) { return link != Link.Usb; }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            if (entry.Link != Link.Usb) return null;
            var r = cmd.Transact(new byte[] { 0x07, 0x14 }, 50);
            if (r == null || r.Length < 3 || !InRange(r[2])) return null;
            return BatteryReading.Of(r[2], ChargeState.Charging);
        }

        public override BatteryReading ParseInput(byte[] r, CatalogEntry entry)
        {
            if (r.Length >= 4 && r[0] == 0x06 && r[1] == 0x66 && r[2] == 0x80 && InRange(r[3]))
                return BatteryReading.Of(r[3], ChargeState.Discharging);
            if (r.Length >= 3 && r[1] == 0x80 && InRange(r[2]))
                return BatteryReading.Of(r[2], ChargeState.Discharging);
            return null;
        }
    }

    /// <summary>
    /// GMMK Numpad. Cable: feature 7 "07 8A", reply [2] percent, [3] charging.
    /// Bluetooth pushes input "04 8A pct charging".
    /// </summary>
    sealed class GmmkNumpadProtocol : BatteryProtocol
    {
        public override bool Verified { get { return false; } }
        public override bool Listens(Link link) { return link == Link.Bluetooth; }

        public override BatteryReading Query(HidConnection cmd, CatalogEntry entry)
        {
            if (entry.Link != Link.Usb) return null;
            var r = cmd.Transact(new byte[] { 0x07, 0x8A }, 30);
            if (r == null || r.Length < 4 || !InRange(r[2])) return null;
            return BatteryReading.Of(r[2], r[3] != 0 ? ChargeState.Charging : ChargeState.Discharging);
        }

        public override BatteryReading ParseInput(byte[] r, CatalogEntry entry)
        {
            if (r.Length >= 4 && r[0] == 0x04 && r[1] == 0x8A && InRange(r[2]))
                return BatteryReading.Of(r[2], r[3] != 0 ? ChargeState.Charging : ChargeState.Discharging);
            return null;
        }
    }
}
