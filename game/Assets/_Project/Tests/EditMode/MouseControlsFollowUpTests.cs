using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 041 follow-ups (2026-11-11): the right click's ray put-down stops at a station's body (AimRules.BlocksRayPutDown), the shared
    /// hit sorting (AimHitSort), and a wheel turn that would lay the item over an occupied neighbour spot is refused by the server's
    /// place plan (PickupService.PlanPlace, PlaceTurn.TurnOverlaps).
    /// </summary>
    public class MouseControlsFollowUpTests
    {
        private const int Surface = (1 << Worktown.Game.Layout.SurfaceId.LocalBits) | 2; // spec 062a: a surface id (piece 1, local 2)

        [Test]
        public void RightClickRay_StopsAtAStationsBody_ButNotAtItsOwnTopOrAtCookware()
        {
            const float top = 1.3f;
            Assert.IsTrue(AimRules.BlocksRayPutDown(AimKind.Station, 1.0f, top, false), "a station's body in front: no putting down behind it");
            Assert.IsTrue(AimRules.BlocksRayPutDown(AimKind.Button, 1.0f, top, false), "a knob on a station's front, too");
            Assert.IsTrue(AimRules.BlocksRayPutDown(AimKind.Other, 1.0f, top, false), "a wall, as before (spec 041)");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Station, 1.0f, top, true), "the station's own top or cup spot");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Vessel, 1.0f, top, false), "Q2: aiming at the pot puts it down next to it");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Pickup, 1.0f, top, false), "an item does not block");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Spot, 1.0f, top, false), "another top does not block");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Station, 1.5f, top, false), "a station behind the top does not block");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Station, top, top, false), "the same distance: not in front");
            Assert.IsFalse(AimRules.BlocksRayPutDown(AimKind.Station, float.NaN, top, false));
            Assert.IsTrue(AimRules.BlocksRayPutDown(AimKind.Station, 1.0f, float.PositiveInfinity, false),
                "no surface found yet (infinite distance): it lies in front of everything; nothing is put down anyway");
        }

        [Test]
        public void AimHitSort_SurfaceBeforeItsStation_StationBody_AndOther()
        {
            var stationGo = new GameObject("Station under test");
            try
            {
                Station station = stationGo.AddComponent<Station>();
                var body = new GameObject("Body");
                body.transform.SetParent(stationGo.transform, false);
                BoxCollider bodyCollider = body.AddComponent<BoxCollider>();
                var top = new GameObject("Top");
                top.transform.SetParent(stationGo.transform, false);
                BoxCollider topCollider = top.AddComponent<BoxCollider>();
                PlaceSurface surface = top.AddComponent<PlaceSurface>();
                var wall = new GameObject("Wall");
                wall.transform.SetParent(stationGo.transform.parent, false);
                BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
                try
                {
                    Assert.AreEqual(AimHitClass.Surface, AimHitSort.Classify(topCollider, out Component a));
                    Assert.AreSame(surface, a, "a top on a station is the top, checked before its station");
                    Assert.AreEqual(AimHitClass.Station, AimHitSort.Classify(bodyCollider, out Component b));
                    Assert.AreSame(station, b);
                    Assert.AreEqual(AimHitClass.Other, AimHitSort.Classify(wallCollider, out Component c));
                    Assert.IsNull(c);
                    Assert.AreEqual(AimHitClass.Other, AimHitSort.Classify(null, out _));
                    Assert.IsNull(AimHitSort.SlotAt(station, Vector3.zero), "no slots: the station itself");
                    Assert.IsTrue(AimHitSort.SlotFollowsTheRay(StationKind.DrinkDispenser));
                    Assert.IsTrue(AimHitSort.SlotFollowsTheRay(StationKind.Assembly));
                    Assert.IsTrue(AimHitSort.SlotFollowsTheRay(StationKind.Register));
                    Assert.IsFalse(AimHitSort.SlotFollowsTheRay(StationKind.Fridge), "the fridge: the slot nearest the hit");
                }
                finally { Object.DestroyImmediate(wall); }
            }
            finally { Object.DestroyImmediate(stationGo); }
        }

        [Test]
        public void Footprint_OfAFlatKnife_TurnsWithTheWheel()
        {
            var half = new Vector3(0.02f, 0.15f, 0.01f); // long along its local Y; lying flat it points along the surface's depth
            Quaternion yaw = Quaternion.Euler(0f, 30f, 0f);
            Quaternion rest = yaw * Quaternion.Euler(90f, 0f, 0f);
            Vector2 plain = PlaceTurn.Footprint(half, rest, yaw);
            Assert.AreEqual(0.02f, plain.x, 1e-4f);
            Assert.AreEqual(0.15f, plain.y, 1e-4f);
            Vector2 quarter = PlaceTurn.Footprint(half, PlaceTurn.Apply(rest, 2), yaw);
            Assert.AreEqual(0.15f, quarter.x, 1e-4f, "two steps: along the width");
            Assert.AreEqual(0.02f, quarter.y, 1e-4f);
            Vector2 half8 = PlaceTurn.Footprint(half, PlaceTurn.Apply(rest, 4), yaw);
            Assert.AreEqual(plain.x, half8.x, 1e-4f, "a half turn: the same footprint");
            Assert.IsTrue(PlaceTurn.Reaches(new Vector2(0.15f, 0.02f), 1, 0));
            Assert.IsFalse(PlaceTurn.Reaches(new Vector2(0.15f, 0.02f), 0, 1));
            Assert.IsFalse(PlaceTurn.Reaches(new Vector2(0.15f, 0.02f), 1, 1), "a diagonal needs both");
            Assert.IsFalse(PlaceTurn.Reaches(new Vector2(PlaceSpots.Spacing * 0.5f, 0f), 1, 0), "up to the half-way line (and its slack) is fine");
        }

        /// <summary>
        /// The server's place plan with the item's footprint: a knife turned across (two steps) next to an occupied spot is refused (Taken)
        /// with no pose; unturned, half turned, or next to a free spot it is placed. What the unturned knife already reaches stays allowed.
        /// </summary>
        [Test]
        public void PlanPlace_RefusesATurn_ThatLaysTheItemOverAnOccupiedNeighbour()
        {
            var go = new GameObject("Worktop under test");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
                var surface = go.AddComponent<PlaceSurface>();
                var so = new UnityEditor.SerializedObject(surface);
                TestPieces.Attach(go, surface, Surface); // spec 062a: the surface is part of a piece
                so.FindProperty("_size").vector2Value = new Vector2(1f, 0.6f); // 4 columns, 2 rows
                so.FindProperty("_topY").floatValue = 0.9f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(8, surface.SpotCount);
                Vector3 player = surface.SpotWorldPosition(1) + go.transform.rotation * new Vector3(0f, -0.9f, -0.8f);
                Quaternion tilt = Quaternion.Euler(90f, 0f, 0f); // a knife lies flat, its long side along the depth
                var knife = new Vector3(0.02f, 0.15f, 0.01f);
                bool NothingThere(Vector3 at) => false;

                var spots = new PlaceSpots();
                Assert.IsTrue(spots.TryTake(Surface, 2, 77), "the spot right of spot 1 is taken");
                Assert.AreEqual(PickupOutcome.Taken, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false,
                    tilt, 0.02f, knife, NothingThere, out _, out Pose refused), "turned across, it would lie over spot 2");
                Assert.AreEqual(default(Pose), refused, "nothing to move");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 0), player, false,
                    tilt, 0.02f, knife, NothingThere, out _, out _), "unturned it fits");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 4), player, false,
                    tilt, 0.02f, knife, NothingThere, out _, out _), "a half turn has the unturned footprint");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false,
                    tilt, 0.02f, Vector3.zero, NothingThere, out _, out _), "no footprint given: the old plan");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false,
                    tilt, 0.02f, NothingThere, out _, out _), "the old overload: no footprint check");

                var behind = new PlaceSpots();
                Assert.IsTrue(behind.TryTake(Surface, 5, 78), "the spot behind spot 1 is taken (the unturned knife reaches it already)");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(behind, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false,
                    tilt, 0.02f, knife, NothingThere, out _, out _), "turned across it leaves spot 5");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(behind, true, Surface, surface, PlaceTurn.Pack(1, 0), player, false,
                    tilt, 0.02f, knife, NothingThere, out _, out _), "unturned stays as before");

                Vector3 spotZero = surface.SpotWorldPosition(0);
                bool ItemOnSpotZero(Vector3 at) => Vector3.Distance(at, spotZero) < 0.01f;
                Assert.AreEqual(PickupOutcome.Taken, PickupService.PlanPlace(new PlaceSpots(), true, Surface, surface, PlaceTurn.Pack(1, 2), player,
                    false, tilt, 0.02f, knife, ItemOnSpotZero, out _, out _), "an item the probe finds on the left neighbour counts too");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// 054 round (041 review nits): a box off the item's pivot reaches further on one side than on the other, and the place plan
        /// checks it where it lies; a half turn is never refused, by construction (PlaceTurn.IsHalfTurn), even when the box is off the pivot.
        /// </summary>
        [Test]
        public void Footprint_OffThePivot_IsCheckedWhereTheBoxLies_AndAHalfTurnIsNeverRefused()
        {
            Quaternion flat = Quaternion.identity;
            var half = new Vector3(0.05f, 0.02f, 0.05f);
            var center = new Vector3(0.12f, 0f, 0f); // the box sits 12 cm towards +X of the pivot
            PlaceTurn.Footprint(half, center, flat, flat, out Vector2 positive, out Vector2 negative);
            Assert.AreEqual(0.17f, positive.x, 1e-4f, "towards +X: the centre plus the half size");
            Assert.AreEqual(-0.07f, negative.x, 1e-4f, "towards -X it does not even reach the pivot");
            Assert.AreEqual(0.05f, positive.y, 1e-4f);
            Assert.AreEqual(0.05f, negative.y, 1e-4f);
            Assert.IsTrue(PlaceTurn.Reaches(positive, negative, 1, 0), "over the right neighbour");
            Assert.IsFalse(PlaceTurn.Reaches(positive, negative, -1, 0), "not over the left one");
            Assert.IsTrue(PlaceTurn.IsHalfTurn(4) && PlaceTurn.IsHalfTurn(12) && !PlaceTurn.IsHalfTurn(2) && !PlaceTurn.IsHalfTurn(0));

            var go = new GameObject("Worktop under test");
            try
            {
                var surface = go.AddComponent<PlaceSurface>();
                var so = new UnityEditor.SerializedObject(surface);
                TestPieces.Attach(go, surface, Surface); // spec 062a: the surface is part of a piece
                so.FindProperty("_size").vector2Value = new Vector2(1f, 0.6f); // 4 columns, 2 rows
                so.FindProperty("_topY").floatValue = 0.9f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Vector3 player = surface.SpotWorldPosition(1) + new Vector3(0f, -0.9f, -0.8f);
                bool NothingThere(Vector3 at) => false;
                var left = new PlaceSpots();
                Assert.IsTrue(left.TryTake(Surface, 0, 77), "the left neighbour of spot 1 is taken");
                var nothing = new Vector3(0.05f, 0.02f, 0.02f);
                var offset = new Vector3(0f, 0f, 0.15f); // the box sits 15 cm in front of the pivot (its local +Z)
                // Turned two steps (90 degrees about Y) the box's local +Z points to the surface's +X: away from the taken left spot.
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(left, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false,
                    Quaternion.identity, 0.02f, nothing, offset, NothingThere, out _, out _), "the box lies right of the pivot: spot 0 stays free");
                // Six steps (270 degrees) it points to -X, over the taken spot 0.
                Assert.AreEqual(PickupOutcome.Taken, PickupService.PlanPlace(left, true, Surface, surface, PlaceTurn.Pack(1, 6), player, false,
                    Quaternion.identity, 0.02f, nothing, offset, NothingThere, out _, out _), "the box lies left of the pivot, over spot 0");
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(left, true, Surface, surface, PlaceTurn.Pack(1, 4), player, false,
                    Quaternion.identity, 0.02f, nothing, offset, NothingThere, out _, out _), "a half turn is never refused");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
