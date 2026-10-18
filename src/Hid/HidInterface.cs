using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BetterGlorious.Hid
{
    /// <summary>One HID top-level collection as Windows exposes it (one device path each).</summary>
    public sealed class HidInterface
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputLength;
        public int OutputLength;
        public int FeatureLength;

        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} in={4} out={5} feat={6}",
                VendorId, ProductId, UsagePage, Usage, InputLength, OutputLength, FeatureLength);
        }

        /// <summary>Lists every present HID collection whose vendor ID passes the filter.</summary>
        public static List<HidInterface> Enumerate(Predicate<ushort> vendorFilter)
        {
            var result = new List<HidInterface>();
            foreach (string path in DevicePaths())
            {
                // Access 0 is enough for attributes and caps, and works even for
                // collections Windows keeps open exclusively (system mouse/keyboard).
                using (var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    var attr = new Native.HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(attr);
                    if (!Native.HidD_GetAttributes(h, ref attr)) continue;
                    if (!vendorFilter(attr.VendorID)) continue;

                    IntPtr pre;
                    if (!Native.HidD_GetPreparsedData(h, out pre)) continue;
                    Native.HIDP_CAPS caps;
                    Native.HidP_GetCaps(pre, out caps);
                    Native.HidD_FreePreparsedData(pre);

                    result.Add(new HidInterface
                    {
                        Path = path,
                        VendorId = attr.VendorID,
                        ProductId = attr.ProductID,
                        UsagePage = caps.UsagePage,
                        Usage = caps.Usage,
                        InputLength = caps.InputReportByteLength,
                        OutputLength = caps.OutputReportByteLength,
                        FeatureLength = caps.FeatureReportByteLength,
                    });
                }
            }
            return result;
        }

        static IEnumerable<string> DevicePaths()
        {
            Guid guid;
            Native.HidD_GetHidGuid(out guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            var paths = new List<string>();
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new Native.SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(data);
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;

                    int required;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (Native.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4)));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return paths;
        }
    }
}
