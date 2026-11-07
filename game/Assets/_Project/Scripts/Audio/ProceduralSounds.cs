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

        public static float[] Samples(SoundKey key)
        {
            switch (key)
            {
                case SoundKey.Chop: return Chop();
                case SoundKey.Tick: return Tick();
                case SoundKey.Sizzle: return Sizzle();
                case SoundKey.Bubble: return Bubble();
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
