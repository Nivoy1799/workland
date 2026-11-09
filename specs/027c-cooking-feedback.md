---
id: 027c
title: Feel pass 2 - cooking reads at a glance (progress, colour, sound, cues, serve reward)
status: done
owner: architect
builder: opus
---

# 027c: Cooking feedback

## Goal
The spec 027 visual check showed that while the pot heated for 30 s, the only change was the words "warming up"
and then "short". docs/feel.md section 7 asks for cooking that reads at a glance:
- the food changes colour while it cooks;
- a ring shows progress and the good window;
- the sizzle follows the heat;
- a clear cue sounds at "perfect", a warning 3 s before burning, and smoke when it burns;
- serving gives a reward moment.

Approved by the orchestrator under the human's 24 h grant. Build after 027b.

## Depends on
- 017 (FoodLook), 019/021 (pan, pot, burners, KitchenService), 022 (orders, stars), 023 (sounds, steam),
  027 (visual check), 027b (FeelSettings, sounds infrastructure).

## Decision touched
**D21** says clients see only FoodLook bytes, names and stars. This spec adds two presentation bytes per vessel
on heat: `HeatProgress` (0–255, the share of the way to the end of the good window) and `HeatZone`
(Raw, Cooking, Good, Perfect, Overdone, Burnt). The server computes both from the sim's doneness window. They
reveal no stats or recipe internals. The inbox asks the human to confirm; the default is yes. Until then the
spec is built as written, which is easy to revert.

## Scope
- `game/Assets/_Project/Scripts/Cooking/`: `Vessel` (two SyncVars), `KitchenService` (fill them on the server
  tick), new pure `HeatZoneRules`.
- `game/Assets/_Project/Scripts/Art/`: new `HeatRingView` (world-space ring over the vessel: fill = progress,
  a marked arc for the good window, colours from the palette), `FoodArtRules` / `FoodVisual` (colour shift with
  progress: raw → golden → brown → dark), `VesselSteam` (smoke when burnt).
- `game/Assets/_Project/Scripts/Audio/`: sizzle and bubble loops with volume and pitch from heat and zone;
  one-shots for Perfect, Warning (3 s before Overdone) and Burnt.
- `game/Assets/_Project/Scripts/UI/` and `Scripts/Npc/`: the serve reward. Stars pop over the counter, a coin
  sound plays, the tip is shown as floating text (palette Mustard). Uses the existing ServeFeed / order result,
  with no new RPCs.
- Tests, `WhatsNew.json`, this spec.

## Out of scope
- New stations (fryer, griddle: specs 029/030 reuse this ring and zone system).
- Changing cooking rules or timings.
- Label clean-up (027d).

## Design
- `HeatZoneRules.Zone(simSeconds, window, burnAfter)` and `Progress(...)` are pure and tested. The server
  updates the SyncVars at most 4 times per second and only when the value changes, so bandwidth stays low.
- The ring shows only while the vessel is on a burner that is on, or while its food is still warm. It faces the
  camera, has a size fixed in pixels, and is hidden in first person when it would cover the view centre.
- Colour shift: lerp the food's FoodLook tint through 4 palette-anchored keys by progress. Burnt forces Charcoal.
- Sound: the loop volume follows the heat (0 when off), and the pitch rises slightly in Overdone. Each cue plays
  once per zone entry, so leaving the zone and coming back does not spam it.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged (SyncVars only).
- [ ] AC2: `HeatZoneRules` tests:
  - zone boundaries;
  - progress is monotonic;
  - the warning fires exactly 3 s before Overdone;
  - bad input.
- [ ] AC3: Server test: the SyncVars change only on value changes, at most 4 Hz.
- [ ] AC4: Visual check `cook` sheet: the ring fills over time, the food colour visibly changes between frames,
      and smoke appears once it burns (extend the cook tour to leave one pan on until burnt).
- [ ] AC5: The serve reward is visible in the sheet when the cook tour serves a dish (extend the tour to serve
      one order).
- [ ] AC6: `WhatsNew.json` entry `spec-027c`.

## Defaults to review
Ring size 48 px; update rate 4 Hz; warning 3 s before overdone; the four colour keys (raw: the ingredient tint,
golden Mustard, brown WoodLight-dark mix, burnt Charcoal).

## Implementation notes
- **Zones (server, `HeatZoneRules`).** When food goes on the heat, `KitchenState` builds a `HeatTimeline` for it by
  bisecting the real sim op (`ItemFactory.Apply` at a time, read the components' doneness): Raw below the op's
  minimum time; Cooking until every component reaches its window's minimum; Good, then Perfect for the second half of
  the window; Overdone past the window's maximum; Burnt at maximum + 0.1 doneness (the rater's doneness factor is at
  half there), never for a window that ends at 1 (tomato, onion). No cooking formula is copied, rules and timings are
  unchanged. Signature deviation: `Zone(simSeconds, timeline)` instead of `(simSeconds, window, burnAfter)`; the
  timeline carries both.
- **Bytes.** `HeatProgress`: 0 to 153 (`GoodShare` 0.6) up to the start of the good window, then to 255 at its end,
  so the client can mark the good arc at a fixed place. `HeatZone`: the zone in the low bits plus a warning bit
  (0x80) from exactly 3 real s (18 sim s) before Overdone; the client cannot know that time otherwise. A pot with
  several foods shows the slowest food, unless one is Overdone or Burnt (then that, progress full). Sent from
  `KitchenService.Update` once per tick through `HeatSync` (only changes, at most 4 per second: 8 ticks at 30 Hz).
- **Zone timings, whole potato** (TestContext output of `HeatZoneRulesTests`), real seconds after the burner goes on:
  pan good 43.5, perfect 55.4, warning 64.3, overdone 67.3, burnt 75.2; pot good 141.4, perfect 180.0, warning
  215.6, overdone 218.6, burnt 244.3. Potato sticks fry about 2.2 times faster (good at about 20 s).
- **Client.** `HeatRingView` is added by `VesselSteam` (pan and pot; the fryer and griddle only need a `Vessel`) and is
  its own scene object, not a child, so the 027b item tween, ghost and outline never pick it up. 48 px, faces the
  camera, to the camera's right of the contents; shows while on a burner that is on or for 5 s after (warm); hidden
  when carried and, in first person, when within 10 px of the view centre. Vertex colours are converted to linear
  (the project is linear; the first sheet showed the palette washed out). The colour shift re-tints the pot's or
  pan's `FoodVisual` parts (all parts take the vessel's progress; boiled food shifts half as far). Smoke: the steam
  turns Charcoal, thicker, with a 10-particle puff, while burnt on the heat.
- **Sounds.** Loop level by zone (0 off the heat, 0.4 raw to 1 good), pitch 1.06 overdone, 1.1 burnt. Perfect,
  Warning, Burnt cues (new procedural clips, plus Coin) on zone entry only; a vessel first seen after a join is only
  recorded; 4 s cooldown per cue and vessel.
- **Serve reward.** Deviation in how: a new `ServeFeed.LastReward` SyncVar (`ServeReward`: serial, counter index,
  stars, paid, tip in whole dollars) instead of parsing the feed text; still no RPC, and nothing the feed text does
  not already show. Tip = paid minus the full price (`OrderRules.TipOf`, exact for the economy's rounding).
  `ServeRewardView` adds itself after the scene loads, pops five stars (earned in Mustard) 0.22 m over the counter
  top, plays the coin there, floats "+$88 · tip $8" in Mustard for 2.4 s; only within 16 m of the counter.
- **Tour.** The cook tour now puts the potato from the stove top into the pan, switches the pan's burner on, shoots
  every 5 s with the pan's zone in the name (and logs `[Heat]` lines), takes 1.5 s of frames when it burns, takes the
  burnt potato out and sells it at the counter with 2.6 s of frames (`serve-reward-*`).
- **Tests.** `HeatZoneRulesTests` (AC2 and the real sim), `HeatSyncTests` (AC3), `CookingFeedbackTests`; the Vessel
  surface test now expects 6 synced members (the two new bytes); 027b's What's-new test is no longer the newest.
- **Follow-ups.** A food taken out of the pan goes back to its bucket colour (Food has no heat bytes); per-part
  progress in a pot; the reward for a sale without a customer shows no tip (there is none); labels (027d) still
  cover the stove. From across the shop (observer sheet, about 8 m) the fixed 48 px rings look bigger than the pan
  and pot; a cap on the world size or a fade by distance would need a human nod (the spec says fixed pixels). The
  rim marks of the good window are thin at 48 px. Unearned stars (Charcoal 70 %) are hard to see against dark hair.
- **Scope.** Beyond the listed folders: `Scripts/Dev/CookSequence.cs` (the tour extension AC4/AC5 ask for).
  Sheets: `.verify/visual/cook/sheet.png` (cells 54-68 `heat-*`, 69-84 the burn and smoke, 85-112 the serve
  reward), `.verify/visual/cook-observer/sheet.png`, `.verify/visual/look/sheet.png`.

## Blockers
(Builder fills this in after 3 failed attempts.)
