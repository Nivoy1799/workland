using Worktown.Sim.Clock;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class ConditionsTableTests
    {
        private static readonly ConditionsTable T = ConditionsTable.CreateDefault();

        private static TownTime At(int minute, DayPhase phase) => new TownTime(2, 0, phase, 0, minute, minute / 60);

        private static DayWeather Day(DayKind kind) => new DayWeather(2, kind, 12 * 60, 60,
            kind == DayKind.OpenPanel ? 9 * 60 : -1, kind == DayKind.OpenPanel ? 18 * 60 : -1,
            kind == DayKind.Storm ? 7 * 60 : -1, kind == DayKind.Storm ? 21 * 60 : -1, 1);

        private static void Check(Conditions c, int street, int outdoor, int indoor, int delivery, int deliveryTime) =>
            Assert.Equal(new Conditions(street, outdoor, indoor, delivery, deliveryTime), c);

        [Fact]
        public void WeatherRows()
        {
            Check(T.At(Day(DayKind.Storm), At(12 * 60 + 10, DayPhase.Day)), 500, 0, 1200, 1300, 1300);
            Check(T.At(Day(DayKind.Normal), At(12 * 60 + 10, DayPhase.Day)), 700, 0, 1150, 1300, 1000);
            Check(T.At(Day(DayKind.OpenPanel), At(12 * 60 + 10, DayPhase.Day)), 700, 0, 1150, 1300, 1000);
            Check(T.At(Day(DayKind.Storm), At(9 * 60, DayPhase.Day)), 600, 300, 1200, 1200, 1300);
            Check(T.At(Day(DayKind.OpenPanel), At(9 * 60, DayPhase.Day)), 1100, 1400, 950, 900, 1000);
            Check(T.At(Day(DayKind.Normal), At(9 * 60, DayPhase.Day)), 1000, 1000, 1000, 1000, 1000);
        }

        [Fact]
        public void PhaseRows()
        {
            Check(T.At(Day(DayKind.Normal), At(6 * 60, DayPhase.Dawn)), 1000, 1000, 1000, 1000, 1000);
            Check(T.At(Day(DayKind.Normal), At(20 * 60, DayPhase.Dusk)), 1000, 1100, 1000, 1000, 1000);
            Check(T.At(Day(DayKind.Normal), At(23 * 60, DayPhase.Night)), 1000, 800, 1000, 1250, 1000);
        }

        [Fact]
        public void RainHourAtDuskOnAStormDay_UsesTheStormRainRowTimesDusk()
        {
            DayWeather w = Day(DayKind.Storm) with { RainStartMinute = 19 * 60 + 30 };
            Check(T.At(w, At(19 * 60 + 40, DayPhase.Dusk)), 500, 0, 1200, 1300, 1300);
        }

        [Fact]
        public void StormWindowAtDusk_MultipliesAndFloors()
        {
            Check(T.At(Day(DayKind.Storm), At(20 * 60, DayPhase.Dusk)), 600, 330, 1200, 1200, 1300);
        }

        [Fact]
        public void NightAfterTheStormWindowIsNormalTimesNight()
        {
            Check(T.At(Day(DayKind.Storm), At(22 * 60, DayPhase.Night)), 1000, 800, 1000, 1250, 1000);
        }
    }
}
