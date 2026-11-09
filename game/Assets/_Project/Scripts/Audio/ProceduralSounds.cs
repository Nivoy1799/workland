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
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "no sound for key");
            }
        }

        public static bool IsLoop(SoundKey key) => key == SoundKey.Sizzle || key == SoundKey.Bubble;

        public static AudioClip CreateClip(SoundKey key)
        {
            float[] samples = Samples(key);
            AudioClip clip = AudioClip.Create("Procedural_" + key, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
