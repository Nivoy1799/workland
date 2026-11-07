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
    }
}
