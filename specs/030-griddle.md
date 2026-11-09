---
id: 030
title: Griddle with flipping (two-sided doneness, spatula, toasting flatbread)
status: approved
owner: architect
builder: opus
---

# 030: Griddle

## Goal
The second fast-food station: a flat griddle for seitan strips and patties, grilled vegetables (paprika, eggplant,
mushroom), and toasting flatbread and wraps. The genre's signature action is the **flip**. Each side cooks on its
own; you flip with the spatula when the down side is done, and a perfect result needs both sides in the good window.
It reuses the 027c rings (one per item, split in two halves for the two sides) and the 027b feel layer. Approved by
the orchestrator under the human's 24 h grant.

## Depends on
027a/028b (floor plan, stock), 027b (feel), 027c (heat zones, rings, cues), 028 (seitan, flatbread, wrap, paprika,
eggplant, mushroom), 029 (shared heat-station patterns; build after 029).

## Scope
- `sim/Runtime/Cooking`: a `griddle` operation with two-sided doneness (side A and side B tracked per item; the
  component doneness is their combination), toasting for bread (short window). Sim tests.
- `game/Assets/_Project/Scripts/Cooking/`:
  - new station kind `Griddle` with a 0.9 × 0.6 m plate and 6 spots;
  - a spatula tool item (like the knife: a free item, placeable);
  - flip = use action with the spatula on an item on the griddle, validated on the server;
  - no new RPCs; `RequestUse(target, action)` carries it.
- `Scripts/Art/`: the griddle model (graybox steel plate on a cabinet, or a Kenney model), the spatula (Kenney Food
  Kit `cooking-spatula`), a flip animation (a 0.25 s arc with a turn, via the 027b tween), grill marks on the cooked
  side (decal or tint), a two-half ring.
- `Scripts/Audio/`: sizzle per item by heat, a flip "tssh".
- Menu: "Grilled seitan wrap" waits for assembly (031). In this spec, add "Grilled veggies" (plated) and
  "Toasted flatbread".
- Back line: the griddle goes next to the fryer (heat block). FloorPlanTests stay green.
- Tests, visual check (the cook tour grills seitan strips and flips them), `WhatsNew.json`, this spec.

## Out of scope
- Assembly and packaging (031). Oil or butter on the griddle. Griddle cleaning (035).

## Design notes
- Timings (default A, about 2x faster): seitan strips about 12 s per side, perfect 14 s, burnt 22 s. Vegetables 10 s
  per side. Bread toasts 4 s per side, burnt at 9 s.
- Flipping a side that is still raw is allowed. Without a flip the top side stays raw and the item scores low.
- A flip without the spatula, by hand, is refused with a prompt: "Use the spatula".

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: per-side doneness, flip, combined rating, toasting, determinism.
- [ ] AC3: Server tests: the flip needs the spatula and reach, spot capacity, zones per side.
- [ ] AC4: FloorPlanTests pass; the griddle front and its controls face the aisle.
- [ ] AC5: Visual check `cook`: seitan on the griddle, the ring's first half fills, flip (arc and turn), grill marks,
      the second half fills, take it off, plate it; the observer sees the flip.
- [ ] AC6: Serve "Grilled veggies" end to end in the tour.
- [ ] AC7: `WhatsNew.json` entry `spec-030`.

## Defaults to review
Timings above, 6 spots, prices Grilled veggies 7, Toasted flatbread 3.

## Implementation notes
Builder (Opus), spec 030. No new RPC, SyncVar or RPC parameter; `RpcSurfaceTests` unchanged.

**Sim (`sim/Runtime/Cooking`).**
- Two heat ops at the station "griddle" (`Griddle.cs`): `griddle` (seitan, vegetables) and `toast` (bread). `Griddle.OperationFor`
  picks `toast` for a single flatbread or wrap. Both have the plate's fixed temperature (200 C, never shown).
- Params: `time` = the **shorter** side's sim seconds, `long_side` = the longer one (`Griddle.Params(sideA, sideB)`; the op refuses
  `time > long_side`). So a flipped item has one hash whichever side went first, and the menu's "time" bucket means "both
  sides at least". Buckets short / medium / long: griddle 0 / 9 / 20 kitchen s, toast 0 / 3 / 9 s. A never-flipped item is
  valid (`time` 0).
- Each side follows the ingredient's curve (`Griddle.SideDoneness`, the fryer's piecewise curve, a `FryCurve`); the component's
  doneness is `Griddle.CombineSides`: the mean when both sides are in the window, else the side further out. So one raw side
  reads raw and rates low, one burnt side reads burnt, only two good sides rate good (`ItemRater` unchanged).
- `GriddleSides` (sim): the two sides' seconds and which side is down; `Cook` adds to the down side, `Flip` turns it.
- Names from the two side buckets only (so they follow the hash): "grilled / toasted {in}", "half-grilled", "warm", "charred".
  Seitan sticks are "seitan strips" on the griddle (`Griddle.Shapes`).
- **The timings are one line each** in `Griddle.cs`, in kitchen seconds per side (default A, about 2x faster):
  seitan 12 / 14 / 22 (good / perfect / burnt), paprika, eggplant, mushroom 10 / 12 / 20, flatbread and wrap 4 / 5 / 9.
- `HeatProfile` got an optional `Grill` (`GriddleProfile`). Every old op, hash and rating is unchanged (all old sim tests pass).
- **Deviation (scope): the grill vegetables did not exist.** The spec's dependency list says 028 added paprika, eggplant and
  mushroom; it did not. Added as base items (`StreetFoodItems.Paprika/Eggplant/Mushroom`, list `GrillVegetables`, cut shapes
  sticks / slices / halves; `All` stays the ten 028 items), grocer prices 60 / 70 / 80 in `sim/Runtime/Economy/EconomyCatalog.cs`
  (outside the named folder, a data row each; the catalog tests' count and sorted list updated), Kenney models
  `paprika`, `eggplant`, `mushroom` (own colours), palette food colours, and slots on the dry shelf (it is now 3 × 3: the third
  column holds mushroom, paprika, eggplant; the fridge was full at 2 × 4).
- Op catalog count 7 → 9 (test updated). New `GriddleTests` (11).

**Server: why fixed spots (`Scripts/Cooking`).** Each of the six spots is a fixed scene `NetworkObject` with a `Vessel` of the new
`VesselKind.Griddle` and a `GriddleSpot` marker, exactly like a fryer basket (no Pickup, never moves). This reuses everything
the basket built: `RequestUse(spot, action)`, the reach check to the pivot (`VesselRules.CheckUseTarget`, now for every
`IsFixed` kind), the client's fixed-vessel aim and cone, the Vessel's bytes, steam, the ring and the sounds. A place surface
with a per-item state would need a second way to address one item (a surface and a spot) for flips and takes, and its own
bytes; there are none to spare without a new SyncVar.
- `StationKind.Griddle` (index 7, appended): the power knob is `RequestStation(griddle, GriddleRules.PowerOption)`; its byte is
  `StationContents.StoveFlags` (on). The down sides cook only while it is on.
- `KitchenState` keeps a `GrillRecord` per spot: the food, the op, two `HeatSession`s (side 1, side 2; only the down one runs),
  the down side, the flip count and the item's **side timeline**. Capacity 1 (VesselFull), plated food never.
- **Flip** = the new `VesselRules.ActionFlip` (4) in `RequestUse`. The server checks reach, then `VesselRules.CheckFlip`: the
  spatula in the hands (else `CookOutcome.NoSpatula`, HUD "Use the spatula"), food on the spot (StationEmpty). Flipping a raw
  side is allowed. After a flip the new down side's heat bytes go out at once with the mode byte (the rate gate is reset for
  that spot), so the two ring halves never swap for a moment.
- Take: empty hands take it off; holding a plate or bowl, the same `RequestUse(spot, 0)` slides it onto what you hold
  (`TipGriddle`, via `TryPour`). Taken before the longer side had 1 s: handed back unchanged (TooShort).
- **Bytes.** `HeatProgress` / `HeatZone` are the down side (the 027c pattern: ring, colour, cues, sizzle). `Mode`: the down
  side (bit 0), a flip counter (bits 1–2, so every viewer sees each flip), the up side's zone (bits 3–5) and a coarse progress
  inside that zone (bits 6–7) (`GriddleRules.Mode`). The up side does not change while it is up.
- **Timeline cache key.** A side timeline is built from the real sim op with both sides at the same time (then the item's
  doneness is that side's), and each side reads its own time against it. Its key is the 027c key plus the side state it was
  built for (`KitchenState.TimelineKeyOf(food, op, temp, SideTimelineKey)` → `…|sides:one-side`), so it never mixes with a
  whole-item timeline; 027c keys are unchanged (`sides` null).
- The spatula is a free item like the knife (`Spatula` marker, a Pickup, placeable), home spot on the griddle's deck, comes
  back when lost (`KitchenService._spatulaPrefab`).

**Measured zone timings per side** (the real sim through `KitchenState.FreshSideTimeline`, `GriddleStationTests`), real
seconds that side was down:
- seitan strips: good 12.0, perfect 14.0, warning 13.0, overdone 16.0, burnt 22.0;
- paprika, eggplant, mushroom: good 10.0, perfect 12.0, warning 11.0, overdone 14.0, burnt 20.0;
- flatbread (toast): good 4.0, perfect 5.0, warning 3.0, overdone 6.0, burnt 9.0.

**Orchestrator additions from the 029b review (all in this round).**
1. Labels never contradict the item's name: every line on the heat is the name a take would give now (`KitchenState.HeatNames`
   previews each session with the basket's shake and drain; too short gives the food's own name). `HeatLabelRules` is now
   `Label(previewName)` and `GriddleLabel(previewName, downSide)` ("grilled seitan strips · side 2 down"); the zone words are
   gone (the ring shows the zone). Labels refresh 4 times a second (was once), only changes are sent. HeatSessionTests,
   PotTests and KitchenPolishOneTests label checks updated to the rule (plus a draining basket: label = the greasy take name).
2. `InteractionRules.EffectiveHands(real, pendingTakeName, pendingPickUp)`, called from `PickupInteractor.Hands()`, three cases tested.
3. A pick-up in flight is `Held.Pending`: held for refusals only, every action None (no take, buy, tip, pour or place). Test: a
   plate in flight never tips a basket or slides from the griddle.
4. The fryer button's prompt uses the same seen state as its click (`FryerRules.WithLowered` with `BasketView.SeenLowered`, the
   new `PickupInteractor.SeenLowered`). Test.
5. `StockTakeLook.DrawnFrame` is set on the first `Step` (LateUpdate) or render, not in `Place`; the test now checks -1 after
   `Begin` and the frame after `Step`.

**Client.**
- `InteractionRules` for a spot: spatula + food = the new `InteractionAction.Flip` ("Click · flip half-grilled seitan strips");
  food = PutInto ("on the griddle"); a plate with room = Take ("Click · slide … on the plate"); empty hands = Take ("Click · take
  …   Flip: use the spatula"). The right click on a spot with food always sends a flip (`RightClickFlips`): by hand the server
  answers "Use the spatula".
- **Prediction:** with the spatula (`PredictsFlip`) the click plays the "tssh" and the arc in the same frame
  (`GriddleSpotView.PredictFlip`), judged by the new `PredictionKind.Flip`: confirmed when the spot's flip counter moves on,
  denied (it turns back, "nope") when the spot empties or times out. Other players' flips come from the counter (with the tssh).
- `GriddleSpotView` (Art): the 0.25 s arc (0.12 m high, `GriddleArtRules.Lift`) with a half turn about the food's middle;
  grill marks (three dark bars) on the food's top for the up side and under it for the down side, shown from Good, darker
  later; the half turn brings the cooked side's marks up. The food's top colour follows the up side (Vessel.SetHeat from the
  mode byte). No per-frame allocations.
- **Two-half ring:** `HeatRingRules.BuildHalves`: side 1 the left half, side 2 the right, each fills from the top in its zone
  colour with its good window on the rim; a Cream tick marks the half that is down. Over the spot (0.16 m up, centred), since
  the next spot is 0.3 m away.
- Sounds: a sizzle loop per item (`VesselSteam` on each spot; hot while the griddle is on), the new procedural `SoundKey.Flip`
  "tssh" (a spatula scrape then a sizzle burst).
- HUD: "Spatula" in the hands.

**Art.** Graybox `Art_StationGriddle` (1.02 × 1.2 m, 0.90 m): steel cabinet, darker front panel, the dark steel plate
0.9 × 0.6 m at the front with a flush rim and a grease trough, a steel deck behind it (a place surface: the spatula's home, room
for a plate). The knob right of the sign, a pilot light (`FryerButtonVisual`, the same on bit). Spatula: Kenney Food Kit
`cooking-spatula`, fitted by the catalog to 0.345 m (0.30 real × 1.15), built 0.096 × 0.036 × 0.345 m. New size rows and guide
rows: Spatula, Paprika, Eggplant, Mushroom.

**Layout.** Dish flow stock, prep, **griddle**, fryer, stoves, plates. The griddle (x 5.49) stands between the prep worktop and
the fryer; the prep run is now one board wide (0.74 m: the board in the middle, the knife behind it), so the fryer (6.44) and
everything right of it keep their places and every 027a/029b limit holds: board to stove A 2.89 m, fryer to the nearest plate
3.28 m, the pan path unchanged. Griddle to the board 0.9 m, to the fryer 0.95 m, to the nearest plate 4.23 m (a plate can wait on
its deck). New `FloorPlanTests` griddle test (order and distances, the knob faces the aisle, six networked spots without a
Pickup on its plate in reach, the spatula on its deck); 12 buttons, 12 place surfaces.

**Menu.** "Grilled veggies" (id 8, meal, main paprika: grilled paprika and one more grilled ingredient, both sides at least
medium, whole or cut, at most 3 ingredients) and "Toasted flatbread" (id 9, snack). As in 029 the menu has no per-dish prices
(the sale price comes from the sim market), so the defaults 7 and 3 are not applied (spec 037).

**Scope beyond the listed folders** (as in 029): `sim/Runtime/Economy` (grocer rows), `Scripts/Net` (aim, rules, Held,
PickupInteractor), `Scripts/Player` (PredictionKind, FeelRules.Judge), `Scripts/UI/HudView.cs`, `Scripts/Npc/Menu.cs`,
`Scripts/Dev` (CookSequence, TourRules, TourRunner), `Editor` (ArtCatalogBuilder, GrayboxSceneBuilder), `docs/art-direction.md`
(rows), `scripts/visual-check.sh` (cook timeout 600 → 780 s). Fixtures updated for the new station, spots, deck, spatula,
shelf and menu: ArtCatalogTests, PlaceSceneTests, ScaleSceneTests, HandsOnSceneTests, StockRulesTests (slot 8 / two samples
per 0.37 m slot), CookingRulesTests, OrderRulesTests, TourRulesTests, WhatsNewTests.

**Tests.** `scripts/verify.sh` PASS: 907 EditMode (baseline 885), 253 sim (baseline 242).
- New EditMode `GriddleStationTests` (20): AC3 (the spatula, reach, spot capacity, zones per side, the off switch pauses, the
  take and TooShort, the toast, the slide onto a plate, the side-timeline cache key), the mode byte, the client rules and
  prompts, the flip prediction's judge, the arc, the two-half ring, both dishes (exact, close when unflipped, sold to their
  market), and the 029b additions 2–4. Plus the FloorPlanTests griddle test, the spec 030 What's-new test, the reworked label
  test and the StockTakeLook frame test.
- New sim `GriddleTests` (11).

**Visual check (AC5, AC6).** `bash scripts/visual-check.sh --tours look,cook`: PASS (timeouts 0, aim fallbacks 0).
- `.verify/visual/cook/sheet.png`, cells 105–164: the griddle with the plate on its deck (105), the knob (106), the seitan
  strips going on (107), the spatula (108), the rings' first halves filling (109–113, `grill-12s-good`), the seitan flip:
  aim and ten frames of the arc and turn (114–124), the second halves (125–130), the grill marks on the cooked sides
  (127 `grill-marks-seitan`), the slide onto the held plate (131–141), the plate in the hands (143) and the serve reward
  (144–164). Log: "sold plate of grilled paprika, grilled mushroom and grilled seitan strips ★★★☆☆ for $1,295 (meal) ·
  Grilled veggies order: exact order"; flips "click to visual: same frame", "click to confirm: 48 / 48 / 86 ms (Flip)".
- `.verify/visual/cook-observer/sheet.png`: cells 42–51 `griddle-flip-*` (the observer's close-up of the remote flip, 0.04 s
  apart), and the guests'-view shots of the griddle with its rings.
- `.verify/visual/look/sheet.png`: cell 6 `griddle-first`; cell 5 the dry shelf with the vegetable column.

**What still looks or feels off (follow-ups).**
- The 3 s warning cue (027c) is longer than the griddle's short windows: vegetables warn at 11 s while still Good (Perfect is
  12–14 s), toast warns at 3 s, before it is even Good (4 s). The cue lead could scale with the window (a human call).
- Grill marks are three dark bars laid on the food's top bounds: they read on the mushroom, the paprika and from the observer's
  close-up, but are small on a pile of strips at a normal distance, and float a little on the paprika's stem. Off the griddle
  (on a plate) the food goes back to its bucket colour without marks (the 027c follow-up).
- Six rings and six labels over a full griddle crowd the view in first person (the labels already fade/stack, 027d).
- The griddle is 4.2 m from the plating worktop; the tour parks a plate on the griddle's deck first. A plate spot next to the
  heat block would shorten it (the floor plan pins plates after the stoves).
- The flip has no spatula motion in the hands (only the food's arc and the tssh).
- Seitan and mushroom rate low on taste (umami heavy), so a perfectly grilled plate is three stars; the grilling itself is
  inside both windows (doneness factor 1).
- `DefaultPrefabObjects` got the spatula prefab inserted by FishNet's refresh (sorted by path, no reorder of the others), so the
  prefab ids after it shift by one in both builds.

## Blockers
(Builder fills this in after 3 failed attempts.)
