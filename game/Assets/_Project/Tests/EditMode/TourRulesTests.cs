using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Dev;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027 AC2: the tours' path following reaches every point within 0.3 m (simulated with the real movement rules,
    /// collisions aside), the sprint and crouch flags sit on the right steps, unknown tour names are rejected, and the
    /// aiming helpers turn the right way.
    /// </summary>
    public class TourRulesTests
    {
        private const float Reached = 0.3f;
        // The graybox spawn points (GrayboxSceneBuilder.CreateSpawnPoints). Spec 027a: on the cook aisle's centre line (was a
        // ring of radius 3 around the origin).
        private static readonly Vector3[] Spawns =
            { new Vector3(1.2f, 0f, 12.95f), new Vector3(2.6f, 0f, 12.95f), new Vector3(4.0f, 0f, 12.95f), new Vector3(5.4f, 0f, 12.95f) }; // spec 028b: the aisle starts at x 0.46

        private static Tour Get(string name)
        {
            Assert.IsTrue(TourRules.TryGet(name, out Tour tour), name);
            return tour;
        }

        /// <summary>
        /// Walks one WalkTo step the way PlayerController does (MovementRules.Accelerate on the camera-relative input),
        /// steering every tick like TourActor; returns the end position and whether it arrived before the walk timeout.
        /// </summary>
        private static Vector3 Walk(Vector3 from, Vector3 to, bool sprint, bool crouch, float cameraYaw, float tick, out bool arrived)
        {
            Vector3 position = from, velocity = Vector3.zero;
            arrived = false;
            for (float t = 0f; t < TourRules.WalkTimeout; t += tick)
            {
                Vector2 move = TourRules.Steer(position, to, cameraYaw, sprint, out arrived);
                if (arrived) break;
                Vector3 target = MovementRules.WorldDirection(move, cameraYaw) * MovementRules.MaxSpeed(sprint, crouch);
                velocity = MovementRules.Accelerate(velocity, target, tick);
                position += velocity * tick;
            }
            // Coast to a stop after the input goes to zero (the 0.3 s pause in TourActor).
            for (float t = 0f; t < 0.3f; t += tick)
            {
                velocity = MovementRules.Accelerate(velocity, Vector3.zero, tick);
                position += velocity * tick;
            }
            return position;
        }

        private static void AssertRouteReached(IEnumerable<(Vector3 point, bool sprint, bool crouch)> route, Vector3 start, float yaw, float tick, string label)
        {
            Vector3 at = start;
            foreach (var (point, sprint, crouch) in route)
            {
                at = Walk(at, point, sprint, crouch, yaw, tick, out bool arrived);
                Assert.IsTrue(arrived, label + ": did not arrive at " + point + " (stuck at " + at + ")");
                float off = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(point.x, point.z));
                Assert.Less(off, Reached, label + ": " + point + " missed by " + off.ToString("0.000") + " m");
            }
        }

        private static IEnumerable<(Vector3, bool, bool)> WalkSteps(Tour tour) =>
            tour.Steps.Where(s => s.Kind == TourStepKind.WalkTo).Select(s => (s.Point, s.Sprint, s.Crouch));

        [TestCase(TourRules.LookTour)]
        [TestCase(TourRules.WalkTour)]
        public void TourRoutes_ReachEveryPoint(string name)
        {
            Tour tour = Get(name);
            foreach (Vector3 spawn in Spawns)
                foreach (float yaw in new[] { 0f, 90f, 205f })
                    foreach (float tick in new[] { 1f / 30f, 1f / 60f })
                        AssertRouteReached(WalkSteps(tour), spawn, yaw, tick, name + " from " + spawn + " yaw " + yaw);
        }

        [TestCase(TourRules.LookTour)]
        [TestCase(TourRules.WalkTour)]
        [TestCase(TourRules.CookTour)]
        public void ObserverRoutes_ReachTheirSpot(string name)
        {
            Tour tour = Get(name);
            Assert.Greater(tour.ObserverRoute.Count, 0);
            foreach (Vector3 spawn in Spawns)
                AssertRouteReached(tour.ObserverRoute.Select(p => (p, false, false)), spawn, 0f, 1f / 30f, name + " observer from " + spawn);
            Assert.AreEqual(tour.ObserverRoute.Last(), tour.ObserverSpot);
        }

        /// <summary>Both players start together; a shared waypoint makes them block each other (seen in the first run).</summary>
        [TestCase(TourRules.LookTour)]
        [TestCase(TourRules.WalkTour)]
        [TestCase(TourRules.CookTour)]
        public void ObserverRoute_SharesNoPointWithTheTour(string name)
        {
            Tour tour = Get(name);
            foreach (Vector3 o in tour.ObserverRoute)
                foreach (TourStep s in tour.Steps.Where(s => s.Kind == TourStepKind.WalkTo))
                    Assert.Greater(Vector3.Distance(o, s.Point), 1.5f, name + ": observer point " + o + " is next to tour point " + s.Point);
        }

        [Test]
        public void WalkTour_WalksSixSprintsEightCrouchesTwo()
        {
            List<TourStep> walks = Get(TourRules.WalkTour).Steps.Where(s => s.Kind == TourStepKind.WalkTo).ToList();
            // The last three walks are the observed segments; the ones before only bring the player there.
            TourStep start = walks[walks.Count - 4], walk = walks[walks.Count - 3], sprint = walks[walks.Count - 2], crouch = walks[walks.Count - 1];
            Assert.AreEqual(6f, Vector3.Distance(start.Point, walk.Point), 0.01f);
            Assert.AreEqual(8f, Vector3.Distance(walk.Point, sprint.Point), 0.01f);
            Assert.AreEqual(2f, Vector3.Distance(sprint.Point, crouch.Point), 0.01f);
            Assert.IsFalse(walk.Sprint || walk.Crouch, "walk segment");
            Assert.IsTrue(sprint.Sprint && !sprint.Crouch, "sprint segment");
            Assert.IsTrue(crouch.Crouch && !crouch.Sprint, "crouch segment");
            Assert.AreEqual(1, walks.Count(s => s.Sprint), "only the sprint segment sprints");
            Assert.AreEqual(1, walks.Count(s => s.Crouch), "only the crouch segment crouches");
        }

        [Test]
        public void LookAndCookTours_NeverSprintOrCrouch()
        {
            foreach (string name in new[] { TourRules.LookTour, TourRules.CookTour })
                Assert.IsFalse(Get(name).Steps.Any(s => s.Kind == TourStepKind.WalkTo && (s.Sprint || s.Crouch)), name);
        }

        [Test]
        public void WalkTour_ObserverCameraIsFourMetresToTheSideAtChestHeight()
        {
            Tour tour = Get(TourRules.WalkTour);
            Assert.IsTrue(tour.HasObserverCamera);
            List<TourStep> walks = tour.Steps.Where(s => s.Kind == TourStepKind.WalkTo).ToList();
            float lineZ = walks[walks.Count - 1].Point.z;
            foreach (TourStep s in walks.Skip(walks.Count - 4)) Assert.AreEqual(lineZ, s.Point.z, 0.001f, "the observed walk is a straight line");
            Assert.AreEqual(4f, Mathf.Abs(lineZ - tour.ObserverCameraPosition.z), 0.01f);
            Assert.That(tour.ObserverCameraPosition.y, Is.InRange(1.1f, 1.5f), "chest height");
            // The camera looks at the line (north, level) and the observer's own player stands behind it.
            Assert.AreEqual(0f, tour.ObserverCameraYaw);
            Assert.Less(tour.ObserverSpot.z, tour.ObserverCameraPosition.z);
        }

        [Test]
        public void WalkTour_FramesAreTheSpecDefaults()
        {
            List<TourStep> frames = TourRules.FrameSteps(Get(TourRules.WalkTour));
            CollectionAssert.AreEqual(new[] { "walk", "sprint", "crouch" }, frames.Select(f => f.Name).ToArray());
            foreach (TourStep f in frames)
            {
                Assert.AreEqual(0.1f, f.Interval, 1e-6f);
                Assert.AreEqual(24, f.FrameCount);
            }
            Assert.AreEqual(6, TourRules.SheetColumns);
            Assert.AreEqual(new Vector2Int(320, 180), TourRules.SheetCell);
        }

        [Test]
        public void LookTour_HasTheStandardShots()
        {
            string[] shots = Get(TourRules.LookTour).Steps.Where(s => s.Kind == TourStepKind.Shot).Select(s => s.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "kitchen-third", "stock-third", "stock-first", "fridge-slot", "shelf-slot", "griddle-first", "fryer-first", "stove-first", "pan-first", "worktop-first", "assembly-first", "drinks-first", "terminal-first", "back-door-first", "scale-person", "counter-guests", "street-third" }, shots);
        }

        [Test]
        public void EveryActIsKnownAndEveryTourWaitsForItsObserver()
        {
            foreach (string name in TourRules.Names)
            {
                Tour tour = Get(name);
                Assert.AreEqual(name, tour.Name);
                foreach (TourStep s in tour.Steps.Where(s => s.Kind == TourStepKind.Act))
                    CollectionAssert.Contains(TourRules.Acts.ToArray(), s.Name, name);
                Assert.AreEqual(1, tour.Steps.Count(s => s.Kind == TourStepKind.WaitObserver), name);
                Assert.Greater(tour.TimeoutSeconds, 0f);
            }
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("LOOK")]
        [TestCase("dance")]
        [TestCase(" walk")]
        public void UnknownTour_IsRejected(string name)
        {
            Assert.IsFalse(TourRules.TryGet(name, out Tour tour));
            Assert.IsNull(tour);
            Assert.IsFalse(TourRules.IsKnown(name));
        }

        [Test]
        public void KnownTours_AreLookWalkCook()
        {
            CollectionAssert.AreEqual(new[] { "look", "walk", "cook" }, TourRules.Names.ToArray());
            foreach (string name in TourRules.Names) Assert.IsTrue(TourRules.IsKnown(name));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(-135f)]
        [TestCase(400f)]
        public void Steer_IsTheInverseOfWorldDirection(float yaw)
        {
            var from = new Vector3(1f, 0f, 2f);
            var to = new Vector3(4f, 5f, -2f); // height is ignored
            Vector2 move = TourRules.Steer(from, to, yaw, false, out bool arrived);
            Assert.IsFalse(arrived);
            Assert.AreEqual(1f, move.magnitude, 1e-4f, "full input far from the point");
            Vector3 world = MovementRules.WorldDirection(move, yaw);
            Vector3 wanted = new Vector3(3f, 0f, -4f).normalized;
            Assert.Less(Vector3.Distance(world, wanted), 1e-4f, world + " vs " + wanted);
        }

        [Test]
        public void Steer_SlowsNearThePointAndStopsOnIt()
        {
            Vector2 near = TourRules.Steer(Vector3.zero, new Vector3(0f, 0f, 0.25f), 0f, false, out bool arrived);
            Assert.IsFalse(arrived);
            Assert.Less(near.magnitude, 1f);
            Assert.GreaterOrEqual(near.magnitude, TourRules.MinApproach - 1e-6f);
            Assert.AreEqual(Vector2.zero, TourRules.Steer(Vector3.zero, new Vector3(0.1f, 0f, 0f), 0f, false, out arrived));
            Assert.IsTrue(arrived);
            TourRules.Steer(Vector3.zero, new Vector3(float.NaN, 0f, 0f), 0f, false, out arrived);
            Assert.IsTrue(arrived, "NaN never walks");
        }

        [Test]
        public void AimError_SignsMatchTheLookIntent()
        {
            // Looking north; the point is to the right (east) and above.
            Vector2 error = TourRules.AimError(Vector3.forward, new Vector3(1f, 1f, 1f));
            Assert.Greater(error.x, 0f, "turn right");
            Assert.Greater(error.y, 0f, "look up");
            Assert.AreEqual(45f, error.x, 0.01f);
            Assert.AreEqual(Vector2.zero, TourRules.AimError(Vector3.zero, Vector3.forward));
            Assert.IsTrue(TourRules.IsOnTarget(TourRules.AimError(Vector3.forward, Vector3.forward)));
        }

        [Test]
        public void AimStep_HalvesAndClamps()
        {
            Assert.AreEqual(new Vector2(2f, -1f), TourRules.AimStep(new Vector2(4f, -2f)));
            Assert.AreEqual(new Vector2(TourRules.MaxAimStep, -TourRules.MaxAimStep), TourRules.AimStep(new Vector2(170f, -90f)));
        }

        [Test]
        public void Direction_MatchesYawAndPitch()
        {
            Assert.Less(Vector3.Distance(Vector3.forward, TourRules.Direction(0f, 0f)), 1e-5f);
            Assert.Less(Vector3.Distance(Vector3.right, TourRules.Direction(90f, 0f)), 1e-5f);
            Assert.AreEqual(Mathf.Sin(30f * Mathf.Deg2Rad), TourRules.Direction(45f, 30f).y, 1e-5f);
            Vector2 error = TourRules.AimError(Vector3.forward, TourRules.Direction(-20f, -10f));
            Assert.AreEqual(-20f, error.x, 0.01f);
            Assert.AreEqual(-10f, error.y, 0.01f);
        }

        [Test]
        public void FrameSteps_NeedsATour()
        {
            Assert.Throws<System.ArgumentNullException>(() => TourRules.FrameSteps(null));
            Assert.AreEqual(0, TourRules.FrameSteps(Get(TourRules.LookTour)).Count);
        }
    }
}
