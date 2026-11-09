---
id: 036
title: Seitan spit (rotating, outer layer cooks, shave slices with the knife)
status: approved
owner: architect
builder: opus
---

# 036: Seitan spit

## Goal
The signature station of the vegan street-food shop (docs/cooking-roadmap.md): a vertical rotisserie with a large
seitan cone that turns in front of a heat panel. The outer layer browns over time. You **shave** thin slices with the
knife (hold to shave). Fresh slices fall into a catch tray, and you scoop them into a wrap or flatbread on the
assembly board. Shave too early and the slices are pale; leave the outside too long and it chars. Shaving exposes the
next layer, which starts cooking again. It is the most "genre" moment of the shop. Approved by the orchestrator under
the human's 24 h grant.

## Depends on
019 (knife, cutting gestures), 027b/c (feel, rings, cues), 031/031b (assembly layers, hold patterns, money pattern),
034 (stock: the seitan cone is a stock item ordered at the terminal, about 30 portions per cone).

## Scope
- `sim/Runtime/Cooking` (pure):
  - a spit model: a cone with layers;
  - the outer-layer doneness by heat time;
  - `shave` (an amount from the server-measured hold) produces "shaved seitan" with that doneness;
  - after a shave the outer layer resets toward the inner one (partly cooked);
  - the cone runs out after N portions.
  - Deterministic; old hashes and ratings unchanged.
- `game/Assets/_Project/Scripts/Cooking/`:
  - station kind `Spit` with a heat knob, a cone mount (load a new cone from stock) and a catch tray (a fixed vessel);
  - shave = holding the knife on the cone (press/release through the existing pattern, server-timed, 0.2 s gate in
    third person);
  - the tray holds up to 3 portions, and a scoop takes one portion into the hand or onto a held build (a 031 layer);
  - the 027c ring for the cone's outer layer;
  - no new RPCs, presentation bytes pinned.
- `Scripts/Art/`:
  - the turning cone (graybox stacked discs in WoodLight/WoodDark, shrinking as it is used);
  - a browning colour per outer layer;
  - a glowing heat panel;
  - shaving curls falling into the tray;
  - the knife motion while shaving.
- `Scripts/Audio/`: a sizzle by heat, a shave rasp, a motor hum.
- Menu: "Seitan wrap" and "Seitan flatbread" (031) take shaved seitan (spit) or grilled strips (griddle), both
  valid, but the review mentions which one.
- Layout: the spit stands in the heat block, its knob and cone facing the aisle. FloorPlanTests stay green.
- Tests, visual check (load a cone, heat, shave at the right time, scoop into a wrap, serve), `WhatsNew.json`, this
  spec.

## Out of scope
- Cone preparation (stacking a raw cone from seitan). A rotisserie for vegetables.

## Design notes
- The outer layer: good at 40 s on high heat, perfect at 55 s, burnt at 90 s. After a shave the new outer layer starts
  at 50 % of the good level.
- A 1 s hold is one portion (about 60 g). The hold amount maps to portions in steps, measured on the server.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: layer doneness, shave, layer reset, running out, determinism.
- [ ] AC3: Server tests:
  - shave timing (the re-press rule);
  - the tray capacity;
  - the scoop onto a build;
  - loading a cone from stock (charged at order time only, refusals change nothing).
- [ ] AC4: Visual check: the turning cone browns, the shave curls fall, a wrap with shaved seitan is served.
- [ ] AC5: `WhatsNew.json` entry `spec-036`.

## Defaults to review
Timings, portion size, 30 portions per cone, the cone price at the terminal.

## Implementation notes
Builder (Opus), spec 036, with the 035 tuning and the 035 review fixes (below). No new RPC, SyncVar or RPC parameter (`RpcSurfaceTests`
unchanged). The spit uses the existing bytes only, pinned in `SpitStationTests.TheSyncedSurface_...`: the station's `StationContents` byte,
and two fixed scene `Vessel`s (their 6 synced members, as since 027c). New values inside existing bytes: `FoodLook.HeatShaved` (5), and
`CookOutcome` StillHolding, NoCone, ConeLoaded (appended, HUD texts).

**Sim (`sim/Runtime/Cooking`, pure).**
- `Spit` (rules) and `SpitCone` (immutable: portions left, the outer layer's heat time, shaves). Heat op `shave` at the station "spit"
  (catalog 15 -> 16): its one input is the cone's meat (a base seitan item; `Spit.IsConeMeat`, else ForbiddenInput: shaved seitan is never
  shaved again), `time` = the outer layer's sim seconds at high heat (0..1800), buckets pale / (shaved) / charred from the good level and
  the window's end; doneness along `Spit.OuterCurve` = `FryCurve.FromKitchenSeconds("seitan", 40, 55, 90)` (the design notes' timings,
  one line). `HeatProfile` got an optional `Spit` curve; `ItemFactory.Heat` has a branch for it (quality factor never above the input's).
- Knob: off / low (half speed) / high. A hold maps to portions in steps (`Spit.PortionsFor`: under 0.5 s nothing, 0.5-1.5 s one, 1.5-2.5 s
  two, from 2.5 s three; the server stops at 3 s). All portions of one hold have the outer layer's doneness; then the new outer layer starts
  at 50 % of the good level (`ResetSeconds` = 20 real s), or stays where it was when that was less cooked. 30 portions per cone, then it
  runs out (the spit is bare).
- Menu: the seitan layer of "seitan wrap" and "seitan flatbread" takes `griddle` or `shave` (one template array, `SeitanHeated`). Old
  dishes are pinned (`SpitTests.Pre036Dishes_...`, measured at 93ce453: hash, name, quality of both with grilled strips). The review note:
  `Spit.ReviewNote(build)` ("The seitan was shaved fresh off the spit." / "The seitan came as grilled strips."), carried as
  `OrderFit.Note` -> `GuestTab` -> `RegisterPayment.Note`; the guest's bubble and the feed append it (`CustomerRules.ReviewText(review,
  note)`); the review's packed byte, stars and key are unchanged.
- No stacking (test `ShavingAgain_NeverRaisesQualityBeyondHonestPlay`): 40 seeded random schedules of heat and holds never give a portion
  above the best single shave of a fresh layer; three portions in one wrap rate as one (extras are neutral); a shave right after a shave
  is pale. Note: the best single shave is at 70 s (the window's end: the stats saturate there), not at 55 s ("perfect"); 0.445 vs 0.435
  alone, in a wrap 0.708 vs 0.701 for grilled strips.

**Server (`Scripts/Cooking`).**
- `SpitDesk` (pure, tested): per spit the cone (folded forward with the ticks on every change), the knob, the catch tray (3 portions) and one
  shave hold with the 031b rules: the same player's re-press keeps its start (10.0), another player is DispenserBusy, the release measures
  the server's ticks, the auto-stop at `Spit.MaxShaveSeconds` (3 s), a player who left is dropped (`DropLeft`), and the knife put down
  meanwhile shaves nothing (like the 035 cloth). `LoadCone` (check, take one unit of the cone line, load; the backstop gives the unit
  back), `ScoopIntoHands` (a failed hand-over puts the portion back), `ScoopOntoBuild` (KitchenState.TryAddLayerToHeld: the 031 layer on an
  open build in the hands; closed or full refuses and the portion goes back).
- `StationKind.Spit` (appended, index 16 in the scene); `VesselKind.SpitCone` and `SpitTray` (appended, fixed). Options through the
  existing `RequestStation`: 1 knob, 2 load, 3/4 shave press/release (the release needs no reach); the tray is `RequestUse(tray, 0)`.
- Bytes: the station byte (level bits 0-1, shaving bit 2, cone bit 3); the cone vessel's Mode (portions 0-5, a shave counter 6-7), its
  HeatProgress/HeatZone = the outer layer through its own `HeatSync` (027c: the ring, Perfect/Warning/Burnt cues, the sizzle, the smoke),
  its Names/Title = the take name (the 030 rule; refreshed with the zone); the tray's Names/Looks = its portions. A burnt cone counts as
  the burning smell (035 hygiene).
- **Measured outer-layer zones** (the real sim op through `KitchenState.SpitTimeline`, `SpitStationTests`), real seconds on high: good
  40.0, perfect 55.0, warning 67.0 (the 031b lead, 3 s), overdone 70.0, burnt 90.0; on low twice as long; after a shave the next layer is
  good 20 s later.
- The 027c timeline is the shave op's on the cone's meat through `KitchenState.TimelineFor` (the 027c cache key: hash, op, temperature,
  the exact bits; nothing new).
- Stock: the cone is a 25th stock line `seitan_cone` (appended last, so every line keeps its index): capacity 2, a new game starts with 1;
  the supplier sells it as 30 seitan blocks' worth at the seitan price, one cone per box (`StockCatalog.TryConeOffer`): $2,640 a cone,
  $88 a portion = a seitan block's bulk price, so the wholesaler's cap (shaved seitan counts as seitan) has no faucet. Loading draws one
  unit, no money; a box of cones unpacks on the cone slot. The terminal has 5 pages now.

**Client.** `InteractionAction.Shave` (hold) and `Load`; `AimTarget.SpitTool`. The knife on the cone: "Hold · shave the cone: shaved seitan
(1/3 in the tray)"; reasons "Load a seitan cone first", "The tray is full: scoop it first", "Someone is shaving", "Take the knife to shave";
empty hands at a bare spit: "Click · load a seitan cone (1 left)" (out of stock: the 034 text). The knob "Click · heat low/high/off". The
tray: "Click · take shaved seitan", with an open build in the hands "Click · scoop shaved seitan onto the wrap". The shave reuses the 031
`SqueezeHold` (the cone is the held tool, like the sink's basin) and, third person, the 0.2 s `SqueezeHoldGate`; a tap of E at the spit loads,
else scoops, else turns the knob. Predicted from the click: the loaded cone (`SpitView.PredictLoad`), the shave (curls, rasp, the knife
sawing in first person: `HeldItemView.SetShaving`). Not predicted: the scoop (the portion comes on the server's answer, ~one round trip).

**Art and audio.** Graybox `Art_StationSpit` (base, heat panel frame, skewer, motor housing) and the scene's parts: a front plate, the
knob, the panel's glowing face (emission by level), the cone of 10 stacked discs (WoodLight raw -> WoodDark -> Charcoal by the cone's
zone and progress, every second disc lighter; shrinking by area with the portions; turning 40 deg/s with a motor hum), 12 pooled curls
falling on an arc into the tray while shaving, an onlookers' knife working down the cone (hidden for the shaver's own first-person view).
`SpitView` builds nothing per frame (property blocks, pooled curls). Shaved seitan looks like golden slices (`HeatShaved`, slices shape).
Sounds (procedural): `ShaveRasp` (loop, pitch rises over the hold), `MotorHum` (loop); the sizzle is the 027c vessel loop, scaled by the
knob (`SoundRules.SpitSizzleScale`: low 0.6). Size row "Seitan spit" (0.78 m above the counter) in `ArtSizeTable` and
`docs/art-direction.md`.

**Layout.** The back line has no room: putting the spit between griddle and fryer moves stove A 0.74 m and breaks "board to stove A
<= 3 m" (2.89 + 0.74); between the stoves breaks "stove B to the plate shelf <= 3 m". So the spit stands on the **front counter's cook
side across the aisle from the heat block** (x 8.10, between stove A and B; the body 0.1 m towards the street so the tray and the knob sit
on the counter in front of it), cone and knob facing the cook, like the drink dispenser. Aisle points: to the nearest stove 0.62 m, to
the assembly board 2.31 m, to the griddle 2.61 m. FloorPlanTests: the plan knows the spit as a front-counter station (its knob is
excluded from the back line's front, faces -Street and stands on the aisle side of its body; 13 buttons); the overlap test checks it on a
front run, beside the other counter stations; the new test `Spit_OnTheFrontCounterOppositeTheHeatBlock_...` checks the heat block, the
distances (stove <= 1.0, board <= 3.0), the knob, the cone slot (no collider, in reach, its view) and both vessels (fixed, networked, in
reach, the tray on the cook's side). Every 027a-035 limit holds unchanged.

**035 tuning (orchestrator decision).** Every hygiene rate is 10x slower: `HygieneRules` keeps the score in tenths of a percent
(`MaxTenths` 1000; `DishTenthsPerMinute` 1, `TrashFullTenthsPerMinute` 2, `SpillTenthsPerMinute` 2, `BurningTenthsPerMinute` 1,
`RecoveryTenthsPerMinute` 1); `HygieneMeter(Tenths, Minute)` with `Percent` = tenths / 10 rounded down (49.9 % is 49 %, below the review
line). The byte, the HUD and the 50 / 30 % effects are unchanged. One spill now costs 8 % in 40 game minutes (test). Sim HygieneTests and
the CleaningTests room test updated to the new numbers.

**035 review fixes.**
1. A server stop keeps exactly 8 plates: `CleaningRoom.EndSession` calls `DishRoom.Restarted()` after the carried dishes are back:
   `Clean = TotalPlates - ScenePlates - Eating - AtReturn - InBasin` (the scene's two plates come back, FishNet's pooled ones are gone);
   should more be dirty than fits, the newest go. Tests: both scene plates served to go then a restart (8, not 10); five from the shelf then
   a restart (8, none lost); a busy room.
2. Both `ForgetBox` methods deleted (dead); the 035 note corrected.
3. `StockRoom.CanDraw(line, minPercent)` (`AvailablePercent >= minPercent`); the stock wallet passes the squeeze's 10 % step and the pour's
   1 %. The client says "Almost empty: order more at the terminal" for a tap whose kegs show no whole cup.
4. `StockRoom.EndSession(Func<string, long, bool>)`: an order whose refund fails stays in the book and still arrives (test).
5. `EconomySnapshot` v2: each account's `GrocerPaid` is in the canonical form (pins updated, new test).
6. The scrub (release and auto-stop) counts only for a scrubber still there with empty hands (`CleaningService.ScrubCounts`), like the
   wipe's `WipeCounts`.
7. Only a full bag counts as a full trash (`CleaningRoom.HasFullBag`; test).
8. `_burningCount` resets on a server stop.
9. The plate shelf's byte is "known" only with a cleaning service in the scene (`CleaningRules.ShelfKnown`).
10. Tests (`CleaningReviewTests`): the discard through the kitchen's own step (`KitchenState.TryDiscard`, which `DiscardHeld` now runs)
    with a real economy state (digest) and stock counters; `ThreeBoxesInOnePass` runs the service's own pass (`StockRoom.SpawnWaiting`, now
    called by `SupplyService.SpawnWaitingBoxes`); the StopPour fix (`DrinkDesk.ForgetGonePour`, extracted); the scrub's and wipe's
    re-checks; the lost dish and bag.
11. Nits: `CleaningRoom` uses `HygieneRules.OverdueDishes`; the dead `StockRoom.Draw(int, int, out int)` removed (tests use the
    four-argument draw); your own other hold is `CookOutcome.StillHolding` ("Finish the other one first"), not "someone".
    `ShopStock`'s test-only overload: left as it is (it is in `/sim`, public for the sim tests; `internal` would need
    InternalsVisibleTo).

**Scope beyond the listed folders** (as in 029-035): `sim/Runtime/Economy` (hygiene, snapshot), `Scripts/Shop` (stock line, review fixes,
the review note), `Scripts/Npc` (the note), `Scripts/Net` (rules, aim, interactor), `Scripts/Player` (`HeldItemView`), `Scripts/UI` (HUD
texts), `Scripts/Dev` (tour), `Editor` (catalog, scene), `docs/art-direction.md` (one row), `scripts/visual-check.sh` (cook timeout
1620 -> 1860 s), `specs/035-cleaning.md` (the ForgetBox note). Fixtures updated (spec-mandated): ArtCatalogTests (the 17th station),
ScaleSceneTests, PlaceSceneTests, HandsOnSceneTests (the spit's vessels and knob), CookingRulesTests (kinds), StockSupplyTests (the cone
line's offer, 5 terminal pages, the four-argument draw), WhatsNewTests (035 no longer newest), TourRulesTests (`spit-first`),
KitchenPolishTests (the rasp's peak), CleaningTests (rates, StillHolding, the refund delegate).

**Tests.** `scripts/verify.sh` PASS: EditMode 1094 (baseline 1067), sim 354 (baseline 341). New EditMode `SpitStationTests` (14),
`CleaningReviewTests` (10), the FloorPlan spit test, `CleaningTests.AServerStop_WhoseRefundFails_...`, the spec 036 What's-new test. New sim
`SpitTests` (11), `HygieneTests` +1, `DeterminismTests` +1. RPC surface tests unchanged.

**Visual check (AC4).** `bash scripts/visual-check.sh --tours look,cook`: PASS (look 62 images, cook 965; timeouts 0, aim fallbacks 0).
The cook tour's new part `ShaveSeitanWrap` runs after the grilled veggies (`CookSequence`).
- `.verify/visual/look/sheet.png` 57 `spit-first`: the bare spit on the front counter from the aisle (panel, skewer, tray, the lit
  "heat" knob), prompt "Click · load a seitan cone (1 left)".
- `.verify/visual/cook/sheet.png`: 664 `spit-bare`, 665-675 `cone-load-*` (the full cone appears at the click), 676-686 `knob-high-*`
  (the panel glows Terracotta, the knob lights), 687 `spit-high` (the turning cone, steam), 688-693 `spit-23s-cooking` ..
  `spit-50s-good` (the cone browns WoodLight -> WoodDark), 694 `shave-aim` ("Hold · shave the cone: shaved seitan", the label "shaved
  seitan", the Perfect ring beside the cone), 695-718 `shave-*` (curls falling into the tray, the knife sawing in view), 719
  `shaved-in-tray` (two portions; the cone pale again: the reset layer), 720-730 `scoop-*` (onto the bare wrap in the hands), 731
  `seitan-on-held-wrap`, 732-734 the build (seitan, lettuce, wrapped), 735-760 `spit-wrap-served-*` (the reward). Log: Perfect at 53.0 s
  on high, "shaved for 2.00 s: 2 portion(s) ... q=0.44", the cone 28 portions, "Player 1 handed seitan wrap in paper ★★★★☆ to a guest ·
  Seitan wrap order: exact order". That guest also ordered a drink, so its review (with the note) comes only when its order is complete;
  the tour does not pour it, so the note is shown by the EditMode test, not on the sheet.

**Still off / follow-ups.**
- The best quality is at the end of the good window (70 s), not at "perfect" (55 s): the heat op's stat gains saturate at the window's
  end, as for the griddle and the fryer. A human call whether "perfect" should rate best.
- The scoop is not predicted (the portion appears on the server's answer, about one round trip); load and shave are.
- An open wrap with fillings can only be folded by empty hands (031: no "take" while it folds), so a scoop "onto a held build" works for a
  bare wrap or a bread-less build; the natural flow is to scoop into the hand and put it on the board.
- The first-person knife saws in the lower right but does not reach the cone; onlookers see a knife on the cone instead.
- The tray's label lists every portion ("shaved seitan / shaved seitan"); a count would read better. The shaved seitan is drawn as the
  generic round slices pile.
- The cone is a stack of 10 cylinders (graybox): it reads as a cone, but the browning is one colour per layer, not a crust.
- The spit's body hides the guests behind it a little; the heat panel faces the cook, so guests see its back.

## Blockers
(Builder fills this in after 3 failed attempts.)
