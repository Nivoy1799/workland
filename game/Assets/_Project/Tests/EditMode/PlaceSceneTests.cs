using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 020/021: the generated scene has worktops between the kitchen stations, stove tops and burners as place surfaces, all
    /// known to the PickupService by index; the knife and the cookware start on spots; the world labels
    /// show no keys; the player prefab has the highlight and the label fade. Also the highlight and fade helpers.
    /// </summary>
    public class PlaceSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        /// <summary>
        /// Spec 042: each tap of the drink dispenser has one cup spot, flagged with its dispenser and tap, its spot right under the spout
        /// where the stream lands (the tap view's CupPose), reachable from the aisle in front of the dispenser.
        /// </summary>
        [Test]
        public void BuiltScene_EachTap_HasOneCupSpot_RightUnderItsSpout()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var taps = new Dictionary<int, Worktown.Game.Art.DrinkTapView>();
                foreach (Worktown.Game.Art.DrinkTapView view in All<Worktown.Game.Art.DrinkTapView>(scene)) taps[view.Tap] = view;
                Assert.AreEqual(DrinkRules.TapCount, taps.Count);
                var seen = new HashSet<int>();
                foreach (PlaceSurface surface in All<PlaceSurface>(scene))
                {
                    if (!surface.IsCupSpot) continue;
                    Assert.IsTrue(seen.Add(surface.CupTap), surface.name + ": one spot per tap");
                    Station dispenser = surface.GetComponentInParent<Station>();
                    Assert.IsNotNull(dispenser, surface.name);
                    Assert.AreEqual(StationKind.DrinkDispenser, dispenser.Kind);
                    Assert.AreEqual(dispenser.Index, surface.DispenserIndex, surface.name);
                    Assert.AreEqual(1, surface.SpotCount, surface.name + ": one cup");
                    Assert.IsFalse(surface.IsBurner);
                    Vector3 spot = surface.SpotWorldPosition(0);
                    Assert.AreEqual(0f, Vector3.Distance(taps[surface.CupTap].CupPose.position, spot), 0.001f, surface.name + " under its spout");
                    Vector3 aisle = dispenser.transform.position + dispenser.transform.rotation * new Vector3(0f, 0f, -0.9f);
                    aisle.y = 0f;
                    Assert.IsTrue(PickupRules.IsInReach(aisle, spot), surface.name + " in reach from the aisle");
                    Assert.AreEqual(-1, PlaceCoverage.ExceptionFor(surface.name), "a place surface, not an exception");
                }
                Assert.AreEqual(DrinkRules.TapCount, seen.Count);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_SurfacesAreRegisteredByIndex_WithSpotsAtStationHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<PlaceSurface> surfaces = All<PlaceSurface>(scene);
                Assert.AreEqual(27, surfaces.Count, "spec 042: the three taps' cup spots; spec 021: five worktops, and per stove its top and two burners; spec 030: the griddle's deck; "
                    + "spec 039: four front counter tops, the stoves' front strips, the fryer deck, the assembly back, the sink front and back, "
                    + "the plate shelf front and back");
                SerializedProperty list = new SerializedObject(All<PickupService>(scene)[0]).FindProperty("_surfaces");
                Assert.AreEqual(surfaces.Count, list.arraySize);
                int burners = 0;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var surface = (PlaceSurface)list.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.IsNotNull(surface, "surface " + i);
                    Assert.AreEqual(i, surface.Index, surface.name);
                    Assert.IsNotNull(surface.GetComponent<BoxCollider>(), surface.name + " needs a collider for the aim ray");
                    Assert.AreEqual(Vector3.one, surface.transform.lossyScale, surface.name + " must not be scaled");
                    if (surface.IsBurner)
                    {
                        burners++;
                        Assert.AreEqual(1, surface.SpotCount, surface.name + ": a burner has one spot");
                        Station stove = surface.GetComponentInParent<Station>();
                        Assert.IsNotNull(stove, surface.name);
                        Assert.AreEqual(StationKind.Stove, stove.Kind);
                        Assert.AreEqual(stove.Index, surface.StoveIndex, surface.name);
                    }
                    else if (i < 12) Assert.Greater(surface.SpotCount, 1, surface.name);
                    else Assert.GreaterOrEqual(surface.SpotCount, 1, surface.name + ": spec 039 rule 3, a narrow top still has a spot");
                    float y = surface.SpotWorldPosition(0).y;
                    Assert.That(y, Is.InRange(0.7f, 1.3f), surface.name + " top at station height");
                    Bounds box = surface.GetComponent<BoxCollider>().bounds;
                    Assert.GreaterOrEqual(box.max.y, y - 0.001f, surface.name + ": the ray hits the top, not air");
                }
                Assert.AreEqual(4, burners, "two stoves with two burners each");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_WorktopsFillTheGaps_WithoutOverlappingStations()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<Station> stations = All<Station>(scene);
                int worktops = 0;
                foreach (PlaceSurface surface in All<PlaceSurface>(scene))
                {
                    if (surface.GetComponentInParent<Station>() != null) continue; // a stove's top or burner
                    if (surface.transform.parent != null && surface.transform.parent.name.StartsWith("Front counter")) continue; // spec 039
                    worktops++;
                    Bounds top = surface.GetComponent<BoxCollider>().bounds;
                    foreach (Station station in stations)
                    {
                        Bounds body = station.transform.Find("Body").GetComponent<BoxCollider>().bounds;
                        bool overlapX = top.min.x < body.max.x - 0.001f && top.max.x > body.min.x + 0.001f;
                        bool overlapZ = top.min.z < body.max.z && top.max.z > body.min.z;
                        Assert.IsFalse(overlapX && overlapZ, surface.name + " overlaps " + station.name);
                    }
                }
                Assert.AreEqual(4, worktops); // spec 035: the bowl's worktop after the plate shelf is the sink (its drainboard)
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>Spec 021: the knife and every piece of cookware start on a spot (their home); the pan and the pot on burners.</summary>
        [Test]
        public void BuiltScene_KnifeAndCookwareStartOnSpots_PanAndPotOnBurners()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<PlaceSurface> surfaces = All<PlaceSurface>(scene);
                var items = new List<Component>();
                items.AddRange(All<Knife>(scene));
                List<Component> vessels = VesselTestKit.AllOf(scene, VesselTestKit.VesselType);
                // Spec 029: the fryer baskets hang in their fryer (no spot, never carried).
                Assert.AreEqual(2, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Basket), "two fryer baskets");
                // Spec 030: the six griddle spots lie on the griddle's plate (fixed, no spot); the spatula on the griddle's deck.
                Assert.AreEqual(Worktown.Game.Cooking.GriddleRules.SpotsPerGriddle, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Griddle), "six griddle spots");
                Assert.AreEqual(1, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.Assembly), "spec 031: one build spot on the assembly board");
                Assert.AreEqual(1, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.SpitCone), "spec 036: the spit's cone (fixed)");
                Assert.AreEqual(1, vessels.RemoveAll(v => VesselTestKit.KindOf(v) == VesselKind.SpitTray), "spec 036: the spit's catch tray (fixed)");
                items.AddRange(All<Spatula>(scene));
                items.AddRange(vessels);
                Assert.AreEqual(1 + 1 + 6, items.Count, "knife, spatula, board, pan, pot, bowl and two plates");
                foreach (Component item in items)
                {
                    PlaceSurface on = SurfaceUnder(surfaces, item.transform.position);
                    Assert.IsNotNull(on, item.name + " lies on no spot (" + item.transform.position + ")");
                    bool heat = !(item is Knife) && !(item is Spatula) && VesselRules.IsHeat(VesselTestKit.KindOf(item));
                    Assert.AreEqual(heat, on.IsBurner, item.name + " on " + on.name);
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static PlaceSurface SurfaceUnder(List<PlaceSurface> surfaces, Vector3 position)
        {
            foreach (PlaceSurface surface in surfaces)
                for (int i = 0; i < surface.SpotCount; i++)
                {
                    Vector3 offset = position - surface.SpotWorldPosition(i);
                    if (new Vector2(offset.x, offset.z).magnitude < 0.001f && offset.y >= -0.001f && offset.y < 0.1f) return surface;
                }
            return null;
        }

        [Test]
        public void BuiltScene_StationLabelsShowNoKeys()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (Station station in All<Station>(scene))
                {
                    string text = station.transform.Find("Label").GetComponent<TextMesh>().text;
                    StringAssert.DoesNotContain("E ·", text, station.name);
                    StringAssert.StartsWith(station.DisplayName.Replace(" crate", string.Empty), text, station.name);
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void PlayerPrefab_HasTheHighlightAndTheLabelFade()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab);
            var highlight = prefab.GetComponent<InteractionHighlight>();
            Assert.IsNotNull(highlight);
            Assert.IsNotNull(new SerializedObject(highlight).FindProperty("_markerMaterial").objectReferenceValue, "no marker material");
            Assert.IsNotNull(prefab.GetComponent<WorldLabelFade>());
        }

        [Test]
        public void HighlightColours_FollowTheAction()
        {
            Assert.AreEqual(InteractionHighlight.TakeColor, InteractionHighlight.ColorFor(InteractionAction.PickUp, false));
            Assert.AreEqual(InteractionHighlight.TakeColor, InteractionHighlight.ColorFor(InteractionAction.Take, false));
            Assert.AreEqual(InteractionHighlight.TakeColor, InteractionHighlight.ColorFor(InteractionAction.Buy, false));
            foreach (InteractionAction use in new[] { InteractionAction.PutInto, InteractionAction.Pour, InteractionAction.Place,
                         InteractionAction.Sell, InteractionAction.Press, InteractionAction.Cut })
                Assert.AreEqual(InteractionHighlight.UseColor, InteractionHighlight.ColorFor(use, false), use.ToString());
            Assert.AreEqual(InteractionHighlight.RefuseColor, InteractionHighlight.ColorFor(InteractionAction.None, true));
            Assert.IsNull(InteractionHighlight.ColorFor(InteractionAction.None, false), "no prompt, no glow");
        }

        [Test]
        public void PerMaterialPropertyBlock_ClearsWithNull_AndKeepsTheRendererBlock()
        {
            // The highlight relies on this: its per-material blocks come off again and leave other scripts' renderer-wide tints.
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var r = cube.GetComponent<MeshRenderer>();
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Color.red);
                r.SetPropertyBlock(block);
                var glow = new MaterialPropertyBlock();
                glow.SetColor("_BaseColor", Color.green);
                r.SetPropertyBlock(glow, 0);
                Assert.IsTrue(r.HasPropertyBlock());
                r.SetPropertyBlock(null, 0);
                var read = new MaterialPropertyBlock();
                r.GetPropertyBlock(read, 0);
                Assert.IsTrue(read.isEmpty, "the per-material block is gone");
                r.GetPropertyBlock(read);
                Assert.AreEqual(Color.red, read.GetColor("_BaseColor"), "the renderer-wide block stays");
            }
            finally { Object.DestroyImmediate(cube); }
        }

        [Test]
        public void LabelAlpha_FadesWithinOneAndAHalfMetres()
        {
            Assert.AreEqual(1f, WorldLabelFade.Alpha(3f));
            Assert.AreEqual(1f, WorldLabelFade.Alpha(WorldLabelFade.FadeStart));
            Assert.AreEqual(0.5f, WorldLabelFade.Alpha(1.25f), 1e-5f);
            Assert.AreEqual(0f, WorldLabelFade.Alpha(WorldLabelFade.FadeEnd));
            Assert.AreEqual(0f, WorldLabelFade.Alpha(0.2f));
            Assert.AreEqual(1f, WorldLabelFade.Alpha(float.NaN));
            Assert.AreEqual(0.5f, WorldLabelFade.FirstPersonScale);
        }
    }
}
