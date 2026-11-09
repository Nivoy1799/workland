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

        /// <summary>Per-key gain on top of the master volume (loops quieter than one-shots).</summary>
        public static float GainOf(SoundKey key)
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
                default: return 1f;
            }
        }
    }
}
