// ProceduralAudio: makes every sound in the game from maths, no audio files needed.
//
// Phase 9 rebuilt the ambience and music to be soothing, in STEREO:
//   - Rain follows how real rain sounds: millions of tiny bubbles that add up to
//     PINK noise. It's layered: a low "boil" and a mid "boil" (pink noise through
//     band-pass filters whose centre wanders slowly), a faint airy layer, and a
//     few soft individual drops (10-20 ms sine chirps), all gently low-passed.
//     The old rain was high-passed white noise with clicks: harsh.
//   - Waves: surf that gets brighter as each wave rises and washes across the
//     stereo field, plus soft lapping close by.
//   - Wind: a gentle band-passed breath.
//   - Music: a slow, warm pad (Fmaj7 - Am7 - Cmaj7 - G6) with soft kalimba bells,
//     through a reverb. The "intense" layer is a soft marimba pattern and a
//     heartbeat bass instead of sharp plucks. Both loop seamlessly in sync.
//
// Building blocks: sine waves, pink/brown noise, biquad filters (RBJ cookbook),
// envelopes, and a small Freeverb-style reverb (comb + all-pass filters).
// Loops are seamless: frequencies fit a whole number of cycles in the loop, and
// effects (filters, reverb) run over the loop twice so the tail wraps around.
// Nothing here touches Unity objects, so it can run on a background thread.

using System;

namespace StarlitTides
{
    /// <summary>Generated audio: samples, interleaved when stereo.</summary>
    public class SoundData
    {
        public float[] samples;
        public int channels;
        public SoundData(float[] s, int c) { samples = s; channels = c; }
    }

    public static class ProceduralAudio
    {
        // 24 kHz (Phase 9 optimisation, was 44.1 kHz): every sound is filtered well below
        // the 12 kHz this can carry, so it sounds the same at ~55% of the memory.
        public const int SampleRate = 24000;
        const float TwoPi = 6.2831853f;

        // ------------------------------------------------------------------
        // Building blocks
        // ------------------------------------------------------------------
        static float[] Buffer(float seconds) { return new float[(int)(seconds * SampleRate)]; }

        static float Noise(Random rng) { return (float)(rng.NextDouble() * 2.0 - 1.0); }

        // Pink noise (Paul Kellet's filter): equal energy per octave, like rain and surf.
        class Pink
        {
            float b0, b1, b2, b3, b4, b5, b6;
            readonly Random rng;
            public Pink(Random r) { rng = r; }
            public float Next()
            {
                float w = Noise(rng);
                b0 = 0.99886f * b0 + w * 0.0555179f; b1 = 0.99332f * b1 + w * 0.0750759f;
                b2 = 0.96900f * b2 + w * 0.1538520f; b3 = 0.86650f * b3 + w * 0.3104856f;
                b4 = 0.55000f * b4 + w * 0.5329522f; b5 = -0.7616f * b5 - w * 0.0168980f;
                float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
                b6 = w * 0.115926f;
                return pink * 0.11f;
            }
        }

        // Biquad filter (Robert Bristow-Johnson's cookbook).
        class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public Biquad LowPass(float f, float q) { Set(f, q, 0); return this; }
            public Biquad HighPass(float f, float q) { Set(f, q, 1); return this; }
            public Biquad BandPass(float f, float q) { Set(f, q, 2); return this; }
            public void Set(float f, float q, int type)
            {
                f = Math.Max(20f, Math.Min(f, SampleRate * 0.45f));
                double w = TwoPi * f / SampleRate, cs = Math.Cos(w), alpha = Math.Sin(w) / (2.0 * q);
                double a0 = 1.0 + alpha;
                double nb0, nb1, nb2;
                if (type == 0) { nb0 = (1 - cs) / 2; nb1 = 1 - cs; nb2 = (1 - cs) / 2; }
                else if (type == 1) { nb0 = (1 + cs) / 2; nb1 = -(1 + cs); nb2 = (1 + cs) / 2; }
                else { nb0 = alpha; nb1 = 0; nb2 = -alpha; }
                b0 = (float)(nb0 / a0); b1 = (float)(nb1 / a0); b2 = (float)(nb2 / a0);
                a1 = (float)(-2 * cs / a0); a2 = (float)((1 - alpha) / a0);
            }
            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
            public void Run(float[] d) { for (int i = 0; i < d.Length; i++) d[i] = Process(d[i]); }
        }

        // A slow random wander between 0 and 1 (for filter centres and gusts).
        class Wander
        {
            readonly Random rng; float value = 0.5f, target = 0.5f; readonly float rate;
            public Wander(Random r, float changesPerSecond) { rng = r; rate = changesPerSecond; }
            public float Next()
            {
                if (rng.NextDouble() < rate / SampleRate) target = (float)rng.NextDouble();
                value += (target - value) * (rate * 2f / SampleRate);
                return value;
            }
        }

        static float[] Normalize(float[] data, float peak)
        {
            float max = 0.0001f;
            foreach (float s in data) max = Math.Max(max, Math.Abs(s));
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
            return data;
        }

        static void NormalizePair(float[] l, float[] r, float peak)
        {
            float max = 0.0001f;
            foreach (float s in l) max = Math.Max(max, Math.Abs(s));
            foreach (float s in r) max = Math.Max(max, Math.Abs(s));
            float k = peak / max;
            for (int i = 0; i < l.Length; i++) { l[i] *= k; r[i] *= k; }
        }

        // Blends the end of a sound into its start, so it loops without a click.
        static float[] MakeLoop(float[] data, float fadeSeconds)
        {
            int fade = (int)(fadeSeconds * SampleRate);
            int length = data.Length - fade;
            float[] result = new float[length];
            Array.Copy(data, result, length);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                // equal-power crossfade keeps noise loops from dipping in the middle
                float a = (float)Math.Sin(t * Math.PI * 0.5), b = (float)Math.Cos(t * Math.PI * 0.5);
                result[i] = data[i] * a + data[length + i] * b;
            }
            return result;
        }

        static float[] Interleave(float[] l, float[] r)
        {
            int n = Math.Min(l.Length, r.Length);
            float[] d = new float[n * 2];
            for (int i = 0; i < n; i++) { d[i * 2] = l[i]; d[i * 2 + 1] = r[i]; }
            return d;
        }

        static float NoteToHz(int midi) { return 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0); }

        // Rounds a frequency so it makes a whole number of cycles in a loop.
        static float LoopSafe(float hz, float loopSeconds) { return (float)Math.Round(hz * loopSeconds) / loopSeconds; }

        // A soft bell / kalimba tone: a sine with a quiet inharmonic partial, slow-ish decay.
        static void AddBell(float[] data, float startSeconds, float frequency, float volume, float decay, float attack = 0.004f)
        {
            float[] partials = { 1f, 2.0f, 2.76f, 5.4f };
            float[] amps = { 1f, 0.22f, 0.12f, 0.03f };
            int start = (int)(startSeconds * SampleRate);
            int length = (int)(Math.Min(6f, 7f / decay) * SampleRate);
            for (int i = 0; i < length; i++)
            {
                int index = (start + i) % data.Length;   // wraps around, so it also works in loops
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / attack) * (float)Math.Exp(-t * decay);
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += amps[p] * (float)Math.Exp(-t * decay * p * 0.8f) * (float)Math.Sin(TwoPi * frequency * partials[p] * t);
                data[index] += s * env * volume;
            }
        }

        // Freeverb-style reverb (Schroeder): 4 parallel combs into 2 all-passes.
        // The input is processed twice in a row and the second pass kept, so the
        // tail of the end of a loop rings on into its start.
        static float[] ReverbLoop(float[] dry, float mix, float room, int spread)
        {
            // (Freeverb's delays are given for 44.1 kHz: scale them to our sample rate.)
            float rateScale = SampleRate / 44100f;
            int[] combs = { (int)((1557 + spread) * rateScale), (int)((1617 + spread) * rateScale), (int)((1491 + spread) * rateScale), (int)((1422 + spread) * rateScale) };
            int[] alls = { (int)((556 + spread) * rateScale), (int)((441 + spread) * rateScale) };
            float[][] cb = new float[4][]; int[] ci = new int[4]; float[] cf = new float[4];
            for (int k = 0; k < 4; k++) cb[k] = new float[combs[k] * 2];   // x2: a bigger room
            float[][] ab = new float[2][]; int[] ai = new int[2];
            for (int k = 0; k < 2; k++) ab[k] = new float[alls[k]];
            float damp = 0.35f;
            float[] wet = new float[dry.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < dry.Length; i++)
                {
                    float x = dry[i] * 0.2f, sum = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        float o = cb[k][ci[k]];
                        cf[k] = o * (1f - damp) + cf[k] * damp;
                        cb[k][ci[k]] = x + cf[k] * room;
                        ci[k] = (ci[k] + 1) % cb[k].Length;
                        sum += o;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float b = ab[k][ai[k]];
                        float o = -sum + b;
                        ab[k][ai[k]] = sum + b * 0.5f;
                        ai[k] = (ai[k] + 1) % ab[k].Length;
                        sum = o;
                    }
                    if (pass == 1) wet[i] = sum;
                }
            }
            float[] result = new float[dry.Length];
            for (int i = 0; i < dry.Length; i++) result[i] = dry[i] * (1f - mix * 0.5f) + wet[i] * mix;
            return result;
        }

        // ------------------------------------------------------------------
        // Ambience loops (stereo)
        // ------------------------------------------------------------------
        public static SoundData Rain(int seed)
        {
            const float seconds = 12f;
            float[] l = RainChannel(seed, seconds), r = RainChannel(seed + 101, seconds);
            NormalizePair(l, r, 0.55f);
            return new SoundData(Interleave(MakeLoop(l, 1.5f), MakeLoop(r, 1.5f)), 2);
        }

        static float[] RainChannel(int seed, float seconds)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(seconds + 1.5f);
            Pink pink = new Pink(rng);
            Biquad low = new Biquad(), mid = new Biquad();
            Biquad airHp = new Biquad().HighPass(3500f, 0.7f), airLp = new Biquad().LowPass(8000f, 0.7f);
            Wander lowW = new Wander(rng, 0.35f), midW = new Wander(rng, 0.5f), gust = new Wander(rng, 0.12f);
            for (int i = 0; i < d.Length; i++)
            {
                if (i % 64 == 0)
                {
                    low.Set(380f + 320f * lowW.Next(), 0.8f, 2);    // low "boil" (the body of the rain)
                    mid.Set(1500f + 1300f * midW.Next(), 1.0f, 2);   // mid "boil" (the patter)
                }
                float p = pink.Next();
                float s = low.Process(p) * 1.0f + mid.Process(p) * 0.55f + airLp.Process(airHp.Process(p)) * 0.12f;
                d[i] = s * (0.82f + 0.18f * gust.Next());
            }

            // Individual drops: tiny bubbles, a sine that chirps upward and dies in ~15 ms.
            int drops = (int)(seconds * 22f);
            for (int n = 0; n < drops; n++)
            {
                int start = rng.Next(d.Length);
                float f0 = 900f + (float)rng.NextDouble() * 2600f;
                float amp = 0.015f + (float)rng.NextDouble() * 0.04f;
                float life = 0.010f + (float)rng.NextDouble() * 0.012f;
                int len = (int)(life * 4f * SampleRate);
                float phase = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float f = f0 * (1f + 0.6f * t / life);
                    phase += TwoPi * f / SampleRate;
                    d[(start + i) % d.Length] += (float)Math.Sin(phase) * amp * (float)Math.Exp(-t / life) * Math.Min(1f, t / 0.0015f);
                }
            }

            // Soften the very top and bottom.
            new Biquad().LowPass(6500f, 0.6f).Run(d);
            new Biquad().HighPass(90f, 0.6f).Run(d);
            return d;
        }

        public static SoundData Waves(int seed)
        {
            const float seconds = 16f;
            float[] l = WaveChannel(seed, seconds, 0f), r = WaveChannel(seed + 37, seconds, 1.9f);
            NormalizePair(l, r, 0.75f);
            return new SoundData(Interleave(MakeLoop(l, 2f), MakeLoop(r, 2f)), 2);
        }

        static float[] WaveChannel(int seed, float seconds, float phaseShift)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(seconds + 2f);
            Pink pink = new Pink(rng);
            Biquad surf = new Biquad(), lap = new Biquad();
            float brown = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                // A wave rises, breaks and drains away (~7.5 s), a smaller one between.
                float swell = 0.5f + 0.5f * (float)Math.Sin(TwoPi * (t + phaseShift) / 7.5f + 0.6f * Math.Sin(TwoPi * t / 13f));
                swell = swell * swell;
                if (i % 64 == 0)
                {
                    surf.Set(260f + 900f * swell, 0.6f, 0);   // brighter as the wave breaks
                    lap.Set(700f + 250f * (float)Math.Sin(TwoPi * t * 0.45f), 1.4f, 2);
                }
                brown = brown * 0.997f + Noise(rng) * 0.06f;
                float p = pink.Next();
                float s = surf.Process(brown * 0.7f + p * 0.6f) * (0.25f + 0.75f * swell);
                float lapping = lap.Process(p) * 0.35f * (0.5f + 0.5f * (float)Math.Sin(TwoPi * t * 0.55f + phaseShift)) * (1f - swell * 0.5f);
                d[i] = s + lapping;
            }
            new Biquad().HighPass(40f, 0.7f).Run(d);
            return d;
        }

        public static SoundData Wind(int seed)
        {
            const float seconds = 12f;
            float[] l = WindChannel(seed, seconds), r = WindChannel(seed + 11, seconds);
            NormalizePair(l, r, 0.6f);
            return new SoundData(Interleave(MakeLoop(l, 1.5f), MakeLoop(r, 1.5f)), 2);
        }

        static float[] WindChannel(int seed, float seconds)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(seconds + 1.5f);
            Pink pink = new Pink(rng);
            Biquad bp = new Biquad(), bp2 = new Biquad();
            Wander centre = new Wander(rng, 0.3f), level = new Wander(rng, 0.2f);
            for (int i = 0; i < d.Length; i++)
            {
                if (i % 64 == 0)
                {
                    float c = centre.Next();
                    bp.Set(260f + 420f * c, 1.6f, 2);
                    bp2.Set(900f + 600f * c, 3f, 2);   // a faint whistle riding on top
                }
                float p = pink.Next();
                float lv = level.Next();
                d[i] = (bp.Process(p) + bp2.Process(p) * 0.15f) * (0.35f + 0.65f * lv * lv);
            }
            return d;
        }

        // ------------------------------------------------------------------
        // Water and sea life (Phase 9): soft, rounded, low; nothing sharp
        // ------------------------------------------------------------------

        // The boat moving through water: a gentle hull swish (pink noise through a
        // slowly wandering band-pass), soft bubbles trickling along the hull, and a
        // low lap of small waves. AudioManager raises volume and brightness with speed.
        public static SoundData BoatWater(int seed)
        {
            const float seconds = 10f;
            float[] l = BoatWaterChannel(seed, seconds, 0f), r = BoatWaterChannel(seed + 71, seconds, 1.3f);
            NormalizePair(l, r, 0.6f);
            return new SoundData(Interleave(MakeLoop(l, 1.5f), MakeLoop(r, 1.5f)), 2);
        }

        static float[] BoatWaterChannel(int seed, float seconds, float phase)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(seconds + 1.5f);
            Pink pink = new Pink(rng);
            Biquad swish = new Biquad(), lap = new Biquad().LowPass(240f, 0.7f);
            Wander centre = new Wander(rng, 0.4f), level = new Wander(rng, 0.3f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                if (i % 64 == 0) swish.Set(280f + 260f * centre.Next(), 0.7f, 2);   // low and soft: a breath of water, not a hiss
                float p = pink.Next();
                // small waves slapping the hull every second or so
                float slap = 0.6f + 0.4f * (float)Math.Sin(TwoPi * t * 0.8f + phase) * (float)Math.Sin(TwoPi * t * 0.31f + phase * 2f);
                float lv = level.Next();
                d[i] = swish.Process(p) * (0.55f + 0.45f * lv) * slap + lap.Process(p) * 0.8f * (0.7f + 0.3f * slap);
            }
            // bubbles: soft low sine chirps along the hull (like the rain drops, but lower and rounder)
            int bubbles = (int)(seconds * 5f);   // just a few, now and then
            for (int n = 0; n < bubbles; n++)
            {
                int start = rng.Next(d.Length);
                float f0 = 320f + (float)rng.NextDouble() * 650f;
                float amp = 0.004f + (float)rng.NextDouble() * 0.008f;
                float life = 0.03f + (float)rng.NextDouble() * 0.04f;
                int len = (int)(life * 4f * SampleRate);
                float ph = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    ph += TwoPi * f0 * 0.7f * (1f + 0.3f * t / life) / SampleRate;
                    d[(start + i) % d.Length] += (float)Math.Sin(ph) * amp * (float)Math.Exp(-t / life) * Math.Min(1f, t / 0.004f);
                }
            }
            new Biquad().LowPass(1400f, 0.6f).Run(d);
            new Biquad().LowPass(1400f, 0.6f).Run(d);   // twice: a gentle, steep roll-off of anything bright
            new Biquad().HighPass(60f, 0.7f).Run(d);
            return d;
        }

        // A splash: a soft burst of water (low-passed pink noise, rounded attack) with a
        // bubbly tail and, for big ones, a deep "whoomp" underneath. Size 0..1.
        public static float[] SoftSplash(int seed, float size)
        {
            Random rng = new Random(seed);
            float seconds = 1.2f + size * 1.6f;
            float[] d = Buffer(seconds);
            Pink pink = new Pink(rng);
            Biquad lp = new Biquad().LowPass(Lerp(1000f, 450f, size), 0.6f);
            Biquad lp2 = new Biquad().LowPass(Lerp(1000f, 450f, size), 0.6f);
            float decay = Lerp(3.2f, 1.6f, size);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / Lerp(0.04f, 0.08f, size)) * (float)Math.Exp(-t * decay);   // a rounded, soft onset
                d[i] = lp2.Process(lp.Process(pink.Next())) * env * 1.6f;
                // the deep whoomp of a big body hitting the water
                d[i] += (float)Math.Sin(TwoPi * Lerp(90f, 48f, size) * t) * (float)Math.Exp(-t * 6f) * Math.Min(1f, t / 0.02f) * size * 0.7f;
            }
            int bubbles = (int)(4 + size * 12);
            for (int n = 0; n < bubbles; n++)
            {
                float start = 0.05f + (float)rng.NextDouble() * seconds * 0.6f;
                float f0 = Lerp(700f, 260f, size) + (float)rng.NextDouble() * 400f;
                float amp = 0.015f * (float)Math.Exp(-start * 2f);
                float life = 0.02f + (float)rng.NextDouble() * 0.04f;
                int s0 = (int)(start * SampleRate), len = (int)(life * 4f * SampleRate);
                float ph = 0f;
                for (int i = 0; i < len && s0 + i < d.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    ph += TwoPi * f0 * (1f + 0.4f * t / life) / SampleRate;
                    d[s0 + i] += (float)Math.Sin(ph) * amp * (float)Math.Exp(-t / life) * Math.Min(1f, t / 0.004f);
                }
            }
            d = ReverbOneShot(d, 0.5f);   // more room: it blends into the night instead of popping out
            return Normalize(d, Lerp(0.5f, 0.7f, size));
        }

        // A bubble net forming: soft bubbles rising, thickening over three seconds.
        public static float[] BubbleNet(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(3.4f);
            int bubbles = 260;
            for (int n = 0; n < bubbles; n++)
            {
                // more bubbles later (the net thickens)
                float u = (float)Math.Sqrt(rng.NextDouble());
                float start = u * 2.9f;
                float f0 = 240f + (float)rng.NextDouble() * 520f;
                float amp = 0.02f + (float)rng.NextDouble() * 0.03f;
                float life = 0.03f + (float)rng.NextDouble() * 0.05f;
                int s0 = (int)(start * SampleRate), len = (int)(life * 4f * SampleRate);
                float ph = 0f;
                for (int i = 0; i < len && s0 + i < d.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    ph += TwoPi * f0 * (1f + 0.7f * t / life) / SampleRate;
                    d[s0 + i] += (float)Math.Sin(ph) * amp * (float)Math.Exp(-t / life) * Math.Min(1f, t / 0.005f);
                }
            }
            // a low murmur of moving water underneath
            Pink pink = new Pink(rng);
            Biquad lp = new Biquad().LowPass(300f, 0.7f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] += lp.Process(pink.Next()) * 0.6f * Math.Min(1f, t / 1.2f) * Math.Min(1f, (3.4f - t) / 0.4f);
            }
            new Biquad().LowPass(1100f, 0.6f).Run(d);
            d = ReverbOneShot(d, 0.45f);
            return Normalize(d, 0.5f);
        }

        // The orcas' wave sweeping past: a long, soft rush that swells and fades.
        public static float[] WaveSweep(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(3f);
            Pink pink = new Pink(rng);
            Biquad bp = new Biquad();
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float swell = (float)Math.Sin(Math.PI * Math.Min(1f, t / 3f));
                if (i % 64 == 0) bp.Set(300f + 900f * swell, 0.7f, 0);
                d[i] = bp.Process(pink.Next()) * swell * swell;
            }
            return Normalize(d, 0.6f);
        }

        // A soft whistle: a gliding sine with gentle vibrato (dolphins high, orcas lower).
        public static float[] Whistle(int seed, float baseHz, int count)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.5f + count * 0.45f);
            for (int w = 0; w < count; w++)
            {
                float start = w * 0.4f + (float)rng.NextDouble() * 0.1f;
                float len = 0.25f + (float)rng.NextDouble() * 0.2f;
                float f0 = baseHz * (0.85f + (float)rng.NextDouble() * 0.3f), f1 = f0 * (1.1f + (float)rng.NextDouble() * 0.4f);
                int s0 = (int)(start * SampleRate), n = (int)(len * SampleRate);
                float ph = 0f;
                for (int i = 0; i < n && s0 + i < d.Length; i++)
                {
                    float t = i / (float)n;
                    float f = Lerp(f0, f1, (float)Math.Sin(t * Math.PI * 0.5)) + (float)Math.Sin(TwoPi * 7f * t * len) * f0 * 0.015f;
                    ph += TwoPi * f / SampleRate;
                    float env = (float)Math.Sin(Math.PI * t);
                    d[s0 + i] += ((float)Math.Sin(ph) + 0.15f * (float)Math.Sin(2f * ph)) * env * env * 0.5f;
                }
            }
            d = ReverbOneShot(d, 0.45f);
            new Biquad().LowPass(3500f, 0.7f).Run(d);
            return Normalize(d, 0.45f);
        }

        // A turtle coming up for air: a soft breathy puff.
        public static float[] Breath(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.9f);
            Pink pink = new Pink(rng);
            Biquad bp = new Biquad().BandPass(650f, 1.2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.06f) * (float)Math.Exp(-t * 4.5f);
                d[i] = bp.Process(pink.Next()) * env;
            }
            return Normalize(d, 0.45f);
        }

        static float Lerp(float a, float b, float t) { return a + (b - a) * Math.Max(0f, Math.Min(1f, t)); }

        // Reverb for a one-off sound (no wrap-around: the tail is added after it).
        static float[] ReverbOneShot(float[] dry, float mix)
        {
            float[] padded = new float[dry.Length + (int)(1.2f * SampleRate)];
            Array.Copy(dry, padded, dry.Length);
            float[] wet = ReverbLoop(padded, mix, 0.8f, 0);
            // (ReverbLoop runs twice over the buffer; with the silent padding the wrap is inaudible)
            return wet;
        }

        // ------------------------------------------------------------------
        // Music: two stereo layers that loop together (same length and chords)
        // ------------------------------------------------------------------
        const float LoopSeconds = 32f;
        // A calm, open progression: Fmaj7 - Am7 - Cmaj7 - G6 (8 seconds each).
        static readonly int[][] Chords =
        {
            new[] { 41, 53, 57, 60, 64 },   // F  (F A C E)
            new[] { 45, 52, 55, 60, 64 },   // Am7 (A E G C E)
            new[] { 36, 52, 55, 59, 62 },   // Cmaj7 (C E G B D)
            new[] { 43, 50, 55, 59, 64 },   // G6 (G D G B E)
        };
        static readonly int[] Pentatonic = { 64, 67, 69, 72, 74, 76, 79, 81 };   // E G A C D E G A

        static float PadVoice(int chordIndex, float t, float detune)
        {
            int[] notes = Chords[(chordIndex + 4) % 4];
            float s = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                float f = LoopSafe(NoteToHz(notes[n]), LoopSeconds);
                float f2 = LoopSafe(f * (1f + detune), LoopSeconds);
                float amp = n == 0 ? 0.7f : 1f;
                s += amp * ((float)Math.Sin(TwoPi * f * t) + 0.5f * (float)Math.Sin(TwoPi * f2 * t) + 0.12f * (float)Math.Sin(TwoPi * 2f * f * t));
            }
            return s;
        }

        public static SoundData MusicCalm(int seed)
        {
            Random rng = new Random(seed);
            float[] l = Buffer(LoopSeconds), r = Buffer(LoopSeconds);
            float chordLength = LoopSeconds / 4f;
            for (int i = 0; i < l.Length; i++)
            {
                float t = i / (float)SampleRate;
                int chord = (int)(t / chordLength);
                float local = t - chord * chordLength;
                float blend = Math.Min(1f, local / 2.5f);           // a slow 2.5 s swell into each chord
                blend = blend * blend * (3f - 2f * blend);
                float breathe = 0.85f + 0.15f * (float)Math.Sin(TwoPi * t / 8f);   // the pad breathes with each chord
                l[i] = (PadVoice(chord, t, 0.0035f) * blend + PadVoice(chord - 1, t, 0.0035f) * (1f - blend)) * breathe;
                r[i] = (PadVoice(chord, t, -0.003f) * blend + PadVoice(chord - 1, t, -0.003f) * (1f - blend)) * breathe;
            }
            Biquad lpl = new Biquad().LowPass(1100f, 0.6f), lpr = new Biquad().LowPass(1100f, 0.6f);
            // (run the filters over the loop twice so it wraps cleanly)
            for (int pass = 0; pass < 2; pass++) { for (int i = 0; i < l.Length; i++) { float a = lpl.Process(l[i]), b = lpr.Process(r[i]); if (pass == 1) { l[i] = a; r[i] = b; } } }
            NormalizePair(l, r, 0.32f);

            // Kalimba bells on a pentatonic scale (no wrong notes), sparse and soft, panned.
            float[] bl = Buffer(LoopSeconds), br = Buffer(LoopSeconds);
            for (float beat = 0f; beat < LoopSeconds; beat += 1f)
            {
                if (rng.NextDouble() < 0.55) continue;
                float pan = (float)rng.NextDouble();
                float time = beat + 0.25f * (float)rng.NextDouble();
                float f = NoteToHz(Pentatonic[rng.Next(Pentatonic.Length)]);
                AddBell(bl, time, f, 0.07f * (1f - pan * 0.6f), 1.6f, 0.006f);
                AddBell(br, time, f, 0.07f * (0.4f + pan * 0.6f), 1.6f, 0.006f);
            }
            for (int i = 0; i < l.Length; i++) { l[i] += bl[i]; r[i] += br[i]; }

            l = ReverbLoop(l, 0.45f, 0.86f, 0); r = ReverbLoop(r, 0.45f, 0.86f, 23);
            NormalizePair(l, r, 0.55f);
            return new SoundData(Interleave(l, r), 2);
        }

        // Intense layer: a soft marimba pattern, a heartbeat bass and a light shaker.
        public static SoundData MusicIntense(int seed)
        {
            Random rng = new Random(seed);
            float[] l = Buffer(LoopSeconds), r = Buffer(LoopSeconds);
            float chordLength = LoopSeconds / 4f;
            float step = chordLength / 32f;   // 8th notes at 120 bpm
            int[] pattern = { 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 2, 4, 3, 2, 3 };

            for (int c = 0; c < 4; c++)
            {
                for (int n = 0; n < 32; n++)
                {
                    float start = c * chordLength + n * step;
                    int note = Chords[c][pattern[n % 16]] + 12;
                    float accent = n % 4 == 0 ? 1f : (n % 2 == 0 ? 0.75f : 0.55f);
                    float pan = 0.5f + 0.35f * (float)Math.Sin(n * 0.7f + c);
                    float f = NoteToHz(note);
                    AddMarimba(l, start, f, 0.16f * accent * (1.1f - pan));
                    AddMarimba(r, start, f, 0.16f * accent * (0.1f + pan));
                }
                // heartbeat bass: the chord root, two soft beats per bar
                for (int beat = 0; beat < 8; beat++)
                {
                    float t0 = c * chordLength + beat * chordLength / 8f;
                    float f = NoteToHz(Chords[c][0]);
                    AddSoftBass(l, t0, f, 0.45f); AddSoftBass(r, t0, f, 0.45f);
                    AddSoftBass(l, t0 + 0.28f, f, 0.22f); AddSoftBass(r, t0 + 0.28f, f, 0.22f);
                }
            }
            // a light shaker on the off-beats (filtered noise, very quiet)
            Biquad hpL = new Biquad().HighPass(6000f, 0.7f), hpR = new Biquad().HighPass(6000f, 0.7f);
            for (int n = 0; n < (int)(LoopSeconds / step); n++)
            {
                int start = (int)((n * step + step * 0.5f) * SampleRate);
                int len = (int)(0.06f * SampleRate);
                float amp = n % 2 == 0 ? 0.03f : 0.018f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Math.Min(1f, t / 0.004f) * (float)Math.Exp(-t * 55f);
                    int idx = (start + i) % l.Length;
                    l[idx] += hpL.Process(Noise(rng)) * env * amp;
                    r[idx] += hpR.Process(Noise(rng)) * env * amp;
                }
            }
            new Biquad().LowPass(3200f, 0.6f).Run(l);
            new Biquad().LowPass(3200f, 0.6f).Run(r);
            l = ReverbLoop(l, 0.3f, 0.8f, 0); r = ReverbLoop(r, 0.3f, 0.8f, 23);
            NormalizePair(l, r, 0.55f);
            return new SoundData(Interleave(l, r), 2);
        }

        // A marimba-like note: sine + its 4th harmonic (the wooden "tock"), quick decay.
        static void AddMarimba(float[] d, float startSeconds, float f, float volume)
        {
            int start = (int)(startSeconds * SampleRate);
            int length = (int)(1.2f * SampleRate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.006f) * (float)Math.Exp(-t * 5f);
                float s = (float)Math.Sin(TwoPi * f * t) + 0.25f * (float)Math.Exp(-t * 18f) * (float)Math.Sin(TwoPi * 4f * f * t);
                d[(start + i) % d.Length] += s * env * volume;
            }
        }

        static void AddSoftBass(float[] d, float startSeconds, float f, float volume)
        {
            int start = (int)(startSeconds * SampleRate);
            int length = (int)(0.9f * SampleRate);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.03f) * (float)Math.Exp(-t * 4.5f);
                d[(start + i) % d.Length] += (float)Math.Sin(TwoPi * f * t) * env * volume;
            }
        }

        // ------------------------------------------------------------------
        // Sound effects (mono)
        // ------------------------------------------------------------------
        // Orb pickup: a soft kalimba note at C5. The AudioManager raises its pitch with the combo.
        public static float[] OrbChime()
        {
            float[] d = Buffer(1.8f);
            AddBell(d, 0f, NoteToHz(72), 0.7f, 3.2f, 0.003f);
            AddBell(d, 0.04f, NoteToHz(79), 0.18f, 4.5f, 0.003f);
            new Biquad().LowPass(5000f, 0.7f).Run(d);
            return Normalize(d, 0.75f);
        }

        // Dash: noise that sweeps up and down in brightness ("fwoosh").
        public static float[] Whoosh(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.5f);
            Pink pink = new Pink(rng);
            Biquad bp = new Biquad();
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                if (i % 32 == 0) bp.Set(300f + 1800f * (float)Math.Sin(Math.PI * t), 0.9f, 2);
                d[i] = bp.Process(pink.Next()) * (float)Math.Sin(Math.PI * t);
            }
            return Normalize(d, 0.6f);
        }

        // Crash: a soft low thump that drops in pitch, with a muffled knock.
        public static float[] Thud(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.6f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = 45f + 70f * (float)Math.Exp(-t * 16f);
                phase += TwoPi * f / SampleRate;
                d[i] = (float)Math.Sin(phase) * (float)Math.Exp(-t * 7f);
                if (t < 0.03f) d[i] += Noise(rng) * 0.35f * (1f - t / 0.03f);
            }
            new Biquad().LowPass(1500f, 0.7f).Run(d);
            return Normalize(d, 0.85f);
        }

        // Thunder: a distant, rolling rumble (only a soft crack: calm, not startling).
        public static float[] Thunder(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(5.5f);
            float walk = 0f;
            Biquad lp = new Biquad().LowPass(160f, 0.7f), crackBp = new Biquad().BandPass(900f, 0.8f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                walk = walk * 0.998f + Noise(rng) * 0.08f;
                float rumbleEnv = Math.Min(1f, t / 0.35f) * (float)Math.Exp(-t * 0.75f) * (0.65f + 0.35f * (float)Math.Sin(TwoPi * t * 1.3f + Math.Sin(t * 3f)));
                float crack = crackBp.Process(Noise(rng)) * (float)Math.Exp(-t * 9f) * 0.25f;
                d[i] = lp.Process(walk) * rumbleEnv * 3f + crack;
            }
            return Normalize(d, 0.8f);
        }

        // Near miss: a quick rising arpeggio of little bells.
        public static float[] Shimmer()
        {
            float[] d = Buffer(1.3f);
            int[] notes = { 81, 84, 88, 93 };
            for (int i = 0; i < notes.Length; i++) AddBell(d, i * 0.06f, NoteToHz(notes[i]), 0.3f, 5f);
            new Biquad().LowPass(7000f, 0.7f).Run(d);
            return Normalize(d, 0.55f);
        }

        // Starburst: a swelling major chord with sparkles on top.
        public static float[] StarburstSwell()
        {
            float[] d = Buffer(2.6f);
            int[] chord = { 69, 73, 76, 81 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.4f) * (float)Math.Exp(-Math.Max(0f, t - 0.4f) * 1.4f);
                float s = 0f;
                foreach (int n in chord) s += (float)Math.Sin(TwoPi * NoteToHz(n) * t);
                d[i] = s * env * 0.2f;
            }
            int[] sparkles = { 93, 96, 100, 105, 100, 108 };
            for (int i = 0; i < sparkles.Length; i++) AddBell(d, 0.1f + i * 0.09f, NoteToHz(sparkles[i]), 0.1f, 5f);
            return Normalize(d, 0.65f);
        }

        // Entering a new zone: a gentle rising three-note chime.
        public static float[] ZoneChime()
        {
            float[] d = Buffer(3.2f);
            int[] notes = { 64, 69, 76, 81 };
            for (int i = 0; i < notes.Length; i++) AddBell(d, i * 0.16f, NoteToHz(notes[i]), 0.35f - i * 0.04f, 1.4f, 0.01f);
            d = ReverbLoop(d, 0.4f, 0.8f, 0);
            new Biquad().LowPass(6000f, 0.7f).Run(d);
            return Normalize(d, 0.55f);
        }

        // Whale call: a deep, sliding, wobbly hum with a simple echo.
        public static float[] WhaleCall()
        {
            float[] d = Buffer(4f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float glide = 165f - 55f * Math.Min(1f, t / 2.6f);
                float vibrato = 4f * (float)Math.Sin(TwoPi * 5f * t);
                phase += TwoPi * (glide + vibrato) / SampleRate;
                float env = Math.Min(1f, t / 0.5f) * Math.Min(1f, Math.Max(0f, (3.2f - t) / 1f));
                d[i] = ((float)Math.Sin(phase) + 0.5f * (float)Math.Sin(2f * phase) + 0.25f * (float)Math.Sin(3f * phase)) * env;
            }
            float[] echo = (float[])d.Clone();
            int delay1 = (int)(0.18f * SampleRate), delay2 = (int)(0.37f * SampleRate);
            for (int i = 0; i < d.Length; i++)
            {
                if (i >= delay1) d[i] += echo[i - delay1] * 0.4f;
                if (i >= delay2) d[i] += echo[i - delay2] * 0.25f;
            }
            new Biquad().LowPass(1400f, 0.7f).Run(d);
            return Normalize(d, 0.8f);
        }

        // A soft whale song for the sea creatures: a slow, low, smooth glide up and down
        // with a gentle (not wobbly) vibrato, a soft overtone and a long reverb tail.
        public static float[] WhaleSongSoft()
        {
            float[] d = Buffer(4.5f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float glide = 118f + 22f * (float)Math.Sin(Math.PI * Math.Min(1f, t / 3.2f)) - 10f * Math.Min(1f, t / 3.2f);
                float vibrato = 1.2f * (float)Math.Sin(TwoPi * 2.2f * t);
                phase += TwoPi * (glide + vibrato) / SampleRate;
                float env = (float)Math.Pow(Math.Sin(Math.PI * Math.Min(1f, t / 3.3f)), 1.5);
                d[i] = ((float)Math.Sin(phase) + 0.22f * (float)Math.Sin(2f * phase)) * env;
            }
            new Biquad().LowPass(700f, 0.6f).Run(d);
            d = ReverbOneShot(d, 0.55f);
            return Normalize(d, 0.6f);
        }

        // Big splash: a burst of filtered pink noise that dies away.
        public static float[] Splash(int seed)
        {
            Random rng = new Random(seed);
            Pink pink = new Pink(rng);
            float[] d = Buffer(1.8f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] = pink.Next() * Math.Min(1f, t / 0.015f) * (float)Math.Exp(-t * 2.8f);
            }
            new Biquad().LowPass(2200f, 0.7f).Run(d);
            return Normalize(d, 0.75f);
        }

        // UI click: a tiny soft "tock" for menu buttons.
        public static float[] UiClick()
        {
            float[] d = Buffer(0.12f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] = (float)Math.Sin(TwoPi * 1100f * t) * (float)Math.Exp(-t * 55f) + (float)Math.Sin(TwoPi * 2200f * t) * 0.2f * (float)Math.Exp(-t * 90f);
            }
            return Normalize(d, 0.45f);
        }

        // Game over: a slow falling minor chord.
        public static float[] GameOverTone()
        {
            float[] d = Buffer(3.5f);
            int[] notes = { 57, 60, 64 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float bend = (float)Math.Pow(2.0, -t * 0.08);
                float env = Math.Min(1f, t / 0.2f) * (float)Math.Exp(-t * 0.9f);
                float s = 0f;
                foreach (int n in notes) s += (float)Math.Sin(TwoPi * NoteToHz(n) * bend * t);
                d[i] = s * env;
            }
            new Biquad().LowPass(1200f, 0.7f).Run(d);
            return Normalize(d, 0.6f);
        }
    }
}
