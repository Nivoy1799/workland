---
id: 027c
title: Feel pass 2 - cooking reads at a glance (progress, colour, sound, cues, serve reward)
status: approved
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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
