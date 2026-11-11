using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 039 part 2: items are never stairs. Every loose item lies on the Items layer, the player and the customers on their
    /// own layers, and the project's collision matrix lets neither touch Items (the same setting on server and client). A player
    /// controller walks through a tomato on the floor instead of climbing it; items still meet the static world.
    /// </summary>
    public class ItemsLayerTests
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";

        [Test]
        public void Layers_HaveTheirNames()
        {
            Assert.AreEqual(PhysicsLayers.Items, LayerMask.NameToLayer(PhysicsLayers.ItemsName));
            Assert.AreEqual(PhysicsLayers.Player, LayerMask.NameToLayer(PhysicsLayers.PlayerName));
            Assert.AreEqual(PhysicsLayers.Npc, LayerMask.NameToLayer(PhysicsLayers.NpcName));
        }

        [Test]
        public void CollisionMatrix_PlayerAndNpcIgnoreItems_ItemsStillMeetTheWorld()
        {
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(PhysicsLayers.Items, PhysicsLayers.Player), "the player walks through items");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(PhysicsLayers.Items, PhysicsLayers.Npc), "customers walk through items");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.Items, 0), "items rest on the static world (Default)");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.Items, PhysicsLayers.Items));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.Player, 0), "the player still walks on the floor and into walls");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.Player, PhysicsLayers.Player), "players still bump into each other");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.Player, PhysicsLayers.Npc));
            Assert.AreEqual(0, PhysicsLayers.BodyMask(PhysicsLayers.Player) & PhysicsLayers.ItemsMask, "the ground probe ignores items too");
            Assert.AreNotEqual(0, PhysicsLayers.BodyMask(PhysicsLayers.Player) & 1, "and sees the floor");
        }

        [Test]
        public void EveryPickupPrefab_IsOnTheItemsLayer_PlayerAndCustomerOnTheirs()
        {
            int pickups = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent(typeof(Pickup)) == null) continue;
                pickups++;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(PhysicsLayers.Items, t.gameObject.layer, path + ": " + t.name);
            }
            Assert.GreaterOrEqual(pickups, 13, "food, the cube, knife, spatula, cloth, five cookware, dirty dish, trash bag, delivery box");
            AssertLayer("Player", PhysicsLayers.Player);
            AssertLayer("Customer", PhysicsLayers.Npc);
        }

        private static void AssertLayer(string name, int layer)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + name + ".prefab");
            Assert.IsNotNull(prefab, name);
            foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
                Assert.AreEqual(layer, c.gameObject.layer, name + ": " + c.name);
            Assert.AreEqual(layer, prefab.layer, name);
        }

        [Test]
        public void BuiltScene_EveryItemIsOnTheItemsLayer()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                int items = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Component pickup in root.GetComponentsInChildren(typeof(Pickup), true))
                    {
                        items++;
                        foreach (Collider c in pickup.GetComponentsInChildren<Collider>(true))
                            Assert.AreEqual(PhysicsLayers.Items, c.gameObject.layer, pickup.name + ": " + c.name);
                    }
                Assert.GreaterOrEqual(items, 9, "the knife, spatula, cloth and the cookware at their homes");
            }
            finally { LayoutScene.Close(scene); }
        }

        /// <summary>
        /// The human's "a tomato behaving like stairs": a controller like the player's (radius, height, step offset and skin from
        /// the prefab) walks 3 m across a tomato and a plate lying on the floor (their colliders and layers from the prefabs). Its
        /// height never rises above where it stands on the floor, and it gets past them. The same walk with the items on the
        /// Default layer is blocked or climbs (so the test can fail).
        /// </summary>
        [Test]
        public void PlayerController_WalksOverATomatoOnTheFloor_WithoutClimbingIt()
        {
            Walk(true, out float ground, out float highest, out float endX, out float skin);
            Assert.LessOrEqual(highest, ground + 0.005f, "the controller climbed an item (rose " + (highest - ground).ToString("0.000") + " m)");
            Assert.LessOrEqual(highest, skin + 0.01f, "never above the floor plus the skin");
            Assert.Greater(endX, 1.5f, "it walked through the tomato and the plate, not stopped by them");
        }

        [Test]
        public void PlayerController_WithItemsOnTheDefaultLayer_IsBlockedOrClimbs_SoTheWalkTestCanFail()
        {
            Walk(false, out float ground, out float highest, out float endX, out _);
            Assert.IsTrue(highest > ground + 0.02f || endX < 1.5f, "without the Items layer the items stop or lift the controller (rose "
                + (highest - ground).ToString("0.000") + " m, ended at x " + endX.ToString("0.00") + ")");
        }

        /// <summary>
        /// A controller like the player's walks 3 m along +x across a tomato (at x 0) and a plate (x 0.9) on a floor. With
        /// <paramref name="itemsLayer"/> false the stand-ins lie on Default instead of their prefab's layer.
        /// </summary>
        private static void Walk(bool itemsLayer, out float ground, out float highest, out float endX, out float skin)
        {
            var created = new List<Object>();
            try
            {
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
                var source = player.GetComponent<CharacterController>();
                Assert.IsNotNull(source);
                skin = source.skinWidth;

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(floor);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(10f, 1f, 10f);

                GameObject tomato = Item("Food", new Vector3(0f, 0f, 0f), created);
                GameObject plate = Item("Plate", new Vector3(0.9f, 0f, 0f), created);
                Assert.AreEqual(PhysicsLayers.Items, tomato.layer, "the food prefab's layer");
                Assert.AreEqual(PhysicsLayers.Items, plate.layer, "the plate prefab's layer");
                if (!itemsLayer) tomato.layer = plate.layer = 0;

                var body = new GameObject("Walker") { layer = PhysicsLayers.Player };
                created.Add(body);
                var controller = body.AddComponent<CharacterController>();
                controller.height = source.height;
                controller.radius = source.radius;
                controller.center = source.center;
                controller.stepOffset = source.stepOffset;
                controller.skinWidth = source.skinWidth;
                controller.slopeLimit = source.slopeLimit;
                body.transform.position = new Vector3(-1.5f, 0.2f, 0f);
                Physics.SyncTransforms();

                for (int i = 0; i < 20; i++) controller.Move(Vector3.down * 0.05f); // settle on the floor
                ground = body.transform.position.y;
                Assert.That(ground, Is.InRange(-0.01f, source.skinWidth + 0.01f), "stands on the floor");

                highest = ground;
                const float step = 0.04f; // 4 m/s at 100 ticks per second, about a walk
                for (int i = 0; i < 90; i++)
                {
                    controller.Move(new Vector3(step, -0.02f, 0f));
                    highest = Mathf.Max(highest, body.transform.position.y);
                }
                endX = body.transform.position.x;
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
        }

        /// <summary>
        /// A stand-in for an item prefab lying on the floor: its collider (size, centre) and its layer, the bottom on y 0. A plain
        /// object, so no networked prefab is instantiated in the editor.
        /// </summary>
        private static GameObject Item(string prefabName, Vector3 at, List<Object> created)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + prefabName + ".prefab");
            Assert.IsNotNull(prefab, prefabName);
            var source = prefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(source, prefabName);
            var item = new GameObject(prefabName) { layer = prefab.layer };
            created.Add(item);
            var box = item.AddComponent<BoxCollider>();
            box.size = source.size;
            box.center = source.center;
            float bottom = box.size.y * 0.5f - box.center.y;
            item.transform.position = at + Vector3.up * bottom;
            return item;
        }
    }
}
