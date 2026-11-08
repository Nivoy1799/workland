using System;
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027, pure: the tours (shot table) and the path following and aiming they use. Coordinates are the graybox
    /// scene's (GrayboxSceneBuilder): kitchen row at z 13 (stoves at x 3 and 5.5, counter 8.5, last worktop to 14.1), the
    /// cook's lane at z 11.3 to 11.7, the queue at z 10.8, the road at z 5.5 to 10.5 east of x 9, a ring of pickup cubes
    /// at 5 and 7 m around the spawns, a box at x 15 to 19, z 1.5 to 2.5. Every tour route first goes to the hub at (-1.75, 0),
    /// inside the ring, then north past it (as the spec 021 demo); observer routes use (1.75, 0), because both players
    /// start at the same time and block each other at a shared point. If the layout changes, change the points here (TourRulesTests walks every route).
    /// </summary>
    public static class TourRules
    {
        public const string LookTour = "look";
        public const string WalkTour = "walk";
        public const string CookTour = "cook";

        /// <summary>Act names the runner knows.</summary>
        public const string ActPickUpPan = "pick-up-pan";
        public const string ActPutPanBack = "put-pan-back";
        public const string ActCook = "cook";

        /// <summary>Spec 027 defaults: walking frames every 0.1 s, 24 per segment; sheet 6 columns of 320x180.</summary>
        public const float FrameInterval = 0.1f;
        public const int FramesPerSegment = 24;
        public const int SheetColumns = 6;
        public static readonly Vector2Int SheetCell = new Vector2Int(320, 180);

        /// <summary>Path following: a point counts as reached within this distance (the tests demand 0.3 m).</summary>
        public const float ArriveDistance = 0.12f;
        /// <summary>Below this distance the move input shrinks, so the player stops on the point (sprint: further out).</summary>
        public const float SlowRadius = 0.5f;
        public const float SprintSlowRadius = 0.9f;
        /// <summary>The smallest move input while approaching, so the last centimetres do not take forever.</summary>
        public const float MinApproach = 0.15f;
        /// <summary>A walk step gives up after this long (counts as a timeout).</summary>
        public const float WalkTimeout = 20f;
        /// <summary>Aiming: done within this many degrees; each frame turns half the error, at most MaxAimStep.</summary>
        public const float AimTolerance = 0.4f;
        public const float MaxAimStep = 8f;
        /// <summary>A walk that gets less than <see cref="StuckProgress"/> closer in <see cref="StuckSeconds"/> is stuck: it
        /// ends there (fine within <see cref="StuckGoodEnough"/> of the point, else a timeout).</summary>
        public const float StuckSeconds = 1.5f;
        public const float StuckProgress = 0.05f;
        public const float StuckGoodEnough = 0.5f;
        /// <summary>The tour client waits this long for the observer to stand at its spot (counts as a timeout).</summary>
        public const float ObserverWait = 150f;
        /// <summary>The observer's player counts as at its spot within this distance.</summary>
        public const float ObserverSpotDistance = 0.6f;

        public static readonly IReadOnlyList<string> Names = new[] { LookTour, WalkTour, CookTour };
        public static readonly IReadOnlyList<string> Acts = new[] { ActPickUpPan, ActPutPanBack, ActCook };

        public static bool IsKnown(string name) => TryGet(name, out _);

        /// <summary>The tour with this name; false (and null) for an unknown or empty name. Names are lower case.</summary>
        public static bool TryGet(string name, out Tour tour)
        {
            switch (name)
            {
                case LookTour: tour = BuildLook(); return true;
                case WalkTour: tour = BuildWalk(); return true;
                case CookTour: tour = BuildCook(); return true;
                default: tour = null; return false;
            }
        }

        /// <summary>
        /// Camera-relative move input (x right, y forward) that walks from <paramref name="position"/> to
        /// <paramref name="target"/> for a camera with <paramref name="cameraYaw"/> (MovementRules.WorldDirection is
        /// its inverse). Full length until the slow radius, then shorter; zero and <paramref name="arrived"/> once within
        /// <see cref="ArriveDistance"/>. Height is ignored.
        /// </summary>
        public static Vector2 Steer(Vector3 position, Vector3 target, float cameraYaw, bool sprint, out bool arrived)
        {
            var delta = new Vector2(target.x - position.x, target.z - position.z);
            float distance = delta.magnitude;
            arrived = !(distance > ArriveDistance); // NaN counts as arrived: never walk on garbage
            if (arrived) return Vector2.zero;
            float yaw = MovementRules.SanitizeYaw(cameraYaw) * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            // Camera right is (cos, -sin) and camera forward (sin, cos) in world x/z.
            var local = new Vector2(delta.x * cos - delta.y * sin, delta.x * sin + delta.y * cos) / distance;
            float slow = sprint ? SprintSlowRadius : SlowRadius;
            float length = distance >= slow ? 1f : Mathf.Max(distance / slow, MinApproach);
            return local * length;
        }

        /// <summary>Unit forward vector for a yaw (degrees, 0 = +z, 90 = +x) and a pitch (degrees, up positive).</summary>
        public static Vector3 Direction(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p));
        }

        /// <summary>How far the view must turn from <paramref name="forward"/> to <paramref name="wanted"/>: x yaw (right
        /// positive), y pitch (up positive), in degrees. Zero for a zero vector.</summary>
        public static Vector2 AimError(Vector3 forward, Vector3 wanted)
        {
            if (forward.sqrMagnitude < 1e-8f || wanted.sqrMagnitude < 1e-8f) return Vector2.zero;
            forward.Normalize();
            wanted.Normalize();
            float yaw = Mathf.DeltaAngle(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, Mathf.Atan2(wanted.x, wanted.z) * Mathf.Rad2Deg);
            float pitch = (Mathf.Asin(Mathf.Clamp(wanted.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f))) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        /// <summary>One frame's look delta for an aim error: half of it (no overshoot even if the camera lags a frame),
        /// at most <see cref="MaxAimStep"/> degrees per axis.</summary>
        public static Vector2 AimStep(Vector2 error)
        {
            return new Vector2(Mathf.Clamp(error.x * 0.5f, -MaxAimStep, MaxAimStep), Mathf.Clamp(error.y * 0.5f, -MaxAimStep, MaxAimStep));
        }

        public static bool IsOnTarget(Vector2 error) => Mathf.Abs(error.x) < AimTolerance && Mathf.Abs(error.y) < AimTolerance;

        /// <summary>The Frames steps of a tour, in order (the observer takes the same sequences).</summary>
        public static List<TourStep> FrameSteps(Tour tour)
        {
            if (tour == null) throw new ArgumentNullException(nameof(tour));
            var result = new List<TourStep>();
            foreach (TourStep step in tour.Steps)
                if (step.Kind == TourStepKind.Frames) result.Add(step);
            return result;
        }

        // ---- The tours ----

        private const float LaneZ = 11.3f;   // the cook's lane in front of the kitchen row
        private const float StandZ = 11.7f;  // standing at a station
        private const float HubX = -1.75f;   // the way north past the ring of cubes
        private const float ObserverHubX = 1.75f; // the observer's own way north, so the two players never meet at the hub

        private static readonly TourStep[] ToKitchen = { TourStep.WalkTo(HubX, 0f), TourStep.WalkTo(HubX, LaneZ) };
        private static readonly TourStep[] ToStreet = { TourStep.WalkTo(HubX, 0f), TourStep.WalkTo(HubX, 9f), TourStep.WalkTo(13f, 9f) };

        /// <summary>
        /// Standard shots: the kitchen from the cook's position (third person), first person at a stove, holding the pan
        /// and at a worktop, a person (the observer) standing in front of the first worktop for scale, the counter and the
        /// queue from the guests' side, the street. The tour waits for the observer first: its route crosses the lane.
        /// </summary>
        private static Tour BuildLook()
        {
            var steps = new List<TourStep> { TourStep.View(ViewMode.FirstPerson), TourStep.WaitObserver() };
            steps.AddRange(ToKitchen);
            steps.AddRange(new[]
            {
                TourStep.WalkTo(4.25f, LaneZ),
                TourStep.View(ViewMode.ThirdPerson), TourStep.Look(0f, -12f), TourStep.Wait(1.2f), TourStep.Shot("kitchen-third"),
                TourStep.View(ViewMode.FirstPerson), TourStep.WalkTo(3f, StandZ), TourStep.LookAt(3f, 0.9f, 12.85f), TourStep.Wait(0.6f),
                TourStep.Shot("stove-first"),
                TourStep.Act(ActPickUpPan), TourStep.Look(0f, -12f), TourStep.Wait(0.8f), TourStep.Shot("pan-first"),
                TourStep.Act(ActPutPanBack),
                TourStep.WalkTo(7f, LaneZ), TourStep.WalkTo(7f, StandZ), TourStep.LookAt(7f, 0.9f, 12.85f), TourStep.Wait(0.6f),
                TourStep.Shot("worktop-first"),
                TourStep.WalkTo(3.4f, 10.2f), TourStep.LookAt(0.6f, 1f, 12.25f), TourStep.Wait(0.6f), TourStep.Shot("scale-person"),
                TourStep.WalkTo(8.5f, 7f), TourStep.LookAt(8.5f, 1f, 12.5f), TourStep.Wait(0.6f), TourStep.Shot("counter-guests"),
                TourStep.View(ViewMode.ThirdPerson), TourStep.WalkTo(11f, 7.5f), TourStep.Look(90f, -6f), TourStep.Wait(1.2f),
                TourStep.Shot("street-third"),
            });
            // In front of the first worktop (x -0.1 to 2.4, front edge near z 12.7), clear of the queue; the lane passes 0.95 m south.
            var route = new List<Vector3> { new Vector3(ObserverHubX, 0f, 0f), new Vector3(ObserverHubX, 0f, 9.5f), new Vector3(0.6f, 0f, 9.5f), new Vector3(0.6f, 0f, 12.25f) };
            return new Tour(LookTour, steps, route, 180f, false, Vector3.zero, 0f, 0f, 0f, 240f);
        }

        /// <summary>
        /// The player walks 6 m east along the road, sprints 8 m back west and crouch-walks 2 m east, past an observer
        /// camera 4 m to the south at chest height (1.3 m) looking north; frames every 0.1 s on both clients. The camera
        /// sees about 4.9 m to each side at that distance, so the segments stay within x 14 to 22 around its x 18.
        /// </summary>
        private static Tour BuildWalk()
        {
            float segment = FramesPerSegment * FrameInterval;
            var steps = new List<TourStep> { TourStep.View(ViewMode.ThirdPerson) };
            steps.AddRange(ToStreet);
            steps.AddRange(new[]
            {
                TourStep.WalkTo(16f, 8f), TourStep.Look(90f, -6f), TourStep.WaitObserver(), TourStep.Wait(3f),
                TourStep.Frames("walk", segment, FrameInterval), TourStep.WalkTo(22f, 8f), TourStep.Wait(1.5f),
                TourStep.Look(270f, -6f), TourStep.Wait(1f),
                TourStep.Frames("sprint", segment, FrameInterval), TourStep.WalkTo(14f, 8f, sprint: true), TourStep.Wait(1.5f),
                TourStep.Look(90f, -6f), TourStep.Wait(1f),
                TourStep.Frames("crouch", segment, FrameInterval), TourStep.WalkTo(16f, 8f, crouch: true), TourStep.Wait(2f),
            });
            var route = new List<Vector3> { new Vector3(ObserverHubX, 0f, 0f), new Vector3(ObserverHubX, 0f, 9f), new Vector3(18f, 0f, 9f), new Vector3(18f, 0f, 3.2f) };
            return new Tour(WalkTour, steps, route, 0f, true, new Vector3(18f, 1.3f, 4f), 0f, 0f, 0f, 300f);
        }

        /// <summary>The spec 021 cookware sequence with a shot after each step; the observer watches from the guests' side.</summary>
        private static Tour BuildCook()
        {
            var steps = new List<TourStep> { TourStep.View(ViewMode.FirstPerson), TourStep.WaitObserver(), TourStep.Act(ActCook) };
            var route = new List<Vector3> { new Vector3(0f, 0f, 3.8f) };
            // Camera 4.7 m in front of the stoves at eye height (1.6 m), a little down: the cook's back, as a guest sees it.
            return new Tour(CookTour, steps, route, 0f, true, new Vector3(3.5f, 1.6f, 8.3f), 0f, -10f, 6f, 420f);
        }
    }
}
