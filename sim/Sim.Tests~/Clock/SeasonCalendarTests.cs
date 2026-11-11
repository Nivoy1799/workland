using Worktown.Sim.Clock;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class SeasonCalendarTests
    {
        private static readonly SeasonCalendar Cal = SeasonCalendar.CreateDefault();

        [Fact]
        public void SeasonOfAndDayInSeason()
        {
            Assert.Equal(Season.Spring, Cal.SeasonOf(1));
            Assert.Equal(1, Cal.DayInSeason(1));
            Assert.Equal(Season.Spring, Cal.SeasonOf(7));
            Assert.Equal(7, Cal.DayInSeason(7));
            Assert.Equal(Season.Summer, Cal.SeasonOf(8));
            Assert.Equal(1, Cal.DayInSeason(8));
            Assert.Equal(Season.Winter, Cal.SeasonOf(28));
            Assert.Equal(Season.Spring, Cal.SeasonOf(29));
            Assert.Equal(1, Cal.DayInSeason(29));
        }

        [Fact]
        public void FirstSeasonShiftsTheCalendar() =>
            Assert.Equal(Season.Autumn, (Cal with { First = Season.Autumn }).SeasonOf(1));

        [Fact]
        public void FestivalsOnTheirDays()
        {
            Assert.Equal("blossom_fair", Cal.FestivalOn(5)!.Id);
            Assert.Equal("lake_nights", Cal.FestivalOn(7 + 5)!.Id);
            Assert.Equal("landing_day", Cal.FestivalOn(14 + 4)!.Id);
            Assert.Equal("winter_lights", Cal.FestivalOn(21 + 5)!.Id);
            Assert.Equal("blossom_fair", Cal.FestivalOn(28 + 5)!.Id);
            Assert.Null(Cal.FestivalOn(1));
            Assert.Equal(4, Cal.Festivals.Count);
        }

        [Fact]
        public void FestivalNamesHaveNoAnimalProducts()
        {
            string[] banned = { "meat", "milk", "egg", "cheese", "fish", "beef", "pork", "chicken", "butter", "honey" };
            foreach (FestivalDef f in Cal.Festivals)
                foreach (string b in banned)
                    Assert.DoesNotContain(b, f.Name.ToLowerInvariant());
        }

        [Fact]
        public void WeatherIgnoresTheCalendar()
        {
            // WeatherPlan takes no calendar: two different calendars cannot change a day.
            var other = Cal with { DaysPerSeason = 3, First = Season.Winter };
            WeatherCatalog w = WeatherCatalog.CreateDefault();
            for (long d = 1; d <= 100; d++)
            {
                DayWeather before = WeatherPlan.For(w, 3UL, d);
                _ = other.SeasonOf(d);
                _ = Cal.SeasonOf(d);
                Assert.Equal(before, WeatherPlan.For(w, 3UL, d));
            }
        }
    }
}
