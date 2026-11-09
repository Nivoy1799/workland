using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Assembly = Worktown.Sim.Cooking.Assembly;

namespace Worktown.Game.Tests
{
    /// <summary>The spec 036 review fixes (done in the spec 038 round): cones in the real economy, the server stop, no free cones, the scoop's give-back.</summary>
    public class SpitReviewTests
    {
        private const double Rate = 30d;
        private const uint T0 = 1000;
        private const int SpitIndex = 16;
        private const int Cook = 3;

        private static uint At(double seconds) => T0 + (uint)System.Math.Round(seconds * Rate);

        private static SpitDesk Loaded(out KitchenState kitchen)
        {
            kitchen = KitchenState.CreateDemo();
            var desk = new SpitDesk(kitchen);
            desk.Register(SpitIndex);
            Assert.IsTrue(desk.Load(SpitIndex, T0));
            while (desk.LevelOf(SpitIndex) != Spit.High) desk.TurnKnob(SpitIndex, T0, Rate);
            return desk;
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
        public void ACone_IsOrderedThroughTheRealEconomy_PricedAsSeitan_ThenDeliveredAndLoaded()
        {
            var catalog = EconomyCatalog.CreateDefault();
            var engine = new EconomyEngine(catalog);
            EconomyState state = engine.OpenAccount(engine.Initial(), "cook").State;
            state = state with { Accounts = new[] { state.Accounts[0] with { Balance = 10000 } } };
            var wallet = new EngineWallet(engine, state);
            var stock = new StockRoom();
            int line = StockCatalog.ConeLine;

            Assert.IsTrue(StockCatalog.TryOffer(catalog, line, out SupplierOffer offer));
            Assert.AreEqual(StockCatalog.ConeId, offer.GoodId, "the stock line and the delivery stay the cone");
            Assert.AreEqual(Spit.MeatId, offer.PriceGoodId, "priced as seitan");
            long balance = wallet.State.Accounts[0].Balance, grocerPaid = wallet.State.Accounts[0].GrocerPaid;
            long revenue = wallet.State.Totals.GrocerRevenue;

            Assert.AreEqual(CookOutcome.Ok, stock.Order(line, 1, "cook", 0, catalog, wallet, out SupplyOrder order), "the engine takes the order");
            Assert.AreEqual(2640, balance - wallet.State.Accounts[0].Balance, "a cone costs $2,640 (30 seitan blocks at the bulk price)");
            Assert.AreEqual(2640, wallet.State.Accounts[0].GrocerPaid - grocerPaid, "GrocerPaid rises (a refund could give it back)");
            Assert.AreEqual(2640, wallet.State.Totals.GrocerRevenue - revenue);

            // The delivery and the load.
            stock.Deliver(order.DueMinute, null);
            Assert.IsTrue(stock.TryPeekWaiting(out BoxContents box));
            Assert.AreEqual((line, 1), (box.Line, box.Units));
            stock.ConfirmSpawned(300);
            Assert.AreEqual(CookOutcome.Ok, stock.Unpack(300, line, out int accepted, out _));
            Assert.AreEqual(1, accepted);
            Assert.AreEqual(2, stock.LineOf(line).Count, "the starting cone and the new one");
            var desk = new SpitDesk(KitchenState.CreateDemo());
            desk.Register(SpitIndex);
            Assert.AreEqual(CookOutcome.Ok, desk.LoadCone(SpitIndex, false, T0, () => stock.Take(line), () => stock.Return(line, 1)));
            Assert.AreEqual(1, stock.LineOf(line).Count);
            Assert.IsTrue(desk.HasCone(SpitIndex));
            // An offer whose price good is another good's price is refused by the engine (nothing charged).
            var wrong = new SupplierOffer(StockCatalog.ConeId, offer.GrocerPrice + 1, offer.GrocerItemsPerUnit, offer.PackSize, offer.DelayMinutes,
                Spit.MeatId);
            Assert.AreEqual(EconomyError.UnknownBaseItem, engine.TryBuySupply(wallet.State, "cook", wrong, 1).Error);
            Assert.AreEqual(EconomyError.UnknownBaseItem,
                engine.TryBuySupply(wallet.State, "cook", new SupplierOffer(StockCatalog.ConeId, offer.GrocerPrice, 30, 1, 60), 1).Error,
                "without its price good the cone has no grocer price (the old bug)");
        }

        [Test]
        public void AServerStop_PutsAnUntouchedCone_BackIntoStock_APartlyShavedOneIsLost()
        {
            SpitDesk untouched = Loaded(out _);
            Assert.AreEqual(1, untouched.ClearReturningUntouched(), "all 30 portions, never shaved: back to stock");
            Assert.IsFalse(untouched.HasCone(SpitIndex), "and the spit is bare");
            Assert.AreEqual(0, untouched.ClearReturningUntouched(), "once");

            var stock = new StockRoom();
            int line = StockCatalog.ConeLine;
            int before = stock.LineOf(line).Count;
            SpitDesk again = Loaded(out _);
            stock.Return(line, again.ClearReturningUntouched()); // what KitchenService does on the stop
            Assert.AreEqual(before + 1, stock.LineOf(line).Count);

            SpitDesk shaved = Loaded(out _);
            Assert.AreEqual(CookOutcome.Ok, shaved.Press(SpitIndex, Cook, true, At(55)));
            shaved.Release(SpitIndex, Cook, true, At(57), Rate, out _);
            Assert.AreEqual(0, shaved.ClearReturningUntouched(), "a shaved cone is lost, like food in a pan");
            Assert.IsFalse(shaved.HasCone(SpitIndex));
        }

        [Test]
        public void NoFreeCones_WithAnEconomyButNoSupply()
        {
            Assert.IsTrue(SpitDesk.MayLoadWithoutStock(true, true), "the stock room draws a unit");
            Assert.IsTrue(SpitDesk.MayLoadWithoutStock(true, false));
            Assert.IsFalse(SpitDesk.MayLoadWithoutStock(false, true), "money but no stock: a free cone would be a faucet");
            Assert.IsTrue(SpitDesk.MayLoadWithoutStock(false, false), "a scene without money (tests) loads for free");
        }

        [Test]
        public void AScoopOntoAFullBuild_GivesThePortionBack_TheTrayIsUnchanged()
        {
            SpitDesk desk = Loaded(out KitchenState kitchen);
            desk.Press(SpitIndex, Cook, true, At(55));
            desk.Release(SpitIndex, Cook, true, At(58), Rate, out _);
            Assert.AreEqual(3, desk.TrayCount(SpitIndex));
            kitchen.RegisterAssemblySpot(90, 8);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("wrap", out Item wrap));
            kitchen.Register(40, wrap);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(40, 90, VesselKind.Assembly, T0));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBuildTake(90, out Item open));
            kitchen.Register(77, open);
            // Fill the held build to CookingLimits.MaxBuildLayers with copies of one portion (the tray keeps its own).
            Item filler = desk.TrayOf(SpitIndex)[0];
            CookOutcome added = CookOutcome.Ok;
            for (int i = 0; i < 64 && added == CookOutcome.Ok; i++) added = kitchen.TryAddLayerToHeld(77, filler, out _);
            Assert.AreEqual(CookOutcome.VesselFull, added, "the build is full");
            Assert.IsTrue(kitchen.TryGet(77, out Item full));
            Assert.AreEqual(CookingLimits.MaxBuildLayers, Assembly.LayersOf(full).Count);
            IReadOnlyList<Item> trayBefore = new List<Item>(desk.TrayOf(SpitIndex));

            Assert.AreEqual(CookOutcome.VesselFull, desk.ScoopOntoBuild(SpitIndex, 77, out _), "TryAddLayerToHeld refuses after the take");
            Assert.AreEqual(3, desk.TrayCount(SpitIndex), "the portion went back");
            CollectionAssert.AreEquivalent(trayBefore, desk.TrayOf(SpitIndex), "the same portions");
            Assert.IsTrue(kitchen.TryGet(77, out Item still));
            Assert.AreSame(full, still, "the build is unchanged");
        }
    }
}
