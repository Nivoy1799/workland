using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 021 AC2: what each kind of cookware holds, what fits, where it pours, what empty hands do.</summary>
    public class VesselRulesTests
    {
        [TestCase(VesselKind.Board, 1)]
        [TestCase(VesselKind.Pan, 1)]
        [TestCase(VesselKind.Pot, 4)]
        [TestCase(VesselKind.Plate, CookingLimits.MaxPlateInputs)]
        [TestCase(VesselKind.Bowl, CookingLimits.MaxMixInputs)]
        public void Capacity_PerKind(VesselKind kind, int capacity)
        {
            Assert.AreEqual(capacity, VesselRules.Capacity(kind));
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CanPutIn(kind, capacity - 1, plated: false));
            Assert.AreEqual(CookOutcome.VesselFull, VesselRules.CanPutIn(kind, capacity, plated: false));
        }

        [Test]
        public void PlatedFood_NeverFits()
        {
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
                Assert.AreEqual(CookOutcome.AlreadyPlated, VesselRules.CanPutIn(kind, 0, plated: true), kind.ToString());
        }

        [Test]
        public void APanAcceptsOneFood_APotFour()
        {
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CanPutIn(VesselKind.Pan, 0, false));
            Assert.AreEqual(CookOutcome.VesselFull, VesselRules.CanPutIn(VesselKind.Pan, 1, false));
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CanPutIn(VesselKind.Pot, 3, false));
            Assert.AreEqual(CookOutcome.VesselFull, VesselRules.CanPutIn(VesselKind.Pot, 4, false), "the 5th ingredient");
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CanPutIn(VesselKind.Pot, -3, false), "a negative count is empty");
        }

        [Test]
        public void FullPot_RefusesTheFifthIngredient_ThroughKitchenState_AndItStaysInTheHands()
        {
            var kitchen = KitchenState.CreateDemo();
            string[] five = { "potato", "apple", "tomato", "onion", "potato" };
            for (int i = 0; i < five.Length; i++)
            {
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(five[i], out Item item));
                kitchen.Register(100 + i, item);
            }
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(100 + i, HeatTestKit.PotIndex, VesselKind.Pot, 0), five[i]);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutInto(104, HeatTestKit.PotIndex, VesselKind.Pot, 0));
            Assert.IsTrue(kitchen.TryGet(104, out _), "the refused potato stays in the hands");
            Assert.AreEqual(4, kitchen.ContentCount(HeatTestKit.PotIndex, VesselKind.Pot));
        }

        [Test]
        public void Pour_PanAndPotIntoPlateOrBowl_BowlOntoPlate_NothingElse()
        {
            VesselKind[] all = { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl };
            foreach (VesselKind source in all)
                foreach (VesselKind target in all)
                {
                    bool expected = (source == VesselKind.Pan || source == VesselKind.Pot) && (target == VesselKind.Plate || target == VesselKind.Bowl)
                        || source == VesselKind.Bowl && target == VesselKind.Plate;
                    Assert.AreEqual(expected, VesselRules.CanPour(source, target), source + " into " + target);
                }
        }

        [Test]
        public void EmptyHands_TakeFromBoardPanBowl_PickUpPotAndPlate()
        {
            Assert.IsTrue(VesselRules.TakesOutByHand(VesselKind.Board));
            Assert.IsTrue(VesselRules.TakesOutByHand(VesselKind.Pan));
            Assert.IsTrue(VesselRules.TakesOutByHand(VesselKind.Bowl));
            Assert.IsFalse(VesselRules.TakesOutByHand(VesselKind.Pot), "a pot is poured");
            Assert.IsFalse(VesselRules.TakesOutByHand(VesselKind.Plate), "a plate is sold");
        }

        [Test]
        public void HeatAndAssemblyKinds_NamesAndHomes()
        {
            Assert.IsTrue(VesselRules.IsHeat(VesselKind.Pan) && VesselRules.IsHeat(VesselKind.Pot));
            Assert.IsFalse(VesselRules.IsHeat(VesselKind.Board) || VesselRules.IsHeat(VesselKind.Plate) || VesselRules.IsHeat(VesselKind.Bowl));
            Assert.IsTrue(VesselRules.IsAssembly(VesselKind.Plate) && VesselRules.IsAssembly(VesselKind.Bowl));
            Assert.AreEqual("pot", VesselRules.Name(VesselKind.Pot));
            Assert.AreEqual("board", VesselRules.Name(VesselKind.Board));
            Assert.IsFalse(VesselRules.RespawnsAtHome(VesselKind.Plate), "plates come from the shelf");
            Assert.IsTrue(VesselRules.RespawnsAtHome(VesselKind.Pan));
            Assert.AreEqual(8, VesselRules.MaxPlatesInWorld);
        }

        [Test]
        public void LostCookware_LikeTheKnife()
        {
            Assert.IsTrue(VesselRules.IsLost(false, 1f));
            Assert.IsTrue(VesselRules.IsLost(true, -11f));
            Assert.IsFalse(VesselRules.IsLost(true, 1f));
            Assert.IsFalse(VesselRules.ShouldRespawn(10f, 14f));
            Assert.IsTrue(VesselRules.ShouldRespawn(10f, 15f));
        }
    }
}
