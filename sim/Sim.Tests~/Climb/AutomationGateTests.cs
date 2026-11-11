using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (d): the stage 4 gate is staff plus machines plus stars, then the automation plan (AC7).</summary>
    public class AutomationGateTests
    {
        private static readonly ClimbCatalog D = ClimbCatalog.CreateDefault();
        private static readonly int[] Good = { 36, 36, 36, 36, 36 };
        private static readonly int[] Poor = { 30, 30, 30, 30, 30 };

        private static ClimbState S4(int[] stars, long balance, params string[] owned) =>
            T.State() with { Stage = ClimbStage.Automation, RecentStars = stars, Owned = owned, Balance = balance };

        [Fact]
        public void StarsAloneOrTooFewStaffOrNoStarsDoNotPass() // AC7
        {
            Assert.False(ClimbPolicy.GateConditionsMet(D, S4(Good, 0)));
            Assert.False(ClimbPolicy.GateConditionsMet(D, S4(Good, 0, "staff_runner", "fry_dump", "dishwasher"))); // 1 staff + 2 machines
            Assert.False(ClimbPolicy.GateConditionsMet(D, S4(Poor, 0, "staff_runner", "staff_cook", "fry_dump", "dishwasher"))); // 2 + 2, 3.0 stars
            Assert.False(ClimbPolicy.GateConditionsMet(D, S4(new[] { 36, 36, 36, 36 }, 0, "staff_runner", "staff_cook", "fry_dump", "dishwasher"))); // 4 of 5
            Assert.True(ClimbPolicy.GateConditionsMet(D, S4(Good, 0, "staff_runner", "staff_cook", "fry_dump", "dishwasher")));
        }

        [Fact]
        public void TwoStaffTwoMachinesAndStarsBuyTheAutomationPlanAndEnterStageFive() // AC7
        {
            long cost = D.Purchase("automation_plan").Cost;
            Assert.Equal(PurchaseKind.Gate, D.Purchase("automation_plan").Kind);
            Assert.Equal("automation_plan", D.StageOf(ClimbStage.Automation).GatePurchaseId);
            ClimbState ready = S4(Good, cost, "staff_cook", "staff_cook", "fry_dump", "auto_fill_drinks"); // copies count
            var (s, bought, spent) = ClimbPolicy.Decide(D, ready, new CrewSetup(4, "average", ClimbStrategy.Reference, 1));
            Assert.Equal(new[] { "automation_plan" }, bought);
            Assert.Equal(cost, spent);
            Assert.Equal(ClimbStage.BeyondKitchen, s.Stage);
            var (notYet, none, _) = ClimbPolicy.Decide(D, ready with { Balance = cost - 100 }, new CrewSetup(4, "average", ClimbStrategy.Reference, 1));
            Assert.DoesNotContain("automation_plan", none);
            Assert.Equal(ClimbStage.Automation, notYet.Stage);
        }

        [Fact]
        public void StaffAndMachinesAreCountedPerCopy()
        {
            string[] owned = { "staff_cook", "staff_cook", "staff_runner", "fry_dump", "dishwasher", "auto_fill_drinks", "second_basket" };
            Assert.Equal(3, D.StaffCount(owned));
            Assert.Equal(3, D.MachineCount(owned)); // the stage 1 basket saves actions but is no stage 4 machine
            Assert.Equal(0, D.StaffCount(new string[0]));
        }

        [Fact]
        public void TheGateMinimumsAreValidated()
        {
            Assert.Throws<System.ArgumentException>(() => T.Build(stages: new[]
            {
                T.Stages[0], T.Stages[1], T.Stages[2] with { GateMinStaff = 17 }, T.Stages[3], T.Stages[4],
            }));
            Assert.Throws<System.ArgumentException>(() => T.Build(stages: new[]
            {
                T.Stages[0], T.Stages[1], T.Stages[2], T.Stages[3] with { GateMinMachines = -1 }, T.Stages[4],
            }));
        }
    }
}
