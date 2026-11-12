using NUnit.Framework;
using Worktown.Game.Climb;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 057 AC3: the rent at a stage 2 service end (open question 4): owed first, the reserve never taken, owed capped, never below 0.</summary>
    public class RentRulesTests
    {
        private const long Rent = 5000, Reserve = 2500, Cap = 10000;

        [Test]
        public void PaidInFull()
        {
            Assert.AreEqual(new RentRules.Result(5000, 0, 0), RentRules.Charge(10000, Rent, 0, Reserve, Cap));
        }

        [Test]
        public void Short_PaysWhatIsAboveTheReserve_TheRestIsOwed()
        {
            Assert.AreEqual(new RentRules.Result(1500, 3500, 0), RentRules.Charge(4000, Rent, 0, Reserve, Cap));
        }

        [Test]
        public void UnderTheReserve_PaysNothing()
        {
            Assert.AreEqual(new RentRules.Result(0, 5000, 0), RentRules.Charge(2000, Rent, 0, Reserve, Cap));
            Assert.AreEqual(new RentRules.Result(0, 5000, 0), RentRules.Charge(2500, Rent, 0, Reserve, Cap), "the reserve itself");
        }

        [Test]
        public void TheOwed_IsCarried_AndPaidFirstNextTime()
        {
            RentRules.Result first = RentRules.Charge(4000, Rent, 0, Reserve, Cap);
            RentRules.Result next = RentRules.Charge(20000, Rent, first.OwedAfter, Reserve, Cap);
            Assert.AreEqual(new RentRules.Result(8500, 0, 0), next, "this service's rent and the 3,500 owed");
        }

        [Test]
        public void TheOwed_IsCapped_TheRestForgiven()
        {
            Assert.AreEqual(new RentRules.Result(0, 10000, 3000), RentRules.Charge(2500, Rent, 8000, Reserve, Cap));
            Assert.AreEqual(new RentRules.Result(500, 10000, 2500), RentRules.Charge(3000, Rent, 8000, Reserve, Cap));
        }

        [Test]
        public void TheBalance_NeverGoesBelowZero()
        {
            foreach (long balance in new long[] { -100, 0, 1, 2499, 2500, 2501, 7000, 100000 })
                foreach (long owed in new long[] { 0, 1, 9999, 10000, 50000 })
                {
                    RentRules.Result r = RentRules.Charge(balance, Rent, owed, Reserve, Cap);
                    Assert.GreaterOrEqual(r.Paid, 0);
                    Assert.LessOrEqual(r.Paid, System.Math.Max(0L, balance - Reserve), balance + " " + owed);
                    Assert.GreaterOrEqual(System.Math.Max(0L, balance) - r.Paid, 0L);
                    Assert.LessOrEqual(r.OwedAfter, Cap);
                    Assert.AreEqual(Rent + owed, r.Paid + r.OwedAfter + r.Forgiven, "nothing is lost or made up");
                }
            Assert.AreEqual("Rent and upkeep: -$50.00", RentRules.PaidText(5000));
            Assert.AreEqual("Rent owed: $30.50", RentRules.OwedText(3050));
        }
    }
}
