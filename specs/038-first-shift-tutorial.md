---
id: 038
title: First-shift tutorial (guided steps through every station, skippable)
status: done
owner: architect
builder: opus
---

# 038: First-shift tutorial

## Goal
The kitchen now has stock, prep, a griddle, a fryer, stoves, a spit, assembly, drinks, a register, cleaning and
ordering. A new player (and the human, play-testing after a long autonomous run) needs a calm, guided first shift.
The tutorial shows one step at a time:
- a short instruction ("Take a potato from the shelf");
- a highlight on the target station or slot;
- the step completes when the player actually does it, as detected from the real game state;
- it can be skipped at any time and replayed from the menu.

It teaches by doing, not by text walls (docs/feel.md 8). Spec 037 (pricing) comes later because it waits for the
human's answer on the price scale. Approved by the orchestrator under the human's 24 h grant.

## Depends on
Everything from 027a to 036 (stations, prompts, highlights, calm view, orders, register, cleaning, stock). It uses the
existing highlight (027b) and prompt system. The steps are client-side guidance only.

## Scope
- `game/Assets/_Project/Scripts/Tutorial/` (new):
  - `TutorialSteps` (pure: an ordered list of steps, each with a goal, a target selector and a done-predicate over
    an observable snapshot of the local player's state: held item, aimed target, last outcome, station bytes);
  - `TutorialRunner` (a MonoBehaviour, owner client only);
  - `TutorialPanelView` (UI Toolkit: a small card at the top centre with the step text, a progress "3 / 14", and
    "Skip tutorial");
  - a persistent "done" flag in PlayerPrefs.
- The steps of the default first shift:
  1. move and look;
  2. take a potato from the shelf;
  3. cut it into sticks on the board;
  4. fry the sticks (load the basket, lower, shake, raise);
  5. tip the fries into a box;
  6. take a wrap and build a falafel wrap (fryer falafel, lettuce, sauce hold);
  7. fold and wrap it;
  8. serve a guest at the counter;
  9. take the payment at the register (count change);
  10. fill a lemonade and lid it;
  11. wash a dirty plate;
  12. order stock at the terminal;
  13. (optional) shave the spit;
  14. done.
  The steps adapt: if the player already holds the needed item, that step completes at once.
- Guidance:
  - the target gets the 027b highlight in a tutorial colour (Mustard pulse), even when it is not aimed at;
  - an arrow at the screen edge points to it when it is off-screen;
  - the prompt line stays as it is.
- Tutorial mode on the server: no new rules. The first shift's guests are calm (patience × 3) only while the local
  tutorial is running. That is a server flag set through an existing request, or simply a host-only option when
  starting as host with `-tutorial`. Choose the simplest server-authoritative way, with no new RPC, and explain it.
  A dedicated-server game never changes its rules for one player.
- The What's-new panel gets a "Play the tutorial" button. The pause menu, or F1 help if it exists, gets "Replay
  tutorial".
- Tests:
  - TutorialSteps predicates (pure, over snapshots);
  - order and adaptivity;
  - skip and replay;
  - the panel is absent when done.
- Visual check: a new tour `tutorial` runs the steps through the tour actor and shoots each step's card and
  highlight. `WhatsNew.json` entry, this spec.

## Out of scope
- Voice-over, localisation, achievements.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: TutorialSteps tests: each predicate fires on the right snapshot only; adaptivity; skip and replay.
- [ ] AC3: Visual check `tutorial`: every step's card plus highlight, an off-screen arrow, the done card.
- [ ] AC4: A dedicated server never changes its rules for one player (test or code review note).
- [ ] AC5: `WhatsNew.json` entry `spec-038`.

## Defaults to review
Step list and wording; patience × 3 for tutorial guests on a host; the Mustard pulse.

## Implementation notes
Builder (Opus), spec 038, with the 036 review fixes (below). No new RPC, SyncVar or RPC parameter (`RpcSurfaceTests` unchanged).

**Pure core (`Scripts/Tutorial`).** `TutorialSteps.Default` (14 `TutorialStep`s: id, text, optional, a done-predicate and a target
selector over a `TutorialSnapshot`), `TutorialProgress` (order, memory, skip, skip step, replay, the done card), `TutorialWatch`
(events), `TutorialRules` (launch rule, calm-guest gate, pulse, edge arrow). The snapshot holds only what this client sees anyway:
metres walked and degrees turned since the start, the hands (`PickupInteractor.HandsNow`, a new read-only getter, because `CurrentHeld`
is first person only), the cutting board's title, the fryer basket in use (title, mode, zone) and the fryer's power, the assembly
board's take name, layer count and mode (its synced name is only the take name, "Falafel wrap" once it matches, so the wrap step counts
4 layers with falafel in the name), the register's payment, the sink basin, the dish return, the spit's byte, and events. Events come from
`TutorialWatch`: a new server answer that is Ok within 1.5 s of the crosshair offering Sell / Pay (hand over or card) / Order, or the
sink's basin going down (the spit tray going up) within 4 s of a Scrub (Shave) aim. Predicates match the sim's own words ("potato
sticks", "fries", "falafel", "lettuce", "sauce", "shaved"); each also holds for later evidence of its chain (fries on a plate mean the
potato was taken, cut and fried).

**Adaptive.** Every observe marks every remaining step whose predicate holds now; the current step moves past every marked step. So a
step done ahead of time (a guest served while the wrap is still being built, a plate washed before the order step) completes at once when
it comes.

**Step list as built.**
1. Move and look (3 m and 90 degrees).
2. Take a potato (dry shelf).
3. Cut it into sticks (board, knife, drag; right click turns).
4. Fry: power, basket, lower, shake once, raise at perfect.
5. Tip the fries onto a held plate. This is the real 029b action: the spec's "box" is a plate in the game.
6. Build a falafel wrap: wrap, fried falafel, sliced lettuce, a sauce held.
7. Fold, then wrap in paper.
8. Serve a guest at the counter.
9. Take the payment (cash: count and hand over; card: the terminal).
10. Lemonade with a lid.
11. Wash a plate (dish return, sink, hold to scrub).
12. Order at the terminal.
13. Optional: the spit (load, knob high, shave with the knife).
14. The done card (closes after 8 s, or with F4 / Close).

The target selector refines inside a step: fryer off gives its power knob; a lowered basket at Perfect gives the raise button; an empty
cup gives the tap.

**Client (`TutorialRunner`).** Adds itself to the HUD's object at start (like 027b's FeelPanelView; no scene change). Never in batch mode
or a server build; disabled for the scripted observer (`-observe`, dev). It starts by itself until `PlayerPrefs worktown.tutorial.done`
is 1 (set on finish or skip). `-tutorial` forces it, `-skip-tutorial` stops it, and dev builds never auto-start it in scripted runs
(`-skip-whatsnew`, `-tour`, `-observe`). Snapshot every 0.2 s, target every 0.25 s; per frame only the walk and turn tracking, the pulse
and the arrow. Keys: **F4** skips the tutorial (or closes the done card), **F5** skips the optional step. The card's buttons do the same
with a free mouse (Esc). Replay: "Play the tutorial" in the What's-new banner, "Replay tutorial" in the F1 menu (there is no pause menu).

**Highlight and arrow.** `TutorialHighlight` has its own outline hulls (the 027b technique). It copies the player's InteractionHighlight
marker material through a new `MarkerMaterial` getter and draws at 2.2 times the Interact outline's width, in Mustard with a 1.2 Hz alpha
pulse (0.3 to 0.95). It never touches the renderers' property blocks, so the aim's own glow and outline (027b) show together with it. The
hulls carry no TextMesh, so the 027d label manager and the 031b calm view (rings) are unchanged. A slot without its own mesh lights its
station. The edge arrow (UI Toolkit, a Painter2D triangle, Mustard with an Ink rim) stands on the border towards the target when the
target is off screen or behind the camera (behind: mirrored, so turning that way finds it).

**Card (`TutorialPanelView`, a plain class built into the HUD's document).** Top centre: the `panel` class plus a Mustard left border,
the kicker "FIRST SHIFT · 3 / 14" (Mustard), the text (Cream), the keys in Steel ("F4 skips the tutorial", "F5 skips this step"
on the optional step, "F4 closes" on the done card), "Skip step" (optional steps only) and "Skip tutorial" / "Close". At most 540 px
wide, so it stays clear of the wallet box at 1280x720. It is hidden while a menu or the banner has the UI open. Texts change only on a step change; nothing allocates
per frame.

**Calm guests: the server-authoritative choice.** No new RPC and no synced value. A host is server and client in one process, so the
local runner calls `CustomerService.SetLocalTutorial(bool)` directly (only when `IsServerStarted`). `CustomerService.CalmGuests` is
`TutorialRules.HostCalms(serverBuild, IsServerStarted, IsClientStarted, running)`. While it is true:
- each guest spawned gets patience × 3 (the `CustomerRules.PatienceFor` scale);
- the first ones come from `Npc/TutorialOrders`, which ships in release builds too: a snack guest with fries, cash ($20 note), eating in
  (its plate comes back for step 11); a meal guest with a falafel wrap (no modifiers) plus a lemonade, cash ($50), to go.

Later guests draw as usual; guests already waiting keep their orders. To make the very first guests calm too, CustomerService applies
the same launch rule itself at Start (args plus the PlayerPrefs flag; not on a server build or in batch mode), and its first queue sync
waits up to 3 s for the local client. With `-tour-orders` as well (the tutorial tour's host), the tutorial's script comes first and the
tour's patience is multiplied.

**AC4: a dedicated server is unaffected.**
- `UNITY_SERVER` builds hard-wire `ServerBuild = true`: HostCalms is false and SetLocalTutorial is ignored.
- A `-server` process has no local client, so HostCalms is false.
- A remote client's tutorial has no channel to the server at all.

Test: `TutorialTests.CalmGuests_OnlyOnAHostWhoseTutorialRuns_NeverOnADedicatedServer`.

**Visual check.** New tour `tutorial` (`TourRules.TutorialTour`, `Dev/TutorialSequence`). visual-check.sh runs the client with
`-tour tutorial -tutorial` and the host with `-host -observe tutorial -tour-orders -tutorial`. The tour walks step 1 and looks away twice
for the arrow. Then it runs the cook tour's own parts (`CookSequence.FryFries`, `BuildFalafelWrap`, `CleanUp`, `RunOutAndOrder`,
`ShaveSeitanWrap`, now `internal`). Meanwhile `WatchSteps` shoots every card 0.35 s after its step starts (`tutorial-NN-<step>`), the
arrow once per step (`tutorial-NN-arrow`) and the done card. Steps the tour did ahead of time pass at once (the fries guest is served
and paid during the fries, so steps 8 and 9 pass when the wrap is wrapped); after each part the tour shows their cards once more through
a dev-only `TutorialRunner.DevPreview` (progress unchanged) and shoots them (`tutorial-NN-passed-<step>`). It also shoots the potato
slot pulsing while in view but not aimed at (`tutorial-02-pulse-*`, 10 frames) and the arrow looking away (`tutorial-arrow-behind`,
`-side`). `TourRulesTests` pins the tour names (now with "tutorial").

**036 review fixes.**
1. (must-fix) Cones can be ordered.
   - `SupplierOffer` has an optional price good (`PriceGoodId`, the good itself by default), and `EconomyEngine.TryBuySupply` checks it.
   - `StockCatalog.TryConeOffer` keeps `GoodId = "seitan_cone"` (stock and delivery) and prices the cone as `seitan`.
   - The `StockSupplyTests` guard has no special case again: every line's offer delivers its own good, and its price good has that
     grocer price.
   - `SpitReviewTests`: a cone ordered through a real `EconomyEngine` wallet costs $2,640, and `GrocerPaid` and the grocer's revenue rise
     by that amount. It is delivered, unpacked and loaded. An offer without its price good is refused (the old bug).
2. A server stop returns every untouched loaded cone (30 portions, never shaved) to the cone line (`SpitDesk.ClearReturningUntouched`,
   called from KitchenService). A partly shaved cone is lost, like food in a pan. Tested.
3. No free cones: with an economy but no SupplyService, the load is refused (OutOfStock, `SpitDesk.MayLoadWithoutStock`). Tested.
4. `DishRoom.Restarted` doc: FishNet respawns the scene plates where they last were. Not done: recording and resetting their start spots.
5. Empty tap.
   - A keg line's synced count now shows less than one cup as 1 (`StockCatalog.Presented`; whole cups otherwise, as before). So 0 means
     the kegs are empty, the tap says "Out of stock", and the last part of a cup still pours.
   - `AlmostEmptyText` is gone (it had one use). The 035 review test now pins "Out of stock" at 0 and a pour with a third of a cup left.
   - **Deviation:** the client never shows "Almost empty". One byte cannot tell 0 from "less than a cup" without this change, and with
     it there is no client case left. The server's refusal below the smallest share (1 % of a cup) shows the HUD's "Out of stock" text.
6. `SpitReviewTests.AScoopOntoAFullBuild_GivesThePortionBack_TheTrayIsUnchanged` (a build at MaxBuildLayers).
7. Comments: DrinkTests (the shave was inserted, not appended); the doubled summary above HandleSpitKey moved to TryHandleStationKey.

**Runs (2026-11-09).**
- `verify.sh` green: 1112 EditMode (1094 + 18: TutorialTests 13, SpitReviewTests 4, WhatsNewTests +1), 354 sim.
- `visual-check.sh --tours look,tutorial`, then `--tours tutorial,cook` on the final build: all ok, exit 0, 0 Exception lines, 0
  timeouts. Tutorial 695 images with all 14 cards; cook 965 images. No card appears in the look or cook tours.
- The sheets are `.verify/visual/{look,tutorial,tutorial-observer,cook,cook-observer}/sheet.png`; the cards are
  `tutorial/NN-tutorial-*.png`.

**Still feels off (for review).**
- Steps 8 and 9 usually pass at once: the fries guest is served and paid during the fries, before the wrap.
- The card covers the top of the view (the dry shelf's sign at the shelf).
- Step 4's target stays the cutting board while the knife is still in the hands; it means "put the knife down, take the sticks", but
  the text does not say so.
- Calm guests still leave as "gone" when the town's demand changes (the 014 reconcile), not from patience: the tour's host shows
  "missed 4".
- Clicking the card's buttons needs a free mouse (Esc); F4 and F5 are the reliable way.

**Scope beyond the listed folders** (recorded in the 038b round, as in 029-036): besides `Scripts/Tutorial`, this round touched
`Scripts/Npc` (CustomerService's calm guests, `TutorialOrders`), `Scripts/Net` (`PickupInteractor.HandsNow`; InteractionRules lost
`AlmostEmptyText`, 036 review 5), `Scripts/Player` (`InteractionHighlight.MarkerMaterial`), `Scripts/UI` (the What's-new and F1 buttons), `Scripts/Dev` (the
tutorial tour, `CookSequence` parts made `internal`), `Resources/WhatsNew.json`, `scripts/visual-check.sh` (the tutorial tour), and for
the 036 review fixes `sim/Runtime/Economy` (`SupplierOffer.PriceGoodId`, `EconomyEngine.TryBuySupply`), `Scripts/Shop` (`StockCatalog`,
`CleaningRoom`, `DishRoom`), `Scripts/Cooking` (`SpitDesk`, `KitchenService`) and their tests (`CleaningReviewTests`, `StockSupplyTests`,
`TourRulesTests`, `WhatsNewTests`, sim `DrinkTests`).

**Not mine in the tree:** `docs/cooking-roadmap.md` shows a 2-line change I did not make (left as it is).

## Blockers
(Builder fills this in after 3 failed attempts.)
