using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Dev;
using Worktown.Game.Economy;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 028b AC2: taking from a fridge or shelf slot is the crate's take rule (StockRules, the server's only path): the
    /// slot must exist on that station (wrong slot index and unknown options refused), reach is measured to the slot's anchor,
    /// the hands must be free, the grocer must be paid (no money, no ingredient). Plus the layout table (every ingredient in
    /// exactly one slot) and the stock shapes the art and the tours share.
    /// </summary>
    public class StockRulesTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _made)
                if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        private static void Set(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            Assert.IsNotNull(p, field);
            switch (value)
            {
                case int i: p.intValue = i; break;
                case Object o: p.objectReferenceValue = o; break;
                case Object[] list:
                    p.arraySize = list.Length;
                    for (int n = 0; n < list.Length; n++) p.GetArrayElementAtIndex(n).objectReferenceValue = list[n];
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A station of <paramref name="kind"/> at <paramref name="at"/>; a stock station gets its StockLayout slots, slot n at
        /// <paramref name="at"/> + n x <paramref name="step"/>.</summary>
        private Station MakeStation(StationKind kind, Vector3 at, Vector3 step, int crateIngredient = 0)
        {
            var go = new GameObject(kind.ToString());
            _made.Add(go);
            go.transform.position = at;
            var station = go.AddComponent<Station>();
            Set(station, "_kind", (int)kind);
            Set(station, "_ingredientIndex", crateIngredient);
            IReadOnlyList<string> ids = StockLayout.SlotsOf(kind);
            var slots = new Object[ids.Count];
            for (int n = 0; n < ids.Count; n++)
            {
                var child = new GameObject("Slot " + n);
                child.transform.SetParent(go.transform, false);
                child.transform.position = at + step * n;
                var slot = child.AddComponent<StockSlot>();
                Set(slot, "_station", station);
                Set(slot, "_option", n);
                Set(slot, "_ingredientIndex", StockLayout.IngredientIndexOf(ids[n]));
                slots[n] = slot;
            }
            if (ids.Count > 0) Set(station, "_slots", slots);
            return station;
        }

        // ---- the layout ----

        [Test]
        public void EveryIngredient_StandsInExactlyOneSlot()
        {
            var seen = new Dictionary<string, int>();
            foreach (StationKind kind in new[] { StationKind.Fridge, StationKind.Shelf })
                foreach (string id in StockLayout.SlotsOf(kind))
                    seen[id] = seen.TryGetValue(id, out int n) ? n + 1 : 1;
            foreach (var item in DemoIngredients.All)
            {
                Assert.IsTrue(seen.TryGetValue(item.Id, out int count), item.Id + " is in no slot");
                Assert.AreEqual(1, count, item.Id + " is in more than one slot");
            }
            Assert.AreEqual(DemoIngredients.All.Count, seen.Count, "no slot holds something that is no ingredient");
        }

        [Test]
        public void Slots_FitTheRowsAndColumns_ColdInTheFridge_DryOnTheShelf()
        {
            Assert.AreEqual(StockLayout.FridgeRows * StockLayout.FridgeColumns, StockLayout.Fridge.Count, "2 shelves of 4");
            Assert.LessOrEqual(StockLayout.Shelf.Count, StockLayout.ShelfRows * StockLayout.ShelfColumns);
            Assert.AreEqual(3, StockLayout.ShelfRows, "3 boards");
            foreach (string cold in new[] { "lettuce", "cucumber", "red_cabbage", "seitan", "falafel_mix", "lemon" })
                CollectionAssert.Contains(StockLayout.Fridge, cold);
            foreach (string dry in new[] { "flatbread", "wrap", "garlic_herb_sauce", "chili_sauce", "potato", "onion" })
                CollectionAssert.Contains(StockLayout.Shelf, dry);
            Assert.IsTrue(StockLayout.IsStock(StationKind.Fridge) && StockLayout.IsStock(StationKind.Shelf));
            Assert.IsFalse(StockLayout.IsStock(StationKind.Crate) || StockLayout.IsStock(StationKind.Stove));
            Assert.IsEmpty(StockLayout.SlotsOf(StationKind.Counter));
            Assert.AreEqual(-1, StockLayout.IngredientIndexOf("cheese"));
        }

        [Test]
        public void StandPoints_AreInsideTheCabinet_RowByRowFromTheTop_LeftToRight()
        {
            foreach (StationKind kind in new[] { StationKind.Fridge, StationKind.Shelf })
            {
                int columns = StockLayout.Columns(kind);
                for (int s = 0; s < StockLayout.SlotsOf(kind).Count; s++)
                {
                    Vector3 p = StockArt.StandPoint(kind, s);
                    Assert.That(Mathf.Abs(p.x), Is.LessThan(StockArt.InnerWidth * 0.5f), kind + " slot " + s + " x");
                    Assert.That(p.y, Is.InRange(0.3f, ArtSizeTable.StockHeight - 0.3f), kind + " slot " + s + " y");
                    Assert.That(p.z, Is.InRange(0.05f, StockArt.DepthOf(kind) - 0.1f), kind + " slot " + s + " z");
                    if (s % columns > 0) Assert.Greater(p.x, StockArt.StandPoint(kind, s - 1).x, "left to right");
                    if (s >= columns) Assert.Less(p.y, StockArt.StandPoint(kind, s - columns).y, "top row first");
                }
                Assert.AreEqual(Vector3.zero, StockArt.StandPoint(kind, -1));
                Assert.AreEqual(Vector3.zero, StockArt.StandPoint(kind, 99));
            }
            Assert.AreEqual(1, StockArt.SamplesPerSlot(StationKind.Fridge, 0.2f));
            Assert.AreEqual(2, StockArt.SamplesPerSlot(StationKind.Fridge, 0.105f));
            Assert.AreEqual(2, StockArt.SamplesPerSlot(StationKind.Shelf, 0.12f)); // spec 030: three columns on the shelf (0.37 m slots)
            Assert.AreEqual(3, StockArt.SamplesPerSlot(StationKind.Shelf, 0.09f));
        }

        // ---- AC2: the server's take rule ----

        [Test]
        public void SlotIndex_IsTheOption_WrongSlotsAndUnknownOptionsAreRefused()
        {
            Station fridge = MakeStation(StationKind.Fridge, Vector3.zero, Vector3.right * 0.28f);
            Station shelf = MakeStation(StationKind.Shelf, Vector3.zero, Vector3.right * 0.56f);
            for (int s = 0; s < StockLayout.Fridge.Count; s++)
            {
                Assert.IsTrue(StockRules.TryResolve(fridge, s, out int ingredient, out Vector3 anchor), "fridge slot " + s);
                Assert.AreEqual(StockLayout.Fridge[s], DemoIngredients.All[ingredient].Id);
                Assert.AreEqual(Vector3.right * 0.28f * s, anchor);
            }
            // Spec 030: slot 8 exists on the shelf (9 slots) but not in the fridge (8): the wrong slot index for that station.
            Assert.IsTrue(StockRules.TryResolve(shelf, 8, out _, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, StockRules.CheckTake(fridge, 8, Vector3.zero, false, out string none));
            Assert.IsNull(none);
            foreach (int unknown in new[] { -1, 8, 99, int.MinValue, int.MaxValue })
                Assert.AreEqual(CookOutcome.InvalidOption, StockRules.CheckTake(fridge, unknown, Vector3.zero, false, out _), "option " + unknown);
            // A station without stock gives nothing; a crate only knows option 0.
            Station stove = MakeStation(StationKind.Stove, Vector3.zero, Vector3.zero);
            Assert.IsFalse(StockRules.TryResolve(stove, 0, out _, out _));
            Assert.IsFalse(StockRules.TryResolve(null, 0, out _, out _));
            Station crate = MakeStation(StationKind.Crate, Vector3.zero, Vector3.zero, crateIngredient: 3);
            Assert.AreEqual(CookOutcome.Ok, StockRules.CheckTake(crate, 0, Vector3.zero, false, out string onion));
            Assert.AreEqual("onion", onion);
            Assert.AreEqual(CookOutcome.InvalidOption, StockRules.CheckTake(crate, 1, Vector3.zero, false, out _));
        }

        [Test]
        public void Reach_IsMeasuredToTheSlotsAnchor_NotTheStationsCentre()
        {
            // Slot 7 of the fridge stands 2.1 m right of the station's centre.
            Station fridge = MakeStation(StationKind.Fridge, Vector3.zero, Vector3.right * 0.3f);
            var leftOfCentre = new Vector3(-1.0f, 0f, 0f);   // 1.0 m from the centre, 3.1 m from slot 7
            var rightOfSlot = new Vector3(4.4f, 0f, 0f);     // 4.4 m from the centre, 2.3 m from slot 7
            Assert.AreEqual(CookOutcome.TooFar, StockRules.CheckTake(fridge, 7, leftOfCentre, false, out _));
            Assert.AreEqual(CookOutcome.Ok, StockRules.CheckTake(fridge, 7, rightOfSlot, false, out string apple));
            Assert.AreEqual(StockLayout.Fridge[7], apple);
            Assert.AreEqual(CookOutcome.Ok, StockRules.CheckTake(fridge, 0, leftOfCentre, false, out _), "slot 0 is at the centre");
        }

        [Test]
        public void TakingFromASlot_IsExactlyLikeACrate_ReachHandsAndIngredient()
        {
            // The same ingredient in a crate and in a slot at the same place: every check gives the same answer.
            int potato = StockLayout.IngredientIndexOf("potato");
            int slotOfPotato = -1;
            for (int s = 0; s < StockLayout.Shelf.Count; s++)
                if (StockLayout.Shelf[s] == "potato") slotOfPotato = s;
            var at = new Vector3(5f, 0.9f, 3f);
            Station crate = MakeStation(StationKind.Crate, at, Vector3.zero, crateIngredient: potato);
            Station shelf = MakeStation(StationKind.Shelf, at - Vector3.right * slotOfPotato, Vector3.right);
            Assert.IsTrue(StockRules.TryResolve(shelf, slotOfPotato, out _, out Vector3 anchor));
            Assert.AreEqual(at, anchor);
            foreach (Vector3 player in new[] { at, at + Vector3.back * 2.4f, at + Vector3.back * 2.6f, at + new Vector3(1.5f, -0.9f, -1.9f) })
                foreach (bool handsFull in new[] { false, true })
                {
                    CookOutcome fromCrate = StockRules.CheckTake(crate, 0, player, handsFull, out string a);
                    CookOutcome fromSlot = StockRules.CheckTake(shelf, slotOfPotato, player, handsFull, out string b);
                    Assert.AreEqual(fromCrate, fromSlot, "player at " + player + ", hands full " + handsFull);
                    Assert.AreEqual(a, b);
                }
            Assert.AreEqual(CookOutcome.HandsFull, StockRules.CheckTake(shelf, slotOfPotato, at, true, out _));
            Assert.AreEqual(CookOutcome.TooFar, StockRules.CheckTake(shelf, slotOfPotato, at + Vector3.back * 2.6f, false, out _));
            Assert.AreEqual(CookOutcome.Ok, StockRules.CheckTake(shelf, slotOfPotato, at + Vector3.back * 2.4f, false, out string id));
            Assert.AreEqual("potato", id);
            // An index outside the ingredient list is refused like a broken crate.
            Assert.AreEqual(CookOutcome.UnknownStation, StockRules.CheckTake(true, DemoIngredients.All.Count, at, at, false));
            Assert.AreEqual(CookOutcome.UnknownStation, StockRules.CheckTake(true, -1, at, at, false));
        }

        [Test]
        public void Money_NoMoneyNoIngredient_TheGrocerIsPaidThePrice()
        {
            Assert.AreEqual(CookOutcome.Ok, StockRules.PaidOutcome(EconomyError.None));
            Assert.AreEqual(CookOutcome.NotEnoughMoney, StockRules.PaidOutcome(EconomyError.InsufficientFunds));
            Assert.AreEqual(CookOutcome.SimRefused, StockRules.PaidOutcome(EconomyError.UnknownBaseItem));
            Assert.AreEqual(CookOutcome.SimRefused, StockRules.PaidOutcome(EconomyError.UnknownAccount));

            // The real grocer (EconomyService.TryBuy calls this): every slot's ingredient has a price and is paid for it.
            TownEconomy economy = TownEconomy.CreateDemo();
            string rich = EconomyRules.AccountId(1), broke = EconomyRules.AccountId(2);
            Assert.IsTrue(economy.TryOpenAccount(rich, 10000));
            Assert.IsTrue(economy.TryOpenAccount(broke, 10));
            foreach (StationKind kind in new[] { StationKind.Fridge, StationKind.Shelf })
                foreach (string id in StockLayout.SlotsOf(kind))
                {
                    Assert.IsTrue(economy.TryGetGrocerPrice(id, out long price), id + " has a grocer price");
                    long before = economy.BalanceOf(rich);
                    Assert.AreEqual(CookOutcome.Ok, StockRules.PaidOutcome(economy.TryBuyBase(rich, id, out long cost)), id);
                    Assert.AreEqual(price, cost, id);
                    Assert.AreEqual(before - price, economy.BalanceOf(rich), id);
                    Assert.AreEqual(CookOutcome.NotEnoughMoney, StockRules.PaidOutcome(economy.TryBuyBase(broke, id, out _)), id + " without money");
                    Assert.AreEqual(10, economy.BalanceOf(broke), "a refused take costs nothing");
                }
        }

        // ---- the tours aim where the builder puts the slots ----

        [Test]
        public void TourSlotPoints_AreOnTheirStations()
        {
            Vector3 cucumber = TourRules.SlotPoint(StationKind.Fridge, "cucumber");
            Vector3 potato = TourRules.SlotPoint(StationKind.Shelf, "potato");
            Assert.AreEqual(TourRules.FridgeX, cucumber.x, StockArt.Width * 0.5f);
            Assert.AreEqual(TourRules.StockShelfX, potato.x, StockArt.Width * 0.5f);
            Assert.Greater(cucumber.y, potato.y, "the cucumber is on the fridge's top shelf, the potato on the shelf's middle board");
            Assert.Throws<System.ArgumentException>(() => TourRules.SlotPoint(StationKind.Fridge, "potato"));
        }
    }
}
