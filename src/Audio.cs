// WASAPI shared-mode, event-driven output.
//  * Uses the Windows 10+ low-latency engine period when the driver allows it.
//  * While you're playing (and for KeepWarm seconds after the last sound) the
//    stream keeps running, writing silence cheaply, so the next note is instant.
//  * After that it stops and the thread sleeps until the next note (0% CPU).
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AlwaysOnKeys
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    // Windows 10+: lets a shared-mode stream run the audio engine at a smaller
    // period (often ~3 ms instead of the default 10 ms).
    [ComImport, Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient3
    {
        // IAudioClient
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        // IAudioClient2
        [PreserveSig] int IsOffloadCapable(int category, out int capable);
        [PreserveSig] int SetClientProperties(IntPtr props);
        [PreserveSig] int GetBufferSizeLimits(IntPtr format, int eventDriven, out long min, out long max);
        // IAudioClient3
        [PreserveSig] int GetSharedModeEnginePeriod(IntPtr format, out uint defaultFrames, out uint fundamentalFrames, out uint minFrames, out uint maxFrames);
        [PreserveSig] int GetCurrentSharedModeEnginePeriod(out IntPtr format, out uint frames);
        [PreserveSig] int InitializeSharedAudioStream(int streamFlags, uint periodFrames, IntPtr format, IntPtr sessionGuid);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }

    sealed class AudioOut
    {
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] static extern IntPtr AvSetMmThreadCharacteristics(string task, ref uint index);

        readonly Synth synth;
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly AutoResetEvent bufferEvent = new AutoResetEvent(false);
        volatile bool quit;
        public int BufferMs = 6;          // how much audio to keep queued (minimum 2 engine periods)
        public bool LowLatency = true;    // use the small engine period (IAudioClient3)
        public int KeepWarmSeconds = 30;  // keep the stream running this long after the last sound
        public string Status = "audio idle";
        public string Info = "";

        IAudioClient client; IAudioRenderClient render;
        string deviceId;
        int channels, sampleRate; bool isFloat; int bits; uint bufFrames, targetFrames, periodFrames;
        float[] L = new float[0], R = new float[0];

        public AudioOut(Synth s)
        {
            synth = s;
            thread = new Thread(Run) { IsBackground = true, Name = "audio", Priority = ThreadPriority.Highest };
            thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start() { thread.Start(); }
        public void Wake() { wake.Set(); }
        public void Stop() { quit = true; wake.Set(); bufferEvent.Set(); thread.Join(1000); }
        /// Re-open the device on the next note (after settings change).
        public void Reset() { resetRequested = true; bufferEvent.Set(); wake.Set(); }
        volatile bool resetRequested;

        void Run()
        {
            uint taskIdx = 0;
            try { AvSetMmThreadCharacteristics("Pro Audio", ref taskIdx); } catch { }
            while (!quit)
            {
                wake.WaitOne();
                if (quit) break;
                try { PlaySession(); }
                catch (Exception e) { Log.Write("audio: " + e.Message); ReleaseClient(); Thread.Sleep(300); }
            }
            ReleaseClient();
        }

        void PlaySession()
        {
            if (resetRequested) { resetRequested = false; ReleaseClient(); }
            bool checkDevice = client != null;
            if (client == null) CreateClient();

            Fill(true);
            Check(client.Start(), "Start");
            Status = "playing";
            DateTime idleSince = DateTime.MaxValue;
            int loops = 0;
            while (!quit && !resetRequested)
            {
                bufferEvent.WaitOne(200);
                int hr = Fill(false);
                if (hr != 0) { ReleaseClient(); throw new Exception("device lost 0x" + hr.ToString("X8")); }
                // follow default-device changes (headphones plugged in, etc.). Checked
                // after the note has already started so it never delays the sound.
                if (checkDevice && ++loops == 4)
                {
                    checkDevice = false;
                    string id = DefaultDeviceId();
                    if (id != null && id != deviceId) { Log.Write("audio: default device changed"); ReleaseClient(); CreateClient(); Fill(true); Check(client.Start(), "Start"); }
                }
                if (synth.IsIdle)
                {
                    DateTime now = DateTime.UtcNow;
                    if (idleSince == DateTime.MaxValue) idleSince = now;
                    else if ((now - idleSince).TotalSeconds > KeepWarmSeconds) break;
                }
                else idleSince = DateTime.MaxValue;
            }
            if (client != null) { client.Stop(); client.Reset(); }
            Status = "audio idle";
            if (resetRequested) wake.Set();
        }

        unsafe int Fill(bool first)
        {
            uint padding;
            int hr = client.GetCurrentPadding(out padding);
            if (hr != 0) return hr;
            int frames = (int)targetFrames - (int)padding;
            if (frames <= 0) return 0;
            IntPtr data;
            hr = render.GetBuffer((uint)frames, out data);
            if (hr != 0) return hr;
            // nothing sounding: hand the engine silence without running the synth
            if (synth.IsIdle) return render.ReleaseBuffer((uint)frames, 2 /*AUDCLNT_BUFFERFLAGS_SILENT*/);
            if (L.Length < frames) { L = new float[frames]; R = new float[frames]; }
            synth.Render(L, R, frames);
            if (isFloat)
            {
                float* p = (float*)data;
                for (int i = 0; i < frames; i++)
                {
                    p[0] = L[i]; if (channels > 1) p[1] = R[i];
                    for (int c = 2; c < channels; c++) p[c] = 0;
                    p += channels;
                }
            }
            else if (bits == 16)
            {
                short* p = (short*)data;
                for (int i = 0; i < frames; i++)
                {
                    p[0] = (short)(L[i] * 32767f); if (channels > 1) p[1] = (short)(R[i] * 32767f);
                    for (int c = 2; c < channels; c++) p[c] = 0;
                    p += channels;
                }
            }
            else if (bits == 32)
            {
                int* p = (int*)data;
                for (int i = 0; i < frames; i++)
                {
                    p[0] = (int)(L[i] * 2147483000f); if (channels > 1) p[1] = (int)(R[i] * 2147483000f);
                    for (int c = 2; c < channels; c++) p[c] = 0;
                    p += channels;
                }
            }
            else return render.ReleaseBuffer((uint)frames, 2);
            return render.ReleaseBuffer((uint)frames, 0);
        }

        static string DefaultDeviceId()
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0) return null;
                string id; return dev.GetId(out id) == 0 ? id : null;
            }
            catch { return null; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        void CreateClient()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            IMMDevice dev = null;
            try
            {
                Check(en.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out dev), "GetDefaultAudioEndpoint");
                dev.GetId(out deviceId);
                Guid iid = typeof(IAudioClient).GUID;
                object o;
                Check(dev.Activate(ref iid, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out o), "Activate");
                client = (IAudioClient)o;
                IntPtr fmt;
                Check(client.GetMixFormat(out fmt), "GetMixFormat");
                string mode = "standard";
                try
                {
                    int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF;
                    channels = Marshal.ReadInt16(fmt, 2);
                    sampleRate = Marshal.ReadInt32(fmt, 4);
                    bits = Marshal.ReadInt16(fmt, 14);
                    isFloat = tag == 3 || (tag == 0xFFFE && Marshal.ReadInt32(fmt, 24) == 3);
                    bool ok = false;
                    IAudioClient3 c3 = LowLatency ? o as IAudioClient3 : null;
                    if (c3 != null)
                    {
                        uint defF, fundF, minF, maxF;
                        if (c3.GetSharedModeEnginePeriod(fmt, out defF, out fundF, out minF, out maxF) == 0 && minF > 0 && minF < defF)
                        {
                            if (c3.InitializeSharedAudioStream(0x00040000 /*EVENTCALLBACK*/, minF, fmt, IntPtr.Zero) == 0)
                            { ok = true; periodFrames = minF; mode = "low-latency"; }
                        }
                    }
                    if (!ok)
                    {
                        Check(client.Initialize(0 /*shared*/, 0x00040000 /*EVENTCALLBACK*/, 0, 0, fmt, IntPtr.Zero), "Initialize");
                        long defP, minP; client.GetDevicePeriod(out defP, out minP);
                        periodFrames = (uint)(defP * sampleRate / 10000000L);
                    }
                }
                finally { Marshal.FreeCoTaskMem(fmt); }
                Check(client.SetEventHandle(bufferEvent.SafeWaitHandle.DangerousGetHandle()), "SetEventHandle");
                Check(client.GetBufferSize(out bufFrames), "GetBufferSize");
                uint want = (uint)(BufferMs * sampleRate / 1000);
                targetFrames = Math.Min(bufFrames, Math.Max(want, periodFrames * 2));
                Guid rid = typeof(IAudioRenderClient).GUID;
                object ro;
                Check(client.GetService(ref rid, out ro), "GetService");
                render = (IAudioRenderClient)ro;
                synth.SetSampleRate(sampleRate);
                Info = string.Format("{0} Hz {1}, engine period {2:0.0} ms, queue {3:0.0} ms",
                    sampleRate, mode, periodFrames * 1000.0 / sampleRate, targetFrames * 1000.0 / sampleRate);
                Log.Write("audio: " + Info + string.Format(" ({0} ch, {1}-bit {2})", channels, bits, isFloat ? "float" : "int"));
            }
            catch
            {
                ReleaseClient();
                throw;
            }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }

        void ReleaseClient()
        {
            try { if (render != null) Marshal.ReleaseComObject(render); } catch { }
            try { if (client != null) { client.Stop(); Marshal.ReleaseComObject(client); } } catch { }
            render = null; client = null;
        }

        static void Check(int hr, string what)
        {
            if (hr != 0) throw new Exception(what + " failed 0x" + hr.ToString("X8"));
        }

        public string WarmTest()
        {
            var me = System.Diagnostics.Process.GetCurrentProcess();
            CreateClient(); Fill(true); client.Start();
            for (int i = 0; i < 400; i++) { bufferEvent.WaitOne(200); Fill(false); }
            me.Refresh(); var t0 = me.TotalProcessorTime; var w = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) { bufferEvent.WaitOne(200); Fill(false); }
            me.Refresh();
            double pct = (me.TotalProcessorTime - t0).TotalMilliseconds / w.ElapsedMilliseconds * 100;
            client.Stop(); client.Reset();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string id = DefaultDeviceId();
            double idMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            Fill(true); client.Start(); bufferEvent.WaitOne(200);
            double restartMs = sw.Elapsed.TotalMilliseconds;
            client.Stop(); ReleaseClient();
            sw.Restart(); CreateClient(); Fill(true); client.Start(); bufferEvent.WaitOne(200);
            double createMs = sw.Elapsed.TotalMilliseconds;
            ReleaseClient();
            return string.Format("{0}\r\nwarm silent stream: {1:0.00}% of one core over {2:0.0}s\r\ndevice-id check {3:0.00} ms, restart {4:0.00} ms, full re-create {5:0.00} ms (id ok: {6})\r\n",
                Info, pct, w.ElapsedMilliseconds / 1000.0, idMs, restartMs, createMs, id == deviceId);
        }

        /// Opens the device and plays silence briefly (for --selftest).
        public string Probe() { return Probe(100); }
        public string Probe(int periods)
        {
            try
            {
                CreateClient();
                Fill(true);
                Check(client.Start(), "Start");
                for (int i = 0; i < periods; i++) { bufferEvent.WaitOne(200); Fill(false); }
                client.Stop();
                ReleaseClient();
                return "OK " + Info;
            }
            catch (Exception e) { ReleaseClient(); return "FAILED: " + e.Message; }
        }
    }
}
