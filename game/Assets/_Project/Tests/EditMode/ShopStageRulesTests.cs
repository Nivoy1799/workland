using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 053 AC6: what stage 1 opens (exactly the list), the shared closed test the server's refusal and the client's prompt use, the
    /// stage's guests (the same draws, but fries, to go, cash) and the card terminal's refusal.
    /// </summary>
    public class ShopStageRulesTests
    {
        private const byte Corner = ShopStageRules.FoodCorner;

        private static readonly StationKind[] OpenWhole =
            { StationKind.Fryer, StationKind.PlateShelf, StationKind.Counter, StationKind.TrashCan, StationKind.Bin,
                StationKind.MenuBoard, // spec 037: the fries' price is set at the food corner too
                StationKind.GoalBoard, // spec 057: open at every stage (GoalBoardRules refuses by itself)
                StationKind.OfficeTerminal, // spec 059: open at every stage (its apps refuse by themselves)
                StationKind.DoorSign }; // spec 062b: the shop can be closed at every stage

        private static readonly StationKind[] Closed =
        {
            StationKind.Crate, StationKind.Stove, StationKind.Fridge, StationKind.Griddle, StationKind.Assembly, StationKind.DrinkDispenser,
            StationKind.Terminal, StationKind.DishReturn, StationKind.Sink, StationKind.Spit,
        };

        [Test]
        public void IsOpen_MatchesTheOpenListExactly()
        {
            foreach (StationKind kind in (StationKind[])Enum.GetValues(typeof(StationKind)))
                for (int slot = 0; slot < 24; slot++)
                {
                    Assert.IsTrue(ShopStageRules.IsOpen(ShopStageRules.OpenKitchen, kind, slot), "the open kitchen opens everything");
                    bool expected = OpenWhole.Contains(kind)
                        || (kind == StationKind.Register && slot != RegisterRules.CardOption)
                        || (kind == StationKind.Shelf && StockCatalog.LineOfSlot(kind, slot) == StockCatalog.IndexOf("potato"));
                    Assert.AreEqual(expected, ShopStageRules.IsOpen(Corner, kind, slot), kind + " slot " + slot);
                }
            Assert.AreEqual(OpenWhole.Length + Closed.Length + 2, Enum.GetValues(typeof(StationKind)).Length, "every station kind is listed");
            foreach (StationKind kind in Closed) Assert.IsFalse(ShopStageRules.IsStationOpen(Corner, kind), kind + " gets a cover");
            foreach (StationKind kind in OpenWhole) Assert.IsTrue(ShopStageRules.IsStationOpen(Corner, kind), kind.ToString());
            Assert.IsTrue(ShopStageRules.IsStationOpen(Corner, StationKind.Shelf), "the potato slot is open");
            Assert.IsTrue(ShopStageRules.IsStationOpen(Corner, StationKind.Register), "the cash tray is open");
            int openShelfSlots = 0;
            for (int slot = 0; slot < StockLayout.Shelf.Count; slot++) if (ShopStageRules.IsOpen(Corner, StationKind.Shelf, slot)) openShelfSlots++;
            Assert.AreEqual(1, openShelfSlots, "only the potato slot");
            Assert.IsFalse(ShopStageRules.IsOpen(Corner, StationKind.Register, RegisterRules.CardOption), "the card terminal is closed");
        }

        [Test]
        public void TheFixedCookwareOfClosedStations_IsClosed_CarriedCookwareIsNot()
        {
            foreach (VesselKind kind in new[] { VesselKind.Griddle, VesselKind.Assembly, VesselKind.SpitCone, VesselKind.SpitTray })
                Assert.IsFalse(ShopStageRules.IsOpenVessel(Corner, kind), kind.ToString());
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Plate, VesselKind.Basket, VesselKind.Bowl, VesselKind.Pan, VesselKind.Pot })
                Assert.IsTrue(ShopStageRules.IsOpenVessel(Corner, kind), kind.ToString());
            foreach (VesselKind kind in (VesselKind[])Enum.GetValues(typeof(VesselKind)))
                Assert.IsTrue(ShopStageRules.IsOpenVessel(ShopStageRules.OpenKitchen, kind));
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [Test]
        public void TheSharedClosedTest_RefusesEachClosedStationAndSlot()
        {
            var made = new List<GameObject>();
            try
            {
                foreach (StationKind kind in (StationKind[])Enum.GetValues(typeof(StationKind)))
                {
                    var go = new GameObject(kind.ToString());
                    made.Add(go);
                    Station station = go.AddComponent<Station>();
                    Set(station, "_kind", kind);
                    bool closed = Closed.Contains(kind);
                    Assert.AreEqual(closed, ClosedTargets.IsClosed(Corner, station), kind.ToString());
                    Assert.IsFalse(ClosedTargets.IsClosed(ShopStageRules.OpenKitchen, station), kind + " in the open kitchen");
                    if (closed) Assert.IsTrue(ClosedTargets.IsClosed(Corner, station, 0), kind + " option 0 (the server's request)");
                    // A part of the station (a collider child) is the station.
                    var child = new GameObject("part");
                    child.transform.SetParent(go.transform, false);
                    Assert.AreEqual(closed, ClosedTargets.IsClosed(Corner, child.transform), kind + "'s part");
                }
                // The shelf's slots: only the potato.
                var shelfGo = new GameObject("Shelf slots");
                made.Add(shelfGo);
                Station shelf = shelfGo.AddComponent<Station>();
                Set(shelf, "_kind", StationKind.Shelf);
                for (int option = 0; option < StockLayout.Shelf.Count; option++)
                {
                    var slotGo = new GameObject("slot " + option);
                    slotGo.transform.SetParent(shelfGo.transform, false);
                    StockSlot slot = slotGo.AddComponent<StockSlot>();
                    Set(slot, "_station", shelf);
                    Set(slot, "_option", option);
                    bool potato = StockLayout.Shelf[option] == "potato";
                    Assert.AreEqual(!potato, ClosedTargets.IsClosed(Corner, slot), StockLayout.Shelf[option]);
                    Assert.AreEqual(!potato, ClosedTargets.IsClosed(Corner, shelf, option), StockLayout.Shelf[option] + " by option");
                }
                // The register: the card terminal only.
                var tillGo = new GameObject("Register");
                made.Add(tillGo);
                Station till = tillGo.AddComponent<Station>();
                Set(till, "_kind", StationKind.Register);
                Assert.IsTrue(ClosedTargets.IsClosed(Corner, till, RegisterRules.CardOption));
                Assert.IsFalse(ClosedTargets.IsClosed(Corner, till, RegisterRules.GiveChangeOption));
                Assert.IsFalse(ClosedTargets.IsClosed(Corner, till), "the register as a whole stays open (the cash tray)");
                // A burner and a cup spot belong to closed stations wherever they hang.
                var burnerGo = new GameObject("Burner");
                made.Add(burnerGo);
                PlaceSurface burner = burnerGo.AddComponent<PlaceSurface>();
                Set(burner, "_isBurner", true);
                Assert.IsTrue(ClosedTargets.IsClosed(Corner, burner));
                var spotGo = new GameObject("Cup spot");
                made.Add(spotGo);
                PlaceSurface spot = spotGo.AddComponent<PlaceSurface>();
                Set(spot, "_isCupSpot", true);
                Assert.IsTrue(ClosedTargets.IsClosed(Corner, spot));
                var topGo = new GameObject("Worktop");
                made.Add(topGo);
                Assert.IsFalse(ClosedTargets.IsClosed(Corner, topGo.AddComponent<PlaceSurface>()), "a free worktop stays open");
            }
            finally
            {
                foreach (GameObject go in made) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheCardTerminal_RefusesWithNotOpenYet_AndChangesNothing()
        {
            var desk = new RegisterDesk { AcceptsCard = false };
            desk.Open(new RegisterPayment { GuestId = 5, Payee = 1, Bill = 300, Card = true });
            var wallet = new CountingWallet();
            Assert.AreEqual(CookOutcome.NotOpenYet, desk.Use(1, RegisterRules.CardOption, wallet, out _));
            Assert.AreEqual(0, wallet.Calls, "nothing paid");
            Assert.IsTrue(desk.IsWaiting(5), "the guest still waits");
            desk.AcceptsCard = true;
            Assert.AreEqual(CookOutcome.Ok, desk.Use(1, RegisterRules.CardOption, wallet, out _), "the open kitchen takes cards");
            Assert.AreEqual(1, wallet.Calls);
        }

        private sealed class CountingWallet : IRegisterWallet
        {
            public int Calls;

            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                Calls++;
                paid = amount;
                return CookOutcome.Ok;
            }
        }

        [Test]
        public void StageOneGuests_DrawTheSamePatience_ButOrderFriesToGoInCash()
        {
            var open = new SeededRandom(CustomerRules.OrderSeed);
            var corner = new SeededRandom(CustomerRules.OrderSeed);
            bool anyCard = false, anyOther = false;
            for (int guest = 0; guest < 40; guest++)
            {
                MenuDish openDish = Menu.Draw(open, "snack", Menu.CrateIngredients);
                GuestOrder openOrder = GuestOrders.Draw(open, null);
                MenuDish dish = Menu.Draw(corner, "snack", Menu.CrateIngredients);
                GuestOrder drawn = GuestOrders.Draw(corner, null);
                string drink = "lemonade";
                bool eatIn = true;
                GuestOrder order = FoodCornerRules.StageOrder(Corner, drawn, ref dish, ref drink, ref eatIn);
                Assert.AreEqual(openOrder.PatienceBaseSeconds, order.PatienceBaseSeconds, "guest " + guest + ": the same patience");
                Assert.AreEqual(openOrder.Note, order.Note, "the same note");
                Assert.AreEqual(Menu.Fries, dish.Id, "always fries");
                Assert.IsFalse(order.PaysByCard, "cash");
                Assert.IsFalse(eatIn, "to go");
                Assert.IsNull(drink, "no drink (the dispenser is closed)");
                CollectionAssert.IsEmpty(order.Modifiers);
                anyCard |= openOrder.PaysByCard;
                anyOther |= openDish.Id != Menu.Fries;
            }
            Assert.IsTrue(anyCard && anyOther, "the open kitchen's guests do differ (card payers, other dishes)");
            MenuDish keep = Menu.ById(Menu.FalafelWrap);
            string keepDrink = "lemonade";
            bool keepEatIn = true;
            GuestOrder same = GuestOrders.Draw(new SeededRandom(1), null);
            Assert.AreSame(same, FoodCornerRules.StageOrder(ShopStageRules.OpenKitchen, same, ref keep, ref keepDrink, ref keepEatIn));
            Assert.AreEqual(Menu.FalafelWrap, keep.Id, "the open kitchen keeps its draws");
            Assert.IsTrue(FoodCornerRules.WantsMarket(Corner, "snack"));
            Assert.IsFalse(FoodCornerRules.WantsMarket(Corner, "meal"), "stage 1 sends only the fries' market");
            Assert.IsTrue(FoodCornerRules.WantsMarket(ShopStageRules.OpenKitchen, "meal"));
        }
    }
}
