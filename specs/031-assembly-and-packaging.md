---
id: 031
title: Assembly station and packaging (build wraps in order, wrap paper, boxes, bags)
status: done
owner: architect
builder: opus
---

# 031: Assembly and packaging

## Goal
The heart of a street-food shop (roadmap pillar 3): you build a dish **in order** on an assembly board:
- the base (flatbread or wrap);
- the fillings (grilled seitan, falafel, fries);
- the salad (lettuce, cucumber, red cabbage, tomato, onion);
- the sauces (garlic-herb, chili);
- then you close it (fold the wrap, wrap it in paper) and pack it (box or bag).

The build stacks visibly, layer by layer. Order and completeness matter for the score. Approved by the orchestrator
under the human's 24 h grant.

## Depends on
027b/c/d (feel, rings, labels), 028/028b (ingredients, stock), 029/029b (fryer, tip), 030 (griddle, spatula). The
label rule from 030: a label shows the name the item would get.

## Scope
- `sim/Runtime/Cooking`:
  - an `assemble` operation that adds a layer to a build in order;
  - `close` (fold or wrap) and `pack` (box or bag) operations;
  - a `Build` item kind with ordered layers;
  - rating by recipe template: required layers, order, sauce amount, and the quality of every layer.
  - Pure, deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/`:
  - station kind `Assembly` (a board with one build spot and nearby sauce dispensers);
  - sauce dispensers as a hold-to-dispense use action (hold left click; the amount by time, server-measured from
    repeated use requests within the token bucket, or a start/stop pair through the existing use action);
  - packaging stock (wrap paper, box, bag) on the shelf or in a small rack.
  - No new RPCs. The RPC surface tests stay unchanged.
- `Scripts/Art/`:
  - the visible layer stack (each layer is the ingredient's model or a flat layer in its palette colour, offset in
    height);
  - a wrap fold animation and a paper wrap;
  - the Kenney `styrofoam` box and `bag`, which are already in the project.
- `Scripts/Npc/` and the menu:
  - recipe templates: "Seitan wrap", "Falafel wrap", "Seitan flatbread", "Veggie box" (fries plus grilled veggies plus
    sauce, in a box);
  - customers order these, and order matching compares layers.
- Layout: the assembly board sits between the plates and the serving counter (the end of the dish flow).
  FloorPlanTests stay green, with plates to assembly at most 1.5 m.
- Tests, visual check (the cook tour builds one falafel wrap end to end and serves it), `WhatsNew.json`, this spec.

## Out of scope
- Orders 2.0 (custom modifiers, patience, register: spec 032). Here an order is just the recipe name.
- Drinks (033).

## Design notes
- A wrong layer order costs score but is allowed (forgiving). Missing required layers cost more. Extra layers are
  neutral.
- Sauce: hold for 0.5–1.5 s for the right amount. Too little or too much costs a little.
- Close: a wrap must be closed before packing. A box needs no close.
- Feel: every layer drops onto the stack with a short arc and a soft "plop", and the sauce makes a squeeze sound
  while held. The predicted look follows 027b.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: assemble order, recipe scoring (order, missing, extra, sauce amount), close, pack, determinism.
- [ ] AC3: Server tests: layer rules, the dispenser amount, packaging stock, wrong-step refusals.
- [ ] AC4: FloorPlanTests: the assembly position and distances.
- [ ] AC5: Visual check `cook`: a falafel wrap built layer by layer (frames show the stack), sauce held, folded,
      wrapped, served; the observer sees the stack.
- [ ] AC6: `WhatsNew.json` entry `spec-031`.

## Defaults to review
Recipe templates and prices (wrap 9, flatbread 8, veggie box 10); the sauce window 0.5–1.5 s; the score weights.

## Implementation notes
Builder (Opus), spec 031. No new RPC, SyncVar or RPC parameter; `RpcSurfaceTests` unchanged.

**Sim (`sim/Runtime/Cooking`).**
- Four new ops (catalog 9 → 13), appended `OperationKind`s, so every old hash and rating is unchanged:
  - `squeeze` (Portion): a sauce with `amount` = seconds held; buckets little < 0.5 ≤ right < 1.5 ≤ lots (names "a dash of …", "lots of …");
  - `assemble` (Assemble): the layers in the order they went on (not commutative, 1..`CookingLimits.MaxBuildLayers` 10; no plated food,
    no build inside a build);
  - `close` (`style` folded / wrapped) and `pack` (`container` box / bag). Steps go in order (`Assembly.CheckStep`, new
    `OperationError.StepOrder`): fold an open build on bread; paper only on a folded build; box or bag on a folded build on bread or on an
    open build without bread ("a box needs no close"). Paper and packs add the `plated` tag: finished, sold as they are.
- `RecipeTemplates` (defaults to review): seitan wrap, falafel wrap, seitan flatbread (folded, bag), veggie box (fries, grilled veg,
  sauce, box). Each template layer claims the first fitting layer (ingredient, heat op, cut); a required layer that fits only by
  ingredient counts as prepared wrong. Penalties: missing 0.25, wrong prep 0.10, each out-of-order pair 0.06 (max 0.18), sauce off 0.05,
  close missing 0.15 (folded instead of wrapped 0.08), pack missing 0.12 (the other container 0.06); factor = 1 − penalties, min 0.2,
  never above 1.
- **Rating**: the claimed layers only (extras neutral): Taste of their combined stats × (0.2 + 0.8 × their doneness factor; a layer the
  template takes raw, such as lettuce, a wrap or a sauce, counts as done) × their quality factor × the template factor. A build rates
  against its best template (`ItemRater`); a plate of a build rates as the build. Names: a build with every required layer of a
  template is that dish ("falafel wrap"), else "wrap with …"; "folded …", "… in paper", "… in a bag", a boxed "veggie box".
- **No stacking exploit** (029 lesson): no step gives a bonus (factor ≤ 1), extra layers are neither rated nor penalised, a second shaken
  falafel adds nothing, close and pack happen once. Test `RepeatedSteps_CannotRaiseTheScore` piles lettuce, sauce and shaken falafel.
- New sim tests: `AssemblyTests` (15). Packaging prices are grocer rows in `sim/Runtime/Economy/EconomyCatalog.cs` (outside the named
  folder, as in 030): wrap paper 35, box 40, bag 35 (≥ the cheapest food, so the anti-faucet rule holds); catalog tests updated.

**Server (`Scripts/Cooking`).**
- `StationKind.Assembly` and `VesselKind.Assembly` (appended). The build spot is a fixed scene NetworkObject (Vessel + `AssemblySpot`, no
  Pickup), exactly like a griddle spot: food goes on with `RequestUse(spot, 0)`, empty hands take the build, the new
  `VesselRules.ActionFold` (5) folds. The sim item **is** the state (`KitchenState` keeps one `Item` per spot), so the label (the take
  name), the looks (one `FoodLook` per layer, in order: the stack) and the mode byte (`BuildRules.Mode`: close, pack, bread) all come
  from it. A build taken off goes back on an empty board as it was.
- **The dispenser: a press/release pair through `RequestStation(board, option)`** (options 1–2 press, 3–4 release, 5–7 paper/box/bag;
  the board's tools are its station slots, so they reuse the stock-slot aim). Why not repeated use requests: at the client's 0.243 s
  spacing the amount would be quantised to about four steps inside the 0.5–1.5 s window, and a long squeeze would spend the whole token
  bucket. The pair costs two tokens, and the server measures the time between the two arrivals with its own ticks
  (`BuildRules.SqueezeSeconds`; both ride the same reliable channel, so jitter mostly cancels). The press checks the build (open, there)
  and records (player, tick); the release previews the sauce layer, charges the sauce's grocer price, then commits. The server stops a
  squeeze held 3 s (`AutoStopSeconds`; a lost release never squeezes for ever) and drops it when the player is gone; a release without
  a press is a harmless Ok; another player's press on the same dispenser is `DispenserBusy`; the release works out of reach (the press
  was in reach). A second squeeze of the sauce on top adds to that layer (one amount, not two layers).
- Packaging: preview in the sim, charge (`Charge` → `EconomyService.TryBuy`), commit; a refusal (`FoldFirst`, `NothingToFold`,
  `BuildClosed`, `StationEmpty`, `NotEnoughMoney`) changes nothing and charges nothing. New `CookOutcome`s and HUD texts:
  BuildClosed, FoldFirst, DispenserBusy, NothingToFold. The board's `StationContents` byte has a squeezing bit per dispenser.

**Client.**
- `InteractionRules`: on the spot, food = PutInto ("on the board"), empty hands = the new `Fold` on an open build on bread with
  something on it, else `Take`; finished food and tools do nothing. Tools: `Squeeze` ("Hold · squeeze garlic-herb sauce", any hands),
  `Pack` ("Click · wrap in paper ($35)"), grey reasons ("Fold it first", "Start with a wrap or flatbread on the board", …).
  A ray on the board means a tool only within 0.18 m of it.
- `SqueezeHold` (pure): the look follows the button at once; the start and the stop go out when the request interval allows, never
  swallowed (a quick tap still sends both, in order).
- Predictions: the fold and the packagings use the new `PredictionKind.BuildStep` (the stack shows the new mode at the click; confirmed
  when the spot's close/pack bits move, denied when the board empties; a refusal opens it back up with "nope"). Layers use the existing
  PutInto prediction (the food flies into the spot). The squeeze ribbon is predicted from the press to the release; others see it from
  the board's byte.
- Looks (`Scripts/Art`): `BuildStackView` stacks the layers (a bread base and a sauce are flat discs in their colour, the rest the
  ingredient's own pile, each on the one before), drops a new layer on a short arc from the cook's side with a squash and a "plop" for
  every viewer, folds (the wrap rises over the fillings), wraps in paper and drops a box or bag over it (0.3 s each).
  `SauceDispenserView` tips the bottle over the build, pulses it, draws the ribbon in a slow zigzag and loops the squeeze sound. Held
  builds draw as the stack, the folded wrap, the paper (Cream), the Kenney `styrofoam` box or `bag`. New procedural sounds: Plop, Squeeze
  (loop), Fold, Paper. No per-frame allocations in the new views.

**Art.** Graybox `Art_StationAssembly` (0.9 × 1.2 m, 0.90 m): a wood cabinet, a cutting board for the build at the front left, a steel
shelf with the two sauce bottles at the back, a low packaging rack on the right (paper sheets, two boxes, a bag). New keys: StationAssembly,
FoodLayerDisc (0.25 flat), FoodWrapFolded (0.25), FoodWrapPaper (0.25), PropTakeawayBox (0.20), PropPaperBag (0.28); guide rows added
to `docs/art-direction.md` (wrapped wrap, takeaway box, paper bag).

**Layout.** Dish flow: stock, prep, griddle, fryer, stoves, plates, **assembly**, plate shelf / serving counter. The plating worktop is
one plate pair wide (0.74 m, plates at x 9.46 and 9.96), the board stands at x 10.55, the plate shelf and the serving counter moved
0.42 m to 11.64 (the queue and the rail with them). Nearest plate to the board 0.60 m, plate shelf to the board 1.09 m, the board to the
counter's aisle point 1.09 m; every 027a/029b/030 limit holds (stove B to the shelf 2.92 m ≤ 3, fryer to the nearest plate 3.02 ≤ 3.5,
griddle 3.97 ≤ 4.5, the aisle 14.3 m ≤ 17.06). New `FloorPlanTests` assembly test (order, the 1.5 m limits to the plates and to the
plate shelf, a step to the counter, the networked spot without a Pickup on the board in reach, the five tools in reach).
Interpretation: "plates to assembly at most 1.5 m" is checked for both the plate home spots and the plate shelf.

**Menu and orders.** Seitan wrap, Falafel wrap, Seitan flatbread, Veggie box (ids 10–13, meal). `MenuDish.Template` names the recipe
template; `OrderRules.MatchBuild` compares the layers: Exact with every required layer prepared right and the close and pack of the
template, Close one short, else Wrong (no build or no main ingredient: Wrong). Order and sauce amount only cost score. Deviation as in
029/030: no per-dish prices (the sale price comes from the sim's meal market), so "wrap 9, flatbread 8, veggie box 10" are not applied
(spec 037). A squeeze costs the sauce's grocer price ($80), a default to review.

**030 review fixes.**
1. `Griddle.CombineSides`: the mean only when both sides are in the window; an outside tie gives the burnt side. Sim test changed
   ((0.25, 1.0) in [0.5, 0.75] is outside); new KitchenState test: five flips with the same per-side totals give the same hash and quality.
2. The right-click flip goes through the ClickBuffer (`QueuedKind.Flip`, dropped when the aim moved).
3. `GriddleSpotView`: owed flips expire every frame; a flip's start snapshot counts our own flips still on their way
   (`FeelRules.FlipStart` / `FlipSteps`), so one counter step never confirms two quick flips. Test.
4. Labels: `KitchenState.ContentNamesAndTitle` previews each food once for the label and the title; the periodic refresh skips vessels
   whose cooked ticks did not change (`HeatStamp`). Test (same strings as before, stamp unchanged while off).
5. New `PrefabRegistrationTests`: every prefab field of the scene's KitchenService is in `DefaultPrefabObjects` (031 adds no prefab: the
   build spot is a scene object).
6. "Grilled veggies" takes only the grill vegetables (`DishNeed.AnyOf`; test: paprika + seitan is not Exact); `Spatula.GriddleIndex`
   removed; the plated-food test asserts `AlreadyPlated` with a real plated item.

**Scope beyond the listed folders** (as in 029/030): `sim/Runtime/Economy` (grocer rows), `Scripts/Net` (aim, rules, PickupInteractor),
`Scripts/Player` (PredictionKind, FeelRules, SqueezeHold), `Scripts/Audio` (four sounds), `Scripts/UI/HudView.cs`, `Scripts/Dev` (tour),
`Editor` (catalog, scene), `docs/art-direction.md` (rows), `scripts/visual-check.sh` (cook timeout 780 → 960 s). Fixtures updated for the
new station, spot, layout and menu: ArtCatalogTests, PlaceSceneTests, ScaleSceneTests, CookingRulesTests, OrderRulesTests,
CustomerOrderTests (80 draws instead of 40 cover the 13-dish menu), TourRulesTests (an `assembly-first` look shot), WhatsNewTests.

**Tests.** `scripts/verify.sh` PASS: 933 EditMode (baseline 907), 268 sim (baseline 253). New EditMode: `AssemblyStationTests` (20),
`PrefabRegistrationTests` (1), the FloorPlanTests assembly test, the spec 031 What's-new test, three 030-review tests in
GriddleStationTests. New sim: `AssemblyTests` (15). RPC surface tests unchanged.

**Visual check (AC5).** `bash scripts/visual-check.sh --tours look,cook`: PASS (timeouts 0, aim fallbacks 0).
- `.verify/visual/cook/sheet.png`, cells 105–213: the board (105), the wrap dropping on (106–117), the falafel from the fryer (118–132),
  the lettuce slices (133–144), the garlic-herb squeeze with the bottle over the stack and the ribbon (145–157), the fold (159–172),
  the paper (173–184), served (187–213). Log: squeeze 1.00 s → "falafel wrap", fold and paper click to visual "same frame", click to
  confirm 64 / 103 ms, "sold falafel wrap in paper ★★★★☆ for $382 (meal)" (q 0.77). No guest had a Falafel wrap order in this run
  (the queue's draws), so the feed says "Veggie mix order: wrong dish"; the exact match is covered by the EditMode test.
- `.verify/visual/cook-observer/sheet.png`, cells 36–148: the observer's close-up of the board: each layer landing (frames 0.04 s), the
  remote squeeze (the bottle tipped over the stack, the ribbon), its end, the fold, the paper, the take.
- `.verify/visual/look/sheet.png`: cell 55 `assembly-first` (the board, the dispensers, the rack).

**What still looks or feels off (follow-ups).**
- The held build in first person sits at the lower right edge like any held food and is barely seen (the 018 hold pose; the potato too).
- The Kenney `styrofoam` model is an open clamshell standing on its hinge; on the rack it reads as two trays, and a box coming down
  over a veggie box is not shown in the tour (only the paper). A closed takeaway box model would read better.
- The paper wrap is a Cream lozenge over two thirds of the folded wrap (graybox); a real paper texture or band would help.
- The build's label (the take name) is long while it has no dish name ("wrap with golden falafel balls and lettuce slices").
- Lettuce slices are the generic green slice pile; the falafel peeks out beside them (layers shift a little to alternating sides).
- A squeeze costs the sauce's full grocer price ($80) per squeeze, more than the wrap ($70): a price model is spec 034/037.
- The tour's `sauce-squeeze` frames run into the fold frames on the sheet (the frame capture runs on while the tour goes on).
- The client's caches of griddle spots and baskets are filled once, like the build spots were; the build spots now look again while
  none is found (a scene object is inactive on a client until spawned). The other two may need the same.

## Blockers
(Builder fills this in after 3 failed attempts.)
