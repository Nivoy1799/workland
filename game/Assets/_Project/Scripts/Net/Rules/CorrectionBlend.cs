using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054b design 3, pure: smoothed reconciliation of the own player. A reconcile still snaps the simulated position to the
    /// server's (the server stays the authority); only the drawn body keeps an <see cref="Offset"/> from it. A correction shorter
    /// than <see cref="SnapMeters"/> leaves the drawn body where it was and the offset then shrinks to zero in
    /// <see cref="BlendSeconds"/>; a longer one snaps the drawn body too, so a real teleport is never hidden. The offset never grows
    /// past <see cref="SnapMeters"/> (a run of small corrections in one direction shows as motion, not as a growing lag).
    /// Immutable: every call returns the next value.
    /// </summary>
    public readonly struct CorrectionBlend
    {
        /// <summary>Corrections this long (m) or longer snap the drawn body.</summary>
        public const float SnapMeters = 0.30f;
        /// <summary>How long (s) the drawn body takes to reach the simulated position after a blended correction.</summary>
        public const float BlendSeconds = 0.12f;

        /// <summary>Drawn position minus simulated position (m).</summary>
        public readonly Vector3 Offset;
        /// <summary>How fast (m/s) the offset shrinks: its length when the last correction came, over <see cref="BlendSeconds"/>.</summary>
        public readonly float Rate;

        public CorrectionBlend(Vector3 offset, float rate)
        {
            Offset = offset;
            Rate = rate;
        }

        /// <summary>No offset: the drawn body is where the simulation is.</summary>
        public static CorrectionBlend None => default;

        /// <summary>Whether a correction of this length blends (true) or snaps (false).</summary>
        public static bool Blends(float correctionMeters) => correctionMeters < SnapMeters;

        /// <summary>Where to draw the body for the simulated position <paramref name="simulated"/>.</summary>
        public Vector3 Drawn(Vector3 simulated) => simulated + Offset;

        /// <summary>
        /// A reconcile moved the simulated position by <paramref name="correction"/> (after minus before). Returns the next blend;
        /// <paramref name="visibleMeters"/> is how far the drawn body moves because of it within the next tick
        /// (<paramref name="tickSeconds"/>): the whole jump when it snaps, else the first step of the blend plus any part over the cap.
        /// </summary>
        public CorrectionBlend Correct(Vector3 correction, float tickSeconds, out float visibleMeters)
        {
            if (!Blends(correction.magnitude))
            {
                visibleMeters = (correction - Offset).magnitude; // the drawn body jumps from simulated + offset to the new position
                return None;
            }
            Vector3 offset = Offset - correction;
            float length = offset.magnitude;
            float over = 0f;
            if (length > SnapMeters)
            {
                over = length - SnapMeters;
                offset *= SnapMeters / length;
                length = SnapMeters;
            }
            float rate = length / BlendSeconds;
            visibleMeters = over + Mathf.Min(length, rate * Mathf.Max(0f, tickSeconds));
            return new CorrectionBlend(offset, rate);
        }

        /// <summary>The blend <paramref name="seconds"/> later: the offset shrinks by <see cref="Rate"/> towards zero.</summary>
        public CorrectionBlend Advance(float seconds)
        {
            if (Rate <= 0f || Offset == Vector3.zero) return None;
            Vector3 offset = Vector3.MoveTowards(Offset, Vector3.zero, Rate * Mathf.Max(0f, seconds));
            return offset == Vector3.zero ? None : new CorrectionBlend(offset, Rate);
        }
    }
}
