using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BetterGlorious.Hid
{
    internal static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int ERROR_IO_PENDING = 997;
        public const uint WAIT_OBJECT_0 = 0;
        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int toRead, IntPtr read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
