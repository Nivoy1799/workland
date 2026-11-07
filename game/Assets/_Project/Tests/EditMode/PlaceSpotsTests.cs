using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 020 AC4: the spot grid on a surface top, the nearest free spot, taken and freed spots.</summary>
    public class PlaceSpotsTests
    {
        private static readonly Vector2 Top = new Vector2(1f, 0.6f);

        private static void Near(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-5f, "x");
            Assert.AreEqual(expected.y, actual.y, 1e-5f, "y");
        }

        [Test]
        public void Grid_OfAOneByPointSixTop_Is4x2()
        {
            Assert.AreEqual(4, PlaceSpots.Columns(Top));
            Assert.AreEqual(2, PlaceSpots.Rows(Top));
            Assert.AreEqual(8, PlaceSpots.Count(Top));
        }

        [Test]
        public void Positions_AreAQuarterMetreApart_CentredAndAwayFromTheEdges()
        {
            Near(new Vector2(-0.375f, -0.125f), PlaceSpots.Position(Top, 0));
            Near(new Vector2(-0.125f, -0.125f), PlaceSpots.Position(Top, 1));
            Near(new Vector2(0.375f, -0.125f), PlaceSpots.Position(Top, 3));
            Near(new Vector2(-0.375f, 0.125f), PlaceSpots.Position(Top, 4));
            Near(new Vector2(0.375f, 0.125f), PlaceSpots.Position(Top, 7));
            for (int i = 0; i < PlaceSpots.Count(Top); i++)
            {
                Vector2 p = PlaceSpots.Position(Top, i);
                Assert.GreaterOrEqual(Top.x * 0.5f - Mathf.Abs(p.x), PlaceSpots.EdgeMargin - 1e-5f, "spot " + i);
                Assert.GreaterOrEqual(Top.y * 0.5f - Mathf.Abs(p.y), PlaceSpots.EdgeMargin - 1e-5f, "spot " + i);
            }
        }

        [Test]
        public void SmallOrBadTops_HaveFewOrNoSpots()
        {
            Assert.AreEqual(1, PlaceSpots.CountAlong(0.24f), "exactly both margins: one spot in the middle");
            Assert.AreEqual(0, PlaceSpots.CountAlong(0.2f));
            Assert.AreEqual(0, PlaceSpots.CountAlong(float.NaN));
            Assert.AreEqual(2, PlaceSpots.CountAlong(0.49f));
            Assert.AreEqual(0, PlaceSpots.Count(new Vector2(1f, 0.1f)));
            Assert.IsFalse(PlaceSpots.IsValid(Top, -1));
            Assert.IsFalse(PlaceSpots.IsValid(Top, 8));
        }

        [Test]
        public void NearestFree_IsTheSpotClosestToTheHitPoint()
        {
            Assert.AreEqual(0, PlaceSpots.NearestFree(Top, new Vector2(-0.4f, -0.2f), null));
            Assert.AreEqual(6, PlaceSpots.NearestFree(Top, new Vector2(0.1f, 0.2f), null));
            Assert.AreEqual(3, PlaceSpots.NearestFree(Top, new Vector2(5f, -5f), null), "far outside: the nearest edge spot");
        }

        [Test]
        public void NearestFree_SkipsTakenSpots()
        {
            var taken = new bool[8];
            taken[6] = true;
            int spot = PlaceSpots.NearestFree(Top, new Vector2(0.1f, 0.2f), taken);
            Assert.AreNotEqual(6, spot);
            Assert.AreEqual(5, spot, "the next closest is the left neighbour at (-0.125, 0.125)");
        }

        [Test]
        public void FullSurface_GivesNone()
        {
            var taken = new bool[8];
            for (int i = 0; i < taken.Length; i++) taken[i] = true;
            Assert.AreEqual(PlaceSpots.None, PlaceSpots.NearestFree(Top, Vector2.zero, taken));
            Assert.AreEqual(PlaceSpots.None, PlaceSpots.NearestFree(new Vector2(0.1f, 0.1f), Vector2.zero, null), "no spots at all");
        }

        [Test]
        public void TakingAndFreeingASpot()
        {
            var spots = new PlaceSpots();
            Assert.IsTrue(spots.TryTake(1, 3, 42));
            Assert.IsTrue(spots.IsTaken(1, 3));
            Assert.IsFalse(spots.IsTaken(0, 3), "another surface");
            Assert.IsFalse(spots.TryTake(1, 3, 43), "one item per spot");
            Assert.IsTrue(spots.TryGetOccupant(1, 3, out int occupant));
            Assert.AreEqual(42, occupant);

            Assert.IsTrue(spots.Free(42));
            Assert.IsFalse(spots.IsTaken(1, 3));
            Assert.IsFalse(spots.Free(42), "already free");
            Assert.IsTrue(spots.TryTake(1, 3, 43));
        }

        private static void SameRotation(Quaternion expected, Quaternion actual, string message)
        {
            Assert.Less(Quaternion.Angle(expected, actual), 0.01f, message + ": " + expected.eulerAngles + " vs " + actual.eulerAngles);
        }

        [Test]
        public void RestRotation_KeepsTheTilt_YawFollowsTheSurface()
        {
            Quaternion lyingKnife = Quaternion.Euler(0f, 40f, 90f); // on its side, turned 40 degrees
            Quaternion tilt = PlaceSpots.TiltOf(lyingKnife);
            SameRotation(Quaternion.Euler(0f, 0f, 90f), tilt, "the tilt without the yaw");
            SameRotation(lyingKnife, PlaceSpots.RestRotation(tilt, Quaternion.Euler(0f, 40f, 0f)), "back on a surface turned 40 degrees");
            Quaternion onTurnedSurface = PlaceSpots.RestRotation(tilt, Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual((lyingKnife * Vector3.up).y, (onTurnedSurface * Vector3.up).y, 1e-4f, "still on its side");
        }

        [Test]
        public void RestRotation_ForAFlatItem_NeverStandsItUp()
        {
            // The scene knife and food rest with no tilt: on a worktop (identity) or a drop they stay flat.
            Quaternion tilt = PlaceSpots.TiltOf(Quaternion.Euler(0f, 123f, 0f));
            SameRotation(Quaternion.identity, tilt, "yaw only is no tilt");
            SameRotation(Quaternion.identity, PlaceSpots.RestRotation(tilt, Quaternion.identity), "flat on the worktop");
            Vector3 along = PlaceSpots.RestRotation(tilt, Quaternion.Euler(0f, 30f, 0f)) * Vector3.forward;
            Assert.AreEqual(0f, along.y, 1e-4f, "the knife's long side stays horizontal");
        }

        [Test]
        public void YawOf_StraightUp_IsIdentity()
        {
            SameRotation(Quaternion.identity, PlaceSpots.YawOf(Quaternion.Euler(-90f, 0f, 0f)), "no yaw to take");
            SameRotation(Quaternion.Euler(0f, 70f, 0f), PlaceSpots.YawOf(Quaternion.Euler(20f, 70f, 0f)), "pitch removed");
        }

        [Test]
        public void AnItemLiesOnOneSpotOnly()
        {
            var spots = new PlaceSpots();
            spots.TryTake(0, 1, 7);
            spots.TryTake(2, 4, 7);
            Assert.IsFalse(spots.IsTaken(0, 1), "it moved off its old spot");
            Assert.IsTrue(spots.IsTaken(2, 4));
            spots.Clear();
            Assert.IsFalse(spots.IsTaken(2, 4));
        }
    }
}
