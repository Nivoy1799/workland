using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 027c: pure rules of the reward moment over the counter after a sale: the stars pop in one after another, the
    /// money and tip float up in Mustard and fade. Timing, text and the star mesh; no scene access.
    /// </summary>
    public static class ServeRewardRules
    {
        /// <summary>The whole moment, in seconds.</summary>
        public const float Seconds = 2.4f;
        /// <summary>Each star pops this much after the one before.</summary>
        public const float StarStagger = 0.07f;
        /// <summary>One star's pop: grows past full size and settles.</summary>
        public const float PopSeconds = 0.25f;
        public const float PopOvershoot = 1.35f;
        /// <summary>The fade at the end.</summary>
        public const float FadeSeconds = 0.6f;
        /// <summary>How far it rises over <see cref="Seconds"/> (metres).</summary>
        public const float Rise = 0.35f;
        /// <summary>Only viewers within this distance of the counter see it (the coin is spatial anyway).</summary>
        public const float ViewDistance = 16f;
        /// <summary>Where it starts above the counter's top (metres): low enough to stay under the order rail and the
        /// guests' speech, seen from the aisle and from the street.</summary>
        public const float AboveCounter = 0.22f;
        /// <summary>Star size and spacing (metres).</summary>
        public const float StarSize = 0.05f;
        public const float StarSpacing = 0.11f;

        public static readonly Color Earned = Palette.Mustard;
        public static readonly Color NotEarned = Palette.WithAlpha(Palette.Charcoal, 0.7f);
        public static readonly Color MoneyColor = Palette.Mustard;

        /// <summary>Scale of star <paramref name="index"/> at <paramref name="t"/> seconds: 0 before its turn, up to
        /// <see cref="PopOvershoot"/> and back to 1 over <see cref="PopSeconds"/>, then 1.</summary>
        public static float StarScale(int index, float t)
        {
            float local = t - index * StarStagger;
            if (local <= 0f) return 0f;
            if (local >= PopSeconds) return 1f;
            float u = local / PopSeconds;
            return u < 0.6f ? Mathf.Lerp(0f, PopOvershoot, u / 0.6f) : Mathf.Lerp(PopOvershoot, 1f, (u - 0.6f) / 0.4f);
        }

        /// <summary>Opacity at <paramref name="t"/>: 1, then fading to 0 over the last <see cref="FadeSeconds"/>.</summary>
        public static float Alpha(float t)
        {
            if (t <= Seconds - FadeSeconds) return 1f;
            return Mathf.Clamp01((Seconds - t) / FadeSeconds);
        }

        /// <summary>Height above the start at <paramref name="t"/> (eases out).</summary>
        public static float Height(float t)
        {
            float u = Mathf.Clamp01(t / Seconds);
            return Rise * (1f - (1f - u) * (1f - u));
        }

        /// <summary>The start point over a counter at <paramref name="counter"/> whose top is at <paramref name="top"/>.</summary>
        public static Vector3 StartOver(Vector3 counter, float top) => new Vector3(counter.x, top + AboveCounter, counter.z);

        /// <summary>"+$64", and "+$64 · tip $4" with a tip.</summary>
        public static string MoneyText(ServeReward reward)
        {
            string text = "+" + EconomyRules.Money(reward.Paid);
            return reward.Tip > 0 ? text + " · tip " + EconomyRules.Money(reward.Tip) : text;
        }

        /// <summary>Stars earned (0-5) of the five shown.</summary>
        public static int EarnedStars(ServeReward reward) => Mathf.Clamp(reward.Stars, 0, StarRating.MaxStars);

        /// <summary>
        /// A five-pointed star (unit outer radius, inner 0.45, point up) in the XY plane as a fan around the centre,
        /// appended to the lists in <paramref name="color"/>, at <paramref name="offset"/> scaled by <paramref name="scale"/>.
        /// </summary>
        public static void AddStar(Vector3 offset, float scale, Color color, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            const int points = 5;
            const float inner = 0.45f;
            int centre = vertices.Count;
            vertices.Add(offset);
            colors.Add(color);
            for (int i = 0; i < points * 2; i++)
            {
                float r = (i % 2 == 0 ? 1f : inner) * scale;
                Vector2 d = HeatRingRules.Direction(i / (float)(points * 2));
                vertices.Add(offset + new Vector3(d.x * r, d.y * r, 0f));
                colors.Add(color);
            }
            for (int i = 0; i < points * 2; i++)
            {
                triangles.Add(centre);
                triangles.Add(centre + 1 + i);
                triangles.Add(centre + 1 + (i + 1) % (points * 2));
            }
        }
    }
}
