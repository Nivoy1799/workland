using System;
using UnityEngine;

namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 023: the kitchen sounds synthesised in code (no sound files), from fixed seeds so the result is always the
    /// same. Mono, 22.05 kHz, peak amplitude well below 1. The sample generators are pure (no Unity types) so they can
    /// be tested; <see cref="CreateClip"/> wraps them in an AudioClip. Client only (a headless server never builds them).
    /// </summary>
    public static class ProceduralSounds
    {
        public const int SampleRate = 22050;

        /// <summary>A knife chop: a short high-passed noise burst with a fast decay plus a low thump.</summary>
        public static float[] Chop()
        {
            var rng = new System.Random(2301);
            var s = new float[(int)(SampleRate * 0.16f)];
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous; // first-order high-pass: the click
                previous = noise;
                float burst = high * 0.35f * (float)Math.Exp(-t / 0.012f);
                float thump = (float)Math.Sin(2.0 * Math.PI * 150.0 * t) * 0.45f * (float)Math.Exp(-t / 0.035f);
                s[i] = burst + thump;
            }
            return s;
        }

        /// <summary>A soft UI click / pick-up tick: a short sine pair with a fast decay.</summary>
        public static float[] Tick()
        {
            var s = new float[(int)(SampleRate * 0.06f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = (float)Math.Exp(-t / 0.010f);
                float a = (float)Math.Sin(2.0 * Math.PI * 1500.0 * t);
                float b = (float)Math.Sin(2.0 * Math.PI * 900.0 * t);
                s[i] = (a * 0.6f + b * 0.4f) * 0.3f * env * Math.Min(1f, t / 0.001f);
            }
            return s;
        }

        /// <summary>A pan sizzle loop (2 s): high-passed noise with random short crackles.</summary>
        public static float[] Sizzle()
        {
            var rng = new System.Random(2302);
            var s = new float[SampleRate * 2];
            float previous = 0f;
            float pop = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous;
                previous = noise;
                if (rng.NextDouble() < 0.0012) pop = 0.5f * (float)(0.4 + rng.NextDouble() * 0.6);
                pop *= 0.985f;
                float popNoise = (float)(rng.NextDouble() * 2.0 - 1.0) * pop;
                s[i] = high * 0.09f + popNoise;
            }
            return s;
        }

        /// <summary>A pot bubbling loop (3 s): random low sine blips that glide upwards; the last 0.2 s stay quiet.</summary>
        public static float[] Bubble()
        {
            var rng = new System.Random(2303);
            var s = new float[SampleRate * 3];
            int blipLength = (int)(SampleRate * 0.07f);
            int lastStart = s.Length - blipLength - (int)(SampleRate * 0.2f);
            int next = 0;
            while (next < lastStart)
            {
                float freq = 180f + (float)rng.NextDouble() * 240f;
                float amp = 0.12f + (float)rng.NextDouble() * 0.18f;
                for (int i = 0; i < blipLength; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = (float)Math.Sin(Math.PI * i / blipLength);
                    float f = freq * (1f + 3f * t);
                    s[next + i] += (float)Math.Sin(2.0 * Math.PI * f * t) * amp * env * env;
                }
                next += (int)(SampleRate * (0.05f + rng.NextDouble() * 0.22f));
            }
            return s;
        }

        /// <summary>Spec 027b: picking something up, a short soft upward "fwip" (a rising blip with a breath of noise).</summary>
        public static float[] Pick()
        {
            var rng = new System.Random(2701);
            var s = new float[(int)(SampleRate * 0.09f)];
            double phase = 0.0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float u = t / 0.09f;
                double freq = 520.0 + 640.0 * u;
                phase += 2.0 * Math.PI * freq / SampleRate;
                float env = (float)Math.Sin(Math.PI * Math.Min(1.0, u * 1.15)) * (1f - u * 0.4f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.05f;
                s[i] = ((float)Math.Sin(phase) * 0.32f + noise) * env;
            }
            return s;
        }

        /// <summary>
        /// Spec 027b: a put-down tock, made of decaying partials (frequency, amplitude, decay seconds) plus a short noise
        /// click for the contact. Wood is hollow and low, metal rings with inharmonic partials, ceramic clinks high, food
        /// is a soft thud.
        /// </summary>
        private static float[] Tock(int seed, float seconds, float clickAmount, float clickDecay, params float[] partials)
        {
            var rng = new System.Random(seed);
            var s = new float[(int)(SampleRate * seconds)];
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float v = 0f;
                for (int p = 0; p + 2 < partials.Length; p += 3)
                    v += (float)Math.Sin(2.0 * Math.PI * partials[p] * t) * partials[p + 1] * (float)Math.Exp(-t / partials[p + 2]);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous;
                previous = noise;
                v += high * clickAmount * (float)Math.Exp(-t / clickDecay);
                s[i] = v * Math.Min(1f, t / 0.0008f);
            }
            return s;
        }

        public static float[] PlaceWood() => Tock(2702, 0.14f, 0.18f, 0.004f, 210f, 0.38f, 0.035f, 470f, 0.16f, 0.02f, 1150f, 0.05f, 0.01f);
        public static float[] PlaceMetal() => Tock(2703, 0.45f, 0.12f, 0.003f, 523f, 0.22f, 0.16f, 1247f, 0.16f, 0.11f, 2093f, 0.1f, 0.07f, 3310f, 0.06f, 0.04f);
        public static float[] PlaceCeramic() => Tock(2704, 0.25f, 0.1f, 0.002f, 1860f, 0.22f, 0.07f, 2730f, 0.14f, 0.05f, 4120f, 0.08f, 0.03f);
        public static float[] PlaceFood() => Tock(2705, 0.12f, 0.25f, 0.012f, 120f, 0.32f, 0.03f, 260f, 0.12f, 0.02f);

        /// <summary>Spec 027b: "nope", two short soft falling tones.</summary>
        public static float[] Nope()
        {
            var s = new float[(int)(SampleRate * 0.24f)];
            int half = s.Length / 2;
            for (int i = 0; i < s.Length; i++)
            {
                bool second = i >= half;
                float t = (second ? i - half : i) / (float)SampleRate;
                float length = half / (float)SampleRate;
                float freq = second ? 300f : 380f;
                float env = (float)Math.Sin(Math.PI * Math.Min(1.0, t / length)) * 0.9f;
                float tone = (float)Math.Sin(2.0 * Math.PI * freq * t) + 0.25f * (float)Math.Sin(2.0 * Math.PI * freq * 2.0 * t);
                s[i] = tone * 0.22f * env;
            }
            return s;
        }

        /// <summary>Spec 027b: a stove button or knob, a crisp short click.</summary>
        public static float[] Button() => Tock(2706, 0.05f, 0.3f, 0.0015f, 2400f, 0.12f, 0.006f, 650f, 0.2f, 0.008f);

        /// <summary>Spec 027c: "perfect", a bright rising two-note chime (a fifth, then the octave), soft attack.</summary>
        public static float[] Perfect()
        {
            var s = new float[(int)(SampleRate * 0.5f)];
            int second = (int)(SampleRate * 0.12f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                s[i] = Bell(t, 784f, 0.22f, 0.25f);
                if (i >= second) s[i] += Bell((i - second) / (float)SampleRate, 1175f, 0.22f, 0.3f);
            }
            return s;
        }

        /// <summary>Spec 027c: the warning 3 s before overdone, three short soft beeps on one mid tone.</summary>
        public static float[] Warning()
        {
            var s = new float[(int)(SampleRate * 0.5f)];
            int beep = (int)(SampleRate * 0.09f), gap = (int)(SampleRate * 0.16f);
            for (int n = 0; n < 3; n++)
            {
                int start = n * gap;
                for (int i = 0; i < beep && start + i < s.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = (float)Math.Sin(Math.PI * i / beep);
                    s[start + i] += ((float)Math.Sin(2.0 * Math.PI * 660.0 * t) + 0.3f * (float)Math.Sin(2.0 * Math.PI * 1320.0 * t)) * 0.25f * env;
                }
            }
            return s;
        }

        /// <summary>Spec 027c: burnt, a hiss of smoke over a low falling tone ("pfff" with a sag).</summary>
        public static float[] Burnt()
        {
            var rng = new System.Random(2801);
            var s = new float[(int)(SampleRate * 0.8f)];
            double phase = 0.0;
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float u = t / 0.8f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous * 0.6f;
                previous = noise;
                float hiss = high * 0.16f * (float)Math.Sin(Math.PI * Math.Min(1.0, u * 1.4)) * (1f - u);
                phase += 2.0 * Math.PI * (220.0 - 120.0 * u) / SampleRate;
                float sag = (float)Math.Sin(phase) * 0.3f * (float)Math.Exp(-t / 0.35f) * Math.Min(1f, t / 0.01f);
                s[i] = hiss + sag;
            }
            return s;
        }

        /// <summary>Spec 027c: a sale, a coin "ka-ching": two quick bright metal pings.</summary>
        public static float[] Coin()
        {
            var s = new float[(int)(SampleRate * 0.45f)];
            int second = (int)(SampleRate * 0.07f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float v = Bell(t, 1976f, 0.18f, 0.12f) + Bell(t, 2960f, 0.08f, 0.08f);
                if (i >= second)
                {
                    float t2 = (i - second) / (float)SampleRate;
                    v += Bell(t2, 2637f, 0.2f, 0.2f) + Bell(t2, 3951f, 0.08f, 0.12f);
                }
                s[i] = v;
            }
            return s;
        }

        /// <summary>
        /// Spec 029: the fryer loop (3 s): a dense low rumble of oil bubbles (short low blips that glide up, much denser and lower
        /// than the pot) over a soft hiss; it starts and ends quiet so the loop has no click.
        /// </summary>
        public static float[] Fryer()
        {
            var rng = new System.Random(2901);
            var s = new float[SampleRate * 3];
            int fade = (int)(SampleRate * 0.05f);
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous;
                previous = noise;
                s[i] = high * 0.035f;
            }
            int blipLength = (int)(SampleRate * 0.045f);
            int next = fade;
            while (next < s.Length - blipLength - fade)
            {
                float freq = 90f + (float)rng.NextDouble() * 160f;
                float amp = 0.06f + (float)rng.NextDouble() * 0.08f;
                for (int i = 0; i < blipLength; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = (float)Math.Sin(Math.PI * i / blipLength);
                    s[next + i] += (float)Math.Sin(2.0 * Math.PI * freq * (1f + 5f * t) * t) * amp * env * env;
                }
                next += (int)(SampleRate * (0.012f + rng.NextDouble() * 0.05f));
            }
            for (int i = 0; i < fade; i++)
            {
                float g = i / (float)fade;
                s[i] *= g;
                s[s.Length - 1 - i] *= g;
            }
            return s;
        }

        /// <summary>Spec 029: a basket shake, a quick metallic wire rattle (four short ringing ticks with a scrape of noise).</summary>
        public static float[] Rattle()
        {
            var rng = new System.Random(2902);
            var s = new float[(int)(SampleRate * 0.32f)];
            for (int n = 0; n < 4; n++)
            {
                int start = (int)(SampleRate * (0.015f + 0.07f * n + rng.NextDouble() * 0.012f));
                float amp = 0.75f - 0.12f * n;
                float previous = 0f;
                for (int i = start; i < s.Length; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    if (t > 0.09f) break;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float high = noise - previous;
                    previous = noise;
                    float ring = (float)Math.Sin(2.0 * Math.PI * 2850.0 * t) * 0.12f + (float)Math.Sin(2.0 * Math.PI * 4170.0 * t) * 0.07f;
                    s[i] += (ring + high * 0.18f) * amp * (float)Math.Exp(-t / 0.012f) * Math.Min(1f, t / 0.0006f);
                }
            }
            return s;
        }

        /// <summary>Spec 029: oil dripping back into the fryer, a few soft high plinks with small sizzles, slowing down.</summary>
        public static float[] Drip()
        {
            var rng = new System.Random(2903);
            var s = new float[(int)(SampleRate * 1.2f)];
            float at = 0.02f;
            for (int n = 0; n < 6 && at < 1.05f; n++)
            {
                int start = (int)(SampleRate * at);
                float freq = 1300f + (float)rng.NextDouble() * 900f;
                float amp = 0.3f - 0.03f * n;
                float previous = 0f;
                for (int i = start; i < s.Length; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    if (t > 0.12f) break;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float high = noise - previous;
                    previous = noise;
                    float plink = (float)Math.Sin(2.0 * Math.PI * freq * (1f + 2.5f * t) * t) * (float)Math.Exp(-t / 0.025f);
                    float fizz = high * 0.12f * (float)Math.Exp(-t / 0.04f);
                    s[i] += (plink + fizz) * amp * Math.Min(1f, t / 0.0008f);
                }
                at += 0.1f + 0.06f * n + (float)rng.NextDouble() * 0.05f;
            }
            return s;
        }

        /// <summary>
        /// Spec 030: the flip "tssh": a soft scrape of the spatula, then a burst of high-passed sizzle noise that swells in 20 ms
        /// and fades over 0.35 s (the raw side hits the hot plate).
        /// </summary>
        public static float[] Flip()
        {
            var rng = new System.Random(3001);
            var s = new float[(int)(SampleRate * 0.45f)];
            float previous = 0f, band = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous;
                previous = noise;
                band += (noise - band) * 0.25f; // a softer, lower noise for the scrape
                float scrape = band * 0.2f * (t < 0.05f ? (float)Math.Sin(Math.PI * t / 0.05f) : 0f);
                float hiss = high * 0.24f * Math.Min(1f, Math.Max(0f, t - 0.04f) / 0.02f) * (float)Math.Exp(-Math.Max(0f, t - 0.06f) / 0.12f);
                s[i] = scrape + hiss;
            }
            return s;
        }

        /// <summary>Spec 031: a layer lands on the build: a soft low "plop" (a falling sine with a short wet noise).</summary>
        public static float[] Plop()
        {
            var rng = new System.Random(3101);
            var s = new float[(int)(SampleRate * 0.14f)];
            float band = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float freq = 320f - 900f * t;
                float body = (float)Math.Sin(2.0 * Math.PI * Math.Max(90f, freq) * t) * 0.45f * (float)Math.Exp(-t / 0.035f);
                band += ((float)(rng.NextDouble() * 2.0 - 1.0) - band) * 0.3f;
                float wet = band * 0.25f * (float)Math.Exp(-t / 0.02f);
                s[i] = (body + wet) * Math.Min(1f, t / 0.002f);
            }
            return s;
        }

        /// <summary>
        /// Spec 031: the sauce squeeze loop (1 s): a soft, slightly bubbly low-passed noise with a slow wobble, faded in and out at
        /// the ends so the loop has no click.
        /// </summary>
        public static float[] Squeeze()
        {
            var rng = new System.Random(3102);
            var s = new float[SampleRate];
            float band = 0f, slow = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                band += (noise - band) * 0.18f;
                slow += (noise - slow) * 0.02f;
                float wobble = 0.7f + 0.3f * (float)Math.Sin(2.0 * Math.PI * 7.0 * t);
                float edge = Math.Min(1f, Math.Min(t, 1f - t) / 0.03f);
                s[i] = (band * 0.5f + slow * 0.9f) * wobble * edge;
            }
            return s;
        }

        /// <summary>Spec 031: the fold of a wrap: a soft cloth-like swish (band noise swelling and falling over 0.25 s).</summary>
        public static float[] Fold()
        {
            var rng = new System.Random(3103);
            var s = new float[(int)(SampleRate * 0.3f)];
            float band = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                band += ((float)(rng.NextDouble() * 2.0 - 1.0) - band) * 0.12f;
                float env = (float)Math.Sin(Math.PI * Math.Min(1f, t / 0.26f));
                s[i] = band * 0.9f * env * env;
            }
            return s;
        }

        /// <summary>Spec 031: wrap paper, a box or a bag: a dry crinkle of short high-passed bursts.</summary>
        public static float[] Paper()
        {
            var rng = new System.Random(3104);
            var s = new float[(int)(SampleRate * 0.35f)];
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - previous;
                previous = noise;
                float crackle = rng.NextDouble() < 0.02 ? 1f : 0.35f;
                float env = (float)Math.Exp(-t / 0.15f) * Math.Min(1f, t / 0.004f);
                s[i] = high * 0.22f * crackle * env;
            }
            return s;
        }

        /// <summary>Spec 032: a coin dropped in the tray: one short bright metal tick with a second, quieter bounce.</summary>
        public static float[] Clink()
        {
            var s = new float[(int)(SampleRate * 0.22f)];
            int bounce = (int)(SampleRate * 0.045f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float v = Bell(t, 3520f, 0.16f, 0.05f) + Bell(t, 5274f, 0.07f, 0.03f);
                if (i >= bounce)
                {
                    float t2 = (i - bounce) / (float)SampleRate;
                    v += Bell(t2, 3729f, 0.07f, 0.035f);
                }
                s[i] = v;
            }
            return s;
        }

        /// <summary>Spec 032: the card terminal: a clean electronic double beep (square-ish, soft edges).</summary>
        public static float[] Beep()
        {
            var s = new float[(int)(SampleRate * 0.32f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                bool on = t < 0.11f || (t > 0.16f && t < 0.27f);
                float local = t < 0.13f ? t : t - 0.16f;
                float edge = Math.Min(1f, Math.Min(local / 0.004f, Math.Max(0f, 0.11f - local) / 0.01f));
                float tone = (float)Math.Sin(2.0 * Math.PI * 1760.0 * t);
                tone = tone + 0.25f * (float)Math.Sin(2.0 * Math.PI * 5280.0 * t);
                s[i] = on ? tone * 0.14f * edge : 0f;
            }
            return s;
        }

        /// <summary>A soft bell partial: a sine with a 3 ms attack and an exponential decay.</summary>
        private static float Bell(float t, float freq, float amp, float decay)
        {
            return (float)Math.Sin(2.0 * Math.PI * freq * t) * amp * (float)Math.Exp(-t / decay) * Math.Min(1f, t / 0.003f);
        }

        public static float[] Samples(SoundKey key)
        {
            switch (key)
            {
                case SoundKey.Chop: return Chop();
                case SoundKey.Tick: return Tick();
                case SoundKey.Sizzle: return Sizzle();
                case SoundKey.Bubble: return Bubble();
                case SoundKey.Pick: return Pick();
                case SoundKey.PlaceWood: return PlaceWood();
                case SoundKey.PlaceMetal: return PlaceMetal();
                case SoundKey.PlaceCeramic: return PlaceCeramic();
                case SoundKey.PlaceFood: return PlaceFood();
                case SoundKey.Nope: return Nope();
                case SoundKey.Button: return Button();
                case SoundKey.Perfect: return Perfect();
                case SoundKey.Warning: return Warning();
                case SoundKey.Burnt: return Burnt();
                case SoundKey.Coin: return Coin();
                case SoundKey.Fryer: return Fryer();
                case SoundKey.Rattle: return Rattle();
                case SoundKey.Drip: return Drip();
                case SoundKey.Flip: return Flip();
                case SoundKey.Plop: return Plop(); // spec 031
                case SoundKey.Squeeze: return Squeeze();
                case SoundKey.Fold: return Fold();
                case SoundKey.Paper: return Paper();
                case SoundKey.Clink: return Clink(); // spec 032
                case SoundKey.Beep: return Beep();
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "no sound for key");
            }
        }

        public static bool IsLoop(SoundKey key) => key == SoundKey.Sizzle || key == SoundKey.Bubble || key == SoundKey.Fryer || key == SoundKey.Squeeze;

        public static AudioClip CreateClip(SoundKey key)
        {
            float[] samples = Samples(key);
            AudioClip clip = AudioClip.Create("Procedural_" + key, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
