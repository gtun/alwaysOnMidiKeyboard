using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("AlwaysOn Keys")]
[assembly: AssemblyProduct("AlwaysOn Keys")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace AlwaysOnKeys
{
    enum Mode { Auto, Synth, Ffxiv }

    static class Program
    {
        public static string Dir;

        [STAThread]
        static int Main(string[] args)
        {
            Dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            Log.Path = System.IO.Path.Combine(Dir, "AlwaysOnKeys.log");
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args);

            bool created;
            using (var m = new Mutex(true, "AlwaysOnKeys_SingleInstance", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new TrayApp()); }
                catch (Exception e) { Log.Write("fatal: " + e); MessageBox.Show(e.Message, "AlwaysOn Keys"); }
            }
            return 0;
        }
    }

    /// Moves MIDI events off the driver callback thread and sends them to the
    /// synth or to FFXIV. Sleeps on an event when nothing is happening.
    sealed class Router
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWaitableTimerExW(IntPtr attrs, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetWaitableTimer(SafeWaitHandle h, ref long due, int period, IntPtr cb, IntPtr arg, bool resume);

        /// Wraps a kernel timer so it can be waited on together with the event.
        sealed class TimerHandle : WaitHandle
        {
            public TimerHandle(IntPtr h) { SafeWaitHandle = new SafeWaitHandle(h, true); }
        }

        public const uint CmdPanic = 0x80000001;
        readonly ConcurrentQueue<uint> q = new ConcurrentQueue<uint>();
        readonly AutoResetEvent ev = new AutoResetEvent(false);
        readonly Synth synth; readonly FfxivPlayer ffx;
        readonly byte[] route = new byte[16 * 128]; // 0 none, 1 synth, 2 ffxiv
        volatile bool quit;
        public volatile Mode Mode = Mode.Auto;
        public Action<int> ProgramChanged;
        public volatile int LastNote = -1, LastCC = -1, LastCCValue;
        int loggedCC = -1;
        IntPtr lastHwnd = IntPtr.Zero; bool lastIsGame;
        Thread th;
        WaitHandle[] waits; // { ev, high-resolution timer }, or null if unavailable

        public Router(Synth s, FfxivPlayer f) { synth = s; ffx = f; }

        public void Start() { th = new Thread(Loop) { IsBackground = true, Name = "router", Priority = ThreadPriority.AboveNormal }; th.Start(); }
        public void Post(uint msg) { q.Enqueue(msg); ev.Set(); }
        public void Stop() { quit = true; ev.Set(); if (th != null) th.Join(500); }

        void Loop()
        {
            // Normal waits round up to the 15.6 ms system tick, too coarse for strums
            // and octave-key timing. A high-resolution timer (Windows 10 1803+) wakes
            // on time without raising the timer rate for the whole system.
            IntPtr hTimer = CreateWaitableTimerExW(IntPtr.Zero, null, 2 /*HIGH_RESOLUTION*/, 0x1F0003 /*TIMER_ALL_ACCESS*/);
            TimerHandle timer = hTimer == IntPtr.Zero ? null : new TimerHandle(hTimer);
            if (timer != null) waits = new WaitHandle[] { ev, timer };
            else Log.Write("router: no high-resolution timer, FFXIV timing uses the 15.6 ms system tick");
            while (!quit)
            {
                int wait = ffx.MsUntilTick();
                if (wait < 0) ev.WaitOne();
                else if (wait > 0)
                {
                    long due = -10000L * wait; // relative, 100 ns units
                    if (timer != null && SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                        WaitHandle.WaitAny(waits);
                    else ev.WaitOne(wait);
                }
                uint m;
                while (q.TryDequeue(out m))
                {
                    try { Dispatch(m); } catch (Exception e) { Log.Write("router: " + e.Message); }
                }
                try { ffx.Tick(); } catch (Exception e) { Log.Write("ffxiv tick: " + e.Message); }
            }
            ffx.Panic();
        }

        bool GameFocused()
        {
            IntPtr h = GetForegroundWindow();
            if (h == lastHwnd) return lastIsGame;
            lastHwnd = h; lastIsGame = false;
            if (h == IntPtr.Zero) return false;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            lastIsGame = sb.ToString() == "FFXIVGAME"; // the game window's class (cheap check, no process lookup)
            return lastIsGame;
        }

        void Dispatch(uint m)
        {
            if (m == CmdPanic)
            {
                ffx.Panic(); synth.AllOff(); Array.Clear(route, 0, route.Length); return;
            }
            int status = (int)(m & 0xFF), d1 = (int)((m >> 8) & 0x7F), d2 = (int)((m >> 16) & 0x7F);
            int type = status & 0xF0, ch = status & 0x0F;
            if (status >= 0xF0) return;
            if (type == 0x90 && d2 == 0) type = 0x80;
            Mode mode = Mode;
            switch (type)
            {
                case 0x90:
                {
                    LastNote = d1;
                    ffx.SeeNote(ch, d1);
                    bool game = mode == Mode.Ffxiv || (mode == Mode.Auto && GameFocused());
                    route[ch * 128 + d1] = (byte)(game ? 2 : 1);
                    if (game) ffx.NoteOn(ch, d1); else synth.NoteOn(ch, d1, d2);
                    break;
                }
                case 0x80:
                {
                    byte r = route[ch * 128 + d1];
                    route[ch * 128 + d1] = 0;
                    if (r == 2) ffx.NoteOff(ch, d1);
                    else synth.NoteOff(ch, d1);
                    break;
                }
                case 0xB0:
                    LastCC = d1; LastCCValue = d2;
                    if (d1 != loggedCC) { loggedCC = d1; Log.Write(string.Format("control: CC {0} = {1} (channel {2})", d1, d2, ch + 1)); }
                    synth.ControlChange(ch, d1, d2);
                    if (d1 == 1) ffx.Mod(d2);
                    break;
                case 0xE0:
                {
                    int v = d1 | (d2 << 7);
                    synth.PitchBend(ch, v);
                    ffx.Bend(v);
                    break;
                }
                case 0xC0:
                    if (ch != synth.DrumChannel && d1 < synth.Instruments.Length)
                    {
                        synth.SetInstrument(d1);
                        Action<int> a = ProgramChanged; if (a != null) a(d1);
                    }
                    break;
            }
        }
    }

    sealed class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        readonly Config cfg;
        readonly Synth synth = new Synth();
        readonly AudioOut audio;
        readonly FfxivPlayer ffx = new FfxivPlayer();
        readonly Router router;
        readonly MidiIn midi;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly SynchronizationContext ui;
        ToolStripMenuItem statusItem, audioItem, instrMenu, modeMenu, pauseItem, startupItem;
        bool paused; string midiProblem = "starting"; string ffxivError;
        IntPtr iconHandle = IntPtr.Zero; int iconState = -1;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            cfg = new Config(Path.Combine(Program.Dir, "AlwaysOnKeys.ini"));
            cfg.Load();
            if (!File.Exists(cfg.Path)) TrySave();
            audio = new AudioOut(synth);
            router = new Router(synth, ffx);
            midi = new MidiIn(router.Post);
            synth.Activity = audio.Wake;
            router.ProgramChanged = i => ui.Post(_ => { cfg.Set("General.Instrument", (i + 1).ToString()); TrySave(); RefreshMenu(); }, null);
            ApplyConfig();

            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(tray, null);
            };
            tray.Visible = true;

            audio.Start();
            router.Start();
            timer.Interval = 2500;
            timer.Tick += (s, e) =>
            {
                CheckMidi();
                if (synth.KnobsDirty)
                {
                    synth.KnobsDirty = false;
                    var parts = new string[5];
                    for (int i = 0; i < 5; i++) parts[i] = synth.KnobValues[i] < 0 ? "" : synth.KnobValues[i].ToString();
                    cfg.Set("General.KnobValues", string.Join(",", parts));
                    TrySave();
                }
            };
            timer.Start();
            CheckMidi();
            // compile the audio code paths now so the very first note isn't delayed
            try
            {
                var warm = new Synth(); var a = new float[256]; var b = new float[256];
                warm.NoteOn(0, 60, 100); warm.NoteOn(warm.DrumChannel, 36, 100); warm.Render(a, b, 256);
                warm.NoteOff(0, 60); warm.ControlChange(0, 1, 0); warm.PitchBend(0, 8192); warm.Render(a, b, 256);
            }
            catch { }
            GC.Collect();
        }

        void ApplyConfig()
        {
            int inst = cfg.GetInt("General.Instrument", 1) - 1;
            synth.SetInstrument(Math.Max(0, Math.Min(synth.Instruments.Length - 1, inst)));
            Mode m;
            if (!Enum.TryParse(cfg.Get("General.Mode", "Auto"), true, out m)) m = Mode.Auto;
            router.Mode = m;
            audio.BufferMs = Math.Max(1, Math.Min(100, cfg.GetInt("General.BufferMs", 6)));
            audio.LowLatency = cfg.GetInt("General.LowLatency", 1) != 0;
            audio.KeepWarmSeconds = Math.Max(3, cfg.GetInt("General.KeepWarmSeconds", 30));
            synth.BendRange = Math.Max(0, Math.Min(24, cfg.GetInt("General.BendRange", 2)));
            try
            {
                string[] k = cfg.Get("General.Knobs", "70,71,72,73,74").Split(',');
                var kc = new int[] { 70, 71, 72, 73, 74 };
                for (int i = 0; i < Math.Min(5, k.Length); i++) kc[i] = int.Parse(k[i].Trim());
                synth.KnobCC = kc;
            }
            catch { Log.Write("settings: Knobs must be 5 CC numbers, e.g. 70,71,72,73,74"); }
            // restore where the knobs were last left
            string[] kv = cfg.Get("General.KnobValues", "").Split(',');
            for (int i = 0; i < Math.Min(5, kv.Length); i++)
            {
                int val;
                if (int.TryParse(kv[i].Trim(), out val) && val >= 0 && val <= 127) synth.ControlChange(0, synth.KnobCC[i], val);
            }
            synth.KnobsDirty = false;
            synth.DrumChannel = Math.Max(1, Math.Min(16, cfg.GetInt("General.DrumChannel", 10))) - 1;
            try { ffx.Configure(cfg); ffxivError = null; }
            catch (Exception e) { ffxivError = e.Message; Log.Write("ffxiv config: " + e.Message); }
        }

        void TrySave()
        {
            try { cfg.Save(); } catch (Exception e) { Log.Write("save settings: " + e.Message); }
        }

        void BuildMenu()
        {
            statusItem = new ToolStripMenuItem("AlwaysOn Keys") { Enabled = false };
            menu.Items.Add(statusItem);
            audioItem = new ToolStripMenuItem("Audio: not started yet") { Enabled = false };
            menu.Items.Add(audioItem);
            menu.Items.Add(new ToolStripSeparator());

            instrMenu = new ToolStripMenuItem("Instrument");
            for (int i = 0; i < synth.Instruments.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem((i + 1) + ".  " + synth.Instruments[i].Name);
                it.Click += (s, e) => { synth.SetInstrument(idx); cfg.Set("General.Instrument", (idx + 1).ToString()); TrySave(); RefreshMenu(); };
                instrMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(instrMenu);

            modeMenu = new ToolStripMenuItem("Mode");
            AddMode("Auto  (FFXIV keys when the game is focused)", Mode.Auto);
            AddMode("Always play sound here", Mode.Synth);
            AddMode("Always send FFXIV keys", Mode.Ffxiv);
            menu.Items.Add(modeMenu);

            menu.Items.Add(new ToolStripSeparator());
            pauseItem = new ToolStripMenuItem("Pause  (let a DAW use the keyboard)");
            pauseItem.Click += (s, e) =>
            {
                paused = !paused;
                if (paused) { midi.Close(); router.Post(Router.CmdPanic); midiProblem = "paused"; }
                CheckMidi();
            };
            menu.Items.Add(pauseItem);
            startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Click += (s, e) => SetStartup(!IsStartup());
            menu.Items.Add(startupItem);
            var open = new ToolStripMenuItem("Open settings file");
            open.Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + cfg.Path + "\""); } catch { } };
            menu.Items.Add(open);
            var reload = new ToolStripMenuItem("Reload settings");
            reload.Click += (s, e) =>
            {
                router.Post(Router.CmdPanic);
                cfg.Load(); ApplyConfig(); audio.Reset(); RefreshMenu();
                if (ffxivError != null) tray.ShowBalloonTip(4000, "FFXIV key settings problem", ffxivError, ToolTipIcon.Warning);
            };
            menu.Items.Add(reload);
            menu.Items.Add(new ToolStripSeparator());
            var quit = new ToolStripMenuItem("Quit");
            quit.Click += (s, e) => Shutdown();
            menu.Items.Add(quit);
            menu.Opening += (s, e) => RefreshMenu();
            RefreshMenu();
        }

        void AddMode(string label, Mode m)
        {
            var it = new ToolStripMenuItem(label) { Tag = m };
            it.Click += (s, e) =>
            {
                router.Mode = m; router.Post(Router.CmdPanic);
                cfg.Set("General.Mode", m.ToString()); TrySave(); RefreshMenu();
            };
            modeMenu.DropDownItems.Add(it);
        }

        void RefreshMenu()
        {
            int cur = synth.Current;
            for (int i = 0; i < instrMenu.DropDownItems.Count; i++) ((ToolStripMenuItem)instrMenu.DropDownItems[i]).Checked = i == cur;
            foreach (ToolStripMenuItem it in modeMenu.DropDownItems) it.Checked = (Mode)it.Tag == router.Mode;
            pauseItem.Checked = paused;
            startupItem.Checked = IsStartup();
            string conn = midi.IsOpen ? "Connected: " + midi.OpenName : midiProblem;
            statusItem.Text = conn + (router.LastNote >= 0 && midi.IsOpen ? "   (last note " + router.LastNote + ", keyboard Oct " + OctText(ffx.KeyboardOctave) + ")" : "")
                + (router.LastCC >= 0 && midi.IsOpen ? "  (last knob CC " + router.LastCC + " = " + router.LastCCValue + ")" : "");
            audioItem.Text = "Audio: " + (audio.Info.Length > 0 ? audio.Info : "starts on first note");
            instrMenu.Text = "Instrument:  " + synth.Instruments[cur].Name;
            string modeTxt = router.Mode == Mode.Auto ? "Auto" : router.Mode == Mode.Synth ? "Sound" : "FFXIV";
            modeMenu.Text = "Mode:  " + modeTxt + (ffxivError != null ? "  (FFXIV keys: settings error)" : "");
            string tip = "AlwaysOn Keys - " + synth.Instruments[cur].Name + (midi.IsOpen ? "" : " (" + midiProblem + ")");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            UpdateIcon(midi.IsOpen ? (router.Mode == Mode.Ffxiv ? 2 : 1) : 0);
        }

        void CheckMidi()
        {
            if (paused) { RefreshMenu(); return; }
            if (midi.IsOpen && !midi.StillPresent())
            {
                midi.Close(); router.Post(Router.CmdPanic);
                Log.Write("keyboard disconnected");
            }
            if (!midi.IsOpen)
            {
                string err = midi.TryOpen(cfg.Get("General.Device", "MPK"));
                if (err == null) Log.Write("connected to " + midi.OpenName);
                midiProblem = err ?? "";
            }
            RefreshMenu();
        }

        bool IsStartup()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("AlwaysOnKeys") != null; }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("AlwaysOnKeys", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("AlwaysOnKeys", false);
                }
            }
            catch (Exception e) { Log.Write("startup: " + e.Message); }
            RefreshMenu();
        }

        static string OctText(int k) { return k == 0 ? "0" : k > 0 ? "+" + k : "\u2212" + (-k); }

        // 0 = disconnected (grey), 1 = ready (teal), 2 = FFXIV (purple)
        void UpdateIcon(int state)
        {
            if (state == iconState) return;
            iconState = state;
            Color body = state == 0 ? Color.FromArgb(120, 120, 120) : state == 1 ? Color.FromArgb(0, 150, 140) : Color.FromArgb(130, 80, 200);
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(body)) g.FillRectangle(b, 1, 5, 30, 23);
                    using (var w = new SolidBrush(Color.White))
                        for (int i = 0; i < 5; i++) g.FillRectangle(w, 3 + i * 5.4f, 10, 4.6f, 16);
                    using (var k = new SolidBrush(Color.FromArgb(25, 25, 25)))
                        foreach (int i in new[] { 0, 1, 3 }) g.FillRectangle(k, 6 + i * 5.4f, 10, 3.2f, 9);
                }
                IntPtr h = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
                iconHandle = h;
            }
        }

        void Shutdown()
        {
            timer.Stop();
            router.Stop();
            midi.Close();
            audio.Stop();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }

    /// `AlwaysOnKeys.exe --selftest [out.wav]` - checks MIDI devices, the audio
    /// device, and renders every instrument offline. Results go to selftest.txt.
    static class SelfTest
    {
        // Plays soft/medium/hard notes with each knob at 0/64/127 and reports what changed.
        static string KnobTest()
        {
            var sb = new StringBuilder();
            const int sr = 48000;
            string[] names = { "Volume", "Tone", "Reverb", "Release", "Compressor" };
            foreach (int ins in new[] { 0, 12, 3 })
            {
                var probe = new Synth();
                sb.AppendLine("== " + probe.Instruments[ins].Name);
                sb.AppendLine("knob        val | loud dB | bright | tail dB | soft-vs-hard dB");
                for (int k = 0; k < 5; k++)
                    foreach (int val in new[] { 0, 64, 127 })
                    {
                        var s = new Synth(); s.SetSampleRate(sr); s.SetInstrument(ins);
                        s.ControlChange(0, s.KnobCC[k], val);
                        var L = new float[sr / 2]; var R = new float[sr / 2];
                        double[] peaks = new double[3]; double sum2 = 0, dsum2 = 0, tail2 = 0; long cnt = 0, tcnt = 0;
                        int[] vels = { 30, 70, 127 };
                        for (int vi = 0; vi < 3; vi++)
                        {
                            s.NoteOn(0, 72, vels[vi]);
                            s.Render(L, R, L.Length);
                            float prev = 0;
                            for (int i = 0; i < L.Length; i++)
                            {
                                double x = L[i]; peaks[vi] = Math.Max(peaks[vi], Math.Abs(x));
                                sum2 += x * x; dsum2 += (x - prev) * (x - prev); prev = L[i]; cnt++;
                            }
                            s.NoteOff(0, 72);
                            for (int t = 0; t < 2; t++)
                            {
                                s.Render(L, R, L.Length);
                                if (vi == 2) for (int i = 0; i < L.Length; i++) { tail2 += L[i] * (double)L[i]; tcnt++; }
                            }
                        }
                        double rms = Math.Sqrt(sum2 / cnt);
                        sb.AppendLine(string.Format("{0,-10} {1,4} | {2,7:0.0} | {3,6:0.000} | {4,7:0.0} | {5,6:0.0}",
                            names[k], val, 20 * Math.Log10(rms + 1e-9), Math.Sqrt(dsum2 / cnt) / (rms + 1e-9),
                            10 * Math.Log10(tail2 / Math.Max(1, tcnt) + 1e-12), 20 * Math.Log10((peaks[0] + 1e-9) / (peaks[2] + 1e-9))));
                    }
            }
            return sb.ToString();
        }

        public static int Run(string[] args)
        {
            var sb = new StringBuilder();
            var devs = MidiIn.Devices();
            sb.AppendLine("MIDI inputs: " + (devs.Count == 0 ? "(none)" : string.Join(" | ", devs.ToArray())));
            var synth = new Synth();
            if (args.Length > 1 && args[1] == "--knobs")
            {
                File.WriteAllText(Path.Combine(Program.Dir, "selftest.txt"), KnobTest());
                return 0;
            }
            if (args.Length > 1 && args[1] == "--warm")
            {
                // measure the CPU cost of a running-but-silent stream, and the restart time
                string r = null;
                var t = new Thread(() => { r = new AudioOut(synth).WarmTest(); });
                t.SetApartmentState(ApartmentState.MTA); t.Start(); t.Join();
                File.WriteAllText(Path.Combine(Program.Dir, "selftest.txt"), r);
                return 0;
            }
            sb.AppendLine("Audio: " + new AudioOut(synth).Probe());

            const int sr = 48000;
            synth.SetSampleRate(sr);
            var wav = new List<short>();
            var L = new float[sr / 10]; var R = new float[sr / 10];
            var sw = Stopwatch.StartNew(); long rendered = 0;
            for (int ins = 0; ins < synth.Instruments.Length; ins++)
            {
                synth.SetInstrument(ins);
                float peak = 0; bool nan = false;
                int[] notes = { 48, 55, 60, 64, 67, 72, 84 };
                for (int step = 0; step < 24; step++)
                {
                    if (step < notes.Length) synth.NoteOn(0, notes[step], 100);
                    if (step == 10) foreach (int n in notes) synth.NoteOff(0, n);
                    synth.Render(L, R, L.Length); rendered += L.Length;
                    for (int i = 0; i < L.Length; i++)
                    {
                        if (float.IsNaN(L[i]) || float.IsInfinity(L[i])) nan = true;
                        peak = Math.Max(peak, Math.Abs(L[i]));
                        wav.Add((short)(L[i] * 32000)); wav.Add((short)(R[i] * 32000));
                    }
                }
                synth.AllOff();
                for (int k = 0; k < 3; k++) synth.Render(L, R, L.Length);
                sb.AppendLine(string.Format("{0,-18} peak {1:0.00}{2}", synth.Instruments[ins].Name, peak, nan ? "  NaN!" : ""));
            }
            // drums
            {
                float peak = 0;
                for (int step = 0; step < 20; step++)
                {
                    if (step < 16) synth.NoteOn(9, 36 + step, 110);
                    synth.Render(L, R, L.Length); rendered += L.Length;
                    for (int i = 0; i < L.Length; i++) { peak = Math.Max(peak, Math.Abs(L[i])); wav.Add((short)(L[i] * 32000)); wav.Add((short)(R[i] * 32000)); }
                }
                sb.AppendLine(string.Format("{0,-18} peak {1:0.00}", "Drums (pads)", peak));
            }
            double secs = rendered / (double)sr;
            sb.AppendLine(string.Format("Rendered {0:0.0}s of audio in {1} ms ({2:0.0}% of one core while playing chords)",
                secs, sw.ElapsedMilliseconds, sw.ElapsedMilliseconds / (secs * 10)));

            try
            {
                var cfg = new Config(Path.Combine(Program.Dir, "AlwaysOnKeys.ini")); cfg.Load();
                new FfxivPlayer().Configure(cfg);
                sb.AppendLine("FFXIV key map: OK");
            }
            catch (Exception e) { sb.AppendLine("FFXIV key map: " + e.Message); }

            if (args.Length > 1)
            {
                using (var f = new BinaryWriter(File.Create(args[1])))
                {
                    int bytes = wav.Count * 2;
                    f.Write(Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + bytes); f.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(sr); f.Write(sr * 4); f.Write((short)4); f.Write((short)16);
                    f.Write(Encoding.ASCII.GetBytes("data")); f.Write(bytes);
                    foreach (short s in wav) f.Write(s);
                }
                sb.AppendLine("Wrote " + args[1]);
            }
            File.WriteAllText(Path.Combine(Program.Dir, "selftest.txt"), sb.ToString());
            return 0;
        }
    }
}
