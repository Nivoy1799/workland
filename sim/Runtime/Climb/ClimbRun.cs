using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: one simulated climb (one <see cref="CrewSetup"/> up to a horizon) and its numbers, all derived from
    /// <see cref="Services"/>: hours to each stage, services per stage, net money per hour, upkeep share, your share per
    /// rival channel, missed share, stars, idle-money services and the debt streak. <see cref="Canonical"/> is the invariant
    /// text of the run and <see cref="Digest"/> its SHA-256 (lowercase hex). Immutable.
    /// </summary>
    public sealed class ClimbRun
    {
        public ClimbRun(CrewSetup setup, long horizonSeconds, IReadOnlyList<ServiceResult> services, ClimbState final)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            HorizonSeconds = horizonSeconds;
            Services = new List<ServiceResult>(services ?? throw new ArgumentNullException(nameof(services))).AsReadOnly();
            Final = final ?? throw new ArgumentNullException(nameof(final));
            Canonical = BuildCanonical();
            Digest = Sha256(Canonical);
        }

        public CrewSetup Setup { get; }
        public long HorizonSeconds { get; }
        public IReadOnlyList<ServiceResult> Services { get; }
        public ClimbState Final { get; }
        public string Canonical { get; }
        public string Digest { get; }

        /// <summary>Play seconds when <paramref name="stage"/> was entered (the start of its first service), or -1 (DNF).</summary>
        public long EnteredSeconds(ClimbStage stage)
        {
            if (stage == ClimbStage.FoodCorner) return 0;
            foreach (ServiceResult r in Services) if (r.Stage >= stage) return r.StartSeconds;
            return -1;
        }

        /// <summary>True when the next stage was entered (stage 5 never completes).</summary>
        public bool Completed(ClimbStage stage) => stage < ClimbStage.BeyondKitchen && EnteredSeconds(stage + 1) >= 0;

        /// <summary>Services played in <paramref name="stage"/>.</summary>
        public int ServicesIn(ClimbStage stage)
        {
            int n = 0;
            foreach (ServiceResult r in Services) if (r.Stage == stage) n++;
            return n;
        }

        /// <summary>Net income per play hour in <paramref name="stage"/> (0 without services).</summary>
        public long NetPerHour(ClimbStage stage)
        {
            long net = 0, seconds = 0;
            foreach (ServiceResult r in Services) if (r.Stage == stage) { net += r.Net; seconds += r.Seconds; }
            return seconds == 0 ? 0 : GuestChoice.FloorDiv(net * 3600, seconds);
        }

        /// <summary>Upkeep as a share of revenue in <paramref name="stage"/> (permille; 0 without upkeep, 9999 with upkeep and no revenue).</summary>
        public int UpkeepPermille(ClimbStage stage)
        {
            long upkeep = 0, revenue = 0;
            foreach (ServiceResult r in Services) if (r.Stage == stage) { upkeep += r.Upkeep; revenue += r.Revenue; }
            if (upkeep == 0) return 0;
            return revenue == 0 ? 9999 : (int)Math.Min(9999, upkeep * 1000 / revenue);
        }

        /// <summary>Missed guests as a share of the guests who chose you in <paramref name="stage"/> (permille).</summary>
        public int MissedPermille(ClimbStage stage)
        {
            long missed = 0, chosen = 0;
            foreach (ServiceResult r in Services) if (r.Stage == stage) { missed += r.Missed; chosen += r.Chosen; }
            return chosen == 0 ? 0 : (int)(missed * 1000 / chosen);
        }

        /// <summary>Average stars (tenths) of the normal services with guests in <paramref name="stage"/> (0 when none).</summary>
        public int StarsTenths(ClimbStage stage)
        {
            long sum = 0, n = 0;
            foreach (ServiceResult r in Services)
                if (r.Stage == stage && r.BigJobId == null && r.Chosen > 0) { sum += r.StarsTenths; n++; }
            return n == 0 ? 0 : (int)(sum / n);
        }

        /// <summary>Channels that had a rival during <paramref name="stage"/>, in first-seen order.</summary>
        public IReadOnlyList<string> RivalChannels(ClimbStage stage)
        {
            var list = new List<string>();
            foreach (ServiceResult r in Services)
            {
                if (r.Stage != stage) continue;
                for (int i = 0; i < r.ChannelIds.Count; i++)
                    if (r.RivalGuests[i] >= 0 && !list.Contains(r.ChannelIds[i])) list.Add(r.ChannelIds[i]);
            }
            return list;
        }

        /// <summary>
        /// Your share of a rival channel, yours / (yours + rivals'), over the last <paramref name="lastServices"/> services of
        /// <paramref name="stage"/> in which the channel was open (permille), or -1 when it never was.
        /// </summary>
        public int SharePermille(ClimbStage stage, string channelId, int lastServices)
        {
            long you = 0, all = 0;
            int seen = 0;
            for (int k = Services.Count - 1; k >= 0 && seen < lastServices; k--)
            {
                ServiceResult r = Services[k];
                if (r.Stage != stage) continue;
                for (int i = 0; i < r.ChannelIds.Count; i++)
                {
                    if (r.ChannelIds[i] != channelId || r.RivalGuests[i] < 0) continue;
                    you += r.YourGuests[i];
                    all += r.YourGuests[i] + r.RivalGuests[i];
                    seen++;
                }
            }
            if (seen == 0) return -1;
            return all == 0 ? 0 : (int)(you * 1000 / all);
        }

        /// <summary>
        /// Service ends where money has no use (T7): the mean net income of the last <paramref name="window"/> services is
        /// positive and no goal cost lies between <paramref name="minTimes"/> and <paramref name="maxTimes"/> times it.
        /// </summary>
        public int IdleMoneyServices(int minTimes, int maxTimes, int window, ClimbStage? stage = null)
        {
            int idle = 0;
            for (int i = 0; i < Services.Count; i++)
            {
                if (stage != null && Services[i].Stage != stage) continue;
                int from = Math.Max(0, i - Math.Max(1, window) + 1);
                long sum = 0;
                for (int k = from; k <= i; k++) sum += Services[k].Net;
                long mean = sum / (i - from + 1);
                if (mean <= 0) continue;
                bool goal = false;
                foreach (long cost in Services[i].GoalCosts)
                    if (cost >= minTimes * mean && cost <= maxTimes * mean) { goal = true; break; }
                if (!goal) idle++;
            }
            return idle;
        }

        /// <summary>The longest run of services that ended below $0.</summary>
        public int LongestDebtStreak()
        {
            int best = 0, run = 0;
            foreach (ServiceResult r in Services)
            {
                run = r.BalanceAfter < 0 ? run + 1 : 0;
                if (run > best) best = run;
            }
            return best;
        }

        private string BuildCanonical()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(Services.Count * 160 + 256);
            sb.Append("climb052 ").Append(Setup.Players.ToString(c)).Append(' ').Append(Setup.ProfileId).Append(' ')
                .Append(Setup.Strategy.Id).Append(' ').Append(Setup.Seed.ToString(c)).Append(' ')
                .Append(Setup.ServiceSeconds.ToString(c)).Append(' ').Append(Setup.BetweenSeconds.ToString(c)).Append(' ')
                .Append(HorizonSeconds.ToString(c)).Append('\n');
            foreach (ServiceResult r in Services) sb.Append(r.Canonical()).Append('\n');
            sb.Append("final ").Append(((int)Final.Stage).ToString(c)).Append(' ').Append(Final.Balance.ToString(c)).Append(' ')
                .Append(Final.Reputation.ToString(c)).Append(' ').Append(Final.Trophies.ToString(c)).Append(' ')
                .Append(string.Join(",", Final.Owned)).Append('\n');
            return sb.ToString();
        }

        private static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
