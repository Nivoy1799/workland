using System;
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027, pure: the tours (shot table) and the path following and aiming they use. Coordinates are the graybox
    /// scene's (GrayboxSceneBuilder). Spec 027a, the shop floor plan: back line at z 14.3 (stove A at x 6.64, stove B 9.14,
    /// plate shelf 11.64; spec 028b: the dry shelf at x 2.36 and the fridge at 3.60, 1.8 m high, against the back wall), the
    /// cook aisle z 12.2 to 13.7 (centre 12.95; players spawn on that line at x 1.2 to 5.4), the front counter z 11.5 to 12.2,
    /// the guests' queue at z 11.1 (x 6.64 to 11.64), the staff gap at the aisle's west end (side walls at x 0.26 to 0.46),
    /// the road at z 5.5 to 10.5 east of x 9, a ring of pickup cubes at 5 and 7 m around
    /// the origin, a box at x 15 to 19, z 1.5 to 2.5. Both players start in the single-file aisle and cannot pass each other
    /// there, so every tour first walks out through the staff gap to <see cref="ParkX"/> (the same way as its observer,
    /// whichever of the two is in front; two 0.3 m players can pass in the 1.5 m aisle, but the straight-line steering
    /// would only slide past), and the observers' spots are outside the aisle; observer routes leave the gap
    /// southwards at x -0.7. If the layout changes, change the points here (TourRulesTests walks every route).
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
        /// <summary>Spec 027b: the same pick-up and put-back, with a highlight shot before the click and frames every
        /// <see cref="FrameInterval"/> around it (the arc into the hand, the ghost, the settle on the burner).</summary>
        public const string ActPickUpPanFrames = "pick-up-pan-frames";
        public const string ActPutPanBackFrames = "put-pan-back-frames";
        /// <summary>Spec 027b: tweens <see cref="SlowMotionScale"/> times longer (the arc shape shows at 0.1 s frames), and back.</summary>
        public const string ActSlowMotion = "slow-motion";
        public const string ActNormalMotion = "normal-motion";
        public const float SlowMotionScale = 4f;
        /// <summary>Spec 027b: seconds of frames around one click.</summary>
        public const float ClickFramesSeconds = 1f;

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
        public static readonly IReadOnlyList<string> Acts = new[]
        {
            ActPickUpPan, ActPutPanBack, ActCook, ActPickUpPanFrames, ActPutPanBackFrames, ActSlowMotion, ActNormalMotion
        };

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

        /// <summary>Spec 027a: the cook aisle's centre line (the graybox's back line z 14.3 minus <see cref="AisleOffset"/>).</summary>
        public const float AisleZ = 12.95f;
        /// <summary>Spec 027a: from a back line station's centre to the aisle's centre line, where the cook stands.</summary>
        public const float AisleOffset = 1.35f;
        private const float StandZ = 13f;    // standing at a back line station (1.3 m in front of its centre)
        private const float BackLineZ = 14.3f;
        // Spec 029: the fryer (x 6.44) moved the stoves 0.84 m to the right. Spec 029b: the stoves stand side by side, the
        // plating worktop (x 9.34 to 10.60) and the plate shelf (11.22) follow stove B.
        // Spec 030: the griddle (x 5.49) between the prep run (now one board wide) and the fryer; nothing else moved.
        private const float GriddleX = 5.49f;
        private const float FryerX = 6.44f;
        private const float StoveAX = 7.48f;
        private const float StoveBX = 8.72f;
        private const float WorktopBetweenX = 9.71f; // spec 029b: the plating worktop after stove B; spec 031: one plate pair wide (9.34 to 10.08)
        /// <summary>Spec 031: the assembly board after the plates (the plate shelf and the counter at 11.64).</summary>
        private const float AssemblyX = 10.55f;
        /// <summary>Outside the staff gap, on the aisle's line: every tour parks here first (see the class summary).</summary>
        public const float ParkX = -2.5f; // spec 028b: 2.8 m west of the side wall, as in 027a
        private const float ObserverExitX = -0.7f; // the observers' way south from the staff gap, west of the side wall
        private const float LookObserverX = 5.0f;  // the look tour's person for scale, at the front counter's street side (spec 028b: clear of the stock shot)
        private const float LookObserverZ = 10.9f;

        private static readonly TourStep Park = TourStep.WalkTo(ParkX, AisleZ);

        /// <summary>Spec 028b: the stock stations' centres (GrayboxSceneBuilder: shelf, fridge, then the prep worktop from x 4.22),
        /// the back line's back (where both stand with their backs), and where the look tour stands to see both.</summary>
        public const float StockShelfX = 2.36f;
        public const float FridgeX = 3.60f;
        private const float BackLineBackZ = 14.9f;
        private const float StockViewX = 2.98f;

        /// <summary>
        /// Spec 028b: the world point of the slot of <paramref name="id"/> on the fridge or the dry shelf (StockLayout and StockArt,
        /// as the scene builder places it): the middle of its items' bottom, plus 5 cm.
        /// </summary>
        public static Vector3 SlotPoint(StationKind kind, string id)
        {
            int slot = -1;
            IReadOnlyList<string> ids = StockLayout.SlotsOf(kind);
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) slot = i;
            if (slot < 0) throw new ArgumentException(id + " is not on the " + kind, nameof(id));
            Vector3 stand = StockArt.StandPoint(kind, slot);
            float front = BackLineBackZ - StockArt.DepthOf(kind);
            return new Vector3((kind == StationKind.Fridge ? FridgeX : StockShelfX) + stand.x, stand.y + 0.05f, front + stand.z);
        }

        private static TourStep LookAtSlot(StationKind kind, string id)
        {
            Vector3 p = SlotPoint(kind, id);
            return TourStep.LookAt(p.x, p.y, p.z);
        }

        /// <summary>
        /// Standard shots: the kitchen down the aisle (third person), first person at a stove, holding the pan and at a
        /// worktop, a person (the observer) standing at the front counter's street side for scale, seen from the aisle across
        /// the counter, the counter and the queue from the guests' side (third person behind the cook, so the camera looks
        /// across the counter at the cook), the street.
        /// </summary>
        private static Tour BuildLook()
        {
            var steps = new List<TourStep> { TourStep.View(ViewMode.FirstPerson), Park, TourStep.WaitObserver() };
            steps.AddRange(new[]
            {
                // Spec 028b: down the aisle from its east end, looking west (the stock now stands where the camera was).
                TourStep.WalkTo(10f, AisleZ),
                TourStep.View(ViewMode.ThirdPerson), TourStep.Look(270f, -12f), TourStep.Wait(1.2f), TourStep.Shot("kitchen-third"),
                // Spec 028b: the dry shelf and the fridge from the aisle (third person, the cook in front of them), then first
                // person in front of them, and a slot's highlight and prompt on the fridge (cucumber) and the shelf (potato).
                TourStep.WalkTo(StockViewX, 12.7f), TourStep.Look(0f, -6f), TourStep.Wait(1.2f), TourStep.Shot("stock-third"),
                TourStep.View(ViewMode.FirstPerson), TourStep.WalkTo(StockViewX, StandZ), TourStep.LookAt(StockViewX, 1.1f, BackLineZ),
                TourStep.Wait(0.6f), TourStep.Shot("stock-first"),
                LookAtSlot(StationKind.Fridge, "cucumber"), TourStep.Wait(0.6f), TourStep.Shot("fridge-slot"),
                LookAtSlot(StationKind.Shelf, "potato"), TourStep.Wait(0.6f), TourStep.Shot("shelf-slot"),
                // Spec 030: the griddle from the aisle (first person): the plate, its six spots, the knob, the spatula on the deck.
                TourStep.WalkTo(GriddleX, StandZ), TourStep.LookAt(GriddleX, 0.75f, BackLineZ - 0.2f), TourStep.Wait(0.6f), TourStep.Shot("griddle-first"),
                // Spec 029: the fryer from the aisle (first person): the knob, the basket buttons, both baskets on the hook.
                TourStep.WalkTo(FryerX, StandZ), TourStep.LookAt(FryerX, 0.85f, BackLineZ - 0.2f), TourStep.Wait(0.6f), TourStep.Shot("fryer-first"),
                TourStep.WalkTo(StoveAX, StandZ), TourStep.LookAt(StoveAX, 0.9f, BackLineZ - 0.15f),
                TourStep.Wait(0.6f), TourStep.Shot("stove-first"),
                // Spec 027b: the pick-up with its frames (highlight and prompt, the arc into the hand), the put-back (ghost, settle),
                // then both again in slow motion so the arc's shape shows at 0.1 s frames.
                TourStep.Act(ActPickUpPanFrames), TourStep.Look(0f, -12f), TourStep.Wait(0.8f), TourStep.Shot("pan-first"),
                TourStep.Act(ActPutPanBackFrames),
                TourStep.Act(ActSlowMotion), TourStep.Act(ActPickUpPanFrames), TourStep.Act(ActPutPanBackFrames), TourStep.Act(ActNormalMotion),
                TourStep.WalkTo(WorktopBetweenX, StandZ), TourStep.LookAt(WorktopBetweenX, 0.9f, BackLineZ - 0.15f), TourStep.Wait(0.6f),
                TourStep.Shot("worktop-first"),
                // Spec 031: the assembly board from the aisle (first person): the cutting board, the two dispensers, the packaging rack.
                TourStep.WalkTo(AssemblyX, StandZ), TourStep.LookAt(AssemblyX, 0.75f, BackLineZ - 0.2f), TourStep.Wait(0.6f),
                TourStep.Shot("assembly-first"),
                TourStep.WalkTo(LookObserverX, AisleZ), TourStep.LookAt(LookObserverX, 1.4f, LookObserverZ), TourStep.Wait(0.6f),
                TourStep.Shot("scale-person"),
                TourStep.WalkTo(StoveBX, AisleZ), TourStep.View(ViewMode.ThirdPerson), TourStep.Look(0f, -10f), TourStep.Wait(1.2f),
                TourStep.Shot("counter-guests"),
                Park, TourStep.WalkTo(ParkX, 9f), TourStep.WalkTo(11f, 7.5f), TourStep.Look(90f, -6f), TourStep.Wait(1.2f),
                TourStep.Shot("street-third"),
            });
            // At the front counter's street side, west of the queue, facing the kitchen.
            var route = new List<Vector3>
            {
                new Vector3(ObserverExitX, 0f, AisleZ), new Vector3(ObserverExitX, 0f, LookObserverZ), new Vector3(LookObserverX, 0f, LookObserverZ)
            };
            return new Tour(LookTour, steps, route, 0f, false, Vector3.zero, 0f, 0f, 0f, 240f);
        }

        /// <summary>
        /// The player walks 6 m east along the road, sprints 8 m back west and crouch-walks 2 m east, past an observer
        /// camera 4 m to the south at chest height (1.3 m) looking north; frames every 0.1 s on both clients. The camera
        /// sees about 4.9 m to each side at that distance, so the segments stay within x 14 to 22 around its x 18.
        /// </summary>
        private static Tour BuildWalk()
        {
            float segment = FramesPerSegment * FrameInterval;
            var steps = new List<TourStep> { TourStep.View(ViewMode.ThirdPerson), Park, TourStep.WalkTo(ParkX, 9f), TourStep.WalkTo(13f, 9f) };
            steps.AddRange(new[]
            {
                TourStep.WalkTo(16f, 8f), TourStep.Look(90f, -6f), TourStep.WaitObserver(), TourStep.Wait(3f),
                TourStep.Frames("walk", segment, FrameInterval), TourStep.WalkTo(22f, 8f), TourStep.Wait(1.5f),
                TourStep.Look(270f, -6f), TourStep.Wait(1f),
                TourStep.Frames("sprint", segment, FrameInterval), TourStep.WalkTo(14f, 8f, sprint: true), TourStep.Wait(1.5f),
                TourStep.Look(90f, -6f), TourStep.Wait(1f),
                TourStep.Frames("crouch", segment, FrameInterval), TourStep.WalkTo(16f, 8f, crouch: true), TourStep.Wait(2f),
            });
            // South of the tour's way (z 7.8 against its 9, north of the cube ring), then down x 11 and east along z 3.2.
            var route = new List<Vector3>
            {
                new Vector3(ObserverExitX, 0f, AisleZ), new Vector3(ObserverExitX, 0f, 7.8f), new Vector3(11f, 0f, 7.8f),
                new Vector3(11f, 0f, 3.2f), new Vector3(18f, 0f, 3.2f)
            };
            return new Tour(WalkTour, steps, route, 0f, true, new Vector3(18f, 1.3f, 4f), 0f, 0f, 0f, 300f);
        }

        /// <summary>
        /// The spec 021 cookware sequence with a shot after each step. The observer watches from the guests' side: its fixed
        /// camera stands 5 m in front of the back line, west of the queue (x 6.64 and up), and looks across the front counter
        /// at the cook, from the crates to the second stove.
        /// </summary>
        private static Tour BuildCook()
        {
            var steps = new List<TourStep> { TourStep.View(ViewMode.FirstPerson), Park, TourStep.WaitObserver(), TourStep.Act(ActCook) };
            var route = new List<Vector3>
            {
                new Vector3(ObserverExitX, 0f, AisleZ), new Vector3(ObserverExitX, 0f, 7.8f), new Vector3(3.2f, 0f, 7.8f)
            };
            return new Tour(CookTour, steps, route, 0f, true, new Vector3(3.2f, 2.1f, 9.3f), 12f, -12f, 6f, 960f); // spec 030: + the griddle; spec 031: + the wrap; spec 032: + the register
        }
    }
}
