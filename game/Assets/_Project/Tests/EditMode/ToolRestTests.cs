using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 039 part 3: tools lie the way a cook leaves them. The pure rule (ToolRest): at home the handle points into the aisle
    /// (the surface's local -Z); put down, it points to where the player stood, snapped to 45 degrees from the surface's facing.
    /// The scene: every home tool's handle points into the aisle within 15 degrees; the tool prefabs are marked as handle tools.
    /// </summary>
    public class ToolRestTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private static readonly Quaternion BackLine = Quaternion.identity;            // a worktop: the aisle at world -Z
        private static readonly Quaternion FrontCounter = Quaternion.Euler(0f, 180f, 0f); // turned round: the aisle at world +Z

        private static void AssertDirection(Vector3 expected, Quaternion rotation, string what)
        {
            Vector3 handle = ToolRest.HandleDirection(rotation);
            Assert.Less(Vector3.Angle(expected, handle), 0.5f, what + ": handle " + handle.ToString("F3") + ", expected " + expected.ToString("F3"));
        }

        [Test]
        public void Home_HandlePointsIntoTheAisle()
        {
            AssertDirection(Vector3.back, ToolRest.Home(BackLine), "back line");
            AssertDirection(Vector3.forward, ToolRest.Home(FrontCounter), "front counter (the cook stands north of it)");
            AssertDirection(Vector3.back, ToolRest.Home(Quaternion.Euler(30f, 0f, 10f)), "only the surface's yaw counts");
        }

        [Test]
        public void Placed_HandlePointsToThePlacer_SnappedToFortyFiveDegrees()
        {
            var spot = new Vector3(5f, 0.9f, 14f);
            AssertDirection(Vector3.back, ToolRest.Rotation(BackLine, spot + new Vector3(0f, 0.1f, -1.3f), spot), "straight in front");
            AssertDirection(new Vector3(1f, 0f, -1f).normalized, ToolRest.Rotation(BackLine, spot + new Vector3(1f, 0f, -1f), spot), "front right");
            AssertDirection(Vector3.back, ToolRest.Rotation(BackLine, spot + new Vector3(Mathf.Tan(20f * Mathf.Deg2Rad), 0f, -1f), spot),
                "20 degrees off snaps back to straight");
            AssertDirection(new Vector3(1f, 0f, -1f).normalized,
                ToolRest.Rotation(BackLine, spot + new Vector3(Mathf.Tan(30f * Mathf.Deg2Rad), 0f, -1f), spot), "30 degrees off snaps to 45");
            AssertDirection(Vector3.left, ToolRest.Rotation(BackLine, spot + new Vector3(-2f, 0f, 0.05f), spot), "from the side");
            // The front counter: the cook stands on the aisle side (+Z), so the handle faces the cook, not the guests.
            AssertDirection(Vector3.forward, ToolRest.Rotation(FrontCounter, spot + new Vector3(0.2f, 0f, 1.1f), spot), "front counter, cook side");
        }

        [Test]
        public void Placed_SnapsFromTheSurfacesFacing_AndFallsBackToHomeOverTheSpot()
        {
            var spot = new Vector3(1f, 1f, 1f);
            Quaternion turned = Quaternion.Euler(0f, 30f, 0f);
            // Straight along -Z is 150 degrees from the facing: it snaps to 135, i.e. world yaw 165.
            float yaw = ToolRest.Rotation(turned, spot + Vector3.back, spot).eulerAngles.y;
            Assert.AreEqual(165f, yaw, 0.01f);
            Assert.AreEqual(ToolRest.Home(BackLine), ToolRest.Rotation(BackLine, spot + Vector3.up, spot), "placer right over the spot");
            Assert.AreEqual(ToolRest.Home(BackLine), ToolRest.Rotation(BackLine, new Vector3(float.NaN, 0f, 0f), spot));
            for (int i = 0; i < 16; i++)
            {
                Vector3 at = spot + Quaternion.Euler(0f, i * 22.5f + 3f, 0f) * Vector3.forward;
                float y = ToolRest.Rotation(BackLine, at, spot).eulerAngles.y;
                Assert.AreEqual(0f, Mathf.Repeat(y + 0.001f, ToolRest.SnapDegrees), 0.01f, "always a multiple of 45 degrees, " + i);
            }
        }

        [Test]
        public void RestRotation_ToolsTurnToThePlacer_OthersKeepTheSurfaceYaw_TiltStays()
        {
            var spot = new Vector3(0f, 0.9f, 14f);
            var placer = new Vector3(0f, 0f, 12.7f);
            Quaternion tilt = Quaternion.Euler(0f, 0f, 90f);
            Assert.AreEqual(PlaceSpots.RestRotation(tilt, FrontCounter), ToolRest.RestRotation(false, tilt, FrontCounter, placer, spot),
                "cookware and food keep the spec 020/021 rule");
            Quaternion rest = ToolRest.RestRotation(true, tilt, BackLine, placer, spot);
            Assert.AreEqual(ToolRest.Rotation(BackLine, placer, spot) * tilt, rest);
            Assert.Less(Quaternion.Angle(tilt, PlaceSpots.TiltOf(rest)), 0.5f, "the knife stays on its edge");
        }

        [Test]
        public void ToolPrefabs_AreHandleTools_FoodAndCookwareAreNot()
        {
            foreach (string name in new[] { "Knife", "Spatula", "WipeCloth" })
                Assert.IsTrue(IsHandleTool(Load(name)), name);
            foreach (string name in new[] { "Food", "Pan", "Pot", "Plate", "Bowl", "Board", "DirtyDish", "TrashBag", "DeliveryBox", "PickupCube" })
                Assert.IsFalse(IsHandleTool(Load(name)), name);
        }

        /// <summary>The prefab's Pickup (by Type: this assembly does not reference FishNet, so no casts to NetworkBehaviours).</summary>
        private static Component Load(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + name + ".prefab");
            Assert.IsNotNull(prefab, name);
            Component pickup = prefab.GetComponent(typeof(Pickup));
            Assert.IsNotNull(pickup, name + " has a Pickup");
            return pickup;
        }

        private static bool IsHandleTool(Component pickup) => new SerializedObject(pickup).FindProperty("_handleTool").boolValue;

        /// <summary>Spec 039 AC (part 3): the knife, the spatula and the cloth lie at home with the handle into the aisle (within 15 degrees).</summary>
        [Test]
        public void BuiltScene_HomeTools_HandleIntoTheAisle()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                var tools = new List<Component>();
                var surfaces = new List<PlaceSurface>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    surfaces.AddRange(root.GetComponentsInChildren<PlaceSurface>(true));
                    tools.AddRange(root.GetComponentsInChildren<Knife>(true));
                    tools.AddRange(root.GetComponentsInChildren<Spatula>(true));
                    tools.AddRange(root.GetComponentsInChildren<WipeCloth>(true));
                }
                Assert.AreEqual(3, tools.Count, "the knife, the spatula and the cloth");
                foreach (Component tool in tools)
                {
                    // 039 review: into the aisle is the cook side of the surface the tool lies on (its local -Z), else world -Z.
                    PlaceSurface under = SurfaceUnder(surfaces, tool.transform.position);
                    Vector3 aisle = under != null ? -under.transform.forward : Vector3.back;
                    aisle.y = 0f;
                    aisle.Normalize();
                    Assert.IsNotNull(under, tool.name + " lies on a place surface at home");
                    Assert.IsTrue(IsHandleTool(tool.GetComponent(typeof(Pickup))), tool.name);
                    float angle = Vector3.Angle(aisle, ToolRest.HandleDirection(tool.transform.rotation));
                    Assert.LessOrEqual(angle, 15f, tool.name + "'s handle is " + angle.ToString("0") + " degrees off the aisle");
                    Assert.Less(Quaternion.Angle(Quaternion.identity, PlaceSpots.TiltOf(tool.transform.rotation)), 0.5f,
                        tool.name + " lies flat in its resting tilt (the knife on its edge)");
                }
            }
            finally { LayoutScene.Close(scene); }
        }

        /// <summary>The surface with a spot right under <paramref name="position"/> (within 0.1 m across, up to 0.3 m above), or null.</summary>
        private static PlaceSurface SurfaceUnder(List<PlaceSurface> surfaces, Vector3 position)
        {
            foreach (PlaceSurface s in surfaces)
                for (int i = 0; i < s.SpotCount; i++)
                {
                    Vector3 d = position - s.SpotWorldPosition(i);
                    if (new Vector2(d.x, d.z).magnitude < 0.1f && d.y > -0.05f && d.y < 0.3f) return s;
                }
            return null;
        }
    }
}
