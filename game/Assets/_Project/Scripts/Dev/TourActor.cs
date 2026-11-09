using System;
using System.Collections;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: moves and aims the owner's player through its <see cref="ScriptedIntentSource"/> (no simulated keys, so
    /// no window focus needed) and clicks like a player (<see cref="PickupInteractor.DevClick"/>). Shared by the tours and
    /// <c>-demo-cookware</c>. Coroutines; failures are counted in <see cref="Timeouts"/>.
    /// </summary>
    public sealed class TourActor
    {
        public PlayerController Player { get; }
        public PickupInteractor Interactor { get; }
        public ScriptedIntentSource Intents { get; }
        /// <summary>Walks that did not reach their point in time.</summary>
        public int Timeouts { get; private set; }
        /// <summary>Uses whose aim did not give the expected action, so the request went out directly.</summary>
        public int Fallbacks { get; private set; }

        public TourActor(PlayerController player, ScriptedIntentSource intents)
        {
            Player = player;
            Interactor = player.GetComponent<PickupInteractor>();
            Intents = intents;
        }

        public Vector3 Position => Player.transform.position;

        public void CountTimeout(string what)
        {
            Timeouts++;
            Debug.LogWarning("[Tour] timed out: " + what);
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Camera yaw the controller uses for the move (Camera.main, else the body), as in BuildMoveData.</summary>
        private float CameraYaw()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.eulerAngles.y : Player.transform.eulerAngles.y;
        }

        /// <summary>Walks to <paramref name="target"/> (height ignored), steering every frame; then stands still briefly.</summary>
        public IEnumerator WalkTo(Vector3 target, bool sprint, bool crouch, float timeout = TourRules.WalkTimeout)
        {
            float giveUp = Time.unscaledTime + timeout;
            Intents.Crouch = crouch;
            float bestDistance = float.PositiveInfinity, lastProgress = Time.unscaledTime;
            while (true)
            {
                Vector2 move = TourRules.Steer(Position, target, CameraYaw(), sprint, out bool arrived);
                if (arrived) break;
                float distance = FlatDistance(Position, target);
                if (distance < bestDistance - TourRules.StuckProgress)
                {
                    bestDistance = distance;
                    lastProgress = Time.unscaledTime;
                }
                else if (Time.unscaledTime - lastProgress > TourRules.StuckSeconds)
                {
                    // Something is in the way: close enough is fine, otherwise it counts like a timeout.
                    if (distance > TourRules.StuckGoodEnough) CountTimeout("walk to " + target + " stuck at " + Position);
                    else Debug.LogWarning("[Tour] stopped " + distance.ToString("0.00") + " m short of " + target + " (blocked)");
                    break;
                }
                if (Time.unscaledTime > giveUp)
                {
                    CountTimeout("walk to " + target + " (at " + Position + ")");
                    break;
                }
                Intents.Move = move;
                Intents.Sprint = sprint;
                yield return null;
            }
            Intents.Stop();
            yield return new WaitForSeconds(0.3f);
            Intents.Crouch = false;
        }

        /// <summary>
        /// Turns the view towards <paramref name="point"/> for up to <paramref name="seconds"/>; stops early once on target
        /// and (if given) <paramref name="until"/> holds.
        /// </summary>
        public IEnumerator AimAt(Vector3 point, float seconds, Func<bool> until = null)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                Camera cam = Camera.main;
                if (cam == null) yield break;
                Vector2 error = TourRules.AimError(cam.transform.forward, point - cam.transform.position);
                bool onTarget = TourRules.IsOnTarget(error);
                if (onTarget && (until == null || until())) yield break;
                if (!onTarget) Intents.AddLook(TourRules.AimStep(error));
                yield return null;
            }
        }

        /// <summary>Turns the view to an absolute yaw and pitch (degrees, pitch up positive), for up to 2 s.</summary>
        public IEnumerator Look(float yaw, float pitch, float seconds = 2f)
        {
            float end = Time.unscaledTime + seconds;
            Vector3 wanted = TourRules.Direction(yaw, pitch);
            while (Time.unscaledTime < end)
            {
                Camera cam = Camera.main;
                if (cam == null) yield break;
                Vector2 error = TourRules.AimError(cam.transform.forward, wanted);
                if (TourRules.IsOnTarget(error)) yield break;
                Intents.AddLook(TourRules.AimStep(error));
                yield return null;
            }
        }

        /// <summary>Looks straight ahead towards the kitchen (+Z), level (the spec 021 demo's walk steered by this view).</summary>
        public IEnumerator LevelView() => Look(0f, 0f, 1.5f);

        /// <summary>
        /// Aims at <paramref name="point"/>, then does what a click would do; if the aim does not give
        /// <paramref name="expected"/> in 3 s, logs a warning and sends <paramref name="fallback"/> directly.
        /// Same log lines as the spec 021 demo. Spec 027b: <paramref name="beforeClick"/> runs between the aim and the click.
        /// </summary>
        public IEnumerator Use(string what, Vector3 point, InteractionAction expected, Action fallback, Func<IEnumerator> beforeClick = null)
        {
            Debug.Log("[Demo] " + what + " (" + Time.time.ToString("0.0") + " s)");
            yield return AimAt(point, 3f, () => Interactor.CurrentAction == expected);
            // Spec 027b: a shot of the aimed state (highlight, prompt, ghost) and the start of a frame sequence, right before
            // the click (frames that start here see the click's own frame).
            if (beforeClick != null) yield return beforeClick();
            if (Interactor.CurrentAction == expected)
            {
                Debug.Log("[Demo] click: '" + InteractionRules.PromptFor(Interactor.CurrentHeld, Interactor.CurrentAim) + "'");
                Interactor.DevClick();
            }
            else
            {
                Fallbacks++;
                Debug.LogWarning("[Demo] aiming gave " + Interactor.CurrentAction + " instead of " + expected + "; sending the request directly");
                fallback();
            }
            yield return new WaitForSeconds(1.5f);
        }
    }
}
