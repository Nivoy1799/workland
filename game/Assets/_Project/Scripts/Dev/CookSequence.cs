using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 021 AC8 <c>-demo-cookware</c>, moved here by spec 027 and now walking through the player's intents
    /// (<see cref="TourActor"/>) instead of simulated keys, so it runs without window focus. Each step aims and does
    /// what a click would do, so the normal RequestPickup / RequestPlace / RequestStation / RequestUse go out (no server
    /// shortcut). Steps: pick up the pan from the first stove, put it on the second stove's free burner, take a potato
    /// and put it on that stove's top next to the pan, take a potato and an onion and put both into the pot, switch the
    /// pot's burner on, then look at the stove. If an aim does not give the expected action in 3 s it logs a warning and
    /// sends the same request directly. Needs first person (<c>-view first</c>) for the clicks.
    /// The cook tour passes <paramref name="shot"/> to take a picture after each step.
    /// </summary>
    public static class CookSequence
    {
        public static IEnumerator Run(TourActor actor, Func<string, IEnumerator> shot, float lookSeconds)
        {
            PickupInteractor me = actor.Interactor;
            Station potatoCrate = null, onionCrate = null;
            var stoves = new List<Station>();
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Crate && s.IngredientIndex == 0) potatoCrate = s;
                else if (s.Kind == StationKind.Crate && s.IngredientIndex == 3) onionCrate = s;
                else if (s.Kind == StationKind.Stove) stoves.Add(s);
            }
            stoves.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            Station stoveA = stoves.Count > 0 ? stoves[0] : null;
            Station stoveB = stoves.Count > 1 ? stoves[1] : null;
            Vessel pan = null, pot = null;
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind == VesselKind.Pan) pan = v;
                else if (v.Kind == VesselKind.Pot) pot = v;
            }
            if (potatoCrate == null || onionCrate == null || stoveA == null || stoveB == null || pan == null || pot == null)
            {
                Debug.LogWarning("[Demo] crates, two stoves, the pan or the pot not found");
                actor.CountTimeout("cook: scene objects missing");
                yield break;
            }
            PlaceSurface freeBurner = null, stoveTop = null;
            foreach (PlaceSurface s in UnityEngine.Object.FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
            {
                if (s.GetComponentInParent<Station>() != stoveB) continue;
                if (!s.IsBurner) stoveTop = s;
                else if (Vector3.Distance(s.SpotWorldPosition(0), pot.transform.position) > 0.2f) freeBurner = s;
            }
            PlaceSurface potBurner = null;
            foreach (PlaceSurface s in UnityEngine.Object.FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
                if (s.IsBurner && Vector3.Distance(s.SpotWorldPosition(0), pot.transform.position) <= 0.2f) potBurner = s;
            StationButton potSwitch = null;
            foreach (StationButton b in UnityEngine.Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
                if (potBurner != null && b.Station != null && b.Station.Index == potBurner.StoveIndex
                    && b.Option == StationRules.StoveOption(potBurner.Burner, StationRules.StoveSwitch)) potSwitch = b;
            if (freeBurner == null || stoveTop == null || potSwitch == null)
            {
                Debug.LogWarning("[Demo] the second stove has no free burner, no stove top or no switch for the pot");
                actor.CountTimeout("cook: stove setup missing");
                yield break;
            }

            float laneZ = stoveA.transform.position.z - 1.7f;
            float standZ = stoveA.transform.position.z - 1.3f;
            Vector3 atA = new Vector3(stoveA.transform.position.x, 0f, standZ);
            Vector3 atB = new Vector3(stoveB.transform.position.x, 0f, standZ);
            Debug.Log("[Demo] walking to the first stove (" + Time.time.ToString("0.0") + " s)");
            yield return actor.WalkTo(new Vector3(-1.75f, 0f, actor.Position.z), false, false);
            yield return actor.WalkTo(new Vector3(-1.75f, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(atA.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(atA, false, false);
            if (shot != null) yield return shot("at-stove");
            NetworkObject panObject = pan.NetworkObject;
            yield return actor.Use("pick up the pan", ContentPoint(pan), InteractionAction.PickUp, () => me.DevSendPickup(panObject));
            if (shot != null) yield return shot("pan-in-hand");

            yield return actor.LevelView();
            yield return actor.WalkTo(atB, false, false);
            Vector3 burnerSpot = freeBurner.SpotWorldPosition(0);
            yield return actor.Use("put the pan on the free burner", burnerSpot, InteractionAction.Place, () => me.DevSendPlace(freeBurner.Index, 0));
            Debug.Log("[Demo] the pan is on the burner (" + Time.time.ToString("0.0") + " s)");
            if (shot != null) yield return shot("pan-on-burner");

            yield return Buy(actor, "take a potato", potatoCrate, laneZ);
            if (shot != null) yield return shot("potato-in-hand");
            yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(atB, false, false);
            int nextToPan = PlaceSpots.NearestFree(stoveTop.Size, stoveTop.ToTopPlane(burnerSpot), null);
            yield return actor.Use("put the potato on the stove top next to the pan", stoveTop.SpotWorldPosition(nextToPan),
                InteractionAction.Place, () => me.DevSendPlace(stoveTop.Index, nextToPan));
            Debug.Log("[Demo] potato next to the pan (" + Time.time.ToString("0.0") + " s)");
            if (shot != null) yield return shot("potato-next-to-pan");

            NetworkObject potObject = pot.NetworkObject;
            foreach (Station crate in new[] { potatoCrate, onionCrate })
            {
                string id = DemoIngredients.All[crate.IngredientIndex].Id;
                yield return Buy(actor, "take " + id, crate, laneZ);
                yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
                yield return actor.WalkTo(atB, false, false);
                yield return actor.Use("put it into the pot", ContentPoint(pot), InteractionAction.PutInto,
                    () => me.DevSendUse(potObject, VesselRules.ActionUse));
                if (shot != null) yield return shot(id + "-in-pot");
            }
            yield return actor.Use("switch the pot's burner on", potSwitch.transform.position, InteractionAction.Press,
                () => me.DevSendStation(potSwitch.Station.Index, potSwitch.Option));
            Debug.Log("[Demo] cookware demo done (" + Time.time.ToString("0.0") + " s): pot holds " + pot.ContentCount + " ingredients");
            Vector3 between = (pan.transform.position + pot.transform.position) * 0.5f + Vector3.up * 0.1f;
            if (shot == null)
            {
                yield return actor.AimAt(between, lookSeconds);
                yield break;
            }
            // The cook tour: watch the pot heat up.
            float start = Time.unscaledTime;
            yield return actor.AimAt(between, 2f);
            yield return shot("burner-on");
            foreach (float at in new[] { 10f, 20f, 30f })
            {
                if (at > lookSeconds) break;
                while (Time.unscaledTime - start < at) yield return null;
                yield return shot("heating-" + at.ToString("0") + "s");
            }
        }

        /// <summary>Walks along the lane to the crate and takes one (aims at it, clicks; or sends the request directly).</summary>
        private static IEnumerator Buy(TourActor actor, string what, Station crate, float laneZ)
        {
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(crate.transform.position.x + 1f, 0f, laneZ), false, false);
            yield return actor.Use(what, crate.transform.position, InteractionAction.Buy, () => actor.Interactor.DevSendStation(crate.Index, 0));
            yield return actor.LevelView();
        }

        /// <summary>Where a vessel draws its contents (inside the pot, on the board); its position if none.</summary>
        private static Vector3 ContentPoint(Component vessel)
        {
            Transform anchor = vessel.transform.Find("Contents");
            return anchor != null ? anchor.position : vessel.transform.position + Vector3.up * 0.05f;
        }
    }
}
