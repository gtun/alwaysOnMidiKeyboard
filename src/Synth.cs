// Lightweight polyphonic synth: a handful of cheap instrument models (additive,
// 2-op FM, subtractive, Karplus-Strong plucked string, flute) plus a small
// synthesized drum kit. Nothing is computed while no voices are sounding.
using System;

namespace AlwaysOnKeys
{
    enum Kind { Additive, FM, Sub, Pluck, Flute, Drum }

    sealed class Instrument
    {
        public string Name;
        public Kind Kind;
        public float A = 0.003f, D = 1f, S = 1f, R = 0.2f; // seconds / sustain level
        public bool KeyScale;           // higher notes decay faster
        public float Gain = 0.5f;
        // additive
        public float[] Ratios, Amps, Decays; // Decays: seconds to -60dB per partial, 0 = no decay
        public float Inharm, VelBright, Click, ClickTime = 0.01f, Tremolo;
        // FM
        public float ModRatio = 1f, Index, IndexDecay = 1f, IndexFloor, Mod2Ratio = 1f, Index2, Index2Decay = 0.05f;
        // subtractive
        public int Wave;                // 0 saw, 1 square
        public int Unison = 1;
        public float Detune, Sub;
        public float Cutoff = 20000f, EnvAmt, FiltDecay = 0.3f, Q = 0.7f, KeyTrack = 0.5f;
        public bool FiltFollowsAmp;
        // pluck
        public float T60 = 3f, Smooth = 0.5f, PluckBright = 0.5f;
        // vibrato / breath
        public float Vibrato, VibratoDelay = 0.3f, Breath;

        public static Instrument[] All()
        {
            return new Instrument[] {
                new Instrument { Name = "Grand Piano", Kind = Kind.Additive,
                    Ratios = new float[] { 1, 1.0013f, 2, 3, 4, 5, 6, 7, 8, 9 },
                    Amps   = new float[] { 1, 0.55f, 0.5f, 0.32f, 0.22f, 0.16f, 0.11f, 0.07f, 0.05f, 0.035f },
                    Decays = new float[] { 6, 5, 3.4f, 2.5f, 1.9f, 1.5f, 1.2f, 0.9f, 0.7f, 0.6f },
                    Inharm = 0.00035f, KeyScale = true, VelBright = 1f, Click = 0.25f, ClickTime = 0.006f,
                    A = 0.001f, S = 1f, R = 0.35f, Gain = 0.42f },
                new Instrument { Name = "Electric Piano", Kind = Kind.FM,
                    ModRatio = 1f, Index = 2.0f, IndexDecay = 0.6f, IndexFloor = 0.25f,
                    Mod2Ratio = 14f, Index2 = 1.1f, Index2Decay = 0.04f,
                    A = 0.001f, D = 4.5f, S = 0f, R = 0.25f, KeyScale = true, Gain = 0.5f },
                new Instrument { Name = "Drawbar Organ", Kind = Kind.Additive,
                    Ratios = new float[] { 0.5f, 1, 1.5f, 2, 3, 4, 6, 8 },
                    Amps   = new float[] { 0.8f, 1, 0.55f, 0.7f, 0.3f, 0.35f, 0.18f, 0.22f },
                    Decays = new float[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    Click = 0.12f, ClickTime = 0.004f, A = 0.006f, S = 1f, R = 0.06f, Gain = 0.22f },
                new Instrument { Name = "String Ensemble", Kind = Kind.Sub,
                    Unison = 3, Detune = 14, Cutoff = 2600, KeyTrack = 0.5f, Q = 0.6f,
                    A = 0.25f, D = 1f, S = 1f, R = 0.7f, Vibrato = 0.08f, VibratoDelay = 0.4f, Gain = 0.3f },
                new Instrument { Name = "Warm Pad", Kind = Kind.Sub,
                    Unison = 3, Detune = 20, Sub = 0.3f, Cutoff = 700, EnvAmt = 2f, FiltFollowsAmp = true,
                    KeyTrack = 0.4f, Q = 0.8f, A = 0.9f, D = 1f, S = 1f, R = 1.8f, Gain = 0.3f },
                new Instrument { Name = "Brass", Kind = Kind.Sub,
                    Unison = 2, Detune = 6, Cutoff = 450, EnvAmt = 6f, FiltFollowsAmp = true, KeyTrack = 0.7f, Q = 0.8f,
                    A = 0.05f, D = 0.5f, S = 0.8f, R = 0.15f, Vibrato = 0.06f, VibratoDelay = 0.5f, Gain = 0.3f },
                new Instrument { Name = "Flute", Kind = Kind.Flute,
                    A = 0.06f, D = 0.2f, S = 0.85f, R = 0.12f, Vibrato = 0.14f, VibratoDelay = 0.35f, Breath = 0.1f, Gain = 0.45f },
                new Instrument { Name = "Nylon Guitar", Kind = Kind.Pluck,
                    T60 = 2.8f, Smooth = 0.55f, PluckBright = 0.45f, KeyScale = true, A = 0.001f, S = 1f, R = 0.12f, Gain = 1.5f },
                new Instrument { Name = "Harp", Kind = Kind.Pluck,
                    T60 = 4.5f, Smooth = 0.4f, PluckBright = 0.7f, KeyScale = true, A = 0.001f, S = 1f, R = 1.5f, Gain = 1.2f },
                new Instrument { Name = "Marimba", Kind = Kind.Additive,
                    Ratios = new float[] { 1, 3.93f, 9.2f },
                    Amps   = new float[] { 1, 0.3f, 0.1f },
                    Decays = new float[] { 1.1f, 0.25f, 0.08f },
                    KeyScale = true, Click = 0.08f, ClickTime = 0.003f, A = 0.001f, S = 1f, R = 0.25f, Gain = 0.6f },
                new Instrument { Name = "Vibraphone", Kind = Kind.Additive,
                    Ratios = new float[] { 1, 3.98f, 9.95f },
                    Amps   = new float[] { 1, 0.22f, 0.07f },
                    Decays = new float[] { 4.5f, 1.2f, 0.4f },
                    KeyScale = true, Tremolo = 0.3f, A = 0.001f, S = 1f, R = 0.6f, Gain = 0.48f },
                new Instrument { Name = "Music Box", Kind = Kind.Additive,
                    Ratios = new float[] { 1, 2, 5.4f, 8.9f },
                    Amps   = new float[] { 1, 0.3f, 0.22f, 0.09f },
                    Decays = new float[] { 1.8f, 0.9f, 0.4f, 0.2f },
                    KeyScale = true, Click = 0.04f, ClickTime = 0.002f, A = 0.001f, S = 1f, R = 0.4f, Gain = 0.45f },
                new Instrument { Name = "Crystal Bells", Kind = Kind.FM,
                    ModRatio = 3.5f, Index = 3f, IndexDecay = 1.6f, IndexFloor = 0.3f,
                    A = 0.001f, D = 5f, S = 0f, R = 1.2f, KeyScale = true, Gain = 0.35f },
                new Instrument { Name = "Synth Lead", Kind = Kind.Sub,
                    Unison = 2, Detune = 9, Cutoff = 2500, EnvAmt = 2.5f, FiltDecay = 0.35f, Q = 1.6f, KeyTrack = 0.6f,
                    A = 0.005f, D = 0.3f, S = 0.85f, R = 0.12f, Gain = 0.26f },
                new Instrument { Name = "Synth Bass", Kind = Kind.Sub,
                    Unison = 1, Sub = 0.6f, Cutoff = 220, EnvAmt = 9f, FiltDecay = 0.18f, Q = 2.2f, KeyTrack = 0.3f,
                    A = 0.002f, D = 0.6f, S = 0.6f, R = 0.08f, Gain = 0.42f },
                new Instrument { Name = "Chiptune", Kind = Kind.Sub,
                    Wave = 1, Unison = 1, Cutoff = 20000, KeyTrack = 0f,
                    A = 0.002f, D = 0.25f, S = 0.75f, R = 0.05f, Gain = 0.18f },
            };
        }
    }

    static class Dsp
    {
        public const int N = 2048;
        public static readonly float[] Sin = Build();
        static float[] Build()
        {
            var t = new float[N + 1];
            for (int i = 0; i <= N; i++) t[i] = (float)Math.Sin(2 * Math.PI * i / N);
            return t;
        }
        // sine of a phase measured in cycles (any real value)
        public static float S(float p)
        {
            p -= (float)Math.Floor(p);
            float x = p * N; int i = (int)x; float f = x - i;
            if (i >= N) { i = 0; f = 0; }
            return Sin[i] + (Sin[i + 1] - Sin[i]) * f;
        }
        public static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }
        public static float T60Coef(float seconds, float sr)
        {
            if (seconds <= 0) return 1f;
            return (float)Math.Exp(-6.9077553 / (seconds * sr));
        }
    }

    sealed class Voice
    {
        public bool Active, Released, Held;
        public int Note, Chan, Drum;
        public long Order;
        public Instrument Ins;
        public float Vel, Freq, PanL, PanR, Age;

        const int MP = 10;
        readonly float sr;
        int stage; float env, attInc, decCoef, relCoef, sus;
        readonly float[] ph = new float[MP], pr = new float[MP], lv = new float[MP], dk = new float[MP];
        int np;
        float clickLvl, clickCoef, nLp, nLp2;
        float cph, mph, m2ph, idx, idxCoef, idxFloor, idx2, idx2Coef;
        float fenv, fenvCoef, ic1, ic2, a1, a2, a3;
        readonly float[] ks; int ksW; float ksLoss, ksPrev;
        float e1, e2, e3, c1, c2, c3, f0, hpA, hpB, hpC;
        uint rng;

        public Voice(float sampleRate, uint seed)
        {
            sr = sampleRate;
            ks = new float[(int)(sr / 15f) + 8];
            rng = seed | 1;
        }

        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        public void Start(Instrument ins, int chan, int note, float vel, long order, int drum)
        {
            Ins = ins; Chan = chan; Note = note; Vel = vel; Order = order; Drum = drum;
            Active = true; Released = false; Held = false; Age = 0;
            Freq = 440f * (float)Math.Pow(2, (note - 69) / 12.0);
            float pan = Math.Max(-0.45f, Math.Min(0.45f, (note - 60) / 50f));
            PanL = (float)Math.Cos((pan + 1) * Math.PI / 4); PanR = (float)Math.Sin((pan + 1) * Math.PI / 4);
            ic1 = ic2 = 0; nLp = nLp2 = 0; hpA = hpB = hpC = 0;

            if (drum >= 0) { StartDrum(); return; }

            float kscale = ins.KeyScale ? (float)Math.Max(0.25, Math.Min(3.0, Math.Pow(2, -(note - 60) / 24.0))) : 1f;
            env = 0; stage = 0;
            attInc = 1f / Math.Max(1f, ins.A * sr);
            sus = ins.S;
            decCoef = Dsp.T60Coef(ins.D * kscale, sr);
            clickLvl = ins.Click * vel; clickCoef = Dsp.T60Coef(ins.ClickTime, sr);

            switch (ins.Kind)
            {
                case Kind.Additive:
                    np = Math.Min(MP, ins.Ratios.Length);
                    for (int k = 0; k < np; k++)
                    {
                        float r = ins.Ratios[k];
                        if (ins.Inharm > 0) { float h = (float)Math.Round(r); r *= (float)Math.Sqrt(1 + ins.Inharm * h * h); }
                        pr[k] = r; ph[k] = 0;
                        float a = ins.Amps[k];
                        if (ins.VelBright > 0) { float w = Math.Min(1f, (r - 1f) / 4f) * ins.VelBright; if (w > 0) a *= (1 - w) + w * vel; }
                        if (Freq * r > 0.45f * sr) a = 0;
                        lv[k] = a;
                        dk[k] = Dsp.T60Coef(ins.Decays[k] * kscale, sr);
                    }
                    break;
                case Kind.FM:
                    cph = mph = m2ph = 0;
                    idx = ins.Index * (0.4f + 0.6f * vel);
                    idxFloor = ins.IndexFloor;
                    idxCoef = Dsp.T60Coef(ins.IndexDecay, sr);
                    idx2 = Freq * ins.Mod2Ratio < 0.4f * sr ? ins.Index2 * vel : 0f;
                    idx2Coef = Dsp.T60Coef(ins.Index2Decay, sr);
                    break;
                case Kind.Sub:
                    np = Math.Max(1, Math.Min(MP - 1, ins.Unison));
                    for (int u = 0; u < np; u++)
                    {
                        float c = np == 1 ? 0 : ins.Detune * (u - (np - 1) / 2f) / ((np - 1) / 2f);
                        pr[u] = (float)Math.Pow(2, c / 1200.0);
                        ph[u] = (Noise() + 1f) * 0.5f;
                    }
                    ph[MP - 1] = 0;
                    fenv = 1f; fenvCoef = Dsp.T60Coef(ins.FiltDecay * 3f, sr);
                    break;
                case Kind.Pluck:
                {
                    float d = sr / Freq;
                    int len = (int)Math.Ceiling(d) + 2;
                    if (len >= ks.Length) len = ks.Length - 1;
                    Array.Clear(ks, 0, ks.Length);
                    float a = 0.15f + 0.85f * ins.PluckBright * (0.4f + 0.6f * vel), y = 0, mean = 0;
                    for (int i = 0; i < len; i++) { y += a * (Noise() - y); ks[i] = y; mean += y; }
                    mean /= len;
                    for (int i = 0; i < len; i++) ks[i] -= mean;
                    ksW = len; ksPrev = 0;
                    ksLoss = (float)Math.Pow(0.001, 1.0 / (ins.T60 * kscale * Freq));
                    break;
                }
                case Kind.Flute:
                    ph[0] = 0;
                    break;
            }
        }

        public void Release(float releaseMul)
        {
            if (Released) return;
            Released = true;
            if (Drum >= 0) return; // one-shots
            stage = 3;
            relCoef = Dsp.T60Coef(Math.Max(0.01f, Ins.R * releaseMul), sr);
        }

        public void Kill() { Released = true; stage = 3; relCoef = Dsp.T60Coef(0.02f, sr); }

        void StartDrum()
        {
            env = 1; stage = 2; sus = 1;
            ph[0] = ph[1] = 0; e3 = 0; c3 = 1;
            switch (Drum)
            {
                case 0: e1 = 1; c1 = Dsp.T60Coef(0.12f, sr); e2 = 1; c2 = Dsp.T60Coef(0.5f, sr); e3 = 0.6f; c3 = Dsp.T60Coef(0.01f, sr); break; // kick
                case 1: e2 = 1; c2 = Dsp.T60Coef(0.05f, sr); break; // rim
                case 2: e1 = 1; c1 = Dsp.T60Coef(0.15f, sr); e2 = 1; c2 = Dsp.T60Coef(0.28f, sr); break; // snare
                case 3: e2 = 1; c2 = Dsp.T60Coef(0.35f, sr); break; // clap
                case 4: e1 = 1; c1 = Dsp.T60Coef(0.12f, sr); e2 = 1; c2 = Dsp.T60Coef(0.6f, sr); break; // tom
                case 5: e2 = 1; c2 = Dsp.T60Coef(0.09f, sr); break; // closed hat
                case 6: e2 = 1; c2 = Dsp.T60Coef(0.16f, sr); break; // pedal hat
                case 7: e2 = 1; c2 = Dsp.T60Coef(0.9f, sr); break;  // open hat
                case 8: e2 = 1; c2 = Dsp.T60Coef(2.4f, sr); break;  // crash
                case 9: e2 = 1; c2 = Dsp.T60Coef(1.6f, sr); break;  // ride
                case 10: e2 = 1; c2 = Dsp.T60Coef(0.35f, sr); break; // cowbell
            }
            float[] metal = { 205.3f, 304.4f, 369.6f, 522.7f, 540f, 800f };
            for (int i = 0; i < 6; i++) { pr[i] = metal[i] * (Drum == 9 ? 1.4f : 1.9f) / sr; ph[2 + i % 6] = 0; }
        }

        // Drum note map (GM-ish, also covers MPK mini pad banks A/B: 36-51)
        public static int DrumFor(int note, out float tomFreq)
        {
            tomFreq = 100;
            switch (note)
            {
                case 35: case 36: return 0;
                case 37: return 1;
                case 38: case 40: return 2;
                case 39: return 3;
                case 41: tomFreq = 80; return 4;
                case 43: tomFreq = 100; return 4;
                case 45: tomFreq = 120; return 4;
                case 47: tomFreq = 145; return 4;
                case 48: tomFreq = 170; return 4;
                case 50: tomFreq = 205; return 4;
                case 42: case 54: return 5;
                case 44: return 6;
                case 46: return 7;
                case 49: case 52: case 55: case 57: return 8;
                case 51: case 53: case 59: return 9;
                case 56: return 10;
            }
            int[] fold = { 0, 1, 2, 3, 2, 4, 5, 4, 6, 4, 7, 4 };
            int d = fold[((note % 12) + 12) % 12];
            tomFreq = 90 + (note % 12) * 12;
            return d;
        }

        public void SetTom(float f) { f0 = f; }

        // Renders n samples (mono) into buf (overwrites). Returns false when the voice has finished.
        public bool Render(float[] buf, int n, float semis, float trem, float bright, float lfoPhase)
        {
            float vamp = Vel * (0.35f + 0.65f * Vel);
            float inc = Freq * (float)Math.Pow(2, semis / 12.0) / sr;
            float peak = 0;
            Instrument ins = Ins;

            if (Drum >= 0) { peak = RenderDrum(buf, n, vamp); goto done; }

            switch (ins.Kind)
            {
                case Kind.Additive:
                {
                    float tremAmt = ins.Tremolo > 0 ? 1f - ins.Tremolo * 0.5f * (1f + Dsp.S(lfoPhase * 0.85f)) : 1f;
                    for (int i = 0; i < n; i++)
                    {
                        float s = 0;
                        for (int k = 0; k < np; k++)
                        {
                            float p = ph[k];
                            s += lv[k] * Dsp.Sin[(int)(p * Dsp.N)];
                            p += inc * pr[k]; if (p >= 1f) p -= 1f; ph[k] = p;
                            lv[k] *= dk[k];
                        }
                        if (clickLvl > 1e-5f) { nLp += 0.5f * (Noise() - nLp); s += nLp * clickLvl; clickLvl *= clickCoef; }
                        buf[i] = s * NextEnv() * tremAmt;
                    }
                    break;
                }
                case Kind.FM:
                {
                    float mr = ins.ModRatio, m2r = ins.Mod2Ratio;
                    const float inv2pi = 0.15915494f;
                    for (int i = 0; i < n; i++)
                    {
                        float m = Dsp.S(mph) * (idx + idxFloor) + Dsp.S(m2ph) * idx2;
                        float s = Dsp.S(cph + m * inv2pi);
                        cph += inc; if (cph >= 1f) cph -= 1f;
                        mph += inc * mr; if (mph >= 1f) mph -= (float)Math.Floor(mph);
                        m2ph += inc * m2r; if (m2ph >= 1f) m2ph -= (float)Math.Floor(m2ph);
                        idx *= idxCoef; idx2 *= idx2Coef;
                        buf[i] = s * NextEnv();
                    }
                    break;
                }
                case Kind.Sub:
                {
                    float fe = ins.FiltFollowsAmp ? env : fenv;
                    float fc = ins.Cutoff * (float)Math.Pow(Freq / 261.6f, ins.KeyTrack) * (1f + ins.EnvAmt * fe * (0.5f + 0.5f * Vel)) * bright;
                    bool filt = fc < 0.45f * sr;
                    if (filt)
                    {
                        if (fc < 20f) fc = 20f;
                        float g = (float)Math.Tan(Math.PI * fc / sr), kq = 1f / ins.Q;
                        a1 = 1f / (1f + g * (g + kq)); a2 = g * a1; a3 = g * a2;
                    }
                    float norm = 1f / (float)Math.Sqrt(np);
                    bool sq = ins.Wave == 1;
                    for (int i = 0; i < n; i++)
                    {
                        float v = 0;
                        for (int u = 0; u < np; u++)
                        {
                            float dt = inc * pr[u], p = ph[u] + dt;
                            if (p >= 1f) p -= 1f;
                            ph[u] = p;
                            if (sq)
                            {
                                float q = p < 0.5f ? 1f : -1f;
                                float p2 = p + 0.5f; if (p2 >= 1f) p2 -= 1f;
                                v += q + Dsp.Blep(p, dt) - Dsp.Blep(p2, dt);
                            }
                            else v += 2f * p - 1f - Dsp.Blep(p, dt);
                        }
                        v *= norm;
                        if (ins.Sub > 0) { float sp = ph[MP - 1] + inc * 0.5f; if (sp >= 1f) sp -= 1f; ph[MP - 1] = sp; v += ins.Sub * Dsp.Sin[(int)(sp * Dsp.N)]; }
                        if (filt)
                        {
                            float v3 = v - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
                            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2; v = v2;
                        }
                        buf[i] = v * NextEnv();
                    }
                    for (int i = 0; i < n; i++) fenv *= fenvCoef;
                    break;
                }
                case Kind.Pluck:
                {
                    int M = ks.Length;
                    float sm = ins.Smooth, d = 1f / inc - sm / (1f - sm);
                    if (d > M - 4) d = M - 4; if (d < 2) d = 2;
                    for (int i = 0; i < n; i++)
                    {
                        float rp = ksW - d; if (rp < 0) rp += M;
                        int i0 = (int)rp; float fr = rp - i0; int i1 = i0 + 1; if (i1 >= M) i1 = 0;
                        float x = ks[i0] + (ks[i1] - ks[i0]) * fr;
                        float y = (1f - sm) * x + sm * ksPrev;
                        ksPrev = y;
                        ks[ksW] = y * ksLoss;
                        if (++ksW >= M) ksW = 0;
                        buf[i] = y * NextEnv();
                    }
                    break;
                }
                case Kind.Flute:
                {
                    float breathAmt = ins.Breath * (0.6f + 2f * Math.Max(0f, 1f - Age / 0.08f));
                    for (int i = 0; i < n; i++)
                    {
                        float p = ph[0];
                        float s = Dsp.S(p) + 0.2f * Dsp.S(2 * p) + 0.06f * Dsp.S(3 * p);
                        p += inc; if (p >= 1f) p -= 1f; ph[0] = p;
                        nLp += 0.25f * (Noise() - nLp); nLp2 += 0.25f * (nLp - nLp2);
                        float e = NextEnv();
                        buf[i] = (s + (nLp - nLp2) * 4f * breathAmt) * e;
                    }
                    break;
                }
            }
        done:
            float gain = (Drum >= 0 ? 0.55f : ins.Gain) * vamp;
            for (int i = 0; i < n; i++) { float s = buf[i] * gain; buf[i] = s; float a = s < 0 ? -s : s; if (a > peak) peak = a; }
            Age += n / sr;
            if (Drum < 0)
            {
                if (stage == 3 && env < 1e-4f) Active = false;
                else if (Age > 0.05f && peak < 2e-5f && (stage >= 1 && (sus <= 0 || ins.Kind == Kind.Additive || ins.Kind == Kind.Pluck))) Active = false;
            }
            else
            {
                if (stage == 3) { for (int i = 0; i < n; i++) buf[i] *= NextEnv(); if (env < 1e-4f) Active = false; }
                if (e2 < 1e-4f) Active = false;
            }
            return Active;
        }

        float NextEnv()
        {
            switch (stage)
            {
                case 0: env += attInc; if (env >= 1f) { env = 1f; stage = 1; } break;
                case 1: env = sus + (env - sus) * decCoef; if (env - sus < 1e-4f) { env = sus; stage = 2; } break;
                case 3: env *= relCoef; break;
            }
            return env;
        }

        float Metal()
        {
            float s = 0;
            for (int k = 0; k < 6; k++)
            {
                float p = ph[2 + k] + pr[k]; if (p >= 1f) p -= 1f; ph[2 + k] = p;
                s += p < 0.5f ? 1f : -1f;
            }
            return s / 6f;
        }

        float HighPass(float x)
        {
            // two cascaded one-pole high-passes (~6 kHz)
            hpA += 0.55f * (x - hpA); float h1 = x - hpA;
            hpB += 0.55f * (h1 - hpB); return h1 - hpB;
        }

        float RenderDrum(float[] buf, int n, float vamp)
        {
            for (int i = 0; i < n; i++)
            {
                float s = 0;
                switch (Drum)
                {
                    case 0: // kick
                    {
                        float f = 46f + 130f * e1;
                        ph[0] += f / sr; if (ph[0] >= 1f) ph[0] -= 1f;
                        s = Dsp.S(ph[0]) * e2 * 1.3f + Noise() * e3 * 0.3f;
                        e1 *= c1; e2 *= c2; e3 *= c3; break;
                    }
                    case 1: // rim
                        ph[0] += 1700f / sr; if (ph[0] >= 1f) ph[0] -= 1f;
                        s = (Dsp.S(ph[0]) * 0.7f + Noise() * 0.3f) * e2; e2 *= c2; break;
                    case 2: // snare
                    {
                        ph[0] += 185f / sr; if (ph[0] >= 1f) ph[0] -= 1f;
                        ph[1] += 330f / sr; if (ph[1] >= 1f) ph[1] -= 1f;
                        float nz = Noise(); hpC += 0.35f * (nz - hpC);
                        s = (Dsp.S(ph[0]) * 0.55f + Dsp.S(ph[1]) * 0.25f) * e1 + (nz - hpC) * e2 * 0.8f;
                        e1 *= c1; e2 *= c2; break;
                    }
                    case 3: // clap
                    {
                        float t = Age + i / sr, burst = 1f;
                        if (t < 0.03f) { float m = t % 0.01f; burst = (float)Math.Exp(-m / 0.002f) * 1.4f; }
                        float nz = Noise(); nLp += 0.45f * (nz - nLp); nLp2 += 0.15f * (nLp - nLp2);
                        s = (nLp - nLp2) * 2.2f * burst * e2; e2 *= c2; break;
                    }
                    case 4: // tom
                    {
                        float f = f0 * (1f + 0.5f * e1);
                        ph[0] += f / sr; if (ph[0] >= 1f) ph[0] -= 1f;
                        s = Dsp.S(ph[0]) * e2 * 1.1f; e1 *= c1; e2 *= c2; break;
                    }
                    case 5: case 6: case 7: // hats
                        s = HighPass(Metal() * 0.8f + Noise() * 0.35f) * e2 * 1.2f; e2 *= c2; break;
                    case 8: // crash
                        s = HighPass(Metal() * 0.4f + Noise() * 0.7f) * e2 * 0.9f; e2 *= c2; break;
                    case 9: // ride
                        s = HighPass(Metal() * 0.8f + Noise() * 0.15f) * e2 * 0.8f; e2 *= c2; break;
                    case 10: // cowbell
                    {
                        ph[0] += 540f / sr; if (ph[0] >= 1f) ph[0] -= 1f;
                        ph[1] += 800f / sr; if (ph[1] >= 1f) ph[1] -= 1f;
                        s = ((ph[0] < 0.5f ? 1f : -1f) + (ph[1] < 0.5f ? 1f : -1f)) * 0.25f * e2; e2 *= c2; break;
                    }
                }
                buf[i] = s;
            }
            return 0;
        }
    }

    // Small Freeverb-style reverb (4 combs + 2 allpasses per side).
    sealed class Reverb
    {
        readonly float[][] comb = new float[8][]; readonly int[] cIdx = new int[8]; readonly float[] cLp = new float[8];
        readonly float[][] ap = new float[4][]; readonly int[] aIdx = new int[4];
        const float Feedback = 0.83f, Damp = 0.25f;
        public Reverb(float sr)
        {
            int[] cl = { 1116, 1188, 1277, 1356 }; int[] al = { 556, 441 };
            float sc = sr / 44100f;
            for (int i = 0; i < 4; i++) { comb[i] = new float[(int)(cl[i] * sc)]; comb[i + 4] = new float[(int)((cl[i] + 23) * sc)]; }
            for (int i = 0; i < 2; i++) { ap[i] = new float[(int)(al[i] * sc)]; ap[i + 2] = new float[(int)((al[i] + 23) * sc)]; }
        }
        public void Process(float input, out float l, out float r)
        {
            l = 0; r = 0;
            for (int i = 0; i < 4; i++) { l += Comb(i, input); r += Comb(i + 4, input); }
            l = Allpass(1, Allpass(0, l)); r = Allpass(3, Allpass(2, r));
        }
        float Comb(int i, float x)
        {
            float[] b = comb[i]; int j = cIdx[i]; float y = b[j];
            cLp[i] = y * (1 - Damp) + cLp[i] * Damp;
            b[j] = x + cLp[i] * Feedback;
            if (++j >= b.Length) j = 0; cIdx[i] = j; return y;
        }
        float Allpass(int i, float x)
        {
            float[] b = ap[i]; int j = aIdx[i]; float bo = b[j];
            b[j] = x + bo * 0.5f;
            if (++j >= b.Length) j = 0; aIdx[i] = j; return bo - x;
        }
    }

    sealed class Synth
    {
        public const int MaxVoices = 32;
        const int Block = 32;
        public readonly Instrument[] Instruments = Instrument.All();
        volatile int current;
        public int Current { get { return current; } }
        Voice[] voices = new Voice[0];
        float sr;
        readonly float[] bend = new float[16], modw = new float[16];
        readonly bool[] pedal = new bool[16];
        public float Volume = 0.8f, Bright = 1f, Tilt = 0f, ReverbMix = 0.25f, ReleaseMul = 1f, BendRange = 2f;
        /// CC numbers of knobs 1-5: volume, tone, reverb, release, compressor
        public int[] KnobCC = { 70, 71, 72, 73, 74 };
        /// Last value (0-127) of each knob, -1 = never set. Saved to the settings file.
        public readonly int[] KnobValues = { -1, -1, -1, -1, -1 };
        public volatile bool KnobsDirty;
        public float Comp; // 0 = off .. 1 = very tight
        float compEnv, compGain = 1f, compAtk, compRel;
        float tiltL, tiltR, tiltCoef;
        Reverb rev; float lfo; long order; int quietSamples = int.MaxValue / 2;
        readonly float[] vb = new float[Block];
        public Action Activity; // called on note-on so the audio thread can wake up
        public int DrumChannel = 9;

        public Synth() { SetSampleRate(48000); }

        public void SetSampleRate(float rate)
        {
            lock (this)
            {
                if (rate == sr && voices.Length > 0) return;
                sr = rate;
                voices = new Voice[MaxVoices];
                for (int i = 0; i < MaxVoices; i++) voices[i] = new Voice(sr, (uint)(0x9E3779B9u * (uint)(i + 1)));
                rev = new Reverb(sr);
                tiltCoef = 1f - (float)Math.Exp(-2 * Math.PI * 650.0 / sr);
                compAtk = 1f - (float)Math.Exp(-1.0 / (0.002 * sr));
                compRel = 1f - (float)Math.Exp(-1.0 / (0.15 * sr));
            }
        }

        /// True when nothing has been audible for a while (incl. reverb tail).
        public bool IsIdle { get { return quietSamples > sr * 3; } }

        public void SetInstrument(int i)
        {
            if (i < 0 || i >= Instruments.Length) return;
            current = i;
        }

        public void NoteOn(int ch, int note, int velocity)
        {
            lock (this)
            {
                float vel = Math.Max(1, velocity) / 127f;
                // compressor knob also evens out how hard you hit the keys
                vel += (0.8f - vel) * Comp * 0.9f;
                bool drum = ch == DrumChannel;
                // retriggering the same key: fade the old one quickly
                for (int i = 0; i < voices.Length; i++)
                {
                    Voice o = voices[i];
                    if (o.Active && o.Chan == ch && o.Note == note && !o.Released) { if (drum) o.Kill(); else o.Release(0.15f); }
                }
                Voice v = Allocate();
                float tom = 100;
                int d = drum ? Voice.DrumFor(note, out tom) : -1;
                if (drum) { v.SetTom(tom); }
                v.Start(Instruments[current], ch, note, vel, ++order, d);
                quietSamples = 0;
            }
            Action a = Activity; if (a != null) a();
        }

        Voice Allocate()
        {
            Voice best = null;
            for (int i = 0; i < voices.Length; i++) if (!voices[i].Active) return voices[i];
            // steal: oldest released voice, otherwise oldest
            for (int i = 0; i < voices.Length; i++)
            {
                Voice v = voices[i];
                if (best == null || (v.Released && !best.Released) || (v.Released == best.Released && v.Order < best.Order)) best = v;
            }
            return best;
        }

        public void NoteOff(int ch, int note)
        {
            lock (this)
            {
                for (int i = 0; i < voices.Length; i++)
                {
                    Voice v = voices[i];
                    if (!v.Active || v.Released || v.Chan != ch || v.Note != note || v.Drum >= 0) continue;
                    if (pedal[ch]) v.Held = true; else v.Release(ReleaseMul);
                }
            }
        }

        public void PitchBend(int ch, int value14)
        {
            lock (this) bend[ch] = Math.Max(-1f, (value14 - 8192) / 8192f);
        }

        public void ControlChange(int ch, int cc, int val)
        {
            lock (this)
            {
                float f = val / 127f;
                int knob = -1;
                for (int i = 0; i < KnobCC.Length; i++) if (cc == KnobCC[i]) { knob = i; break; }
                if (knob < 0) { if (cc == 7) knob = 0; else if (cc == 91) knob = 2; }
                if (knob >= 0)
                {
                    SetKnob(knob, f);
                    if (KnobValues[knob] != val) { KnobValues[knob] = val; KnobsDirty = true; }
                    return;
                }
                switch (cc)
                {
                    case 1: modw[ch] = f; break;
                    case 64:
                        pedal[ch] = val >= 64;
                        if (!pedal[ch])
                            for (int i = 0; i < voices.Length; i++)
                            { Voice v = voices[i]; if (v.Active && v.Held && v.Chan == ch) { v.Held = false; v.Release(ReleaseMul); } }
                        break;
                    case 120: case 123: AllOff(); break;
                }
            }
        }

        void SetKnob(int knob, float f)
        {
            switch (knob)
            {
                case 0: Volume = f; break;
                case 1:
                    // tone: centre = neutral, left = darker, right = brighter (all instruments)
                    Tilt = f < 0.5f ? -0.95f * (1f - 2f * f) : 2.5f * (2f * f - 1f);
                    Bright = 0.2f + 2.3f * f * f;
                    break;
                case 2: ReverbMix = 1.1f * f; break;
                case 3: ReleaseMul = 0.15f + 7.85f * f * f; break;
                case 4: Comp = f; break;
            }
        }

        public void AllOff()
        {
            lock (this)
            {
                for (int i = 0; i < voices.Length; i++) if (voices[i].Active) voices[i].Kill();
                for (int c = 0; c < 16; c++) { pedal[c] = false; }
            }
        }

        public int ActiveVoices
        {
            get { int n = 0; lock (this) for (int i = 0; i < voices.Length; i++) if (voices[i].Active) n++; return n; }
        }

        // Fill interleaved-free stereo buffers.
        public void Render(float[] L, float[] R, int frames)
        {
            lock (this)
            {
                int pos = 0;
                while (pos < frames)
                {
                    int n = Math.Min(Block, frames - pos);
                    for (int i = 0; i < n; i++) { L[pos + i] = 0; R[pos + i] = 0; }
                    bool any = false;
                    float lfoS = Dsp.S(lfo);
                    for (int vi = 0; vi < voices.Length; vi++)
                    {
                        Voice v = voices[vi];
                        if (!v.Active) continue;
                        any = true;
                        float semis = bend[v.Chan] * BendRange;
                        if (v.Drum < 0)
                        {
                            float vibDepth = modw[v.Chan] * 0.45f;
                            if (v.Ins.Vibrato > 0) vibDepth += v.Ins.Vibrato * Math.Min(1f, Math.Max(0f, (v.Age - v.Ins.VibratoDelay) / 0.4f));
                            semis += vibDepth * lfoS;
                        }
                        v.Render(vb, n, semis, 0, Bright, lfo);
                        float pl = v.PanL, pr = v.PanR;
                        for (int i = 0; i < n; i++) { L[pos + i] += vb[i] * pl; R[pos + i] += vb[i] * pr; }
                    }
                    lfo += 5.5f * n / sr; if (lfo >= 1f) lfo -= 1f;
                    if (any) quietSamples = 0; else quietSamples += n;

                    float wet = ReverbMix, vol = Volume * Volume * 1.6f;
                    float thr = 1f, slope = 0f, makeup = 1f;
                    if (Comp > 0.01f)
                    {
                        thr = (float)Math.Pow(10, (-4f - 26f * Comp) / 20f);
                        slope = 1f - 1f / (1f + 9f * Comp);
                        makeup = (float)Math.Pow(1f / thr, slope * 0.45f);
                    }
                    for (int i = 0; i < n; i++)
                    {
                        float l = L[pos + i], r = R[pos + i];
                        if (wet > 0.001f)
                        {
                            float rl, rr;
                            rev.Process((l + r) * 0.02f, out rl, out rr);
                            l += rl * wet * 3f; r += rr * wet * 3f;
                        }
                        if (Tilt != 0f)
                        {
                            // tilt EQ around ~1 kHz: x + t*(x - lowpass(x))
                            tiltL += tiltCoef * (l - tiltL); tiltR += tiltCoef * (r - tiltR);
                            l += Tilt * (l - tiltL); r += Tilt * (r - tiltR);
                        }
                        l *= vol; r *= vol;
                        if (Comp > 0.01f)
                        {
                            // peak compressor: threshold falls and ratio rises as the knob turns up,
                            // with make-up gain so loudness stays about the same
                            float pk = Math.Max(l < 0 ? -l : l, r < 0 ? -r : r);
                            compEnv += (pk > compEnv ? compAtk : compRel) * (pk - compEnv);
                            float g = compEnv > thr ? (float)Math.Pow(thr / compEnv, slope) : 1f;
                            compGain += 0.02f * (g - compGain);
                            l *= compGain * makeup; r *= compGain * makeup;
                        }
                        L[pos + i] = SoftClip(l); R[pos + i] = SoftClip(r);
                    }
                    pos += n;
                }
            }
        }

        static float SoftClip(float x)
        {
            if (x > 3f) return 1f; if (x < -3f) return -1f;
            float x2 = x * x; return x * (27f + x2) / (27f + 9f * x2);
        }
    }
}
