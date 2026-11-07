using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 018 AC4: the first-person target from the hits of one camera-centre ray.</summary>
    public class AimRulesTests
    {
        private static readonly Vector3 Player = Vector3.zero;
        private static readonly Vector3 Near = new Vector3(0f, 0.5f, 1.5f);   // 1.58 m from the feet
        private static readonly Vector3 Near2 = new Vector3(0.5f, 0.5f, 2f);  // 2.12 m
        private static readonly Vector3 Far = new Vector3(0f, 0.5f, 2.6f);    // 2.65 m, outside 2.5 m

        private static int Pick(params AimHit[] hits) => AimRules.Pick(new List<AimHit>(hits), Player);

        [Test]
        public void NoHits_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick());
            Assert.AreEqual(AimRules.None, AimRules.Pick(null, Player));
        }

        [Test]
        public void ClosestHitWins()
        {
            // RaycastAll does not sort its hits: the order in the list must not matter.
            Assert.AreEqual(1, Pick(new AimHit(2f, AimKind.Station, Near2), new AimHit(1.2f, AimKind.Station, Near)));
            Assert.AreEqual(0, Pick(new AimHit(1.2f, AimKind.Pickup, Near), new AimHit(2f, AimKind.Station, Near2)));
        }

        [Test]
        public void WallInFrontOfStation_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(new AimHit(1.8f, AimKind.Station, Near), new AimHit(0.9f, AimKind.Other, Vector3.zero)));
        }

        [Test]
        public void OnlyAWall_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(new AimHit(1f, AimKind.Other, Vector3.zero)));
        }

        [Test]
        public void StationOutOfReach_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(new AimHit(2.2f, AimKind.Station, Far)));
        }

        [Test]
        public void PickupOutOfReach_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(new AimHit(2.2f, AimKind.Pickup, Far)));
        }

        [Test]
        public void ReachIsTheServerRule()
        {
            var edge = new Vector3(0f, 0f, PickupRules.MaxReach);
            Assert.AreEqual(0, Pick(new AimHit(2f, AimKind.Pickup, edge)), "2.5 m is inclusive, like the server");
        }

        [Test]
        public void PickupBehindStation_GivesTheStation()
        {
            Assert.AreEqual(0, Pick(new AimHit(1.2f, AimKind.Station, Near), new AimHit(1.6f, AimKind.Pickup, Near2)));
        }

        [Test]
        public void StationInReach_IsTheTarget()
        {
            Assert.AreEqual(0, Pick(new AimHit(1.2f, AimKind.Station, Near)));
        }

        // ---- Spec 019 AC4: buttons on station fronts ----

        [Test]
        public void ButtonHitBeforeItsStation_GivesTheButton()
        {
            var hits = new List<AimHit>
            {
                new AimHit(1.25f, AimKind.Station, Near),
                AimHit.Button(1.2f, Near, 7, 2)
            };
            int picked = AimRules.Pick(hits, Player);
            Assert.AreEqual(1, picked);
            Assert.AreEqual(AimKind.Button, hits[picked].Kind);
            Assert.AreEqual(7, hits[picked].StationIndex);
            Assert.AreEqual(2, hits[picked].Option);
        }

        [Test]
        public void ButtonAndStationAtTheSameDistance_TheButtonWins()
        {
            Assert.AreEqual(1, Pick(new AimHit(1.2f, AimKind.Station, Near), AimHit.Button(1.2f, Near, 7, 1)));
            Assert.AreEqual(0, Pick(AimHit.Button(1.2f, Near, 7, 1), new AimHit(1.2f, AimKind.Station, Near)));
        }

        [Test]
        public void ButtonOutOfReach_NoTarget()
        {
            // Reach is measured to the button's station, like the server measures it.
            Assert.AreEqual(AimRules.None, Pick(AimHit.Button(2.2f, Far, 7, 1)));
        }

        [Test]
        public void WallInFrontOfAButton_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(AimHit.Button(1.2f, Near, 7, 1), new AimHit(0.8f, AimKind.Other, Vector3.zero)));
        }

        [Test]
        public void NonButtonHits_CarryNoStationOrOption()
        {
            var hit = new AimHit(1f, AimKind.Station, Near);
            Assert.AreEqual(-1, hit.StationIndex);
            Assert.AreEqual(0, hit.Option);
        }

        [Test]
        public void BadDistances_AreIgnored()
        {
            Assert.AreEqual(1, Pick(new AimHit(float.NaN, AimKind.Other, Vector3.zero), new AimHit(1.2f, AimKind.Station, Near)));
            Assert.AreEqual(1, Pick(new AimHit(-1f, AimKind.Other, Vector3.zero), new AimHit(1.2f, AimKind.Station, Near)));
        }

        // ---- spec 020: spots on place surfaces ----

        [Test]
        public void Spot_IsATarget_CarryingItsSurfaceAndSpot()
        {
            AimHit spot = AimHit.Spot(1.3f, Near, 2, 5);
            Assert.AreEqual(0, Pick(spot));
            Assert.AreEqual(AimKind.Spot, spot.Kind);
            Assert.AreEqual(2, spot.SurfaceIndex);
            Assert.AreEqual(5, spot.SpotIndex);
            Assert.AreEqual(-1, spot.StationIndex);
            Assert.AreEqual(-1, new AimHit(1f, AimKind.Station, Near).SurfaceIndex, "other hits carry no surface");
        }

        [Test]
        public void SpotOutOfReach_NoTarget()
        {
            Assert.AreEqual(AimRules.None, Pick(AimHit.Spot(2.2f, Far, 0, 1)));
        }

        [Test]
        public void ButtonStillWinsOverItsStation_AndOverASpot()
        {
            Assert.AreEqual(1, Pick(new AimHit(1.2f, AimKind.Station, Near), AimHit.Button(1.2f, Near, 7, 1)));
            Assert.AreEqual(1, Pick(AimHit.Spot(1.2f, Near, 0, 1), AimHit.Button(1.2f, Near, 7, 1)));
        }

        [Test]
        public void SpotInFrontOfItsStationBody_Wins()
        {
            // The board station's free half: its thin collider sits just above the station body.
            Assert.AreEqual(0, Pick(AimHit.Spot(1.18f, Near, 0, 3), new AimHit(1.2f, AimKind.Station, Near)));
        }

        [Test]
        public void FullHands_PickupsDoNotBlock_SoTheSpotBehindIsFound()
        {
            var hits = new List<AimHit> { new AimHit(1.1f, AimKind.Pickup, Near), AimHit.Spot(1.2f, Near, 1, 4) };
            Assert.AreEqual(0, AimRules.Pick(hits, Player, pickupsTransparent: false), "empty hands: the item");
            Assert.AreEqual(1, AimRules.Pick(hits, Player, pickupsTransparent: true), "full hands: the spot behind it");
            Assert.AreEqual(AimRules.None, AimRules.Pick(new List<AimHit> { new AimHit(1.1f, AimKind.Pickup, Near) }, Player, true));
        }

        [Test]
        public void OutOfReach_IsReported_SoItCanSayTooFar()
        {
            var hits = new List<AimHit> { new AimHit(2.6f, AimKind.Station, Far) };
            Assert.AreEqual(AimRules.None, AimRules.Pick(hits, Player, false, out int far));
            Assert.AreEqual(0, far);
            Assert.AreEqual(AimRules.None, AimRules.Pick(new List<AimHit> { AimHit.Spot(2.5f, Far, 1, 2) }, Player, true, out far));
            Assert.AreEqual(0, far, "a spot too");
            Assert.AreEqual(AimRules.None, AimRules.Pick(new List<AimHit> { AimHit.Button(2.5f, Far, 3, 1) }, Player, false, out far));
            Assert.AreEqual(0, far, "a button too");
        }

        [Test]
        public void InReachOrWalls_AreNotOutOfReach()
        {
            Assert.AreEqual(0, AimRules.Pick(new List<AimHit> { new AimHit(1.2f, AimKind.Station, Near) }, Player, false, out int far));
            Assert.AreEqual(AimRules.None, far);
            var wall = new List<AimHit> { new AimHit(1f, AimKind.Other, Vector3.zero), new AimHit(2.6f, AimKind.Station, Far) };
            Assert.AreEqual(AimRules.None, AimRules.Pick(wall, Player, false, out far));
            Assert.AreEqual(AimRules.None, far, "behind a wall is not 'too far', it is hidden");
            Assert.AreEqual(AimRules.None, AimRules.Pick(new List<AimHit> { new AimHit(2.6f, AimKind.Pickup, Far) }, Player, true, out far));
            Assert.AreEqual(AimRules.None, far, "full hands ignore items");
        }

        [Test]
        public void FullHands_WallsStillBlock()
        {
            var hits = new List<AimHit> { new AimHit(0.9f, AimKind.Other, Vector3.zero), AimHit.Spot(1.2f, Near, 1, 4) };
            Assert.AreEqual(AimRules.None, AimRules.Pick(hits, Player, pickupsTransparent: true));
        }
    }
}
