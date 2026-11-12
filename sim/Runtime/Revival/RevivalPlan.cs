using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: where the climb's meals are eaten, for <see cref="RevivalReplay"/>. <see cref="Shares"/> split a channel's meals
    /// over districts by weight (largest remainder, ties in plan order); a channel with no share is eaten in
    /// <see cref="HomeDistrict"/> (the shop's district). An event big job's meals go to <see cref="EventDistrict"/>; a big job
    /// listed in <see cref="CompetitionJobIds"/> adds nothing (a competition serves no guests).
    /// </summary>
    public sealed record RevivalPlan(string HomeDistrict, IReadOnlyList<(string ChannelId, string DistrictId, int Weight)> Shares,
        string EventDistrict, IReadOnlyList<string> CompetitionJobIds)
    {
        /// <summary>
        /// The default plan (provisional; tuning changes only these numbers): walk-up channels at the shop (<c>s1</c>, Main
        /// Street); events at the Festival Meadow. Delivery (tuning round 1; the spec's start was Homes 500, Tower Rise 200,
        /// Low Ring 150, Station Square 150): Homes 350, Workshop Lane 120, Greenhouse Hill 120, Tower Rise 100, Festival Meadow
        /// 100, Garden Promenade 60, Low Ring 50, Station Square 50, so that every district gets meals (spill does not travel
        /// further) and the Low Ring comes back last.
        /// </summary>
        public static RevivalPlan Default { get; } = new RevivalPlan("main_street",
            new[]
            {
                ("corner_counter", "main_street", 1000),
                ("street_counter", "main_street", 1000),
                ("custom_orders", "main_street", 1000),
                ("tables", "main_street", 1000),
                ("buffet", "main_street", 1000),
                ("delivery", "homes", 350),
                ("delivery", "workshop_lane", 120),
                ("delivery", "greenhouse_hill", 120),
                ("delivery", "tower_rise", 100),
                ("delivery", "festival_meadow", 100),
                ("delivery", "garden_promenade", 60),
                ("delivery", "low_ring", 50),
                ("delivery", "station_square", 50),
            },
            "festival_meadow",
            new[] { "cook_off" });

        /// <summary>The districts and weights a channel's meals go to, in plan order (the home district when none is listed).</summary>
        public IReadOnlyList<(string DistrictId, int Weight)> SharesOf(string channelId)
        {
            var list = (Shares ?? Array.Empty<(string, string, int)>()).Where(s => s.ChannelId == channelId)
                .Select(s => (s.DistrictId, s.Weight)).ToList();
            if (list.Count == 0) list.Add((HomeDistrict, 1));
            return list.AsReadOnly();
        }

        /// <summary>True when the big job adds no meals.</summary>
        public bool IsCompetition(string jobId) => CompetitionJobIds != null && CompetitionJobIds.Contains(jobId);

        /// <summary>Throws <see cref="ArgumentException"/> when a district is unknown to the catalog or a weight is not positive.</summary>
        public void Validate(RevivalCatalog c)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (!c.HasDistrict(HomeDistrict)) throw new ArgumentException("Plan: unknown home district '" + HomeDistrict + "'.");
            if (!c.HasDistrict(EventDistrict)) throw new ArgumentException("Plan: unknown event district '" + EventDistrict + "'.");
            if (Shares == null) throw new ArgumentException("Plan: shares are required.");
            foreach ((string ch, string d, int w) in Shares)
            {
                if (string.IsNullOrEmpty(ch)) throw new ArgumentException("Plan: a share needs a channel id.");
                if (!c.HasDistrict(d)) throw new ArgumentException("Plan: " + ch + " goes to the unknown district '" + d + "'.");
                if (w <= 0) throw new ArgumentException("Plan: " + ch + " to " + d + " needs a weight > 0.");
            }
        }
    }
}
