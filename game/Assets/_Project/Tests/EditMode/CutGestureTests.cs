using NUnit.Framework;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 019 AC5: mouse drags to knife strokes (the Input System's y is positive up).</summary>
    public class CutGestureTests
    {
        /// <summary>Feeds <paramref name="frames"/> frames of <paramref name="deltaY"/>; returns the strokes fired.</summary>
        private static int Feed(CutGesture gesture, int frames, float deltaY, bool armed = true, bool held = true)
        {
            int strokes = 0;
            for (int i = 0; i < frames; i++)
                if (gesture.Update(armed, held, deltaY)) strokes++;
            return strokes;
        }

        [Test]
        public void FortyCountsDown_IsOneStroke()
        {
            var gesture = new CutGesture();
            Assert.AreEqual(0, Feed(gesture, 3, -10f), "30 counts are not enough");
            Assert.IsTrue(gesture.Update(true, true, -10f), "the 40th count fires");
            Assert.IsTrue(gesture.IsWaitingForRearm);
        }

        [Test]
        public void OneBigFrame_IsOneStroke()
        {
            Assert.AreEqual(1, Feed(new CutGesture(), 1, -40f));
        }

        [Test]
        public void HundredTwentyCountsDown_WithoutReset_IsOneStroke()
        {
            Assert.AreEqual(1, Feed(new CutGesture(), 12, -10f));
            Assert.AreEqual(1, Feed(new CutGesture(), 1, -120f));
        }

        [Test]
        public void Release_Rearms()
        {
            var gesture = new CutGesture();
            Assert.AreEqual(1, Feed(gesture, 4, -10f));
            Assert.AreEqual(0, Feed(gesture, 1, 0f, held: false));
            Assert.IsFalse(gesture.IsWaitingForRearm);
            Assert.AreEqual(1, Feed(gesture, 4, -10f), "the next drag cuts again");
        }

        [Test]
        public void TwentyCountsUp_Rearms_WithoutReleasing()
        {
            var gesture = new CutGesture();
            Assert.AreEqual(1, Feed(gesture, 1, -40f));
            Assert.AreEqual(0, Feed(gesture, 1, 15f), "15 up is not enough");
            Assert.IsTrue(gesture.IsWaitingForRearm);
            Assert.AreEqual(0, Feed(gesture, 1, 5f));
            Assert.IsFalse(gesture.IsWaitingForRearm, "20 up re-arms");
            Assert.AreEqual(1, Feed(gesture, 1, -40f));
        }

        [Test]
        public void UpOrSidewaysOnly_IsNoStroke()
        {
            Assert.AreEqual(0, Feed(new CutGesture(), 50, 10f), "up");
            Assert.AreEqual(0, Feed(new CutGesture(), 50, 0f), "sideways (no vertical movement)");
        }

        [Test]
        public void MovingUpFirst_DoesNotBankAStroke()
        {
            var gesture = new CutGesture();
            Feed(gesture, 1, 100f);
            Assert.AreEqual(0, Feed(gesture, 1, -30f));
            Assert.AreEqual(1, Feed(gesture, 1, -10f));
        }

        [Test]
        public void NotArmed_IsNoStroke()
        {
            // No knife in the hands, or no board with food under the dot.
            Assert.AreEqual(0, Feed(new CutGesture(), 10, -40f, armed: false));
            // Button not held: a plain mouse move does not cut (open question 2).
            Assert.AreEqual(0, Feed(new CutGesture(), 10, -40f, held: false));
        }

        [Test]
        public void LosingTheArm_ResetsTheSum()
        {
            var gesture = new CutGesture();
            Feed(gesture, 3, -10f);
            Feed(gesture, 1, -10f, armed: false);
            Assert.AreEqual(0, Feed(gesture, 3, -10f), "the 30 counts from before do not count");
            Assert.AreEqual(1, Feed(gesture, 1, -10f));
        }

        [Test]
        public void BadDeltas_AreIgnored()
        {
            var gesture = new CutGesture();
            Assert.AreEqual(0, Feed(gesture, 1, float.NaN));
            Assert.AreEqual(0, Feed(gesture, 1, float.NegativeInfinity));
            Assert.AreEqual(1, Feed(gesture, 1, -40f));
        }
    }
}
