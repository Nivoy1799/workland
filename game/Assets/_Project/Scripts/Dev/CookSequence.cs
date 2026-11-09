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
    /// The cook tour passes <paramref name="capture"/> to take a picture after each step (spec 027b: and frames around the
    /// pan's pick-up and put-down and the potato going into the pot, the onion in slow motion). Spec 027c, tour only: the
    /// potato next to the pan then goes into the pan, the pan's burner goes on and stays on until it burns (a shot every
    /// <see cref="HeatShotEvery"/> s and frames when it burns: ring, colour, smoke), and the burnt potato is served at the
    /// counter (frames of the reward moment).
    /// </summary>
    public static class CookSequence
    {
        /// <summary>Spec 027c: seconds between the shots while the pan cooks, and the longest it waits for Burnt.</summary>
        public const float HeatShotEvery = 5f;
        public const float BurnWait = 95f;
        public const float BurnFramesSeconds = 1.5f;
        public const float RewardFramesSeconds = 2.6f;
        public const float WatchAbove = 0.3f;

        public static IEnumerator Run(TourActor actor, CookCapture capture, float lookSeconds)
        {
            Func<string, IEnumerator> shot = capture?.Shot;
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            // Spec 028b: the potato and the onion come from the dry shelf's slots.
            StockSlot potatoCrate = FindSlot("potato"), onionCrate = FindSlot("onion");
            var stoves = new List<Station>();
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Kind == StationKind.Stove) stoves.Add(s);
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
                Debug.LogWarning("[Demo] the potato or onion slot, two stoves, the pan or the pot not found");
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

            // Spec 027a: the cook walks and stands on the aisle's centre line (it starts there, or outside the staff gap on
            // the same line in a tour), between the back line and the front counter.
            float laneZ = stoveA.transform.position.z - TourRules.AisleOffset;
            float standZ = laneZ;
            Vector3 atA = new Vector3(stoveA.transform.position.x, 0f, standZ);
            Vector3 atB = new Vector3(stoveB.transform.position.x, 0f, standZ);
            // Spec 029 (AC5, AC6): first an order of fries from potato to the counter, through the fryer.
            if (capture != null) yield return FryFries(actor, capture, laneZ);
            Debug.Log("[Demo] walking to the first stove (" + Time.time.ToString("0.0") + " s)");
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(atA.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(atA, false, false);
            if (shot != null) yield return shot("at-stove");
            NetworkObject panObject = pan.NetworkObject;
            yield return actor.Use("pick up the pan", ContentPoint(pan), InteractionAction.PickUp, () => me.DevSendPickup(panObject),
                capture?.Before("pan-highlight", "pan-pickup", frames));
            if (shot != null) yield return shot("pan-in-hand");

            yield return actor.LevelView();
            yield return actor.WalkTo(atB, false, false);
            Vector3 burnerSpot = freeBurner.SpotWorldPosition(0);
            yield return actor.Use("put the pan on the free burner", burnerSpot, InteractionAction.Place, () => me.DevSendPlace(freeBurner.Index, 0),
                capture?.Before("pan-ghost-on-burner", "pan-place", frames));
            Debug.Log("[Demo] the pan is on the burner (" + Time.time.ToString("0.0") + " s)");
            if (shot != null) yield return shot("pan-on-burner");

            // Spec 028b: the shelf slot's highlight and prompt before the click, then frames of the potato flying into the hand.
            yield return Buy(actor, "take a potato", potatoCrate, laneZ, capture?.Before("potato-slot-highlight", "potato-take", frames));
            if (shot != null) yield return shot("potato-in-hand");
            yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(atB, false, false);
            int nextToPan = PlaceSpots.NearestFree(stoveTop.Size, stoveTop.ToTopPlane(burnerSpot), null);
            yield return actor.Use("put the potato on the stove top next to the pan", stoveTop.SpotWorldPosition(nextToPan),
                InteractionAction.Place, () => me.DevSendPlace(stoveTop.Index, nextToPan), capture?.Before("potato-aims-next-to-pan", null, 0f));
            Debug.Log("[Demo] potato next to the pan (" + Time.time.ToString("0.0") + " s)");
            if (shot != null) yield return shot("potato-next-to-pan");

            NetworkObject potObject = pot.NetworkObject;
            foreach (StockSlot crate in new[] { potatoCrate, onionCrate })
            {
                string id = crate.IngredientId;
                yield return Buy(actor, "take " + id, crate, laneZ);
                yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
                yield return actor.WalkTo(atB, false, false);
                // Spec 027b: the potato at normal speed, the onion in slow motion (the drop shows at 0.1 s frames).
                bool slow = capture != null && crate == onionCrate;
                if (slow) capture.TweenScale?.Invoke(TourRules.SlowMotionScale);
                yield return actor.Use("put it into the pot", ContentPoint(pot), InteractionAction.PutInto,
                    () => me.DevSendUse(potObject, VesselRules.ActionUse),
                    capture?.Before(id + "-aims-at-pot", id + "-into-pot" + (slow ? "-x4" : ""), frames));
                if (slow) capture.TweenScale?.Invoke(1f);
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
            yield return actor.AimAt(between, 2f);
            yield return shot("burner-on");

            // Spec 027c: the potato from the stove top into the pan, the pan's burner on, and wait until it burns.
            Food potato = NearestFood(stoveTop.SpotWorldPosition(nextToPan));
            StationButton panSwitch = SwitchOf(freeBurner);
            if (potato == null || panSwitch == null)
            {
                Debug.LogWarning("[Demo] no potato next to the pan or no switch for the pan's burner");
                actor.CountTimeout("cook: pan heat setup missing");
                yield break;
            }
            NetworkObject potatoObject = potato.NetworkObject;
            yield return actor.Use("pick up the potato next to the pan", potato.transform.position, InteractionAction.PickUp,
                () => me.DevSendPickup(potatoObject));
            yield return actor.Use("put the potato into the pan", ContentPoint(pan), InteractionAction.PutInto,
                () => me.DevSendUse(panObject, VesselRules.ActionUse));
            yield return actor.Use("switch the pan's burner on", panSwitch.transform.position, InteractionAction.Press,
                () => me.DevSendStation(panSwitch.Station.Index, panSwitch.Option));
            float start = Time.unscaledTime;
            Debug.Log("[Demo] the pan's burner is on (" + Time.time.ToString("0.0") + " s)");
            // A little above the two, so neither ring sits on the view centre (it would hide there in first person).
            yield return actor.AimAt(between + Vector3.up * WatchAbove, 1f);
            float nextShot = 0f;
            bool burnt = false;
            while (Time.unscaledTime - start < BurnWait)
            {
                float at = Time.unscaledTime - start;
                if (!burnt && pan.Zone == HeatZone.Burnt)
                {
                    burnt = true;
                    LogHeat("burnt", at, pan, pot);
                    if (capture.Frames != null) yield return capture.Frames("pan-burns", BurnFramesSeconds);
                    yield return new WaitForSecondsRealtime(BurnFramesSeconds + 0.5f);
                    yield return shot("pan-burnt-smoke");
                    break;
                }
                if (at >= nextShot)
                {
                    LogHeat("shot", at, pan, pot);
                    yield return shot("heat-" + Mathf.RoundToInt(at).ToString("00") + "s-" + pan.Zone.ToString().ToLowerInvariant());
                    nextShot += HeatShotEvery;
                }
                yield return null;
            }
            if (!burnt) actor.CountTimeout("cook: the pan did not burn in " + BurnWait + " s");

            // Spec 027c: take the burnt potato out and serve it at the counter (the reward moment).
            yield return actor.Use("take the burnt potato out of the pan", ContentPoint(pan), InteractionAction.Take,
                () => me.DevSendUse(panObject, VesselRules.ActionUse));
            Station counter = null;
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Kind == StationKind.Counter) counter = s;
            if (counter == null)
            {
                actor.CountTimeout("cook: no counter");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(counter.transform.position.x, 0f, laneZ), false, false);
            // The station's pivot is its centre: aim at the middle of its top.
            Vector3 counterTop = counter.transform.position + Vector3.up * (Worktown.Game.Art.ArtSizeTable.ServingCounterHeight * 0.5f - 0.02f);
            yield return actor.Use("serve it at the counter", counterTop, InteractionAction.Sell,
                () => me.DevSendStation(counter.Index, 0), capture.Before("serve-aim", "serve-reward", RewardFramesSeconds));
            yield return shot("served");

            yield return CutCucumber(actor, capture, laneZ);
        }

        /// <summary>
        /// Spec 028 AC5: buy a cucumber, put it on the board, take the knife and make two strokes (halves, then slices). The
        /// strokes are sent as the knife's use request, as a mouse drag would. Spec 028b: the cucumber comes from the fridge (a
        /// shot of its slot's highlight and prompt, then frames of the copy flying into the hand).
        /// </summary>
        private static IEnumerator CutCucumber(TourActor actor, CookCapture capture, float laneZ)
        {
            PickupInteractor me = actor.Interactor;
            StockSlot slot = FindSlot("cucumber");
            Vessel board = null;
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
                if (v.Kind == VesselKind.Board) board = v;
            Knife knife = UnityEngine.Object.FindFirstObjectByType<Knife>();
            if (slot == null || board == null || knife == null)
            {
                actor.CountTimeout("cook: cucumber slot, board or knife missing");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(slot.Anchor.x, 0f, laneZ), false, false);
            yield return capture.Shot("cucumber-fridge");
            yield return actor.Use("take a cucumber", slot.Anchor, InteractionAction.Buy, () => me.DevSendStation(slot.Station.Index, slot.Option),
                capture.Before("cucumber-slot-highlight", "cucumber-take", TourRules.ClickFramesSeconds));
            yield return capture.Shot("cucumber-in-hand");
            if (!me.IsHoldingSomething() || !HeldName(me).Contains("cucumber"))
                actor.CountTimeout("cook: the fridge gave no cucumber (" + HeldName(me) + ")");
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(board.transform.position.x, 0f, laneZ), false, false);
            NetworkObject boardObject = board.NetworkObject;
            yield return actor.Use("put the cucumber on the board", ContentPoint(board), InteractionAction.PutInto,
                () => me.DevSendUse(boardObject, VesselRules.ActionUse));
            NetworkObject knifeObject = knife.GetComponent<NetworkObject>();
            yield return actor.Use("pick up the knife", knife.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(knifeObject));
            yield return actor.AimAt(ContentPoint(board), 1f);
            for (int stroke = 1; stroke <= 2; stroke++)
            {
                Debug.Log("[Demo] knife stroke " + stroke + " on the cucumber (" + Time.time.ToString("0.0") + " s)");
                me.DevSendUse(boardObject, VesselRules.ActionStroke);
                yield return new WaitForSeconds(1f);
                yield return capture.Shot(stroke == 1 ? "cucumber-halves" : "cucumber-slices");
            }
            Debug.Log("[Demo] the board holds: " + board.Title.Value);
            if (!board.Title.Value.Contains("slice")) actor.CountTimeout("cook: the cucumber is not sliced (" + board.Title.Value + ")");
        }

        /// <summary>Spec 029: seconds between the shots while the basket fries, and the longest it waits for a zone.</summary>
        public const float FryShotEvery = 5f;
        public const float FryWait = 60f;
        public const float DrainFramesSeconds = 1.5f;

        /// <summary>
        /// Spec 029 AC5/AC6: fries from potato to the counter. A potato from the shelf onto the board, the knife cuts sticks (two
        /// strokes, a turn, a stroke), the knife goes back, the sticks go into basket 0, the fryer goes on, the basket goes down
        /// (frames of the arc), the ring fills (a shot every <see cref="FryShotEvery"/> s with the zone), one shake in the good
        /// window, the basket comes up at Perfect (frames of the arc and the drips), the fries come out once drained, onto a plate
        /// on the plating worktop (the fries box), and the plate is served at the counter (the orders waiting are logged first).
        /// </summary>
        private static IEnumerator FryFries(TourActor actor, CookCapture capture, float laneZ)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            StockSlot potatoSlot = FindSlot("potato");
            Vessel board = null, plate = null;
            Station fryer = null, counter = null;
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Fryer) fryer = s;
                else if (s.Kind == StationKind.Counter) counter = s;
            }
            Vessel basket = null;
            foreach (FryerBasket fb in UnityEngine.Object.FindObjectsByType<FryerBasket>(FindObjectsSortMode.None))
                if (fb.Basket == 0 && fb.Fryer == fryer) basket = fb.GetComponent<Vessel>();
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind == VesselKind.Board) board = v;
                else if (v.Kind == VesselKind.Plate && fryer != null
                         && (plate == null || Distance(v, fryer) < Distance(plate, fryer))) plate = v;
            }
            Knife knife = UnityEngine.Object.FindFirstObjectByType<Knife>();
            StationButton power = null, lower = null;
            foreach (StationButton b in UnityEngine.Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
            {
                if (b.Station != fryer || fryer == null) continue;
                if (b.Option == FryerRules.PowerOption) power = b;
                else if (b.Option == FryerRules.BasketOption(0)) lower = b;
            }
            if (potatoSlot == null || board == null || plate == null || fryer == null || counter == null || basket == null || knife == null
                || power == null || lower == null)
            {
                Debug.LogWarning("[Demo] fries: the potato slot, board, plate, fryer, counter, basket, knife or a fryer button is missing");
                actor.CountTimeout("cook: fryer setup missing");
                yield break;
            }
            NetworkObject boardObject = board.NetworkObject, basketObject = basket.NetworkObject, plateObject = plate.NetworkObject;
            NetworkObject knifeObject = knife.GetComponent<NetworkObject>();
            Vector3 knifeHome = knife.transform.position;
            Vector3 atFryer = new Vector3(fryer.transform.position.x, 0f, laneZ);

            // Potato, board, sticks.
            Debug.Log("[Demo] fries: start (" + Time.time.ToString("0.0") + " s)");
            yield return Buy(actor, "take a potato for fries", potatoSlot, laneZ);
            yield return actor.WalkTo(new Vector3(board.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("put the potato on the board", ContentPoint(board), InteractionAction.PutInto,
                () => me.DevSendUse(boardObject, VesselRules.ActionUse));
            yield return actor.Use("pick up the knife", knife.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(knifeObject));
            yield return actor.AimAt(ContentPoint(board), 0.6f);
            foreach (int action in new[] { VesselRules.ActionStroke, VesselRules.ActionStroke, VesselRules.ActionTurn, VesselRules.ActionStroke })
            {
                me.DevSendUse(boardObject, action);
                yield return new WaitForSeconds(0.7f);
            }
            Debug.Log("[Demo] fries: the board holds " + board.Title.Value);
            yield return capture.Shot("fries-sticks");
            if (!board.Title.Value.Contains("stick")) actor.CountTimeout("cook: the potato is not cut into sticks (" + board.Title.Value + ")");
            PlaceSurface prep = SurfaceUnder(knifeHome);
            if (prep != null)
            {
                int spot = PlaceSpots.NearestFree(prep.Size, prep.ToTopPlane(knifeHome), null);
                yield return actor.Use("put the knife back", prep.SpotWorldPosition(spot), InteractionAction.Place,
                    () => me.DevSendPlace(prep.Index, spot));
            }
            yield return actor.Use("take the sticks", ContentPoint(board), InteractionAction.Take, () => me.DevSendUse(boardObject, VesselRules.ActionUse));

            // The fryer: on, sticks in, down.
            yield return actor.LevelView();
            yield return actor.WalkTo(atFryer, false, false);
            yield return capture.Shot("fryer");
            yield return actor.Use("switch the fryer on", power.transform.position, InteractionAction.Press,
                () => me.DevSendStation(fryer.Index, FryerRules.PowerOption), capture.Before("fryer-power-aim", null, 0f));
            yield return actor.Use("put the sticks into the basket", ContentPoint(basket), InteractionAction.PutInto,
                () => me.DevSendUse(basketObject, VesselRules.ActionUse), capture.Before("basket-aim", "basket-in", frames));
            yield return actor.Use("lower the basket", lower.transform.position, InteractionAction.Press,
                () => me.DevSendStation(fryer.Index, FryerRules.BasketOption(0)), capture.Before("basket-button-aim", "basket-lower", frames));
            float start = Time.unscaledTime;
            Debug.Log("[Demo] fries: the basket is down (" + Time.time.ToString("0.0") + " s)");
            yield return actor.AimAt(ContentPoint(basket) + Vector3.up * WatchAbove, 0.8f);

            // The ring fills; one shake in the good window; up at Perfect.
            float nextShot = 0f;
            bool shaken = false;
            while (Time.unscaledTime - start < FryWait)
            {
                float at = Time.unscaledTime - start;
                HeatZone zone = basket.Zone;
                if (!shaken && zone == HeatZone.Good)
                {
                    shaken = true;
                    LogBasket("shake", at, basket);
                    yield return actor.Use("shake the basket", ContentPoint(basket), InteractionAction.Shake,
                        () => me.DevSendUse(basketObject, VesselRules.ActionShake), capture.Before("shake-aim", "shake", 0.6f));
                    yield return actor.AimAt(ContentPoint(basket) + Vector3.up * WatchAbove, 0.5f);
                    continue;
                }
                if (zone == HeatZone.Perfect && Time.unscaledTime - start > 1f && shaken)
                {
                    LogBasket("perfect", at, basket);
                    yield return new WaitForSecondsRealtime(1f);
                    break;
                }
                if (at >= nextShot)
                {
                    LogBasket("shot", at, basket);
                    yield return capture.Shot("fry-" + Mathf.RoundToInt(at).ToString("00") + "s-" + zone.ToString().ToLowerInvariant());
                    nextShot += FryShotEvery;
                }
                yield return null;
            }
            if (!shaken || basket.Zone != HeatZone.Perfect) actor.CountTimeout("cook: the basket did not reach Perfect with a shake (" + basket.Zone + ")");
            yield return actor.Use("raise the basket", lower.transform.position, InteractionAction.Press,
                () => me.DevSendStation(fryer.Index, FryerRules.BasketOption(0)), capture.Before("raise-aim", "basket-raise", frames));
            yield return actor.AimAt(ContentPoint(basket), 0.2f);
            if (capture.Frames != null) yield return capture.Frames("drain-drips", DrainFramesSeconds);
            float drainStart = Time.unscaledTime;
            while (FryerRules.StateOf(basket.Mode.Value) != BasketState.Raised && Time.unscaledTime - drainStart < 5f) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return capture.Shot("drained");
            yield return actor.Use("take the fries out", ContentPoint(basket), InteractionAction.Take,
                () => me.DevSendUse(basketObject, VesselRules.ActionUse));
            Debug.Log("[Demo] fries: in the hands: " + HeldName(me));
            yield return capture.Shot("fries-in-hand");

            // Onto a plate at the plating table (the fries box), then to the counter.
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(plate.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("put the fries on the plate", ContentPoint(plate), InteractionAction.PutInto,
                () => me.DevSendUse(plateObject, VesselRules.ActionUse), capture.Before("plate-aim", "fries-onto-plate", frames));
            yield return actor.AimAt(ContentPoint(plate), 0.6f);
            yield return capture.Shot("fries-box-on-plate");
            yield return actor.Use("pick up the plate", ContentPoint(plate), InteractionAction.PickUp, () => me.DevSendPickup(plateObject));
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(counter.transform.position.x, 0f, laneZ), false, false);
            LogOrders();
            Vector3 counterTop = counter.transform.position + Vector3.up * (Worktown.Game.Art.ArtSizeTable.ServingCounterHeight * 0.5f - 0.02f);
            yield return actor.Use("serve the fries at the counter", counterTop, InteractionAction.Sell,
                () => me.DevSendStation(counter.Index, 0), capture.Before("fries-serve-aim", "fries-served", RewardFramesSeconds));
            ServeFeed feed = UnityEngine.Object.FindFirstObjectByType<ServeFeed>();
            yield return new WaitForSecondsRealtime(0.5f);
            string latest = feed != null && feed.Entries.Count > 0 ? feed.Entries[0] : "?";
            Debug.Log("[Demo] fries: served (" + Time.time.ToString("0.0") + " s); feed: " + latest);
            if (!latest.Contains("Fries order: exact order")) Debug.LogWarning("[Demo] fries: no exact Fries order was served");
        }

        private static float Distance(Component a, Component b) => Vector3.Distance(a.transform.position, b.transform.position);

        /// <summary>The place surface whose top is under <paramref name="point"/> (the knife's worktop), or null.</summary>
        private static PlaceSurface SurfaceUnder(Vector3 point)
        {
            foreach (PlaceSurface s in UnityEngine.Object.FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
            {
                Vector2 local = s.ToTopPlane(point);
                if (s.IsBurner || Mathf.Abs(local.x) > s.Size.x * 0.5f || Mathf.Abs(local.y) > s.Size.y * 0.5f) continue;
                return s;
            }
            return null;
        }

        /// <summary>Spec 029 AC6: the orders the waiting customers show (their synced dish).</summary>
        private static void LogOrders()
        {
            var orders = new List<string>();
            foreach (Worktown.Game.Npc.Customer c in UnityEngine.Object.FindObjectsByType<Worktown.Game.Npc.Customer>(FindObjectsSortMode.None))
                orders.Add(Worktown.Game.Npc.Menu.NameOf(c.Dish.Value));
            Debug.Log("[Demo] fries: orders in the queue: " + string.Join(", ", orders));
        }

        private static void LogBasket(string what, float at, Vessel basket)
        {
            var bubbles = basket.GetComponent<Worktown.Game.Art.FryerBubbles>();
            var view = basket.GetComponent<Worktown.Game.Art.BasketView>();
            Debug.Log("[Fry] " + what + " at " + at.ToString("0.0") + " s: " + Describe(basket) + ", " + FryerRules.StateOf(basket.Mode.Value)
                + ", bubbles " + (bubbles != null ? bubbles.Rate.ToString("0") : "-") + "/s, pose " + (view != null ? view.Pose.ToString("0.00") : "-"));
        }

        /// <summary>Spec 027c: the zone, progress and ring of the pan and pot, for the tour log.</summary>
        private static void LogHeat(string what, float at, Vessel pan, Vessel pot)
        {
            Debug.Log("[Heat] " + what + " at " + at.ToString("0.0") + " s: pan " + Describe(pan) + "; pot " + Describe(pot));
        }

        private static string Describe(Vessel v)
        {
            var ring = v.GetComponent<Worktown.Game.Art.HeatRingView>();
            return v.Zone + " " + v.HeatProgress.Value + (v.Warning ? " warn" : string.Empty)
                + (ring != null && ring.IsShown ? " ring" : " no-ring");
        }

        private static Food NearestFood(Vector3 point)
        {
            Food best = null;
            float bestDistance = 0.3f;
            foreach (Food f in UnityEngine.Object.FindObjectsByType<Food>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(f.transform.position, point);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = f;
                }
            }
            return best;
        }

        private static StationButton SwitchOf(PlaceSurface burner)
        {
            foreach (StationButton b in UnityEngine.Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
                if (burner != null && b.Station != null && b.Station.Index == burner.StoveIndex
                    && b.Option == StationRules.StoveOption(burner.Burner, StationRules.StoveSwitch)) return b;
            return null;
        }

        /// <summary>
        /// Walks along the lane to a stock slot and takes one (aims at it, clicks; or sends the request directly). Spec 028b:
        /// a slot of the fridge or the dry shelf (RequestStation with the slot as the option).
        /// </summary>
        private static IEnumerator Buy(TourActor actor, string what, StockSlot slot, float laneZ, Func<IEnumerator> beforeClick = null)
        {
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(slot.Anchor.x + 1f, 0f, laneZ), false, false);
            yield return actor.Use(what, slot.Anchor, InteractionAction.Buy,
                () => actor.Interactor.DevSendStation(slot.Station.Index, slot.Option), beforeClick);
            if (!actor.Interactor.IsHoldingSomething() || !HeldName(actor.Interactor).Contains(slot.IngredientId.Replace('_', ' ')))
                Debug.LogWarning("[Demo] " + what + ": the hands hold '" + HeldName(actor.Interactor) + "'");
            yield return actor.LevelView();
        }

        /// <summary>Spec 028b: the stock slot that holds <paramref name="id"/>, or null.</summary>
        private static StockSlot FindSlot(string id)
        {
            foreach (StockSlot s in UnityEngine.Object.FindObjectsByType<StockSlot>(FindObjectsSortMode.None))
                if (s.IngredientId == id && s.Station != null) return s;
            return null;
        }

        /// <summary>The synced name of what the player holds ("cucumber"), or an empty string.</summary>
        private static string HeldName(PickupInteractor me)
        {
            Pickup held = me.FindHeldPickup();
            return held != null && held.TryGetComponent(out Food food) ? food.DisplayName.Value : string.Empty;
        }

        /// <summary>Where a vessel draws its contents (inside the pot, on the board); its position if none.</summary>
        private static Vector3 ContentPoint(Component vessel)
        {
            if (vessel is Vessel v) return v.ContentPoint; // spec 029: a basket draws its contents inside its rig
            Transform anchor = vessel.transform.Find("Contents");
            return anchor != null ? anchor.position : vessel.transform.position + Vector3.up * 0.05f;
        }
    }
}
