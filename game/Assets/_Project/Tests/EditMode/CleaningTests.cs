using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 035 AC3: the server's cleaning, pure (CleaningRoom, DishRoom, CleaningRules): dishes come back dirty from eat-in guests and
    /// the plates stay finite; the sink's scrub is server-timed (the 031b re-press rule, the auto-stop, a player who left); discards
    /// fill the trash (never money or stock), the bag is taken out; the wipe clears a spill; the hygiene score drives the review and the
    /// patience; the rules, prompts and the synced surface. With the 034 review fixes (delivery spots, lost boxes, a server stop, stock).
    /// </summary>
    public class CleaningTests
    {
        private const double Rate = 30d; // ticks per second
        private const int Cook = 3, Other = 4;

        private static uint Ticks(double seconds) => (uint)Math.Round(seconds * Rate);

        // ---- plates: finite, eat-in dishes come back dirty ----

        private static int Total(DishRoom d, int inUse) => d.Clean + inUse + d.Eating + d.AtReturn + d.Carried + d.InBasin;

        [Test]
        public void ThePlates_AreFinite_AndNeverCreatedOrDestroyed()
        {
            var d = new DishRoom();
            Assert.AreEqual(CleaningRules.TotalPlates - CleaningRules.ScenePlates, d.Clean, "the shelf starts with the plates not on the worktop");
            int inUse = CleaningRules.ScenePlates;
            Assert.AreEqual(CleaningRules.TotalPlates, Total(d, inUse));
            while (d.TakeClean() == CookOutcome.Ok) inUse++;
            Assert.AreEqual(CookOutcome.NoCleanPlates, d.TakeClean(), "without washing, plates run out");
            Assert.AreEqual(0, d.Clean);
            Assert.AreEqual(CleaningRules.TotalPlates, Total(d, inUse));
            // Served: two eat in, one takes away (clean again at once), one is lost (it turns up on the shelf).
            d.PlateServed(true, 100); inUse--;
            d.PlateServed(true, 101); inUse--;
            d.PlateServed(false, 101); inUse--;
            d.PlateLost(); inUse--;
            Assert.AreEqual(2, d.Clean);
            Assert.AreEqual(2, d.Eating);
            Assert.AreEqual(CleaningRules.TotalPlates, Total(d, inUse));
            Assert.AreEqual(0, d.ReturnDue(100 + CleaningRules.EatMinutes - 1), "still eating");
            Assert.AreEqual(1, d.ReturnDue(100 + CleaningRules.EatMinutes), "the first plate comes back dirty after the meal");
            Assert.AreEqual(1, d.ReturnDue(200));
            Assert.AreEqual(2, d.AtReturn);
            Assert.AreEqual(2, d.Dirty);
            Assert.AreEqual(CleaningRules.TotalPlates, Total(d, inUse));
            // The hand-over backstop: a take undone changes nothing.
            Assert.AreEqual(CookOutcome.Ok, d.TakeClean());
            d.UndoTakeClean();
            Assert.AreEqual(2, d.Clean);
        }

        [Test]
        public void ADirtyDish_GoesFromTheReturnIntoTheSink_AndScrubbedOntoTheShelf()
        {
            var d = new DishRoom(0);
            Assert.AreEqual(CookOutcome.NoDirtyDishes, d.TakeDirty(out _));
            for (int i = 0; i < 4; i++) d.PlateServed(true, i);
            d.ReturnDue(100);
            Assert.AreEqual(CookOutcome.Ok, d.TakeDirty(out long since));
            Assert.AreEqual(0 + CleaningRules.EatMinutes, since, "the oldest first, dirty since its meal ended");
            d.ConfirmCarried(51, since);
            Assert.IsTrue(d.IsCarried(51));
            Assert.AreEqual(CookOutcome.NotDirty, d.PutInSink(99), "only a dirty dish we know");
            Assert.AreEqual(CookOutcome.Ok, d.PutInSink(51));
            Assert.AreEqual(1, d.InBasin);
            for (int id = 52; id <= 54; id++)
            {
                if (d.TakeDirty(out long s) != CookOutcome.Ok) break;
                d.ConfirmCarried(id, s);
            }
            Assert.AreEqual(CookOutcome.Ok, d.PutInSink(52));
            Assert.AreEqual(CookOutcome.Ok, d.PutInSink(53));
            Assert.AreEqual(CookOutcome.SinkFull, d.PutInSink(54), "the basin holds " + CleaningRules.BasinCapacity);
            Assert.IsTrue(d.IsCarried(54), "a refused put changes nothing");
            Assert.AreEqual(CookOutcome.Ok, d.Scrubbed());
            Assert.AreEqual(1, d.Clean, "washed: back on the clean shelf");
            Assert.AreEqual(2, d.InBasin);
            // A carried dish that is lost waits at the return again, as dirty as before.
            Assert.IsTrue(d.CarriedLost(54));
            Assert.AreEqual(1, d.AtReturn);
            Assert.AreEqual(CookOutcome.Ok, d.TakeDirty(out long again));
            Assert.AreEqual(3 + CleaningRules.EatMinutes, again);
            d.UndoTakeDirty(again);
            Assert.AreEqual(1, d.AtReturn);
            Assert.AreEqual(CookOutcome.Ok, d.Scrubbed());
            Assert.AreEqual(CookOutcome.Ok, d.Scrubbed());
            Assert.AreEqual(CookOutcome.SinkEmpty, d.Scrubbed());
            Assert.AreEqual(3, d.Clean);
        }

        // ---- the timed holds: the scrub and the wipe ----

        [Test]
        public void TheScrub_IsServerTimed_ARePressKeepsItsStart_AndAShortOneDoesNothing()
        {
            var room = new CleaningRoom();
            const int sink = 13;
            uint t0 = 1000;
            Assert.AreEqual(CookOutcome.Ok, room.Press(sink, CleaningRules.HoldScrub, Cook, t0));
            // 031b 10.0: the same player's press again (a lost release, a repeat) keeps the first start.
            Assert.AreEqual(CookOutcome.Ok, room.Press(sink, CleaningRules.HoldScrub, Cook, t0 + Ticks(1.4)));
            Assert.IsTrue(room.IsHeld(sink, CleaningRules.HoldScrub, out int owner, out uint start));
            Assert.AreEqual(Cook, owner);
            Assert.AreEqual(t0, start);
            Assert.AreEqual(CookOutcome.DispenserBusy, room.Press(sink, CleaningRules.HoldScrub, Other, t0 + 1), "another cook waits");
            Assert.AreEqual(CookOutcome.Ok, room.Release(sink, CleaningRules.HoldScrub, Other, t0 + 2, Rate, out bool othersDone), "a release without a hold: harmless");
            Assert.IsFalse(othersDone);
            Assert.AreEqual(CookOutcome.Ok, room.Release(sink, CleaningRules.HoldScrub, Cook, t0 + Ticks(CleaningRules.ScrubSeconds), Rate, out bool done));
            Assert.IsTrue(done, "1.5 s from the first press: scrubbed");
            Assert.IsFalse(room.IsHeld(sink, CleaningRules.HoldScrub));

            room.Press(sink, CleaningRules.HoldScrub, Cook, 5000);
            room.Release(sink, CleaningRules.HoldScrub, Cook, 5000 + Ticks(1.2), Rate, out bool tooShort);
            Assert.IsFalse(tooShort, "let go after 1.2 s: nothing is washed");
        }

        [Test]
        public void TheServer_StopsAHoldAtItsTime_AndFreesAPlayerWhoLeft()
        {
            var room = new CleaningRoom();
            const int sink = 13, dispenser = 10;
            room.Press(sink, CleaningRules.HoldScrub, Cook, 100);
            Assert.AreEqual(CookOutcome.StillHolding, room.Press(dispenser, CleaningRules.HoldWipe, Cook, 100), "one hold per player (035 review: its own reason)");
            room.Press(dispenser, CleaningRules.HoldWipe, Other, 100);
            var due = new List<long>();
            room.DueHolds(100 + Ticks(0.9), Rate, due);
            Assert.IsEmpty(due);
            room.DueHolds(100 + Ticks(CleaningRules.WipeSeconds), Rate, due);
            CollectionAssert.AreEqual(new[] { CleaningRoom.KeyOf(dispenser, CleaningRules.HoldWipe) }, due, "the wipe's 1 s first");
            Assert.AreEqual(Other, room.OwnerOf(due[0]));
            Assert.IsTrue(room.Complete(due[0]));
            Assert.IsFalse(room.Complete(due[0]), "once");
            room.DueHolds(100 + Ticks(CleaningRules.ScrubSeconds), Rate, due);
            CollectionAssert.AreEqual(new[] { CleaningRoom.KeyOf(sink, CleaningRules.HoldScrub) }, due, "the auto-stop at 1.5 s");
            // The scrubbing cook leaves before that: its hold is dropped without a result.
            var dropped = new List<long>();
            room.DropLeft(id => id != Cook, dropped);
            CollectionAssert.AreEqual(new[] { CleaningRoom.KeyOf(sink, CleaningRules.HoldScrub) }, dropped);
            Assert.IsFalse(room.IsHeld(sink, CleaningRules.HoldScrub));
            Assert.AreEqual(CookOutcome.Ok, room.Press(sink, CleaningRules.HoldScrub, Other, 200), "the sink is free again");
        }

        [Test]
        public void TheWipe_ClearsTheSpill_TheKitchenStateHook()
        {
            KitchenState kitchen = KitchenState.CreateDemo();
            const int dispenser = 10;
            kitchen.MarkSpill(dispenser);
            Assert.IsTrue(DrinkRules.HasSpill(kitchen.DrinkFlags(dispenser)));
            var room = new CleaningRoom();
            Assert.AreEqual(CookOutcome.Ok, room.Press(dispenser, CleaningRules.HoldWipe, Cook, 0));
            room.Release(dispenser, CleaningRules.HoldWipe, Cook, Ticks(0.5), Rate, out bool early);
            Assert.IsFalse(early, "half a second is not a wipe");
            Assert.IsTrue(kitchen.HasSpill(dispenser));
            room.Press(dispenser, CleaningRules.HoldWipe, Cook, 100);
            room.Release(dispenser, CleaningRules.HoldWipe, Cook, 100 + Ticks(CleaningRules.WipeSeconds), Rate, out bool done);
            Assert.IsTrue(done);
            Assert.IsTrue(kitchen.ClearSpill(dispenser), "the service clears it on a finished wipe");
            Assert.IsFalse(DrinkRules.HasSpill(kitchen.DrinkFlags(dispenser)));
            Assert.IsFalse(kitchen.ClearSpill(dispenser));
        }

        // ---- the trash ----

        [Test]
        public void Discards_FillTheTrash_ToTwelve_ThenTheBagGoesOutToTheBin()
        {
            var room = new CleaningRoom();
            Assert.AreEqual(CookOutcome.CannotDiscard, room.CanDiscard(false), "a tool, a box, empty cookware");
            for (int i = 0; i < HygieneRules.TrashCapacity; i++)
            {
                Assert.AreEqual(CookOutcome.Ok, room.CanDiscard(true));
                Assert.AreEqual(CookOutcome.Ok, room.Discarded());
            }
            Assert.IsTrue(room.TrashIsFull);
            Assert.AreEqual(CookOutcome.TrashFull, room.CanDiscard(true));
            Assert.AreEqual(CookOutcome.TrashFull, room.Discarded());
            Assert.AreEqual(CookOutcome.HandsFull, room.TakeBag(true, out _));
            Assert.AreEqual(CookOutcome.Ok, room.TakeBag(false, out int size));
            Assert.AreEqual(HygieneRules.TrashCapacity, size);
            Assert.AreEqual(0, room.TrashFill);
            room.ConfirmBag(77, size);
            Assert.AreEqual(1, room.BagsOut);
            Assert.AreEqual(CookOutcome.TrashEmpty, room.TakeBag(false, out _));
            Assert.AreEqual(CookOutcome.NotABag, room.Binned(78));
            Assert.AreEqual(CookOutcome.Ok, room.Binned(77));
            Assert.AreEqual(0, room.BagsOut);
            Assert.AreEqual(CookOutcome.NotABag, room.Binned(77), "once");
            // A lost bag's trash is back in the can (never lost, never doubled).
            room.Discarded();
            room.TakeBag(false, out int one);
            room.ConfirmBag(80, one);
            room.Discarded();
            Assert.IsTrue(room.BagLost(80));
            Assert.AreEqual(2, room.TrashFill);
        }

        [Test]
        public void ADiscard_NeverRefundsOrCreatesMoneyOrStock()
        {
            // The trash only counts: the cleaning room holds no wallet and no stock (the anti-faucet rule), and a discard after a take
            // from the stock leaves the stock where the take left it.
            foreach (FieldInfo f in typeof(CleaningRoom).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(typeof(StockRoom).IsAssignableFrom(f.FieldType), f.Name);
                Assert.IsFalse(f.FieldType.Name.Contains("Wallet") || f.FieldType.Name.Contains("Economy"), f.Name);
            }
            foreach (MethodInfo m in typeof(ICleaningKitchen).GetMethods())
                foreach (ParameterInfo p in m.GetParameters())
                    Assert.IsFalse(p.ParameterType == typeof(long) || p.ParameterType.Name.Contains("Wallet"), m.Name + " moves no money");
            var stock = new StockRoom();
            int line = StockCatalog.IndexOf("tomato");
            int before = stock.LineOf(line).Count;
            Assert.AreEqual(CookOutcome.Ok, stock.Take(line));
            int version = stock.Version;
            var room = new CleaningRoom();
            Assert.AreEqual(CookOutcome.Ok, room.Discarded());
            Assert.AreEqual(before - 1, stock.LineOf(line).Count, "no refund");
            Assert.AreEqual(version, stock.Version, "the stock never changed");
        }

        // ---- hygiene in the room, and what guests make of it ----

        [Test]
        public void TheRoomsScore_FollowsItsDirt_DishesAfterTheGrace_TheTrash_Spills()
        {
            var room = new CleaningRoom(480);
            room.Dishes.Reset(0);
            room.Dishes.PlateServed(true, 480 - CleaningRules.EatMinutes);
            room.Dishes.ReturnDue(480); // dirty since 480
            Assert.IsFalse(room.Tick(481, 0, false), "a fresh dirty dish costs nothing, but nothing recovers either");
            Assert.AreEqual(100, room.Hygiene);
            Assert.IsFalse(room.Tick(482, 0, false));
            Assert.IsTrue(room.Tick(483, 0, false), "after two game minutes: -0.1 % a minute (036 tuning), so the shown 100 % drops to 99 %");
            Assert.AreEqual(99, room.Hygiene);
            Assert.AreEqual(999, room.Meter.Tenths);
            Assert.IsFalse(room.Tick(484, 1, false), "and a spill -0.2 %: 99.6 % still shows 99 %");
            Assert.AreEqual(996, room.Meter.Tenths);
            for (int i = 0; i < HygieneRules.TrashCapacity; i++) room.Discarded();
            HygieneSources s = room.Sources(485, 0, true);
            Assert.IsTrue(s.TrashFull);
            Assert.IsTrue(s.Burning);
            Assert.AreEqual(1, s.OverdueDishes);
            room.TakeBag(false, out int size);
            room.ConfirmBag(5, size);
            Assert.IsTrue(room.Sources(485, 0, false).TrashFull, "a full bag not taken out still counts");
            room.Binned(5);
            room.Dishes.TakeDirty(out long since);
            room.Dishes.ConfirmCarried(9, since);
            room.Dishes.PutInSink(9);
            room.Dishes.Scrubbed();
            Assert.IsTrue(room.Sources(486, 0, false).IsClean);
            room.Tick(486, 0, false);
            Assert.AreEqual(998, room.Meter.Tenths, "two minutes since the last tick, now all clean: +0.1 % a minute");
            Assert.AreEqual(99, room.Hygiene);
        }

        private sealed class PayAll : IRegisterWallet
        {
            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                paid = amount;
                return CookOutcome.Ok;
            }
        }

        private sealed class LogStage : IGuestStage
        {
            public readonly List<string> Lines = new List<string>();
            public void Paying(int guestId, string bubble, int lineIndex) { }
            public void Leave(int guestId, string bubble, byte mood) => Lines.Add("Leave " + bubble);
            public void LineMoved() { }
            public void Feed(string line) => Lines.Add("Feed " + line);
            public void Reward(int stars, long paid, long tip) => Lines.Add("Reward " + stars);
            public void Log(string line) => Lines.Add(line);
        }

        private static string ReviewAt(int hygiene)
        {
            var shift = new Shift();
            shift.OnClock(8, 0f, 0);
            var flow = new RegisterFlow(shift) { HygienePercent = hygiene };
            var stage = new LogStage();
            var pick = new GuestPick(1, Menu.FalafelWrap, new OrderFit(OrderMatch.Exact, 0, 1f, 5), 1000, 100, 1200);
            flow.HandOver(pick, Cook, 400, new GuestOrder(new OrderModifier[0], 60, true, 10), stage, 0f);
            Assert.AreEqual(CookOutcome.Ok, flow.Use(Cook, RegisterRules.CardOption, new PayAll(), stage, 1f));
            return stage.Lines.First(l => l.StartsWith("Reward"));
        }

        [Test]
        public void Hygiene_DrivesTheReviewAndThePatience_Through032sRules()
        {
            Assert.AreEqual("Reward 5", ReviewAt(100), "a clean shop: the order's five stars");
            Assert.AreEqual("Reward 5", ReviewAt(50));
            Assert.AreEqual("Reward 4", ReviewAt(49), "below 50 %: one star less");
            var order = new GuestOrder(new OrderModifier[0], 60, true, 10);
            float clean = CustomerRules.PatienceFor(order, 2, true, 1f, 100);
            Assert.AreEqual(GuestOrders.PatienceSeconds(order, 3), clean, 1e-3f, "032 patience: the base, the layers and the drink");
            Assert.AreEqual(clean, CustomerRules.PatienceFor(order, 2, true, 1f, 30), 1e-3f);
            Assert.AreEqual(clean * 0.8f, CustomerRules.PatienceFor(order, 2, true, 1f, 29), 1e-3f, "below 30 %: 20 % less");
            Assert.AreEqual(clean * 8f * 0.8f, CustomerRules.PatienceFor(order, 2, true, 8f, 0), 1e-2f, "the tour's scale too");
        }

        [Test]
        public void EatIn_IsDrawnFromItsOwnSeed_AndTheOrderBubbleSaysIt()
        {
            CollectionAssert.AllItemsAreUnique(new[] { CustomerRules.OrderSeed, CustomerRules.DrinkSeed, CustomerRules.EatInSeed });
            StringAssert.EndsWith("· for here", CustomerRules.OrderText(Menu.Fries, 0, 0, true));
            StringAssert.EndsWith("· to go", CustomerRules.OrderText(Menu.Fries, 0, 0, false));
            Assert.AreEqual(CustomerRules.OrderText(Menu.Fries, 0, 0), CustomerRules.OrderText(Menu.Fries, 0, 0, true).Split('\n')[0]);
            Assert.AreEqual(ArtKey.FoodPlate, HandoffRules.ParcelOf(Menu.Fries, true), "an eat-in guest takes the plate");
            Assert.AreEqual(HandoffRules.ParcelOf(Menu.Fries), HandoffRules.ParcelOf(Menu.Fries, false));
            Assert.AreEqual(ArtKey.FoodWrapPaper, HandoffRules.ParcelOf(Menu.FalafelWrap, true), "a wrap is held in its paper either way");
            Assert.IsFalse(HandoffRules.IsPlateDish(Menu.VeggieBox));
        }

        // ---- the client's rules (prompts, reasons) ----

        private static AimTarget Station(StationKind kind, byte b) => AimTarget.CleaningStation(kind, 0, b, true);

        [Test]
        public void TheRules_OfferTheCleaningActions_WithTheirPromptsAndReasons()
        {
            Held food = Held.Food("burnt potato", false);
            // The dish return.
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Station(StationKind.DishReturn, 2)));
            Assert.AreEqual("Click · take a dirty plate (2 waiting)", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.DishReturn, 2)));
            Assert.AreEqual("No dirty dishes here", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.DishReturn, 0)));
            // The sink.
            Assert.AreEqual(InteractionAction.Wash, InteractionRules.ActionFor(Held.DirtyDish, Station(StationKind.Sink, CleaningRules.SinkByte(0, false))));
            Assert.AreEqual("The sink is full: scrub a dish first",
                InteractionRules.PromptFor(Held.DirtyDish, Station(StationKind.Sink, CleaningRules.SinkByte(CleaningRules.BasinCapacity, false))));
            Assert.AreEqual(InteractionAction.Scrub, InteractionRules.ActionFor(Held.Nothing, Station(StationKind.Sink, CleaningRules.SinkByte(2, false))));
            Assert.AreEqual("Hold · scrub the plate (2 in the sink)", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.Sink, CleaningRules.SinkByte(2, false))));
            Assert.AreEqual("Someone is scrubbing", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.Sink, CleaningRules.SinkByte(1, true))));
            Assert.AreEqual("Bring a dirty dish from the dish return", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.Sink, 0)));
            Assert.AreEqual("Only dirty dishes go in the sink", InteractionRules.PromptFor(food, Station(StationKind.Sink, 0)));
            // The trash.
            Assert.AreEqual(InteractionAction.Discard, InteractionRules.ActionFor(food, Station(StationKind.TrashCan, 3)));
            Assert.AreEqual("Click · throw away burnt potato", InteractionRules.PromptFor(food, Station(StationKind.TrashCan, 3)));
            Assert.AreEqual("Click · throw away the food", InteractionRules.PromptFor(Held.Cookware(VesselKind.Plate, 2, "fries"), Station(StationKind.TrashCan, 3)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Cookware(VesselKind.Plate, 0, null), Station(StationKind.TrashCan, 3)),
                "empty cookware stays");
            Assert.AreEqual(InteractionAction.Discard, InteractionRules.ActionFor(Held.Cup("empty cup", -1, 0, false), Station(StationKind.TrashCan, 0)),
                "an empty cup is packaging");
            Assert.AreEqual("The trash is full: take the bag out", InteractionRules.PromptFor(food, Station(StationKind.TrashCan, 12)));
            Assert.AreEqual(InteractionRules.WashItText, InteractionRules.PromptFor(Held.DirtyDish, Station(StationKind.TrashCan, 3)));
            Assert.AreEqual("That doesn't go in the trash", InteractionRules.PromptFor(Held.Knife, Station(StationKind.TrashCan, 3)));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, Station(StationKind.TrashCan, 5)));
            Assert.AreEqual("Click · take the trash bag out (5/12)", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.TrashCan, 5)));
            Assert.AreEqual("The trash is empty", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.TrashCan, 0)));
            // The bin.
            Assert.AreEqual(InteractionAction.Discard, InteractionRules.ActionFor(Held.TrashBag, Station(StationKind.Bin, 0)));
            Assert.AreEqual("Click · throw the bag in", InteractionRules.PromptFor(Held.TrashBag, Station(StationKind.Bin, 0)));
            Assert.AreEqual(InteractionRules.ToTheBinText, InteractionRules.PromptFor(Held.TrashBag, Station(StationKind.TrashCan, 0)));
            Assert.AreEqual("Only the trash bag goes in here", InteractionRules.PromptFor(food, Station(StationKind.Bin, 0)));
            // The plate shelf counts its clean plates.
            Assert.AreEqual("Click · take a plate (3 clean)", InteractionRules.PromptFor(Held.Nothing, Station(StationKind.PlateShelf, 3)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Station(StationKind.PlateShelf, 0)));
            Assert.AreEqual(InteractionRules.NoCleanPlatesText, InteractionRules.PromptFor(Held.Nothing, Station(StationKind.PlateShelf, 0)));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, AimTarget.OnStation(StationKind.PlateShelf)),
                "without the byte (no cleaning service) the shelf gives plates as before");
            // A dirty dish goes nowhere else: not onto food, not to the counter.
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.DirtyDish, AimTarget.Cookware(VesselKind.Pan, 1, "potato")));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.DirtyDish, AimTarget.OnStation(StationKind.Counter)));
            Assert.AreEqual(InteractionAction.Place, InteractionRules.ActionFor(Held.DirtyDish, AimTarget.Spot(2)));
        }

        [Test]
        public void TheSpill_IsWipedWithTheCloth_AHold()
        {
            byte spill = DrinkRules.WithSpill(0, true);
            AimTarget puddle = AimTarget.DrinkTool(CleaningRules.SpillTool, spill, null, -1);
            Assert.AreEqual(InteractionAction.Wipe, InteractionRules.ActionFor(Held.Cloth, puddle));
            Assert.AreEqual("Hold · wipe the spill", InteractionRules.PromptFor(Held.Cloth, puddle));
            Assert.AreEqual("Take the cloth to wipe the spill", InteractionRules.PromptFor(Held.Nothing, puddle));
            Assert.AreEqual("Someone is wiping it",
                InteractionRules.PromptFor(Held.Cloth, AimTarget.DrinkTool(CleaningRules.SpillTool, (byte)(spill | CleaningRules.WipingBit), null, -1)));
            Assert.IsNull(InteractionRules.PromptFor(Held.Cloth, AimTarget.DrinkTool(CleaningRules.SpillTool, 0, null, -1)), "no spill: nothing to say");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Cloth, AimTarget.DrinkTool(0, 0, "lemonade", -1)), "the cloth pours nothing");
        }

        [Test]
        public void TheOptionsAndBytes_ArePinned_AndTheReleasesNeedNoReach()
        {
            Assert.AreEqual(9, CleaningRules.WipeStartOption, "after the dispenser's taps, cups and lids");
            Assert.AreEqual(10, CleaningRules.WipeStopOption);
            Assert.AreEqual(DrinkRules.ToolCount, CleaningRules.SpillTool);
            Assert.AreEqual(1 << 4, CleaningRules.WipingBit, "above the three pouring bits and the spill bit");
            Assert.AreEqual(0x0B, CleaningRules.SinkByte(3, true));
            Assert.AreEqual(3, CleaningRules.BasinOf(0x0B));
            Assert.IsTrue(CleaningRules.IsScrubbing(0x0B));
            Assert.AreEqual(7, CleaningRules.BasinOf(CleaningRules.SinkByte(20, false)), "clamped to three bits");
            Assert.IsFalse(CleaningRules.NeedsReach(StationKind.Sink, CleaningRules.ScrubStopOption));
            Assert.IsTrue(CleaningRules.NeedsReach(StationKind.Sink, CleaningRules.ScrubStartOption));
            Assert.IsFalse(CleaningRules.NeedsReach(StationKind.DrinkDispenser, CleaningRules.WipeStopOption));
            Assert.IsTrue(CleaningRules.NeedsReach(StationKind.DrinkDispenser, CleaningRules.WipeStartOption));
            Assert.IsTrue(CleaningRules.NeedsReach(StationKind.TrashCan, CleaningRules.TrashOption));
            Assert.AreEqual(1.5f, CleaningRules.ScrubSeconds);
            Assert.AreEqual(HygieneRules.TrashCapacity, 12);
            Assert.AreEqual(-1, StockCatalog.LineOfSlot(StationKind.DrinkDispenser, CleaningRules.SpillTool), "the spill is no stock");
            Assert.AreEqual(-1, StockCatalog.UnpackSlotOf(StationKind.DrinkDispenser, CleaningRules.WipeStartOption));
        }

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type SyncArgument(FieldInfo field) =>
            field.FieldType.IsGenericType && field.FieldType.Name.StartsWith("Sync") ? field.FieldType.GetGenericArguments()[0] : null;

        private static string[] Synced(Type type) => type.GetFields(Instance).Where(f => SyncArgument(f) != null).Select(f => f.Name).ToArray();

        [Test]
        public void TheSyncedSurface_IsTheScoresByte_AndTheGuestsEatIn_NoRpc()
        {
            CollectionAssert.AreEquivalent(new[] { "Percent" }, Synced(typeof(ShopHygiene)));
            Assert.AreEqual(typeof(byte), SyncArgument(typeof(ShopHygiene).GetField("Percent", Instance)), "the score: one byte");
            CollectionAssert.AreEquivalent(new[] { "Wish", "Bubble", "Dish", "Mood", "Patience", "EatIn" }, Synced(typeof(Customer)));
            Assert.AreEqual(typeof(bool), SyncArgument(typeof(Customer).GetField("EatIn", Instance)), "eat in or to go: the hand-off's plate");
            CollectionAssert.AreEquivalent(new[] { "StoveFlags" }, Synced(typeof(StationContents)), "the stations' bytes are the existing one");
            foreach (Type t in new[] { typeof(ShopHygiene), typeof(CleaningService), typeof(DirtyDish), typeof(TrashBag), typeof(WipeCloth),
                         typeof(SinkView), typeof(TrashCanView), typeof(DishReturnView), typeof(SpillWipeView) })
            {
                Assert.IsFalse(t.GetMethods(Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name.EndsWith("RpcAttribute"))), "no RPC on " + t.Name);
                Assert.IsEmpty(Synced(t).Where(n => t != typeof(ShopHygiene)), "nothing synced on " + t.Name);
            }
        }

        [Test]
        public void TheLooks_AreDrawnFromTheBytes_Pure()
        {
            Assert.AreEqual(CleaningArtRules.TrashFloor, CleaningArtRules.TrashLevel(0f), 1e-5f);
            Assert.Less(CleaningArtRules.TrashLevel(1f), CleaningArtRules.TrashHeight);
            Assert.Greater(CleaningArtRules.TrashLevel(0.5f), CleaningArtRules.TrashLevel(0.25f));
            Assert.AreEqual(0, CleaningArtRules.BubbleCount(0, false));
            Assert.Greater(CleaningArtRules.BubbleCount(1, true), CleaningArtRules.BubbleCount(1, false));
            Assert.LessOrEqual(CleaningArtRules.BubbleCount(3, true), CleaningArtRules.MaxBubbles);
            Assert.AreEqual(0f, CleaningArtRules.LidAngle(-1f));
            Assert.AreEqual(CleaningArtRules.LidOpenAngle, CleaningArtRules.LidAngle(CleaningArtRules.LidOpenSeconds * 0.3f), 0.01f);
            Assert.AreEqual(0f, CleaningArtRules.LidAngle(5f));
            Assert.AreEqual(1f, CleaningArtRules.PuddleLeft(0f));
            Assert.AreEqual(0f, CleaningArtRules.PuddleLeft(1f));
            Assert.AreEqual(1f, CleaningRules.HoldShare(CleaningRules.HoldScrub, 2.0));
            Assert.AreEqual(0.5f, CleaningRules.HoldShare(CleaningRules.HoldWipe, 0.5), 1e-4f);
            Assert.AreEqual("Hygiene 92 %", Worktown.Game.UI.HudView.HygieneText(92));
            Assert.AreEqual(Palette.Sage, Worktown.Game.UI.HudView.HygieneColour(50));
            Assert.AreEqual(Palette.Mustard, Worktown.Game.UI.HudView.HygieneColour(49));
            Assert.AreEqual(Palette.Terracotta, Worktown.Game.UI.HudView.HygieneColour(29));
        }

        // ---- 034 review fixes ----

        [Test]
        public void ThreeBoxesInOnePass_LandOnThreeSpots_NeverOnOne()
        {
            var room = new StockRoom();
            var catalog = Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            Assert.AreEqual(CookOutcome.Ok, room.Order(StockCatalog.IndexOf("tomato"), 3, "player-1", 0, catalog, null, out SupplyOrder order));
            room.Deliver(order.DueMinute, new List<SupplyOrder>());
            Assert.AreEqual(3, room.WaitingBoxes);
            var occupied = new[] { false, false, false }; // the probe sees nothing it moved in this frame
            var used = new bool[3];
            var spots = new List<int>();
            int id = 900;
            // 035 review: the service's own pass (SupplyService.SpawnWaitingBoxes runs StockRoom.SpawnWaiting), not a copy of it.
            Assert.AreEqual(3, room.SpawnWaiting(occupied, used, (box, spot) =>
            {
                spots.Add(spot);
                return id++;
            }));
            Assert.AreEqual(3, room.BoxesInShop);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, spots, "one box per spot");
            Assert.AreEqual(-1, StockRoom.FreeSpot(occupied, used), "a fourth waits");
            Assert.AreEqual(1, StockRoom.FreeSpot(new[] { true, false, false }, new bool[3]), "an occupied spot is skipped");
        }

        [Test]
        public void ALostBox_ComesBackAtTheBackDoor_WithItsContents()
        {
            var room = new StockRoom();
            var catalog = Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            room.Order(StockCatalog.IndexOf("lettuce"), 1, "player-1", 0, catalog, null, out SupplyOrder order);
            room.Deliver(order.DueMinute, null);
            room.ConfirmSpawned(41);
            Assert.IsTrue(room.TryGetBox(41, out BoxContents box));
            Assert.IsTrue(room.ReturnLostBox(41));
            Assert.IsFalse(room.TryGetBox(41, out _));
            Assert.IsTrue(room.TryPeekWaiting(out BoxContents again));
            Assert.AreEqual(box.Line, again.Line);
            Assert.AreEqual(box.Units, again.Units);
            Assert.IsFalse(room.ReturnLostBox(41), "once");
        }

        [Test]
        public void AServerStop_RefundsTheOrdersOnTheirWay_AndKeepsTheStockAndTheBoxes()
        {
            var room = new StockRoom();
            var catalog = Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            int tomato = StockCatalog.IndexOf("tomato");
            room.Take(tomato);
            int count = room.LineOf(tomato).Count;
            var wallet = new SupplyWallet();
            room.Order(tomato, 1, "player-1", 0, catalog, wallet, out SupplyOrder first);
            room.Deliver(first.DueMinute, null);
            room.ConfirmSpawned(50);
            room.Order(StockCatalog.IndexOf("lettuce"), 2, "player-2", 10, catalog, wallet, out SupplyOrder second);
            var refunds = new List<(string, long)>();
            Assert.AreEqual(1, room.EndSession((who, amount) => { refunds.Add((who, amount)); return true; }));
            CollectionAssert.AreEqual(new[] { ("player-2", second.Cost) }, refunds, "the order still on its way, to its payer, once");
            Assert.AreEqual(0, room.Book.Pending.Count);
            Assert.AreEqual(count, room.LineOf(tomato).Count, "the stock stays (the game goes on)");
            Assert.AreEqual(0, room.BoxesInShop);
            Assert.AreEqual(1, room.WaitingBoxes, "the delivered box comes back at the back door");
            Assert.AreEqual(0, room.EndSession((who, amount) => { refunds.Add((who, amount)); return true; }), "nothing twice");
        }

        [Test]
        public void AServerStop_WhoseRefundFails_KeepsTheOrder_ItStillArrives()
        {
            // 035 review: an order is never lost without its money: a refused refund keeps it in the book.
            var room = new StockRoom();
            var catalog = Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            int lettuce = StockCatalog.IndexOf("lettuce");
            room.Order(lettuce, 1, "gone", 0, catalog, new SupplyWallet(), out SupplyOrder order);
            int before = room.LineOf(lettuce).Count;
            Assert.AreEqual(0, room.EndSession((who, amount) => false));
            Assert.AreEqual(1, room.Book.Pending.Count, "kept");
            room.Deliver(order.DueMinute, null);
            Assert.AreEqual(1, room.WaitingBoxes, "and it still comes to the back door");
            Assert.AreEqual(before, room.LineOf(lettuce).Count);
            Assert.AreEqual(0, room.EndSession((who, amount) => true), "delivered: nothing left to refund");
            Assert.AreEqual(0, room.Book.Pending.Count);
        }

        private sealed class SupplyWallet : ISupplyWallet
        {
            public bool CanAfford(long cost) => true;

            public CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                cost = 100 * packs;
                return CookOutcome.Ok;
            }

            public void Refund(long cost) { }
        }

        [Test]
        public void EverySauceTheBoardSqueezes_EveryPackaging_AndEveryDrink_HasAStockLine_AndNothingUnstockedIsFree()
        {
            for (int d = 0; d < BuildRules.DispenserCount; d++)
                Assert.GreaterOrEqual(StockCatalog.IndexOf(BuildRules.SauceOf(d)), 0, BuildRules.SauceOf(d));
            for (int p = 0; p < BuildRules.PackagingCount; p++)
                Assert.GreaterOrEqual(StockCatalog.IndexOf(BuildRules.PackagingId(p)), 0, BuildRules.PackagingId(p));
            foreach (BaseItemDef drink in Drinks.All)
                Assert.GreaterOrEqual(StockCatalog.IndexOf(drink.Id), 0, drink.Id);
            Assert.GreaterOrEqual(StockCatalog.IndexOf(Drinks.CupId), 0, "the cups");
            var wallet = new StockWallet(new StockRoom());
            Assert.AreEqual(CookOutcome.OutOfStock, wallet.Charge(Cook, "basil", 20, out long cost), "a good the shop does not stock is no free sauce");
            Assert.AreEqual(0, cost);
            Assert.IsFalse(wallet.CanAfford(Cook, "basil", 20));
            Assert.AreEqual(CookOutcome.OutOfStock, wallet.Charge(Cook, "basil_tea", 20, false, out _), "nor a free drink");
            Assert.IsFalse(wallet.CanAfford(Cook, "basil_tea", 20, false));
        }

        [Test]
        public void AStockDraw_IsWholeOrNothing_NoLastDrops()
        {
            var room = new StockRoom();
            int chili = StockCatalog.IndexOf("chili_sauce");
            while (room.CanTake(chili)) room.Take(chili); // no sealed bottle left
            var wallet = new StockWallet(room);
            Assert.IsFalse(wallet.CanAfford(Cook, "chili_sauce", 10), "nothing left to draw");
            room.ReturnPercent(chili, 20); // 20 % of a bottle left (opened)
            Assert.AreEqual(CookOutcome.OutOfStock, wallet.Charge(Cook, "chili_sauce", 30, out _), "no lots squeeze out of 20 %");
            Assert.AreEqual(0, wallet.Drawn);
            Assert.AreEqual(CookOutcome.Ok, wallet.Charge(Cook, "chili_sauce", 20, out _));
            Assert.AreEqual(20, wallet.Drawn);
            Assert.AreEqual(CookOutcome.OutOfStock, wallet.Charge(Cook, "chili_sauce", 10, out _));
            Assert.AreEqual(0, wallet.Drawn, "each charge starts from nothing drawn");
        }
    }
}
