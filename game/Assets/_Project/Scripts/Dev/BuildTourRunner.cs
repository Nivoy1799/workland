using System.Collections;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Sim.Layout;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 062b, development builds only: the build tour's act (TourRules.ActBuild), run by the tour client while the observer host films
    /// the kitchen from the street. The host starts with the shop closed (-tour-build). The client enters build mode, stores the plating
    /// worktop by stove 2 on the pallet, moves the worktop by the fridge into its place (the ghost, then the move: the sheet shows the
    /// counter piece moved), takes the stored one out to the free place, undoes that once (it goes back into storage), shows a refused place
    /// (the red ghost in the aisle), leaves build mode, opens the shop at the door sign and waits for a guest at the rebuilt queue. Every
    /// step is the same intent a click sends (BuildModeView's dev hooks); the server decides.
    /// </summary>
    public static class BuildTourRunner
    {
        private const float AnswerWait = 6f;
        private const float GuestWait = 90f;
        private const float StandZ = 12.9f;
        /// <summary>Where the camera stands for the shots: at the front counter's kitchen side, looking at the back line.</summary>
        private const float ShotZ = 12.45f;

        public static IEnumerator Run(TourActor actor, CookCapture capture)
        {
            BuildModeView view = BuildModeView.Local;
            float until = Time.unscaledTime + 10f;
            while (view == null && Time.unscaledTime < until)
            {
                yield return null;
                view = BuildModeView.Local;
            }
            if (view == null)
            {
                actor.CountTimeout("build: no build mode view on the player");
                yield break;
            }
            ShopLayout layout = DefaultLayout.Load();
            LayoutPiece[] worktops = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(layout.Pieces, p => p.TypeId == PieceCatalog.Worktop));
            LayoutPiece byFridge = worktops[1], byStove = worktops[3]; // both 740 mm (ids 20 and 22)
            LayoutPiece fryer = System.Linq.Enumerable.First(layout.Pieces, p => p.TypeId == PieceCatalog.Fryer);
            LayoutPiece pallet = System.Linq.Enumerable.First(layout.Pieces, p => p.TypeId == PieceCatalog.StoragePallet);
            Vector3 Top(LayoutPiece p) => new Vector3(p.XMm / 1000f, 0.9f, p.ZMm / 1000f);
            Vector3 Floor(LayoutPiece p) => new Vector3(p.XMm / 1000f, 0f, p.ZMm / 1000f);

            // The closed shop: the door sign reads CLOSED, the clock waits.
            yield return LookAtBackLine(actor, byStove);
            Debug.Log("[Tour] build: the door is " + ShopDoor.Now);
            if (ShopDoor.Now != ShopDoorState.Closed) actor.CountTimeout("build: the shop is not closed (-tour-build on the host?)");
            yield return capture.Shot("build-kitchen-before");

            if (!view.DevEnter())
            {
                actor.CountTimeout("build: could not enter build mode");
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return capture.Shot("build-mode-on");

            // 1. Store the worktop by stove 2 on the pallet.
            yield return Click(view, Top(byStove), true, "pick up the worktop by stove 2", actor);
            yield return Click(view, Floor(pallet), false, "store it on the pallet", actor);
            yield return capture.Shot("build-stored");

            // 2. The worktop by the fridge goes into its place: the ghost first, then the move.
            yield return Click(view, Top(byFridge), true, "pick up the worktop by the fridge", actor);
            yield return LookAtBackLine(actor, byStove);
            view.DevAim(Floor(byStove));
            yield return new WaitForSecondsRealtime(0.6f);
            Debug.Log("[Tour] build: ghost " + (view.GhostShown ? view.GhostFits ? "fits" : "does not fit" : "not shown") + ": " + view.PromptText);
            yield return capture.Shot("build-ghost-move");
            yield return Click(view, Floor(byStove), false, "place it by stove 2", actor);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return capture.Shot("build-worktop-moved");

            // 3. Take the stored worktop out to the free place by the fridge, then undo that once (back into storage).
            yield return Click(view, Floor(pallet), true, "take the worktop out of storage", actor);
            yield return LookAtBackLine(actor, byFridge);
            view.DevAim(Floor(byFridge));
            yield return new WaitForSecondsRealtime(0.6f);
            yield return capture.Shot("build-ghost-takeout");
            yield return Click(view, Floor(byFridge), false, "place it by the fridge", actor);
            yield return capture.Shot("build-swapped");
            view.DevUndo();
            yield return Answer(view, "undo", actor);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return capture.Shot("build-undone");

            // 4. A refused place: the fryer aimed into the aisle shows the red ghost and its reason.
            yield return Click(view, Top(fryer), true, "pick up the fryer", actor);
            yield return actor.WalkTo(new Vector3(fryer.XMm / 1000f - 1.6f, 0f, ShotZ), false, false);
            yield return actor.AimAt(new Vector3(fryer.XMm / 1000f, 0.4f, 13.2f), 1.5f);
            view.DevAim(new Vector3(fryer.XMm / 1000f, 0f, 13.0f));
            yield return new WaitForSecondsRealtime(0.6f);
            Debug.Log("[Tour] build: the fryer in the aisle: " + (view.GhostFits ? "fits (unexpected)" : "refused") + ": " + view.PromptText);
            if (view.GhostFits) actor.CountTimeout("build: the ghost in the aisle should not fit");
            yield return capture.Shot("build-ghost-refused");
            view.DevAim(null);
            view.DevLeave(); // lets the fryer go where it stands
            yield return Answer(view, "release", actor);

            // 5. Open the shop at the door sign; a guest walks the rebuilt route to the queue.
            Station door = LayoutRegistry.First(StationKind.DoorSign);
            if (door == null)
            {
                actor.CountTimeout("build: no door sign");
                yield break;
            }
            actor.Intents.View = ViewMode.FirstPerson;
            yield return new WaitForSecondsRealtime(ViewRules.BlendSeconds + 0.2f);
            yield return actor.WalkTo(new Vector3(13.9f, 0f, door.transform.position.z), false, false);
            yield return actor.Use("open the shop", door.transform.position, InteractionAction.Press,
                () => actor.Interactor.DevSendStation(door.Id, 0));
            float open = Time.unscaledTime + AnswerWait;
            while (ShopDoor.Now != ShopDoorState.Open && Time.unscaledTime < open) yield return null;
            Debug.Log("[Tour] build: the door is " + ShopDoor.Now);
            if (ShopDoor.Now != ShopDoorState.Open) actor.CountTimeout("build: the shop did not open");
            yield return capture.Shot("build-door-open");
            float guest = Time.unscaledTime + GuestWait;
            Customer seen = null;
            while (seen == null && Time.unscaledTime < guest)
            {
                foreach (Customer c in Object.FindObjectsByType<Customer>(FindObjectsSortMode.None))
                    if (c != null && c.transform.position.z < 11.5f && c.transform.position.x < 16f) { seen = c; break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (seen == null) actor.CountTimeout("build: no guest came to the counter within " + GuestWait + " s");
            else Debug.Log("[Tour] build: a guest at " + seen.transform.position.ToString("F2"));
            actor.Intents.View = ViewMode.ThirdPerson;
            yield return actor.WalkTo(new Vector3(11.6f, 0f, StandZ), false, false);
            yield return actor.Look(180f, -12f);
            yield return new WaitForSecondsRealtime(1f);
            yield return capture.Shot("build-guest-queue");
        }

        /// <summary>First person at the front counter's kitchen side, looking at <paramref name="piece"/>'s place on the back line.</summary>
        private static IEnumerator LookAtBackLine(TourActor actor, LayoutPiece piece)
        {
            actor.Intents.View = ViewMode.FirstPerson;
            yield return actor.WalkTo(new Vector3(piece.XMm / 1000f, 0f, ShotZ), false, false);
            yield return actor.AimAt(new Vector3(piece.XMm / 1000f, 0.7f, piece.ZMm / 1000f), 1.5f);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>Aims at <paramref name="point"/>, clicks (left: pick up or take out; right: place or store) and waits for the answer.</summary>
        private static IEnumerator Click(BuildModeView view, Vector3 point, bool left, string what, TourActor actor)
        {
            Debug.Log("[Tour] build: " + what);
            view.DevAim(point);
            yield return new WaitForSecondsRealtime(0.4f);
            if (left) view.DevLeftClick();
            else view.DevRightClick();
            yield return Answer(view, what, actor);
        }

        private static IEnumerator Answer(BuildModeView view, string what, TourActor actor)
        {
            int before = view.AnswerCount;
            yield return null;
            yield return null;
            float until = Time.unscaledTime + AnswerWait;
            while (view.AnswerCount == before && Time.unscaledTime < until) yield return null;
            if (view.AnswerCount == before)
            {
                actor.CountTimeout("build: no answer to '" + what + "'");
                yield break;
            }
            Debug.Log("[Tour] build: " + what + ": " + view.LastOutcome);
            if (view.LastOutcome != BuildOutcome.Ok) actor.CountTimeout("build: '" + what + "' was refused (" + view.LastOutcome + ")");
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }
}
