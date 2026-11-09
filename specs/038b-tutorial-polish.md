---
id: 038b
title: Tutorial and supply polish (review follow-ups of 038)
status: done
owner: architect
builder: opus
---

# 038b: Tutorial and supply polish

## Goal
Close the follow-ups of the spec 038 review before the human's play-test, most of all the one that would hide the
tutorial from the human. Approved by the orchestrator under the human's 24 h grant.

## Scope (files as needed; see the 038 review notes in docs/cooking-roadmap.md)
1. **Never mark the tutorial done in scripted runs.** `TutorialRunner.SaveDone`: no save when `-tour` or `-observe`
   is present (dev scripted runs). Test.
2. **No stale calm-guests flag.** CustomerService reads the local runner's state when the server starts, and the
   runner re-tells the host when it finds its player. Tests for both scenarios: a tutorial finished as a remote
   client and then hosting; Replay pressed before hosting.
3. **The engine ties PriceGoodId to GoodId.** A price good is allowed only when it is null, equals GoodId, or GoodId
   has no grocer price of its own. Sim test: `("lettuce", seitan price, …, "seitan")` is refused. Also add sim tests
   for `PriceGoodId` and `TryBuySupply` in StockAndSupplyTests, and make it `string?`.
4. **A tap pour larger than what is left** draws `min(poured, available)` and fills only that much, with the bucket
   from the drawn amount. A server-desk test.
5. **A returned cone never exceeds the line's capacity:** the overflow goes into a waiting delivery box (the 034
   mechanism). Test.
6. **Cache** the tutorial's plate lookup, and rate-limit the PlayerInputReader lookup.
7. **Tutorial step 4 text:** when the knife is in the hands, the text says to put it down first.
8. Record the scope note in spec 038.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Tests for items 1–5.
- [ ] AC3: `visual-check.sh --tours tutorial` passes, and afterwards the machine's PlayerPrefs still has no
      `worktown.tutorial.done` (or its earlier value).

## Implementation notes
Builder (Opus). No new RPC, SyncVar or RPC parameter (`RpcSurfaceTests` unchanged).

1. **Done flag.** `TutorialRules.SavesDone(args)` is false when `-tour` or `-observe` is present (in any build: release builds never
   pass them); `TutorialRunner.SaveDone` logs "[Tutorial] scripted run: the done flag is not stored" instead. Test
   `TutorialPolishTests.AScriptedRun_NeverStoresTheDoneFlag`.
2. **Calm-guests flag.** The flag and the scripted orders moved out of CustomerService into the pure `Npc/HostTutorial` (`Set`,
   `OnServerStarted`, `NewGame`; a server build ignores all of it). CustomerService calls `SyncLocalTutorial` on the server's `Started`
   state (and in Start when the server is already up). It takes `TutorialRules.RunsForHost(runner.Decided, runner.Running, launch
   rule)`: the runner's own state once it has decided, else the launch rule. With no runner (batch mode) nothing changes. The runner
   calls `TellHost(Running)` again whenever it finds a new player. `TutorialRunner.StartsOnLaunchNow()` holds the launch rule that the
   runner and CustomerService used to duplicate. Tests: a tutorial finished as a remote client and then hosting; Replay before hosting;
   the launch rule before the runner decides, and the tour's observer host (runner off, `-tutorial` keeps calm guests); a restart
   or a replay scripts the first guests again.
3. **PriceGoodId.** `EconomyEngine.TryBuySupply` refuses (UnknownBaseItem, nothing changes) a price good other than `GoodId` when
   `GoodId` has a grocer price of its own. `SupplierOffer.PriceGood` is `string?`. New sim tests in `StockAndSupplyTests`:
   `PriceGoodId_IsTheGoodItself_UnlessAnotherIsGiven` and `BuyingSupply_TiesThePriceGoodToTheGood_...`. The second covers lettuce at
   the seitan price refused, the cone charged its bulk cost (balance, GrocerPaid, revenue), and an unknown or mispriced price good refused.
4. **Short keg.** New optional `Cooking/IDrinkStock` (`AvailablePercent(drink)`), implemented only by `StockWallet`; money wallets and
   test fakes are unchanged. `DrinkDesk.End` cuts the pour to `DrinkDesk.PourableMl(poured, left)` (= left % x 4 ml) when less is left
   than `FillPercent(poured)`, then previews the cup again (`KitchenState.TryPreviewFill`, extracted from `TryEndPour`). Bucket, spill
   mark, server ml and charge all come from the drawn ml. At 0 left the press is still refused (OutOfStock). Test
   `StockSupplyTests.APourLargerThanTheKegsRest_...`: 360 ml held with 30 % left gives 120 ml, "low", keg 0; a top-up held to 500 ml with
   50 % left gives 320 ml and no spill.
5. **Cone overflow.** `StockRoom.ReturnUpToCapacity(line, units)` fills up to the capacity (`ShopStock.Fill`). The rest becomes one
   waiting delivery box (the 034 `_waiting` list, which `EndSession` keeps), returned as the boxed count. KitchenService's server stop
   uses it for untouched cones. Test `SpitReviewTests.AReturnedCone_NeverPassesTheLinesCapacity_...` (full line: boxed, count stays 2,
   the box spawns, refuses while full, unpacks after a take). The 036 test's "what KitchenService does" line now calls the new method.
6. **Lookups.** `TutorialTargets` keeps a list of the plates and finds it again at most once a second. It skips destroyed or pooled
   (inactive) plates: FindObjectsByType skipped those before. The runner's PlayerInputReader lookup runs at most every 0.5 s while
   there is none.
7. **Step 4 text.** `TutorialStep` has an optional text variant (`TextFor(in snapshot)`, constant strings). FrySticks shows
   `TutorialSteps.FryKnifeText` ("Put the knife down first (Q drops it). Then take the sticks from the board to the fryer: ...") while
   the knife is held and the target is the board. The runner sets the card's text only when it changes (`TutorialPanelView.SetText`).
   Test `StepFour_SaysPutTheKnifeDownFirst_WhileTheKnifeIsHeld`.
8. Spec 038 has a "Scope beyond the listed folders" paragraph now.

**Scope:** `sim/Runtime/Economy`, sim tests, `Scripts/Tutorial`, `Scripts/Npc` (CustomerService, new HostTutorial), `Scripts/Cooking`
(DrinkDesk, KitchenState, KitchenService, new IDrinkStock), `Scripts/Shop` (StockRoom, StockWallet), EditMode tests (new
`TutorialPolishTests`, `StockSupplyTests` +1, `SpitReviewTests` +1), `specs/038`.

**Runs (2026-11-09).**
- `verify.sh` PASS: EditMode 1120 (1112 + 8), sim 356 (354 + 2).
- `BuildAll` (Linux server, Windows) succeeded; `visual-check.sh --tours tutorial` PASS (tour exit 0, observer exit 0, 0 Exception
  lines, 697 files). The client logged "[Tutorial] finished" then "[Tutorial] scripted run: the done flag is not stored".
- AC3: before the run, `HKCU\Software\DefaultCompany\Worktown` already held `worktown.tutorial.done_h1988807466 = 0x1` (from the 038
  round's runs, before this fix). Afterwards it holds the same value: the earlier value, unchanged. The human should delete that value
  before the play-test, or the tutorial will not start by itself (F1 "Replay tutorial" still works).

**Follow-ups.** A pour cut short still shows the client's predicted level until the server's bucket comes back (the 033 rule corrects
it, about one round trip). The money wallet's pour still refuses a pour it cannot pay whole (no economy short-pour).

## Blockers
(Builder fills this in after 3 failed attempts.)
