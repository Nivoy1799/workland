---
id: 035
title: Cleaning (dirty dishes, sink, trash, spills, hygiene score)
status: approved
owner: architect
builder: opus
---

# 035: Cleaning and hygiene

## Goal
Roadmap pillar 5: a clean shop. Plates and bowls come back dirty from eat-in guests. The sink washes them with a
short hold, so clean plates run out if nobody washes. Burnt food and food scraps go into the trash, which fills and
must be taken out. Spills (spec 033's drink overfill) need a wipe with a cloth. A visible **hygiene score**
(0–100 %) drops with dirt, and guests notice it in reviews and patience. Approved by the orchestrator under the
human's 24 h grant.

## Depends on
027b (feel, hold actions), 031b (hold gate), 032 (reviews, patience, eat-in vs take-away), 033 (spill flag:
`KitchenState.MarkSpill/HasSpill/ClearSpill`), 034 (stock and refund backstop, the plate and bowl counts).

## Scope
- `sim/Runtime/Economy` or a new `Shop` module (pure): the hygiene model.
  - Dirt sources: dirty dishes left out, a full trash can, an open spill, burnt smell.
  - Decay and recovery per game minute.
  - The effect on review stars and patience (small: −1 star below 50 %, patience −20 % below 30 %).
  - Deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/` and `Shop/`:
  - eat-in plates and bowls come back dirty to a return shelf (a station);
  - the sink station: put a dirty dish in, hold to scrub (server-timed like the dispensers, 1.5 s), and it goes to
    the clean plate shelf;
  - the trash can station: a "discard" action (food scraps, burnt food, empty packaging), a fill level, and "take
    out the trash" (carry the bag to the bin outside the staff gap);
  - a wipe cloth tool (a free item like the knife) for the spill flag; holding it on the spill clears it.
  - No new RPCs. New presentation bytes only where needed (pinned).
- `Scripts/Art/`:
  - dirty-plate looks (a smudge tint);
  - the sink with water and bubbles;
  - the trash fill level;
  - the bin bag;
  - spill puddles and a wipe animation;
  - the hygiene meter in the HUD (a small icon plus %).
- `Scripts/Audio/`: scrub, splash, trash rustle, a lid thump.
- Layout: the sink and dish return near the plates (the end of the line), and the trash at the staff gap.
  FloorPlanTests stay green.
- Tests, visual check (a dirty plate comes back, gets washed, the trash fills and is taken out, a spill is wiped,
  the hygiene meter moves), `WhatsNew.json`, this spec.

## Out of scope
- Health inspections and fines. Pests. Floor mopping beyond spills.

## Design notes
- The plate count is finite (from 034's stock: plates are stock). Without washing, plates run out.
- Scrub 1.5 s per dish (server-timed hold). The trash holds 12 items, and a bag is a carryable pickup.
- Hygiene starts at 100 %. Each dirty dish left more than 2 game minutes is −1 %/min, a full trash −2 %/min, an open
  spill −2 %/min. Recovery is +1 %/min when nothing is dirty.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: the hygiene model (sources, decay, recovery, effects), determinism.
- [ ] AC3: Server tests:
  - dishes return dirty;
  - the sink scrub is server-timed (the re-press rule as in 031b);
  - discard;
  - the trash fills and the bag is taken out;
  - the wipe clears a spill;
  - hygiene drives the review and patience modifiers.
- [ ] AC4: Visual check: the full loop above.
- [ ] AC5: `WhatsNew.json` entry `spec-035`.

## Defaults to review
All numbers in Design notes; the eat-in share (half of the guests eat in).

## Implementation notes
Builder (Opus), spec 035, with the 034 review fixes (below). No new RPC and no new RPC parameter (`RpcSurfaceTests` unchanged): every
request is the existing `RequestStation(station, option)`. New synced fields, presentation only, pinned in
`CleaningTests.TheSyncedSurface_...` (and `OrdersTwoTests.TheSyncedSurface_...` for the guest):
- `Shop.ShopHygiene.Percent` (`SyncVar<byte>`, a global scene object like the stock counts): the HUD meter.
- `Npc.Customer.EatIn` (`SyncVar<bool>`, set before the spawn): the hand-off shows the plate instead of a parcel.
- The new stations' bytes are the existing `StationContents.StoveFlags` (dish return: dirty count; sink: basin count in bits 0-2 and the
  scrubbing bit 3; trash can: fill; plate shelf: clean plates). The drink dispenser's byte gets a wiping bit (bit 4, `CleaningRules.WipingBit`).

**Sim (`sim/Runtime/Economy`, pure, the injected clock is the economy's game minute).** `HygieneSources` (dirty dishes, overdue dishes,
trash full, spills, burning), `HygieneRules` (the spec's numbers; `Step`, `Advance`, `OverdueDishes`, `ReviewPenalty`, `PatiencePermille`,
`Patience`, `TrashCapacity` 12), `HygieneMeter` (immutable: percent and minute; `Advance`). `ReviewRules.Review(..., hygienePercent)` and a
new appended `ReviewKey.Dirty` ("Tasty, but the place was dirty."; `GuestReview.Unpack` accepts it). `GuestOrders.DrawEatIn` (50 %).
Defaults of my own (the spec names the source, not the number), to review: burning costs 1 %/min; recovery needs nothing dirty at all
(a fresh dirty dish stops it); a clock jump (the night skipped after a shift) counts at most 10 minutes; a wipe takes 1.0 s; the sink holds
3 dishes; an eat-in guest's plate comes back 20 game minutes after the hand-over.

**Plates as a finite resource.** Spec 034 made no stock line of plates (its 24 lines: goods, packaging, cups, kegs), so the plates are
counted by the new pure `Shop.DishRoom`: the shop owns `CleaningRules.TotalPlates` = 8 (the old `VesselRules.MaxPlatesInWorld`): 2 lie on
the plating worktop at the start, 6 clean on the shelf. The shelf gives one only while it has one (`NoCleanPlates`, "No clean plates: wash
some at the sink"; the shelf's stack shows the count). A plate sold with its meal: an eat-in guest's comes back dirty to the dish return
after the meal; a take-away guest's (the meal went into its parcel) and a wholesaler sale's are clean again at once; a plate that falls
out of the world turns up on the shelf. Invariant (tested): clean + in use + eating + at the return + carried + in the sink = 8. Bowls are
never sold (only plates go to the counter), so no bowl comes back dirty.

**Eat-in versus take-away.** The 032 order model had no packaging choice (every guest left with a parcel), so it is added: each guest
draws "for here" or "to go" from its own seeded RNG (`CustomerRules.EatInSeed` 35, one draw per guest, so the 022/032/033 draws keep
their sequence); the bubble says "· for here" / "· to go". An eat-in guest handed a plate dish takes the plate (`HandoffRules.ParcelOf(dish,
eatIn)`), builds stay in their paper, box or bag either way. The tour's scripted fries and grilled-veggies guests eat in.

**Server (`Scripts/Shop`, `Scripts/Cooking`).**
- `Shop.CleaningRoom` (pure): the `DishRoom`, the trash (fill, the bags out, by object id), the timed holds, the meter. Holds: a press
  records (owner, start tick) per (station, kind); the same player's re-press keeps its start (031b 10.0); another player is
  `DispenserBusy`; one hold per player; the release is done at >= 1.5 s (scrub) / 1.0 s (wipe), else nothing; `DueHolds` is the auto-stop;
  `DropLeft` frees a player who left.
- `Shop.CleaningService` (MonoBehaviour next to the NetworkManager): answers the cleaning stations (KitchenService routes their options and
  the dispenser's wipe options; the scrub's and the wipe's releases need no reach), spawns the dirty dish and the bag into the hands (with
  the undo backstop), the eat-in returns, the auto-stop, players who left, lost dishes (back to the return) and bags (back into the can),
  the meter every frame from `EconomyService.GameMinute`, the bytes. A new economy (`EconomyService.Game`) is a new game; a server stop keeps
  the game (carried dishes and bags come back).
- `KitchenService` implements `Shop.ICleaningKitchen`: the spill count, the burning smell (a heat vessel in the Burnt zone, counted while
  the heat bytes are published), the discard (held food is removed and despawned; held cookware's food is cleared and the cookware
  stays), the cloth, `ClearSpill`. The plate shelf asks the cleaning service; `Serve` reports the plate served (eat-in from
  `CustomerService.EatsIn`); a lost plate is reported. The cloth is a home-spot tool like the knife (`_clothPrefab`).
- **Anti-faucet:** a discard calls no wallet and no stock (the room holds neither; tested): nothing is refunded or created; an empty cup
  thrown away frees its `MaxEmptyCups` place, but a new cup still comes from the cup stack's stock.
- Hygiene in the 032 rules: `RegisterFlow.HygienePercent` (set by CustomerService every frame) goes into every review, the open-tab review
  too; a new guest's patience is `CustomerRules.PatienceFor` (the 032 base and layers, the 033 drink, the tour scale, x0.8 below 30 %).

**Client.** New actions `Discard`, `Wash`, `Scrub` (hold), `Wipe` (hold); `Held.DirtyDish/TrashBag/Cloth`; `AimTarget.CleaningStation` with
the byte and whether it is known (no cleaning service: the plate shelf gives plates as before). The scrub and the wipe reuse the 031
`SqueezeHold` (the sink's basin and the dispenser's new spill slot are the held tools; `HoldOption` maps the press and release per station),
third person through the 0.2 s `SqueezeHoldGate` (at a dispenser with the cloth in the hands E is held on the spill). Predicted from the
click: the dirty plate (and the bag) fly into the hands (the 028b take copy; the take now confirms on a dirty dish or bag), the plate drops
into the sink with a splash, food flies into the can (its lid swings and thumps), the bag into the bin; the scrub (plate wobbling, bubbles
racing, the scrub loop) and the wipe (the held cloth goes to the puddle and circles in first person; the puddle shrinks and is gone at
1.0 s) start at the press. The views own the predicted counts (`SeenCount`, `SeenByte`, `SeenFill`), so a second click never waits for the
server. Onlookers see it all from the bytes (the scrubbing bit, the wiping bit with the view's own cloth). Looks: a smudged plate (WoodDark
and dark-sauce smudges on the catalog plate) at the return, in the hands and in the water; the sink's water, plates and up to 8 bubbles;
the trash can's bag rising with the fill, its rim Terracotta when full; the wheelie bin's lid; the HUD meter (a drop, a bar and
"Hygiene 92 %", Sage, Mustard below 50, Terracotta below 30; rebuilt only on a byte change). Sounds through `SoundRules`/`SoundCatalog`:
`Scrub` (loop), `TrashRustle`, `LidThump` (procedural), the 033 `Splash`. No per-frame allocations in the new views.

**Layout.** The sink (1.2 m, worktop high) stands right after the plate shelf at the end of the line, where the bowl's worktop was: that
place surface is now the sink's drainboard with the same index (so no surface index moved), the bowl lies on the pass worktop, the cloth's
home is on the drainboard. The dish return (a 0.6 m rack) stands on the front counter right of the register, where paying guests stand,
1.1 m along the aisle from the sink. The trash can stands at the front counter's west end by the staff gap (the run starts 0.56 m later),
out of the aisle. The bin stands outside the staff gap (x -2.0): out of reach from the aisle, a step out of the gap reaches it.
`FloorPlanTests`: the stations are classified (front counter, outside) and a new test checks all of the above; the dispenser's spill slot.

**Scope beyond the listed folders** (as in 029-034): `sim/Runtime/Economy`, `Scripts/Npc` (eat-in, patience, the tour script),
`Scripts/Net` (rules, aim, interactor), `Scripts/Economy` (`Game`, `RefundAccount`), `Scripts/UI` (the meter, HUD texts), `Scripts/Dev`
(tour), `Editor` (catalog models, scene, prefabs), `docs/art-direction.md` (five rows), `scripts/visual-check.sh` (cook timeout 1440 ->
1620 s). Fixtures updated (spec-mandated): ArtCatalogTests and ScaleSceneTests (the four stations), CookingRulesTests (kinds),
KitchenPolishTests / PlaceSceneTests / ScaleSceneTests (4 worktops: the fifth is the sink's drainboard), OrdersTwoTests (the guest's
`EatIn`), WhatsNewTests (034 no longer newest), PrefabRegistrationTests (the cleaning service's prefabs, the cloth), StockSupplyTests (an
unstocked good is no longer free). New DefaultPrefabObjects entries: DirtyDish, TrashBag, WipeCloth (appended; the refresh's reorder reverted).

**034 review fixes.**
1. Boxes on one spot: `StockRoom.FreeSpot(occupied, usedThisPass)` (pure); `SupplyService.SpawnWaitingBoxes` probes once per pass and marks
   the spots it used. Test: a 3-pack order lands on spots 0, 1, 2, a fourth waits.
2. Last drops: `ShopStock.TryDraw(good, percent, minPercent)` refuses (OutOfStock, nothing changes) below the minimum; `StockWallet` draws
   all or nothing (minimum = the share), so a lots squeeze never comes out of 20 % and a full cup never out of 1 %; the old two-argument draw
   keeps its last drops. `PriceRules.StockCost` comment fixed. The dead second loop in `TryDraw` removed. Tests (sim and EditMode).
3. A server stop: `StockRoom.EndSession(refund)` refunds every order still on its way to its payer (`EconomyService.RefundAccount`, the
   payer may be gone), keeps the stock, and puts the boxes lying in the shop back at the back door; the starting stock is tied to a new
   economy (`EconomyService.Game`), not to a server start. Test.
4. `StockWallet`: an unstocked good is OutOfStock (never free); test pins every board sauce, packaging and `Drinks.All` id (and the cups) to
   a stock line. The 034 assertion "a good the shop does not stock is free" is inverted accordingly.
5. Lost boxes (corrected in the 036 round: `ForgetBox` was never called; both `ForgetBox` methods are deleted): `SupplyService.WatchLostBoxes` (every 0.25 s) returns a box below the world or despawned without its unpack to the
   back door with its contents (`StockRoom.ReturnLostBox`, tested); an emptied box is forgotten as before.
6. `KitchenService.StopPour`: when the pour is already gone (`CancelPoursOfCup`), its `_pourers` key goes and the dispenser byte is refreshed.
7. `EconomyEngine.Refund` is bounded by what the account paid: new `Account.GrocerPaid` (init-only, not in the snapshot hash) rises in
   `Debit` and falls with a refund. Test: two payers, a refund above one's share is refused.
8. Cleanups: `StockTake.Pay` reads the line from `_baseId`; `KitchenState.TryRemove` goes through `Forget`; `StockWallet` resets `_taken` /
   `_drawn` per charge.
9. Tests (`StockReviewTests`, sim): the wholesale cap for a falafel wrap wrapped, bagged and boxed; the $32 floor for wrap paper, the bag
   and the cup; the last drops; the per-account refund.

**Tests.** `scripts/verify.sh` PASS: EditMode 1067 (baseline 1045): `CleaningTests` (20), the FloorPlan cleaning test, the spec 035
What's-new test. Sim 341 (baseline 328): `HygieneTests` (9), `StockReviewTests` (4).

**Visual check (AC4).** `bash scripts/visual-check.sh --tours look,cook`: PASS (look 61 images, cook 859 images; timeouts 0, aim fallbacks
0). The cook tour's host scripts the fries and grilled-veggies guests to eat in and starts the trash at 11 (dev builds only); the new
`CookSequence.CleanUp` runs after the delivery is unpacked. `.verify/visual/cook/sheet.png`:
- 198 `dish-return` (the fries plate back dirty on the rack, "Click · take a dirty plate (1 waiting)"), 199-209 `dirty-take-*`, 210
  `dirty-in-hand` (smudged plate lower right, HUD "Dirty plate").
- 211-221 `wash-*` (into the water), 222 `dish-in-sink`, 223 `scrub-aim` ("Hold · scrub the plate"), 224-244 `scrub-*` (plate wobbling,
  bubbles), 245 `scrubbed`. Log: scrubbed for 1.80 s, clean plates 6 -> 7, the sink byte 1 -> 0.
- 246 `spill` (an overfilled cup, the puddle, "Take the cloth to wipe the spill").
- 258 `trash-full` (the lid up, the trash at the rim, the rim Terracotta, "Click · take the trash bag out (12/12)"), 259-269 `bag-take-*`,
  270 `bag-in-hand`, 271 `hygiene-low`, 272-282 `bin-*` (out through the staff gap to the bin; feed "Player 1 took the trash out"), 283
  `trash-out`.
- 284 `wipe-aim` ("Hold · wipe the spill"), 285-300 `wipe-*` (the held cloth circling on the shrinking puddle), 301 `wiped`, 302
  `hygiene-climbs`.
- The meter: 100 % at the start, 86 % with the spill and the full trash, 68 % when the bag was out, 49 % (Mustard) when the spill was
  wiped (the walk to the cloth), 58 % six seconds later (Sage again at 50).
`.verify/visual/look/sheet.png` 58 `back-door-first`: the trash can by the staff gap, the bin outside, the HUD meter.

**Still off / follow-ups.**
- A game minute is one real second, so the spec's rates are harsh in play: one open spill costs 2 % a second, a dirty plate left out
  from 2 s on 1 % a second, and recovery is 1 % a second. In the tour a single spill took the score from 100 to 49 % in about 40 s.
  The rates are the spec's (Defaults to review); per game hour, or a slower clock for hygiene, is the human's call.
- Eat-in guests leave with the plate in their hand (no seats in the shop); the plate turns up dirty at the dish return 20 game minutes
  (20 s) later.
- The trash can's flip lid stands open at rest (so the fill reads); a discard swings it down with the thump. The bag in the can is a box
  of Charcoal that reads olive under the kitchen light, and the copy that flies into the hands is that box (the carried bag is a sack).
- Third person: the own wipe draws the view's cloth on the puddle (the held cloth stays at the hold point); onlookers see the same cloth.
- The bin outside has no byte: onlookers see the bag vanish (the rustle plays only for the thrower); its lid's flip is the thrower's.
- Dirty dishes are only plates (bowls are never sold); a lost dirty dish returns to the dish return, a lost bag's trash to the can.

## Blockers
(Builder fills this in after 3 failed attempts.)
