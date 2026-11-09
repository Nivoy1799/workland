---
id: 033
title: Drinks (cups, dispenser with fill timing, lids, drinks on orders)
status: done
owner: architect
builder: sonnet
---

# 033: Drinks

## Goal
Roadmap pillars 1 and 3: guests order a drink with their food. You take a cup, hold it under the dispenser
(lemonade, iced tea, soda), stop at the fill line, put a lid on, and serve it with the order. Overfilling spills and
needs a wipe (a hook for the cleaning spec 035). It is a small, satisfying station that teaches timing without heat.
Approved by the orchestrator under the human's 24 h grant.

## Depends on
027b (feel), 031/031b (hold-to-dispense pattern with server-measured time and portion prices, AssemblyDesk money
pattern), 032 (orders with modifiers and register).

## Scope
- `sim/Runtime/Cooking`: a `fill` operation (drink kind, millilitres from the server-measured hold time) and
  `lid`; fill level buckets (low, right, full, spilled); drink items lemonade, iced_tea, soda (vegan, D27 content check).
  Pure, deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/`:
  - a station kind `DrinkDispenser` with 3 taps, each pressed and released through `RequestStation` options, like
    the 031 sauce dispenser;
  - a cup stack (take a cup) and a lid stack (lid a filled cup);
  - spills leave a spill decal flag on the station for spec 035.
  - No new RPCs.
- `Scripts/Art/`: the dispenser (graybox with three coloured taps), the Kenney `cup` / `soda-glass` models (in the
  project), a liquid level inside the cup rising while held, the fill line, a lid, and a spill puddle.
- `Scripts/Audio/`: pour loop, lid click, spill splash.
- Orders: drinks as part of an order (032 order model: an optional drink line). The register charges for them.
- Layout: the dispenser sits on the front counter's cook side, near the serving point. FloorPlanTests stay green.
- Tests, visual check (the tour fills a lemonade to the line, lids it, serves it with a wrap), `WhatsNew.json`,
  this spec.

## Out of scope
- Cleaning the spill (035). Hot drinks. Ice.

## Design notes
- A cup holds 400 ml. The right fill is 340–380 ml. The pour rate is 200 ml/s, so a good hold is about 1.7–1.9 s.
- The server stops pouring at 500 ml: a spill, the cup is full, quality drops.
- The predicted liquid level rises from the press frame. Others see it from the station byte.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: fill buckets, lid, drink rating, content check.
- [ ] AC3: Server tests: press and release timing, the auto-stop at 500 ml, charging once (cup plus portion), a lid
      only on a filled cup.
- [ ] AC4: Visual check: the liquid rises to the line, lid, served with an order.
- [ ] AC5: `WhatsNew.json` entry `spec-033`.

## Defaults to review
Cup 400 ml, right fill 340–380 ml, pour rate 200 ml/s, prices (drink 3).

## Implementation notes
Builder (Opus), spec 033. No new RPC, SyncVar or RPC parameter (`RpcSurfaceTests` unchanged): the dispenser's state rides in the
existing `StationContents` byte, the cup's in the existing `Food.Look` (`FoodLook` unchanged as a struct, two new kind values).

**Sim (`sim/Runtime/Cooking/Drinks.cs`, pure, deterministic).** Base items `cup` ("empty cup", tag `cup`), `lemonade`, `iced_tea`,
`soda` (tag `drink`; D27 content check in `DrinkTests`). Two appended ops (catalog 13 → 15): `fill` (one input: the drink base
item, `ml` 0..500 in buckets low < 340 ≤ right < 380 ≤ full < 401 ≤ spilled; adds `cup`; name "half-full / brimming / overfilled cup
of …") and `lid` (only on a fill, `StepOrder` otherwise; adds `plated`, so a lidded drink sells in the existing `drink` market; "… to
go"). Identity is the bucket; the exact ml only sets the quality factor (`FillFactor`: right 1.0, full 0.9, spilled 0.6, low 0.3 → 1
along the ml). A cup rates by `Drinks.Taste` (0.4 + 0.6 × the five tastes' balance) × that factor (right fill: lemonade 0.78, iced tea
0.82, soda 0.62). Every other op forbids the `cup` tag (forbidden tags are not in the hash: all pinned hashes and ratings unchanged).
Economy (`sim/Runtime/Economy`, outside the named folder as in 031/032): grocer rows cup 35, lemonade 60, iced tea 55, soda 50 (price
= one full cup); `PriceRules.FillPercent` (a started % per 4 ml, 1..100), `FillCost`, `EconomyEngine.TryBuyFill` (one debit: the cup
when it was empty + the drink's share); `ItemCost` counts a fill's drink at its bucket's least share minus $1 and never counts the cup
(the wholesaler cap stays below what was paid; tested for every drink and level). `Drinks.DrawOrderDrink` (seeded: half none).

**Server (`Scripts/Cooking`).** `StationKind.DrinkDispenser` (appended, station index 10). Tools are its slots (`DrinkRules`): taps
0–2, cup stack 3, lid stack 4; `RequestStation` options: press 1–3, release 4–6, take a cup 7, lid 8 — the 031 dispenser pattern.
`KitchenState`: a pour record per (dispenser, tap) = owner, start tick, the cup object in the hands at the press, the ml already in
it; a repeated press by the same player keeps the first start (031b 10.0); another player gets `DispenserBusy`, one player pours one
tap at a time. `DrinkDesk` (pure, the `AssemblyDesk` money pattern, `IDrinkWallet`): press checks the cup (`NeedsCup`, `CupClosed`,
`OtherDrink`, `CupFull`) and that the player can pay the smallest pour; release/auto-stop previews the cup for the server-measured
ml (`PourRate` 200 ml/s, capped at 500), charges once (cup if it was empty + `FillPercent` of the drink), then commits; a refusal
(wallet, cup put down, gone) changes and charges nothing. `KitchenService` stops a pour at 500 ml in the next tick (as released: a
spill, `MarkSpill`) and drops the pour of a player who left at once without a charge (`DropLeftPours`). The release needs no reach.
A top-up of the same drink adds its ml (exact ml kept server-side in `_cupMl`, cleared with the item) and pays no second cup. Lid:
free, only on a filled open cup, not while it is under a tap. Cup stack: free (charged with the first pour), capped at
`MaxEmptyCups` 6 empty cups in the world (`NoCupsLeft`). **Spill (for 035):** `KitchenState.MarkSpill/HasSpill/ClearSpill(station)`,
bit 3 of the dispenser's byte; it stays until 035 clears it (nothing else consumes it). New `CookOutcome`s with HUD texts.

**Orders (`Scripts/Npc`).** Half the guests order a drink with their dish, from their own seeded RNG (`CustomerRules.DrinkSeed` 33),
so the 022/032 draws keep their sequence. `QueuedCustomer.Drink`, the bubble ("Falafel wrap / extra garlic / + lemonade"), the rail
(the open drink line in DeepBlue, a served dish in Sage; ticket bits 21–23, minutes now 13 bits). The pure `GuestTab`: the dish and
the drink are handed over on their own, in any order; a line that leaves the other open goes on the tab (the commit point for a
line: `HasBill` makes the counter's fallback never pay it again), the guest says "Thanks! And my lemonade?" and stays in the queue
(patience draining, `KeepOpenOrders` keeps its place in the demand reconcile); the last line combines both into one pick (worst
match, steps short summed, mean stars, bill = the lines' bills) and `RegisterFlow.HandOver` runs exactly as in 032 (pay once, any cook
at the till, no tip when settled by itself). A drink matches with `OrderRules.FitDrink`: the ordered drink at the line with its lid
Exact, low/full/spilled Close, other drink or no lid Wrong; its bill share is the 022 share. A guest that leaves with a line open
(patience, closing, changed its mind, gone) pays what it got once, no tip, to the cook who handed it (`PayOpenTab`). Interpretation:
the payee of a two-line order is whoever handed the last line. The tour scripts the falafel wrap guest a lemonade.

**Client.** `InteractionRules`: taps `Fill` ("Hold · fill lemonade"; grey "Take a cup first", "That cup has another drink", "It has
its lid", "The cup is full", "Someone is using that tap"), cup stack `Take` ("Click · take a cup"), lid stack `Lid`; a cup goes into
no cookware; the counter sells a cup only lidded ("Put a lid on it first"). `PickupInteractor` reuses `SqueezeHold` for the taps (the
option by the station's kind; views through the new `IPourView`); first person holds left click, third person holds E through the
0.2 s `SqueezeHoldGate` (nearest tap; a tap of E takes a cup or lids). Predicted from the press frame: the held cup eases under the tap
(`HeldItemView.SetPourPose`, 0.14 s ease-out, and back), the lever goes down, the stream runs into the cup, the level rises at
200 ml/s (`FoodVisual.PredictMl`, no rebuild), the splash and puddle the moment it runs over; the predicted level stays while the
server's bucket agrees (else back to the bucket's; a refused start or stop takes it out with "nope", an unconfirmed one goes after
3 s). The lid shows and clicks at the click (others hear the click on the look change). Others see the stream and the lever from the
byte, the cup's level from its bucket; the cup in the pourer's hand stays in the hand for them (follow-up). Cup look: Kenney models
did not fit (the Food Kit `cup` is a mug with a handle, `soda-glass` a purple cocktail glass with a straw and a lemon), so the cup is
a graybox: an open, tapered Cream paper cup of 12 slabs with its fill line printed round it (0.18 m, the guide row "Takeaway cup
(400 ml)"); liquid disc in the drink's colour, Cream lid. Sounds: `Pour` (loop), `LidClick`, `Splash`. No per-frame allocations in
the new views.

**Layout.** The dispenser stands on the front counter's left run (x = serving counter − 1.15 m, mirror of the register), on the counter
top, its taps facing the cook; across the aisle from the assembly board. Graybox `Art_StationDrinkDispenser` (0.84 × 0.42 m, 0.56 m
high; guide row "Drink dispenser"). `FloorPlanTests`: the register and the dispenser are front-counter stations (not in the aisle
start), checked on a run's top, beside the serving counter and the register; new test: within a step of the serving point and of the
assembly board, taps facing the aisle in order, every tool in reach, no colliders on tools, the views and the byte.

**Scope beyond the listed folders** (as in 029–032): `sim/Runtime/Economy`, `Scripts/Economy` (TryBuyFill), `Scripts/Net` (rules,
aim, interactor), `Scripts/Player/HeldItemView`, `Scripts/UI` (HUD texts, rail), `Scripts/Dev` (tour), `Editor` (catalog, scene),
`docs/art-direction.md` (two rows), `scripts/visual-check.sh` (cook timeout 1260 → 1320 s). Fixtures updated for the new station:
ArtCatalogTests, ScaleSceneTests, CookingRulesTests, FloorPlanTests, TourRulesTests (`drinks-first` look shot), WhatsNewTests (032 no
longer newest), sim catalog counts.

**Tests.** `scripts/verify.sh` PASS: 1017 EditMode (baseline 991), 314 sim (baseline 292). New sim: `DrinkTests` (22: buckets,
the identity being the bucket, rating, lid, a fill only of a drink, cups in no other op, names, the content check, determinism, the
pour's price and the wholesaler's cap, the order's drink draw). New EditMode: `DrinkStationTests` (24: press/release by ticks and
charged once, a repeated press keeping its start, the stop at 500 ml with a spill and a free late release, a top-up without a second
cup, refusals charging nothing (wallet, cup put down, another player, two taps), the press's cup checks, a player who left, the lid,
an open cup not sold, reach, the cup cap, options, the byte, the cup's look, the rules' actions and prompts, `GuestTab` both orders and
the open bill, `FitDrink`, the queue's pick, the reconcile keeping a waiting guest, the ticket, one payment for a whole order), the
`FloorPlanTests` dispenser test, the spec 033 What's-new test. All pinned hashes and ratings unchanged.

**Visual check (AC4).** `bash scripts/visual-check.sh --tours look,cook`: PASS (cook: 647 images, timeouts 0, aim fallbacks 0).
- `.verify/visual/look/sheet.png` cell 56 `drinks-first`: the dispenser from the aisle (three coloured taps, the cup stack, the lid
  stack, "Take a cup first").
- `.verify/visual/cook/sheet.png` cells 255–314: `drinks-dispenser` 255, `cup-take-*` 257–266 (the top cup flies into the hand),
  `cup-in-hand` 267, `lemonade-aim` 268 ("Hold · fill lemonade"), `lemonade-pour-01..22` 269–290 (the cup eases under the tap, the
  lever goes down, the stream, the lemonade rising to the line; log: "held the tap 1.80 s (drawn level 0.90 …) the cup is 'cup of
  lemonade', level 2"), `lemonade-filled` 291 (at the line, the fill line printed on the cup), `lid-aim` 292 ("Click · put a lid on
  it"), `lid-01..10` 293–302 (the lid on at the click), `lemonade-held-level` 303 (the lidded cup whole in the lower right; `[Hold]
  lemonade … drawn x1.00`), `lemonade-served-*` 305–314 ("drink order: exact order"; the guest walks to the register), then
  `wrap-handoff-*` 326– and the cash payment. Host log: "guest 47 took its Falafel wrap (exact order, line bill $1,525); the rest of its
  order is still open", "poured lemonade: 360 ml ($89)", "handed Falafel wrap over: … bill $1,843 + tip $414", "guest 47 paid $2,257
  of $2,257" (once).
- `.verify/visual/cook-observer/sheet.png`: unchanged parts (the observer films the board; no drink frames).

**What still looks or feels off (follow-ups).**
- Others see the stream and the lever, but the pourer's cup stays in its hand for them (not under the tap) and its level only jumps
  at the release (the byte names the tap, not the player).
- The cup is a 12-slab graybox (the Food Kit has no takeaway cup); its sides read a little stripy under the kitchen light.
- The hand-off parcel is the dish's only (the guest walks off without a cup in its hand).
- The tap labels sit on the coloured panels just above the levers and touch them; "ICED TEA" is half hidden behind its lever when
  that tap is highlighted.
- A pour costs the cup ($35) plus up to $60 of drink against a drink market of $50–250 and bills of $300+ for the line: prices are
  spec 037 ("drink 3" not applied, as the dish prices in 031).
- A guest that leaves with a line open (e.g. at closing) pays what it got and counts as a sale and a miss in the shift numbers.
- The payee of a two-line order is whoever handed the last line (any cook may do either line).

## Blockers
(Builder fills this in after 3 failed attempts.)
