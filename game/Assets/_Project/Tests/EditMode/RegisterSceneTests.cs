using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 032: the generated scene has the register on the front counter (a station of KitchenService with its nine slots, the
    /// synced Register and its view), the paying guests' spot in front of it, the wiring of the customer service (register, pay
    /// spot, feed), the rail's modifier lines with their strikes, and the guests' patience ring and hand-off look.
    /// </summary>
    public class RegisterSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string CustomerPrefabPath = "Assets/_Project/Prefabs/Customer.prefab";

        [Test]
        public void BuiltScene_HasTheRegister_OnTheFrontCounter_WiredToTheGuests()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                List<Component> registers = VesselTestKit.AllOf(scene, typeof(Register));
                Assert.AreEqual(1, registers.Count);
                var station = registers[0].GetComponent<Station>();
                Assert.IsNotNull(station);
                Assert.AreEqual(StationKind.Register, station.Kind);
                Assert.AreEqual(RegisterRules.SlotCount, station.SlotCount);
                for (int option = 0; option < RegisterRules.SlotCount; option++)
                {
                    Assert.IsTrue(station.TryGetSlot(option, out StockSlot slot), "slot " + option);
                    Assert.AreEqual(option, slot.Option);
                    Assert.AreSame(station, slot.Station);
                }
                Assert.IsNotNull(registers[0].GetComponent<RegisterView>());
                var body = station.transform.Find("Body").GetComponent<BoxCollider>();
                Assert.IsNotNull(body, "aimable");
                Assert.AreEqual(Worktown.Game.Art.ArtSizeTable.ServingCounterHeight, body.bounds.min.y, 0.01f, "on the counter top");

                // Spec 062a: the kitchen finds the register in the LayoutRegistry by its id (its piece id).
                Assert.AreSame(station, Worktown.Game.Layout.LayoutRegistry.Station(station.Id), "the register is station " + station.Id);

                List<Component> services = VesselTestKit.AllOf(scene, typeof(CustomerService));
                var so = new SerializedObject(services[0]);
                // Spec 062a: the guests' service finds the register (and its station) in the LayoutRegistry, not in a scene field.
                const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                Assert.AreSame(station, typeof(CustomerService).GetProperty("RegisterStation", statics).GetValue(null), "the register's station");
                Assert.AreSame(registers[0], typeof(CustomerService).GetProperty("RegisterNow", statics).GetValue(null), "the register's synced state");
                Assert.IsNull(so.FindProperty("_register"), "no scene reference to a spawned piece");
                Assert.IsNotNull(so.FindProperty("_feed").objectReferenceValue);
                var pay = (Transform)so.FindProperty("_payHead").objectReferenceValue;
                var head = (Transform)so.FindProperty("_queueHead").objectReferenceValue;
                Assert.IsNotNull(pay);
                Assert.AreEqual(head.position.z, pay.position.z, 0.01f, "on the guests' line, street side");
                Assert.AreEqual(station.transform.position.x, pay.position.x, 0.01f, "in front of the register");
                Assert.Greater(pay.position.x - head.position.x, 0.8f, "beside the queue head, not on it");

                List<Component> rails = VesselTestKit.AllOf(scene, typeof(TicketRailView));
                var rail = new SerializedObject(rails[0]);
                Assert.AreEqual(CustomerRules.MaxVisible * TicketRailView.ModLines, rail.FindProperty("_strikes").arraySize, "a strike per modifier line");
                Assert.AreEqual(CustomerRules.MaxVisible, rail.FindProperty("_cards").arraySize, "one text per card (dish, modifiers, minutes)");
            }
            finally
            {
                LayoutScene.Close(scene);
            }
        }

        [Test]
        public void TheGuest_HasThePatienceRing_AndTheHandOffLook()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<PatienceRingView>());
            var handoff = prefab.GetComponent<GuestHandoffView>();
            Assert.IsNotNull(handoff);
            Assert.IsNotNull(new SerializedObject(handoff).FindProperty("_catalog").objectReferenceValue, "the parcel models");
        }
    }
}
