namespace BetterGlorious.Devices
{
    /// <summary>
    /// Onboard settings of one device. Each family implements reading (or remembering) and writing;
    /// the UI gets the same JSON shape from all of them, plus a "features" object saying what applies.
    /// </summary>
    public abstract class DeviceConfig
    {
        public abstract string ToJson();
        public abstract DeviceConfig Clone();
    }
}
