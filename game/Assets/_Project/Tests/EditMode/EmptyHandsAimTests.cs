using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Shop;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 039 review (must-fix): the spec 039 strips (the plate shelf's front, the sink's front, ...) have a thin aim collider 5 mm above
    /// their station. With empty hands a spot does nothing, so it must not block the ray: AimRules.Pick with spotsTransparent finds
    /// the station underneath, which then gives its action (take a plate, scrub). The rays are cast in the built scene from where a
    /// cook stands, and the hits are sorted as PickupInteractor.FindExactTarget sorts them (surface, station or its slot, item, other).
    /// </summary>
    public class EmptyHandsAimTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";

        [Test]
        public void SpotHits_AreTransparent_OnlyWhenTheHandsAreEmpty()
        {
            var feet = new Vector3(0f, 0f, 0f);
            var near = new Vector3(0.5f, 0.9f, 0.8f);
            var hits = new List<AimHit> { AimHit.Spot(1.18f, near, 3, 1), new AimHit(1.2f, AimKind.Station, near) };
            Assert.AreEqual(0, AimRules.Pick(hits, feet, true, false, out _), "full hands: the spot");
            Assert.AreEqual(1, AimRules.Pick(hits, feet, false, true, out _), "empty hands: the station under it");
            Assert.AreEqual(0, AimRules.Pick(hits, feet, false, out _), "the old overload keeps spots solid");
            var onlySpot = new List<AimHit> { AimHit.Spot(1.18f, near, 3, 1) };
            Assert.AreEqual(AimRules.None, AimRules.Pick(onlySpot, feet, false, true, out int far));
            Assert.AreEqual(AimRules.None, far, "a transparent spot is not 'too far' either");
        }

        [Test]
        public void EmptyHands_AtThePlateShelfCentre_TakeAPlate()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Component picked = AimFromTheAisle(scene, "Plates front", out Component withFullHands);
                Assert.IsInstanceOf<PlaceSurface>(withFullHands, "the strip is the first hit (what a held item would be put on)");
                Station shelf = picked as Station ?? (picked as StockSlot)?.Station;
                Assert.IsNotNull(shelf, "empty hands reach a station, got " + (picked != null ? picked.name : "nothing"));
                Assert.AreEqual(StationKind.PlateShelf, shelf.Kind);
                Assert.AreEqual(InteractionAction.Take,
                    InteractionRules.ActionFor(Held.Nothing, AimTarget.CleaningStation(StationKind.PlateShelf, 0, 3, true)), "take a plate");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void EmptyHands_AtTheSinkFront_GetTheSinksAction()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Component picked = AimFromTheAisle(scene, "Sink front", out Component withFullHands);
                Assert.IsInstanceOf<PlaceSurface>(withFullHands, "the strip is the first hit");
                Station sink = picked as Station ?? (picked as StockSlot)?.Station;
                Assert.IsNotNull(sink, "empty hands reach a station, got " + (picked != null ? picked.name : "nothing"));
                Assert.AreEqual(StationKind.Sink, sink.Kind);
                int option = picked is StockSlot slot ? slot.Option : 0;
                Assert.AreEqual(InteractionAction.Scrub,
                    InteractionRules.ActionFor(Held.Nothing, AimTarget.CleaningStation(StationKind.Sink, option, CleaningRules.SinkByte(1, false), true)),
                    "a dirty plate in the sink: hold to scrub");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>
        /// Aims from 0.75 m in front of the strip's centre (its cook side, local -Z) at eye height to that centre; returns what empty hands
        /// target there, and in <paramref name="withFullHands"/> what full hands target.
        /// </summary>
        private static Component AimFromTheAisle(Scene scene, string stripName, out Component withFullHands)
        {
            PlaceSurface strip = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (PlaceSurface s in root.GetComponentsInChildren<PlaceSurface>(true))
                    if (s.name == stripName) strip = s;
            Assert.IsNotNull(strip, stripName);
            Physics.SyncTransforms();

            Vector3 target = strip.transform.TransformPoint(new Vector3(0f, strip.TopY, 0f));
            Vector3 cookSide = -strip.transform.forward;
            cookSide.y = 0f;
            cookSide.Normalize();
            Vector3 feet = target + cookSide * 0.75f;
            feet.y = 0f;
            Vector3 eye = feet + Vector3.up * 1.6f;
            var ray = new Ray(eye, (target - eye).normalized);
            RaycastHit[] raw = Physics.RaycastAll(ray, 3f, PhysicsLayers.Interact, QueryTriggerInteraction.Ignore);

            var hits = new List<AimHit>();
            var sources = new List<Component>();
            foreach (RaycastHit h in raw)
            {
                if (h.collider.TryGetComponent(out PlaceSurface surface))
                {
                    hits.Add(AimHit.Spot(h.distance, surface.SpotWorldPosition(0), surface.Index, 0));
                    sources.Add(surface);
                    continue;
                }
                Station station = h.collider.GetComponentInParent<Station>();
                if (station != null)
                {
                    StockSlot slot = station.SlotCount > 0 ? station.NearestSlot(h.point) : null;
                    Component source = slot != null ? slot : (Component)station;
                    hits.Add(new AimHit(h.distance, AimKind.Station, source.transform.position));
                    sources.Add(source);
                    continue;
                }
                bool item = h.collider.GetComponentInParent(typeof(Pickup)) != null;
                hits.Add(new AimHit(h.distance, item ? AimKind.Pickup : AimKind.Other, h.collider.transform.position));
                sources.Add(null);
            }
            Assert.Greater(hits.Count, 0, "the ray at " + stripName + " hits something");
            int full = AimRules.Pick(hits, feet, true, false, out _);
            withFullHands = full >= 0 ? sources[full] : null;
            int empty = AimRules.Pick(hits, feet, false, true, out _);
            return empty >= 0 ? sources[empty] : null;
        }
    }
}
