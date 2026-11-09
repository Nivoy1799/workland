using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, pure: when the tutorial runs, when a host's guests are calm, the card's texts, the pulse and the edge arrow.
    /// <para>Calm guests (AC4): only a host (server and the local client in one process, never a server build) whose own tutorial runs
    /// gives the guests it spawns <see cref="CalmPatienceScale"/> times the patience and scripts their first orders. A dedicated server
    /// has no local client and no tutorial, so <see cref="HostCalms"/> is false there whatever any player does: it never changes its
    /// rules for one player, and no RPC or synced value carries a tutorial state.</para>
    /// </summary>
    public static class TutorialRules
    {
        /// <summary>PlayerPrefs: 1 once the tutorial was finished or skipped (it does not start by itself again).</summary>
        public const string DoneKey = "worktown.tutorial.done";
        /// <summary>Starts the tutorial whatever the flag says (and, on a host, the calm guests from the first one).</summary>
        public const string Arg = "-tutorial";
        public const string SkipArg = "-skip-tutorial";
        public const float CalmPatienceScale = 3f;
        /// <summary>The done card closes by itself after this long.</summary>
        public const float DoneCardSeconds = 8f;
        /// <summary>A host with a pending tutorial waits at most this long for its local client before the first guests come.</summary>
        public const float FirstSyncWaitSeconds = 3f;
        public const float PulseHz = 1.2f;
        /// <summary>The edge arrow keeps this share of the screen from the border.</summary>
        public const float ArrowMargin = 0.06f;

        /// <summary>
        /// Whether the tutorial starts by itself when the game starts: always with <see cref="Arg"/>; never with <see cref="SkipArg"/>;
        /// in a development build never in the scripted runs (-skip-whatsnew, -tour, -observe); else until it was done once.
        /// </summary>
        public static bool StartsOnLaunch(IReadOnlyList<string> args, bool done, bool debugBuild)
        {
            if (Has(args, Arg)) return true;
            if (Has(args, SkipArg)) return false;
            if (debugBuild && (Has(args, "-skip-whatsnew") || Has(args, "-tour") || Has(args, "-observe"))) return false;
            return !done;
        }

        /// <summary>AC4: the guests a server spawns now are calm only on a host whose local tutorial runs; never on a server build.</summary>
        public static bool HostCalms(bool serverBuild, bool serverStarted, bool localClientStarted, bool tutorialRunning) =>
            !serverBuild && serverStarted && localClientStarted && tutorialRunning;

        /// <summary>A guest's patience scale: <see cref="CalmPatienceScale"/> times <paramref name="scale"/> when calm.</summary>
        public static float PatienceScale(bool calm, float scale) => calm ? scale * CalmPatienceScale : scale;

        /// <summary>A host with a pending tutorial lets the first guests come only once its local client runs (or after a short wait).</summary>
        public static bool WaitsForLocalClient(bool serverBuild, bool tutorialPending, bool clientStarted, float secondsSinceServerStart) =>
            !serverBuild && tutorialPending && !clientStarted && secondsSinceServerStart < FirstSyncWaitSeconds;

        /// <summary>"3 / 14" (built only when the step changes).</summary>
        public static string ProgressText(int index, int count) => (index + 1) + " / " + count;

        /// <summary>The card shows while the tutorial runs and no menu or banner covers the screen.</summary>
        public static bool ShowsCard(bool running, bool uiOpen) => running && !uiOpen;

        /// <summary>The Mustard pulse, 0 to 1.</summary>
        public static float Pulse01(float time) => 0.5f + 0.5f * Mathf.Sin(time * PulseHz * 2f * Mathf.PI);

        /// <summary>A viewport point (Camera.WorldToViewportPoint) is in front of the camera and inside the screen.</summary>
        public static bool IsOnScreen(Vector3 viewport) =>
            viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;

        /// <summary>
        /// Where the edge arrow stands (viewport, 0..1, y up) for a target at <paramref name="viewport"/>, and its angle in degrees
        /// (0 points right, counter-clockwise). A target behind the camera points the other way, so turning that way finds it.
        /// </summary>
        public static Vector2 EdgePoint(Vector3 viewport, float margin, out float angleDegrees)
        {
            float dx = viewport.x - 0.5f, dy = viewport.y - 0.5f;
            if (viewport.z < 0f)
            {
                dx = -dx;
                dy = -dy;
            }
            if (Mathf.Abs(dx) < 1e-4f && Mathf.Abs(dy) < 1e-4f) dy = -1f; // straight behind: point down
            float half = 0.5f - Mathf.Clamp(margin, 0f, 0.45f);
            float t = Mathf.Min(Mathf.Abs(dx) > 1e-6f ? half / Mathf.Abs(dx) : float.PositiveInfinity,
                Mathf.Abs(dy) > 1e-6f ? half / Mathf.Abs(dy) : float.PositiveInfinity);
            angleDegrees = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            return new Vector2(0.5f + dx * t, 0.5f + dy * t);
        }

        private static bool Has(IReadOnlyList<string> args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Count; i++)
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
