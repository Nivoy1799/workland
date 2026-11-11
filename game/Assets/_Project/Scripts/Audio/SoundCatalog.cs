using System;
using UnityEngine;

namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 023: maps <see cref="SoundKey"/> to a clip. The clips are synthesised once by
    /// <see cref="ProceduralSounds"/> (deviation from the spec: no sound pack download, so this is a plain class, not
    /// an asset). One master volume for everything.
    /// </summary>
    public sealed class SoundCatalog
    {
        /// <summary>The one master volume (0..1); every source multiplies it with its own gain.</summary>
        public const float MasterVolume = 0.5f;

        private readonly AudioClip[] _clips = new AudioClip[Enum.GetValues(typeof(SoundKey)).Length];

        public bool TryGet(SoundKey key, out AudioClip clip)
        {
            int i = (int)key;
            if (i < 0 || i >= _clips.Length)
            {
                clip = null;
                return false;
            }
            if (_clips[i] == null) _clips[i] = ProceduralSounds.CreateClip(key);
            clip = _clips[i];
            return clip != null;
        }

        /// <summary>Per-key gain on top of the master volume: <see cref="BaseGainOf"/> times the player's volume of the key's group
        /// (spec 048, <see cref="AudioMix"/>; 1 by default). The player's master volume is the AudioListener's.</summary>
        public static float GainOf(SoundKey key) => BaseGainOf(key) * AudioMix.Of(SoundRules.CategoryOf(key));

        /// <summary>The mix's own per-key gain (loops quieter than one-shots).</summary>
        public static float BaseGainOf(SoundKey key)
        {
            switch (key)
            {
                case SoundKey.Sizzle: return 0.6f;
                case SoundKey.Bubble: return 0.7f;
                case SoundKey.Tick: return 0.8f;
                case SoundKey.Pick: return 0.7f;
                case SoundKey.Nope: return 0.8f;
                case SoundKey.Button: return 0.8f;
                case SoundKey.Perfect: return 0.8f;
                case SoundKey.Warning: return 0.7f;
                case SoundKey.Coin: return 0.8f;
                case SoundKey.Fryer: return 0.75f; // spec 029
                case SoundKey.Rattle: return 0.7f;
                case SoundKey.Drip: return 0.6f;
                case SoundKey.Flip: return 0.8f; // spec 030
                case SoundKey.Plop: return 0.8f; // spec 031
                case SoundKey.Squeeze: return 0.55f;
                case SoundKey.Fold: return 0.7f;
                case SoundKey.Paper: return 0.7f;
                case SoundKey.Pour: return 0.5f; // spec 033
                case SoundKey.ShaveRasp: return 0.6f; // spec 036
                case SoundKey.MotorHum: return 0.25f;
                case SoundKey.LidClick: return 0.8f;
                case SoundKey.Splash: return 0.8f;
                case SoundKey.Scrub: return 0.55f; // spec 035
                case SoundKey.TrashRustle: return 0.75f;
                case SoundKey.LidThump: return 0.8f;
                default: return 1f;
            }
        }
    }
}
