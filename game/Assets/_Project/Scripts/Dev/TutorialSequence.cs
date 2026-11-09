using System.Collections;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Tutorial;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 038 (AC3), development builds: the <c>tutorial</c> tour. The tour client runs with <c>-tutorial</c>, so the real
    /// <see cref="TutorialRunner"/> shows its card; the tour player walks the steps through its intents with the cook tour's own parts
    /// (<see cref="CookSequence"/>: the fries, the falafel wrap with its lemonade and the payments, the cleaning, the stock order, the
    /// spit). <see cref="WatchSteps"/> runs beside it and shoots every card when its step comes (with the target's Mustard pulse), the
    /// edge arrow once per step when the target is off the screen, and the done card. The host runs with -tutorial and -tour-orders, so
    /// its guests are calm and the first ones order the tutorial's dishes.
    /// </summary>
    public static class TutorialSequence
    {
        public const float StartWait = 20f;
        public const float ShotDelay = 0.35f;

        /// <summary>The steps whose card <see cref="WatchSteps"/> shot (one tour per process).</summary>
        private static readonly System.Collections.Generic.HashSet<int> Shot = new System.Collections.Generic.HashSet<int>();

        public static IEnumerator Run(TourActor actor, CookCapture capture)
        {
            TutorialRunner runner = null;
            float until = Time.unscaledTime + StartWait;
            while ((runner == null || !runner.Running) && Time.unscaledTime < until)
            {
                runner = TutorialRunner.Find();
                yield return null;
            }
            if (runner == null || !runner.Running)
            {
                actor.CountTimeout("tutorial: the tutorial did not start (-tutorial)");
                yield break;
            }
            Station stove = null;
            foreach (Station s in Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Kind == StationKind.Stove) stove = s;
            float laneZ = stove != null ? stove.transform.position.z - TourRules.AisleOffset : TourRules.AisleZ;

            // Step 1: walk along the aisle and look around (the card shows the first step; then the potato slot pulses).
            Debug.Log("[Tutorial tour] step 1: move and look");
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.Look(90f, 0f, 1.2f);
            yield return actor.Look(-90f, 0f, 1.2f);
            yield return actor.WalkTo(new Vector3(actor.Position.x + 2f, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(actor.Position.x - 1.5f, 0f, laneZ), false, false);
            yield return actor.LevelView();
            yield return Expect(actor, runner, TutorialStepId.TakePotato, 3f);
            // The potato slot behind the player: the edge arrow (the watcher shoots it).
            yield return actor.Look(180f, 0f, 1.2f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return capture.Shot("tutorial-arrow-behind");
            yield return actor.Look(-90f, 20f, 1.2f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return capture.Shot("tutorial-arrow-side");
            yield return actor.LevelView();
            // The potato slot pulsing in Mustard while it is in view but not aimed at (frames show the pulse).
            if (runner.TargetObject is StockSlot potato)
            {
                yield return actor.WalkTo(new Vector3(potato.Anchor.x + 1.6f, 0f, laneZ), false, false);
                yield return actor.AimAt(potato.Anchor + new Vector3(0.9f, 0.45f, 0f), 1.2f);
                yield return capture.Shot("tutorial-02-pulse-not-aimed");
                if (capture.Frames != null) yield return capture.Frames("tutorial-02-pulse", 1f);
                yield return capture.WaitForFrames();
                yield return actor.LevelView();
            }

            // Steps 2-5, 8, 9: the fries (taken, cut, fried, tipped onto a plate), served and paid at the register.
            yield return CookSequence.FryFries(actor, capture, laneZ);
            Debug.Log("[Tutorial tour] after the fries: step " + runner.StepIndex + " " + runner.StepId);
            yield return PreviewPassed(runner, capture);
            // Steps 6, 7, 10: the falafel wrap on the board, folded and wrapped; the lemonade; both served and paid.
            yield return CookSequence.BuildFalafelWrap(actor, capture, laneZ);
            Debug.Log("[Tutorial tour] after the wrap: step " + runner.StepIndex + " " + runner.StepId);
            yield return PreviewPassed(runner, capture);
            // Step 11: the fries guest's plate comes back and is washed (and the rest of the cleaning loop).
            yield return CookSequence.CleanUp(actor, capture, laneZ);
            // Step 12: stock runs out and is ordered at the terminal.
            yield return CookSequence.RunOutAndOrder(actor, capture, laneZ);
            Debug.Log("[Tutorial tour] after the order: step " + runner.StepIndex + " " + runner.StepId);
            yield return PreviewPassed(runner, capture);
            // Step 13 (optional): the spit.
            yield return CookSequence.ShaveSeitanWrap(actor, capture, laneZ);
            // The spit's shave reaches the done card during the seitan wrap's serving; the card may have closed by itself (8 s) since.
            if (runner.Running) yield return Expect(actor, runner, TutorialStepId.Done, 5f);
            else if (!Shot.Contains(runner.StepCount - 1)) actor.CountTimeout("tutorial: it ended without the done card");
            yield return actor.LevelView();
            yield return new WaitForSecondsRealtime(1f);
            if (runner.Running) yield return capture.Shot("tutorial-done-card");
            until = Time.unscaledTime + TutorialRules.DoneCardSeconds + 3f;
            while (runner.Running && Time.unscaledTime < until) yield return null;
            if (runner.Running) actor.CountTimeout("tutorial: the done card did not close by itself");
            yield return capture.Shot("tutorial-ended");
            Debug.Log("[Tutorial tour] done (" + Time.time.ToString("0.0") + " s)");
        }

        /// <summary>
        /// The steps the tour did ahead of time passed at once (adaptive) without a card shot: each is shown once more (the runner's dev
        /// preview, the progress unchanged) and shot with its target.
        /// </summary>
        private static IEnumerator PreviewPassed(TutorialRunner runner, CookCapture capture)
        {
            for (int i = 0; i < runner.StepIndex && runner.Running; i++)
            {
                if (Shot.Contains(i)) continue;
                Shot.Add(i);
                runner.DevPreview(i);
                yield return new WaitForSecondsRealtime(0.8f);
                Debug.Log("[Tutorial tour] preview card " + (i + 1) + " (passed at once): target " + runner.CurrentTarget + " ("
                    + (runner.TargetObject != null ? runner.TargetObject.name : "none") + "), arrow " + runner.ArrowShown);
                yield return capture.Shot("tutorial-" + (i + 1).ToString("00") + "-passed-" + TutorialSteps.Default[i].Id.ToString().ToLowerInvariant());
                runner.DevEndPreview();
            }
        }

        /// <summary>Waits for step <paramref name="id"/> (counts a timeout when it does not come).</summary>
        private static IEnumerator Expect(TourActor actor, TutorialRunner runner, TutorialStepId id, float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (runner.Running && runner.StepId != id && Time.unscaledTime < until) yield return null;
            if (!runner.Running || runner.StepId != id)
                actor.CountTimeout("tutorial: expected step " + id + ", at " + (runner.Running ? runner.StepId.ToString() : "the end"));
        }

        /// <summary>
        /// Beside <see cref="Run"/>: a shot of the card (and the pulsing target) shortly after every step change, and one of the edge arrow
        /// per step when it shows. Logs the step, its target and the card's text.
        /// </summary>
        public static IEnumerator WatchSteps(CookCapture capture)
        {
            TutorialRunner runner = null;
            while (runner == null)
            {
                runner = TutorialRunner.Find();
                yield return null;
            }
            int shown = -2;
            bool arrowShot = false;
            while (true)
            {
                int step = runner.StepIndex;
                if (step != shown)
                {
                    shown = step;
                    arrowShot = false;
                    if (step >= 0)
                    {
                        yield return new WaitForSecondsRealtime(ShotDelay);
                        Debug.Log("[Tutorial tour] card " + (step + 1) + "/" + runner.StepCount + " " + runner.StepId + ", target " + runner.CurrentTarget
                            + " (" + (runner.TargetObject != null ? runner.TargetObject.name : "none") + "), arrow " + runner.ArrowShown + ": " + runner.CardText);
                        Shot.Add(step);
                        yield return capture.Shot("tutorial-" + (step + 1).ToString("00") + "-" + runner.StepId.ToString().ToLowerInvariant());
                    }
                }
                else if (step >= 0 && !arrowShot && runner.ArrowShown)
                {
                    arrowShot = true;
                    yield return capture.Shot("tutorial-" + (step + 1).ToString("00") + "-arrow");
                }
                yield return null;
            }
        }
    }
}
