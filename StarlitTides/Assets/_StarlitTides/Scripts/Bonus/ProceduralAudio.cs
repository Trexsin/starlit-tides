// ProceduralAudio: makes every sound in the game from maths, no audio files needed.
//
// Each method returns raw samples (mono, 44.1 kHz) as a float array.
// The building blocks are classic sound synthesis:
//   - sine waves (pure tones) added together for bells and chords
//   - white noise (random samples) for wind, rain, splashes and waves
//   - a one-pole low-pass filter to make noise softer and "darker"
//   - envelopes (volume over time) like exponential decay for plucks and bells
// Loops (music, waves, wind, rain) are made seamless so they repeat without a click.
// None of this touches Unity objects, so it can run on a background thread.

using System;

namespace StarlitTides
{
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;
        const float TwoPi = 6.2831853f;

        // ------------------------------------------------------------------
        // Building blocks
        // ------------------------------------------------------------------
        static float[] Buffer(float seconds) { return new float[(int)(seconds * SampleRate)]; }

        static float Noise(Random rng) { return (float)(rng.NextDouble() * 2.0 - 1.0); }

        // One-pole low-pass filter: lets low sounds through, softens high ones.
        static void LowPass(float[] data, float cutoffHz)
        {
            float a = (float)Math.Exp(-TwoPi * cutoffHz / SampleRate);
            float y = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                y = (1f - a) * data[i] + a * y;
                data[i] = y;
            }
        }

        // High-pass = original minus its low-passed copy.
        static void HighPass(float[] data, float cutoffHz)
        {
            float[] low = (float[])data.Clone();
            LowPass(low, cutoffHz);
            for (int i = 0; i < data.Length; i++) data[i] -= low[i];
        }

        // Scales the sound so its loudest sample is "peak".
        static float[] Normalize(float[] data, float peak)
        {
            float max = 0.0001f;
            foreach (float s in data) max = Math.Max(max, Math.Abs(s));
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
            return data;
        }

        // Blends the end of the sound into its start, so it loops without a click.
        static float[] MakeLoop(float[] data, float fadeSeconds)
        {
            int fade = (int)(fadeSeconds * SampleRate);
            int length = data.Length - fade;
            float[] result = new float[length];
            Array.Copy(data, result, length);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                result[i] = data[i] * t + data[length + i] * (1f - t);
            }
            return result;
        }

        // A bell: a few sine "partials" that fade out exponentially.
        static void AddBell(float[] data, float startSeconds, float frequency, float volume, float decay)
        {
            float[] partials = { 1f, 2.01f, 3.0f, 4.2f };
            float[] amps = { 1f, 0.45f, 0.22f, 0.1f };
            int start = (int)(startSeconds * SampleRate);
            int length = (int)(Math.Min(4f, 6f / decay) * SampleRate);
            for (int i = 0; i < length; i++)
            {
                int index = (start + i) % data.Length;   // wraps around, so it also works in loops
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.005f) * (float)Math.Exp(-t * decay);
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += amps[p] * (float)Math.Sin(TwoPi * frequency * partials[p] * t);
                data[index] += s * env * volume;
            }
        }

        static float NoteToHz(int midi) { return 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0); }

        // Rounds a frequency so it makes a whole number of cycles in a loop
        // (otherwise the wave would jump when the loop restarts).
        static float LoopSafe(float hz, float loopSeconds) { return (float)Math.Round(hz * loopSeconds) / loopSeconds; }

        // ------------------------------------------------------------------
        // Ambience loops
        // ------------------------------------------------------------------
        public static float[] Waves(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(13f);
            // Brown noise: white noise that wanders slowly. Sounds like rumbling surf.
            float walk = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                walk = walk * 0.995f + Noise(rng) * 0.1f;
                d[i] = walk;
            }
            LowPass(d, 700f);
            // Waves rolling in: slow swells of volume.
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 6.5f + Math.Sin(TwoPi * t / 11f));
                d[i] *= 0.3f + 0.7f * swell * swell;
            }
            return Normalize(MakeLoop(d, 1f), 0.8f);
        }

        public static float[] Wind(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(11f);
            for (int i = 0; i < d.Length; i++) d[i] = Noise(rng);
            LowPass(d, 900f);
            HighPass(d, 150f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] *= 0.45f + 0.35f * (float)Math.Sin(TwoPi * t / 5f) + 0.2f * (float)Math.Sin(TwoPi * t / 2.3f);
            }
            return Normalize(MakeLoop(d, 1f), 0.7f);
        }

        public static float[] Rain(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(7f);
            for (int i = 0; i < d.Length; i++) d[i] = Noise(rng) * 0.35f;
            HighPass(d, 1200f);
            // Individual drops: tiny clicks that die away fast.
            for (int drop = 0; drop < 900; drop++)
            {
                int start = rng.Next(d.Length);
                float volume = (float)rng.NextDouble() * 0.6f;
                for (int i = 0; i < 300; i++)
                    d[(start + i) % d.Length] += Noise(rng) * volume * (float)Math.Exp(-i / 40.0);
            }
            return Normalize(MakeLoop(d, 0.5f), 0.7f);
        }

        // ------------------------------------------------------------------
        // Music: two layers that loop together (same length and chords)
        // ------------------------------------------------------------------
        const float LoopSeconds = 16f;
        // A dreamy minor progression: Am - F - C - G (4 seconds each).
        static readonly int[][] Chords =
        {
            new[] { 45, 48, 52, 57 },   // A minor
            new[] { 41, 45, 48, 53 },   // F major
            new[] { 48, 52, 55, 60 },   // C major
            new[] { 43, 47, 50, 55 },   // G major
        };

        static float Chord(int chordIndex, float t)
        {
            int[] notes = Chords[(chordIndex + 4) % 4];
            float s = 0f;
            foreach (int note in notes)
            {
                float f = LoopSafe(NoteToHz(note), LoopSeconds);
                float f2 = LoopSafe(f * 1.004f, LoopSeconds);   // slightly detuned copy = warm, chorus-like
                s += (float)Math.Sin(TwoPi * f * t) + 0.6f * (float)Math.Sin(TwoPi * f2 * t) + 0.2f * (float)Math.Sin(TwoPi * 2f * f * t);
            }
            return s;
        }

        // Calm layer: soft pad chords plus slow, sparkly bells.
        public static float[] MusicCalm(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(LoopSeconds);
            float chordLength = LoopSeconds / 4f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                int chord = (int)(t / chordLength);
                float local = t - chord * chordLength;
                // crossfade from the previous chord over the first second
                float blend = Math.Min(1f, local / 1f);
                blend = blend * blend * (3f - 2f * blend);
                d[i] = Chord(chord, t) * blend + Chord(chord - 1, t) * (1f - blend);
            }
            LowPass(d, 1400f);
            Normalize(d, 0.35f);

            // Bells on a pentatonic scale (no wrong notes), about one per second.
            int[] scale = { 69, 72, 74, 76, 79, 81, 84 };
            for (float beat = 0f; beat < LoopSeconds; beat += 1f)
            {
                if (rng.NextDouble() < 0.35) continue;
                AddBell(d, beat + 0.5f * (float)rng.NextDouble(), NoteToHz(scale[rng.Next(scale.Length)]), 0.08f, 2.2f);
            }
            return Normalize(d, 0.6f);
        }

        // Intense layer: a plucked arpeggio (16 notes per chord) and a pulsing bass.
        public static float[] MusicIntense(int seed)
        {
            float[] d = Buffer(LoopSeconds);
            float chordLength = LoopSeconds / 4f;
            float step = chordLength / 16f;
            int[] pattern = { 0, 1, 2, 3, 2, 1, 2, 3, 0, 2, 3, 1, 3, 2, 1, 2 };

            for (int c = 0; c < 4; c++)
            {
                for (int n = 0; n < 16; n++)
                {
                    float start = c * chordLength + n * step;
                    float f = NoteToHz(Chords[c][pattern[n]] + 12);
                    float accent = n % 4 == 0 ? 1f : 0.7f;
                    AddPluck(d, start, f, 0.25f * accent);
                }
                // bass: chord root, one octave down, on every beat (4 per chord)
                for (int beat = 0; beat < 4; beat++)
                    AddPluck(d, c * chordLength + beat * chordLength / 4f, NoteToHz(Chords[c][0] - 12), 0.5f, 3f);
            }
            LowPass(d, 3000f);
            return Normalize(d, 0.6f);
        }

        static void AddPluck(float[] d, float startSeconds, float f, float volume, float decay = 7f)
        {
            int start = (int)(startSeconds * SampleRate);
            int length = (int)(Math.Min(2f, 6f / decay) * SampleRate);   // long enough to fade out fully
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.004f) * (float)Math.Exp(-t * decay);
                float s = (float)Math.Sin(TwoPi * f * t) + 0.35f * (float)Math.Sin(TwoPi * 2f * f * t);
                d[(start + i) % d.Length] += s * env * volume;
            }
        }

        // ------------------------------------------------------------------
        // Sound effects
        // ------------------------------------------------------------------
        // Orb pickup: a bright bell at C5. The AudioManager raises its pitch with the combo.
        public static float[] OrbChime()
        {
            float[] d = Buffer(1.6f);
            AddBell(d, 0f, NoteToHz(72), 0.7f, 4f);
            AddBell(d, 0.03f, NoteToHz(84), 0.25f, 6f);
            return Normalize(d, 0.8f);
        }

        // Dash: noise that sweeps up and down in brightness ("fwoosh").
        public static float[] Whoosh(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.45f);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float cutoff = 300f + 2500f * (float)Math.Sin(Math.PI * t);
                float a = (float)Math.Exp(-TwoPi * cutoff / SampleRate);
                y = (1f - a) * Noise(rng) + a * y;
                d[i] = y * (float)Math.Sin(Math.PI * t);
            }
            return Normalize(d, 0.7f);
        }

        // Crash: a low thump that drops in pitch, plus a short click.
        public static float[] Thud(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(0.6f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = 40f + 60f * (float)Math.Exp(-t * 18f);
                phase += TwoPi * f / SampleRate;
                d[i] = (float)Math.Sin(phase) * (float)Math.Exp(-t * 7f);
                if (t < 0.02f) d[i] += Noise(rng) * 0.6f * (1f - t / 0.02f);
            }
            return Normalize(d, 0.9f);
        }

        // Thunder: a sharp crack, then a long rumble.
        public static float[] Thunder(int seed)
        {
            Random rng = new Random(seed);
            float[] crack = Buffer(4.5f);
            float[] rumble = Buffer(4.5f);
            float walk = 0f;
            for (int i = 0; i < rumble.Length; i++)
            {
                walk = walk * 0.997f + Noise(rng) * 0.1f;
                rumble[i] = walk;
                crack[i] = Noise(rng);
            }
            LowPass(rumble, 180f);
            HighPass(crack, 800f);
            float[] d = Buffer(4.5f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float rumbleEnv = Math.Min(1f, t / 0.15f) * (float)Math.Exp(-t * 0.9f) * (0.7f + 0.3f * (float)Math.Sin(TwoPi * t * 1.7f));
                float crackEnv = (float)Math.Exp(-t * 14f);
                d[i] = rumble[i] * rumbleEnv * 3f + crack[i] * crackEnv * 0.5f;
            }
            return Normalize(d, 0.9f);
        }

        // Near miss: a quick rising arpeggio of little bells.
        public static float[] Shimmer()
        {
            float[] d = Buffer(1.2f);
            int[] notes = { 84, 88, 91, 96 };
            for (int i = 0; i < notes.Length; i++) AddBell(d, i * 0.055f, NoteToHz(notes[i]), 0.35f, 6f);
            return Normalize(d, 0.6f);
        }

        // Starburst: a swelling major chord with sparkles on top.
        public static float[] StarburstSwell()
        {
            float[] d = Buffer(2.5f);
            int[] chord = { 69, 73, 76, 81 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.35f) * (float)Math.Exp(-Math.Max(0f, t - 0.35f) * 1.5f);
                float s = 0f;
                foreach (int n in chord) s += (float)Math.Sin(TwoPi * NoteToHz(n) * t);
                d[i] = s * env * 0.2f;
            }
            int[] sparkles = { 93, 96, 100, 105, 100, 108 };
            for (int i = 0; i < sparkles.Length; i++) AddBell(d, 0.1f + i * 0.09f, NoteToHz(sparkles[i]), 0.12f, 5f);
            return Normalize(d, 0.7f);
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
            // echo: quieter copies a little later, like sound bouncing in the deep
            float[] echo = (float[])d.Clone();
            int delay1 = (int)(0.18f * SampleRate), delay2 = (int)(0.37f * SampleRate);
            for (int i = 0; i < d.Length; i++)
            {
                if (i >= delay1) d[i] += echo[i - delay1] * 0.4f;
                if (i >= delay2) d[i] += echo[i - delay2] * 0.25f;
            }
            LowPass(d, 1500f);
            return Normalize(d, 0.8f);
        }

        // Big splash: a burst of noise that dies away.
        public static float[] Splash(int seed)
        {
            Random rng = new Random(seed);
            float[] d = Buffer(1.8f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] = Noise(rng) * Math.Min(1f, t / 0.01f) * (float)Math.Exp(-t * 3f);
            }
            LowPass(d, 1800f);
            return Normalize(d, 0.8f);
        }

        // UI click: a tiny soft "tick" for menu buttons.
        public static float[] UiClick()
        {
            float[] d = Buffer(0.12f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] = (float)Math.Sin(TwoPi * 1500f * t) * (float)Math.Exp(-t * 60f) + (float)Math.Sin(TwoPi * 3000f * t) * 0.3f * (float)Math.Exp(-t * 90f);
            }
            return Normalize(d, 0.5f);
        }

        // Game over: a slow falling minor chord.
        public static float[] GameOverTone()
        {
            float[] d = Buffer(3.5f);
            int[] notes = { 57, 60, 64 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float bend = (float)Math.Pow(2.0, -t * 0.08);   // slowly sinks in pitch
                float env = Math.Min(1f, t / 0.2f) * (float)Math.Exp(-t * 0.9f);
                float s = 0f;
                foreach (int n in notes) s += (float)Math.Sin(TwoPi * NoteToHz(n) * bend * t);
                d[i] = s * env;
            }
            LowPass(d, 1200f);
            return Normalize(d, 0.6f);
        }
    }
}
