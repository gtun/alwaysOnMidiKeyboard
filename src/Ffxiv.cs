// FFXIV bard performance mode: turns MIDI notes into the game's performance
// keybinds via SendInput (scan codes, so the game sees real key presses).
//
//  * The game plays one note at a time. Chords are strummed: the held notes are
//    played lowest to highest, one game frame or so apart, so they ring together
//    on plucked instruments. The top note is the one left sounding.
//  * The MPK's Oct -/+ buttons transpose inside the keyboard (no MIDI sent), so
//    they simply move which game notes the keys reach, like in a DAW.
//  * Octave pads (hold): pad 1 shifts by an octave toward the game octave the keys
//    don't reach - up when the keyboard sits low (no Oct set), down when Oct + is
//    on. Pad 2 shifts two octaves the same way, so the lower half reaches the far
//    octave; it wins when both are held. Shifts past the game's range fold back.
//    The keyboard's octave isn't sent over MIDI, so it is worked out from the
//    note numbers: any note outside the overlap of two positions settles it.
//  * Octave modifier keys (Ctrl/Shift by default) go down a little before the note
//    key. If both land in the same game frame, the game treats the modifier as an
//    octave change and cuts the note it just started, so held notes go staccato.
//  * Pitch-bend joystick: the game has no real bend, so pushing it re-plays the
//    held note 1-2 semitones up/down (a "step bend"), and snaps back on release.
//  * Modulation (joystick up): trills the held note with the note above it,
//    faster the further you push.
//
// Nothing here sleeps: every delay is a deadline that Tick() acts on, and the
// router wakes up for it through MsUntilTick().
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        /// Test hook: when set, key events go here instead of to Windows.
        public static Action<Key, bool> Sink = null;

        public static void Send(Key k, bool down)
        {
            if (k.IsEmpty) return;
            if (Sink != null) { Sink(k, down); return; }
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
        public int PadChannel = 9;        // 0-based; pads are ignored in game mode
        public int OctavePad = 36;        // hold to shift an octave (-1 = none)
        public int TwoOctavePad = 37;     // hold to shift two octaves (-1 = none)
        const int KbdLow = 48, KbdSpan = 24; // MPK mini: 25 keys, C3-C5 with no Oct set
        public int BendRange = 2, TrillInterval = 2;
        public int GapMs = 15;            // same key again: up, wait, down
        public int LeadMs = 25;           // octave modifier change -> note key
        public int StrumMs = 20;          // between strummed chord notes (0 = no strum)
        public float TrillMinRate = 5, TrillMaxRate = 14;
        const int GatherMs = 10;          // wait this long for the rest of a chord's keys
        const long Never = long.MaxValue;

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly double usPerTick = 1e6 / Stopwatch.Frequency;
        static long Now() { return (long)(clock.ElapsedTicks * usPerTick); } // microseconds

        struct Pending { public int Midi; public bool Released; }
        readonly List<Pending> queue = new List<Pending>(); // chord notes waiting their turn
        long nextNoteAt;                  // the next queued note may start from here
        long holdUntil;                   // a note is kept down at least until here

        int curMidi = -1;                 // MIDI key currently sounding
        bool curReleased;                 // ...whose key is already up (short tap)
        int curBaseG;                     // game note for curMidi before bend/trill
        Combo soundingCombo;              // note key that is down
        Combo pressCombo; long pressAt = Never; // note key waiting for a gap
        readonly List<Key> modsDown = new List<Key>();
        int bendSemis;
        float mod;
        bool trillUp; long nextTrill = Never;
        bool padHeld, pad2Held;
        volatile int kbdOct;              // keyboard's Oct -/+ setting, inferred

        /// The keyboard's Oct setting as far as the notes played so far tell (0 = none).
        public int KeyboardOctave { get { return kbdOct; } }

        /// Called for every note-on, in any mode, to keep track of the keyboard's
        /// Oct setting. A note fits some range of positions; if the current guess
        /// is outside it, move the guess to the nearest one that fits.
        public void SeeNote(int ch, int note)
        {
            if (ch == PadChannel) return;
            int lo = (int)Math.Ceiling((note - KbdLow - KbdSpan) / 12.0);
            int hi = (int)Math.Floor((note - KbdLow) / 12.0);
            int k = kbdOct;
            if (k < lo) k = lo; else if (k > hi) k = hi;
            kbdOct = k;
        }

        public void Configure(Config cfg)
        {
            MiddleC = cfg.GetInt("Ffxiv.MiddleC", 60);
            PadChannel = cfg.GetInt("General.DrumChannel", 10) - 1;
            BendRange = Math.Max(0, Math.Min(12, cfg.GetInt("Ffxiv.BendRange", 2)));
            TrillInterval = cfg.GetInt("Ffxiv.TrillInterval", 2);
            GapMs = Math.Max(0, cfg.GetInt("Ffxiv.RetriggerGapMs", 15));
            LeadMs = Math.Max(0, cfg.GetInt("Ffxiv.OctaveKeyLeadMs", 25));
            StrumMs = Math.Max(0, Math.Min(200, cfg.GetInt("Ffxiv.StrumMs", 20)));
            OctavePad = cfg.GetInt("Ffxiv.OctavePad", 36);
            TwoOctavePad = cfg.GetInt("Ffxiv.TwoOctavePad", 37);

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
            if (ch == PadChannel) // other pads do nothing
            {
                if (note == OctavePad) padHeld = true;
                if (note == TwoOctavePad) pad2Held = true;
                return;
            }
            long now = Now();
            if (StrumMs == 0) { StartNote(note, false, now); return; }
            // first key of a new chord: give its other keys a moment to arrive
            if (queue.Count == 0 && pressCombo == null && now >= nextNoteAt) nextNoteAt = now + GatherMs * 1000L;
            queue.Add(new Pending { Midi = note });
        }

        public void NoteOff(int ch, int note)
        {
            if (ch == PadChannel)
            {
                if (note == OctavePad) padHeld = false;
                if (note == TwoOctavePad) pad2Held = false;
                return;
            }
            for (int i = 0; i < queue.Count; i++)
                if (queue[i].Midi == note) { queue[i] = new Pending { Midi = note, Released = true }; return; }
            if (note != curMidi) return;
            // a very short tap still gets held long enough for the game to see it
            if (pressCombo != null || Now() < holdUntil || queue.Count > 0) { curReleased = true; return; }
            StopNote();
        }

        void StartNote(int note, bool released, long now)
        {
            curMidi = note; curReleased = released;
            curBaseG = note - (MiddleC - 12) + PadShift();
            trillUp = false;
            Play(Fold(curBaseG + bendSemis), now);
            nextTrill = mod > 0.08f ? now + TrillPeriodUs() : Never;
        }

        /// Holding an octave pad moves toward the octave the keys can't reach:
        /// pad 2 by two octaves (it wins if both are held), pad 1 by one.
        int PadShift()
        {
            int octs = pad2Held ? 2 : padHeld ? 1 : 0;
            return (kbdOct >= 1 ? -12 : 12) * octs;
        }

        void StopNote()
        {
            curMidi = -1; curReleased = false; nextTrill = Never;
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
            if (curMidi >= 0) Play(Fold(curBaseG + bendSemis + (trillUp ? TrillInterval : 0)), Now());
        }

        public void Mod(int val)
        {
            mod = val / 127f;
            if (mod > 0.08f) { if (curMidi >= 0 && nextTrill == Never) nextTrill = Now(); }
            else if (nextTrill != Never)
            {
                nextTrill = Never;
                if (trillUp && curMidi >= 0) { trillUp = false; Play(Fold(curBaseG + bendSemis), Now()); }
            }
        }

        long TrillPeriodUs() { return (long)(1e6f / (TrillMinRate + (TrillMaxRate - TrillMinRate) * Math.Min(1f, (mod - 0.08f) / 0.9f))); }

        /// The next deadline Tick() has to act on, or Never.
        long NextDeadline()
        {
            if (pressCombo != null) return pressAt;
            long t = Never;
            if (queue.Count > 0) t = nextNoteAt;
            else if (curReleased && curMidi >= 0) t = holdUntil;
            else if (curMidi >= 0) t = nextTrill;
            return t;
        }

        /// Milliseconds until Tick() wants to run, or -1 for "nothing scheduled".
        public int MsUntilTick()
        {
            long t = NextDeadline();
            if (t == Never) return -1;
            long us = t - Now();
            return us <= 0 ? 0 : (int)Math.Min(int.MaxValue, (us + 999) / 1000);
        }

        public void Tick()
        {
            long now = Now();
            if (pressCombo != null)
            {
                if (now < pressAt) return;
                Press(pressCombo, now);
            }
            if (queue.Count > 0)
            {
                if (now < nextNoteAt) return;
                int lo = 0; // strum upwards: lowest waiting note first
                for (int i = 1; i < queue.Count; i++) if (queue[i].Midi < queue[lo].Midi) lo = i;
                Pending p = queue[lo]; queue.RemoveAt(lo);
                StartNote(p.Midi, p.Released, now);
                return;
            }
            if (curMidi < 0) return;
            if (curReleased) { if (now >= holdUntil) StopNote(); return; }
            if (nextTrill == Never || now < nextTrill) return;
            trillUp = !trillUp;
            Play(Fold(curBaseG + bendSemis + (trillUp ? TrillInterval : 0)), now);
            nextTrill = now + TrillPeriodUs();
        }

        static int Fold(int g)
        {
            while (g < 0) g += 12;
            while (g > 36) g -= 12;
            return g;
        }

        void Play(int g, long now)
        {
            Combo c = map[g];
            bool retrigger = soundingCombo != null && soundingCombo.Main.Same(c.Main);
            if (soundingCombo != null) { KeyInput.Send(soundingCombo.Main, false); soundingCombo = null; }
            pressCombo = null; pressAt = Never;
            bool modChanged = false;
            // release modifiers the new note doesn't use
            for (int i = modsDown.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                foreach (Key k in c.Mods) if (k.Same(modsDown[i])) keep = true;
                if (!keep) { KeyInput.Send(modsDown[i], false); modsDown.RemoveAt(i); modChanged = true; }
            }
            foreach (Key k in c.Mods)
            {
                bool down = false;
                foreach (Key d in modsDown) if (d.Same(k)) down = true;
                if (!down) { KeyInput.Send(k, true); modsDown.Add(k); modChanged = true; }
            }
            // the game samples keys once per frame: a repeated key needs to be seen
            // up, and a modifier change must be seen before the note key goes down
            int gap = Math.Max(retrigger ? GapMs : 0, modChanged ? LeadMs : 0);
            if (gap > 0) { pressCombo = c; pressAt = now + gap * 1000L; }
            else Press(c, now);
        }

        void Press(Combo c, long now)
        {
            KeyInput.Send(c.Main, true);
            soundingCombo = c;
            pressCombo = null; pressAt = Never;
            nextNoteAt = now + StrumMs * 1000L;
            holdUntil = now + Math.Max(StrumMs, GapMs) * 1000L;
        }

        void ReleaseSounding()
        {
            if (soundingCombo != null) KeyInput.Send(soundingCombo.Main, false);
            for (int i = modsDown.Count - 1; i >= 0; i--) KeyInput.Send(modsDown[i], false);
            modsDown.Clear();
            soundingCombo = null; pressCombo = null; pressAt = Never;
        }

        public void Panic()
        {
            queue.Clear();
            trillUp = false; padHeld = pad2Held = false;
            StopNote();
        }
    }
}
