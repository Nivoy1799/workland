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
    /// Spec 020: the generated scene has worktops between the kitchen stations and a place surface on the board
    /// station's free half, all known to the PickupService by index; the knife starts on a spot; the world labels
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

        [Test]
        public void BuiltScene_SurfacesAreRegisteredByIndex_WithSpotsAtStationHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<PlaceSurface> surfaces = All<PlaceSurface>(scene);
                Assert.AreEqual(6, surfaces.Count, "the board's free half and five worktops");
                SerializedProperty list = new SerializedObject(All<PickupService>(scene)[0]).FindProperty("_surfaces");
                Assert.AreEqual(surfaces.Count, list.arraySize);
                for (int i = 0; i < list.arraySize; i++)
                {
                    var surface = (PlaceSurface)list.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.IsNotNull(surface, "surface " + i);
                    Assert.AreEqual(i, surface.Index, surface.name);
                    Assert.IsNotNull(surface.GetComponent<BoxCollider>(), surface.name + " needs a collider for the aim ray");
                    Assert.AreEqual(Vector3.one, surface.transform.lossyScale, surface.name + " must not be scaled");
                    Assert.Greater(surface.SpotCount, 1, surface.name);
                    float y = surface.SpotWorldPosition(0).y;
                    Assert.That(y, Is.InRange(0.7f, 1.3f), surface.name + " top at station height");
                    Bounds box = surface.GetComponent<BoxCollider>().bounds;
                    Assert.GreaterOrEqual(box.max.y, y - 0.001f, surface.name + ": the ray hits the top, not air");
                }
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
                    if (surface.GetComponentInParent<Station>() != null) continue; // the board's own half
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
                Assert.AreEqual(5, worktops);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_KnifeStartsOnASpotOfItsBoard()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Knife knife = All<Knife>(scene)[0];
                Station board = All<Station>(scene).Find(s => s.Index == knife.BoardIndex);
                PlaceSurface surface = board.GetComponentInChildren<PlaceSurface>();
                Assert.IsNotNull(surface, "the board station has a place surface");
                bool onSpot = false;
                for (int i = 0; i < surface.SpotCount; i++)
                {
                    Vector3 spot = surface.SpotWorldPosition(i);
                    Vector3 offset = knife.transform.position - spot;
                    if (new Vector2(offset.x, offset.z).magnitude < 0.001f && offset.y > 0f && offset.y < 0.1f) onSpot = true;
                }
                Assert.IsTrue(onSpot, "the knife lies on its home spot (" + knife.transform.position + ")");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
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
            foreach (InteractionAction use in new[] { InteractionAction.PutInto, InteractionAction.PutOnBoard, InteractionAction.Place,
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
