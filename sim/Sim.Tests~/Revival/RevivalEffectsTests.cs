using System;
using System.Linq;
using Worktown.Sim.Revival;
using Xunit;
using R = Worktown.Sim.Tests.Revival.TestRevival;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalEffectsTests
    {
        private static RevivalState At(RevivalCatalog c, string district, int level) =>
            RevivalState.FromSaved(c, new SavedRevival(new[] { district }, new[] { level }, new[] { 0 }));

        [Fact]
        public void FootfallPerLevel() // AC8
        {
            RevivalCatalog spec = R.Spec();
            Assert.Equal(new[] { 1000, 1100, 1200, 1300 },
                new[] { RevivalLevel.Grey, RevivalLevel.Patched, RevivalLevel.Lively, RevivalLevel.Thriving }.Select(l => RevivalEffects.FootfallPermille(spec, l)));
            RevivalCatalog tuned = RevivalCatalog.CreateDefault(R.Town); // V6 lowered the top to +10 %
            Assert.Equal(1000, RevivalEffects.FootfallPermille(tuned, RevivalLevel.Grey));
            Assert.Equal(1100, RevivalEffects.FootfallPermille(tuned, RevivalLevel.Thriving));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEffects.FootfallPermille(spec, (RevivalLevel)4));
        }

        [Fact]
        public void IngredientCeilingLiterals() // AC8
        {
            RevivalCatalog c = RevivalCatalog.CreateDefault(R.Town);
            Assert.Equal(650, RevivalEffects.IngredientCeilingPermille(c, 1, RevivalLevel.Grey));
            Assert.Equal(1000, RevivalEffects.IngredientCeilingPermille(c, 5, RevivalLevel.Thriving)); // 930 + 70
            Assert.Equal(830, RevivalEffects.IngredientCeilingPermille(c, 3, RevivalLevel.Lively));   // 790 + 40
            var p = new R.Parts();
            p.Ceiling[4] = 990;
            Assert.Equal(1000, RevivalEffects.IngredientCeilingPermille(p.Build(), 5, RevivalLevel.Thriving)); // capped
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEffects.IngredientCeilingPermille(c, 0, RevivalLevel.Grey));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEffects.IngredientCeilingPermille(c, 6, RevivalLevel.Grey));
        }

        [Fact]
        public void RobotWorkshopOpensOnlyAtWorkshopLaneLively() // AC8
        {
            RevivalCatalog c = RevivalCatalog.CreateDefault(R.Town);
            Assert.False(RevivalEffects.IsUnlocked(c, RevivalEngine.Start(c), "robot_workshop"));
            Assert.False(RevivalEffects.IsUnlocked(c, At(c, "workshop_lane", 1), "robot_workshop"));
            Assert.True(RevivalEffects.IsUnlocked(c, At(c, "workshop_lane", 2), "robot_workshop"));
            Assert.True(RevivalEffects.IsUnlocked(c, At(c, "workshop_lane", 3), "robot_workshop"));
            Assert.False(RevivalEffects.IsUnlocked(c, At(c, "main_street", 3), "robot_workshop")); // another district does not count
            Assert.True(RevivalEffects.IsUnlocked(c, At(c, "greenhouse_hill", 1), "greenhouse_stall"));
            Assert.Throws<ArgumentException>(() => RevivalEffects.IsUnlocked(c, RevivalEngine.Start(c), "moon_shop"));
        }
    }
}
