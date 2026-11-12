using System;
using System.Linq;
using Worktown.Sim.Town;
using Xunit;
using T = Worktown.Sim.Tests.Town.TestTown;

namespace Worktown.Sim.Tests.Town
{
    public class FootfallTests
    {
        // Fixture: alpha 1000 a day (ceiling 1200), beta 500 a day (ceiling 1300), curve 42 permille for hours 0..15, 41 after.

        [Fact]
        public void HourlyBase() // AC6
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(42, Footfall.Hourly(c, "p_one", 0));        // 1000 x 42 / 1000 x 1000 / 1000
            Assert.Equal(41, Footfall.Hourly(c, "p_one", 20));
            Assert.Equal(25, Footfall.Hourly(c, "p_two", 3));        // 500 x 42 / 1000 = 21, x 1200 / 1000 = 25 (floor)
        }

        [Fact]
        public void RevivalIsCappedByTheCeiling() // AC6: revival 1300, ceiling 1200
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(50, Footfall.Hourly(c, "p_one", 0, revivalPermille: 1300)); // 54 boosted, capped at 42 x 1200 / 1000 = 50
            Assert.Equal(46, Footfall.Hourly(c, "p_one", 0, revivalPermille: 1100)); // under the cap
            Assert.Equal(32, Footfall.Hourly(c, "p_two", 0, revivalPermille: 1300)); // beta's ceiling 1300: 25 x 1.3 = 32, cap 32
        }

        [Fact]
        public void ConditionsLowerIt() // AC6: conditions 700
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(29, Footfall.Hourly(c, "p_one", 0, conditionsPermille: 700));
            Assert.Equal(0, Footfall.Hourly(c, "p_one", 0, conditionsPermille: 0));
            Assert.Equal(37, Footfall.Hourly(c, "p_one", 0, revivalPermille: 1300, conditionsPermille: 700)); // 42 x 1.3 = 54 (floor), x 0.7 = 37 (floor)
        }

        [Fact]
        public void EventOnlyPlaceIsZeroOnANormalDay() // AC6
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(0, Footfall.Hourly(c, "p_event", 12));
            Assert.Equal(21, Footfall.Hourly(c, "p_event", 12, eventDay: true));
            Assert.Equal(0, Footfall.Daily(c, "p_event"));
            Assert.Equal(0, Footfall.Hourly(TownCatalog.CreateDefault(), "c8", 12));
            Assert.True(Footfall.Hourly(TownCatalog.CreateDefault(), "c8", 12, eventDay: true) > 0);
        }

        [Fact]
        public void DailyAndLocation() // AC6
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(1000, Footfall.Daily(c, "p_one"));                  // 16 x 42 + 8 x 41
            Assert.Equal(16 * 25 + 8 * 24, Footfall.Daily(c, "p_two"));      // 592
            Assert.Equal(1000, Footfall.LocationPermille(c, "p_one"));
            Assert.Equal(592, Footfall.LocationPermille(c, "p_two"));
            TownCatalog d = TownCatalog.CreateDefault();
            Assert.Equal(1000, Footfall.LocationPermille(d, "s1"));
            Assert.True(Footfall.LocationPermille(d, "c3") > 1000);
            Assert.True(Footfall.LocationPermille(d, "s6") < 1000);
        }

        [Fact]
        public void LocationIsZeroWhenTheReferenceHasNoFootfall()
        {
            var parts = T.Small();
            parts.Reference = "p_event";
            Assert.Equal(0, Footfall.LocationPermille(parts.Build(), "p_one"));
        }

        [Fact]
        public void DistrictHourlyAndSum()
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(42, Footfall.DistrictHourly(c, "alpha", 0));
            Assert.Equal(20, Footfall.DistrictHourly(c, "beta", 23));         // 500 x 41 / 1000
            Assert.Equal(1000, Footfall.DistrictSum(c, "alpha", 0, 23));
            Assert.Equal(3 * 41 + 4 * 42, Footfall.DistrictSum(c, "alpha", 21, 3)); // wraps past midnight
            Assert.Equal(41, Footfall.DistrictSum(c, "alpha", 16, 16));
        }

        [Fact]
        public void BadInputsThrow()
        {
            TownCatalog c = T.Catalog();
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.Hourly(c, "p_one", 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.Hourly(c, "p_one", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.Hourly(c, "p_one", 0, revivalPermille: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.Hourly(c, "p_one", 0, conditionsPermille: -1));
            Assert.Throws<ArgumentException>(() => Footfall.Hourly(c, "nope", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.DistrictSum(c, "alpha", 0, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => Footfall.DistrictSum(c, "alpha", 24, 0));
            Assert.Throws<ArgumentException>(() => Footfall.DistrictHourly(c, "gamma", 0));
            Assert.Throws<ArgumentException>(() => Footfall.Daily(null!, "p_one"));
            Assert.Throws<ArgumentException>(() => Footfall.LocationPermille(null!, "p_one"));
        }

        [Fact]
        public void DefaultLowRingIsANightDistrict() // town-plan 5
        {
            TownCatalog c = TownCatalog.CreateDefault();
            Assert.True(Footfall.DistrictSum(c, "low_ring", 21, 3) > Footfall.DistrictSum(c, "main_street", 21, 3));
            Assert.All(c.Districts.Where(d => d.InCore), d =>
                Assert.True(Footfall.DistrictSum(c, "low_ring", 10, 16) < Footfall.DistrictSum(c, d.Id, 10, 16)));
        }
    }
}
