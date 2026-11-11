using System;
using System.Globalization;
using System.IO;
using Worktown.Sim.Clock;

namespace Worktown.ClockReport
{
    /// <summary>
    /// Spec 066: prints one line per day (kind, Rain Hour, panels, storm, strike count, season, festival) and the
    /// service fit lines. Usage: clock-report [--seed 1] [--days 28]. Exit 0, or 1 on bad arguments.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args) => Run(args, Console.Out);

        public static int Run(string[] args, TextWriter output)
        {
            ulong seed = 1;
            int days = 28;
            for (int i = 0; i < args.Length; i++)
            {
                bool ok = i + 1 < args.Length;
                if (args[i] == "--seed" && ok && ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out seed)) continue;
                if (args[i] == "--days" && ok && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out days) && days > 0) continue;
                Console.Error.WriteLine("clock-report: bad arguments. Usage: [--seed 1] [--days 28]");
                return 1;
            }

            ClockCatalog clock = ClockCatalog.CreateDefault();
            WeatherCatalog weather = WeatherCatalog.CreateDefault();
            SeasonCalendar calendar = SeasonCalendar.CreateDefault();
            output.WriteLine("seed " + seed + ", " + days + " days");
            for (long day = 1; day <= days; day++)
            {
                DayWeather w = WeatherPlan.For(weather, seed, day);
                string panels = w.PanelsFromMinute < 0 ? "-" : Hm(w.PanelsFromMinute) + "-" + Hm(w.PanelsToMinute);
                string storm = w.StormFromMinute < 0 ? "-" : Hm(w.StormFromMinute) + "-" + Hm(w.StormToMinute);
                FestivalDef? f = calendar.FestivalOn(day);
                output.WriteLine(
                    "day " + day.ToString("00", CultureInfo.InvariantCulture)
                    + "  " + w.Kind
                    + "  rain " + Hm(w.RainStartMinute) + "-" + Hm(w.RainStartMinute + w.RainMinutes)
                    + "  panels " + panels
                    + "  storm " + storm
                    + "  strikes " + LightningSchedule.Strikes(w, clock).Count
                    + "  " + calendar.SeasonOf(day) + " " + calendar.DayInSeason(day)
                    + "  " + (f == null ? "-" : f.Id));
            }
            foreach (int between in new[] { 60, 15 })
            {
                var fit = ServiceFit.For(clock, 360, between);
                output.WriteLine(
                    "service fit 360+" + between + ": " + fit.ServicesPerDay + " per day, " + fit.LeftoverSeconds
                    + " s left, " + fit.ServicesPerPlayHourTimes100 + " per 100 play hours");
            }
            return 0;
        }

        private static string Hm(int minute) =>
            (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
