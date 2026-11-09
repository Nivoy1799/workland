using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027d AC2: when world text shows, how big it is, and which of two overlapping labels stays.</summary>
    public class LabelRulesTests
    {
        private const float Front = 1f, Behind = -1f;

        [Test]
        public void OneSidedLabels_AreHiddenFromBehindAndEdgeOn()
        {
            foreach (LabelKind kind in new[] { LabelKind.Sign, LabelKind.Rail, LabelKind.Button })
            {
                Assert.IsTrue(LabelRules.IsVisible(kind, 2f, Front, 90f, false, false, false), kind + " from the front");
                Assert.IsFalse(LabelRules.IsVisible(kind, 2f, Behind, 90f, false, false, false), kind + " from behind");
                Assert.IsFalse(LabelRules.IsVisible(kind, 2f, 0.19f, 90f, false, false, false), kind + " edge on");
                Assert.IsTrue(LabelRules.IsVisible(kind, 2f, 0.2f, 90f, false, false, false), kind + " just enough");
            }
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Bubble, 2f, Behind, 90f, false, false, false), "a billboard has no back");
        }

        [Test]
        public void Visibility_FollowsTheRangeOfTheKind()
        {
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Bubble, 7.9f, 1f, 90f, false, false, false));
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Bubble, 8.1f, 1f, 90f, false, false, false));
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Sign, 20f, 1f, 90f, false, false, false));
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Button, 5f, 1f, 90f, false, false, false));
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Sign, float.NaN, 1f, 90f, false, false, false));
        }

        [Test]
        public void ItemLabels_ShowOnlyForTheAimedOrHeldItem_AndNotHeldInFirstPerson()
        {
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Item, 2f, 1f, 90f, false, false, false), "a loose item shows nothing");
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Item, 2f, 1f, 90f, false, true, false), "aimed");
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Item, 2f, 1f, 90f, false, false, true), "held in third person");
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Item, 2f, 1f, 90f, true, false, true), "held in first person");
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Item, 7f, 1f, 90f, false, true, false), "too far");
        }

        [Test]
        public void FirstPerson_KeepsTheViewCentreClear_ExceptForTheAimedItem()
        {
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Sign, 3f, 1f, 14.9f, true, false, false));
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Sign, 3f, 1f, 15f, true, false, false));
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Sign, 3f, 1f, 5f, false, false, false), "third person has no centre zone");
            Assert.IsTrue(LabelRules.IsVisible(LabelKind.Item, 1.5f, 1f, 2f, true, true, false), "the aimed item label stays");
            Assert.IsFalse(LabelRules.IsVisible(LabelKind.Bubble, 3f, 1f, 3f, true, false, false));
        }

        [Test]
        public void AngleFromCentre_IsTheAngleToTheView()
        {
            Assert.AreEqual(0f, LabelRules.AngleFromCentre(Vector3.forward, new Vector3(0f, 0f, 5f)), 1e-3f);
            Assert.AreEqual(90f, LabelRules.AngleFromCentre(Vector3.forward, Vector3.right), 1e-3f);
            Assert.AreEqual(180f, LabelRules.AngleFromCentre(Vector3.forward, Vector3.zero), "a label at the eye counts as out of the way");
        }

        [Test]
        public void SizeGrowsWithDistance_SignsFromOneMetre_BubblesFromThree()
        {
            Assert.AreEqual(1f, LabelRules.DistanceScale(LabelKind.Sign, 0.5f));
            Assert.AreEqual(1f, LabelRules.DistanceScale(LabelKind.Sign, 1f));
            Assert.AreEqual(2f, LabelRules.DistanceScale(LabelKind.Sign, 2f), 1e-5f);
            Assert.AreEqual(LabelRules.SignMaxScale, LabelRules.DistanceScale(LabelKind.Sign, 20f));
            Assert.AreEqual(1f, LabelRules.DistanceScale(LabelKind.Bubble, 3f));
            Assert.AreEqual(2f, LabelRules.DistanceScale(LabelKind.Bubble, 6f), 1e-5f);
            Assert.AreEqual(1f, LabelRules.DistanceScale(LabelKind.Item, 5f));
        }

        [Test]
        public void Priority_RunsAimedHeldRingHeadNextRest()
        {
            int aimed = LabelRules.Priority(LabelKind.Item, true, false, -1);
            int held = LabelRules.Priority(LabelKind.Item, false, true, -1);
            int head = LabelRules.Priority(LabelKind.Bubble, false, false, 0);
            int next = LabelRules.Priority(LabelKind.Bubble, false, false, 1);
            int sign = LabelRules.Priority(LabelKind.Sign, false, false, -1);
            int rest = LabelRules.Priority(LabelKind.Bubble, false, false, 4);
            Assert.Less(aimed, held);
            Assert.Less(held, LabelRules.PriorityRing, "the ring sits just below the aimed target and the held item");
            Assert.AreEqual(LabelRules.PriorityRing + 1, head, "the ring is above every label");
            Assert.Less(head, next);
            Assert.Less(next, sign);
            Assert.Less(sign, rest);
            Assert.AreEqual(aimed, LabelRules.Priority(LabelKind.Bubble, true, true, 0), "aimed beats everything");
        }

        [Test]
        public void OnlyTheQueueHeadAndTheNextGuestShowTheFullOrder()
        {
            Assert.IsTrue(LabelRules.ShowsFullText(0, true));
            Assert.IsTrue(LabelRules.ShowsFullText(1, true));
            Assert.IsFalse(LabelRules.ShowsFullText(2, true));
            Assert.IsTrue(LabelRules.ShowsFullText(7, false), "a guest leaving with a remark says it in full");
            Assert.AreEqual("T", LabelRules.Icon("Tomato salad"));
            Assert.AreEqual("V", LabelRules.Icon("  veggie mix"));
            Assert.AreEqual("?", LabelRules.Icon(string.Empty));
            Assert.AreEqual("?", LabelRules.Icon("123"));
        }

        [Test]
        public void Cull_KeepsTheHigherPriorityOfTwoOverlappingLabels()
        {
            var rects = new[] { new Rect(0, 0, 100, 40), new Rect(50, 10, 100, 40), new Rect(300, 0, 100, 40) };
            var keep = new bool[3];
            LabelRules.Cull(rects, new[] { 5, 3, 5 }, new[] { 1f, 9f, 1f }, keep);
            Assert.AreEqual(new[] { false, true, true }, keep, "the priority 3 label wins although it is further away");
        }

        [Test]
        public void Cull_TiesGoToTheNearerLabel_AndChainsAreHandledInOrder()
        {
            var rects = new[] { new Rect(0, 0, 100, 40), new Rect(80, 0, 100, 40), new Rect(160, 0, 100, 40) };
            var keep = new bool[3];
            LabelRules.Cull(rects, new[] { 5, 5, 5 }, new[] { 2f, 1f, 3f }, keep);
            Assert.AreEqual(new[] { false, true, false }, keep, "the middle one is nearest and takes both neighbours");
            LabelRules.Cull(rects, new[] { 5, 5, 5 }, new[] { 1f, 2f, 3f }, keep);
            Assert.AreEqual(new[] { true, false, true }, keep, "the first keeps, the second gives way, the third is free again");
        }

        [Test]
        public void Cull_DisjointLabelsAllStay_AndHeatRingsBlockLabels()
        {
            var keep = new bool[3];
            LabelRules.Cull(new[] { new Rect(0, 0, 10, 10), new Rect(20, 0, 10, 10), new Rect(40, 0, 10, 10) },
                new[] { 5, 6, 7 }, new[] { 1f, 1f, 1f }, keep);
            Assert.AreEqual(new[] { true, true, true }, keep);

            // A ring (priority ring, rect of its own) hides the sign it covers but never the other way round.
            var withRing = new bool[2];
            LabelRules.Cull(new[] { new Rect(0, 0, 60, 20), new Rect(30, 0, 40, 40) },
                new[] { LabelRules.PrioritySign, LabelRules.PriorityRing }, new[] { 1f, 0f }, withRing);
            Assert.AreEqual(new[] { false, true }, withRing);
            LabelRules.Cull(new[] { new Rect(30, 0, 40, 40), new Rect(0, 0, 60, 20) },
                new[] { LabelRules.PriorityAimed, LabelRules.PriorityRing }, new[] { 1f, 0f }, withRing);
            Assert.AreEqual(new[] { true, false }, withRing, "only the aimed target outranks the ring");
        }

        [Test]
        public void LabelCull_ReusedEveryFrame_GivesTheSameAnswerAsTheRule()
        {
            // Second review of 027d: the per-frame form keeps its buffers; it must decide exactly like LabelRules.Cull.
            var cull = new LabelCull();
            var rects = new[] { new Rect(0, 0, 10, 10), new Rect(5, 0, 10, 10), new Rect(30, 0, 5, 5), new Rect(32, 2, 5, 5) };
            var priorities = new[] { 5, 3, 5, 5 };
            var distances = new[] { 1f, 9f, 2f, 1f };
            var expected = new bool[4];
            LabelRules.Cull(rects, priorities, distances, expected);
            for (int frame = 0; frame < 3; frame++)
            {
                var keep = new bool[8];
                cull.Run(rects, priorities, distances, keep);
                for (int i = 0; i < 4; i++) Assert.AreEqual(expected[i], keep[i], "frame " + frame + ", rect " + i);
            }
            var many = new Rect[100];
            var p = new int[100];
            var d = new float[100];
            for (int i = 0; i < 100; i++) many[i] = new Rect(i * 20, 0, 10, 10);
            var keepMany = new bool[100];
            cull.Run(many, p, d, keepMany);
            foreach (bool k in keepMany) Assert.IsTrue(k, "the buffers grow when there are more labels");
        }

        [Test]
        public void Icon_ReturnsTheSameStringEachTime()
        {
            Assert.AreSame(LabelRules.Icon("tomato"), LabelRules.Icon("Tea"), "no new string per collapsed bubble per frame");
            Assert.AreEqual("É", LabelRules.Icon("éclair"), "other letters still work");
        }

        [Test]
        public void WorldTextShader_IsTheClientOnlyShader()
        {
            // Second review of 027d: a dedicated server build strips it (Editor/Build/ServerShaderStripping).
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Resources/Labels/WorldText.shader");
            Assert.IsNotNull(shader);
            Assert.AreEqual(Worktown.Game.Art.LabelMaterials.ShaderName, shader.name);
            Assert.IsTrue(Worktown.Game.Art.LabelMaterials.IsClientOnlyShader(shader.name));
            Assert.IsFalse(Worktown.Game.Art.LabelMaterials.IsClientOnlyShader("Universal Render Pipeline/Lit"));
        }
    }
}
