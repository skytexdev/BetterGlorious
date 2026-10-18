using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BetterGlorious.Hid
{
    /// <summary>
    /// An open HID collection. Feature reports use a synchronous handle; input reads use a
    /// separate overlapped handle so they can time out and be cancelled.
    /// </summary>
    public sealed class HidConnection : IDisposable
    {
        public readonly HidInterface Info;
        SafeFileHandle sync;
        SafeFileHandle async;
        IntPtr readEvent = IntPtr.Zero;
        readonly object ioLock = new object();

        HidConnection(HidInterface info, SafeFileHandle handle)
        {
            Info = info;
            sync = handle;
        }

        public static HidConnection Open(HidInterface info)
        {
            var h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            return new HidConnection(info, h);
        }

        /// <summary>Sends a feature report. The buffer is padded to the collection's feature length.</summary>
        public bool SetFeature(byte[] report)
        {
            byte[] buf = Pad(report, Info.FeatureLength);
            lock (ioLock) return Native.HidD_SetFeature(sync, buf, buf.Length);
        }

        /// <summary>Reads a feature report. Index 0 of the result is the report ID.</summary>
        public byte[] GetFeature(byte reportId)
        {
            var buf = new byte[Math.Max(Info.FeatureLength, 2)];
            buf[0] = reportId;
            lock (ioLock) return Native.HidD_GetFeature(sync, buf, buf.Length) ? buf : null;
        }

        /// <summary>Sends an output report via the control pipe.</summary>
        public bool SetOutput(byte[] report)
        {
            byte[] buf = Pad(report, Info.OutputLength);
            lock (ioLock) return Native.HidD_SetOutputReport(sync, buf, buf.Length);
        }

        /// <summary>Waits for one input report; returns null on timeout or error.</summary>
        public byte[] ReadInput(int timeoutMs)
        {
            if (Info.InputLength <= 0) return null;
            if (async == null)
            {
                async = Native.CreateFile(Info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                    Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (async.IsInvalid)
                {
                    // Some collections refuse write access; reading is all we need here.
                    async.Dispose();
                    async = Native.CreateFile(Info.Path, Native.GENERIC_READ,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                        Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                    if (async.IsInvalid) { async.Dispose(); async = null; return null; }
                }
                readEvent = Native.CreateEvent(IntPtr.Zero, true, false, null);
            }

            int len = Info.InputLength;
            IntPtr buf = Marshal.AllocHGlobal(len);
            IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.OVERLAPPED)));
            try
            {
                Native.ResetEvent(readEvent);
                var o = new Native.OVERLAPPED { hEvent = readEvent };
                Marshal.StructureToPtr(o, ov, false);

                int got;
                if (!Native.ReadFile(async, buf, len, IntPtr.Zero, ov))
                {
                    if (Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING) return null;
                    if (Native.WaitForSingleObject(readEvent, (uint)timeoutMs) != Native.WAIT_OBJECT_0)
                    {
                        Native.CancelIoEx(async, ov);
                        Native.GetOverlappedResult(async, ov, out got, true);
                        return null;
                    }
                }
                if (!Native.GetOverlappedResult(async, ov, out got, true) || got <= 0) return null;
                var data = new byte[got];
                Marshal.Copy(buf, data, 0, got);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(ov);
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>Feature write, short wait, feature read: the request/response shape most Glorious firmware uses.</summary>
        public byte[] Transact(byte[] request, int delayMs)
        {
            if (!SetFeature(request)) return null;
            Thread.Sleep(delayMs);
            return GetFeature(request[0]);
        }

        static byte[] Pad(byte[] report, int length)
        {
            if (length <= 0 || report.Length == length) return report;
            var buf = new byte[length];
            Array.Copy(report, buf, Math.Min(report.Length, length));
            return buf;
        }

        public void Dispose()
        {
            if (sync != null) { sync.Dispose(); sync = null; }
            if (async != null) { Native.CancelIoEx(async, IntPtr.Zero); async.Dispose(); async = null; }
            if (readEvent != IntPtr.Zero) { Native.CloseHandle(readEvent); readEvent = IntPtr.Zero; }
        }
    }
}
