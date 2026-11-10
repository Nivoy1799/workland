using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 033 AC3 (and the rules around it): the drink dispenser's server steps through KitchenState and the pure DrinkDesk (press and
    /// release timed by the server's ticks, a repeated press keeping its start, the stop at 500 ml, charged once with the cup and the
    /// drink's portion, a refusal charging nothing, a player who left freeing the tap at once, a lid only on a filled cup), the station
    /// byte and the cup's look, the client's rules (actions, prompts, reasons), and drinks as a line of a guest's order (GuestTab, the
    /// match, the queue's pick, the ticket, one payment at the register for the whole order). Spec 042: a tap pours into the cup on its
    /// spot on the drip tray (placed there first), never into the hands.
    /// </summary>
    public class DrinkStationTests
    {
        private const int Dispenser = 10;
        private const int Owner = 3;
        private const int Cup = 50;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;
        private const int Lemonade = 0, IcedTea = 1;

        private static uint Real(double seconds) => Start + (uint)System.Math.Round(seconds * Rate);

        /// <summary>Counts the pours charged; refuses with <see cref="Refuse"/>; <see cref="Broke"/> fails the press's check.</summary>
        private sealed class CountingWallet : IDrinkWallet
        {
            public readonly List<(int Owner, string Drink, int Percent, bool Cup)> Charges = new List<(int, string, int, bool)>();
            public CookOutcome Refuse = CookOutcome.Ok;
            public bool Broke;
            public int Calls;

            public CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost)
            {
                Calls++;
                cost = 0;
                if (Refuse != CookOutcome.Ok) return Refuse;
                Charges.Add((owner, drinkId, percent, withCup));
                cost = PriceRules.FillCost(35, 60, percent, withCup);
                return CookOutcome.Ok;
            }

            public bool CanAfford(int owner, string drinkId, int percent, bool withCup) => !Broke;

            /// <summary>Spec 034: what a refused press answers (money here).</summary>
            public CookOutcome Short => CookOutcome.NotEnoughMoney;

            /// <summary>Spec 034: the refund backstop's calls.</summary>
            public int Refunds;

            public void Refund(int owner, string drinkId, int percent, bool withCup, long cost) => Refunds++;
        }

        /// <summary>A kitchen with an empty cup <paramref name="id"/>, standing on tap <paramref name="onTap"/>'s spot (spec 042; -1: in the hands).</summary>
        private static KitchenState WithCup(int id = Cup, int onTap = Lemonade)
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(id, cup);
            if (onTap >= 0) Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, onTap, id));
            return kitchen;
        }

        private static Item CupOf(KitchenState kitchen, int id = Cup)
        {
            Assert.IsTrue(kitchen.TryGet(id, out Item item));
            return item;
        }

        // ---- the server: press, release, the stop, money ----

        [Test]
        public void APressAndARelease_FillTheCupOnTheTapsSpot_ByTheServersTicks_ChargedOnce()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Start, wallet));
            Assert.AreEqual(0, wallet.Calls, "a press charges nothing");
            Assert.IsTrue(DrinkRules.IsPouring(kitchen.DrinkFlags(Dispenser), Lemonade), "the byte says it pours");
            Assert.IsTrue(Drinks.IsEmptyCup(CupOf(kitchen)), "nothing in the cup until the release");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(1, wallet.Calls, "charged exactly once");
            Assert.AreEqual((Owner, "lemonade", 90, true), wallet.Charges[0], "the cup and 90 % of a full cup's lemonade (360 ml)");
            Assert.AreEqual(360f, r.Poured, 0.01f);
            Item cup = CupOf(kitchen);
            Assert.IsTrue(Drinks.IsFill(cup));
            Assert.AreEqual("lemonade", Drinks.DrinkOf(cup));
            Assert.AreEqual(Drinks.Right, Drinks.LevelOf(cup), "at the line");
            Assert.AreEqual(360f, kitchen.CupMl(Cup), 0.01f, "the exact ml stays on the server");
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser), "the tap is free, no spill");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(2.5), Rate, wallet, out _), "a second release is harmless");
            Assert.AreEqual(1, wallet.Calls, "and charges nothing");
        }

        [Test]
        public void ARepeatedPress_KeepsTheFirstStart()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Real(1.5), wallet), "the same player again: Ok");
            Assert.IsTrue(kitchen.IsPouring(Dispenser, Lemonade, out _, out uint startTick, out _));
            Assert.AreEqual(Start, startTick, "the first start stays");
            Assert.IsFalse(kitchen.TryFindOverduePour(Real(2.45), Rate, out _, out _, out _));
            Assert.IsTrue(kitchen.TryFindOverduePour(Real(2.5), Rate, out _, out _, out _), "the stop still comes 2.5 s after the first press");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(1.9), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(380f, r.Total, 0.01f, "1.9 s from the first press, not 0.4 s");
        }

        [Test]
        public void TheServer_StopsAtFiveHundredMl_ASpill_ChargedOnce_ALateReleaseFree()
        {
            KitchenState kitchen = WithCup(onTap: IcedTea);
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, IcedTea, Owner, Start, wallet);
            Assert.IsTrue(kitchen.TryFindOverduePour(Real(2.5), Rate, out int station, out int tap, out int owner));
            Assert.AreEqual((Dispenser, IcedTea, Owner), (station, tap, owner));
            Assert.AreEqual(CookOutcome.Ok, desk.End(station, tap, owner, Real(2.6), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(Drinks.MaxMl, r.Total, 0.01f, "never more than the stop");
            Assert.IsTrue(r.Spilled);
            Assert.AreEqual(Drinks.Spilled, Drinks.LevelOf(CupOf(kitchen)));
            Assert.AreEqual(1, wallet.Calls);
            Assert.AreEqual(100, wallet.Charges[0].Percent, "a spill pays the whole cup's drink, no more");
            Assert.IsTrue(kitchen.HasSpill(Dispenser), "the spill stays for spec 035");
            Assert.IsTrue(DrinkRules.HasSpill(kitchen.DrinkFlags(Dispenser)), "in the byte");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, IcedTea, Owner, Real(4), Rate, wallet, out _));
            Assert.AreEqual(1, wallet.Calls, "the late release after the stop charges nothing");
            Assert.AreEqual(CookOutcome.CupFull, desk.Press(Dispenser, IcedTea, Owner, Real(5), wallet), "nothing more fits");
            Assert.IsTrue(kitchen.ClearSpill(Dispenser), "spec 035's hook");
            Assert.IsFalse(DrinkRules.HasSpill(kitchen.DrinkFlags(Dispenser)));
            // A spill is worth less than the right fill.
            Assert.Less(ItemRater.Rate(CupOf(kitchen)).Quality, Drinks.Taste(Drinks.IcedTea.Stats) - 0.1f);
        }

        [Test]
        public void ATopUp_OfTheSameDrink_PaysItsOwnShare_AndNoSecondCup()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.0), Rate, wallet, out _);
            Assert.AreEqual(Drinks.Low, Drinks.LevelOf(CupOf(kitchen)));
            Assert.AreEqual(CookOutcome.OtherDrink, desk.CanPlace(Cup, IcedTea), "spec 042: it does not go under another tap");
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, IcedTea, Cup)); // if it stood there anyway (the rule, not the place, decides)
            Assert.AreEqual(CookOutcome.OtherDrink, desk.Press(Dispenser, IcedTea, Owner, Real(2), wallet), "another drink: no");
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, Lemonade, Cup));
            Assert.AreEqual(-1, kitchen.CupOnSpot(Dispenser, IcedTea), "it left the other spot");
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Real(2), wallet));
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(2.8), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(2, wallet.Calls);
            Assert.AreEqual((Owner, "lemonade", 50, true), wallet.Charges[0]);
            Assert.AreEqual((Owner, "lemonade", 40, false), wallet.Charges[1], "the top-up: 160 ml, no cup");
            Assert.AreEqual(360f, r.Total, 0.01f);
            Assert.AreEqual(Drinks.Right, Drinks.LevelOf(CupOf(kitchen)));
        }

        [Test]
        public void ARefusal_ChargesNothing_AndChangesNothing()
        {
            // The wallet says no at the release: the cup stays empty, the tap is free.
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var poor = new CountingWallet { Refuse = CookOutcome.NotEnoughMoney };
            Item before = CupOf(kitchen);
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Start, poor));
            Assert.AreEqual(CookOutcome.NotEnoughMoney, desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, poor, out _));
            Assert.AreEqual(0, poor.Charges.Count);
            Assert.AreSame(before, CupOf(kitchen));
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser));

            // Spec 042: the cup was taken off the tray while pouring: the pour stops at once, nothing charged.
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Real(3), wallet);
            Assert.IsTrue(desk.TakenOff(Cup, out int station, out int tap), "a pour stopped");
            Assert.AreEqual((Dispenser, Lemonade), (station, tap));
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser), "the tap is free at once");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(4), Rate, wallet, out _), "its late release is harmless");
            Assert.AreEqual(0, wallet.Calls);
            Assert.AreSame(before, CupOf(kitchen));
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, Lemonade, Cup));

            // Someone else pours that tap: busy; their release charges nobody.
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Real(5), wallet));
            AddCup(kitchen, Cup + 1);
            Assert.AreEqual(CookOutcome.DispenserBusy, desk.Press(Dispenser, Lemonade, Owner + 1, Real(5.5), wallet));
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner + 1, Real(6), Rate, wallet, out _));
            Assert.AreEqual(0, wallet.Calls, "the other player's release charges nobody");
            // The same player cannot pour two taps at once (one pour per player).
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, IcedTea, Cup + 1));
            Assert.AreEqual(CookOutcome.DispenserBusy, desk.Press(Dispenser, IcedTea, Owner, Real(5.5), wallet));

            // A press without the money for the smallest pour takes no tap.
            KitchenState broke = WithCup();
            var desk2 = new DrinkDesk(broke);
            Assert.AreEqual(CookOutcome.NotEnoughMoney, desk2.Press(Dispenser, Lemonade, Owner, Start, new CountingWallet { Broke = true }));
            Assert.AreEqual(0, broke.DrinkFlags(Dispenser));
        }

        private static void AddCup(KitchenState kitchen, int id)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateCup(out Item cup));
            kitchen.Register(id, cup);
        }

        [Test]
        public void ThePress_NeedsAnOpenCupOfItsDrink()
        {
            KitchenState kitchen = WithCup(onTap: -1);
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.NeedsCup, desk.Press(Dispenser, Lemonade, Owner, Start, wallet), "spec 042: the cup in the hands, none on the spot");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("lettuce", out Item lettuce));
            kitchen.Register(77, lettuce);
            Assert.AreEqual(CookOutcome.NeedsCup, desk.CanPlace(77, Lemonade), "lettuce does not go under a tap");
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, Lemonade, 77)); // if it lay there anyway, the tap still pours nothing into it
            Assert.AreEqual(CookOutcome.NeedsCup, desk.Press(Dispenser, Lemonade, Owner, Start, wallet), "no cup");
            Assert.IsTrue(kitchen.TakeCupOffSpot(77, out _, out _, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, desk.Press(Dispenser, 5, Owner, Start, wallet));
            Assert.AreEqual(CookOutcome.Ok, desk.CanPlace(Cup, Lemonade), "an empty cup goes under any tap");
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, Lemonade, Cup));
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, wallet, out _);
            Assert.AreEqual(CookOutcome.Ok, desk.Lid(Cup, out _));
            Assert.AreEqual(CookOutcome.CupClosed, desk.Press(Dispenser, Lemonade, Owner, Real(3), wallet), "a lidded cup");
            Assert.AreEqual(CookOutcome.CupClosed, desk.CanPlace(Cup, Lemonade), "a lidded cup does not go back under a tap");
        }

        [Test]
        public void APlayerWhoLeft_FreesTheTapAtOnce_WithoutACharge()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            var pourers = new Dictionary<long, int> { [DrinkDesk.KeyOf(Dispenser, Lemonade)] = Owner };
            var dropped = new List<long>();
            desk.DropLeftPours(pourers, who => false, dropped);
            CollectionAssert.AreEqual(new[] { DrinkDesk.KeyOf(Dispenser, Lemonade) }, dropped);
            Assert.AreEqual(0, pourers.Count);
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser), "no stream for anyone");
            Assert.AreEqual(0, wallet.Calls);
            Assert.IsTrue(Drinks.IsEmptyCup(CupOf(kitchen)));
            Assert.AreEqual(Cup, kitchen.CupOnSpot(Dispenser, Lemonade), "the cup stays on the tray");
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner + 1, Real(0.1), wallet), "someone else can use it at once");
            // A present player keeps its pour.
            pourers[DrinkDesk.KeyOf(Dispenser, Lemonade)] = Owner + 1;
            desk.DropLeftPours(pourers, who => true, dropped);
            Assert.IsEmpty(dropped);
            Assert.AreEqual(Dispenser, DrinkDesk.StationOfKey(DrinkDesk.KeyOf(Dispenser, 2)));
            Assert.AreEqual(2, DrinkDesk.TapOfKey(DrinkDesk.KeyOf(Dispenser, 2)));
        }

        [Test]
        public void ALid_GoesOnlyOnAFilledCup_Once_AndNotUnderATap()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.NotFilled, desk.Lid(Cup, out _), "an empty cup");
            Assert.AreEqual(CookOutcome.NeedsCup, desk.Lid(-1, out _), "nothing held");
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.0), Rate, wallet, out _);
            desk.Press(Dispenser, Lemonade, Owner, Real(2), wallet);
            Assert.AreEqual(CookOutcome.DispenserBusy, desk.Lid(Cup, out _), "not while it is under a tap");
            desk.Release(Dispenser, Lemonade, Owner, Real(2.8), Rate, wallet, out _);
            Assert.AreEqual(CookOutcome.Ok, desk.Lid(Cup, out Item lidded));
            Assert.IsTrue(Drinks.IsLidded(CupOf(kitchen)));
            Assert.AreSame(lidded, CupOf(kitchen));
            Assert.AreEqual(2, wallet.Calls, "the lid is free");
            Assert.AreEqual(CookOutcome.CupClosed, desk.Lid(Cup, out _), "once");
            Assert.IsTrue(AssemblyRules.IsPlated(lidded), "finished: sold as it is");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(Cup, out Item sold));
            Assert.AreSame(lidded, sold);
        }

        [Test]
        public void AnOpenCup_IsNotSoldAndGoesIntoNoCookware()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            desk.Press(Dispenser, Lemonade, Owner, Start, null);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, null, out _);
            Assert.AreEqual(CookOutcome.SimRefused, kitchen.TryPlate(Cup, out _), "no lid: the counter cannot sell it");
            kitchen.RegisterAssemblySpot(90, 8);
            Assert.AreNotEqual(CookOutcome.Ok, kitchen.TryPutInto(Cup, 90, VesselKind.Assembly, Start), "not on a build");
            Assert.AreNotEqual(CookOutcome.Ok, kitchen.TryPutInto(Cup, 91, VesselKind.Plate, Start), "not on a plate");
            Assert.AreNotEqual(CookOutcome.Ok, kitchen.TryPutInto(Cup, 92, VesselKind.Board, Start), "not on a board");
            // 033 review (rule 6): not into an empty bowl either (a lone first item in a bowl skips the mix check).
            Assert.AreEqual(CookOutcome.NotFood, kitchen.TryPutInto(Cup, 93, VesselKind.Bowl, Start), "not into an empty bowl");
            Assert.AreEqual(CookOutcome.NotFood, AssemblyRules.CanAdd(VesselKind.Bowl, 0, CupOf(kitchen)), "the rule refuses any cup");
            Assert.IsTrue(kitchen.TryGet(Cup, out _), "still in the hands");
        }

        [Test]
        public void TheReleaseAlone_NeedsNoReach()
        {
            for (int tap = 0; tap < DrinkRules.TapCount; tap++)
            {
                Assert.IsFalse(DrinkDesk.NeedsReach(StationKind.DrinkDispenser, DrinkRules.PourStopOption(tap)));
                Assert.IsTrue(DrinkDesk.NeedsReach(StationKind.DrinkDispenser, DrinkRules.PourStartOption(tap)));
            }
            Assert.IsTrue(DrinkDesk.NeedsReach(StationKind.DrinkDispenser, DrinkRules.CupOption));
            Assert.IsTrue(DrinkDesk.NeedsReach(StationKind.DrinkDispenser, DrinkRules.LidOption));
            Assert.IsTrue(DrinkDesk.NeedsReach(StationKind.Assembly, DrinkRules.PourStopOption(0)), "only the dispenser's release");
        }

        [Test]
        public void EmptyCups_AreFree_ButCapped()
        {
            var kitchen = KitchenState.CreateDemo();
            var desk = new DrinkDesk(kitchen);
            Assert.AreEqual(CookOutcome.HandsFull, desk.CanTakeCup(true));
            for (int i = 0; i < DrinkRules.MaxEmptyCups; i++)
            {
                Assert.AreEqual(CookOutcome.Ok, desk.CanTakeCup(false));
                kitchen.TryCreateCup(out Item cup);
                kitchen.Register(200 + i, cup);
            }
            Assert.AreEqual(CookOutcome.NoCupsLeft, desk.CanTakeCup(false));
            kitchen.TryRemove(200, out _);
            Assert.AreEqual(CookOutcome.Ok, desk.CanTakeCup(false));
        }

        // ---- spec 042: the cup spots on the drip tray ----

        [Test]
        public void APour_NeedsACupOnThatTapsSpot()
        {
            KitchenState kitchen = WithCup(onTap: IcedTea);
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.NeedsCup, desk.Press(Dispenser, Lemonade, Owner, Start, wallet), "the cup stands under another tap");
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser), "no tap taken");
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, IcedTea, Owner, Start, wallet), "its own tap pours into it");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, IcedTea, Owner, Real(1.8), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(Cup, r.CupObjectId, "the pour went into the cup on the spot");
            Assert.AreEqual("iced_tea", Drinks.DrinkOf(CupOf(kitchen)));
            Assert.AreEqual(Cup, kitchen.CupOnSpot(Dispenser, IcedTea), "it stays on the tray after the pour");
            Assert.IsTrue(kitchen.IsOnSpot(Cup, out int station, out int tap));
            Assert.AreEqual((Dispenser, IcedTea), (station, tap));
            Assert.AreEqual(-1, kitchen.CupOnSpot(Dispenser + 1, IcedTea), "another dispenser's spot is its own");
            Assert.AreEqual(-1, kitchen.CupOnSpot(Dispenser, 7), "no such tap");
        }

        [Test]
        public void TheWrongDrink_IsRefused_AtTheSpotAndAtTheTap_ChangingNothing()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.0), Rate, wallet, out _);
            Item lemonade = CupOf(kitchen);
            Assert.AreEqual(CookOutcome.OtherDrink, desk.CanPlace(Cup, IcedTea));
            Assert.AreEqual(CookOutcome.OtherDrink, desk.CanPlace(Cup, 2));
            Assert.AreEqual(CookOutcome.Ok, desk.CanPlace(Cup, Lemonade), "back under its own tap (a top-up)");
            Assert.IsTrue(kitchen.PutCupOnSpot(Dispenser, 2, Cup));
            Assert.AreEqual(CookOutcome.OtherDrink, desk.Press(Dispenser, 2, Owner, Real(2), wallet));
            Assert.AreEqual(1, wallet.Calls, "nothing more charged");
            Assert.AreSame(lemonade, CupOf(kitchen), "the cup is unchanged");
            Assert.AreEqual(0, kitchen.DrinkFlags(Dispenser));
            Assert.AreEqual(CookOutcome.NeedsCup, desk.CanPlace(999, Lemonade), "an unknown item");
            Assert.AreEqual(CookOutcome.InvalidOption, desk.CanPlace(Cup, 5));
        }

        [Test]
        public void MoneyAndStock_GoExactlyOnce_PerPour_OnTheTray()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            desk.Press(Dispenser, Lemonade, Owner, Real(0.5), wallet); // a repeated press
            Assert.IsTrue(kitchen.TryFindOverduePour(Real(2.5), Rate, out int station, out int tap, out int owner), "the auto-stop at the spill");
            Assert.AreEqual(CookOutcome.Ok, desk.End(station, tap, owner, Real(2.5), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.IsTrue(r.Spilled);
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(2.7), Rate, wallet, out _), "the late release");
            Assert.AreEqual(1, wallet.Calls, "one charge for the whole pour");
            Assert.AreEqual((Owner, "lemonade", 100, true), wallet.Charges[0]);
            Assert.IsTrue(kitchen.HasSpill(Dispenser), "over the tray: the 035 spill");
        }

        [Test]
        public void ACupTakenAwayMidPour_StopsIt_WithNoCharge()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            var wallet = new CountingWallet();
            Item before = CupOf(kitchen);
            desk.Press(Dispenser, Lemonade, Owner, Start, wallet);
            Assert.AreEqual(CookOutcome.Ok, desk.CanTake(Cup, Owner), "its pourer may take it");
            Assert.IsTrue(desk.Moved(Cup, false, -1, -1, out int station, out int tap), "taken up: the pour stopped");
            Assert.AreEqual((Dispenser, Lemonade), (station, tap));
            Assert.IsFalse(kitchen.IsPouring(Dispenser, Lemonade, out _, out _, out _));
            Assert.AreEqual(-1, kitchen.CupOnSpot(Dispenser, Lemonade), "the spot is free");
            Assert.IsFalse(kitchen.TryFindOverduePour(Real(9), Rate, out _, out _, out _), "no stop comes later");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, wallet, out DrinkDesk.PourResult r));
            Assert.AreEqual(0, wallet.Calls, "nothing charged");
            Assert.AreEqual(0f, r.Poured);
            Assert.AreSame(before, CupOf(kitchen), "the cup is still empty");
            Assert.AreEqual(0f, kitchen.CupMl(Cup));
            // Back on the tray, a new pour starts fresh.
            Assert.IsFalse(desk.Moved(Cup, true, Dispenser, Lemonade, out _, out _), "putting it down stops nothing");
            Assert.AreEqual(Cup, kitchen.CupOnSpot(Dispenser, Lemonade));
            Assert.AreEqual(CookOutcome.Ok, desk.Press(Dispenser, Lemonade, Owner, Real(3), wallet));
            Assert.IsFalse(desk.TakenOff(Cup + 9, out _, out _), "an item on no spot: nothing stops");
        }

        [Test]
        public void AnotherPlayer_CannotTakeTheCup_DuringAPour()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            Assert.AreEqual(CookOutcome.Ok, desk.CanTake(Cup, Owner + 1), "nobody pours: anyone may take it");
            desk.Press(Dispenser, Lemonade, Owner, Start, null);
            Assert.AreEqual(CookOutcome.DispenserBusy, desk.CanTake(Cup, Owner + 1), "someone pours into it");
            Assert.AreEqual(CookOutcome.Ok, desk.CanTake(Cup, Owner), "the pourer may");
            Assert.AreEqual(CookOutcome.Ok, desk.CanTake(Cup + 1, Owner + 1), "any other item");
            Assert.IsTrue(kitchen.IsPouring(Dispenser, Lemonade, out _, out _, out _), "asking changes nothing");
            desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, null, out _);
            Assert.AreEqual(CookOutcome.Ok, desk.CanTake(Cup, Owner + 1), "after the pour anyone may take it");
        }

        [Test]
        public void TheLid_GoesOnTheCupInTheHands_ElseTheFirstFilledCupOnASpot()
        {
            KitchenState kitchen = WithCup(onTap: IcedTea);
            var desk = new DrinkDesk(kitchen);
            Assert.AreEqual(-1, desk.LidTarget(Dispenser, -1), "an empty cup on the tray gets no lid");
            desk.Press(Dispenser, IcedTea, Owner, Start, null);
            Assert.AreEqual(-1, desk.LidTarget(Dispenser, -1), "not while it pours");
            desk.Release(Dispenser, IcedTea, Owner, Real(1.8), Rate, null, out _);
            Assert.AreEqual(Cup, desk.LidTarget(Dispenser, -1), "the filled cup on the tray");
            AddCup(kitchen, Cup + 1);
            Assert.AreEqual(Cup + 1, desk.LidTarget(Dispenser, Cup + 1), "a cup in the hands first (its own refusals then)");
            Assert.AreEqual(CookOutcome.NotFilled, desk.Lid(desk.LidTarget(Dispenser, Cup + 1), out _));
            Assert.AreEqual(Cup, desk.LidTarget(Dispenser, 12345), "hands holding no cup: the tray's");
            Assert.AreEqual(CookOutcome.Ok, desk.Lid(desk.LidTarget(Dispenser, -1), out Item lidded));
            Assert.IsTrue(Drinks.IsLidded(CupOf(kitchen)));
            Assert.AreSame(lidded, CupOf(kitchen));
            Assert.AreEqual(Cup, kitchen.CupOnSpot(Dispenser, IcedTea), "lidded where it stands");
            Assert.AreEqual(-1, desk.LidTarget(Dispenser, -1), "once");
            Assert.AreEqual(-1, desk.LidTarget(Dispenser + 1, -1), "only this dispenser's spots");
        }

        [Test]
        public void TheSpotByte_RoundTrips_AndTheClientsRulesReadIt()
        {
            Assert.AreEqual(DrinkRules.NoSpotCup, DrinkRules.SpotByte(false, true, 2, DrinkRules.LevelFull));
            Assert.IsFalse(DrinkRules.HasSpotCup(DrinkRules.NoSpotCup));
            Assert.AreEqual(-1, DrinkRules.SpotCupTap(DrinkRules.NoSpotCup));
            for (int tap = -1; tap < DrinkRules.TapCount; tap++)
                for (byte level = DrinkRules.LevelEmpty; level <= DrinkRules.LevelSpilled; level++)
                    foreach (bool lid in new[] { false, true })
                    {
                        byte b = DrinkRules.SpotByte(true, lid, tap, level);
                        Assert.IsTrue(DrinkRules.HasSpotCup(b));
                        Assert.AreEqual((lid, tap, level), (DrinkRules.SpotLidded(b), DrinkRules.SpotCupTap(b), DrinkRules.SpotLevel(b)));
                    }
            byte empty = DrinkRules.SpotByte(true, false, -1, DrinkRules.LevelEmpty);
            byte low = DrinkRules.SpotByte(true, false, Lemonade, DrinkRules.LevelLow);
            Assert.AreEqual(CookOutcome.NeedsCup, DrinkRules.CanPourOnSpot(DrinkRules.NoSpotCup, Lemonade, 0));
            Assert.AreEqual(CookOutcome.Ok, DrinkRules.CanPourOnSpot(empty, IcedTea, 0));
            Assert.AreEqual(CookOutcome.Ok, DrinkRules.CanPourOnSpot(low, Lemonade, 0));
            Assert.AreEqual(CookOutcome.OtherDrink, DrinkRules.CanPourOnSpot(low, IcedTea, 0));
            Assert.AreEqual(CookOutcome.DispenserBusy, DrinkRules.CanPourOnSpot(low, Lemonade, DrinkRules.WithPouring(0, Lemonade, true)));
            Assert.AreEqual(CookOutcome.Ok, DrinkRules.CanLidOnSpot(low));
            Assert.AreEqual(CookOutcome.NotFilled, DrinkRules.CanLidOnSpot(empty));
            Assert.AreEqual(CookOutcome.NeedsCup, DrinkRules.CanLidOnSpot(DrinkRules.NoSpotCup));
            Assert.AreEqual(CookOutcome.Ok, DrinkRules.CanStand(true, false, -1, DrinkRules.LevelEmpty, 2));
            Assert.AreEqual(CookOutcome.Ok, DrinkRules.CanStand(true, false, 2, DrinkRules.LevelSpilled, 2), "a full cup of its drink (to lid it there)");
            Assert.AreEqual(CookOutcome.OtherDrink, DrinkRules.CanStand(true, false, 1, DrinkRules.LevelRight, 2));
            Assert.AreEqual(CookOutcome.CupClosed, DrinkRules.CanStand(true, true, 2, DrinkRules.LevelRight, 2));
            Assert.AreEqual(CookOutcome.NeedsCup, DrinkRules.CanStand(false, false, -1, 0, 2));
        }

        // ---- the rules shared by server and clients ----

        [Test]
        public void TheOptions_RoundTrip_AndDoNotCollide()
        {
            var seen = new HashSet<int>();
            for (int tap = 0; tap < DrinkRules.TapCount; tap++)
            {
                Assert.IsTrue(DrinkRules.TryParsePour(DrinkRules.PourStartOption(tap), out int t, out bool start));
                Assert.AreEqual((tap, true), (t, start));
                Assert.IsTrue(DrinkRules.TryParsePour(DrinkRules.PourStopOption(tap), out t, out start));
                Assert.AreEqual((tap, false), (t, start));
                Assert.IsTrue(seen.Add(DrinkRules.PourStartOption(tap)));
                Assert.IsTrue(seen.Add(DrinkRules.PourStopOption(tap)));
            }
            Assert.IsTrue(seen.Add(DrinkRules.CupOption));
            Assert.IsTrue(seen.Add(DrinkRules.LidOption));
            Assert.IsFalse(DrinkRules.TryParsePour(DrinkRules.CupOption, out _, out _));
            Assert.IsFalse(DrinkRules.TryParsePour(0, out _, out _));
            Assert.AreEqual(DrinkRules.CupOption, DrinkRules.ClickOption(DrinkRules.CupTool));
            Assert.AreEqual(DrinkRules.LidOption, DrinkRules.ClickOption(DrinkRules.LidTool));
            Assert.AreEqual(-1, DrinkRules.ClickOption(0));
            CollectionAssert.AreEqual(new[] { "lemonade", "iced_tea", "soda" }, Enumerable.Range(0, 3).Select(DrinkRules.DrinkOf));
        }

        [Test]
        public void TheByte_HasAPouringBitPerTap_AndTheSpill()
        {
            byte flags = 0;
            flags = DrinkRules.WithPouring(flags, 2, true);
            Assert.IsTrue(DrinkRules.IsPouring(flags, 2));
            Assert.IsFalse(DrinkRules.IsPouring(flags, 0));
            flags = DrinkRules.WithSpill(flags, true);
            Assert.IsTrue(DrinkRules.HasSpill(flags));
            Assert.IsTrue(DrinkRules.IsPouring(flags, 2), "independent bits");
            flags = DrinkRules.WithPouring(flags, 2, false);
            Assert.AreEqual(DrinkRules.SpillBit, flags);
            Assert.AreEqual(flags, DrinkRules.WithPouring(flags, 7, true), "no such tap");
        }

        [Test]
        public void TheCupsLook_CarriesItsDrinkAndLevel_NeverTheMillilitres()
        {
            KitchenState kitchen = WithCup(onTap: IcedTea);
            FoodLook empty = FoodLookRules.From(CupOf(kitchen));
            Assert.AreEqual(FoodLook.KindCup, empty.Kind);
            Assert.AreEqual(0, empty.Ingredient);
            Assert.AreEqual(DrinkRules.LevelEmpty, empty.Shape);
            var desk = new DrinkDesk(kitchen);
            desk.Press(Dispenser, IcedTea, Owner, Start, null);
            desk.Release(Dispenser, IcedTea, Owner, Real(1.8), Rate, null, out _);
            FoodLook filled = FoodLookRules.From(CupOf(kitchen));
            Assert.AreEqual(new FoodLook(2, DrinkRules.LevelRight, 0, 0, FoodLook.KindCup, 0), filled);
            desk.Lid(Cup, out _);
            FoodLook lidded = FoodLookRules.From(CupOf(kitchen));
            Assert.AreEqual(FoodLook.KindLidded, lidded.Kind);
            Assert.IsTrue(FoodLookRules.IsFinished(lidded));
            Assert.IsTrue(FoodLookRules.IsCup(lidded));
            Assert.IsEmpty(FoodLookRules.PartsOf(CupOf(kitchen)));
            Assert.AreEqual(IcedTea, DrinkRules.TapOfDrinkByte(lidded.Ingredient));
            // A level for exactly what the pouring player predicts, and for the bucket everyone else sees.
            Assert.AreEqual(DrinkRules.LevelRight, DrinkRules.LevelOfMl(360f));
            Assert.AreEqual(DrinkRules.LevelEmpty, DrinkRules.LevelOfMl(0f));
            Assert.AreEqual(0.9f, DrinkRules.DrawnFill(360f), 1e-4f);
            Assert.AreEqual(0.9f, DrinkRules.DrawnFill(DrinkRules.LevelRight), 1e-4f, "the line is where a right cup is drawn");
            Assert.AreEqual(1f, DrinkRules.DrawnFill(500f));
            Assert.AreEqual(380f, DrinkRules.PredictedMl(0f, 1.9), 1e-3f);
            Assert.AreEqual(Drinks.MaxMl, DrinkRules.PredictedMl(300f, 5.0), 1e-3f, "the prediction stops where the server does");
        }

        // ---- the client's rules: actions, prompts, reasons ----

        private static AimTarget Tool(int tool, byte flags = 0, byte spot = DrinkRules.NoSpotCup) =>
            AimTarget.DrinkTool(tool, flags, DrinkRules.IsTap(tool) ? DrinkRules.DrinkName(tool) : null, -1, spot);

        /// <summary>Spec 042: the cup on a spot as clients see it.</summary>
        private static byte OnSpot(int tap, byte level, bool lidded = false) => DrinkRules.SpotByte(true, lidded, tap, level);

        private static readonly byte EmptyOnSpot = OnSpot(-1, DrinkRules.LevelEmpty);

        private static Held EmptyCup => Held.Cup("empty cup", -1, DrinkRules.LevelEmpty, false);

        [Test]
        public void ATap_FillsTheCupOnItsSpot_WithAnyHands_AndSaysWhyNotOtherwise()
        {
            foreach (Held hands in new[] { Held.Nothing, EmptyCup, Held.Food("lettuce", false), Held.Knife })
            {
                Assert.AreEqual(InteractionAction.Fill, InteractionRules.ActionFor(hands, Tool(Lemonade, 0, EmptyOnSpot)), "free hands or not");
                Assert.AreEqual("Hold left click · fill lemonade", InteractionRules.PromptFor(hands, Tool(Lemonade, 0, EmptyOnSpot)));
            }
            byte low = OnSpot(Lemonade, DrinkRules.LevelLow);
            Assert.AreEqual(InteractionAction.Fill, InteractionRules.ActionFor(Held.Nothing, Tool(Lemonade, 0, low)), "a top-up of the same drink");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(IcedTea, 0, low)));
            Assert.AreEqual("That cup has another drink", InteractionRules.PromptFor(Held.Nothing, Tool(IcedTea, 0, low)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(EmptyCup, Tool(Lemonade)), "spec 042: never into the hands");
            Assert.AreEqual("Put a cup under the tap first", InteractionRules.PromptFor(Held.Nothing, Tool(Lemonade)));
            Assert.AreEqual("Put a cup under the tap first", InteractionRules.PromptFor(Held.Food("lettuce", false), Tool(Lemonade)));
            Assert.AreEqual("Put the cup on the tray under the tap", InteractionRules.PromptFor(EmptyCup, Tool(Lemonade)));
            Assert.AreEqual("It has its lid", InteractionRules.PromptFor(Held.Nothing, Tool(Lemonade, 0, OnSpot(Lemonade, DrinkRules.LevelRight, true))));
            Assert.AreEqual("The cup is full", InteractionRules.PromptFor(Held.Nothing, Tool(Lemonade, 0, OnSpot(Lemonade, DrinkRules.LevelSpilled))));
            byte busy = DrinkRules.WithPouring(0, Lemonade, true);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Tool(Lemonade, busy, EmptyOnSpot)));
            Assert.AreEqual("Someone is using that tap", InteractionRules.PromptFor(Held.Nothing, Tool(Lemonade, busy, EmptyOnSpot)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Pending(EmptyCup), Tool(Lemonade, 0, EmptyOnSpot)), "a pick-up in flight");
        }

        [Test]
        public void ACupSpot_TakesAFittingCup_WithTheLeftClick_AndSaysWhyNotOtherwise()
        {
            AimTarget free = AimTarget.CupSpot(0, Lemonade);
            Assert.IsTrue(free.IsCupSpot);
            Assert.IsFalse(AimTarget.Spot(0).IsCupSpot);
            Assert.AreEqual(InteractionAction.Place, InteractionRules.ActionFor(EmptyCup, free));
            Assert.AreEqual("Left click · put empty cup under the lemonade tap", InteractionRules.PromptFor(EmptyCup, free));
            Held open = Held.Cup("half-full cup of lemonade", Lemonade, DrinkRules.LevelLow, false);
            Assert.AreEqual(InteractionAction.Place, InteractionRules.ActionFor(open, free), "back under its own tap");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(open, AimTarget.CupSpot(0, IcedTea)));
            Assert.AreEqual("That cup has another drink", InteractionRules.PromptFor(open, AimTarget.CupSpot(0, IcedTea)));
            Held lidded = Held.Cup("lemonade to go", Lemonade, DrinkRules.LevelRight, true);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(lidded, free));
            Assert.AreEqual("It has its lid", InteractionRules.PromptFor(lidded, free));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Knife, free), "only a cup goes there");
            Assert.AreEqual("Only a cup goes under the tap", InteractionRules.PromptFor(Held.Knife, free));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(EmptyCup, AimTarget.CupSpot(PlaceSpots.None, Lemonade)));
            Assert.AreEqual("A cup is already there", InteractionRules.PromptFor(EmptyCup, AimTarget.CupSpot(PlaceSpots.None, Lemonade)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, free), "empty hands put nothing down");
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, free));
            // The cup on the tray: empty hands take it, unless another player pours into it.
            Assert.AreEqual(InteractionAction.PickUp, InteractionRules.ActionFor(Held.Nothing, AimTarget.Item("cup of lemonade")));
            AimTarget busy = AimTarget.BusyCup("empty cup");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, busy));
            Assert.AreEqual(InteractionRules.BusyCupText, InteractionRules.PromptFor(Held.Nothing, busy));
            Assert.IsNull(InteractionRules.PromptFor(EmptyCup, busy), "full hands take nothing anyway");
        }

        [Test]
        public void TheStacks_GiveACup_AndALidOnAFilledCup()
        {
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Tool(DrinkRules.CupTool)));
            Assert.AreEqual("Left click · take a cup", InteractionRules.PromptFor(Held.Nothing, Tool(DrinkRules.CupTool)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(EmptyCup, Tool(DrinkRules.CupTool)), "full hands");
            Held filled = Held.Cup("cup of soda", 2, DrinkRules.LevelRight, false);
            Assert.AreEqual(InteractionAction.Lid, InteractionRules.ActionFor(filled, Tool(DrinkRules.LidTool)));
            Assert.AreEqual("Left click · put a lid on it", InteractionRules.PromptFor(filled, Tool(DrinkRules.LidTool)));
            Assert.AreEqual("Fill the cup first", InteractionRules.PromptFor(EmptyCup, Tool(DrinkRules.LidTool)));
            Assert.AreEqual("It has its lid", InteractionRules.PromptFor(Held.Cup("x", 2, DrinkRules.LevelRight, true), Tool(DrinkRules.LidTool)));
            // Spec 042: with no cup in the hands, the filled cup on a spot.
            byte onTray = OnSpot(2, DrinkRules.LevelRight);
            foreach (Held hands in new[] { Held.Nothing, Held.Knife })
                Assert.AreEqual(InteractionAction.Lid, InteractionRules.ActionFor(hands, Tool(DrinkRules.LidTool, 0, onTray)));
            Assert.AreEqual("Fill the cup first", InteractionRules.PromptFor(Held.Nothing, Tool(DrinkRules.LidTool, 0, EmptyOnSpot)));
            Assert.AreEqual("Take a cup first", InteractionRules.PromptFor(Held.Nothing, Tool(DrinkRules.LidTool)));
            Assert.AreEqual("Fill the cup first", InteractionRules.PromptFor(EmptyCup, Tool(DrinkRules.LidTool, 0, onTray)), "the cup in the hands first");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.DrinkTool(-1, 0, null, -1)), "the body");
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, AimTarget.DrinkTool(-1, 0, null, -1)));
        }

        [Test]
        public void ACup_IsSoldOnlyWithItsLid_AndGoesIntoNoCookware()
        {
            AimTarget counter = AimTarget.OnStation(StationKind.Counter);
            Held open = Held.Cup("cup of lemonade", Lemonade, DrinkRules.LevelRight, false);
            Held lidded = Held.Cup("cup of lemonade to go", Lemonade, DrinkRules.LevelRight, true);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(open, counter));
            Assert.AreEqual("Put a lid on it first", InteractionRules.PromptFor(open, counter));
            Assert.AreEqual(InteractionAction.Sell, InteractionRules.ActionFor(lidded, counter));
            foreach (VesselKind kind in new[] { VesselKind.Plate, VesselKind.Pan, VesselKind.Pot, VesselKind.Board, VesselKind.Bowl, VesselKind.Assembly })
            {
                Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(open, AimTarget.Cookware(kind, 0, null)), kind.ToString());
                Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(lidded, AimTarget.Cookware(kind, 0, null)), kind.ToString());
            }
            Assert.AreEqual(InteractionAction.Place, InteractionRules.ActionFor(open, AimTarget.Spot(0)), "it can be put down");
        }

        // ---- drinks as a line of a guest's order ----

        private static GuestPick FoodLine(int guest, OrderMatch match = OrderMatch.Exact, int misses = 0, int stars = 4) =>
            new GuestPick(guest, Menu.FalafelWrap, new OrderFit(match, misses, 0.7f, stars), 1000, 300, 1200);

        private static GuestPick DrinkLine(int guest, OrderMatch match = OrderMatch.Exact, int misses = 0, int stars = 5) =>
            new GuestPick(guest, Menu.FalafelWrap, new OrderFit(match, misses, 0.9f, stars), OrderRules.GuestSharePermille(match), 450, 1200, true);

        [Test]
        public void AnOrderWithADrink_CompletesOnlyWithBothLines_InAnyOrder_ThenOnePickForTheRegister()
        {
            var tab = new GuestTab();
            tab.Open(1, "lemonade");
            Assert.IsTrue(tab.WantsFood(1));
            Assert.IsTrue(tab.WantsDrink(1));
            Assert.IsFalse(tab.Serve(FoodLine(1), Owner, 800, out _, out _), "the drink is still open");
            Assert.IsFalse(tab.WantsFood(1), "it takes no second dish");
            Assert.IsTrue(tab.WantsDrink(1));
            Assert.IsTrue(tab.IsBilled(1, false));
            Assert.IsFalse(tab.IsBilled(1, true));
            Assert.IsTrue(tab.Serve(DrinkLine(1, OrderMatch.Close, 1, 3), Owner + 1, 120, out GuestPick whole, out long bill));
            Assert.AreEqual(920, bill, "the two lines' bills");
            Assert.AreEqual(1, whole.CustomerId);
            Assert.AreEqual(Menu.FalafelWrap, whole.Dish);
            Assert.AreEqual(OrderMatch.Close, whole.Fit.Match, "the worse of the two");
            Assert.AreEqual(1, whole.Fit.Misses);
            Assert.AreEqual(4, whole.Fit.Stars, "the mean of 4 and 3, rounded half up");
            Assert.AreEqual(OrderRules.ExactPermille, whole.BillPermille, "the bill is already the lines' shares");
            Assert.AreEqual(450, whole.WaitedTenths, "waited until the last line");
            Assert.IsFalse(whole.IsDrink);
            Assert.AreEqual(0, tab.Count, "the tab is closed");

            // The drink first, then the dish.
            tab.Open(2, "soda");
            Assert.IsFalse(tab.Serve(DrinkLine(2), Owner, 100, out _, out _));
            Assert.IsTrue(tab.WantsFood(2));
            Assert.IsTrue(tab.Serve(FoodLine(2), Owner, 900, out whole, out bill));
            Assert.AreEqual(1000, bill);
            Assert.AreEqual(OrderMatch.Exact, whole.Fit.Match);

            // No drink line: complete at once, the line as it is (the 032 flow).
            tab.Open(3, null);
            Assert.IsFalse(tab.WantsDrink(3));
            Assert.IsTrue(tab.Serve(FoodLine(3), Owner, 700, out whole, out bill));
            Assert.AreEqual(700, bill);
            Assert.AreEqual(FoodLine(3).Fit.Stars, whole.Fit.Stars);
        }

        [Test]
        public void AGuestLeavingWithALineOpen_PaysWhatItGot_Once()
        {
            var tab = new GuestTab();
            tab.Open(1, "iced_tea");
            Assert.IsFalse(tab.TakeOpenBill(1, out _, out _, out _, out _), "nothing got yet: nothing to pay");
            tab.Open(1, "iced_tea");
            tab.Serve(FoodLine(1), Owner, 640, out _, out _);
            Assert.IsTrue(tab.TakeOpenBill(1, out long bill, out int payee, out int stars, out int misses));
            Assert.AreEqual((640L, Owner, 4, 1), (bill, payee, stars, misses), "its dish's bill, to its cook; the missing drink is a step short");
            Assert.IsFalse(tab.TakeOpenBill(1, out _, out _, out _, out _), "once");
            Assert.IsTrue(tab.WantsFood(1), "no tab any more");
        }

        [Test]
        public void TheDrinkMatch_IsTheOrderedDrinkAtTheLine_WithItsLid()
        {
            KitchenState kitchen = WithCup();
            var desk = new DrinkDesk(kitchen);
            desk.Press(Dispenser, Lemonade, Owner, Start, null);
            desk.Release(Dispenser, Lemonade, Owner, Real(1.8), Rate, null, out _);
            Item open = CupOf(kitchen);
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.FitDrink(open, "lemonade").Match, "no lid");
            desk.Lid(Cup, out Item right);
            OrderFit fit = OrderRules.FitDrink(right, "lemonade");
            Assert.AreEqual(OrderMatch.Exact, fit.Match);
            Assert.AreEqual(0, fit.Misses);
            Assert.AreEqual(StarRating.Stars(ItemRater.Rate(right).Quality), fit.Stars);
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.FitDrink(right, "soda").Match, "another drink");
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.FitDrink(null, "soda").Match);

            KitchenState low = WithCup();
            var desk2 = new DrinkDesk(low);
            desk2.Press(Dispenser, Lemonade, Owner, Start, null);
            desk2.Release(Dispenser, Lemonade, Owner, Real(1.0), Rate, null, out _);
            desk2.Lid(Cup, out Item lowCup);
            Assert.AreEqual((OrderMatch.Close, 1), (OrderRules.FitDrink(lowCup, "lemonade").Match, OrderRules.FitDrink(lowCup, "lemonade").Misses));
        }

        [Test]
        public void TheQueue_GivesALineOnlyToGuestsWhoStillWantIt()
        {
            var queue = new CustomerQueue();
            queue.Add(1, "meal", Menu.FalafelWrap, 0, DrinkRules.DrinkByte("lemonade"));
            queue.Add(2, "meal", Menu.SeitanWrap, 0, DrinkRules.DrinkByte("soda"));
            queue.Add(3, "meal", Menu.SeitanWrap, 0, 0);
            Assert.AreEqual(DrinkRules.DrinkByte("lemonade"), queue.Entries[0].Drink);
            // Guest 1 has its dish: a dish goes to the first one still waiting for one.
            Assert.IsTrue(queue.TryPick(e => e.Id != 1, e => e.Dish == Menu.FalafelWrap ? OrderMatch.Exact : OrderMatch.Wrong,
                out QueuedCustomer c, out OrderMatch m));
            Assert.AreEqual((2, OrderMatch.Wrong), (c.Id, m), "the first eligible, nobody eligible matches exactly");
            Assert.IsTrue(queue.TryPick(e => e.Drink != 0, e => e.Drink == DrinkRules.DrinkByte("soda") ? OrderMatch.Exact : OrderMatch.Close,
                out c, out m));
            Assert.AreEqual((2, OrderMatch.Exact), (c.Id, m), "the exact one among the thirsty");
            Assert.IsFalse(queue.TryPick(e => false, e => OrderMatch.Exact, out _, out _), "nobody wants it");
        }

        [Test]
        public void AGuestWaitingForItsDrink_KeepsItsPlace_NobodyElseIsSentAwayForItsDish()
        {
            // Three meal guests; guest 1 got its wrap (the meal demand fell to two) and waits for its drink.
            var queue = new CustomerQueue();
            queue.Add(1, "meal", Menu.FalafelWrap, 0, DrinkRules.DrinkByte("lemonade"));
            queue.Add(2, "meal", Menu.SeitanWrap, 0, 0);
            queue.Add(3, "meal", Menu.SeitanWrap, 0, 0);
            IReadOnlyList<string> demand = new[] { "meal", "meal" };
            IReadOnlyList<string> desired = CustomerRules.KeepOpenOrders(demand, queue.Entries, id => id == 1);
            CollectionAssert.AreEqual(new[] { "meal", "meal", "meal" }, desired);
            var leaving = new List<QueuedCustomer>();
            var joining = new List<string>();
            queue.Reconcile(desired, leaving, joining);
            Assert.IsEmpty(leaving, "nobody leaves for the dish guest 1 got");
            Assert.IsEmpty(joining);
            Assert.AreSame(demand, CustomerRules.KeepOpenOrders(demand, queue.Entries, id => false), "nobody waits: unchanged");
        }

        [Test]
        public void TheTicket_CarriesTheOpenDrinkLine_OldTicketsUnchanged()
        {
            byte lemonade = DrinkRules.DrinkByte("lemonade");
            int ticket = CustomerRules.PackTicket(Menu.FalafelWrap, 12, 0x21, lemonade, true);
            Assert.AreEqual(Menu.FalafelWrap, CustomerRules.TicketDish(ticket));
            Assert.AreEqual(12, CustomerRules.TicketMinutes(ticket));
            Assert.AreEqual(0x21, CustomerRules.TicketModifiers(ticket));
            Assert.AreEqual(lemonade, CustomerRules.TicketDrink(ticket));
            Assert.IsTrue(CustomerRules.TicketDishServed(ticket));
            Assert.AreEqual(CustomerRules.PackTicket(Menu.Fries, 3, 0), CustomerRules.PackTicket(Menu.Fries, 3, 0, 0, false), "032 tickets unchanged");
            Assert.AreEqual(CustomerRules.MaxTicketMinutes, CustomerRules.TicketMinutes(CustomerRules.PackTicket(Menu.Fries, 1 << 20, 0, 3, true)),
                "the minutes never run into the drink bits");
            Assert.AreEqual(3, CustomerRules.TicketDrink(CustomerRules.PackTicket(Menu.Fries, 1 << 20, 0, 3, true)));
            Assert.AreEqual("+ lemonade", CustomerRules.DrinkLine(lemonade));
            Assert.AreEqual(string.Empty, CustomerRules.DrinkLine(0));
            Assert.AreEqual("Falafel wrap\n+ iced tea", CustomerRules.OrderText(Menu.FalafelWrap, 0, DrinkRules.DrinkByte("iced_tea")));
            Assert.AreEqual(CustomerRules.OrderText(Menu.FalafelWrap, 0), CustomerRules.OrderText(Menu.FalafelWrap, 0, 0));
            Assert.AreEqual("Thanks! And my lemonade?", CustomerRules.StillWaitingText(true, Menu.FalafelWrap, lemonade));
            Assert.AreEqual("Thanks! And my falafel wrap?", CustomerRules.StillWaitingText(false, Menu.FalafelWrap, lemonade));
        }

        [Test]
        public void AWholeOrder_IsPaidOnceAtTheRegister_AndTheCountersFallbackNeverPaysALineOnTheTab()
        {
            var shift = new Shift();
            shift.OnClock(8, 0f, 0);
            var flow = new RegisterFlow(shift);
            var tab = new GuestTab();
            tab.Open(7, "lemonade");
            Assert.IsFalse(tab.Serve(FoodLine(7), Owner, 800, out _, out _));
            // The counter's fallback after the first line: the line is on the tab, so it never pays it at the counter.
            Assert.IsFalse(RegisterFlow.FallbackPays(false, flow.Desk.IsWaiting(7) || tab.IsBilled(7, false)));
            Assert.IsTrue(tab.Serve(DrinkLine(7), Owner, 150, out GuestPick whole, out long bill));
            RegisterPayment p = flow.HandOver(whole, Owner, bill, new Worktown.Sim.Economy.GuestOrder(new OrderModifier[0], 60, true, 10), null, 0f);
            Assert.AreEqual(950, p.Bill, "one bill for the whole order");
            var wallet = new PayCounter();
            Assert.AreEqual(CookOutcome.Ok, flow.Use(Owner + 1, RegisterRules.CardOption, wallet, null, 1f), "any cook takes it");
            Assert.AreEqual(1, wallet.Calls, "paid exactly once");
            Assert.AreEqual(950 + p.Tip, wallet.Paid);
            Assert.AreEqual(CookOutcome.NoPayment, flow.Use(Owner, RegisterRules.CardOption, wallet, null, 2f));
            Assert.AreEqual(1, wallet.Calls);
        }

        private sealed class PayCounter : IRegisterWallet
        {
            public int Calls;
            public long Paid;

            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                Calls++;
                Paid += amount;
                paid = amount;
                return CookOutcome.Ok;
            }
        }
    }
}
