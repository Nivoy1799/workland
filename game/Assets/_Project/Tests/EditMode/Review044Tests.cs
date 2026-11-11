using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Player;
using Worktown.Game.Shop;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 045: the 044 review follow-ups (the button press rule, the arm order, two public statics).</summary>
    public class Review044Tests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            EnabledWipeViews().Clear();
            foreach (GameObject go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            _objects.Add(go);
            return go;
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, field);
            f.SetValue(target, value);
        }

        private static IList EnabledWipeViews()
        {
            FieldInfo f = typeof(SpillWipeView).GetField("Enabled", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f);
            return (IList)f.GetValue(null);
        }

        [Test]
        public void ButtonPress_AFlagsChangeSoonAfterOurPress_IsOurOwn_LaterOrWithoutAPressItIsSomeoneElses()
        {
            Assert.IsTrue(ButtonPressRules.IsOwnConfirm(10.2f, 10f), "our press, confirmed by the server");
            Assert.IsTrue(ButtonPressRules.IsOwnConfirm(10f + ButtonPressRules.OwnPressWindow, 10f), "the window's edge still counts");
            Assert.IsFalse(ButtonPressRules.IsOwnConfirm(11.5f, 10f), "too late: someone else pressed it");
            Assert.IsFalse(ButtonPressRules.IsOwnConfirm(3f, float.NegativeInfinity), "no own press at all");
        }

        [Test]
        public void CharacterVisual_TurnsTheArms_AfterThePickupsSetTheirTargets()
        {
            var visual = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(CharacterVisual), typeof(DefaultExecutionOrder));
            Assert.IsNotNull(visual, "the arms read this frame's box and cloth targets");
            var pickup = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(Pickup), typeof(DefaultExecutionOrder));
            Assert.Greater(visual.order, pickup != null ? pickup.order : 0);
        }

        [Test]
        public void CleaningExitPoint_IsTheOpening_TheBasin_OrAboveTheStation()
        {
            Assert.AreEqual(Vector3.zero, CleaningService.ExitPoint(null));

            Station plain = Make("counter", new Vector3(1f, 0f, 2f)).AddComponent<Station>();
            SetField(plain, "_kind", StationKind.Counter);
            Assert.AreEqual(new Vector3(1f, 0.5f, 2f), CleaningService.ExitPoint(plain));

            GameObject binGo = Make("bin", new Vector3(-3f, 0f, 0f));
            Station bin = binGo.AddComponent<Station>();
            SetField(bin, "_kind", StationKind.Bin);
            BinView binView = binGo.AddComponent<BinView>();
            Assert.AreEqual(binView.Opening, CleaningService.ExitPoint(bin), "the bag goes in at the bin's opening");
            Assert.AreEqual(new Vector3(-3f, 1.1f, 0f), CleaningService.ExitPoint(bin));

            GameObject sinkGo = Make("sink", new Vector3(5f, 0f, 0f));
            Station sink = sinkGo.AddComponent<Station>();
            SetField(sink, "_kind", StationKind.Sink);
            GameObject basinGo = Make("basin", new Vector3(5.2f, 0.9f, 0.1f));
            basinGo.transform.SetParent(sinkGo.transform, true);
            StockSlot basin = basinGo.AddComponent<StockSlot>();
            SetField(basin, "_station", sink);
            Vector3 point = CleaningService.ExitPoint(sink);
            Assert.AreEqual(5.2f, point.x, 1e-5f);
            Assert.AreEqual(0.95f, point.y, 1e-5f, "a little above the basin");
            Assert.AreEqual(0.1f, point.z, 1e-5f);
        }

        [Test]
        public void TryClothFor_OnlyAWipeBeingDrawnNearTheHolder_LendsItsClothPose()
        {
            Assert.IsFalse(SpillWipeView.TryClothFor(Vector3.zero, out Pose none), "nobody wipes");
            Assert.AreEqual(default(Pose), none);

            SpillWipeView view = Make("spill", new Vector3(2f, 1f, 0f)).AddComponent<SpillWipeView>();
            EnabledWipeViews().Add(view);
            Assert.IsFalse(SpillWipeView.TryClothFor(new Vector3(2f, 0f, 0.5f), out _), "the puddle is not being wiped");

            SetField(view, "_shown", true);
            Assert.IsTrue(SpillWipeView.TryClothFor(new Vector3(2f, 0f, 0.5f), out Pose cloth), "a holder at the wiped dispenser");
            Assert.Less(Vector2.Distance(new Vector2(cloth.position.x, cloth.position.z), new Vector2(2f, 0f)), 0.5f, "on the puddle");
            float far = SpillWipeView.WiperRange + 0.1f;
            Assert.IsFalse(SpillWipeView.TryClothFor(new Vector3(2f + far, 0f, 0f), out _), "too far away to be the one who wipes");
        }
    }
}
