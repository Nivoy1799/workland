using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 054b AC2 (D48): the soft separation. Two overlapping players separate within 0.3 s; a walker cannot pass through a
    /// standing player but slides around them; not even two sprinters head-on pass through each other; the push is symmetric and
    /// deterministic. The players are simulated the way the server runs them: one replicate after the other at the 30 Hz tick.
    /// </summary>
    public class PlayerSeparationTests
    {
        private const float Dt = 1f / 30f;
        private const int A = 7, B = 9; // object ids

        private struct Run
        {
            public Vector3 A, B;
            public float MinDistance;
            public bool Crossed; // the walker was ever past the other along x (dead-centre runs)
        }

        /// <summary>Ticks two players: A moves at <paramref name="velocityA"/>, then B (it sees A's new position, like the server), or both
        /// from the same positions (<paramref name="together"/>, like two clients each predicting).</summary>
        private static Run Simulate(Vector3 a, Vector3 b, Vector3 velocityA, Vector3 velocityB, float seconds, bool together = false)
        {
            var run = new Run { MinDistance = float.MaxValue };
            int ticks = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                Vector3 nextA = a + PlayerSeparation.Step(a, velocityA * Dt, b, A, B, Dt);
                Vector3 seenByB = together ? a : nextA;
                Vector3 nextB = b + PlayerSeparation.Step(b, velocityB * Dt, seenByB, B, A, Dt);
                a = nextA;
                b = nextB;
                run.MinDistance = Mathf.Min(run.MinDistance, Flat(a, b));
                if (Mathf.Abs(a.z - b.z) < 0.01f && a.x > b.x) run.Crossed = true;
            }
            run.A = a;
            run.B = b;
            return run;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void TwoOverlappingPlayers_SeparateWithinAThirdOfASecond()
        {
            Vector3 a = new Vector3(-0.1f, 0f, 0f), b = new Vector3(0.1f, 0f, 0f);
            int ticks = 0;
            while (Flat(a, b) < PlayerSeparation.ContactDistance - 0.01f && ticks < 100)
            {
                // Both from the same positions (each player's own replicate); the server's one-after-the-other order is in the runs below.
                Vector3 nextA = a + PlayerSeparation.Step(a, Vector3.zero, b, A, B, Dt);
                b += PlayerSeparation.Step(b, Vector3.zero, a, B, A, Dt);
                a = nextA;
                ticks++;
            }
            Assert.LessOrEqual(ticks * Dt, 0.3f + 1e-4f, "separated (within 1 cm of contact) after " + ticks + " ticks");
            Assert.AreEqual(0f, a.z, 1e-6f, "pushed along the line between them only");
            Assert.AreEqual(-a.x, b.x, 1e-5f, "both moved apart by the same");
        }

        [Test]
        public void TwoPlayersOnTheSameSpot_SplitByTheirIds()
        {
            Vector3 spot = new Vector3(1f, 0f, 2f);
            Vector3 pushA = PlayerSeparation.PushVelocity(spot, spot, A, B);
            Vector3 pushB = PlayerSeparation.PushVelocity(spot, spot, B, A);
            Assert.AreEqual(PlayerSeparation.MaxPushSpeed, pushA.x, 1e-5f, "the lower id goes +x at full push");
            Assert.AreEqual(-pushA, pushB);
            Assert.AreEqual(Vector3.zero, PlayerSeparation.PushVelocity(spot, spot, A, A), "a player never pushes itself");
        }

        [Test]
        public void AWalker_CannotPassThroughAStandingPlayer_ButSlidesAroundThem()
        {
            Vector3 walk = new Vector3(MovementSpeed.Walk, 0f, 0f);
            // A little off the centre line: the push turns sideways and the walker slides round, then walks on.
            Run off = Simulate(new Vector3(-2f, 0f, 0.1f), Vector3.zero, walk, Vector3.zero, 3f);
            Assert.Greater(off.MinDistance, 2f * PlayerSeparation.BodyRadius - 0.05f, "the bodies never sink into each other");
            Assert.Greater(off.A.x, off.B.x + PlayerSeparation.ContactDistance, "the walker got round and walked on");
            Assert.Greater(off.A.z, off.B.z, "round the side it came in on");
            Assert.Less(Flat(off.B, Vector3.zero), 1f, "the standing player is nudged, not carried away");

            // Dead on the centre line there is nothing to slide along: the walker still never gets through.
            Run centre = Simulate(new Vector3(-2f, 0f, 0f), Vector3.zero, walk, Vector3.zero, 3f);
            Assert.IsFalse(centre.Crossed, "never past the other");
            Assert.Greater(centre.MinDistance, PlayerSeparation.MinDistance);

            // Sprinting into them is no different.
            Run sprint = Simulate(new Vector3(-2f, 0f, 0f), Vector3.zero, new Vector3(MovementSpeed.Sprint, 0f, 0f), Vector3.zero, 3f);
            Assert.IsFalse(sprint.Crossed);
            Assert.Greater(sprint.MinDistance, PlayerSeparation.MinDistance);
        }

        [Test]
        public void TwoSprintersHeadOn_DoNotPassThroughEachOther()
        {
            Vector3 east = new Vector3(MovementSpeed.Sprint, 0f, 0f);
            // Both predicting from the same positions (the worst case: neither sees the other's step).
            Run run = Simulate(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), east, -east, 2f, together: true);
            Assert.IsFalse(run.Crossed);
            Assert.Greater(run.MinDistance, PlayerSeparation.MinDistance - 2f * MovementSpeed.Sprint * Dt, "at most one step each past the floor");
            Assert.Less(run.A.x, run.B.x);
        }

        [Test]
        public void TheFloor_KeepsTheSideways_PartOfTheStep()
        {
            Vector3 self = new Vector3(-0.4f, 0f, 0f), other = Vector3.zero;
            Vector3 step = new Vector3(0.1f, 0f, 0.1f); // into the other and along its edge
            Vector3 blocked = PlayerSeparation.Block(self, step, other, A, B);
            Assert.AreEqual(0f, blocked.x, 1e-6f, "nothing towards the other");
            Assert.AreEqual(0.1f, blocked.z, 1e-6f, "the slide stays");
            Vector3 away = new Vector3(-0.1f, 0f, 0f);
            Assert.AreEqual(away, PlayerSeparation.Block(self, away, other, A, B), "stepping away is never blocked");
            Assert.AreEqual(step, PlayerSeparation.Block(new Vector3(-2f, 0f, 0f), step, other, A, B), "far away: unchanged");
        }

        [Test]
        public void TheViewTick_FeedsOnlyTheSoftPush_TheFloorAndTheDeepPushUseRealPositions()
        {
            // PlayerSeparation.Separate is what PlayerController runs on the server (seen = the history at the input's view tick).
            Vector3 self = Vector3.zero;
            Vector3 step = new Vector3(0.1f, 0f, 0f); // walking east
            int[] ids = { 2 };
            // Claimed view far away, but the other really stands 0.48 m ahead: the floor (and the deep push) still stop the walker.
            Vector3 blocked = PlayerSeparation.Separate(self, step, 1, new[] { new Vector3(3f, 0f, 0f) }, new[] { new Vector3(0.48f, 0f, 0f) },
                ids, 0, 1, Dt);
            Vector3 otherClaim = PlayerSeparation.Separate(self, step, 1, new[] { new Vector3(-3f, 0f, 1f) }, new[] { new Vector3(0.48f, 0f, 0f) },
                ids, 0, 1, Dt);
            Assert.AreEqual(blocked, otherClaim, "really deep in contact, the claimed view changes nothing");
            Assert.GreaterOrEqual(0.48f - blocked.x, PlayerSeparation.MinDistance, "a forged view cannot walk anyone through a player");
            Vector3 atTheFloor = PlayerSeparation.Separate(self, step, 1, new[] { new Vector3(3f, 0f, 0f) }, new[] { new Vector3(0.46f, 0f, 0f) },
                ids, 0, 1, Dt);
            Assert.LessOrEqual(atTheFloor.x, 0f, "at the floor no step towards the real position");
            // Claimed view right ahead, but the other really is far away: only the soft push follows the claim, capped, and away from it.
            Vector3 pushed = PlayerSeparation.Separate(self, Vector3.zero, 1, new[] { new Vector3(0.55f, 0f, 0f) }, new[] { new Vector3(5f, 0f, 0f) },
                ids, 0, 1, Dt);
            Assert.Less(pushed.x, 0f, "the soft push follows where the player saw the other");
            Assert.LessOrEqual(pushed.magnitude, PlayerSeparation.MaxPushSpeed * Dt + 1e-6f);
            // The same arrays from an offset (the owner's per-tick ring).
            Vector3[] ring = { new Vector3(9f, 0f, 9f), new Vector3(0.48f, 0f, 0f) };
            Assert.AreEqual(blocked.x, PlayerSeparation.Separate(self, step, 1, ring, ring, new[] { 7, 2 }, 1, 1, Dt).x, 1e-6f, "index 1 only");
        }

        [Test]
        public void TheSummedPush_IsCapped_NoCrowdShove()
        {
            // Three players crowd one from the same side: the pushes add up to more than MaxPushSpeed, the sum is capped.
            Vector3 self = Vector3.zero;
            Vector3[] others = { new Vector3(0.3f, 0f, 0f), new Vector3(0.3f, 0f, 0.1f), new Vector3(0.3f, 0f, -0.1f) };
            Vector3 step = PlayerSeparation.Separate(self, Vector3.zero, 1, others, others, new[] { 2, 3, 4 }, 0, 3, Dt);
            Assert.AreEqual(PlayerSeparation.MaxPushSpeed * Dt, step.magnitude, 1e-5f, "one full push, not three");
            Assert.Less(step.x, 0f, "away from the crowd");
            Assert.AreEqual(Vector3.zero, PlayerSeparation.CapPush(Vector3.zero));
            Assert.AreEqual(new Vector3(1f, 0f, 0f), PlayerSeparation.CapPush(new Vector3(1f, 0f, 0f)), "under the cap: unchanged");
        }

        [Test]
        public void AWalker_CannotSqueezeBetweenTwoStandingPlayers()
        {
            // Two players stand 0.6 m apart (centres); a walker aims straight at the gap between them: three players, one floor each.
            Vector3 walker = new Vector3(-2f, 0f, 0f);
            Vector3[] standing = { new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, -0.3f) };
            int[] ids = { 2, 3 };
            float closest = float.MaxValue;
            for (int tick = 0; tick < 90; tick++)
            {
                walker += PlayerSeparation.Separate(walker, new Vector3(MovementSpeed.Walk * Dt, 0f, 0f), 1, standing, standing, ids, 0, 2, Dt);
                for (int k = 0; k < 2; k++)
                {
                    Vector3[] seenByK = { walker, standing[1 - k] };
                    standing[k] += PlayerSeparation.Separate(standing[k], Vector3.zero, ids[k], seenByK, seenByK, new[] { 1, ids[1 - k] }, 0, 2, Dt);
                }
                foreach (Vector3 s in standing) closest = Mathf.Min(closest, Flat(walker, s));
            }
            Assert.GreaterOrEqual(closest, PlayerSeparation.MinDistance - MovementSpeed.Walk * Dt, "the floor holds against both");
            Assert.IsTrue(walker.x < standing[0].x || walker.x < standing[1].x || Mathf.Abs(standing[0].z - standing[1].z) > PlayerSeparation.ContactDistance,
                "the walker only gets through once the two have been pushed a body apart");
        }

        [Test]
        public void ADeepRealOverlap_PushesAtFullStrength_FromTheRealPosition()
        {
            Vector3 self = Vector3.zero;
            Vector3 real = new Vector3(0.4f, 0f, 0f);  // bodies 20 cm into each other
            Vector3 seen = new Vector3(-0.6f, 0f, 0f); // seen late, on the other side
            Vector3 push = PlayerSeparation.ContactPush(self, seen, real, 1, 2);
            Assert.AreEqual(-PlayerSeparation.MaxPushSpeed, push.x, 1e-5f, "away from where it really is, at full push");
            Vector3 shallow = new Vector3(0.55f, 0f, 0f);
            Assert.AreEqual(PlayerSeparation.PushVelocity(self, seen, 1, 2), PlayerSeparation.ContactPush(self, seen, shallow, 1, 2),
                "not deep: the push from the seen position");
            Assert.AreEqual(0.5f, PlayerSeparation.DeepDistance, 1e-6f, "10 cm of body overlap");
            Assert.Greater(PlayerSeparation.DeepDistance, PlayerSeparation.MinDistance);
        }

        [Test]
        public void ThePush_IsSymmetric_ClampedAndOnlyInContact()
        {
            Vector3[] pairs =
            {
                new Vector3(0.3f, 0f, 0.2f), new Vector3(-0.1f, 0.4f, 0.05f), new Vector3(0.65f, 0f, 0f), new Vector3(0.05f, 0f, -0.02f),
            };
            Vector3 origin = new Vector3(3f, 0.5f, -1f);
            foreach (Vector3 d in pairs)
            {
                Vector3 other = origin + d;
                Vector3 onSelf = PlayerSeparation.PushVelocity(origin, other, A, B);
                Vector3 onOther = PlayerSeparation.PushVelocity(other, origin, B, A);
                Assert.AreEqual(-onSelf.x, onOther.x, 1e-6f, "symmetric for " + d);
                Assert.AreEqual(-onSelf.z, onOther.z, 1e-6f);
                Assert.AreEqual(0f, onSelf.y, "horizontal only");
                Assert.LessOrEqual(onSelf.magnitude, PlayerSeparation.MaxPushSpeed + 1e-5f);
                float overlap = PlayerSeparation.ContactDistance - Flat(origin, other);
                Assert.AreEqual(Mathf.Min(overlap * PlayerSeparation.Stiffness, PlayerSeparation.MaxPushSpeed), onSelf.magnitude, 1e-4f);
                Assert.Greater(Vector3.Dot(onSelf, new Vector3(-d.x, 0f, -d.z)), 0f, "pushed away from the other");
            }
            Assert.AreEqual(Vector3.zero, PlayerSeparation.PushVelocity(origin, origin + new Vector3(0.7f, 0f, 0f), A, B), "touching is not overlapping");
            Assert.AreEqual(Vector3.zero, PlayerSeparation.PushVelocity(origin, origin + new Vector3(0.1f, 2f, 0f), A, B), "two metres higher: no contact");
        }

        [Test]
        public void TheSeparation_IsDeterministic()
        {
            Vector3 walk = new Vector3(MovementSpeed.Walk, 0f, 0.3f);
            Run first = Simulate(new Vector3(-2f, 0f, 0.07f), new Vector3(0.1f, 0f, 0f), walk, new Vector3(-0.5f, 0f, 0f), 2f);
            Run second = Simulate(new Vector3(-2f, 0f, 0.07f), new Vector3(0.1f, 0f, 0f), walk, new Vector3(-0.5f, 0f, 0f), 2f);
            Assert.AreEqual(first.A, second.A, "same inputs, same positions, to the bit");
            Assert.AreEqual(first.B, second.B);
            Assert.AreEqual(first.MinDistance, second.MinDistance);
        }

        [Test]
        public void ARemotePlayer_IsDrawnAtMostFiveCentimetresIntoTheOwnBody()
        {
            Vector3 own = new Vector3(1f, 0f, 1f);
            Vector3 deep = own + new Vector3(0.2f, 0.1f, 0f);
            Vector3 drawn = PlayerSeparation.ClampDrawn(deep, own);
            Assert.AreEqual(PlayerSeparation.DrawnMinDistance, Flat(drawn, own), 1e-5f);
            Assert.AreEqual(2f * PlayerSeparation.BodyRadius - PlayerSeparation.MaxDrawnOverlap, PlayerSeparation.DrawnMinDistance, 1e-6f);
            Assert.AreEqual(deep.y, drawn.y, "the height is kept");
            Assert.Greater(drawn.x, own.x, "pushed out on its own side");
            Vector3 clear = own + new Vector3(0.6f, 0f, 0f);
            Assert.AreEqual(clear, PlayerSeparation.ClampDrawn(clear, own), "not touching: drawn where it is");
            Vector3 above = own + new Vector3(0.1f, 2f, 0f);
            Assert.AreEqual(above, PlayerSeparation.ClampDrawn(above, own), "not level: unchanged");
        }

        [Test]
        public void ARemotePlayerPassingTheOwnCentre_IsKeptOut_AndNeverJumps()
        {
            // PlayerController.LateUpdate's display rule, frame by frame at 60 fps: a remote root walks straight through the own body.
            Vector3 own = Vector3.zero, offset = Vector3.zero, lastDrawn = new Vector3(1.2f, 0f, 0.02f);
            const float frame = 1f / 60f;
            float closestWhileDeep = float.MaxValue;
            for (float x = 1.2f; x > -2f; x -= 2.5f * frame)
            {
                Vector3 root = new Vector3(x, 0f, 0.02f);
                Vector3 wanted = PlayerSeparation.DrawnTooClose(root, own) ? PlayerSeparation.ClampDrawn(root + offset, own) - root : Vector3.zero;
                offset = PlayerSeparation.FollowOffset(offset, wanted, frame);
                Vector3 drawn = root + offset;
                Assert.LessOrEqual((drawn - lastDrawn).magnitude, 2.5f * frame + PlayerSeparation.DrawnOffsetSpeed * frame + 1e-4f, "no jump at x " + x);
                if (Mathf.Abs(x) < 0.3f) closestWhileDeep = Mathf.Min(closestWhileDeep, Flat(drawn, own));
                lastDrawn = drawn;
            }
            Assert.AreEqual(Vector3.zero, offset, "drawn at its root again once clear");
            Assert.GreaterOrEqual(closestWhileDeep, PlayerSeparation.DrawnMinDistance - 1e-3f, "kept out while its root is deep inside");
        }

        /// <summary>MovementRules' speeds, named for the tests (walk 2.5, sprint 5 m/s).</summary>
        private static class MovementSpeed
        {
            public const float Walk = Worktown.Game.Player.MovementRules.WalkSpeed;
            public const float Sprint = Worktown.Game.Player.MovementRules.SprintSpeed;
        }
    }
}
