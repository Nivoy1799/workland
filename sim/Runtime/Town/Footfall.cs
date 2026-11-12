using System;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: footfall per district and clock hour (relative points), and the location factor of a place. Pure integer
    /// functions. Revival (spec 065) and conditions (spec 066) come in as plain permille inputs; 1000 changes nothing.
    /// </summary>
    public static class Footfall
    {
        /// <summary>
        /// A place's footfall in clock hour <paramref name="hour"/> (0..23): base = DayFootfall x curve[hour] / 1000 x
        /// FrontagePermille / 1000; boosted = base x revival / 1000 x conditions / 1000; the result is min(boosted,
        /// base x CeilingPermille / 1000). An event-only place gives 0 unless <paramref name="eventDay"/>.
        /// </summary>
        public static int Hourly(TownCatalog c, string placeId, int hour, int revivalPermille = 1000, int conditionsPermille = 1000,
            bool eventDay = false)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (revivalPermille < 0) throw new ArgumentOutOfRangeException(nameof(revivalPermille), "Must be >= 0.");
            if (conditionsPermille < 0) throw new ArgumentOutOfRangeException(nameof(conditionsPermille), "Must be >= 0.");
            PlaceDef place = c.Place(placeId);
            DistrictDef district = c.District(place.DistrictId);
            long baseValue = (long)DistrictHourly(c, district.Id, hour) * place.FrontagePermille / 1000;
            if (place.EventDaysOnly && !eventDay) return 0;
            long boosted = baseValue * revivalPermille / 1000 * conditionsPermille / 1000;
            long cap = baseValue * district.CeilingPermille / 1000;
            return (int)Math.Min(boosted, cap);
        }

        /// <summary>A place's footfall over the 24 hours at revival 1000 and conditions 1000 (0 for an event-only place).</summary>
        public static int Daily(TownCatalog c, string placeId)
        {
            int sum = 0;
            for (int h = 0; h < TownCatalog.Hours; h++) sum += Hourly(c, placeId, h);
            return sum;
        }

        /// <summary>
        /// The location factor: Daily(place) x 1000 / Daily(reference place); exactly 1000 for the reference place (0 when the
        /// reference has no footfall).
        /// </summary>
        public static int LocationPermille(TownCatalog c, string placeId)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            long reference = Daily(c, c.ReferencePlaceId);
            long own = Daily(c, placeId);
            return reference <= 0 ? 0 : (int)(own * 1000 / reference);
        }

        /// <summary>A district's own footfall in clock hour <paramref name="hour"/>: DayFootfall x curve[hour] / 1000.</summary>
        public static int DistrictHourly(TownCatalog c, string districtId, int hour)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (hour < 0 || hour >= TownCatalog.Hours) throw new ArgumentOutOfRangeException(nameof(hour), "Clock hours are 0..23.");
            DistrictDef d = c.District(districtId);
            return (int)((long)d.DayFootfall * c.Curve(d.CurveId).HourPermille[hour] / 1000);
        }

        /// <summary>
        /// A district's footfall summed over the clock hours from <paramref name="fromHour"/> to <paramref name="toHour"/>
        /// inclusive, wrapping past midnight (21..3 is 21, 22, 23, 0, 1, 2, 3). 0..23 is the whole day.
        /// </summary>
        public static int DistrictSum(TownCatalog c, string districtId, int fromHour, int toHour)
        {
            if (fromHour < 0 || fromHour >= TownCatalog.Hours) throw new ArgumentOutOfRangeException(nameof(fromHour), "Clock hours are 0..23.");
            if (toHour < 0 || toHour >= TownCatalog.Hours) throw new ArgumentOutOfRangeException(nameof(toHour), "Clock hours are 0..23.");
            int sum = 0;
            for (int h = fromHour; ; h = (h + 1) % TownCatalog.Hours)
            {
                sum += DistrictHourly(c, districtId, h);
                if (h == toHour) return sum;
            }
        }
    }
}
