using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Npc;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 022: the generated scene has the ticket rail above the counter (six cards, no colliders), wired to the
    /// customer service, and the UI has the shift summary panel. Network types are found by name (no FishNet reference).
    /// </summary>
    public class TicketRailSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private static readonly Type RailType = typeof(CustomerRules).Assembly.GetType("Worktown.Game.UI.TicketRailView");
        private static readonly Type SummaryType = typeof(CustomerRules).Assembly.GetType("Worktown.Game.UI.ShiftSummaryView");

        [Test]
        public void BuiltScene_HasTheTicketRail_AboveTheCounter_WiredToTheCustomers()
        {
            Assert.IsNotNull(RailType);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<Component> rails = VesselTestKit.AllOf(scene, RailType);
                Assert.AreEqual(1, rails.Count);
                Component rail = rails[0];
                var so = new SerializedObject(rail);
                Assert.AreEqual(CustomerRules.MaxVisible, so.FindProperty("_cards").arraySize, "one card per queued customer");
                Assert.AreEqual(CustomerRules.MaxVisible, so.FindProperty("_cardBacks").arraySize);
                Assert.IsNotNull(so.FindProperty("_title").objectReferenceValue);
                Assert.IsEmpty(rail.GetComponentsInChildren<Collider>(true), "the rail never blocks aiming");
                Assert.Greater(rail.transform.position.y, 2f, "above the counter");

                List<Component> services = VesselTestKit.AllOf(scene, typeof(CustomerService));
                Assert.AreEqual(1, services.Count);
                Assert.AreEqual(rail, new SerializedObject(services[0]).FindProperty("_rail").objectReferenceValue);

                Assert.IsNotNull(SummaryType);
                Assert.AreEqual(1, VesselTestKit.AllOf(scene, SummaryType).Count, "the shift summary panel");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Spec 044 (032 feels off): from the kitchen, the queue head's order bubble is not hidden behind the ticket rail. The lines
        /// from the cook's eye (first person, standing at the counter, in the aisle and at the back line) and from the third-person
        /// camera behind the cook to every corner of the bubble (2.1 to 2.4 m, 0.6 m wide) pass under the rail's board and cards
        /// (their axis-aligned bounds, so the tilted board counts larger than it is). And the rail faces down towards the cook.
        /// </summary>
        [Test]
        public void Spec044_TheOrderBubbles_AreSeenUnderTheRail_AndTheRailTiltsTowardsTheCook()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Component rail = VesselTestKit.AllOf(scene, RailType)[0];
                Bounds railBounds = default;
                bool any = false;
                foreach (MeshRenderer r in rail.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.GetComponent<TextMesh>() != null) continue;
                    if (!any) railBounds = r.bounds;
                    else railBounds.Encapsulate(r.bounds);
                    any = true;
                }
                Assert.IsTrue(any, "the rail has a board and cards");
                GameObject head = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Transform t = root.transform.Find("QueueHead");
                    if (t != null) head = t.gameObject;
                }
                Assert.IsNotNull(head, "the queue head");
                Vector3 face = -rail.transform.forward; // the rail's text side (TextMesh reads from -z)
                Vector3 toKitchen = new Vector3(face.x, 0f, face.z).normalized;
                Assert.Less(face.y, -0.25f, "the cards face down towards the cook (tilted about 20 degrees)");

                var feel = Worktown.Game.Player.FeelSettings.PresetA();
                float camUp = feel.ThirdPersonHeight + feel.ThirdPersonDistance * Mathf.Sin(feel.ThirdPersonPitch * Mathf.Deg2Rad);
                float camBack = feel.ThirdPersonDistance * Mathf.Cos(feel.ThirdPersonPitch * Mathf.Deg2Rad);
                Vector3 railFoot = new Vector3(head.transform.position.x, 0f, rail.transform.position.z);
                var eyes = new List<Vector3>();
                foreach (float cookAt in new[] { 0.8f, 1.6f, 2.6f }) // at the counter, in the aisle, at the back line (m from the rail)
                {
                    Vector3 feet = railFoot + toKitchen * cookAt;
                    eyes.Add(feet + Vector3.up * Worktown.Game.Player.ViewRules.EyeHeight);
                    eyes.Add(feet + toKitchen * camBack + Vector3.up * camUp);
                }
                foreach (Vector3 eye in eyes)
                    foreach (float y in new[] { 2.1f, 2.4f })
                        foreach (float x in new[] { -0.3f, 0f, 0.3f })
                        {
                            Vector3 bubble = head.transform.position + new Vector3(x, y, 0f);
                            Vector3 d = bubble - eye;
                            bool hit = railBounds.IntersectRay(new Ray(eye, d), out float distance) && distance < d.magnitude;
                            Assert.IsFalse(hit, "the rail hides the bubble point " + bubble + " from " + eye);
                        }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
