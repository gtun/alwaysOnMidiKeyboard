// Minimal winmm MIDI input. The driver calls us only when the keyboard sends
// something, so there is no polling while you're not playing.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AlwaysOnKeys
{
    sealed class MidiIn
    {
        delegate void MidiInProc(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MIDIINCAPS
        {
            public ushort wMid, wPid; public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint midiInGetDevCapsW(UIntPtr id, ref MIDIINCAPS caps, uint size);
        [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr h, uint id, MidiInProc cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr h);

        const uint CALLBACK_FUNCTION = 0x30000, MIM_DATA = 0x3C3;

        readonly MidiInProc proc; // kept alive for the driver
        readonly Action<uint> sink;
        IntPtr handle = IntPtr.Zero;
        public string OpenName;
        public bool IsOpen { get { return handle != IntPtr.Zero; } }

        public MidiIn(Action<uint> sink)
        {
            this.sink = sink;
            proc = Callback;
        }

        void Callback(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            if (msg == MIM_DATA) sink((uint)(p1.ToInt64() & 0xFFFFFF));
        }

        public static List<string> Devices()
        {
            var list = new List<string>();
            uint n = midiInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new MIDIINCAPS();
                if (midiInGetDevCapsW(new UIntPtr(i), ref caps, (uint)Marshal.SizeOf(typeof(MIDIINCAPS))) == 0) list.Add(caps.szPname ?? "");
                else list.Add("");
            }
            return list;
        }

        /// Opens the first device whose name contains `match` (or any device if match is empty).
        /// Returns null on success, otherwise a short reason.
        public string TryOpen(string match)
        {
            if (IsOpen) return null;
            var devs = Devices();
            int pick = -1;
            for (int i = 0; i < devs.Count; i++)
                if (match.Length == 0 || devs[i].IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0) { pick = i; break; }
            if (pick < 0) return devs.Count == 0 ? "no MIDI keyboard found" : "keyboard not found";
            IntPtr h;
            uint r = midiInOpen(out h, (uint)pick, proc, IntPtr.Zero, CALLBACK_FUNCTION);
            if (r != 0) return "keyboard busy (another app is using it)";
            midiInStart(h);
            handle = h; OpenName = devs[pick];
            return null;
        }

        /// True if the opened device is still plugged in.
        public bool StillPresent()
        {
            if (!IsOpen) return false;
            return Devices().Contains(OpenName);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IntPtr h = handle; handle = IntPtr.Zero;
            try { midiInStop(h); midiInReset(h); midiInClose(h); } catch { }
            OpenName = null;
        }
    }
}
