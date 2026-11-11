using System;
using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: how a guest picks a shop. Every shop (you, each rival of the channel, and one "outside" option) has an
    /// attraction; a guest picks shop i with probability A_i / sum A (one draw). Integers only.
    /// </summary>
    public static class GuestChoice
    {
        /// <summary>
        /// <c>A = max(Min, 1000 + Quality*(q-500)/1000 + Reputation*(rep-500)/1000 - Price*(offset*1000/avgRef)/1000
        /// - Wait*max(0, wait-60)/60 + Variety*(dishes-1))</c>, divisions floored. <paramref name="priceOffset"/> is the average
        /// price minus reference (money), <paramref name="averageReference"/> the menu's average reference price.
        /// </summary>
        public static int Attraction(ChoiceWeights w, int quality, int reputation, long priceOffset, long averageReference,
            int waitSeconds, int dishes)
        {
            long reference = Math.Max(1, averageReference);
            long a = 1000
                + FloorDiv((long)w.Quality * (quality - 500), 1000)
                + FloorDiv((long)w.Reputation * (reputation - 500), 1000)
                - FloorDiv(w.Price * FloorDiv(priceOffset * 1000, reference), 1000)
                - FloorDiv((long)w.Wait * Math.Max(0, waitSeconds - 60), 60)
                + (long)w.Variety * Math.Max(0, dishes - 1);
            if (a < w.Min) a = w.Min;
            return a > int.MaxValue / 64 ? int.MaxValue / 64 : (int)a;
        }

        /// <summary>The demand ceiling of a channel: <c>GuestsPerService * (1000 + bonus) / 1000</c>.</summary>
        public static int Ceiling(ChannelDef channel, int bonusPermille) =>
            (int)((long)channel.GuestsPerService * (1000 + Math.Max(0, bonusPermille)) / 1000);

        /// <summary>One draw <c>NextInt(0, sum A)</c>; the index of the chosen shop.</summary>
        public static int Pick(IReadOnlyList<int> attractions, IRandom rng)
        {
            long total = 0;
            foreach (int a in attractions) total += a;
            if (total <= 0) throw new ArgumentException("Attractions must sum to more than 0.", nameof(attractions));
            int roll = rng.NextInt(0, (int)Math.Min(int.MaxValue, total));
            long acc = 0;
            for (int i = 0; i < attractions.Count; i++)
            {
                acc += attractions[i];
                if (roll < acc) return i;
            }
            return attractions.Count - 1;
        }

        /// <summary>Floor division (towards minus infinity), so negative terms round the same way as positive ones.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
