using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054b (D48), pure: players block each other softly instead of as rigid capsules. When two players' contact circles
    /// (<see cref="ContactRadius"/> each, on the ground plane) overlap, each is pushed out along the line between them at
    /// <c>clamp(overlap x Stiffness, 0, MaxPushSpeed)</c>. A walker therefore cannot walk through someone; off the centre line the
    /// push turns sideways and the walker slides around them. Inside <see cref="MinDistance"/> the own step towards the other is
    /// removed as well, so not even two sprinters running head-on pass through each other.
    /// The server runs this with the players' real positions, the owner's prediction with the positions it draws (spec 054: about
    /// 133 ms late); a soft push changes little when the other is a few centimetres off, so what is left is a small error that the
    /// reconcile smoothing (<see cref="CorrectionBlend"/>) hides. Only the horizontal plane; the height decides only whether the
    /// two are level (<see cref="VerticalReach"/>).
    /// </summary>
    public static class PlayerSeparation
    {
        /// <summary>k: push speed per metre of overlap (1/s).</summary>
        public const float Stiffness = 8f;
        /// <summary>The largest push speed from one other player (m/s).</summary>
        public const float MaxPushSpeed = 2.5f;
        /// <summary>Each player's contact circle (m); the body (CharacterController) is 0.30.</summary>
        public const float ContactRadius = 0.35f;
        /// <summary>Two players touch below this distance between their centres (m).</summary>
        public const float ContactDistance = 2f * ContactRadius;
        /// <summary>Inside this distance (m) the own step towards the other is removed (the no-pass-through floor).</summary>
        public const float MinDistance = 0.45f;
        /// <summary>Players whose feet are this far apart in height (m) or more do not meet (one stands two metres higher).</summary>
        public const float VerticalReach = 1.8f;
        /// <summary>The drawn body's radius (m), the CharacterController's.</summary>
        public const float BodyRadius = 0.30f;
        /// <summary>Option (a): bodies overlapping more than 10 cm for real (centres closer than this, m) are pushed apart at full
        /// <see cref="MaxPushSpeed"/> along the real line, whatever the pushed player saw: a late view never keeps them in each other.</summary>
        public const float DeepDistance = 2f * BodyRadius - 0.10f;
        /// <summary>Spec 054b design 4: a remote player is drawn overlapping the own body by at most this much (m).</summary>
        public const float MaxDrawnOverlap = 0.05f;
        /// <summary>The closest a remote player is drawn to the own body's centre (m).</summary>
        public const float DrawnMinDistance = 2f * BodyRadius - MaxDrawnOverlap;
        /// <summary>How fast (m/s) a remote player's drawn push-out may change: it slides round the own body, never jumps.</summary>
        public const float DrawnOffsetSpeed = 3f;

        private const float Coincident = 1e-4f;

        /// <summary>
        /// The push on <paramref name="self"/> from <paramref name="other"/> (m/s, horizontal; zero without contact). Symmetric: swapping
        /// the two gives the opposite push. Two players on exactly the same spot are split along x by their object ids (the lower id
        /// goes +x), which both sides know.
        /// </summary>
        public static Vector3 PushVelocity(Vector3 self, Vector3 other, int selfId, int otherId)
        {
            if (!Level(self, other)) return Vector3.zero;
            float dx = self.x - other.x;
            float dz = self.z - other.z;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq >= ContactDistance * ContactDistance) return Vector3.zero;
            float distance = Mathf.Sqrt(distanceSq);
            float speed = Mathf.Clamp((ContactDistance - distance) * Stiffness, 0f, MaxPushSpeed);
            return Away(dx, dz, distance, selfId, otherId) * speed;
        }

        /// <summary>
        /// Option (a): the push on <paramref name="self"/> from another player it saw at <paramref name="seen"/> (where its client drew
        /// them) who really is at <paramref name="real"/>: at full <see cref="MaxPushSpeed"/> away from the real position when the bodies
        /// really overlap more than 10 cm (<see cref="DeepDistance"/>), else the <see cref="PushVelocity"/> from the seen position.
        /// The owner's prediction passes its drawn position for both.
        /// </summary>
        public static Vector3 ContactPush(Vector3 self, Vector3 seen, Vector3 real, int selfId, int otherId)
        {
            if (TryFullPush(self, real, DeepDistance, selfId, otherId, out Vector3 push)) return push;
            return PushVelocity(self, seen, selfId, otherId);
        }

        private static bool TryFullPush(Vector3 self, Vector3 other, float within, int selfId, int otherId, out Vector3 push)
        {
            push = Vector3.zero;
            if (!Level(self, other)) return false;
            float dx = self.x - other.x, dz = self.z - other.z;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq >= within * within) return false;
            push = Away(dx, dz, Mathf.Sqrt(distanceSq), selfId, otherId) * MaxPushSpeed;
            return true;
        }

        /// <summary>
        /// <paramref name="step"/> (this tick's horizontal displacement of <paramref name="self"/>) without the part that goes towards
        /// <paramref name="other"/> when the step ends inside <see cref="MinDistance"/>; the part along the other's edge stays (sliding).
        /// </summary>
        public static Vector3 Block(Vector3 self, Vector3 step, Vector3 other, int selfId, int otherId)
        {
            if (!Level(self, other)) return step;
            float ex = self.x + step.x - other.x;
            float ez = self.z + step.z - other.z;
            if (ex * ex + ez * ez >= MinDistance * MinDistance) return step;
            float dx = self.x - other.x;
            float dz = self.z - other.z;
            Vector3 away = Away(dx, dz, Mathf.Sqrt(dx * dx + dz * dz), selfId, otherId);
            float towards = step.x * away.x + step.z * away.z;
            if (towards >= 0f) return step;
            return new Vector3(step.x - away.x * towards, step.y, step.z - away.z * towards);
        }

        /// <summary>
        /// One tick against one other player: the own horizontal step (<paramref name="move"/>, already times the tick) plus the push,
        /// then the no-pass-through floor. Several others: add every <see cref="PushVelocity"/> first, then <see cref="Block"/> against each.
        /// </summary>
        public static Vector3 Step(Vector3 self, Vector3 move, Vector3 other, int selfId, int otherId, float dt)
        {
            return Step(self, move, other, other, selfId, otherId, dt);
        }

        /// <summary>Option (a): one tick against another player seen at <paramref name="seen"/> and really at <paramref name="real"/>:
        /// the <see cref="ContactPush"/>, then the floor against the real position.</summary>
        public static Vector3 Step(Vector3 self, Vector3 move, Vector3 seen, Vector3 real, int selfId, int otherId, float dt)
        {
            Vector3 step = move + ContactPush(self, seen, real, selfId, otherId) * dt;
            return Block(self, step, real, selfId, otherId);
        }

        /// <summary>
        /// Spec 054b review: one tick of the separation against <paramref name="count"/> other players, from index <paramref name="first"/>
        /// of the arrays: every <see cref="ContactPush"/> (from where the player saw each, <paramref name="seen"/>, and where each really
        /// is, <paramref name="real"/>) summed and the sum capped at <see cref="MaxPushSpeed"/> (no crowd shove, and no claimed view can
        /// add up a bigger push), then the no-pass-through floor (<see cref="Block"/>) against each real position. The server passes the
        /// history at the input's view tick as seen; the owner's prediction passes its drawn positions as both. Deterministic.
        /// </summary>
        public static Vector3 Separate(Vector3 self, Vector3 step, int selfId, Vector3[] seen, Vector3[] real, int[] ids, int first, int count,
            float dt)
        {
            Vector3 push = Vector3.zero;
            for (int i = first; i < first + count; i++) push += ContactPush(self, seen[i], real[i], selfId, ids[i]);
            step += CapPush(push) * dt;
            for (int i = first; i < first + count; i++) step = Block(self, step, real[i], selfId, ids[i]);
            return step;
        }

        /// <summary>A summed push no faster than <see cref="MaxPushSpeed"/>, its direction kept.</summary>
        public static Vector3 CapPush(Vector3 push) =>
            push.sqrMagnitude > MaxPushSpeed * MaxPushSpeed ? push.normalized * MaxPushSpeed : push;

        /// <summary>
        /// Spec 054b design 4 (display only): where to draw a remote player so it never overlaps the own drawn body by more than
        /// <see cref="MaxDrawnOverlap"/>; pushed out along the line between the two, height unchanged. Far enough away: unchanged.
        /// </summary>
        public static Vector3 ClampDrawn(Vector3 remote, Vector3 own)
        {
            if (!Level(remote, own)) return remote;
            float dx = remote.x - own.x;
            float dz = remote.z - own.z;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq >= DrawnMinDistance * DrawnMinDistance) return remote;
            float distance = Mathf.Sqrt(distanceSq);
            if (distance < Coincident) return remote; // no direction to push in; the separation splits them within a few ticks
            float scale = DrawnMinDistance / distance;
            return new Vector3(own.x + dx * scale, remote.y, own.z + dz * scale);
        }

        /// <summary>
        /// Spec 054b design 4 (display only): a remote player's drawn offset from its root, one frame (<paramref name="dt"/> s) on towards
        /// <paramref name="wanted"/> at no more than <see cref="DrawnOffsetSpeed"/>. The clamp is measured from where it is drawn now
        /// (root plus this offset), so the push-out keeps its side while the root passes through the own centre.
        /// </summary>
        public static Vector3 FollowOffset(Vector3 current, Vector3 wanted, float dt) =>
            Vector3.MoveTowards(current, wanted, DrawnOffsetSpeed * Mathf.Max(0f, dt));

        /// <summary>Whether a remote player drawn at <paramref name="remote"/> would sink into the own body more than
        /// <see cref="MaxDrawnOverlap"/>.</summary>
        public static bool DrawnTooClose(Vector3 remote, Vector3 own)
        {
            float dx = remote.x - own.x, dz = remote.z - own.z;
            return Level(remote, own) && dx * dx + dz * dz < DrawnMinDistance * DrawnMinDistance;
        }

        /// <summary>Whether two players are level enough to meet.</summary>
        public static bool Level(Vector3 a, Vector3 b) => Mathf.Abs(a.y - b.y) < VerticalReach;

        private static Vector3 Away(float dx, float dz, float distance, int selfId, int otherId)
        {
            if (distance >= Coincident) return new Vector3(dx / distance, 0f, dz / distance);
            if (selfId == otherId) return Vector3.zero;
            return new Vector3(selfId < otherId ? 1f : -1f, 0f, 0f);
        }
    }
}
