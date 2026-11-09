using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Shop;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// The spec 035 review fixes (done in the spec 036 round): the plates over a server restart, the discard through the kitchen's own
    /// step with real stock and wallet counters, the pour that is gone at its release, the scrub's and the wipe's re-checks, the lost
    /// dish and bag, only full bags for the hygiene, the own other hold's reason, the plate shelf's "known" and the stock's minimum draw.
    /// </summary>
    public class CleaningReviewTests
    {
        private const int Cook = 3;

        private static int Total(DishRoom d, int inUse) => d.Clean + inUse + d.Eating + d.AtReturn + d.Carried + d.InBasin;

        [Test]
        public void AServerRestart_KeepsExactlyTheShopsPlates_BothScenePlatesServedToGo()
        {
            // Both scene plates leave to go (clean again at once: the shelf has 8), then the server restarts: the scene sets its two plates up
            // again on the worktop, so the shelf must hold 6, not 8 (10 plates).
            var room = new CleaningRoom(0);
            room.Dishes.PlateServed(false, 1);
            room.Dishes.PlateServed(false, 2);
            Assert.AreEqual(CleaningRules.TotalPlates, room.Dishes.Clean);
            room.EndSession(new List<int>());
            Assert.AreEqual(CleaningRules.TotalPlates - CleaningRules.ScenePlates, room.Dishes.Clean);
            Assert.AreEqual(CleaningRules.TotalPlates, Total(room.Dishes, CleaningRules.ScenePlates));
        }

        [Test]
        public void AServerRestart_KeepsExactlyTheShopsPlates_FiveTakenFromTheShelf()
        {
            // Five plates from the shelf are pooled objects FishNet despawns for good on a restart: they come back on the shelf, never lost.
            var room = new CleaningRoom(0);
            for (int i = 0; i < 5; i++) Assert.AreEqual(CookOutcome.Ok, room.Dishes.TakeClean());
            Assert.AreEqual(1, room.Dishes.Clean);
            room.EndSession(new List<int>());
            Assert.AreEqual(CleaningRules.TotalPlates - CleaningRules.ScenePlates, room.Dishes.Clean);
            // With dishes eating, waiting and in the sink, and one carried: the carried one waits at the return, the rest stay.
            var busy = new CleaningRoom(0);
            for (int i = 0; i < 4; i++) busy.Dishes.TakeClean();
            busy.Dishes.PlateServed(true, 0); // eating
            busy.Dishes.PlateServed(true, 0);
            busy.Dishes.ReturnDue(CleaningRules.EatMinutes); // both at the return
            busy.Dishes.TakeDirty(out long since);
            busy.Dishes.ConfirmCarried(70, since); // one carried
            busy.Dishes.PlateServed(true, 50); // eating again
            busy.EndSession(new List<int>());
            Assert.AreEqual(0, busy.Dishes.Carried);
            Assert.AreEqual(2, busy.Dishes.AtReturn, "the carried one is back at the return");
            Assert.AreEqual(1, busy.Dishes.Eating);
            Assert.AreEqual(CleaningRules.TotalPlates, Total(busy.Dishes, CleaningRules.ScenePlates));
        }

        [Test]
        public void ADiscard_ThroughTheKitchensStep_NeverRefundsOrCreatesMoneyOrStock()
        {
            // Real counters: an economy with the cook's account (it paid for the stock), the shop's stock and the kitchen's items.
            var catalog = EconomyCatalog.CreateDefault();
            var engine = new EconomyEngine(catalog);
            EconomyState state = engine.OpenAccount(engine.Initial(), "cook").State;
            state = state with { Accounts = new[] { state.Accounts[0] with { Balance = 5000 } } };
            var stock = new StockRoom();
            var wallet = new EngineWallet(engine, state);
            int tomato = StockCatalog.IndexOf("tomato");
            Assert.AreEqual(CookOutcome.Ok, stock.Order(tomato, 1, "cook", 0, catalog, wallet, out SupplyOrder order));
            Assert.AreEqual(CookOutcome.Ok, stock.Take(tomato));
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("tomato", out Item food));
            kitchen.Register(41, food);
            string money = EconomySnapshot.Digest(wallet.State);
            int count = stock.LineOf(tomato).Count, version = stock.Version;
            var room = new CleaningRoom();

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryDiscard(41, null)); // the step KitchenService.DiscardHeld runs
            Assert.AreEqual(CookOutcome.Ok, room.Discarded());
            Assert.IsFalse(kitchen.TryGet(41, out _), "the food is gone");
            Assert.AreEqual(CookOutcome.CannotDiscard, kitchen.TryDiscard(41, null), "once");
            Assert.AreEqual(money, EconomySnapshot.Digest(wallet.State), "no money moved");
            Assert.AreEqual((count, version), (stock.LineOf(tomato).Count, stock.Version), "no stock moved");
            Assert.AreEqual(1, stock.Book.Pending.Count, "the order is untouched");
            // Held cookware: its food goes, the cookware stays; empty cookware is nothing to throw away.
            kitchen.Register(42, food);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(42, 90, VesselKind.Bowl, 0));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryDiscard(90, VesselKind.Bowl));
            Assert.AreEqual(0, kitchen.ContentCount(90, VesselKind.Bowl));
            Assert.AreEqual(CookOutcome.CannotDiscard, kitchen.TryDiscard(90, VesselKind.Bowl));
            Assert.AreEqual(money, EconomySnapshot.Digest(wallet.State));
        }

        private sealed class EngineWallet : ISupplyWallet
        {
            private readonly EconomyEngine _engine;

            public EngineWallet(EconomyEngine engine, EconomyState state)
            {
                _engine = engine;
                State = state;
            }

            public EconomyState State { get; private set; }

            public bool CanAfford(long cost) => State.Accounts[0].Balance >= cost;

            public CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                PurchaseResult r = _engine.TryBuySupply(State, "cook", offer, packs);
                cost = r.Cost;
                if (!r.Ok) return CookOutcome.NotEnoughMoney;
                State = r.State;
                return CookOutcome.Ok;
            }

            public void Refund(long cost) => State = _engine.Refund(State, "cook", cost).State;
        }

        [Test]
        public void AReleaseOfAPourThatIsGone_ForgetsItsPourer_TheTapGoesFree()
        {
            var kitchen = KitchenState.CreateDemo();
            var desk = new DrinkDesk(kitchen);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(50, cup);
            const int station = 9, tap = 1;
            Assert.AreEqual(CookOutcome.Ok, desk.Press(station, tap, Cook, 50, 100, null));
            var player = new object();
            var pourers = new Dictionary<long, object> { [DrinkDesk.KeyOf(station, tap)] = player };
            Assert.IsFalse(desk.ForgetGonePour(pourers, station, tap, player), "the pour still runs: kept");
            kitchen.CancelPoursOfCup(50); // the cup was put down
            Assert.IsFalse(desk.ForgetGonePour(pourers, station, tap, new object()), "someone else's entry is not ours to forget");
            Assert.IsTrue(desk.ForgetGonePour(pourers, station, tap, player));
            Assert.AreEqual(0, pourers.Count);
            Assert.AreEqual(0, kitchen.DrinkFlags(station), "the tap's bit is off");
        }

        [Test]
        public void TheScrubAndTheWipe_CountOnlyForAPlayerStillThere_WithTheRightHands()
        {
            Assert.IsTrue(CleaningService.ScrubCounts(true, false));
            Assert.IsFalse(CleaningService.ScrubCounts(true, true), "picked something up meanwhile: no scrub (also at the auto-stop)");
            Assert.IsFalse(CleaningService.ScrubCounts(false, false), "left: no scrub");
            Assert.IsTrue(CleaningService.WipeCounts(true, true));
            Assert.IsFalse(CleaningService.WipeCounts(true, false), "the cloth was put down: no wipe");
            Assert.IsFalse(CleaningService.WipeCounts(false, true));
        }

        [Test]
        public void ALostDish_WaitsAtTheReturnAsDirtyAsBefore_ALostBag_PutsItsTrashBack()
        {
            var room = new CleaningRoom(0);
            room.Dishes.PlateServed(true, 0);
            room.Dishes.ReturnDue(CleaningRules.EatMinutes);
            room.Dishes.TakeDirty(out long since);
            room.Dishes.ConfirmCarried(60, since);
            Assert.IsTrue(room.Dishes.CarriedLost(60));
            Assert.AreEqual(1, room.Dishes.AtReturn);
            Assert.AreEqual(since, room.Dishes.DirtySince()[0], "as dirty as before (the grace does not restart)");
            Assert.IsFalse(room.Dishes.CarriedLost(60), "once");
            for (int i = 0; i < 5; i++) room.Discarded();
            room.TakeBag(false, out int size);
            room.ConfirmBag(61, size);
            for (int i = 0; i < 9; i++) room.Discarded();
            Assert.IsTrue(room.BagLost(61));
            Assert.AreEqual(Worktown.Sim.Economy.HygieneRules.TrashCapacity, room.TrashFill, "back in the can, at most full");
            Assert.IsFalse(room.BagLost(61));
        }

        [Test]
        public void OnlyAFullBag_CountsAsAFullTrash()
        {
            var room = new CleaningRoom(0);
            for (int i = 0; i < 3; i++) room.Discarded();
            room.TakeBag(false, out int small);
            room.ConfirmBag(62, small);
            Assert.IsFalse(room.Sources(1, 0, false).TrashFull, "a bag of 3 carried out is no full trash");
            for (int i = 0; i < Worktown.Sim.Economy.HygieneRules.TrashCapacity; i++) room.Discarded();
            room.TakeBag(false, out int full);
            room.ConfirmBag(63, full);
            Assert.IsTrue(room.HasFullBag);
            Assert.IsTrue(room.Sources(1, 0, false).TrashFull, "a full bag not taken out yet still counts");
            room.Binned(63);
            Assert.IsFalse(room.Sources(1, 0, false).TrashFull);
        }

        [Test]
        public void YourOwnOtherHold_GetsItsOwnReason_NotSomeoneElse()
        {
            var room = new CleaningRoom(0);
            Assert.AreEqual(CookOutcome.Ok, room.Press(9, CleaningRules.HoldWipe, Cook, 10));
            Assert.AreEqual(CookOutcome.StillHolding, room.Press(14, CleaningRules.HoldScrub, Cook, 11), "your own wipe blocks your scrub");
            Assert.AreEqual(CookOutcome.DispenserBusy, room.Press(9, CleaningRules.HoldWipe, Cook + 1, 11), "another cook's is someone's");
        }

        [Test]
        public void ThePlateShelf_IsKnownOnlyWithACleaningService()
        {
            Assert.IsTrue(CleaningRules.ShelfKnown(true, true));
            Assert.IsFalse(CleaningRules.ShelfKnown(true, false), "without one the shelf is endless: never \"No clean plates\"");
            Assert.IsFalse(CleaningRules.ShelfKnown(false, true));
        }

        [Test]
        public void APress_NeedsTheSmallestStep_LeftInTheStock()
        {
            var room = new StockRoom();
            int garlic = StockCatalog.IndexOf("garlic_herb_sauce");
            while (room.LineOf(garlic).AvailablePercent > 5) room.Draw(garlic, 5, 5, out _);
            Assert.IsTrue(room.CanDraw(garlic, 1));
            Assert.IsFalse(room.CanDraw(garlic, PriceRules.PortionStepPercent), "5 % left: no squeeze (its step is 10 %)");
            var wallet = new StockWallet(room);
            Assert.IsFalse(wallet.CanAfford(Cook, "garlic_herb_sauce", PriceRules.PortionStepPercent));
            Assert.IsTrue(((IDrinkWallet)wallet).CanAfford(Cook, "lemonade", 1, false));
            // The client: a tap whose kegs show no whole cup says so in grey (the server refuses a press below the smallest share).
            AimTarget tap = AimTarget.DrinkTool(0, 0, "lemonade", -1).WithStock(StockCatalog.IndexOf("lemonade"), 0);
            Held cup = Held.Cup("empty cup", -1, DrinkRules.LevelEmpty, false);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(cup, tap));
            Assert.AreEqual(InteractionRules.AlmostEmptyText, InteractionRules.PromptFor(cup, tap));
        }
    }
}
