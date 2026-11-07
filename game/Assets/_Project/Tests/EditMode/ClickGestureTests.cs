using NUnit.Framework;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 020 AC3: a click is a short press with little movement; a hold, a drag or a cut stroke is none.</summary>
    public class ClickGestureTests
    {
        private const float Frame = 1f / 60f;

        /// <summary>Press at t = 0, hold for <paramref name="frames"/> frames moving (dx, dy) each, release. True if it clicked.</summary>
        private static bool Press(ClickGesture gesture, int frames, float dx = 0f, float dy = 0f)
        {
            float t = 0f;
            Assert.IsFalse(gesture.Update(true, 0f, 0f, t), "the press itself never clicks");
            for (int i = 0; i < frames; i++)
            {
                t += Frame;
                Assert.IsFalse(gesture.Update(true, dx, dy, t), "no click while held");
            }
            t += Frame;
            return gesture.Update(false, 0f, 0f, t);
        }

        [Test]
        public void ShortPress_IsAClick()
        {
            Assert.IsTrue(Press(new ClickGesture(), 5));
        }

        [Test]
        public void ClickFiresOnce_OnTheRelease()
        {
            var gesture = new ClickGesture();
            Assert.IsTrue(Press(gesture, 3));
            Assert.IsFalse(gesture.Update(false, 0f, 0f, 1f), "released: nothing more");
            Assert.IsTrue(Press(gesture, 3), "the next press clicks again");
        }

        [Test]
        public void LongPress_IsNoClick()
        {
            Assert.IsFalse(Press(new ClickGesture(), 30)); // 0.5 s
            Assert.IsTrue(Press(new ClickGesture(), 16), "just under 0.3 s still clicks");
        }

        [Test]
        public void PressWith15CountsOfMovement_IsNoClick()
        {
            Assert.IsFalse(Press(new ClickGesture(), 5, dx: 3f), "5 x 3 = 15 counts");
            Assert.IsFalse(Press(new ClickGesture(), 3, dx: 3f, dy: 4f), "3 x 5 = 15 counts, diagonal");
            Assert.IsTrue(Press(new ClickGesture(), 5, dx: 2.5f), "12.5 counts is still a click");
        }

        [Test]
        public void DownwardDragOverABoardWithFood_IsAStroke_NotAClick()
        {
            var click = new ClickGesture();
            var cut = new CutGesture();
            bool stroke = false;
            float t = 0f;
            click.Update(true, 0f, 0f, t);
            cut.Update(true, true, 0f);
            for (int i = 0; i < 8; i++)
            {
                t += Frame;
                float dy = -6f; // down: 8 x 6 = 48 counts, more than one stroke
                if (cut.Update(true, true, dy))
                {
                    stroke = true;
                    click.Cancel(); // what the interactor does when a stroke fires
                }
                Assert.IsFalse(click.Update(true, 0f, dy, t));
            }
            Assert.IsTrue(stroke, "the drag cut");
            Assert.IsFalse(click.Update(false, 0f, 0f, t + Frame), "and did not click");
        }

        [Test]
        public void CancelledPress_IsNoClick_TheNextOneIs()
        {
            var gesture = new ClickGesture();
            gesture.Update(true, 0f, 0f, 0f);
            gesture.Cancel(); // e.g. the press only locked the cursor
            Assert.IsFalse(gesture.Update(false, 0f, 0f, Frame));
            Assert.IsTrue(Press(gesture, 2));
        }

        [Test]
        public void BadMouseData_IsNoClick()
        {
            Assert.IsFalse(Press(new ClickGesture(), 2, dx: float.NaN));
            Assert.IsFalse(Press(new ClickGesture(), 2, dy: float.PositiveInfinity));
        }
    }
}
