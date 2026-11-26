using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AlwaysOnKeys
{
    static class Log
    {
        public static string Path;
        static readonly object gate = new object();
        public static void Write(string msg)
        {
            if (Path == null) return;
            try
            {
                lock (gate)
                {
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// Tiny INI reader/writer. Keys are stored as "Section.Key".
    sealed class Config
    {
        public readonly string Path;
        readonly Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Config(string path) { Path = path; }

        public void Load()
        {
            v.Clear();
            if (!File.Exists(Path)) return;
            string section = "";
            foreach (string raw in File.ReadAllLines(Path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string val = line.Substring(eq + 1);
                int sc = val.IndexOf(" ;"); if (sc >= 0) val = val.Substring(0, sc);
                v[(section.Length > 0 ? section + "." : "") + line.Substring(0, eq).Trim()] = val.Trim();
            }
        }

        public string Get(string key, string def) { string s; return v.TryGetValue(key, out s) && s.Length > 0 ? s : def; }
        public int GetInt(string key, int def) { int i; return int.TryParse(Get(key, ""), out i) ? i : def; }
        public void Set(string key, string val) { v[key] = val; }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; AlwaysOn Keys settings. Edit, save, then tray menu > Reload settings.");
            sb.AppendLine("; (Instrument and mode are also saved here automatically.)");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("; Instrument number (1 = first in the tray menu list)");
            sb.AppendLine("Instrument=" + Get("General.Instrument", "1"));
            sb.AppendLine("; Auto = FFXIV keys while the game window is focused, local sound otherwise");
            sb.AppendLine("; Synth = always local sound,  Ffxiv = always send game keys");
            sb.AppendLine("Mode=" + Get("General.Mode", "Auto"));
            sb.AppendLine("; Part of the MIDI device name to connect to (blank = first MIDI input found)");
            sb.AppendLine("Device=" + Get("General.Device", "MPK"));
            sb.AppendLine("; Audio queued ahead, in milliseconds (never less than 2 engine periods).");
            sb.AppendLine("; Lower = snappier. Raise it (10, 15, 20) only if you hear crackles.");
            sb.AppendLine("BufferMs=" + Get("General.BufferMs", "6"));
            sb.AppendLine("; 1 = ask Windows for its low-latency audio engine period (Windows 10+), 0 = standard 10 ms");
            sb.AppendLine("LowLatency=" + Get("General.LowLatency", "1"));
            sb.AppendLine("; Keep the audio stream running this many seconds after the last note, so the next");
            sb.AppendLine("; note starts instantly. After that the app fully sleeps (0% CPU).");
            sb.AppendLine("KeepWarmSeconds=" + Get("General.KeepWarmSeconds", "30"));
            sb.AppendLine("; Pitch bend range of the joystick in semitones (local sound)");
            sb.AppendLine("BendRange=" + Get("General.BendRange", "2"));
            sb.AppendLine("; CC numbers of the knobs for Volume, Tone, Reverb, Release, Compressor (MPK mini 3: K1-K5 = 70-74)");
            sb.AppendLine("Knobs=" + Get("General.Knobs", "70,71,72,73,74"));
            sb.AppendLine("; Where those knobs were last left (0-127, blank = default). Saved automatically.");
            sb.AppendLine("KnobValues=" + Get("General.KnobValues", ""));
            sb.AppendLine("; MIDI channel the pads send on (they play drums in local mode and are ignored in FFXIV mode)");
            sb.AppendLine("DrumChannel=" + Get("General.DrumChannel", "10"));
            sb.AppendLine();
            sb.AppendLine("[Ffxiv]");
            sb.AppendLine("; Must match your in-game keybinds (Character Config / Keybind > Performance).");
            sb.AppendLine("; Layout=octave : one octave of note keys + the game's -1/+1 Octave modifier keys (game default)");
            sb.AppendLine("; Layout=full   : every one of the 37 notes (C3..C6) has its own key, listed in FullKeys");
            sb.AppendLine("Layout=" + Get("Ffxiv.Layout", "octave"));
            sb.AppendLine("; 13 keys for C C# D D# E F F# G G# A A# B C(+1)");
            sb.AppendLine("NoteKeys=" + Get("Ffxiv.NoteKeys", "Q,2,W,3,E,R,5,T,6,Y,7,U,I"));
            sb.AppendLine("OctaveUpKey=" + Get("Ffxiv.OctaveUpKey", "Shift"));
            sb.AppendLine("OctaveDownKey=" + Get("Ffxiv.OctaveDownKey", "Ctrl"));
            sb.AppendLine("; 37 comma-separated keys, lowest C to highest C. Combos like Ctrl+Q are allowed.");
            sb.AppendLine("FullKeys=" + Get("Ffxiv.FullKeys", ""));
            sb.AppendLine("; MIDI note that plays the game's middle C (60 = middle C on the keyboard)");
            sb.AppendLine("MiddleC=" + Get("Ffxiv.MiddleC", "60"));
            sb.AppendLine("; Joystick left/right: re-plays the held note up to this many semitones away");
            sb.AppendLine("BendRange=" + Get("Ffxiv.BendRange", "2"));
            sb.AppendLine("; Joystick up (modulation): trill to this many semitones above the held note");
            sb.AppendLine("TrillInterval=" + Get("Ffxiv.TrillInterval", "2"));
            sb.AppendLine("; Pause (ms) so the game notices a repeated note. Raise if fast repeats get dropped.");
            sb.AppendLine("RetriggerGapMs=" + Get("Ffxiv.RetriggerGapMs", "15"));
            sb.AppendLine("; Pad that shifts an octave while held (MIDI note on DrumChannel; pad 1 = 36 ... pad 8 = 43, -1 = none).");
            sb.AppendLine("; It shifts up when the keyboard has no Oct set, and down when Oct + is on.");
            sb.AppendLine("OctavePad=" + Get("Ffxiv.OctavePad", "36"));
            sb.AppendLine("; Pad that shifts two octaves the same way while held (wins over OctavePad if both are held).");
            sb.AppendLine("TwoOctavePad=" + Get("Ffxiv.TwoOctavePad", "37"));
            sb.AppendLine("; Time (ms) the octave key (Ctrl/Shift) goes down before its note key. If both land in the");
            sb.AppendLine("; same game frame, held notes in the low/high octave play staccato. Must be longer than one");
            sb.AppendLine("; frame: 25 covers 40+ FPS, use 40 if you play at 30 FPS.");
            sb.AppendLine("OctaveKeyLeadMs=" + Get("Ffxiv.OctaveKeyLeadMs", "25"));
            sb.AppendLine("; Chords are strummed upwards, this many ms between notes (0 = off, last key wins).");
            sb.AppendLine("; Keep it above one frame (17 ms at 60 FPS) or the game skips notes.");
            sb.AppendLine("StrumMs=" + Get("Ffxiv.StrumMs", "20"));
            File.WriteAllText(Path, sb.ToString());
        }
    }
}
