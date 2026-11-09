using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b, pure: the numbers and curves of docs/feel.md 1–4 for the client's look. Easing, the pick-up arc, the place
    /// settle and the drop; which tween a change of place gets; the cone aim score with stickiness; how long a target
    /// stays shown; and how a predicted visual reads the synced state (confirmed, denied or still pending). Nothing here
    /// decides a game outcome: the server checks every request as before.
    /// </summary>
    public static class FeelRules
    {
        /// <summary>No candidate.</summary>
        public const int None = -1;
        /// <summary>Degrees added to every aim angle, so two candidates a hair apart near the centre do not swap on noise.</summary>
        public const float AimCostFloor = 0.5f;
        /// <summary>Fraction of a place tween spent travelling (then it settles back up).</summary>
        public const float SettleTravel = 0.7f;
        /// <summary>m: a networked position that moves at least this much counts as a change (prediction, settling).</summary>
        public const float MoveEpsilon = 0.01f;

        public static float Clamp01(float t) => float.IsNaN(t) ? 0f : t < 0f ? 0f : t > 1f ? 1f : t;

        /// <summary>Cubic ease-out: fast start, soft arrival. 0 at 0, 1 at 1, monotonic.</summary>
        public static float EaseOut(float t)
        {
            t = Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>Quadratic ease-in (a fall). 0 at 0, 1 at 1, monotonic.</summary>
        public static float EaseIn(float t)
        {
            t = Clamp01(t);
            return t * t;
        }

        /// <summary>Smoothstep. 0 at 0, 1 at 1, monotonic.</summary>
        public static float EaseInOut(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Height of the arc above the straight line: 0 at both ends, <paramref name="lift"/> in the middle.</summary>
        public static float ArcLift(float t, float lift)
        {
            t = Clamp01(t);
            return lift * 4f * t * (1f - t);
        }

        /// <summary>
        /// Progress of a place tween: eases out to 1 + <paramref name="settle"/> over the first <see cref="SettleTravel"/>
        /// of the time (the item sinks a little past the spot), then settles back to exactly 1.
        /// </summary>
        public static float SettleProgress(float t, float settle)
        {
            t = Clamp01(t);
            if (settle <= 0f || float.IsNaN(settle)) return EaseOut(t);
            if (t <= SettleTravel) return (1f + settle) * EaseOut(t / SettleTravel);
            return 1f + settle * (1f - EaseInOut((t - SettleTravel) / (1f - SettleTravel)));
        }

        /// <summary>Where a tween of <paramref name="style"/> draws the item at time fraction <paramref name="t"/>.</summary>
        public static Vector3 TweenPosition(TweenStyle style, Vector3 from, Vector3 to, float t, float amount)
        {
            t = Clamp01(t);
            switch (style)
            {
                case TweenStyle.Arc:
                    return Vector3.LerpUnclamped(from, to, EaseOut(t)) + Vector3.up * ArcLift(t, amount);
                case TweenStyle.Settle:
                    return Vector3.LerpUnclamped(from, to, SettleProgress(t, amount));
                case TweenStyle.Drop:
                    return Vector3.LerpUnclamped(from, to, EaseIn(t));
                default:
                    return to;
            }
        }

        /// <summary>Rotation of a tween at <paramref name="t"/> (eases with the position, never overshoots).</summary>
        public static Quaternion TweenRotation(TweenStyle style, Quaternion from, Quaternion to, float t)
        {
            if (style == TweenStyle.None) return to;
            float p = style == TweenStyle.Drop ? EaseIn(t) : EaseOut(t);
            return Quaternion.Slerp(from, to, p);
        }

        /// <summary>
        /// The tween when an item's goal changes from (<paramref name="from"/>, holder) to (<paramref name="to"/>, holder).
        /// Picked up (from a rest place, or from another holder): an arc. Put down: a settle. Into cookware: an arc into it.
        /// Back from a refused put-into: an arc. Toggling your own view between hand and first person: none (the camera
        /// blends). A rest place that moves is handled by the item itself (it waits until the networked position settles).
        /// </summary>
        public static TweenStyle StyleFor(ItemGoalKind from, int fromHolder, ItemGoalKind to, int toHolder)
        {
            bool fromHeld = IsHeld(from), toHeld = IsHeld(to);
            if (to == ItemGoalKind.Into) return from == ItemGoalKind.Into ? TweenStyle.None : TweenStyle.Arc;
            if (from == ItemGoalKind.Into) return toHeld ? TweenStyle.Arc : TweenStyle.Settle;
            if (from == ItemGoalKind.Rest && toHeld) return TweenStyle.Arc;
            if (fromHeld && to == ItemGoalKind.Rest) return TweenStyle.Settle;
            if (fromHeld && toHeld) return fromHolder != toHolder ? TweenStyle.Arc : TweenStyle.None;
            return TweenStyle.None;
        }

        public static bool IsHeld(ItemGoalKind kind) =>
            kind == ItemGoalKind.Hand || kind == ItemGoalKind.View || kind == ItemGoalKind.HoldPoint;

        /// <summary>Seconds of a tween of <paramref name="style"/> into <paramref name="to"/>.</summary>
        public static float TweenSeconds(TweenStyle style, ItemGoalKind to, FeelSettings settings)
        {
            if (settings == null) settings = FeelSettings.PresetA();
            switch (style)
            {
                case TweenStyle.Arc: return to == ItemGoalKind.Into ? settings.DropSeconds : settings.PickupSeconds;
                case TweenStyle.Settle: return settings.PlaceSeconds;
                case TweenStyle.Drop: return settings.DropSeconds;
                default: return 0f;
            }
        }

        /// <summary>The style's amount: the arc's lift (a smaller one into cookware), the settle fraction.</summary>
        public static float TweenAmount(TweenStyle style, ItemGoalKind to, FeelSettings settings)
        {
            if (settings == null) settings = FeelSettings.PresetA();
            switch (style)
            {
                case TweenStyle.Arc: return to == ItemGoalKind.Into ? settings.PickupLift * 0.5f : settings.PickupLift;
                case TweenStyle.Settle: return settings.PlaceSettle;
                default: return 0f;
            }
        }

        // ---- Aim (docs/feel.md 3) ----

        /// <summary>Degrees between a view direction and the direction to a point; 180 for a zero vector.</summary>
        public static float AngleTo(Vector3 viewDirection, Vector3 toPoint)
        {
            if (viewDirection.sqrMagnitude < 1e-10f || toPoint.sqrMagnitude < 1e-10f) return 180f;
            return Vector3.Angle(viewDirection, toPoint);
        }

        /// <summary>Lower is better: the angle (plus <see cref="AimCostFloor"/>), cheaper by <paramref name="heldBonus"/> for
        /// cookware that takes what the hands hold.</summary>
        public static float AimCost(float angle, bool acceptsHeld, float heldBonus)
        {
            if (float.IsNaN(angle) || angle < 0f) angle = 180f;
            float cost = angle + AimCostFloor;
            return acceptsHeld ? cost * (1f - Clamp01(heldBonus)) : cost;
        }

        /// <summary>Stickiness: switch from the current target only when the new one is better by <paramref name="stickMargin"/>.</summary>
        public static bool ShouldSwitch(float currentCost, float candidateCost, float stickMargin)
        {
            return candidateCost < currentCost * (1f - Clamp01(stickMargin));
        }

        /// <summary>
        /// The target among <paramref name="candidates"/>: a <see cref="AimCandidate.Direct"/> hit always wins (the smallest
        /// angle if there are more); otherwise the lowest <see cref="AimCost"/> within <paramref name="cone"/> degrees, but
        /// the <paramref name="current"/> one stays unless the best beats it by <paramref name="stickMargin"/>. Returns an
        /// index or <see cref="None"/>.
        /// </summary>
        public static int PickTarget(IReadOnlyList<AimCandidate> candidates, int current, float cone, float stickMargin, float heldBonus)
        {
            if (candidates == null || candidates.Count == 0) return None;
            int direct = None;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Direct && (direct == None || candidates[i].Angle < candidates[direct].Angle)) direct = i;
            if (direct != None) return direct;

            int best = None;
            float bestCost = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!(candidates[i].Angle <= cone)) continue;
                float cost = AimCost(candidates[i].Angle, candidates[i].AcceptsHeld, heldBonus);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            if (best == None) return None;
            if (current >= 0 && current < candidates.Count && current != best && candidates[current].Angle <= cone)
            {
                float currentCost = AimCost(candidates[current].Angle, candidates[current].AcceptsHeld, heldBonus);
                if (!ShouldSwitch(currentCost, bestCost, stickMargin)) return current;
            }
            return best;
        }

        /// <summary>
        /// docs/feel.md 3: a target that was shown since <paramref name="shownSince"/> stays (even if the fresh selection lost
        /// it) until <paramref name="minSeconds"/> have passed; no single-frame flicker.
        /// </summary>
        public static bool KeepShown(float shownSince, float now, float minSeconds)
        {
            return now - shownSince < minSeconds;
        }

        // ---- Prediction (docs/feel.md 1) ----

        /// <summary>Review of spec 027b: s of round trip assumed before the first measurement (FishNet reports 0 until a ping).</summary>
        public const float AssumedRoundTrip = 0.15f;

        /// <summary>The measured round trip, or <see cref="AssumedRoundTrip"/> while there is none yet (0, negative or NaN).</summary>
        public static float RoundTrip(float measuredSeconds)
        {
            return float.IsNaN(measuredSeconds) || float.IsInfinity(measuredSeconds) || measuredSeconds <= 0f ? AssumedRoundTrip : measuredSeconds;
        }

        /// <summary>
        /// Spec 027b review: the confirm timeout actually used: the setting, but never less than one and a half round trips
        /// plus two ticks (the request waits for a tick to leave, the state for one to come back), so a slow connection does
        /// not send a success back. Bad inputs count as 0.
        /// </summary>
        public static float ConfirmTimeout(float setting, float roundTripSeconds, float tickDelta)
        {
            if (float.IsNaN(setting) || setting < 0f) setting = 0f;
            if (float.IsNaN(roundTripSeconds) || float.IsInfinity(roundTripSeconds) || roundTripSeconds < 0f) roundTripSeconds = 0f;
            if (float.IsNaN(tickDelta) || float.IsInfinity(tickDelta) || tickDelta < 0f) tickDelta = 0f;
            return System.Math.Max(setting, roundTripSeconds * 1.5f + 2f * tickDelta);
        }

        /// <summary>
        /// Spec 027b review: a click queued inside the request interval is still sent only if the dot is on the same target
        /// (and spot) as when it was clicked, that click did something then, and it would still do the same thing (second
        /// review: the action is compared too). Otherwise it is dropped, never retargeted.
        /// </summary>
        public static bool KeepQueuedClick(Worktown.Game.Net.Rules.InteractionAction queuedAction, int queuedTarget, int queuedSpot,
            Worktown.Game.Net.Rules.InteractionAction nowAction, int nowTarget, int nowSpot)
        {
            return queuedAction != Worktown.Game.Net.Rules.InteractionAction.None && queuedAction == nowAction
                   && queuedTarget == nowTarget && queuedSpot == nowSpot;
        }

        /// <summary>
        /// What the synced state <paramref name="now"/> says about a prediction made when it was <paramref name="start"/>:
        /// PickUp is confirmed when you hold it and denied when someone else does or it is gone. Place and Drop are
        /// confirmed when nobody holds it any more, denied when someone else holds it or it is gone. PutInto is confirmed
        /// only when our own food is gone (the server despawns what it put in; a content count that changes can be another
        /// player's put-in or a pot that went away, so it only restarts the timeout), denied when you no longer hold it. Anything else
        /// is still <see cref="PredictionPhase.Pending"/>.
        /// </summary>
        public static PredictionPhase Judge(PredictionKind kind, int me, ItemSnapshot start, ItemSnapshot now)
        {
            const int noHolder = -1;
            switch (kind)
            {
                case PredictionKind.PickUp:
                    if (!now.Exists) return PredictionPhase.Denied;
                    if (now.Holder == me) return PredictionPhase.Confirmed;
                    return now.Holder != noHolder ? PredictionPhase.Denied : PredictionPhase.Pending;
                case PredictionKind.Place:
                case PredictionKind.Drop:
                    if (!now.Exists) return PredictionPhase.Denied;
                    if (now.Holder == noHolder) return PredictionPhase.Confirmed;
                    return now.Holder != me ? PredictionPhase.Denied : PredictionPhase.Pending;
                case PredictionKind.PutInto:
                    if (!now.Exists) return PredictionPhase.Confirmed;
                    return now.Holder != me ? PredictionPhase.Denied : PredictionPhase.Pending;
                default:
                    return PredictionPhase.Pending;
            }
        }

        /// <summary>
        /// True when the synced state changed in a way that matters to the prediction (it changed hands, appeared or went,
        /// the cookware changed, or a lying item moved): the timeout then counts from now. A held item follows its holder
        /// all the time, so its position never counts.
        /// </summary>
        public static bool IsRelevantChange(ItemSnapshot before, ItemSnapshot now)
        {
            const int noHolder = -1;
            if (before.Exists != now.Exists || before.Holder != now.Holder || before.VesselCount != now.VesselCount) return true;
            bool lying = before.Holder == noHolder && now.Holder == noHolder;
            return lying && (before.Position - now.Position).sqrMagnitude > MoveEpsilon * MoveEpsilon;
        }
    }
}
