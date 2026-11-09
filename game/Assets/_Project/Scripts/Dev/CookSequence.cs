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
            StockSlot potatoSlot = FindSlot("potato"), onionSlot = FindSlot("onion");
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
            if (potatoSlot == null || onionSlot == null || stoveA == null || stoveB == null || pan == null || pot == null)
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
            // Spec 034 (AC4): the tomatoes run out (the host starts them at one), and a pack is ordered at the terminal; its box comes
            // while the fries cook and is unpacked right after them.
            if (capture != null) yield return RunOutAndOrder(actor, capture, laneZ);
            // Spec 029 (AC5, AC6): first an order of fries from potato to the counter, through the fryer.
            if (capture != null) yield return FryFries(actor, capture, laneZ);
            if (capture != null) yield return UnpackTheDelivery(actor, capture, laneZ); // spec 034
            // Spec 031 (AC5): then one falafel wrap on the assembly board, layer by layer, folded, in paper, to the counter.
            if (capture != null) yield return BuildFalafelWrap(actor, capture, laneZ);
            // Spec 030 (AC5, AC6): then "Grilled veggies" with seitan strips on the griddle (flips, grill marks), to the counter.
            if (capture != null) yield return GrillVeggies(actor, capture, laneZ);
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
            yield return Buy(actor, "take a potato", potatoSlot, laneZ, capture?.Before("potato-slot-highlight", "potato-take", frames));
            if (shot != null) yield return shot("potato-in-hand");
            yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(atB, false, false);
            int nextToPan = PlaceSpots.NearestFree(stoveTop.Size, stoveTop.ToTopPlane(burnerSpot), null);
            yield return actor.Use("put the potato on the stove top next to the pan", stoveTop.SpotWorldPosition(nextToPan),
                InteractionAction.Place, () => me.DevSendPlace(stoveTop.Index, nextToPan), capture?.Before("potato-aims-next-to-pan", null, 0f));
            Debug.Log("[Demo] potato next to the pan (" + Time.time.ToString("0.0") + " s)");
            if (shot != null) yield return shot("potato-next-to-pan");

            NetworkObject potObject = pot.NetworkObject;
            foreach (StockSlot slot in new[] { potatoSlot, onionSlot })
            {
                string id = slot.IngredientId;
                yield return Buy(actor, "take " + id, slot, laneZ);
                yield return actor.WalkTo(new Vector3(atB.x, 0f, laneZ), false, false);
                yield return actor.WalkTo(atB, false, false);
                // Spec 027b: the potato at normal speed, the onion in slow motion (the drop shows at 0.1 s frames).
                bool slow = capture != null && slot == onionSlot;
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
            yield return PayAtRegister(actor, capture, laneZ, "potato"); // spec 032: whoever got it pays

            yield return CutCucumber(actor, capture, laneZ);
            yield return ShowSummary(actor, capture); // spec 032: tips, reviews, missed by reason
        }

        // ---- Spec 032: orders 2.0 in the tour ----

        /// <summary>
        /// Spec 032 AC4: the guest who got the dish pays at the register. Card: one click on the terminal (the beep). Cash: the change
        /// the register shows is counted out coin by coin (CashRules.Coins), a shot before each click and frames of the first coin
        /// landing, then handed over (frames of the reward over the register). Nothing to do when nobody pays (a wholesaler sale).
        /// </summary>
        private static IEnumerator PayAtRegister(TourActor actor, CookCapture capture, float laneZ, string prefix)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            Worktown.Game.Shop.Register register = UnityEngine.Object.FindFirstObjectByType<Worktown.Game.Shop.Register>();
            Station till = register != null ? register.GetComponent<Station>() : null;
            if (till == null)
            {
                actor.CountTimeout("cook: no register");
                yield break;
            }
            float wait = Time.unscaledTime + 3f;
            while (!Worktown.Game.Shop.RegisterRules.HasPayer(register.Payment.Value) && Time.unscaledTime < wait) yield return null;
            ushort payment = register.Payment.Value;
            if (!Worktown.Game.Shop.RegisterRules.HasPayer(payment))
            {
                Debug.Log("[Demo] " + prefix + ": nobody pays at the register (" + Time.time.ToString("0.0") + " s)");
                yield break;
            }
            var view = till.GetComponent<Worktown.Game.Shop.RegisterView>();
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(till.transform.position.x, 0f, laneZ), false, false);
            Debug.Log("[Demo] " + prefix + ": at the register (" + Time.time.ToString("0.0") + " s): "
                + (Worktown.Game.Shop.RegisterRules.IsCard(payment) ? "card" : "cash, change " + Worktown.Game.Shop.RegisterRules.ChangeDue(payment)));
            yield return actor.AimAt(till.transform.position + Vector3.up * 0.15f, 0.8f);
            yield return capture.Shot(prefix + "-register");
            if (Worktown.Game.Shop.RegisterRules.IsCard(payment))
            {
                till.TryGetSlot(Worktown.Game.Shop.RegisterRules.CardOption, out StockSlot terminal);
                yield return actor.Use("pay by card", terminal.Anchor, InteractionAction.Pay,
                    () => me.DevSendStation(till.Index, Worktown.Game.Shop.RegisterRules.CardOption),
                    capture.Before(prefix + "-card-aim", prefix + "-card-paid", RewardFramesSeconds));
            }
            else
            {
                int change = Worktown.Game.Shop.RegisterRules.ChangeDue(payment);
                bool first = true;
                foreach (int coin in Worktown.Sim.Economy.CashRules.Coins(change))
                {
                    int option = IndexOfCoin(coin);
                    till.TryGetSlot(option, out StockSlot slot);
                    yield return actor.Use("give $" + coin, slot.Anchor, InteractionAction.Pay, () => me.DevSendStation(till.Index, option),
                        capture.Before(prefix + "-give-" + coin + "-aim", first ? prefix + "-coin-" + coin : null, frames));
                    first = false;
                }
                Debug.Log("[Demo] " + prefix + ": the tray holds $" + register.Tray.Value + ", the display says '"
                    + (view != null ? view.DisplayText : "?") + "'");
                till.TryGetSlot(Worktown.Game.Shop.RegisterRules.GiveChangeOption, out StockSlot give);
                yield return actor.Use("hand over the change", give.Anchor, InteractionAction.Pay,
                    () => me.DevSendStation(till.Index, Worktown.Game.Shop.RegisterRules.GiveChangeOption),
                    capture.Before(prefix + "-handover-aim", prefix + "-paid", RewardFramesSeconds));
            }
            yield return capture.Shot(prefix + "-after-paying");
            ServeFeed feed = UnityEngine.Object.FindFirstObjectByType<ServeFeed>();
            string latest = feed != null && feed.Entries.Count > 0 ? feed.Entries[0] : "?";
            Debug.Log("[Demo] " + prefix + ": paid (" + Time.time.ToString("0.0") + " s); feed: " + latest);
            if (!latest.StartsWith("Guest paid")) actor.CountTimeout("cook: the " + prefix + " guest did not pay");
            if (latest.Contains("wrong change")) Debug.LogWarning("[Demo] " + prefix + ": wrong change");
            // The guest takes its review and leaves: a look at it on its way.
            yield return actor.AimAt(new Vector3(till.transform.position.x + 0.8f, 1.7f, till.transform.position.z - 1.4f), 0.8f);
            yield return capture.Shot(prefix + "-review-bubble");
            yield return actor.LevelView();
        }

        private static int IndexOfCoin(int value)
        {
            for (int i = 0; i < Worktown.Sim.Economy.CashRules.Tray.Count; i++)
                if (Worktown.Sim.Economy.CashRules.Tray[i] == value) return i;
            return 0;
        }

        /// <summary>
        /// Spec 032 AC4: the ticket rail (the modifiers in plain words, "no X" struck through) and the guests' patience rings, a shot
        /// of each and their state in the log.
        /// </summary>
        private static IEnumerator LookAtOrders(TourActor actor, CookCapture capture, float laneZ, string name)
        {
            Worktown.Game.UI.TicketRailView rail = UnityEngine.Object.FindFirstObjectByType<Worktown.Game.UI.TicketRailView>();
            if (rail == null) yield break;
            yield return actor.LevelView();
            // A little back from the aisle's middle (towards the back line), so the rail is read less steeply from below.
            yield return actor.WalkTo(new Vector3(rail.transform.position.x, 0f, laneZ + 0.4f), false, false);
            // The view's centre just under the board: first person hides labels right at the centre (027d), the cards stay above it.
            yield return actor.AimAt(rail.transform.position + Vector3.down * 0.75f, 1.0f);
            var lines = new List<string>();
            for (int card = 0; card < rail.Tickets.Count; card++)
            {
                string mods = string.Empty;
                for (int line = 0; line < Worktown.Game.UI.TicketRailView.ModLines; line++)
                {
                    string text = rail.ModText(card, line, out bool struck);
                    if (text.Length > 0) mods += " [" + text + (struck ? ", struck" : string.Empty) + "]";
                }
                lines.Add(Worktown.Game.Npc.Menu.NameOf(Worktown.Game.Npc.CustomerRules.TicketDish(rail.Tickets[card])) + mods);
            }
            Debug.Log("[Orders] " + name + ": rail " + string.Join("; ", lines));
            yield return capture.Shot(name + "-rail");
            // The guests across the counter: their bubbles and patience rings.
            var rings = new List<string>();
            Vector3 middle = Vector3.zero;
            int n = 0;
            foreach (Worktown.Game.Npc.Customer c in UnityEngine.Object.FindObjectsByType<Worktown.Game.Npc.Customer>(FindObjectsSortMode.None))
            {
                var ring = c.GetComponent<Worktown.Game.Npc.PatienceRingView>();
                rings.Add(Worktown.Game.Npc.Menu.NameOf(c.Dish.Value) + " " + Mathf.RoundToInt(100f * c.Patience.Value / 255f) + " %"
                    + (ring != null && ring.IsShown ? " (ring)" : string.Empty) + (c.Paying ? " paying" : string.Empty));
                if (!c.Waiting) continue;
                middle += c.transform.position;
                n++;
            }
            Debug.Log("[Orders] " + name + ": guests " + string.Join(", ", rings));
            if (n > 0)
            {
                // From beside the register, looking along the queue: the rail is not in front of the rings.
                yield return actor.LevelView();
                yield return actor.WalkTo(new Vector3(rail.transform.position.x + 1.6f, 0f, laneZ), false, false);
                yield return actor.AimAt(middle / n + Vector3.up * 1.75f, 1.0f);
                yield return capture.Shot(name + "-patience");
            }
            yield return actor.LevelView();
        }

        /// <summary>Spec 032 AC4: the shift summary panel (F3) with the tips, the reviews and the missed guests; a shot and its lines in the log.</summary>
        private static IEnumerator ShowSummary(TourActor actor, CookCapture capture)
        {
            Worktown.Game.UI.ShiftSummaryView summary = UnityEngine.Object.FindFirstObjectByType<Worktown.Game.UI.ShiftSummaryView>();
            if (summary == null)
            {
                actor.CountTimeout("cook: no shift summary panel");
                yield break;
            }
            summary.DevPin(true);
            yield return new WaitForSecondsRealtime(0.6f);
            Debug.Log("[Summary] " + summary.DevText());
            yield return capture.Shot("shift-summary");
            summary.DevPin(false);
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
            yield return TakeWhileKnifeFliesIn(actor, capture, slot, knife, laneZ);
            yield return actor.LevelView();
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

        /// <summary>
        /// Spec 029b AC3 (028b review 1): a stock take clicked while the knife's pick-up is still pending. The knife click starts
        /// its predicted arc; in the same frame the take starts as the old rules allowed (the rule now offers no take: the knife
        /// counts as held, logged). The knife arrives first, which must not confirm the take: the server refuses the take
        /// (hands full, nothing charged) and the cucumber copy settles back into its slot. Frames of it, then the knife goes
        /// back to the prep worktop.
        /// </summary>
        private static IEnumerator TakeWhileKnifeFliesIn(TourActor actor, CookCapture capture, StockSlot slot, Knife knife, float laneZ)
        {
            PickupInteractor me = actor.Interactor;
            Vector3 knifeHome = knife.transform.position;
            yield return actor.WalkTo(new Vector3((slot.Anchor.x + knifeHome.x) * 0.5f, 0f, laneZ), false, false);
            yield return actor.AimAt(knife.transform.position, 3f, () => me.CurrentAction == InteractionAction.PickUp);
            if (me.CurrentAction != InteractionAction.PickUp)
            {
                actor.CountTimeout("cook: no knife pick-up to race the take against (" + me.CurrentAction + ")");
                yield break;
            }
            if (capture.Frames != null) yield return capture.Frames("knife-pending-take", 1.2f);
            me.DevClick(); // the knife's predicted pick-up
            InteractionAction offered = InteractionRules.ActionFor(me.DevHands(), AimTarget.Crate(slot.IngredientId, 50));
            Debug.Log("[Demo] take while the knife flies in: the hands read '" + me.DevHands().Name + "', a stock slot offers " + offered);
            if (offered != InteractionAction.None) actor.CountTimeout("cook: a take was offered while the knife pick-up was pending");
            me.DevTake(slot); // the take as the old rules let it go out
            yield return new WaitForSecondsRealtime(1.5f);
            string held = me.FindHeldPickup() != null ? me.FindHeldPickup().name : "nothing";
            bool knifeHeld = me.FindHeldPickup() != null && me.FindHeldPickup().TryGetComponent(out Knife _);
            Debug.Log("[Demo] take while the knife flies in: the hands hold " + held + (knifeHeld ? " (the knife, no swap)" : " (SWAP)"));
            if (!knifeHeld) actor.CountTimeout("cook: the take swapped the knife's identity (" + held + ")");
            yield return capture.Shot("knife-pending-take-after");
            PlaceSurface prep = SurfaceUnder(knifeHome);
            if (prep != null && knifeHeld)
            {
                int spot = PlaceSpots.NearestFree(prep.Size, prep.ToTopPlane(knifeHome), null);
                yield return actor.Use("put the knife back", prep.SpotWorldPosition(spot), InteractionAction.Place,
                    () => me.DevSendPlace(prep.Index, spot));
            }
        }

        /// <summary>Spec 029: seconds between the shots while the basket fries, and the longest it waits for a zone.</summary>
        public const float FryShotEvery = 5f;
        public const float FryWait = 60f;
        public const float DrainFramesSeconds = 1.5f;

        /// <summary>
        /// Spec 029 AC5/AC6: fries from potato to the counter. A potato from the shelf onto the board, the knife cuts sticks (two
        /// strokes, a turn, a stroke), the knife goes back, the sticks go into basket 0, the fryer goes on, the basket goes down
        /// (frames of the arc), the ring fills (a shot every <see cref="FryShotEvery"/> s with the zone), one shake in the good
        /// window, the basket comes up at Perfect (frames of the arc and the drips); spec 029b: once drained the cook fetches a
        /// plate and tips the fries from the basket onto it (the fries box on the held plate), and the plate is served at the
        /// counter (the orders waiting are logged first).
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
                () => me.DevSendStation(fryer.Index, FryerRules.LowerOption(0)), capture.Before("basket-button-aim", "basket-lower", frames));
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
                () => me.DevSendStation(fryer.Index, FryerRules.RaiseOption(0)), capture.Before("raise-aim", "basket-raise", frames));
            yield return actor.AimAt(ContentPoint(basket), 0.2f);
            if (capture.Frames != null) yield return capture.Frames("drain-drips", DrainFramesSeconds);
            float drainStart = Time.unscaledTime;
            while (FryerRules.StateOf(basket.Mode.Value) != BasketState.Raised && Time.unscaledTime - drainStart < 5f) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return capture.Shot("drained");
            Debug.Log("[Demo] fries: the basket's label reads '" + string.Join(" + ", basket.Names) + "'");

            // Spec 029b: fetch a plate from the plating worktop, and tip the fries from the raised basket onto the held plate.
            yield return actor.LevelView();
            Debug.Log("[Demo] fries: fryer to the nearest plate " + TourActor.FlatDistance(fryer.transform.position, plate.transform.position).ToString("0.00") + " m");
            yield return actor.WalkTo(new Vector3(plate.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("pick up a plate", ContentPoint(plate), InteractionAction.PickUp, () => me.DevSendPickup(plateObject));
            yield return actor.LevelView();
            yield return actor.WalkTo(atFryer, false, false);
            yield return actor.Use("tip the fries onto the plate", ContentPoint(basket), InteractionAction.Take,
                () => me.DevSendUse(basketObject, VesselRules.ActionUse), capture.Before("tip-aim", "fries-tipped-onto-plate", frames));
            Debug.Log("[Demo] fries: the held plate holds '" + plate.Title.Value + "', the basket " + basket.ContentCount + " portions");
            if (basket.ContentCount != 0 || plate.ContentCount == 0) actor.CountTimeout("cook: the fries were not tipped onto the plate");
            yield return actor.Look(0f, -35f, 0.6f);
            yield return capture.Shot("fries-box-on-held-plate");
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
            yield return PayAtRegister(actor, capture, laneZ, "fries"); // spec 032
        }

        /// <summary>Spec 031: how long the tour holds the garlic-herb dispenser (the right amount is 0.5 to 1.5 s). Spec 032: the
        /// tour's guest asks for "extra garlic" (lots, 1.5 s or more), so it holds 1.8 s.</summary>
        public const float SqueezeSeconds = 1.8f;

        /// <summary>
        /// Spec 031 AC5: one falafel wrap from stock to the counter on the assembly board, layer by layer. A wrap from the dry shelf
        /// onto the board (frames of the drop); falafel mix from the fridge into basket 0 of the fryer (still on from the fries),
        /// down, up at Perfect, drained, taken by hand onto the wrap (frames); lettuce from the fridge cut into slices on the
        /// cutting board, onto the falafel (frames); the garlic-herb dispenser held for <see cref="SqueezeSeconds"/> (frames of the
        /// ribbon); the fold (frames), the wrap paper from the rack (frames); taken and served at the counter (the reward). A shot
        /// of the stack after every layer. The observer films the board close-up.
        /// </summary>
        private static IEnumerator BuildFalafelWrap(TourActor actor, CookCapture capture, float laneZ)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            AssemblySpot assemblySpot = UnityEngine.Object.FindFirstObjectByType<AssemblySpot>();
            Station board = assemblySpot != null ? assemblySpot.Board : null;
            Vessel spot = assemblySpot != null ? assemblySpot.GetComponent<Vessel>() : null;
            Station fryer = null, counter = null;
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Fryer) fryer = s;
                else if (s.Kind == StationKind.Counter) counter = s;
            }
            Vessel basket = null, cutting = null;
            foreach (FryerBasket fb in UnityEngine.Object.FindObjectsByType<FryerBasket>(FindObjectsSortMode.None))
                if (fb.Basket == 0 && fb.Fryer == fryer) basket = fb.GetComponent<Vessel>();
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
                if (v.Kind == VesselKind.Board) cutting = v;
            StationButton lower = null;
            foreach (StationButton b in UnityEngine.Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
                if (fryer != null && b.Station == fryer && b.Option == FryerRules.BasketOption(0)) lower = b;
            Knife knife = UnityEngine.Object.FindFirstObjectByType<Knife>();
            StockSlot wrapSlot = FindSlot("wrap"), falafelSlot = FindSlot("falafel_mix"), lettuceSlot = FindSlot("lettuce");
            StockSlot garlic = null, paper = null;
            if (board != null)
            {
                board.TryGetSlot(0, out garlic);
                board.TryGetSlot(BuildRules.DispenserCount + BuildRules.Paper, out paper);
            }
            if (spot == null || board == null || fryer == null || counter == null || basket == null || cutting == null || lower == null
                || knife == null || wrapSlot == null || falafelSlot == null || lettuceSlot == null || garlic == null || paper == null)
            {
                Debug.LogWarning("[Demo] wrap: the assembly board, its tools, the fryer, the board, the knife or a stock slot is missing");
                actor.CountTimeout("cook: assembly setup missing");
                yield break;
            }
            NetworkObject spotObject = spot.NetworkObject, basketObject = basket.NetworkObject, cuttingObject = cutting.NetworkObject;
            NetworkObject knifeObject = knife.GetComponent<NetworkObject>();
            Vector3 knifeHome = knife.transform.position;
            Vector3 atBoard = new Vector3(board.transform.position.x, 0f, laneZ);
            Vector3 atFryer = new Vector3(fryer.transform.position.x, 0f, laneZ);
            Debug.Log("[Demo] wrap: start (" + Time.time.ToString("0.0") + " s); plates to the board "
                + TourActor.FlatDistance(spot.transform.position, NearestPlate(board.transform.position)).ToString("0.00") + " m");
            yield return LookAtOrders(actor, capture, laneZ, "orders"); // spec 032: the ticket with "+ extra garlic", the rings

            // 1. The wrap: the base.
            yield return TakeFrom(actor, wrapSlot, laneZ);
            yield return actor.WalkTo(atBoard, false, false);
            yield return capture.Shot("assembly-board");
            yield return actor.Use("put the wrap on the board", ContentPoint(spot), InteractionAction.PutInto,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse), capture.Before("wrap-aim", "wrap-onto-board", frames));
            yield return StackShot(actor, capture, spot, "stack-1-wrap");

            // 2. The falafel, from the fryer.
            yield return TakeFrom(actor, falafelSlot, laneZ);
            yield return actor.WalkTo(atFryer, false, false);
            yield return actor.Use("put the falafel mix into the basket", ContentPoint(basket), InteractionAction.PutInto,
                () => me.DevSendUse(basketObject, VesselRules.ActionUse));
            yield return actor.Use("lower the basket", lower.transform.position, InteractionAction.Press,
                () => me.DevSendStation(fryer.Index, FryerRules.LowerOption(0)));
            float start = Time.unscaledTime;
            yield return actor.AimAt(ContentPoint(basket) + Vector3.up * WatchAbove, 0.8f);
            float nextShot = 0f;
            while (Time.unscaledTime - start < FryWait && basket.Zone != HeatZone.Perfect)
            {
                float at = Time.unscaledTime - start;
                if (at >= nextShot)
                {
                    LogBasket("falafel", at, basket);
                    yield return capture.Shot("falafel-" + Mathf.RoundToInt(at).ToString("00") + "s-" + basket.Zone.ToString().ToLowerInvariant());
                    nextShot += FryShotEvery * 2f;
                }
                yield return null;
            }
            if (basket.Zone != HeatZone.Perfect) actor.CountTimeout("cook: the falafel did not reach Perfect (" + basket.Zone + ")");
            yield return new WaitForSecondsRealtime(1f);
            yield return actor.Use("raise the basket", lower.transform.position, InteractionAction.Press,
                () => me.DevSendStation(fryer.Index, FryerRules.RaiseOption(0)));
            float drainStart = Time.unscaledTime;
            while (FryerRules.StateOf(basket.Mode.Value) != BasketState.Raised && Time.unscaledTime - drainStart < 5f) yield return null;
            yield return actor.Use("take the falafel", ContentPoint(basket), InteractionAction.Take, () => me.DevSendUse(basketObject, VesselRules.ActionUse));
            Debug.Log("[Demo] wrap: holding '" + HeldName(me) + "'");
            yield return actor.LevelView();
            yield return actor.WalkTo(atBoard, false, false);
            yield return actor.Use("put the falafel on the wrap", ContentPoint(spot), InteractionAction.PutInto,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse), capture.Before("falafel-aim", "falafel-onto-wrap", frames));
            yield return StackShot(actor, capture, spot, "stack-2-falafel");

            // 3. The lettuce, cut into slices on the cutting board.
            yield return TakeFrom(actor, lettuceSlot, laneZ);
            yield return actor.WalkTo(new Vector3(cutting.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("put the lettuce on the cutting board", ContentPoint(cutting), InteractionAction.PutInto,
                () => me.DevSendUse(cuttingObject, VesselRules.ActionUse));
            yield return actor.Use("pick up the knife", knife.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(knifeObject));
            yield return actor.AimAt(ContentPoint(cutting), 0.6f);
            foreach (int action in new[] { VesselRules.ActionStroke, VesselRules.ActionStroke })
            {
                me.DevSendUse(cuttingObject, action);
                yield return new WaitForSeconds(0.7f);
            }
            Debug.Log("[Demo] wrap: the cutting board holds " + cutting.Title.Value);
            PlaceSurface prep = SurfaceUnder(knifeHome);
            if (prep != null)
            {
                int knifeSpot = PlaceSpots.NearestFree(prep.Size, prep.ToTopPlane(knifeHome), null);
                yield return actor.Use("put the knife back", prep.SpotWorldPosition(knifeSpot), InteractionAction.Place,
                    () => me.DevSendPlace(prep.Index, knifeSpot));
            }
            yield return actor.Use("take the lettuce slices", ContentPoint(cutting), InteractionAction.Take,
                () => me.DevSendUse(cuttingObject, VesselRules.ActionUse));
            yield return actor.LevelView();
            yield return actor.WalkTo(atBoard, false, false);
            yield return actor.Use("put the lettuce on the falafel", ContentPoint(spot), InteractionAction.PutInto,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse), capture.Before("lettuce-aim", "lettuce-onto-wrap", frames));
            yield return StackShot(actor, capture, spot, "stack-3-lettuce");

            // 4. The garlic-herb sauce: hold the dispenser.
            yield return actor.AimAt(garlic.Anchor, 1.5f, () => me.CurrentAction == InteractionAction.Squeeze);
            yield return capture.Shot("sauce-aim");
            if (me.CurrentAction != InteractionAction.Squeeze) Debug.LogWarning("[Demo] wrap: the dispenser offers " + me.CurrentAction);
            Debug.Log("[Demo] wrap: press the garlic-herb dispenser: '" + InteractionRules.PromptFor(me.CurrentHeld, me.CurrentAim) + "'");
            me.DevSqueezePress(garlic);
            float pressed = Time.unscaledTime;
            if (capture.Frames != null) yield return capture.Frames("sauce-squeeze", SqueezeSeconds + 0.4f);
            while (Time.unscaledTime - pressed < SqueezeSeconds) yield return null;
            me.DevSqueezeRelease();
            float released = Time.unscaledTime;
            while ((me.DevSqueezeBusy || spot.ContentCount < 4) && Time.unscaledTime - released < 3f) yield return null;
            Debug.Log("[Demo] wrap: held the dispenser " + (released - pressed).ToString("0.00") + " s; the build is '" + spot.Title.Value + "'");
            if (spot.ContentCount < 4) actor.CountTimeout("cook: no sauce layer on the build");
            yield return StackShot(actor, capture, spot, "stack-4-sauce");
            // Spec 031b item 9: the squeeze's frames end before the fold's frames start (separate captures).
            yield return capture.WaitForFrames();

            // 5. Fold, then the wrap paper.
            yield return actor.Use("fold the wrap", ContentPoint(spot), InteractionAction.Fold,
                () => me.DevSendUse(spotObject, VesselRules.ActionFold), capture.Before("fold-aim", "fold", frames));
            yield return StackShot(actor, capture, spot, "folded");
            yield return actor.Use("wrap it in paper", paper.Anchor, InteractionAction.Pack,
                () => me.DevSendStation(board.Index, BuildRules.PackagingOption(BuildRules.Paper)), capture.Before("paper-aim", "paper", frames));
            yield return StackShot(actor, capture, spot, "wrapped");
            Debug.Log("[Demo] wrap: the board holds '" + spot.Title.Value + "', mode " + spot.Mode.Value);

            // 6. Take it and serve it.
            yield return actor.Use("take the falafel wrap", ContentPoint(spot), InteractionAction.Take,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse), capture.Before("take-wrap-aim", null, 0f));
            yield return actor.Look(0f, -35f, 0.6f);
            yield return capture.Shot("wrap-in-hand");
            yield return actor.LevelView();
            // Spec 031b item 4: the held wrap stands whole in the lower right of the level view.
            yield return capture.Shot("wrap-held-level");
            LogHold(me, "wrap");
            yield return actor.WalkTo(new Vector3(counter.transform.position.x, 0f, laneZ), false, false);
            LogOrders();
            Vector3 counterTop = counter.transform.position + Vector3.up * (Worktown.Game.Art.ArtSizeTable.ServingCounterHeight * 0.5f - 0.02f);
            yield return actor.Use("serve the falafel wrap at the counter", counterTop, InteractionAction.Sell,
                () => me.DevSendStation(counter.Index, 0), capture.Before("wrap-serve-aim", "wrap-served", RewardFramesSeconds));
            ServeFeed feed = UnityEngine.Object.FindFirstObjectByType<ServeFeed>();
            yield return new WaitForSecondsRealtime(0.5f);
            string latest = feed != null && feed.Entries.Count > 0 ? feed.Entries[0] : "?";
            Debug.Log("[Demo] wrap: served (" + Time.time.ToString("0.0") + " s); feed: " + latest);
            if (!latest.Contains("falafel wrap")) actor.CountTimeout("cook: the falafel wrap was not sold");
            if (!latest.Contains("Falafel wrap order: exact order")) Debug.LogWarning("[Demo] wrap: no exact Falafel wrap order was served");
            // Spec 033: the guest ordered a lemonade with it: fill it to the line, lid it and serve it; then the guest takes both.
            yield return PourLemonade(actor, capture, laneZ, counter);
            // Spec 032: the guest takes its order (frames of the hand-off), then pays cash with change.
            if (capture.Frames != null) yield return capture.Frames("wrap-handoff", 0.8f);
            yield return PayAtRegister(actor, capture, laneZ, "wrap");

            // 7. Spec 031b item 5: box packing: a lettuce on the empty board (no bread: a box needs no fold), into a box, served.
            yield return PackABox(actor, capture, laneZ, board, spot, lettuceSlot);
        }

        /// <summary>Spec 033: how long the tour holds the lemonade tap (s): 360 ml at 200 ml/s, the middle of the fill line.</summary>
        public const float PourSeconds = 1.8f;

        /// <summary>
        /// Spec 033 AC4: the wrap guest's lemonade. A cup from the stack (frames of it flying into the hand), the lemonade tap held for
        /// <see cref="PourSeconds"/> (frames of the cup under the tap, the stream and the level rising to the line), the lid from the lid
        /// stack (frames), a shot of the lidded cup in the hand, then served at the counter (frames of the reward). The observer sees the
        /// stream from the guests' side.
        /// </summary>
        private static IEnumerator PourLemonade(TourActor actor, CookCapture capture, float laneZ, Station counter)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            Station drinks = null;
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Kind == StationKind.DrinkDispenser) drinks = s;
            StockSlot cups = null, lemonade = null, lids = null;
            if (drinks != null)
            {
                drinks.TryGetSlot(DrinkRules.CupTool, out cups);
                drinks.TryGetSlot(0, out lemonade);
                drinks.TryGetSlot(DrinkRules.LidTool, out lids);
            }
            if (drinks == null || cups == null || lemonade == null || lids == null)
            {
                Debug.LogWarning("[Demo] drinks: the dispenser or its tools are missing");
                actor.CountTimeout("cook: drink dispenser missing");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(drinks.transform.position.x, 0f, laneZ), false, false);
            yield return capture.Shot("drinks-dispenser");
            yield return actor.Use("take a cup", cups.Anchor, InteractionAction.Take, () => me.DevTakeCup(drinks),
                capture.Before("cup-aim", "cup-take", frames));
            float until = Time.unscaledTime + 3f;
            while (HeldCup(me) == null && Time.unscaledTime < until) yield return null;
            if (HeldCup(me) == null) actor.CountTimeout("cook: no cup in the hands");
            yield return actor.LevelView();
            yield return capture.Shot("cup-in-hand");

            // The tap: hold it, the cup goes under it and the lemonade rises to the line.
            yield return actor.AimAt(lemonade.Anchor, 1.5f, () => me.CurrentAction == InteractionAction.Fill);
            yield return capture.Shot("lemonade-aim");
            if (me.CurrentAction != InteractionAction.Fill) Debug.LogWarning("[Demo] drinks: the tap offers " + me.CurrentAction);
            Debug.Log("[Demo] drinks: press the lemonade tap: '" + InteractionRules.PromptFor(me.CurrentHeld, me.CurrentAim) + "'");
            me.DevSqueezePress(lemonade);
            float pressed = Time.unscaledTime;
            var tap = lemonade.GetComponent<Worktown.Game.Art.DrinkTapView>();
            if (capture.Frames != null) yield return capture.Frames("lemonade-pour", PourSeconds + 0.4f);
            while (Time.unscaledTime - pressed < PourSeconds) yield return null;
            Food cup = HeldCup(me);
            float drawn = cup != null && cup.Visual != null ? cup.Visual.DrawnFill : -1f;
            me.DevSqueezeRelease();
            float released = Time.unscaledTime;
            while ((me.DevSqueezeBusy || (cup != null && cup.Look.Value.Shape == DrinkRules.LevelEmpty)) && Time.unscaledTime - released < 3f)
                yield return null;
            Debug.Log("[Demo] drinks: held the tap " + (released - pressed).ToString("0.00") + " s (drawn level " + drawn.ToString("0.00")
                + ", the tap drew its stream from frame " + (tap != null ? tap.PredictionDrawnFrame : -1) + "); the cup is '" + HeldName(me)
                + "', level " + (cup != null ? cup.Look.Value.Shape : 0));
            if (cup == null || cup.Look.Value.Shape != DrinkRules.LevelRight) actor.CountTimeout("cook: the lemonade is not at the line");
            yield return capture.WaitForFrames();
            yield return actor.Look(0f, -30f, 0.6f);
            yield return capture.Shot("lemonade-filled");
            yield return actor.LevelView();

            // The lid.
            yield return actor.Use("put a lid on it", lids.Anchor, InteractionAction.Lid, () => me.DevLid(drinks), capture.Before("lid-aim", "lid", frames));
            until = Time.unscaledTime + 3f;
            while ((cup == null || cup.Look.Value.Kind != FoodLook.KindLidded) && Time.unscaledTime < until) yield return null;
            if (cup == null || cup.Look.Value.Kind != FoodLook.KindLidded) actor.CountTimeout("cook: no lid on the lemonade");
            yield return actor.LevelView();
            yield return capture.Shot("lemonade-held-level");
            LogHold(me, "lemonade");

            // Served with the wrap: the guest has both and goes to pay.
            yield return actor.WalkTo(new Vector3(counter.transform.position.x, 0f, laneZ), false, false);
            Vector3 counterTop = counter.transform.position + Vector3.up * (Worktown.Game.Art.ArtSizeTable.ServingCounterHeight * 0.5f - 0.02f);
            yield return actor.Use("serve the lemonade at the counter", counterTop, InteractionAction.Sell,
                () => me.DevSendStation(counter.Index, 0), capture.Before("lemonade-serve-aim", "lemonade-served", RewardFramesSeconds));
            ServeFeed feed = UnityEngine.Object.FindFirstObjectByType<ServeFeed>();
            yield return new WaitForSecondsRealtime(0.5f);
            string latest = feed != null && feed.Entries.Count > 0 ? feed.Entries[0] : "?";
            Debug.Log("[Demo] drinks: served (" + Time.time.ToString("0.0") + " s); feed: " + latest);
            if (!latest.Contains("lemonade")) actor.CountTimeout("cook: the lemonade was not served");
            if (!latest.Contains("drink order: exact order")) Debug.LogWarning("[Demo] drinks: no exact lemonade order was served");
        }

        /// <summary>Spec 033: the cup the player holds, or null.</summary>
        private static Food HeldCup(PickupInteractor me)
        {
            Pickup held = me.FindHeldPickup();
            return held != null && held.TryGetComponent(out Food food) && FoodLookRules.IsCup(food.Look.Value) ? food : null;
        }

        /// <summary>
        /// Spec 031b item 5 (AC3): the box from the rack coming down over a build. A whole lettuce goes on the empty board (an open
        /// build without bread, which a box takes as it is), the box goes on (frames), the boxed build is taken (held in view) and
        /// put back on the empty board.
        /// </summary>
        private static IEnumerator PackABox(TourActor actor, CookCapture capture, float laneZ, Station board, Vessel spot,
            StockSlot lettuceSlot)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            if (!board.TryGetSlot(BuildRules.DispenserCount + BuildRules.Box, out StockSlot box) || box == null)
            {
                actor.CountTimeout("cook: the box rack slot is missing");
                yield break;
            }
            NetworkObject spotObject = spot.NetworkObject;
            Vector3 atBoard = new Vector3(board.transform.position.x, 0f, laneZ);
            Debug.Log("[Demo] box: start (" + Time.time.ToString("0.0") + " s)");
            yield return TakeFrom(actor, lettuceSlot, laneZ);
            yield return actor.LevelView();
            yield return actor.WalkTo(atBoard, false, false);
            yield return actor.Use("put the lettuce on the empty board", ContentPoint(spot), InteractionAction.PutInto,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse));
            yield return StackShot(actor, capture, spot, "box-stack");
            yield return capture.WaitForFrames();
            yield return actor.Use("put it into a box", box.Anchor, InteractionAction.Pack,
                () => me.DevSendStation(board.Index, BuildRules.PackagingOption(BuildRules.Box)), capture.Before("box-aim", "box", frames));
            yield return capture.WaitForFrames();
            yield return StackShot(actor, capture, spot, "boxed");
            Debug.Log("[Demo] box: the board holds '" + spot.Title.Value + "', mode " + spot.Mode.Value);
            if (BuildRules.PackOf(spot.Mode.Value) != BuildRules.PackBox) actor.CountTimeout("cook: the box did not go on");
            yield return actor.Use("take the box", ContentPoint(spot), InteractionAction.Take,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse));
            yield return actor.LevelView();
            yield return capture.Shot("box-held-level");
            LogHold(me, "box");
            // A boxed raw lettuce is no dish (the counter would not buy it): it goes back on the empty board (031b: a build, even a
            // finished one, goes back only on an empty board) and stays there.
            yield return actor.Use("put the box back on the empty board", ContentPoint(spot), InteractionAction.PutInto,
                () => me.DevSendUse(spotObject, VesselRules.ActionUse));
            yield return StackShot(actor, capture, spot, "box-back-on-board");
            Debug.Log("[Demo] box: back on the board (" + Time.time.ToString("0.0") + " s), holding '" + HeldName(me) + "'");
            if (spot.ContentCount == 0) actor.CountTimeout("cook: the box did not go back on the board");
        }

        /// <summary>Spec 031: a look down at the stack, a shot, and the stack's state in the log.</summary>
        private static IEnumerator StackShot(TourActor actor, CookCapture capture, Vessel spot, string name)
        {
            yield return actor.AimAt(ContentPoint(spot), 0.5f);
            var view = spot.GetComponent<Worktown.Game.Art.BuildStackView>();
            Debug.Log("[Build] " + name + ": " + spot.ContentCount + " layers, drawn " + (view != null ? view.ShownLayers : -1) + ", mode "
                + spot.Mode.Value + ", label '" + string.Join(" + ", spot.Names) + "'");
            yield return capture.Shot(name);
        }

        private static Vector3 NearestPlate(Vector3 from)
        {
            Vector3 best = from + Vector3.right * 99f;
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
                if (v.Kind == VesselKind.Plate && TourActor.FlatDistance(v.transform.position, from) < TourActor.FlatDistance(best, from))
                    best = v.transform.position;
            return best;
        }

        /// <summary>Spec 030: seconds between the shots while the griddle cooks, and the longest the griddle part waits.</summary>
        public const float GrillShotEvery = 3f;
        public const float GrillWait = 60f;

        /// <summary>
        /// Spec 030 AC5/AC6: "Grilled veggies" from stock to the counter, with seitan strips. A plate onto the griddle's deck; seitan
        /// from the fridge cut into sticks on the board (the griddle calls them strips) and onto spot 0, a whole paprika onto spot
        /// 1 and a whole mushroom onto spot 2 while the griddle is still off; the knob on; the spatula from the deck. Each item is
        /// flipped with the spatula when its down side reaches Perfect (frames of the seitan's flip, a shot of its grill marks);
        /// then the spatula goes back on the deck, the plate comes up, and each item slides onto the held plate when its second
        /// side reaches Perfect (frames of the seitan's slide). The plate is served at the counter (the orders are logged).
        /// </summary>
        private static IEnumerator GrillVeggies(TourActor actor, CookCapture capture, float laneZ)
        {
            const float frames = TourRules.ClickFramesSeconds;
            PickupInteractor me = actor.Interactor;
            Station griddle = null, counter = null;
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Griddle) griddle = s;
                else if (s.Kind == StationKind.Counter) counter = s;
            }
            var spots = new Vessel[GriddleRules.SpotsPerGriddle];
            foreach (GriddleSpot gs in UnityEngine.Object.FindObjectsByType<GriddleSpot>(FindObjectsSortMode.None))
                if (gs.Griddle == griddle && gs.Spot >= 0 && gs.Spot < spots.Length) spots[gs.Spot] = gs.GetComponent<Vessel>();
            StationButton knob = null;
            foreach (StationButton b in UnityEngine.Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
                if (griddle != null && b.Station == griddle && b.Option == GriddleRules.PowerOption) knob = b;
            Spatula spatula = UnityEngine.Object.FindFirstObjectByType<Spatula>();
            Knife knife = UnityEngine.Object.FindFirstObjectByType<Knife>();
            Vessel board = null, plate = null;
            foreach (Vessel v in UnityEngine.Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind == VesselKind.Board) board = v;
                else if (v.Kind == VesselKind.Plate && griddle != null && v.ContentCount == 0
                         && (plate == null || Distance(v, griddle) < Distance(plate, griddle))) plate = v;
            }
            StockSlot seitanSlot = FindSlot("seitan"), paprikaSlot = FindSlot("paprika"), mushroomSlot = FindSlot("mushroom");
            if (griddle == null || counter == null || knob == null || spatula == null || knife == null || board == null || plate == null
                || seitanSlot == null || paprikaSlot == null || mushroomSlot == null || spots[0] == null || spots[1] == null || spots[2] == null)
            {
                Debug.LogWarning("[Demo] griddle: the griddle, its knob, spots, the spatula, knife, board, a plate, the counter or a slot is missing");
                actor.CountTimeout("cook: griddle setup missing");
                yield break;
            }
            PlaceSurface deck = SurfaceUnder(spatula.transform.position);
            if (deck == null)
            {
                actor.CountTimeout("cook: the spatula lies on no surface");
                yield break;
            }
            Vector3 atGriddle = new Vector3(griddle.transform.position.x, 0f, laneZ);
            Vector3 knifeHome = knife.transform.position;
            NetworkObject boardObject = board.NetworkObject, plateObject = plate.NetworkObject;
            NetworkObject knifeObject = knife.GetComponent<NetworkObject>(), spatulaObject = spatula.GetComponent<NetworkObject>();
            Debug.Log("[Demo] griddle: start (" + Time.time.ToString("0.0") + " s)");

            // A plate onto the griddle's deck, so it is at hand when the food is done.
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(plate.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("pick up a plate for the griddle", ContentPoint(plate), InteractionAction.PickUp, () => me.DevSendPickup(plateObject));
            yield return actor.LevelView();
            yield return actor.WalkTo(atGriddle, false, false);
            int plateSpot = PlaceSpots.NearestFree(deck.Size, new Vector2(-0.375f, -0.125f), null);
            yield return actor.Use("put the plate on the griddle's deck", deck.SpotWorldPosition(plateSpot), InteractionAction.Place,
                () => me.DevSendPlace(deck.Index, plateSpot));
            yield return capture.Shot("griddle");

            // A whole paprika and a whole mushroom from the dry shelf onto the deck (staged; they go on the griddle after the seitan).
            var staged = new List<Vector3>();
            foreach (StockSlot slot in new[] { paprikaSlot, mushroomSlot })
            {
                yield return TakeFrom(actor, slot, laneZ);
                yield return actor.WalkTo(atGriddle, false, false);
                int deckSpot = PlaceSpots.NearestFree(deck.Size, new Vector2(-0.125f + 0.25f * staged.Count, -0.125f), null);
                staged.Add(deck.SpotWorldPosition(deckSpot));
                yield return actor.Use("put the " + slot.IngredientId + " on the griddle's deck", deck.SpotWorldPosition(deckSpot), InteractionAction.Place,
                    () => me.DevSendPlace(deck.Index, deckSpot));
            }

            // Seitan strips: the fridge, the board, the knife (two strokes, a turn, a stroke), the knife back.
            yield return TakeFrom(actor, seitanSlot, laneZ);
            yield return actor.WalkTo(new Vector3(board.transform.position.x, 0f, laneZ), false, false);
            yield return actor.Use("put the seitan on the board", ContentPoint(board), InteractionAction.PutInto,
                () => me.DevSendUse(boardObject, VesselRules.ActionUse));
            yield return actor.Use("pick up the knife", knife.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(knifeObject));
            yield return actor.AimAt(ContentPoint(board), 0.6f);
            foreach (int action in new[] { VesselRules.ActionStroke, VesselRules.ActionStroke, VesselRules.ActionTurn, VesselRules.ActionStroke })
            {
                me.DevSendUse(boardObject, action);
                yield return new WaitForSeconds(0.7f);
            }
            Debug.Log("[Demo] griddle: the board holds " + board.Title.Value);
            if (!board.Title.Value.Contains("seitan")) actor.CountTimeout("cook: the board holds no seitan (" + board.Title.Value + ")");
            PlaceSurface prep = SurfaceUnder(knifeHome);
            if (prep != null)
            {
                int spot = PlaceSpots.NearestFree(prep.Size, prep.ToTopPlane(knifeHome), null);
                yield return actor.Use("put the knife back", prep.SpotWorldPosition(spot), InteractionAction.Place, () => me.DevSendPlace(prep.Index, spot));
            }
            yield return actor.Use("take the seitan sticks", ContentPoint(board), InteractionAction.Take, () => me.DevSendUse(boardObject, VesselRules.ActionUse));
            yield return actor.LevelView();
            yield return actor.WalkTo(atGriddle, false, false);

            // The griddle on, the seitan strips on spot 0, then the staged paprika and mushroom on spots 1 and 2 (a few seconds
            // apart, so their flips and takes do not all fall on the same moment).
            yield return actor.Use("switch the griddle on", knob.transform.position, InteractionAction.Press,
                () => me.DevSendStation(griddle.Index, GriddleRules.PowerOption), capture.Before("griddle-knob-aim", null, 0f));
            NetworkObject spot0 = spots[0].NetworkObject;
            yield return actor.Use("put the seitan strips on the griddle", ContentPoint(spots[0]), InteractionAction.PutInto,
                () => me.DevSendUse(spot0, VesselRules.ActionUse), capture.Before("griddle-put-aim", null, 0f));
            float start = Time.unscaledTime;
            Debug.Log("[Demo] griddle: on, the seitan strips on spot 0 (" + Time.time.ToString("0.0") + " s)");
            for (int i = 0; i < staged.Count; i++)
            {
                Food food = NearestFood(staged[i]);
                if (food == null)
                {
                    actor.CountTimeout("cook: a staged vegetable is gone from the deck");
                    continue;
                }
                NetworkObject foodObject = food.NetworkObject, target = spots[i + 1].NetworkObject;
                yield return actor.Use("pick up the " + food.DisplayName.Value + " from the deck", food.transform.position, InteractionAction.PickUp,
                    () => me.DevSendPickup(foodObject));
                yield return actor.Use("put it on the griddle", ContentPoint(spots[i + 1]), InteractionAction.PutInto,
                    () => me.DevSendUse(target, VesselRules.ActionUse));
            }
            yield return actor.Use("pick up the spatula", spatula.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(spatulaObject),
                capture.Before("spatula-aim", null, 0f));
            Vector3 watch = (ContentPoint(spots[0]) + ContentPoint(spots[2])) * 0.5f + Vector3.up * WatchAbove;
            yield return actor.AimAt(watch, 0.4f);

            // Flip each item once its down side is Perfect (a late one at once); a shot every GrillShotEvery s.
            var flipped = new bool[3];
            var taken = new bool[3];
            float nextShot = 0f;
            while (Time.unscaledTime - start < GrillWait && !(flipped[0] && flipped[1] && flipped[2]))
            {
                for (int i = 0; i < 3; i++)
                {
                    if (flipped[i] || spots[i].ContentCount == 0 || spots[i].Zone < HeatZone.Perfect) continue;
                    flipped[i] = true;
                    LogGrill("flip " + i, Time.unscaledTime - start, spots);
                    NetworkObject target = spots[i].NetworkObject;
                    yield return actor.Use("flip " + string.Join(" + ", spots[i].Names), ContentPoint(spots[i]), InteractionAction.Flip,
                        () => me.DevSendUse(target, VesselRules.ActionFlip), i == 0 ? capture.Before("seitan-flip-aim", "seitan-flip", frames) : null);
                    yield return actor.AimAt(watch, 0.2f);
                }
                float at = Time.unscaledTime - start;
                if (at >= nextShot)
                {
                    LogGrill("shot", at, spots);
                    yield return capture.Shot("grill-" + Mathf.RoundToInt(at).ToString("00") + "s-" + spots[0].Zone.ToString().ToLowerInvariant());
                    nextShot += GrillShotEvery;
                }
                yield return null;
            }
            if (!(flipped[0] && flipped[1] && flipped[2])) actor.CountTimeout("cook: not every item was flipped at Perfect");
            // The cooked side up: the grill marks on the seitan.
            yield return actor.AimAt(ContentPoint(spots[0]) + Vector3.up * 0.12f, 0.5f);
            LogGrill("marks", Time.unscaledTime - start, spots);
            yield return capture.Shot("grill-marks-seitan");

            // The spatula back on the deck, the plate up; each item slides onto the plate when its second side is Perfect.
            int spatulaSpot = PlaceSpots.NearestFree(deck.Size, new Vector2(0.375f, 0.125f), null);
            yield return actor.Use("put the spatula back", deck.SpotWorldPosition(spatulaSpot), InteractionAction.Place,
                () => me.DevSendPlace(deck.Index, spatulaSpot));
            yield return actor.Use("pick up the plate from the deck", ContentPoint(plate), InteractionAction.PickUp, () => me.DevSendPickup(plateObject));
            yield return actor.AimAt(watch, 0.3f);
            while (Time.unscaledTime - start < GrillWait && !(taken[0] && taken[1] && taken[2]))
            {
                for (int i = 0; i < 3; i++)
                {
                    if (taken[i] || spots[i].ContentCount == 0 || spots[i].Zone < HeatZone.Perfect) continue;
                    taken[i] = true;
                    LogGrill("slide " + i, Time.unscaledTime - start, spots);
                    NetworkObject target = spots[i].NetworkObject;
                    yield return actor.Use("slide " + spots[i].Title.Value + " onto the plate", ContentPoint(spots[i]), InteractionAction.Take,
                        () => me.DevSendUse(target, VesselRules.ActionUse), i == 0 ? capture.Before("seitan-slide-aim", "seitan-onto-plate", frames) : null);
                    yield return actor.AimAt(watch, 0.2f);
                }
                float at = Time.unscaledTime - start;
                if (at >= nextShot)
                {
                    LogGrill("shot", at, spots);
                    yield return capture.Shot("grill-" + Mathf.RoundToInt(at).ToString("00") + "s-" + spots[0].Zone.ToString().ToLowerInvariant());
                    nextShot += GrillShotEvery;
                }
                yield return null;
            }
            Debug.Log("[Demo] griddle: the held plate holds '" + plate.Title.Value + "' (" + plate.ContentCount + " parts)");
            if (plate.ContentCount != 3) actor.CountTimeout("cook: the grilled food did not all slide onto the plate");
            yield return actor.Look(0f, -35f, 0.6f);
            yield return capture.Shot("grilled-veggies-on-held-plate");

            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(counter.transform.position.x, 0f, laneZ), false, false);
            LogOrders();
            Vector3 counterTop = counter.transform.position + Vector3.up * (Worktown.Game.Art.ArtSizeTable.ServingCounterHeight * 0.5f - 0.02f);
            yield return actor.Use("serve the grilled veggies at the counter", counterTop, InteractionAction.Sell,
                () => me.DevSendStation(counter.Index, 0), capture.Before("veggies-serve-aim", "veggies-served", RewardFramesSeconds));
            ServeFeed feed = UnityEngine.Object.FindFirstObjectByType<ServeFeed>();
            yield return new WaitForSecondsRealtime(0.5f);
            string latest = feed != null && feed.Entries.Count > 0 ? feed.Entries[0] : "?";
            Debug.Log("[Demo] griddle: served (" + Time.time.ToString("0.0") + " s); feed: " + latest);
            if (!latest.Contains("Grilled veggies order: exact order")) Debug.LogWarning("[Demo] griddle: no exact Grilled veggies order was served");
            yield return PayAtRegister(actor, capture, laneZ, "veggies"); // spec 032
            yield return LookAtOrders(actor, capture, laneZ, "orders-later"); // spec 032: the rings have drained since
        }

        // ---- Spec 034: a slot runs empty, the order at the terminal, the delivery at the back door, the unpack ----

        /// <summary>The longest the tour waits for the delivery's boxes after the fries (s; the delay is one game hour, about 60 s).</summary>
        public const float DeliveryWait = 100f;

        private static Station FindStation(StationKind kind)
        {
            foreach (Station s in UnityEngine.Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Kind == kind) return s;
            return null;
        }

        private static string StockText(StockSlot slot)
        {
            var counts = UnityEngine.Object.FindFirstObjectByType<Worktown.Game.Shop.StockCounts>();
            int line = Worktown.Game.Shop.StockCatalog.LineOfSlot(slot.Station.Kind, slot.Option);
            return counts != null && counts.TryGet(line, out byte n) ? n + "/" + Worktown.Game.Shop.StockCatalog.PresentedCapacity(line) : "?";
        }

        /// <summary>
        /// Spec 034: the last tomato is taken (the slot's last sample flies into the hand, the tag turns "empty"), the empty slot is
        /// aimed at ("Out of stock"), the tomato is put down on the landing worktop's back corner, and at the terminal the page buttons
        /// are tried and one pack of tomatoes is ordered (the row lights, the feed says when it is due).
        /// </summary>
        private static IEnumerator RunOutAndOrder(TourActor actor, CookCapture capture, float laneZ)
        {
            PickupInteractor me = actor.Interactor;
            StockSlot tomato = FindSlot(Worktown.Game.Shop.SupplyService.TourLine);
            Station terminal = FindStation(StationKind.Terminal);
            PlaceSurface landing = SurfaceUnder(new Vector3(1.6f, 0.9f, 14.5f));
            if (tomato == null || terminal == null || landing == null)
            {
                actor.CountTimeout("stock: the tomato slot, the terminal or the landing worktop missing");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            // From the left of the slot: the fridge's open right door hides its right column from the right.
            yield return actor.WalkTo(new Vector3(tomato.Anchor.x - 0.5f, 0f, laneZ), false, false);
            yield return actor.AimAt(tomato.Anchor, 1f);
            Debug.Log("[Stock] the tomato slot shows " + StockText(tomato) + " before the last take");
            yield return capture.Shot("stock-last-tomato");
            yield return actor.Use("take the last tomato", tomato.Anchor, InteractionAction.Buy, () => me.DevTake(tomato),
                capture.Before(null, "stock-last-take", TourRules.ClickFramesSeconds));
            yield return new WaitForSecondsRealtime(0.4f);
            Debug.Log("[Stock] the tomato slot shows " + StockText(tomato) + " after the take, hands '" + HeldName(me) + "'");
            yield return actor.AimAt(tomato.Anchor, 1f);
            Debug.Log("[Stock] aimed at the empty slot: '" + InteractionRules.PromptFor(me.CurrentHeld, me.CurrentAim) + "'");
            yield return capture.Shot("stock-empty-slot");
            yield return actor.AimAt(tomato.Anchor + new Vector3(-0.35f, -0.35f, 0f), 1f); // the slot off the prompt: no samples, "tomato · empty"
            yield return capture.Shot("stock-empty-slot-view");

            // Put the tomato down at the landing worktop's back corner (away from the delivery spots at its front).
            int spot = 0;
            float best = float.MaxValue;
            for (int s = 0; s < landing.SpotCount; s++)
            {
                float d = Vector3.Distance(landing.SpotWorldPosition(s), new Vector3(1.62f, 0.9f, 14.7f));
                if (d < best) { best = d; spot = s; }
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(landing.SpotWorldPosition(spot).x + 0.3f, 0f, laneZ), false, false);
            yield return actor.Use("put the tomato down on the landing worktop", landing.SpotWorldPosition(spot), InteractionAction.Place,
                () => me.DevSendPlace(landing.Index, spot));

            // The terminal: page on and back (the client's own page), then order one pack of tomatoes.
            int line = Worktown.Game.Shop.StockCatalog.IndexOf(Worktown.Game.Shop.SupplyService.TourLine);
            int row = line % Worktown.Game.Shop.TerminalRules.Rows;
            if (!terminal.TryGetSlot(row, out StockSlot tomatoRow) || !terminal.TryGetSlot(Worktown.Game.Shop.TerminalRules.NextSlot, out StockSlot next)
                || !terminal.TryGetSlot(Worktown.Game.Shop.TerminalRules.PrevSlot, out StockSlot back))
            {
                actor.CountTimeout("stock: the terminal's slots missing");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(terminal.transform.position.x + 0.2f, 0f, laneZ + 0.25f), false, false);
            yield return actor.AimAt(terminal.transform.position, 1f);
            yield return capture.Shot("terminal");
            yield return actor.Use("next page", next.Anchor, InteractionAction.Page, () => me.DevTerminal(next),
                capture.Before("terminal-next-aim", "terminal-page", TourRules.ClickFramesSeconds));
            yield return actor.Use("page back", back.Anchor, InteractionAction.Page, () => me.DevTerminal(back));
            long before = me.TryGetComponent(out Worktown.Game.Economy.Wallet wallet) ? wallet.Balance.Value : -1;
            yield return actor.Use("order a pack of tomatoes", tomatoRow.Anchor, InteractionAction.Order, () => me.DevTerminal(tomatoRow),
                capture.Before("terminal-order-aim", "terminal-order", TourRules.ClickFramesSeconds));
            yield return new WaitForSecondsRealtime(0.5f);
            long after = wallet != null ? wallet.Balance.Value : -1;
            Debug.Log("[Stock] ordered at the terminal: the wallet went from " + before + " to " + after + " (" + (before - after) + " paid)");
            yield return capture.Shot("terminal-ordered");
            yield return actor.LevelView();
        }

        /// <summary>
        /// Spec 034: after the fries, the delivery's box waits on the worktop by the back door (it came with a knock about a game hour
        /// after the order): it is picked up (carried low in front, with both hands), carried to the fridge's tomato slot and unpacked
        /// (the box tips into the slot, the samples pop back, the tag reads "tomato 6/12").
        /// </summary>
        private static IEnumerator UnpackTheDelivery(TourActor actor, CookCapture capture, float laneZ)
        {
            PickupInteractor me = actor.Interactor;
            StockSlot tomato = FindSlot(Worktown.Game.Shop.SupplyService.TourLine);
            if (tomato == null) yield break;
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(1.3f, 0f, laneZ), false, false);
            Worktown.Game.Shop.SupplyBox box = null;
            float until = Time.unscaledTime + DeliveryWait;
            while (box == null && Time.unscaledTime < until)
            {
                foreach (Worktown.Game.Shop.SupplyBox b in UnityEngine.Object.FindObjectsByType<Worktown.Game.Shop.SupplyBox>(FindObjectsSortMode.None))
                    if (b.IsSpawned && b.TryGetContents(out _)) box = b;
                if (box == null) yield return new WaitForSecondsRealtime(0.5f);
            }
            if (box == null)
            {
                actor.CountTimeout("stock: no delivery box within " + DeliveryWait + " s");
                yield break;
            }
            box.TryGetContents(out Worktown.Game.Shop.BoxContents contents);
            Debug.Log("[Stock] the delivery is at the back door: a box of " + Worktown.Game.Shop.StockCatalog.Describe(contents.Line, contents.Units)
                + " at " + box.transform.position.ToString("0.00") + " (" + Time.time.ToString("0.0") + " s)");
            yield return actor.AimAt(box.transform.position, 1f);
            yield return capture.Shot("delivery-boxes");
            NetworkObject boxObject = box.NetworkObject;
            yield return actor.Use("pick up the delivery box", box.transform.position, InteractionAction.PickUp, () => me.DevSendPickup(boxObject),
                capture.Before("delivery-box-aim", "delivery-box-pickup", TourRules.ClickFramesSeconds));
            yield return actor.LevelView();
            LogHold(me, "delivery box");
            yield return capture.Shot("delivery-box-carried");
            yield return actor.WalkTo(new Vector3(tomato.Anchor.x - 0.5f, 0f, laneZ), false, false);
            yield return actor.AimAt(tomato.Anchor, 1f, () => me.CurrentAction == InteractionAction.Unpack);
            Debug.Log("[Stock] aimed at the slot with the box: '" + InteractionRules.PromptFor(me.CurrentHeld, me.CurrentAim) + "'");
            yield return actor.Use("unpack the tomatoes", tomato.Anchor, InteractionAction.Unpack, () => me.DevUnpack(tomato),
                capture.Before("unpack-aim", "unpack", TourRules.ClickFramesSeconds));
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log("[Stock] the tomato slot shows " + StockText(tomato) + " after the unpack, hands '"
                + (me.IsHoldingSomething() ? "something" : "empty") + "'");
            yield return capture.Shot("tomato-restocked");
            yield return actor.AimAt(tomato.Anchor + new Vector3(-0.35f, -0.35f, 0f), 1f); // the slot off the prompt: the samples back, "tomato 6/12"
            yield return capture.Shot("tomato-restocked-view");
            yield return actor.LevelView();
        }

        /// <summary>
        /// Spec 030: takes one item from exactly <paramref name="slot"/> (the fridge's seitan sits right next to the falafel mix, so
        /// the aim alone may pick the neighbour): walks there, aims at it, and sends that slot's take with its predicted look.
        /// </summary>
        private static IEnumerator TakeFrom(TourActor actor, StockSlot slot, float laneZ)
        {
            PickupInteractor me = actor.Interactor;
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(actor.Position.x, 0f, laneZ), false, false);
            yield return actor.WalkTo(new Vector3(slot.Anchor.x + 1f, 0f, laneZ), false, false);
            yield return actor.AimAt(slot.Anchor, 0.5f);
            me.DevTake(slot);
            float until = Time.unscaledTime + 3f;
            while (!me.IsHoldingSomething() && Time.unscaledTime < until) yield return null;
            yield return new WaitForSecondsRealtime(0.3f); // the name arrives with the food
            string want = DemoIngredients.All[slot.IngredientIndex].DisplayName;
            if (!HeldName(me).Contains(want)) actor.CountTimeout("cook: took '" + HeldName(me) + "' instead of " + want);
            yield return actor.LevelView();
        }

        private static void LogGrill(string what, float at, Vessel[] spots)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Vessel s = spots[i];
                if (s == null) continue;
                byte mode = s.Mode.Value;
                var view = s.GetComponent<Worktown.Game.Art.GriddleSpotView>();
                parts.Add("spot " + i + " " + Describe(s) + " up " + GriddleRules.UpZoneOf(mode) + " " + GriddleRules.SideName(GriddleRules.DownSideOf(mode))
                    + " down, flips " + GriddleRules.FlipsOf(mode) + (view != null && view.TopMarksShown ? ", marks on top" : string.Empty)
                    + " '" + string.Join(" + ", s.Names) + "'");
            }
            Debug.Log("[Grill] " + what + " at " + at.ToString("0.0") + " s: " + string.Join("; ", parts) + "; " + RingCount());
        }

        /// <summary>Spec 031b (AC3): the held item's size and its scale in the first-person view.</summary>
        private static void LogHold(PickupInteractor me, string what)
        {
            var view = me.GetComponent<Worktown.Game.Player.HeldItemView>();
            Debug.Log("[Hold] " + what + ": '" + HeldName(me) + "', size " + (view != null ? view.LastViewSize.ToString("0.000") : "?") + " m, drawn x"
                + (view != null ? view.LastViewScale.ToString("0.00") : "?"));
        }

        /// <summary>Spec 031b (AC3): how many heat rings draw now, and for what (the calm view: the aimed one and the alarms).</summary>
        private static string RingCount()
        {
            int shown = 0, all = 0;
            var what = new List<string>();
            foreach (Worktown.Game.Art.HeatRingView ring in UnityEngine.Object.FindObjectsByType<Worktown.Game.Art.HeatRingView>(FindObjectsSortMode.None))
            {
                Vessel v = ring.Vessel;
                if (v == null || v.ContentCount == 0) continue;
                all++;
                if (!ring.IsShown) continue;
                shown++;
                what.Add(v.Kind + " " + v.Zone + (v.Warning ? " warn" : string.Empty));
            }
            return "rings shown " + shown + " of " + all + (what.Count > 0 ? " (" + string.Join(", ", what) + ")" : string.Empty);
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
                + ", bubbles " + (bubbles != null ? bubbles.Rate.ToString("0") : "-") + "/s, pose " + (view != null ? view.Pose.ToString("0.00") : "-")
                + ", label '" + string.Join(" + ", basket.Names) + "'");
        }

        /// <summary>Spec 027c: the zone, progress and ring of the pan and pot, for the tour log.</summary>
        private static void LogHeat(string what, float at, Vessel pan, Vessel pot)
        {
            Debug.Log("[Heat] " + what + " at " + at.ToString("0.0") + " s: pan " + Describe(pan) + " '" + string.Join(" + ", pan.Names)
                + "'; pot " + Describe(pot) + " '" + string.Join(" + ", pot.Names) + "'");
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
