using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Dev;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027: the scripted intents behave like the device inputs (one-shot jump, per-frame look, clamped move).</summary>
    public class ScriptedIntentSourceTests
    {
        [Test]
        public void Jump_IsConsumedOnce()
        {
            var source = new ScriptedIntentSource();
            Assert.IsFalse(source.ConsumeJump());
            source.Jump();
            Assert.IsTrue(source.ConsumeJump());
            Assert.IsFalse(source.ConsumeJump());
        }

        [Test]
        public void Look_AddsUpUntilConsumed()
        {
            var source = new ScriptedIntentSource();
            source.AddLook(new Vector2(3f, -1f));
            source.AddLook(new Vector2(2f, 0.5f));
            source.AddLook(new Vector2(float.NaN, 1f)); // ignored
            Assert.AreEqual(new Vector2(5f, -0.5f), source.ConsumeLook());
            Assert.AreEqual(Vector2.zero, source.ConsumeLook());
        }

        [Test]
        public void Move_IsClampedLikeAStick_AndStopKeepsStanceAndView()
        {
            var source = new ScriptedIntentSource { Move = new Vector2(3f, 4f), Sprint = true, Crouch = true, View = ViewMode.ThirdPerson };
            Assert.AreEqual(1f, source.Move.magnitude, 1e-5f);
            source.Move = new Vector2(float.NaN, 0f);
            Assert.AreEqual(Vector2.zero, source.Move);
            source.Move = Vector2.up;
            source.Stop();
            Assert.AreEqual(Vector2.zero, source.Move);
            Assert.IsFalse(source.Sprint);
            Assert.IsTrue(source.Crouch);
            Assert.AreEqual(ViewMode.ThirdPerson, source.View);
        }
    }
}
