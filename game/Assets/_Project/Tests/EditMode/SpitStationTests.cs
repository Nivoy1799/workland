using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Assembly = Worktown.Sim.Cooking.Assembly;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 036 AC3: the seitan spit on the server, pure (SpitDesk, SpitRules, KitchenState): the shave is server-timed (the 031b re-press
    /// rule, the auto-stop, a player who left, the knife put down), the catch tray holds 3 portions, a scoop goes into the hands or onto a
    /// held build as a 031 layer, a cone is loaded from stock (paid at the terminal only; refusals change nothing), the outer layer's 027c
    /// zones from the real sim op, the bytes; and the client's rules, the looks, the review's note and the synced surface.
    /// </summary>
    public class SpitStationTests
    {
        private const double Rate = 30d;
        private const uint T0 = 1000;
        private const int SpitIndex = 16;
        private const int Cook = 3, Other = 4;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static uint At(double seconds) => T0 + (uint)Math.Round(seconds * Rate);

        private static SpitDesk Loaded(out KitchenState kitchen, int level = Spit.High)
        {
            kitchen = KitchenState.CreateDemo();
            var desk = new SpitDesk(kitchen);
            desk.Register(SpitIndex);
            Assert.IsTrue(desk.Load(SpitIndex, T0));
            while (desk.LevelOf(SpitIndex) != level) desk.TurnKnob(SpitIndex, T0, Rate);
            return desk;
        }

        // ---- the outer layer ----

        [Test]
        public void TheOuterLayer_FollowsTheSimsShaveOp_ItsZonesMeasured_AndTheKnob()
        {
            SpitDesk desk = Loaded(out KitchenState kitchen);
            HeatTimeline line = kitchen.SpitTimeline();
            double S(double sim) => sim / CookingTime.TimeScale;
            double warn = line.OverdoneFrom - HeatZoneRules.WarnLeadSimSeconds(line);
            TestContext.WriteLine("spit outer layer (high heat), real s: good " + S(line.GoodFrom).ToString("0.0") + ", perfect "
                + S(line.PerfectFrom).ToString("0.0") + ", warning " + S(warn).ToString("0.0") + ", overdone " + S(line.OverdoneFrom).ToString("0.0")
                + ", burnt " + S(line.BurntFrom).ToString("0.0"));
            Assert.AreEqual(40.0, S(line.GoodFrom), 0.1);
            Assert.AreEqual(55.0, S(line.PerfectFrom), 0.1);
            Assert.AreEqual(70.0, S(line.OverdoneFrom), 0.1);
            Assert.AreEqual(90.0, S(line.BurntFrom), 0.1);
            Assert.AreEqual(HeatZone.Cooking, desk.Reading(SpitIndex, At(20), Rate).Zone);
            Assert.AreEqual(HeatZone.Good, desk.Reading(SpitIndex, At(45), Rate).Zone);
            Assert.AreEqual(HeatZone.Perfect, desk.Reading(SpitIndex, At(60), Rate).Zone);
            Assert.IsTrue(desk.Reading(SpitIndex, At(68), Rate).Warn, "the warning before overdone");
            Assert.AreEqual(HeatZone.Burnt, desk.Reading(SpitIndex, At(91), Rate).Zone);
            Assert.AreEqual("pale shaved seitan", desk.TakeName(SpitIndex, At(20), Rate), "the label is the take name");
            Assert.AreEqual("shaved seitan", desk.TakeName(SpitIndex, At(60), Rate));
            // Low heat cooks at half the speed; off keeps what it had.
            SpitDesk low = Loaded(out _, Spit.Low);
            Assert.AreEqual(HeatZone.Cooking, low.Reading(SpitIndex, At(60), Rate).Zone);
            Assert.AreEqual(HeatZone.Good, low.Reading(SpitIndex, At(82), Rate).Zone);
            SpitDesk off = Loaded(out _);
            off.TurnKnob(SpitIndex, At(30), Rate); // high -> off at 30 s
            Assert.AreEqual(Spit.Off, off.LevelOf(SpitIndex));
            Assert.AreEqual(off.Reading(SpitIndex, At(30), Rate).Progress, off.Reading(SpitIndex, At(300), Rate).Progress, "off: no more heat");
            Assert.AreEqual(HeatReading.Empty.Zone, new SpitDesk(kitchen).Reading(SpitIndex, At(30), Rate).Zone, "no spit, nothing");
        }

        // ---- the shave: server-timed ----

        [Test]
        public void TheShave_IsServerTimed_ARePressKeepsItsStart_AndTheServerStopsItAtThreeSeconds()
        {
            SpitDesk desk = Loaded(out _);
            Assert.AreEqual(CookOutcome.NoKnife, desk.Press(SpitIndex, Cook, false, At(55)));
            Assert.AreEqual(CookOutcome.Ok, desk.Press(SpitIndex, Cook, true, At(55)));
            Assert.AreEqual(CookOutcome.Ok, desk.Press(SpitIndex, Cook, true, At(57.9)), "031b 10.0: the same player's re-press keeps the start");
            Assert.AreEqual(CookOutcome.DispenserBusy, desk.Press(SpitIndex, Other, true, At(56)), "another cook waits");
            Assert.AreEqual(SpitRules.ShavingBit, desk.Flags(SpitIndex) & SpitRules.ShavingBit, "the byte says someone shaves");
            var due = new List<int>();
            desk.DueHolds(At(57.95), Rate, due);
            Assert.IsEmpty(due, "not yet at 2.95 s");
            desk.DueHolds(At(58.0), Rate, due);
            CollectionAssert.AreEqual(new[] { SpitIndex }, due, "the auto-stop at 3 s from the first press");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(SpitIndex, Cook, true, At(58.0), Rate, out int three));
            Assert.AreEqual(3, three);
            Assert.AreEqual(3, desk.TrayCount(SpitIndex));
            Assert.AreEqual("shaved seitan", KitchenState.NameOf(desk.TrayOf(SpitIndex)[0]), "each with the layer's doneness");
            Assert.AreEqual(CookOutcome.Ok, desk.Release(SpitIndex, Cook, true, At(59), Rate, out int none), "a late release: harmless");
            Assert.AreEqual(0, none);
            Assert.AreEqual(Spit.PortionsPerCone - 3, SpitRules.PortionsOf(desk.ConeMode(SpitIndex)));
            Assert.AreEqual(1, SpitRules.ShavesOf(desk.ConeMode(SpitIndex)), "every viewer sees the shave");
            // The next layer starts half-way to good: shaving it now gives pale seitan.
            Assert.AreEqual("pale shaved seitan", desk.TakeName(SpitIndex, At(59), Rate));
        }

        [Test]
        public void AShortTap_ShavesNothing_OneSecondIsOnePortion_AndAPutDownKnifeShavesNothing()
        {
            SpitDesk desk = Loaded(out _);
            desk.Press(SpitIndex, Cook, true, At(50));
            desk.Release(SpitIndex, Cook, true, At(50.3), Rate, out int tap);
            Assert.AreEqual(0, tap, "0.3 s: nothing");
            Assert.AreEqual(Spit.PortionsPerCone, SpitRules.PortionsOf(desk.ConeMode(SpitIndex)), "and the cone is as it was");
            desk.Press(SpitIndex, Cook, true, At(55));
            desk.Release(SpitIndex, Cook, true, At(56), Rate, out int one);
            Assert.AreEqual(1, one);
            desk.Press(SpitIndex, Cook, true, At(80));
            desk.Release(SpitIndex, Cook, false, At(81.6), Rate, out int dropped);
            Assert.AreEqual(0, dropped, "the knife was put down meanwhile: no shave");
            Assert.AreEqual(1, desk.TrayCount(SpitIndex));
            Assert.IsFalse(desk.IsHeld(SpitIndex, out _, out _), "the spit is free again");
        }

        [Test]
        public void APlayerWhoLeft_IsFreed_WithoutAShave()
        {
            SpitDesk desk = Loaded(out _);
            desk.Press(SpitIndex, Cook, true, At(55));
            var dropped = new List<int>();
            desk.DropLeft(owner => owner != Cook, dropped);
            CollectionAssert.AreEqual(new[] { SpitIndex }, dropped);
            Assert.IsFalse(desk.IsHeld(SpitIndex, out _, out _));
            Assert.AreEqual(0, desk.TrayCount(SpitIndex));
            Assert.AreEqual(CookOutcome.Ok, desk.Press(SpitIndex, Other, true, At(56)), "another cook may shave now");
        }

        [Test]
        public void TheTray_HoldsThreePortions_ALongShaveFillsOnlyItsRoom()
        {
            SpitDesk desk = Loaded(out _);
            desk.Press(SpitIndex, Cook, true, At(55));
            desk.Release(SpitIndex, Cook, true, At(57), Rate, out int two);
            Assert.AreEqual(2, two);
            desk.Press(SpitIndex, Cook, true, At(80));
            desk.Release(SpitIndex, Cook, true, At(83), Rate, out int room);
            Assert.AreEqual(1, room, "only one more fits");
            Assert.AreEqual(SpitRules.TrayCapacity, desk.TrayCount(SpitIndex));
            Assert.AreEqual(CookOutcome.TrayFull, desk.Press(SpitIndex, Cook, true, At(100)), "scoop first");
            Assert.AreEqual(CookOutcome.Ok, desk.TakePortion(SpitIndex, out Item portion));
            Assert.IsNotNull(portion);
            Assert.AreEqual(CookOutcome.Ok, desk.Press(SpitIndex, Cook, true, At(100)));
        }

        [Test]
        public void TheCone_RunsOut_ThenTheSpitIsBareUntilANewOneIsLoaded()
        {
            SpitDesk desk = Loaded(out _);
            int shaved = 0;
            double t = 60;
            while (desk.HasCone(SpitIndex))
            {
                desk.Press(SpitIndex, Cook, true, At(t));
                desk.Release(SpitIndex, Cook, true, At(t + 3), Rate, out int n);
                shaved += n;
                while (desk.TakePortion(SpitIndex, out _) == CookOutcome.Ok) { }
                t += 30;
                Assert.Less(t, 2000, "it must run out");
            }
            Assert.AreEqual(Spit.PortionsPerCone, shaved);
            Assert.AreEqual(0, desk.Flags(SpitIndex) & SpitRules.ConeBit, "bare");
            Assert.AreEqual(CookOutcome.NoCone, desk.Press(SpitIndex, Cook, true, At(t)));
            Assert.AreEqual(HeatZone.None, desk.Reading(SpitIndex, At(t), Rate).Zone);
            Assert.IsNull(desk.TakeName(SpitIndex, At(t), Rate));
        }

        // ---- the scoop ----

        [Test]
        public void AScoop_GoesIntoEmptyHands_OrOntoTheHeldBuildAsItsNextLayer_RefusalsChangeNothing()
        {
            SpitDesk desk = Loaded(out KitchenState kitchen);
            desk.Press(SpitIndex, Cook, true, At(55));
            desk.Release(SpitIndex, Cook, true, At(58), Rate, out _);
            // Into the hands: a failed hand-over puts it back.
            Assert.AreEqual(CookOutcome.SimRefused, desk.ScoopIntoHands(SpitIndex, false, p => false, out _));
            Assert.AreEqual(3, desk.TrayCount(SpitIndex));
            Item handed = null;
            Assert.AreEqual(CookOutcome.Ok, desk.ScoopIntoHands(SpitIndex, false, p => { handed = p; return true; }, out Item portion));
            Assert.AreSame(portion, handed);
            Assert.AreEqual(2, desk.TrayCount(SpitIndex));
            Assert.AreEqual(CookOutcome.HandsFull, desk.ScoopIntoHands(SpitIndex, true, p => true, out _));
            // Onto a held build: a wrap taken off the assembly board.
            kitchen.RegisterAssemblySpot(90, 8);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("wrap", out Item wrap));
            kitchen.Register(40, wrap);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(40, 90, VesselKind.Assembly, T0));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBuildTake(90, out Item open));
            kitchen.Register(77, open);
            Assert.AreEqual(CookOutcome.Ok, desk.ScoopOntoBuild(SpitIndex, 77, out Item build));
            Assert.AreEqual(1, desk.TrayCount(SpitIndex));
            Assert.IsTrue(kitchen.TryGet(77, out Item held));
            Assert.AreSame(build, held, "the held food is the new build");
            IReadOnlyList<Item> layers = Assembly.LayersOf(held);
            Assert.AreEqual(2, layers.Count);
            Assert.AreEqual(Spit.OperationId, Assembly.HeatOpOf(layers[1]), "the shaved seitan is the build's next (031) layer");
            // Plain food in the hands is no build; a closed build refuses and the portion stays in the tray.
            kitchen.Register(78, wrap);
            Assert.AreEqual(CookOutcome.HandsFull, desk.ScoopOntoBuild(SpitIndex, 78, out _));
            kitchen.Register(79, ClosedBuild(kitchen, held));
            Assert.AreEqual(CookOutcome.HandsFull, desk.ScoopOntoBuild(SpitIndex, 79, out _), "a folded build takes no more");
            Assert.AreEqual(1, desk.TrayCount(SpitIndex), "nothing changed");
            Assert.AreEqual(CookOutcome.BuildClosed, kitchen.TryAddLayerToHeld(79, desk.TrayOf(SpitIndex)[0], out _));
        }

        private static Item ClosedBuild(KitchenState kitchen, Item build)
        {
            kitchen.RegisterAssemblySpot(91, 9);
            Assert.IsTrue(kitchen.CommitBuild(91, build));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryFold(91));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBuildTake(91, out Item folded));
            return folded;
        }

        // ---- loading a cone from stock ----

        [Test]
        public void ACone_IsLoadedFromStock_PaidAtTheTerminalOnly_AndRefusalsChangeNothing()
        {
            var stock = new StockRoom();
            int line = StockCatalog.ConeLine;
            Assert.GreaterOrEqual(line, 0, "the cone is a stock line");
            Assert.AreEqual(StockCatalog.Count - 1, line, "appended last: every earlier line keeps its index");
            Assert.AreEqual(1, stock.LineOf(line).Count, "a new game starts with one cone (half of two)");
            Assert.AreEqual(line, StockCatalog.LineOfSlot(StationKind.Spit, SpitRules.ConeTool), "the cone slot shows the cones in stock");
            Assert.AreEqual(SpitRules.ConeTool, StockCatalog.UnpackSlotOf(StationKind.Spit, SpitRules.LoadOption), "a box of cones unpacks on the cone");
            Assert.AreEqual(SpitRules.LoadOption, StockCatalog.UnpackOptionOf(StationKind.Spit, SpitRules.ConeTool));

            var kitchen = KitchenState.CreateDemo();
            var desk = new SpitDesk(kitchen);
            desk.Register(SpitIndex);
            int takes = 0, returns = 0;
            CookOutcome Take() { takes++; return stock.Take(line); }
            void GiveBack() { returns++; stock.Return(line, 1); }
            Assert.AreEqual(CookOutcome.HandsFull, desk.LoadCone(SpitIndex, true, T0, Take, GiveBack));
            Assert.AreEqual(0, takes, "a refusal takes nothing");
            Assert.AreEqual(CookOutcome.Ok, desk.LoadCone(SpitIndex, false, T0, Take, GiveBack));
            Assert.AreEqual(0, stock.LineOf(line).Count, "one unit drawn");
            Assert.IsTrue(desk.HasCone(SpitIndex));
            Assert.AreEqual(CookOutcome.ConeLoaded, desk.LoadCone(SpitIndex, false, T0, Take, GiveBack));
            Assert.AreEqual(1, takes, "the loaded spit took nothing more");
            // A bare spit with no cone in stock: OutOfStock, nothing changes.
            var bare = new SpitDesk(kitchen);
            bare.Register(SpitIndex);
            Assert.AreEqual(CookOutcome.OutOfStock, bare.LoadCone(SpitIndex, false, T0, Take, GiveBack));
            Assert.IsFalse(bare.HasCone(SpitIndex));
            Assert.AreEqual(0, stock.LineOf(line).Count);
            // No such spit: the unit taken goes back (the backstop) -- here it is never taken, CanLoad refuses first.
            Assert.AreEqual(CookOutcome.UnknownStation, new SpitDesk(kitchen).LoadCone(7, false, T0, Take, GiveBack));
            Assert.AreEqual(0, returns);

            // Money moves only at the terminal: a cone is 30 seitan blocks at the bulk price (a portion costs what a block does), one a box.
            var catalog = EconomyCatalog.CreateDefault();
            Assert.IsTrue(StockCatalog.TryOffer(catalog, line, out SupplierOffer offer));
            Assert.AreEqual(StockCatalog.ConeId, offer.GoodId);
            Assert.AreEqual(1, offer.PackSize);
            Assert.IsTrue(catalog.TryGetGrocerPrice("seitan", out long seitan));
            long cone = Supplier.OrderCost(catalog, offer, 1);
            Assert.AreEqual(Supplier.OrderCost(catalog, seitan, Spit.PortionsPerCone), cone);
            Assert.AreEqual(Supplier.BulkUnitPrice(catalog, seitan) * Spit.PortionsPerCone, cone, 1.0, "a portion costs a block's bulk price");
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.Ok, stock.Order(line, 1, "player-1", 0, catalog, wallet, out SupplyOrder order));
            Assert.AreEqual((1, cone), (wallet.Charges, wallet.Charged), "charged once, at the order");
            stock.Deliver(order.DueMinute, null);
            Assert.IsTrue(stock.TryPeekWaiting(out BoxContents box));
            Assert.AreEqual((line, 1), (box.Line, box.Units), "one cone in its box");
            stock.ConfirmSpawned(300);
            Assert.AreEqual(CookOutcome.Ok, stock.Unpack(300, line, out int accepted, out _));
            Assert.AreEqual(1, accepted);
            Assert.AreEqual(CookOutcome.Ok, bare.LoadCone(SpitIndex, false, T0, Take, GiveBack));
            Assert.AreEqual(1, wallet.Charges, "loading charged nothing");
            Assert.AreEqual(StockCatalog.ConeCapacity, StockCatalog.Goods[line].Capacity);
            Assert.AreEqual("seitan cone", StockCatalog.NameOf(line));
        }

        private sealed class CountingWallet : ISupplyWallet
        {
            public int Charges;
            public long Charged;

            public bool CanAfford(long cost) => true;

            public CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                cost = Supplier.OrderCost(EconomyCatalog.CreateDefault(), offer, packs);
                Charges++;
                Charged += cost;
                return CookOutcome.Ok;
            }

            public void Refund(long cost) => Charged -= cost;
        }

        // ---- bytes, client rules, looks ----

        [Test]
        public void TheBytes_AndOptions_ArePinned_AndTheReleaseNeedsNoReach()
        {
            Assert.AreEqual(0, SpitRules.ConeTool);
            Assert.AreEqual((1, 2, 3, 4), (SpitRules.KnobOption, SpitRules.LoadOption, SpitRules.ShaveStartOption, SpitRules.ShaveStopOption));
            Assert.AreEqual(3, SpitRules.TrayCapacity);
            byte flags = SpitRules.Flags(Spit.High, true, true);
            Assert.AreEqual(2 | 4 | 8, flags);
            Assert.AreEqual(Spit.High, SpitRules.LevelOf(flags));
            Assert.IsTrue(SpitRules.IsShaving(flags) && SpitRules.HasCone(flags) && SpitRules.IsOn(flags));
            Assert.IsFalse(SpitRules.IsShaving(SpitRules.WithShaving(flags, false)));
            Assert.AreEqual(Spit.Low, SpitRules.LevelOf(SpitRules.Flags(Spit.Low, false, false)));
            byte mode = SpitRules.ConeMode(27, 5);
            Assert.AreEqual(27, SpitRules.PortionsOf(mode));
            Assert.AreEqual(1, SpitRules.ShavesOf(mode), "the counter wraps at 4");
            Assert.AreEqual(0.9f, SpitRules.ShareLeft(mode), 1e-4f);
            Assert.IsFalse(SpitRules.NeedsReach(StationKind.Spit, SpitRules.ShaveStopOption), "the release ends it anywhere");
            Assert.IsTrue(SpitRules.NeedsReach(StationKind.Spit, SpitRules.ShaveStartOption));
            Assert.IsTrue(SpitRules.NeedsReach(StationKind.Sink, SpitRules.ShaveStopOption));
            Assert.AreEqual(FoodLook.HeatShaved, FoodLookRules.HeatOf(Spit.OperationId));
            Assert.IsTrue(FoodArtRules.IsFried(FoodLook.HeatShaved), "golden like fried");
            Assert.IsTrue(VesselRules.IsFixed(VesselKind.SpitCone) && VesselRules.IsFixed(VesselKind.SpitTray));
            Assert.AreEqual(SpitRules.TrayCapacity, VesselRules.Capacity(VesselKind.SpitTray));
        }

        [Test]
        public void ShavedSeitan_LooksLikeGoldenSlices()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryShave(55f * CookingTime.TimeScale, out Item portion));
            FoodLook look = FoodLookRules.From(portion);
            Assert.AreEqual(FoodLook.ShapeSlices, look.Shape, "curls, never a whole block");
            Assert.AreEqual(FoodLook.HeatShaved, look.Heat);
            Assert.AreEqual(FoodLook.DonenessMedium, look.Doneness);
        }

        [Test]
        public void TheSyncedSurface_IsUnchanged_TheSpitUsesTheStationByteAndTwoVessels_NoRpc()
        {
            static Type Arg(FieldInfo f) => f.FieldType.IsGenericType && f.FieldType.Name.StartsWith("Sync") ? f.FieldType.GetGenericArguments()[0] : null;
            static string[] Synced(Type t) => t.GetFields(Instance).Where(f => Arg(f) != null).Select(f => f.Name).ToArray();
            CollectionAssert.AreEquivalent(new[] { "StoveFlags" }, Synced(typeof(StationContents)), "the spit's byte is the existing one");
            CollectionAssert.AreEquivalent(new[] { "Names", "Looks", "Title", "Mode", "HeatProgress", "HeatZone" }, Synced(typeof(Vessel)),
                "the cone and the tray are plain vessels (6 synced members, as since 027c)");
            foreach (Type t in new[] { typeof(SpitPart), typeof(SpitView) })
            {
                Assert.IsEmpty(Synced(t), "nothing synced on " + t.Name);
                Assert.IsFalse(t.GetMethods(Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name.EndsWith("RpcAttribute"))), "no RPC on " + t.Name);
            }
        }

        private static AimTarget Cone(byte flags, int tray = 0, string name = "shaved seitan", int stock = 1) =>
            AimTarget.SpitTool(SpitRules.ConeTool, flags, SpitRules.ConeMode(20, 0), tray, name).WithStock(StockCatalog.ConeLine, stock);

        [Test]
        public void TheRules_OfferTheShaveTheLoadAndTheScoop_WithTheirPromptsAndReasons()
        {
            byte on = SpitRules.Flags(Spit.High, false, true);
            Assert.AreEqual(InteractionAction.Shave, InteractionRules.ActionFor(Held.Knife, Cone(on)));
            Assert.AreEqual("Hold · shave the cone: shaved seitan (1/3 in the tray)", InteractionRules.PromptFor(Held.Knife, Cone(on, 1)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Knife, Cone(on, 3)));
            Assert.AreEqual("The tray is full: scoop it first", InteractionRules.PromptFor(Held.Knife, Cone(on, 3)));
            Assert.AreEqual("Someone is shaving", InteractionRules.PromptFor(Held.Knife, Cone(SpitRules.WithShaving(on, true))));
            Assert.AreEqual("Take the knife to shave", InteractionRules.PromptFor(Held.Nothing, Cone(on)));
            byte bare = SpitRules.Flags(Spit.Off, false, false);
            Assert.AreEqual(InteractionAction.Load, InteractionRules.ActionFor(Held.Nothing, Cone(bare)));
            Assert.AreEqual("Click · load a seitan cone (1 left)", InteractionRules.PromptFor(Held.Nothing, Cone(bare)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, Cone(bare, stock: 0)));
            Assert.AreEqual(InteractionRules.OutOfStockText, InteractionRules.PromptFor(Held.Nothing, Cone(bare, stock: 0)));
            Assert.AreEqual("Load a seitan cone first", InteractionRules.PromptFor(Held.Knife, Cone(bare)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Food("tomato", false), Cone(bare)));
            Assert.AreEqual(InteractionAction.Press, InteractionRules.ActionFor(Held.Nothing, AimTarget.Button(StationKind.Spit, SpitRules.KnobOption, bare)));
            Assert.AreEqual("Click · heat low", InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.Spit, SpitRules.KnobOption, bare)));
            Assert.AreEqual("Click · heat off", InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.Spit, SpitRules.KnobOption, on)));
            // A carried box of cones unpacks on the cone.
            Held box = Held.Box("box of 1 seitan cone", StockCatalog.ConeLine, 1);
            Assert.AreEqual(InteractionAction.Unpack, InteractionRules.ActionFor(box, Cone(bare, stock: 0)));

            AimTarget tray = AimTarget.Cookware(VesselKind.SpitTray, 2, "shaved seitan");
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Nothing, tray));
            Assert.AreEqual("Click · take shaved seitan", InteractionRules.PromptFor(Held.Nothing, tray));
            Held wrap = Held.Food("wrap", false, true);
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(wrap, tray));
            Assert.AreEqual("Click · scoop shaved seitan onto the wrap", InteractionRules.PromptFor(wrap, tray));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Food("seitan wrap in paper", true, true), tray), "finished: no");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Food("tomato", false), tray));
            Assert.AreEqual("Scoop with empty hands or onto a wrap", InteractionRules.PromptFor(Held.Food("tomato", false), tray));
            Assert.AreEqual("Shave the cone with the knife", InteractionRules.PromptFor(Held.Nothing, AimTarget.Cookware(VesselKind.SpitTray, 0, null)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.Cookware(VesselKind.SpitCone, 1, "shaved seitan")));
        }

        [Test]
        public void TheLooks_ShrinkBrownGlowAndCurl_Pure()
        {
            for (int i = 0; i < SpitArtRules.Discs; i++)
            {
                Assert.Greater(SpitArtRules.DiscRadius(i, 1f), SpitArtRules.DiscRadius(i, 0.5f));
                Assert.Greater(SpitArtRules.DiscRadius(i, 0.5f), SpitArtRules.DiscRadius(i, 0f));
                Assert.AreEqual(SpitArtRules.CoreRadius, SpitArtRules.DiscRadius(i, 0f), 1e-5f, "a cone that ran out leaves the core");
            }
            Assert.Greater(SpitArtRules.DiscRadius(SpitArtRules.Discs - 1, 1f), SpitArtRules.DiscRadius(0, 1f), "wider at the top");
            Assert.AreEqual(Palette.WoodLight, SpitArtRules.OuterColour(HeatZone.Raw, 0f, 0));
            Assert.AreEqual(Palette.WoodDark, SpitArtRules.OuterColour(HeatZone.Perfect, 1f, 0));
            Color burnt = SpitArtRules.OuterColour(HeatZone.Burnt, 1f, 0);
            Assert.Less(burnt.r + burnt.g + burnt.b, Palette.WoodDark.r + Palette.WoodDark.g + Palette.WoodDark.b, "darker when burnt");
            Assert.AreEqual(Color.black, SpitArtRules.GlowColour(Spit.Off));
            Assert.Greater(SpitArtRules.GlowColour(Spit.High).r, SpitArtRules.GlowColour(Spit.Low).r, "brighter on high");
            Vector3 from = new Vector3(0f, 1.4f, 0f), to = new Vector3(0f, 1.1f, 0.2f);
            Assert.AreEqual(0f, Vector3.Distance(from, SpitArtRules.CurlPoint(from, to, 0f)), 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(to, SpitArtRules.CurlPoint(from, to, SpitArtRules.CurlFallSeconds)), 1e-5f, "it lands in the tray");
            for (double s = 0; s < 3; s += 0.05)
            {
                Assert.That(SpitArtRules.KnifeHeight(s), Is.InRange(SpitArtRules.ConeBottom, SpitArtRules.ConeTop), "the knife stays on the cone");
                Assert.That(SpitArtRules.KnifeStroke(s), Is.InRange(0f, 1f));
            }
            Assert.AreEqual(0.6f, Worktown.Game.Audio.SoundRules.SpitSizzleScale(Spit.Low), 1e-5f);
            Assert.AreEqual(1f, Worktown.Game.Audio.SoundRules.SpitSizzleScale(Spit.High));
            Assert.AreEqual(0f, Worktown.Game.Audio.SoundRules.SpitSizzleScale(Spit.Off));
        }

        [Test]
        public void TheReview_SaysWhichSeitan_TheSpitsOrTheGriddles()
        {
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.Catalog), OperationCatalog.CreateDefault());
            Item B(string id) => factory.CreateBase(id).Item;
            Item Cut(Item i, string shape) => factory.Apply("cut", new[] { i }, new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }).Item;
            Item shaved = factory.Apply(Spit.OperationId, new[] { B("seitan") }, Spit.ShaveParams(55f * CookingTime.TimeScale)).Item;
            Item grilled = factory.Apply(Griddle.OperationId, new[] { Cut(B("seitan"), "sticks") }, Griddle.Params(13f * 6f, 13f * 6f)).Item;
            Item chili = factory.Apply(Assembly.SqueezeOperationId, new[] { B("chili_sauce") }, Assembly.SqueezeParams(1f)).Item;
            Item Wrap(Item seitan)
            {
                Item open = factory.Apply(Assembly.AssembleOperationId, new[] { B("wrap"), seitan, Cut(B("lettuce"), "slices"), chili },
                    new Dictionary<string, ParamValue>()).Item;
                Item folded = factory.Apply(Assembly.CloseOperationId, new[] { open }, Assembly.CloseParams(Assembly.Folded)).Item;
                return factory.Apply(Assembly.CloseOperationId, new[] { folded }, Assembly.CloseParams(Assembly.Wrapped)).Item;
            }
            MenuDish dish = Menu.ById(Menu.SeitanWrap);
            OrderFit spit = OrderRules.Fit(Wrap(shaved), dish, 0), griddle = OrderRules.Fit(Wrap(grilled), dish, 0);
            Assert.AreEqual(OrderMatch.Exact, spit.Match, "shaved seitan is a seitan wrap");
            Assert.AreEqual(OrderMatch.Exact, griddle.Match, "and so are grilled strips (unchanged)");
            Assert.AreEqual("The seitan was shaved fresh off the spit.", spit.Note);
            Assert.AreEqual("The seitan came as grilled strips.", griddle.Note);
            var review = new GuestReview(5, ReviewKey.Great);
            Assert.AreEqual("★★★★★ Perfect, just how I asked. The seitan was shaved fresh off the spit.", CustomerRules.ReviewText(review, spit.Note));
            Assert.AreEqual(CustomerRules.ReviewText(review), CustomerRules.ReviewText(review, null), "no note: the old text");
            Assert.AreEqual(spit.Note, spit.WithNote(spit.Note).Note);
        }
    }
}
