---
id: 032
title: Orders 2.0 (order at the counter, modifiers, patience, register, tips, reviews)
status: done
owner: architect
builder: opus
---

# 032: Orders 2.0

## Goal
Roadmap pillar 1, "orders you can read". A guest walks up and says what they want, and a ticket prints with the
exact build, for example "Falafel wrap, extra garlic, no onion". The guest waits with visible patience. You hand the
food over, take payment at the register (cash with change, or card), and get a tip for speed and accuracy. Guests
leave a short review. This turns cooking into a shop. Approved by the orchestrator under the human's 24 h grant.

## Depends on
022 (shift, orders, ticket rail), 027c (serve reward), 027d (order bubbles), 031 (recipes as layers, packaging).

## Scope
- `sim/Runtime/Economy` or `Cooking` (pure):
  - an order model: dish, modifiers (add, remove, extra), packaging (eat in or take away);
  - order matching per layer with modifiers;
  - patience model (a per-guest base plus a dish complexity term, deterministic from the seed);
  - tip formula (speed × accuracy × quality);
  - review score and text keys (the text itself comes later from the AI gateway; here it is template text).
- `game/Assets/_Project/Scripts/Npc/`:
  - the guest says the order (a speech bubble with an icon list);
  - patience shown as a small ring or bar above the guest that turns Terracotta near the end;
  - a guest leaves when patience runs out (missed);
  - the ticket rail shows modifiers in clear words ("no onion" struck through, "+ extra garlic").
- `game/Assets/_Project/Scripts/Cooking/` or a new `Shop/` folder: a register station on the counter.
  - **Cash:** the guest hands a note. You click coins and notes in the tray to give change, and the server validates.
  - **Card:** you click the terminal.
  - Wrong change costs the difference and some satisfaction.
  - Use existing RPCs only (`RequestStation` with the option as tray slot or terminal). No new RPCs.
- `Scripts/UI/`: a shift summary that adds tips, reviews (1–5 stars with a short line), and missed orders by reason.
- Tests, visual check (the tour takes a wrap order with one modifier, builds it, serves it, takes cash with change),
  `WhatsNew.json`, this spec.

## Out of scope
- AI-generated review text (the AI gateway spec later; here, template lines).
- Delivery, tables, buffets (docs/ideas.md).
- Price setting by the player (037).

## Design notes
- Modifiers come only from a recipe's allowed list:
  - "no X" for a removable salad or sauce layer;
  - "extra X" for sauces and fillings;
  - at most 2 per order at the start of the game.
- Patience (default): 60 s plus 15 s per layer, shown as the ring. The guest's mood changes the review, not the tip
  formula.
- Tip: up to 30 % of the price. It falls with time past half the patience, with modifier mistakes, and with low
  quality.
- Feel:
  - the register is one click per coin with a satisfying clink;
  - card payment is a single click with a beep;
  - the guest takes the food with a small hand animation and a thank-you bubble.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: order generation from seed, modifier matching, patience, tip, review score, determinism.
- [ ] AC3: Server tests: the register validates change, card payment, wrong change penalty, a guest leaves on
      patience timeout.
- [ ] AC4: Visual check: the ticket with modifiers is readable; the patience ring drains; cash with change; the
      review shows in the summary.
- [ ] AC5: `WhatsNew.json` entry `spec-032`.

## Defaults to review
Patience 60 s + 15 s per layer; 2 modifiers maximum; a 30 % tip cap; a 50/50 cash/card split; coin and note values
(5, 10, 20, 50 notes; 1, 2 coins).

## Implementation notes
Builder (Opus), spec 032 (and the 031b review fixes the orchestrator added). No new RPC and no new RPC parameter: `RpcSurfaceTests`
unchanged. New synced fields, each small and presentation only (pinned in `OrdersTwoTests.TheSyncedSurface_...`):
- `Customer.Patience` (byte): the patience ring; the server owns the clock, sent only on change, at most 4/s per guest, never while paying.
- `Register.Payment` (ushort: nobody / card / cash + change due) and `Register.Tray` (byte: the change built so far, at most $255): the
  register's display, its coins and the prompts must show the payment to the cook and to onlookers; nothing else carries it.
- `TicketRailView.LiveReport` / `LastReport` (`ShiftReport`: ints, shorts, bytes): the summary's tips, reviews, missed by reason (one
  struct SyncVar each, like 027c's `ServeReward`). The ticket's modifiers ride in the unused top byte of the existing ticket int.

**Sim (pure, seeded, deterministic).** `Cooking/OrderModifiers` (+ `OrderModifier`, `ModifierKind`, `ModifierCheck`): the allowed list comes
only from the recipe template ("no X" for a required salad or sauce layer, "extra X" for a required sauce or filling layer); draw 50 % none,
30 % one, 20 % two, never "no" and "extra" on one layer; packed as two 4-bit indices in one byte; `ForOrder` (a removed layer is no longer
required, an extra sauce accepts lots: new optional `TemplateLayer.SauceAmounts`, null for every default template, so every 031 score,
hash and rating is unchanged; pinned tests pass); `Check` = the 031 template score on the order's template + modifier mistakes ("no X":
no layer contains X; extra sauce: lots or twice; extra filling: two prepared layers). `Economy/GuestOrders` (+ `GuestOrder`): modifiers,
patience base (54-66 s), card or cash (50/50), the note size (10/20/50), always in that draw order. `GuestPatience` (60 s + 15 s per
layer), `TipRules` (≤ 30 % of the bill: speed full to half the patience then linear to 0, accuracy 1 / 0.5 / 0 by steps short, quality
by stars 0..1; integer permille math), `ReviewRules` (+ `GuestReview`, `ReviewKey`: the order's stars, −1 slow past 75 %, −1 one step
short, −1 wrong change, a wrong dish at most 2; template line per key, `review.*` keys for the gateway later), `CashRules` (tray 1, 2, 5,
10, 20, 50; guest hands the due rounded up to its notes; `CashPayout` = due − |given − due change|).

**Order match (game, extends 031).** `OrderRules.Fit(item, dish, modifiers)`: a build is checked per layer with the modifiers (Exact 0
steps short, Close 1, Wrong more; layer order and amount only cost score), its quality is the order's; other dishes match as in 022 (they
have no modifiers). `CustomerQueue.TryPick` gets the whole entry (dish + modifiers). The bill = the sim's demand payout × the match share
(100/60/25 %, the 022 star tip is gone: the tip is `TipRules`) × (500 + q_order)/(500 + q_item), so a wrap the guest wanted without
lettuce is paid as the order, not as an incomplete recipe (the sim's own rating of the item is unchanged).

**Server flow.** Serving a guest: `TownEconomy.TrySellForGuest` (the sim sells as always; a sale to NPC demand credits nothing yet),
`CustomerService.OnHandedOver` (bill, tip, the guest's note; it leaves the queue and the patience book, walks to the pay spot in front of
the register, bubble "Thanks! Here's $400." / "Thanks! Card, please."). The register is `StationKind.Register` (appended, station index 9)
with nine slots as `RequestStation` options: tray 0-5, take back 6, hand over 7, card terminal 8. The pure `Shop/RegisterDesk` behind
`IRegisterWallet` (031b pattern): a tray click adds to the server's change, take back empties it, hand over / the terminal settle once
(the payment leaves the line); refusals (NoPayment, PaysByCard, PaysCash, TrayFull, TrayEmpty, the wallet's) change and
pay nothing. Wrong change either way costs the difference and a review star. Any cook works the head payment, the money goes to
the payee (who handed the food over); the kitchen closing, the payee leaving or 30 s untouched settle it by itself (the bill, no tip;
032 review). On settling: the review, the shift's
numbers, a feed line, the reward moment over the register (stars = the review, paid, tip), the guest leaves with its review bubble.
Patience: the pure `Npc/PatienceBook` drains only for queued guests standing at their slot; out of patience they leave ("I can't wait any
longer...", missed: Patience). Missed reasons: Patience, Gone (the 014 demand reconcile, "Never mind..."), Closed.
If the hand-over fails after the sim sale (cannot happen in one frame), the bill is paid at once so no money is lost.

**Client.** `PatienceRingView` (ring beside the head at 1.72 m, under the rail's board: Sage, Mustard below half, Terracotta and a pulse in the last quarter; mesh rebuilt
only on a byte change). `GuestHandoffView`: on the switch to paying, a parcel (wrap paper / box / bag by dish, catalog models) flies from
the serving counter into the guest's right hand on a 0.35 s ease-out arc while the hold pose raises the arm; it keeps it out of the shop.
`RegisterView`: display "CHANGE $8 / $18" (Terracotta when over), "CARD", "PAID"/"APPROVED"; the change lies as coins and notes on the
mat; the clicking player's coin shows at the click (`TrayPrediction`, refusal → back out with "nope"), others see the byte rise with the
clink; card beep (procedural `Clink`, `Beep`). Prompts "Click · give $5", "Click · hand over $18", "Click · card payment", grey reasons
("They pay by card", "Nobody is paying"). Third person: E at the register takes a card payment (counting change is first person).
Ticket rail cards: the dish on top, two modifier lines ("+ extra garlic" Leaf, "no lettuce" Terracotta with an Ink line through it,
measured from the text's bounds), the minutes at the bottom. Shift summary (F3): tips, reviews (average, count, the last three lines),
missed by reason. No per-frame allocations in the new views (texts only change on input changes).

**Scene and art.** `Art_StationRegister` (graybox in the catalog, `RegisterArtRules`, palette only; a guide row added to
docs/art-direction.md): on the front counter 1.15 m right of the serving counter, turned to the cook; a `Body` collider like every station
and a sign. The pay spot ("PayHead") in front of it on the guests' line; paying guests queue towards the street. Customer prefab: ring and
hand-off. Fixtures updated for the new station (CookingRulesTests kinds, ArtCatalogTests positions, ScaleSceneTests and FloorPlanTests: the
register stands on the front counter, checked explicitly, not skipped), WhatsNewTests (031b is no longer the newest).

**Tour.** `-tour-orders` (dev builds only, set by visual-check.sh on the cook host): scripted first guests (snack: fries, card; meal:
falafel wrap "extra garlic", cash $50 note; grilled veggies, card; seitan wrap "no lettuce", cash $20, short patience so its ring turns
Terracotta and it leaves), patience × 8 for the rest (the tour is long). The tour squeezes 1.8 s (lots), looks at the rail and the rings,
pays every served guest at the register (card or coin by coin), and shows the summary at the end. Cook timeouts 760 → 960 s (tour),
1080 → 1260 s (script).

**031b review fixes** (orchestrator, merged with 030-032):
1. Third-person E at the board: `SqueezeHoldGate` (pure): a squeeze starts only after 0.2 s held and is measured from then; a tap is
   queued as the normal E action. `ToolAction(held, target)`: a carried build (or a pick-up in flight) gets no tool. The board's nearest
   dispenser, its build spot and view are cached per target (6). Test: a tap never squeezes or charges, a hold squeezes from 0.2 s.
2. A press previews the layer (VesselFull refused; a merge onto the same sauce on top still works) and checks the player can afford a
   10 % portion (`IAssemblyWallet.CanAfford`, `EconomyService.CanAffordPortion`); `SqueezeCheck` refuses a full build ("The build is full").
3. `AssemblyDesk.DropLeftSqueezes` (pure selection + cancel, with `KeyOf` / `BoardOfKey` / `DispenserOfKey`), tested.
4. `PortionPriceTests`: three merged 10 % taps at a $14 bottle ($3 paid, the lots ceiling $4, SurplusCap = paid); the `ItemCost` comment
   says it is a ceiling the −1 in SurplusCap absorbs.
5. `InteractionRules.Fits`: a held build goes into no pan, pot, basket, griddle, cutting board or bowl (an empty plate still takes it).
7. `BuildStackView.EndPrediction(token)` (no unused flag); `BuildStepLook.Expire` runs in LateUpdate (`Seen` is a pure read);
   `EconomyEngine.Debit` shared by TryBuyBase and TryBuyPortion.

**Scope beyond the listed folders** (as in 029-031): `sim/Runtime/Economy` (listed), `Scripts/Economy` (the guest sale hold and pay),
`Scripts/Net` (aim, rules, PickupInteractor), `Scripts/Player` (SqueezeHoldGate, BuildStepLook), `Scripts/Art` (RegisterArtRules,
ArtKey, catalog rules, size row), `Scripts/Audio` (two sounds), `Scripts/Dev` (tour), `Editor` (catalog model, scene), `docs/art-direction.md`
(row), `scripts/visual-check.sh` (`-tour-orders`, timeout).

**Tests.** `scripts/verify.sh` PASS: 984 EditMode (baseline 958), 291 sim (baseline 277). New sim: `OrderModifierTests` (allowed list,
seeded draw, packing, a plain order scores as in 031, no / extra checks, determinism), `GuestOrderTests` (orders from the seed, patience,
tip, cash rules, review), the merged-portion case in `PortionPriceTests`. New EditMode: `OrdersTwoTests` (the register: change built and
paid once, take back and the tray limit, wrong change either way, card, refusals pay nothing, only the payee, settling by itself; the tray
prediction; patience drains only at the slot and never at the register, a guest leaves when it runs out, the ring; the order match with
modifiers on real builds and the queue pick; the bill and the held guest sale; tickets; the shift's numbers; the synced surface),
`RegisterSceneTests`, six 031b review tests in `KitchenPolishTwoTests`, the spec 032 What's-new test.

**Visual check (AC4).** `bash scripts/visual-check.sh --tours look,cook`: PASS (cook: 570 images, timeouts 0, aim fallbacks 0).
- `.verify/visual/cook/sheet.png`: `orders-rail` 135 (every card: dish, "+ extra garlic" in Leaf, "no lettuce" struck through, the
  minutes), `orders-patience` 136 and `orders-later-patience` 418 (the rings drained; the seitan guest's ring Terracotta at 17 %, then
  "a guest ran out of patience" in the log); `wrap-served-01..` 229- (the parcel flies from the counter into the guest's hand, the guest walks off with it);
  `wrap-register` 263, `wrap-give-20-aim` 264, `wrap-coin-20-*` 265- (the note on the mat at the click, "CHANGE $20 / $32"),
  `wrap-handover-aim` 277 ("Click · hand over $32", $20 + $10 + $2 on the mat), `wrap-paid-*` 278- ("PAID", "+$1,868 · tip $343" over the
  register), `wrap-review-bubble` 295 ("★★★★☆ Tasty, I'd come back." over the leaving guest); `fries-card-*` and `veggies-card-*` (card
  payments); `shift-summary` 570 (tips $632, reviews 3.3 (4) with their three lines, missed 11: out of patience 1, changed their mind 5,
  kitchen closed 5). Log: falafel wrap "exact order", bill $1,525 + tip $343, cash $1,900, change $32 of $32.
- `.verify/visual/cook-observer/sheet.png`, `.verify/visual/look/sheet.png`.

**Still off / follow-ups.**
- Bills are large ($1,525 for a wrap: the meal market's price): a cash guest hands "$1,900" in notes. Prices are spec 037.
- "Changed their mind" (the 014 demand reconcile) is the most common miss now that guests have patience; it may be dropped later.
- Order bubbles stand at 2.1 m, behind the ticket rail's board for guests near the serving counter (seen from the kitchen); 022/027d
  layout, not changed here. Guests at the register show their bubble above the view when the cook looks down at the till.
- The rail's cards read dim from below (the board faces the aisle, lit from behind); the text is Ink on Cream.
- The tour's burnt potato is served after 14:00 (no guest pays); the guest it went to before closing was settled by the register.
  Wrong change is not shown in the tour (EditMode covers it).
- Counting change is first person only; third-person E takes only a card payment.
- The hand-off parcel is the wrap paper for wraps, the box for the veggie box, a bag for everything else (fries too).

**032 review fixes** (review of 129af3d; uncommitted round).
1. **Hand-over pays once.** The served guest's flow moved into the pure `Shop/RegisterFlow` (CustomerService only routes; the
   presentation goes through `IGuestStage`, the money through `IRegisterWallet`). `RegisterFlow.HandOver` opens the payment in the
   register's line first (the commit point), then shows it; every presentation step runs on its own and is logged on failure, never
   thrown. `OnHandedOver` returns true once the guest is in line; the counter's fallback pays only when `RegisterFlow.FallbackPays(handed,
   IsPaying(guest))` (not handed and not in line). Test: a hand-over whose presentation throws after the line joined pays once.
2. **Orchestrator decisions.** Any cook works the head payment (`NotYourGuest` is gone; the money still goes to the payee who served).
   Payments that settle by themselves pay the bill only, no tip: closing (`SettleAll`), the payee leaving, the guest's object gone
   (`Exited`), and a new rule: the head untouched for 30 s (`RegisterFlow.Tick`, `IdleSettleSeconds`). `RegisterSettlement.TipPaid`.
3. **No ghost guests.** On every settle path the guest is sent out, the leave first, then the feed and the reward (each on its own); a
   refused self-settle still removes the guest from the line and sends it out; a paying guest whose object is gone settles (bill only).
4. **SurplusCap margin.** A lots portion counts one dollar less (`PriceRules.LotsRoundingMargin`, `PortionCounted`): with any number
   of merged lots layers the item's cost is never above what was spent. Tests: three merged taps at $14 cost exactly what was paid; two
   merged lots layers at $14 (and every bottle price 1-200) never above; the old pin of a lots portion is updated to the new rule;
   "stays below" reworded to "never above".
5. **Shift numbers** record `TipPaid` (after a wrong change the difference comes out of the tip first). Test.
6. **SqueezeHoldGate:** the aim leaving the dispenser before 0.2 s is `Cancelled` (no E action at all); after 0.2 s the squeeze ends. Tests.
7. **Dead code:** `CustomerService.TryPick(plated, stars, ...)`, `TownEconomy.TrySell(..., npcPayPermille)`, `EconomyService.TrySell(...,
   npcPayPermille)`, `OrderRules.PayPermille` / `TipOf` (and their constants) removed; their tests replaced (`GuestShare_...`, the
   guest-sale test in `CustomerOrderTests`) or removed (the 027c `TipOf` test). Only `TrySellForGuest` / `TryPayGuest` adjust balances
   outside the engine (`AdjustBalance`). `_squeezers` is keyed with `AssemblyDesk.KeyOf`.
8. **Flow tests** (`OrdersTwoTests`, through `RegisterFlow` with a fake wallet and a recording stage): the hand-over fallback, the payee
   leaving, settling at closing (no tip), payment then the guest leaving (leave before feed and reward, a failing feed keeps nothing),
   a paying guest's object gone, the 30 s idle settle, a refused self-settle, the tip after a wrong change, any cook settling another's guest.
9. `ReviewRules.SlowPermille` (750, integer math; `GuestPatience.SlowShare` removed).

After this round `scripts/verify.sh` PASS: 991 EditMode (was 984), 292 sim (was 291). Visual check: cook ok (570 images, timeouts
0, aim fallbacks 0, the same payments, rail and rings as before); the look tour wrote its sheet (58 images, timeouts 0) but its client
exited 139 after `[Tour] done`, in 4 of the last 6 runs (3 in a row). The Windows minidumps (`%LOCALAPPDATA%\CrashDumps\Worktown.exe.*.dmp`,
11-09) all fault in `D3D12Core.dll` +0x264831 (access violation) during the player's shutdown, after FishNet stopped; an older dump
(11-07, before 032) is a breakpoint in UnityPlayer.dll. No managed stack, no Exception line. It looks like a D3D12 teardown crash
(perhaps the PSO cache `dx12_pso_cache_lib.bin`, rewritten at each shutdown), not a game rule; trying `-force-d3d11` for the tours, or
clearing that cache, is a decision for the orchestrator.

## Blockers
(Builder fills this in after 3 failed attempts.)
