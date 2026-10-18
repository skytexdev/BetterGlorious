using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGlorious.Macros
{
    /// <summary>
    /// Software macros, played by the PC instead of the mouse firmware (the way Razer Synapse does it).
    ///
    /// A mouse button is mapped onboard to one of F13–F24, keys no game uses. A low-level keyboard
    /// hook swallows those keys and plays the macro bound to them with SendInput (scan codes, so
    /// games that read raw input see real key presses). Timing uses the high-resolution clock, and
    /// every key stays down at least <see cref="SoftMacro.MinHoldMs"/> so tick-based games
    /// (Minecraft samples every 50 ms) don't miss short presses.
    ///
    /// Must be created on a thread with a message loop (the UI thread): the hook runs there.
    /// </summary>
    public sealed class MacroEngine : IDisposable
    {
        public const int FirstTriggerVk = 0x7C;   // VK_F13
        public const int TriggerCount = 12;       // F13..F24
        public const int FirstTriggerUsage = 0x68; // HID usage of F13
        /// <summary>
        /// Injected trigger presses are ignored (so a macro can never trigger another), except those
        /// carrying this tag in dwExtraInfo: lets tests and tools simulate a mouse button press.
        /// </summary>
        public static readonly IntPtr SimulatedTag = (IntPtr)0x42474D43; // "BGMC"

        readonly object sync = new object();
        List<SoftMacro> macros = new List<SoftMacro>();
        readonly Dictionary<int, Runner> running = new Dictionary<int, Runner>();
        IntPtr hook = IntPtr.Zero;
        readonly LowLevelProc proc; // keep a reference so the GC doesn't collect the callback

        static string FilePath { get { return Path.Combine(AppInfo.DataDir, "software-macros.json"); } }

        public MacroEngine()
        {
            proc = HookProc;
            Load();
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Log.Info("macro hook failed: " + Marshal.GetLastWin32Error());
        }

        /// <summary>The macro list as the UI stores it (JSON array).</summary>
        public string Json { get; private set; }

        public void Save(string json)
        {
            var parsed = SoftMacro.ParseAll(json);
            lock (sync) { macros = parsed; Json = json; }
            try { File.WriteAllText(FilePath, json); } catch (Exception ex) { Log.Error("save macros", ex); }
        }

        void Load()
        {
            string json = "[]";
            try { if (File.Exists(FilePath)) json = File.ReadAllText(FilePath); } catch (Exception ex) { Log.Error("load macros", ex); }
            try { macros = SoftMacro.ParseAll(json); Json = json; }
            catch (Exception ex) { Log.Error("parse macros", ex); macros = new List<SoftMacro>(); Json = "[]"; }
        }

        /// <summary>Plays a macro once from the UI ("test" button).</summary>
        public void Test(int id)
        {
            SoftMacro m;
            lock (sync) m = macros.FirstOrDefault(x => x.Id == id);
            if (m != null) new Thread(() => { Thread.Sleep(2000); Play(m, () => false); }) { IsBackground = true }.Start();
        }

        // ---- Trigger handling ---------------------------------------------------------------

        IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool injected = (k.flags & LLKHF_INJECTED) != 0 && k.dwExtraInfo != SimulatedTag;
                int slot = (int)k.vkCode - FirstTriggerVk;
                if (!injected && slot >= 0 && slot < TriggerCount)
                {
                    SoftMacro m;
                    lock (sync) m = macros.FirstOrDefault(x => x.Trigger == slot);
                    if (m != null)
                    {
                        int msg = wParam.ToInt32();
                        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                        OnTrigger(m, down);
                        return (IntPtr)1; // swallow: the game never sees F13-F24
                    }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        void OnTrigger(SoftMacro m, bool down)
        {
            lock (sync)
            {
                Runner r;
                running.TryGetValue(m.Id, out r);
                bool active = r != null && r.Thread.IsAlive;

                if (!down)
                {
                    if (active && m.Mode == "hold") r.Stop = true;
                    if (r != null) r.KeyHeld = false;
                    return;
                }
                if (r != null && r.KeyHeld) return; // keyboard auto-repeat of the trigger
                if (active)
                {
                    if (m.Mode == "toggle") r.Stop = true;
                    if (r != null) r.KeyHeld = true;
                    return;
                }

                var runner = new Runner { KeyHeld = true };
                runner.Thread = new Thread(() =>
                {
                    int loops = m.Mode == "repeat" ? Math.Max(1, m.RepeatCount) : 1;
                    bool forever = m.Mode == "hold" || m.Mode == "toggle";
                    for (int i = 0; forever || i < loops; i++)
                    {
                        if (runner.Stop) break;
                        Play(m, () => runner.Stop);
                    }
                }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Macro " + m.Name };
                running[m.Id] = runner;
                runner.Thread.Start();
            }
        }

        sealed class Runner
        {
            public Thread Thread;
            public volatile bool Stop;
            public volatile bool KeyHeld;
        }

        // ---- Playback -----------------------------------------------------------------------

        [ThreadStatic] static Random rng;

        /// <summary>A delay with the macro's random jitter applied (never negative).</summary>
        static long Vary(int ms, int jitter)
        {
            if (jitter <= 0) return ms;
            if (rng == null) rng = new Random(Guid.NewGuid().GetHashCode());
            return Math.Max(0, ms + rng.Next(-jitter, jitter + 1));
        }

        static void Play(SoftMacro m, Func<bool> stop)
        {
            var held = new HashSet<int>();   // keys currently down, released on stop
            int mouseMask = 0;
            var clock = Stopwatch.StartNew();
            long due = 0;
            var lastDown = new Dictionary<int, long>();

            foreach (var ev in m.Events)
            {
                due += ev.Delay > 0 ? Vary(ev.Delay, m.JitterMs) : 0;
                // Keep every press down for at least MinHoldMs (keys use their usage as key, mouse buttons -bit).
                if (m.MinHoldMs > 0)
                {
                    var released = new List<int>();
                    if (ev.Kind == SoftEvent.Key && ev.IsUp) released.Add(ev.Code);
                    if (ev.Kind == SoftEvent.Mouse)
                        foreach (int bit in new[] { 1, 2, 4, 8, 16 })
                            if ((mouseMask & bit) != 0 && (ev.Code & bit) == 0) released.Add(-bit);
                    foreach (int k in released)
                    {
                        long downAt;
                        if (lastDown.TryGetValue(k, out downAt)) due = Math.Max(due, downAt + m.MinHoldMs);
                    }
                }
                if (!WaitUntil(clock, due, stop)) break;

                switch (ev.Kind)
                {
                    case SoftEvent.Key:
                        SendKey(ev.Code, !ev.IsUp);
                        if (ev.IsUp) held.Remove(ev.Code); else { held.Add(ev.Code); lastDown[ev.Code] = clock.ElapsedMilliseconds; }
                        break;
                    case SoftEvent.Mouse:
                        int changed = mouseMask ^ ev.Code;
                        foreach (int bit in new[] { 1, 2, 4, 8, 16 })
                            if ((changed & bit) != 0)
                            {
                                bool d = (ev.Code & bit) != 0;
                                SendMouseButton(bit, d);
                                if (d) lastDown[-bit] = clock.ElapsedMilliseconds;
                            }
                        mouseMask = ev.Code;
                        break;
                    case SoftEvent.Wheel:
                        SendWheel(ev.Code);
                        break;
                }
            }
            if (m.GapMs > 0) WaitUntil(clock, due + Vary(m.GapMs, m.JitterMs), stop);
            // Never leave anything stuck down, even when stopped mid-way.
            foreach (int usage in held) SendKey(usage, false);
            foreach (int bit in new[] { 1, 2, 4, 8, 16 }) if ((mouseMask & bit) != 0) SendMouseButton(bit, false);
        }

        static bool WaitUntil(Stopwatch clock, long dueMs, Func<bool> stop)
        {
            while (true)
            {
                if (stop()) return false;
                long left = dueMs - clock.ElapsedMilliseconds;
                if (left <= 0) return true;
                if (left > 15) Thread.Sleep((int)Math.Min(left - 10, 50));
                else Thread.SpinWait(2000); // last few ms: spin for accuracy
            }
        }

        static void SendKey(int usage, bool down)
        {
            int sc; bool ext;
            if (!Scancodes.TryGet(usage, out sc, out ext)) return;
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wScan = (ushort)sc;
            input.u.ki.dwFlags = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP) | (ext ? KEYEVENTF_EXTENDEDKEY : 0);
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        static void SendMouseButton(int bit, bool down)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            switch (bit)
            {
                case 1: input.u.mi.dwFlags = down ? 0x0002u : 0x0004u; break; // left
                case 2: input.u.mi.dwFlags = down ? 0x0008u : 0x0010u; break; // right
                case 4: input.u.mi.dwFlags = down ? 0x0020u : 0x0040u; break; // middle
                case 8: input.u.mi.dwFlags = down ? 0x0080u : 0x0100u; input.u.mi.mouseData = 1; break; // XBUTTON1 (back)
                case 16: input.u.mi.dwFlags = down ? 0x0080u : 0x0100u; input.u.mi.mouseData = 2; break; // XBUTTON2 (forward)
            }
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        static void SendWheel(int notches)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = 0x0800; // MOUSEEVENTF_WHEEL
            input.u.mi.mouseData = (uint)(notches * 120);
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public void Dispose()
        {
            lock (sync) foreach (var r in running.Values) r.Stop = true;
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        // ---- Win32 --------------------------------------------------------------------------

        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        const uint LLKHF_INJECTED = 0x10;
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;

        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    }
}
