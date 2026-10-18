using System;

namespace BetterGlorious.Devices
{
    public enum DeviceKind { Mouse, Keyboard, Headset, Mousepad, Other }

    /// <summary>How a device reports its battery. One protocol class per family.</summary>
    public enum BatteryFamily { None, Sinowealth, PixartV2, ModelV3, Gmmk3He, Gmmk3, GmmkNumpad }

    public enum Link { Usb, Receiver, Bluetooth }

    public enum ChargeState { Unknown, Discharging, Charging, Full }

    public enum ReadState
    {
        /// <summary>Fresh value from the device.</summary>
        Ok,
        /// <summary>Receiver is present but the device is asleep or switched off.</summary>
        Asleep,
        /// <summary>The device answered but had no value yet.</summary>
        NotReady,
    }

    public sealed class BatteryReading
    {
        public int Percent = -1;
        public ChargeState Charge = ChargeState.Unknown;
        public ReadState State = ReadState.Ok;
        public DateTime Time = DateTime.Now;

        public static BatteryReading Of(int percent, ChargeState charge)
        {
            return new BatteryReading { Percent = Math.Max(0, Math.Min(100, percent)), Charge = charge };
        }

        public static BatteryReading Asleep()
        {
            return new BatteryReading { State = ReadState.Asleep };
        }

        public bool HasPercent { get { return Percent >= 0; } }
    }
}
