// FFXIV bard performance mode: turns MIDI notes into the game's performance
// keybinds via SendInput (scan codes, so the game sees real key presses).
//
//  * Monophonic, last-note priority (the game plays one note at a time).
//  * Hold-to-shift octave: two MPK pads act like the game's "-1 / +1 Octave"
//    keys - the shift only lasts while the pad is held.
//  * Pitch-bend joystick: the game has no real bend, so pushing it re-plays the
//    held note 1-2 semitones up/down (a "step bend"), and snaps back on release.
//  * Modulation (joystick up): trills the held note with the note above it,
//    faster the further you push.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace AlwaysOnKeys
{
    struct Key
    {
        public ushort Scan; public bool Ext;
        public bool IsEmpty { get { return Scan == 0; } }
        public bool Same(Key o) { return Scan == o.Scan && Ext == o.Ext; }
    }

    sealed class Combo
    {
        public Key[] Mods = new Key[0];
        public Key Main;
    }

    static class KeyInput
    {
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public ushort wVk;
            [FieldOffset(10)] public ushort wScan;
            [FieldOffset(12)] public uint dwFlags;
            [FieldOffset(16)] public uint time;
            [FieldOffset(24)] public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScan(char ch);

        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
        static readonly INPUT[] one = new INPUT[1];

        public static void Send(Key k, bool down)
        {
            if (k.IsEmpty) return;
            one[0].type = 1;
            one[0].wVk = 0;
            one[0].wScan = k.Scan;
            one[0].dwFlags = KEYEVENTF_SCANCODE | (k.Ext ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
            one[0].time = 0;
            one[0].dwExtraInfo = IntPtr.Zero;
            SendInput(1, one, 40);
        }

        static readonly Dictionary<string, int> named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {"Shift",0x10A0},{"LShift",0x10A0},{"RShift",0x10A1},{"Ctrl",0x10A2},{"Control",0x10A2},{"LCtrl",0x10A2},{"RCtrl",0x30A3},
            {"Alt",0x10A4},{"LAlt",0x10A4},{"RAlt",0x30A5},{"Space",0x1020},{"Tab",0x1009},{"Enter",0x100D},
            {"Up",0x3026},{"Down",0x3028},{"Left",0x3025},{"Right",0x3027},{"Insert",0x302D},{"Delete",0x302E},
            {"Home",0x3024},{"End",0x3023},{"PageUp",0x3021},{"PageDown",0x3022},
            {"NumMultiply",0x106A},{"NumAdd",0x106B},{"NumSubtract",0x106D},{"NumDecimal",0x106E},{"NumDivide",0x306F},
        };
        static readonly Dictionary<string, char> punct = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            {"Comma",','},{"Period",'.'},{"Slash",'/'},{"Semicolon",';'},{"Quote",'\''},{"LBracket",'['},{"RBracket",']'},
            {"Minus",'-'},{"Equals",'='},{"Backslash",'\\'},{"Backtick",'`'},
        };

        /// Parses e.g. "Q", "2", "Shift", "F5", "Num3", "Ctrl+Q", "Comma".
        public static Combo ParseCombo(string spec)
        {
            var c = new Combo();
            var parts = spec.Split('+');
            var mods = new List<Key>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) { if (i == parts.Length - 1 && spec.EndsWith("+")) p = "+"; else continue; }
                Key k = ParseKey(p);
                if (k.IsEmpty) throw new FormatException("unknown key '" + p + "'");
                if (i < parts.Length - 1) mods.Add(k); else c.Main = k;
            }
            c.Mods = mods.ToArray();
            return c;
        }

        public static Key ParseKey(string p)
        {
            int vk = 0; bool ext = false;
            int nv;
            if (named.TryGetValue(p, out nv)) { vk = nv & 0xFF; ext = (nv & 0x2000) != 0; }
            else if (p.Length > 1 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p.Substring(1), out nv) && nv >= 1 && nv <= 24) vk = 0x6F + nv;
            else if (p.Length == 4 && p.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(p[3])) vk = 0x60 + (p[3] - '0');
            else
            {
                char ch;
                if (p.Length == 1) ch = p[0];
                else if (!punct.TryGetValue(p, out ch)) return new Key();
                short r = VkKeyScan(char.ToLowerInvariant(ch));
                if (r == -1) return new Key();
                vk = r & 0xFF;
            }
            uint scan = MapVirtualKey((uint)vk, 0 /*MAPVK_VK_TO_VSC*/);
            if (vk == 0xA1) scan = 0x36; // right shift
            return new Key { Scan = (ushort)scan, Ext = ext };
        }
    }

    sealed class FfxivPlayer
    {
        // 37 game notes: index 0 = lowest C (the game's "-1 octave" C) ... 36 = top C
        Combo[] map = new Combo[37];
        public int MiddleC = 60;          // MIDI note that plays the game's middle C
        public int PadChannel = 9;        // 0-based (MIDI channel 10)
        public int OctaveDownPad = 36, OctaveUpPad = 37;
        public int BendRange = 2, TrillInterval = 2, GapMs = 15, ModGapMs = 0;
        public float TrillMinRate = 5, TrillMaxRate = 14;

        bool downHeld, upHeld;
        int curMidi = -1;             // MIDI key currently sounding
        int curBaseG;                 // game note for curMidi before bend/trill
        Combo soundingCombo;
        readonly List<Key> modsDown = new List<Key>();
        int bendSemis;
        float mod;
        bool trillUp; int nextTrill = -1;

        public void Configure(Config cfg)
        {
            MiddleC = cfg.GetInt("Ffxiv.MiddleC", 60);
            PadChannel = cfg.GetInt("Ffxiv.PadChannel", 10) - 1;
            OctaveDownPad = cfg.GetInt("Ffxiv.OctaveDownPad", 36);
            OctaveUpPad = cfg.GetInt("Ffxiv.OctaveUpPad", 37);
            BendRange = Math.Max(0, Math.Min(12, cfg.GetInt("Ffxiv.BendRange", 2)));
            TrillInterval = cfg.GetInt("Ffxiv.TrillInterval", 2);
            GapMs = Math.Max(0, cfg.GetInt("Ffxiv.RetriggerGapMs", 15));
            ModGapMs = Math.Max(0, cfg.GetInt("Ffxiv.ModifierGapMs", 0));

            var m = new Combo[37];
            string layout = cfg.Get("Ffxiv.Layout", "octave");
            if (layout.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                string[] k = cfg.Get("Ffxiv.FullKeys", "").Split(',');
                if (k.Length != 37) throw new FormatException("Ffxiv.FullKeys needs 37 keys (C3..C6), found " + k.Length);
                for (int i = 0; i < 37; i++) m[i] = KeyInput.ParseCombo(k[i].Trim());
            }
            else
            {
                string[] k = cfg.Get("Ffxiv.NoteKeys", "Q,2,W,3,E,R,5,T,6,Y,7,U,I").Split(',');
                if (k.Length != 13) throw new FormatException("Ffxiv.NoteKeys needs 13 keys (C..C), found " + k.Length);
                var notes = new Combo[13];
                for (int i = 0; i < 13; i++) notes[i] = KeyInput.ParseCombo(k[i].Trim());
                Key up = KeyInput.ParseKey(cfg.Get("Ffxiv.OctaveUpKey", "Shift").Trim());
                Key dn = KeyInput.ParseKey(cfg.Get("Ffxiv.OctaveDownKey", "Ctrl").Trim());
                if (up.IsEmpty || dn.IsEmpty) throw new FormatException("bad Ffxiv.OctaveUpKey / OctaveDownKey");
                for (int g = 0; g < 37; g++)
                {
                    Combo src; Key? extra = null;
                    if (g < 12) { src = notes[g]; extra = dn; }
                    else if (g <= 24) src = notes[g - 12];
                    else { src = notes[g - 24]; extra = up; }
                    var c = new Combo { Main = src.Main };
                    var mods = new List<Key>(src.Mods);
                    if (extra.HasValue) mods.Insert(0, extra.Value);
                    c.Mods = mods.ToArray();
                    m[g] = c;
                }
            }
            map = m;
        }

        public void NoteOn(int ch, int note)
        {
            if (ch == PadChannel)
            {
                if (note == OctaveDownPad) downHeld = true;
                else if (note == OctaveUpPad) upHeld = true;
                return; // other pads do nothing in game mode
            }
            curMidi = note;
            curBaseG = note - (MiddleC - 12) + (upHeld ? 12 : 0) - (downHeld ? 12 : 0);
            trillUp = false;
            Play(Fold(curBaseG + bendSemis));
            nextTrill = mod > 0.08f ? Environment.TickCount + TrillPeriod() : -1;
        }

        public void NoteOff(int ch, int note)
        {
            if (ch == PadChannel)
            {
                if (note == OctaveDownPad) downHeld = false;
                else if (note == OctaveUpPad) upHeld = false;
                return;
            }
            if (note != curMidi) return;
            curMidi = -1; nextTrill = -1;
            ReleaseSounding();
        }

        public void Bend(int value14)
        {
            float b = (value14 - 8192) / 8192f;
            // hysteresis so the joystick resting slightly off-centre doesn't flicker
            float target = b * BendRange;
            int s = bendSemis;
            if (target > s + 0.65f) s = (int)Math.Floor(target + 0.35f);
            else if (target < s - 0.65f) s = (int)Math.Ceiling(target - 0.35f);
            if (Math.Abs(b) < 0.08f) s = 0;
            if (s == bendSemis) return;
            bendSemis = s;
            if (curMidi >= 0) Play(Fold(curBaseG + bendSemis + (trillUp ? TrillInterval : 0)));
        }

        public void Mod(int val)
        {
            mod = val / 127f;
            if (mod > 0.08f) { if (curMidi >= 0 && nextTrill < 0) nextTrill = Environment.TickCount; }
            else if (nextTrill >= 0)
            {
                nextTrill = -1;
                if (trillUp && curMidi >= 0) { trillUp = false; Play(Fold(curBaseG + bendSemis)); }
            }
        }

        int TrillPeriod() { return (int)(1000f / (TrillMinRate + (TrillMaxRate - TrillMinRate) * Math.Min(1f, (mod - 0.08f) / 0.9f))); }

        /// Milliseconds until Tick() wants to run, or -1 for "nothing scheduled".
        public int MsUntilTick()
        {
            if (nextTrill < 0) return -1;
            return Math.Max(0, nextTrill - Environment.TickCount);
        }

        public void Tick()
        {
            if (nextTrill < 0 || curMidi < 0) return;
            if (Environment.TickCount - nextTrill < 0) return;
            trillUp = !trillUp;
            Play(Fold(curBaseG + bendSemis + (trillUp ? TrillInterval : 0)));
            nextTrill = Environment.TickCount + TrillPeriod();
        }

        static int Fold(int g)
        {
            while (g < 0) g += 12;
            while (g > 36) g -= 12;
            return g;
        }

        void Play(int g)
        {
            Combo c = map[g];
            bool retrigger = soundingCombo != null && soundingCombo.Main.Same(c.Main);
            if (soundingCombo != null) KeyInput.Send(soundingCombo.Main, false);
            // release modifiers the new note doesn't use
            for (int i = modsDown.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                foreach (Key k in c.Mods) if (k.Same(modsDown[i])) keep = true;
                if (!keep) { KeyInput.Send(modsDown[i], false); modsDown.RemoveAt(i); }
            }
            bool modChanged = false;
            foreach (Key k in c.Mods)
            {
                bool down = false;
                foreach (Key d in modsDown) if (d.Same(k)) down = true;
                if (!down) { KeyInput.Send(k, true); modsDown.Add(k); modChanged = true; }
            }
            // the game samples keys once per frame; give it a moment to notice a
            // release (same key again) or a new modifier before the note key goes down
            int gap = Math.Max(retrigger ? GapMs : 0, modChanged ? ModGapMs : 0);
            if (gap > 0) Thread.Sleep(gap);
            KeyInput.Send(c.Main, true);
            soundingCombo = c;
        }

        void ReleaseSounding()
        {
            if (soundingCombo != null) KeyInput.Send(soundingCombo.Main, false);
            for (int i = modsDown.Count - 1; i >= 0; i--) KeyInput.Send(modsDown[i], false);
            modsDown.Clear();
            soundingCombo = null;
        }

        public void Panic()
        {
            curMidi = -1; nextTrill = -1; downHeld = upHeld = false; trillUp = false;
            ReleaseSounding();
        }
    }
}
