---
id: 029
title: Deep fryer (basket, oil, timer by ring and sound, shake, drain, burn)
status: approved
owner: architect
builder: opus
---

# 029: Deep fryer

## Goal
The first real fast-food station: a deep fryer for fries and falafel, the core of the vegan street-food menu
(docs/cooking-roadmap.md). Its rhythm:
- load the basket;
- lower it into the oil;
- watch the ring and listen to the bubbling;
- shake once for an even fry;
- lift and drain;
- tip the food into a fries box or onto a plate.

It reuses the 027c heat zones and ring and the 027b feel layer (arcs, ghost, sounds, prediction). Approved by the
orchestrator under the human's 24 h grant.

## Depends on
- 027a (floor plan; the fryer joins the back line between prep and the stoves), 027b (feel), 027c (heat zones,
  ring, cues), 028 (falafel mix, potato; fries-empty and styrofoam models).

## Scope
- `sim/Runtime/Cooking`: a `deep_fry` operation (oil temperature parameter, time), falafel mix to falafel balls
  (shaped by the basket: one portion becomes 4 balls), potato sticks to fries. Sim tests.
- `game/Assets/_Project/Scripts/Cooking/`: new station kind `Fryer` with two baskets. Each basket is a vessel with
  capacity 1 portion, and has a raised, lowered and draining state. A basket button lowers and raises it. KitchenService
  runs the server rules; the existing ServerRpcs are used (`RequestStation` and `RequestUse` with actions), no new
  RPCs.
- Shake: a use action on a lowered basket (the server gives an even-fry bonus once per fry, within the good window).
- Drain: a raised basket drips for 2 s; food taken out before draining ends gets a small greasy penalty.
- `Scripts/Art/`: the fryer model (Kenney Furniture Kit stove or graybox with an oil well), basket visuals
  (lowered or raised arcs), oil bubbles particles scaling with heat, the 027c ring per basket.
- `Scripts/Audio/`: deep bubbling loop by load and heat, a shake rattle, a drain drip, and the cues from 027c.
- Menu: add "Fries" (already exists, now from the fryer) and "Falafel (4)". Price defaults in "Defaults to review".
- `Editor/GrayboxSceneBuilder.cs`: place the fryer on the back line (dish-flow order: stock, prep, fryer, stoves,
  plates), FloorPlanTests updated for the new order.
- Tests, visual check (extend the cook tour: fries from potato to box), `WhatsNew.json`, this spec.

## Out of scope
- Assembly and packaging stations (031); fries boxes beyond plating fries into a box at the plating table.
- Oil quality and oil change (later, with stock and supply in 034).

## Design notes
- Timings: tuned to the cooking-pace inbox question. Default A: about 2x faster than today's pan. Fries: good at
  about 25 s, perfect 30 s, burnt 45 s. Falafel: good at 20 s, perfect 25 s, burnt 38 s.
- A basket can only be lowered when it holds food and the fryer is on. Raising is always allowed.
- All state is server-side. The client predicts only the basket arc (027b prediction pattern).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: deep_fry makes fries and falafel with doneness by time and temperature; shake bonus once;
      determinism.
- [ ] AC3: Server tests: lower or raise rules, capacity, the shake window, the drain penalty, zones and ring bytes.
- [ ] AC4: FloorPlanTests pass with the fryer in the dish flow; the fryer controls face the aisle.
- [ ] AC5: Visual check `cook` sheet: potato, then sticks, then basket, lower, the ring filling, shake, raise,
      drain drips, fries in a box at the plating table; the observer sees the basket arcs and bubbles.
- [ ] AC6: An order of "Fries" can be served end to end in the tour.
- [ ] AC7: `WhatsNew.json` entry `spec-029`.

## Defaults to review
Timings above; shake bonus +10 % quality; drain 2 s; greasy penalty -10 %; two baskets; prices Fries 6, Falafel 8.

## Implementation notes
Builder (Opus), spec 029.

**Sim (`sim/Runtime/Cooking`).**
- New heat op `deep_fry` at the station "fryer": params `temp` (150–190 C), `time` (6–600 sim s; buckets short "pale",
  medium 120 "golden", long 240 "dark"), and two server choices `shake` (none / shaken) and `drain` (drained / greasy).
  `HeatProfile` got an optional `Fry` (`DeepFryProfile`); the pan, pot and every old item are computed exactly as before
  (the determinism tests and the pinned golden-fries hash are unchanged).
- Doneness follows a per-ingredient `FryCurve`, piecewise linear: to the window's minimum at Good, to its maximum at the
  end of the window, to maximum + 0.1 at Burnt. Time is scaled by the oil temperature ((T - 60) / 120). Food that was cooked
  before continues along the curve.
- **The timings are one line each** in `DeepFry.cs`, in kitchen seconds (the pending inbox answer "about 2x faster"):
  `Fries = FryCurve.FromKitchenSeconds("potato", 25f, 30f, 45f)`, `Falafel = ...("falafel_mix", 20f, 25f, 38f)`.
  `DeepFry.KitchenTimeScale` (6) must equal `CookingTime.TimeScale` (a test checks it).
- Shaping (`DeepFry.Shapes`): potato sticks become "fries", one falafel mix portion "falafel balls" (4 pieces). The name
  template becomes "{drain} {time} fries" ("golden fries", "greasy golden fries").
- The shake bonus (+10 %) and the greasy penalty (−10 %) live in a new `Item.QualityFactor`: deep fry multiplies it, cut and
  heat keep it, mix and plate take the weighted mean. `ItemRater` multiplies the quality by it (clamped). It is exactly 1 for
  everything else.
- The default op catalog now has 7 ops (the count test is updated). New `DeepFryTests` (11).

**Server (`Scripts/Cooking`).**
- New `StationKind.Fryer` and `VesselKind.Basket` (appended). A basket is a scene `NetworkObject` with a `Vessel` and a
  `FryerBasket` (fryer and index). It has **no Pickup** and is never carried. The networked root never moves.
- Fryer front, sent as `RequestStation(fryer, option)`: option 1 is the power knob, options 2 and 3 lower or raise basket 0 or 1 (`FryerRules`).
- Basket clicks are `RequestUse(basket, action)`:
  - action 0 puts food in, or takes it out with empty hands;
  - the new action 3 (`VesselRules.ActionShake`) shakes.
- No new RPC, SyncVar or RPC parameter. The basket uses the existing Vessel bytes:
  - `Mode`: state (raised / lowered / draining) plus a 2-bit shake counter, so every viewer sees each shake;
  - `HeatProgress` and `HeatZone` (027c), as for the pan.
- The fryer's `StationContents.StoveFlags` byte carries the power flag and which baskets are lowered (button lights and prompts).
- `KitchenState` rules:
  - capacity 1;
  - lowering needs food in the basket (StationEmpty) and the fryer on (FryerOff);
  - raising is always allowed;
  - nothing goes in or out while the basket is lowered (BasketLowered).
  - The food fries only while the basket is lowered and the fryer is on; switching the fryer off pauses it.
  - A shake counts once per fry, inside Good or Perfect while the fryer is on; any lowered shake rattles. A shake on a
    raised basket gives NotLowered.
  - The drain is a server timer of 2 s (`SettleDrain`, checked every tick in `KitchenService.Update`). Food taken out while it drains is greasy.
- `KitchenService` publishes the 027c heat bytes for baskets like for pans. TryUseVessel accepts a basket without a Pickup.
- Burner code only looks at pans and pots (`VesselRules.StandsOnBurner`).

**Measured zone timings** (the real sim through `KitchenState.FreshTimeline`, `FryerTests`), in real seconds after the basket goes in:
- fries: good 25.0, perfect 30.0, warning 32.0, overdone 35.0, burnt 45.0;
- falafel: good 20.0, perfect 25.0, overdone 30.0, burnt 38.0.

**Client.**
- `PickupInteractor` aims at baskets (exact ray on the moving collider, and the cone at the contents, reach to the pivot as
  the server measures it). Their fryer and their own collider never hide them.
- `InteractionRules` for a basket:
  - raised: put food in, or take it out ("take golden fries (still dripping)" while it drains);
  - lowered: empty hands get the new `InteractionAction.Shake`;
  - never picked up.
- Fryer button prompts: "fryer on/off", "lower/raise the basket" (`AimTarget.Button(station, option, flags)`).
- **Prediction (027b pattern).** A basket button click swings the basket in the click's frame (`BasketView.Predict`). This is
  judged by the new `PredictionKind.Basket`: confirmed when the synced state crosses the oil line. A refusal or the timeout
  swings it back with "nope". Lowering is only predicted when the client sees the fryer on and food in the basket.
- A shake plays the rattle and the jolt at the click. Other players' shakes come from the counter in the mode byte.
- **Looks** (`Scripts/Art`):
  - `BasketView` tweens the "Rig" child for every viewer: a 0.35 s smooth arc with a small lift, the raised pose 0.30 m up
    and tilted 12°, the shake jolt, drips while draining.
  - `FryerBubbles`: oil bubbles by load and heat (`FryerArtRules.BubbleRate`: none while off, a shimmer while hot and empty,
    a hard boil that calms as the food gets done).
  - `FryerButtonVisual`: knob, pilot light, basket lights.
  - `VesselSteam` works on baskets (steam, burnt smoke, and the one 027c ring per basket). The ring stands 0.2 m above the
    contents (`HeatRingView.BasketLift`), because the contents are under the oil.
  - The basket draws fries as loose sticks (`FoodVisual._loose`). Elsewhere, deep-fried fries are the Kenney `fries` box,
    drawn 1.6x larger on a plate (`FoodArtRules.PartBoost`). Falafel is 4 brown spheres (`ArtKey.FoodFalafelBall`).
- **Sounds** through `SoundRules` and `SoundCatalog`, as new procedural clips:
  - `Fryer`: a deep bubbling loop, 0.85 pitch, level by zone like the sizzle; one loop per hot basket, so two baskets are louder;
  - `Rattle`: the shake;
  - `Drip`: when a basket starts to drain.
- No per-frame allocations in the new views.

**Art.**
- Graybox `Art_StationFryer` (0.8 × 1.2 m, 0.90 m high), built in `ArtCatalogBuilder`:
  - steel cabinet, darker front panel;
  - two oil wells with a golden oil surface (Mustard/WoodDark), a divider, a low rail behind the wells.
- `Art_ToolFryerBasket`: a wire cage of thin steel bars with a handle and a Charcoal grip, 0.52 m with the handle (cookware x1.15).
- The fryer's body collider stops at 0.60 m, under the oil, so the aim ray finds a lowered basket.
- Three empty Kenney `fries-empty` boxes stand on the back deck.
- New size rows:
  - `StationFryer` (0.90, the worktop row);
  - `ToolFryerBasket` (0.45 real, no guide row);
  - `FoodFalafelBall` (0.035, no guide row);
  - `PropFriesBox` (the "Fries (portion)" row).

**Layout.**
- The fryer (index 6, appended so the other station indices stay) stands at x 6.44, between the prep worktop (ends 6.02) and
  stove A. Dish flow: stock, prep, fryer, stoves, plates.
- Everything right of prep moved 0.84 m right:
  - stoves at 7.48 and 9.98;
  - plate shelf and serving counter at 12.48;
  - the queue head and the rail with them.
- The aisle is now 13.92 m (027a + 1.5 m allows 17.06).
- Board to fryer 1.8 m; fryer to stove A 1.04 m.
- The fryer's controls are on its front, facing the aisle: power in the middle, a button under each basket.
- `FloorPlanTests`:
  - 11 buttons;
  - dish-flow order and distances for the fryer;
  - a new fryer test: controls face the aisle, the body collider under the oil, two baskets in the wells, in reach,
    without a Pickup, the handle towards the aisle.

**Menu.** "Fries" accepts `deep_fry` or the pan's `fry` (the old pan fries stay valid). New "Falafel (4)" (id 7, snack,
falafel mix deep-fried to medium). Deviation: the menu has no per-dish prices (the sale price comes from the sim's snack
market), so the defaults "Fries 6, Falafel 8" are not applied; that needs a price model (spec 037).

**Tours.**
- Cook tour, first part:
  - a potato from the shelf, cut into sticks on the board;
  - the fryer on, the sticks in, the basket down (frames);
  - a shot every 5 s with the zone;
  - one shake at Good (frames);
  - up at Perfect (frames), the drain drips (frames);
  - the fries onto a plate, served at the counter.
- The log lists the orders waiting and the feed line.
- In the observer, when basket 0 first goes down the fixed camera moves to a fryer close-up from the prep side. It takes
  1.2 s of frames of each lower and raise, and goes back when the basket is empty.
- Look tour: a new `fryer-first` shot. `TourRules` stove positions are updated.

**Scope beyond the listed folders** (needed for the brief's "basket button, use actions, prediction"):
- `Scripts/Net/PickupInteractor.cs`, `Net/Rules` (AimTarget, InteractionAction, InteractionRules);
- `Scripts/Player` (PredictionKind, FeelRules.Judge, WorldLabelFade: basket labels and fryer button labels are classified);
- `Scripts/UI/HudView.cs` (three outcome texts);
- `Scripts/Npc/Menu.cs`;
- `Scripts/Dev` (CookSequence, TourRules, TourRunner);
- `Editor/ArtCatalogBuilder.cs`.
- Fixtures updated for the new station, menu dish, layout and baskets:
  - ArtCatalogTests, ScaleSceneTests, PlaceSceneTests, HandsOnSceneTests;
  - CookingRulesTests, OrderRulesTests, CustomerOrderTests;
  - TourRulesTests, WhatsNewTests.

**Tests.** `scripts/verify.sh` passes: 865 EditMode tests (baseline 847) and 238 sim tests (baseline 227).
- New EditMode tests (18):
  - `FryerTests` (16);
  - the `FloorPlanTests` fryer test;
  - the spec 029 What's-new test.
- New sim tests: `DeepFryTests` (11).
- The RPC surface tests are unchanged.

**Visual check (AC5, AC6).** `bash scripts/visual-check.sh --tours look,cook`: PASS.
- `.verify/visual/cook/sheet.png`, cells 1–105:
  - sticks, fryer, power, basket in, lower;
  - `fry-00s` … `fry-25s` with the ring;
  - shake, raise, drain drips, drained;
  - fries in hand, onto the plate, the box on the plate;
  - the serve reward.
- `.verify/visual/cook-observer/sheet.png`: `fryer-lower-*`, `fryer-raise-*`, and the close-up guests'-view shots with bubbles and the ring.
- `.verify/visual/look/sheet.png`: cell 6 `fryer-first`.
- Tour log:
  - the queue held two Fries orders;
  - "sold plate of golden fries ★★★★☆ … Fries order: exact order";
  - the server log has "shook the basket (even fry)" and "took golden fries q=0.79";
  - basket arc: click to visual in the same frame, click to confirm 44 ms (lower) and 78 ms (raise).

**What still looks or feels off (follow-ups).**
- Fryer to the plates is about 4.5 m: the two stoves stand between them, because the spec fixes the dish-flow order.
- The oil is opaque, so the fries are hidden while they fry. The ring, the bubbles and the sound carry the doneness; the
  colour shows once the basket is up.
- The drain drops are small. The drip sound and "(still dripping)" in the prompt carry the drain more than the drops do.
- The basket's world label shows the time bucket ids ("potato sticks · short"), as the pan's does.
- No idle oil sound while the fryer is on with both baskets up (only the bubbles shimmer).
- No "tip the basket onto a plate you hold": food comes out by hand, then goes onto the plate.
- `docs/art-direction.md` has no rows yet for the fryer basket and the falafel ball (their size rows have no guide row).
- Per-dish prices are not modelled (see Menu): the tour's fries sold for $620 at the snack market's price.

## Blockers
(Builder fills this in after 3 failed attempts.)
