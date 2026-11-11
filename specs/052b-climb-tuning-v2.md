---
id: 052b
title: Climb tuning v2 (crew scaling, menu choice, repeatable sinks, automation gate, stage 1 pace) in /sim
status: draft
owner: architect
builder: sonnet
---

# 052b: Climb tuning v2

## Goal
Apply the defaults of inbox questions 22 and 24 to the spec 052 climb simulation, then rerun its report until T1-T8 pass
for 1, 4 and 16 players. Solo and small crews get fewer, more patient guests and choose their menu; money gets repeatable
uses that grow with income; the stage 4 gate becomes an automation purchase plus stars; stage 1 is calibrated to the pace
measured in the game (spec 053) and its goal and delivery scale with crew size. The output is a table of game numbers that
a later /game spec applies. This spec changes no game code.

## Depends on
Spec 052 (built, AC12 failed by design: see its Blockers), spec 053 Implementation notes ("The rent goal, measured"),
inbox questions 22 and 24 (defaults), D23 (whole dollars), D27 (catalog names pass `ContentRules.FindForbidden`), D37.

## Scope
Only `/sim`, plus this spec. Unlike 052, code in `sim/Runtime/Climb` may change (not only `CreateDefault()`).
- `sim/Runtime/Climb/` (change): `StageDef.cs`, `PurchaseDef.cs`, `BigJobDef.cs`, `ClimbCatalog.cs`, `ClimbState.cs`,
  `ClimbEngine.cs`, `ClimbPolicy.cs`, `ClimbRun.cs`, `ClimbTargets.cs`, `ClimbEvaluation.cs`, `ClimbReport.cs`.
- `sim/Runtime/Climb/` (new): `CrewScaleRow.cs`, `MenuFit.cs`, `GameNumbersRow.cs`, `GameNumbers.cs`.
- `sim/Tools~/ClimbReport/Program.cs` (change: new flags below).
- `sim/Sim.Tests~/Climb/` (change and new tests, see Test plan).
- This spec (Implementation notes, tuning log, Blockers).
- No change to `sim/Runtime/Cooking`, `Core`, `Economy`; no change to `/game`, `scripts/` or any other spec.

## Out of scope
- Any `/game` change, including the 053 numbers (a later spec, listed under "Game-side follow-up").
- Calibrating stages 2-5 to the game (they are not built; see Open question 3). Mixed-skill or partial crews.
- Price units relative to each dish, a per-stage price weight, a general optimiser, cents (question 17).

## Design

### (a) Solo and small-crew scaling
`CrewScaleRow(int MaxPlayers, int GuestsPermille, int PatienceSeconds, int GoalPermille, int StockPerService)`;
`ClimbCatalog.CrewScaling` is a list sorted by `MaxPlayers` (validated: ascending, last row `MaxPlayers >= 16`, permille
1..5000, patience 0..600, stock >= 1). `ClimbCatalog.RowFor(int players)` returns the first row with `players <= MaxPlayers`.
- Every channel ceiling (step 3 of 052) becomes `ceiling * GuestsPermille / 1000` (after the bonuses, floor, min 1).
- Patience: the wait thresholds of steps 3 and 5 (`60` in attraction, `120` for the star penalty) both add `PatienceSeconds`.
- Starting rows (provisional; tuning changes only numbers):

| MaxPlayers | Guests | Patience | S1 goal | S1 stock |
|---|---|---|---|---|
| 1 | 450 | +60 s | 600 | 12 |
| 2 | 650 | +40 s | 1300 | 12 |
| 3 | 850 | +20 s | 1500 | 14 |
| 7 | 1000 | 0 | 1700 | 16 |
| 16 | 1000 | 0 | 2300 | 20 |

**The crew chooses its menu** (`MenuFit`, pure, no draws, runs in step 1 after buying). The active menu is a subset of the
owned dishes (stage 1 dish always first and always active; never empty; never more than `MaxDishes`). Kept in
`ClimbState.ActiveMenu` (new field, ids in buying order).
- Drop: last normal service missed share > `MenuFit.DropMissedPermille` (100) and more than 1 active dish: drop the active
  dish with the most `Actions` (tie: id ordinal, last wins), but never the stage 1 dish.
- Re-add: the last 3 normal services each missed < `MenuFit.AddMissedPermille` (30): re-add the owned inactive dish with
  the fewest `Actions` (tie: id ordinal, first wins). At most one drop or one add per service.
- Guests draw dishes only from the active menu; `Variety` counts active dishes. A newly bought dish is active at once.
- Applies to every strategy (it is crew behaviour, not a strategy axis). `ClimbRun` records drops and re-adds per stage.

### (b) T4 against the reference, not the median
New T4: per crew size and profile, the fastest of the 15 strategies to S5 (median over seeds) is >= 0.85 x the reference
strategy (Balanced+Match) to S5. If the reference does not finish, T4 fails with that detail.
Why: the old median is pulled up by strategies that are slow or do not finish (Minus1, Plus2, Undercut), so a normal
strategy looked like a shortcut against it. The question T4 asks is "does any way of playing beat the intended way by
more than 15 %", and the intended way is the reference strategy T1 already uses. The price shortcut itself (052
Blockers 2) came from the few-players check, which (d) removes; if Plus1 is still faster after (d), that is a finding
(price units per dish, out of scope), written under Blockers. The report keeps the old rule as an `INFO T4m` line (not
counted in RESULT) so both can be compared.

### (c) Repeatable money sinks that grow with income
- `PurchaseDef` gains `int MaxCount` (1 = buy once, as today; 0 = no limit) and `int CostGrowthPermille` (1000 = flat).
  The k-th copy (k from 0) costs `cost_k = cost_{k-1} * CostGrowthPermille / 1000` (floor each step, `cost_0 = Cost`).
  Upkeep is paid per owned copy; effects add per copy. `ClimbState.Owned` may hold an id several times;
  `ClimbState.Count(id)` is new; `Owns` stays. `PurchaseDef.CostOf(int ownedCount)` is new and pure.
- `BigJobDef` gains `int StakeGrowthPermille` and `int MaxTier`. `ClimbState.JobTier` (new, id to tier, start 0): after
  an event or a won competition, tier + 1 (max `MaxTier`); a lost competition leaves it. Deposit, prize and event guests
  each scale by growth^tier (floor each step), so stakes follow a richer crew.
- New default data (all provisional):

| id | stage | kind | cost | growth | max | upkeep | effect |
|---|---|---|---|---|---|---|---|
| `bigger_place` | 3 | P | $3,000 | 1600 | 4 | $40 | +1 work spot, +100 permille ceiling (all) |
| `staff_cook` | 4 | P | $2,500 | 1300 | 6 | $45 | staff 26 APM, skill 650 (replaces `staff_cook_1`, `_2`) |
| `decor` | 5 | S | $4,000 | 1500 | 0 | 0 | +10 rep (replaces `decor_3`..`decor_6`) |
| `car_collection` | 5 | S | $20,000 | 1500 | 0 | $10 | +5 rep (replaces `show_car`) |

  Big jobs: `cook_off`, `street_festival`, `stadium_day` get growth 1400, max tier 8.
- `bigger_place` needs a new `PurchaseEffect.WorkSpots` (int, default 0), added to `W` in step 2 of 052.
- T7 reads the cost of the next copy (`CostOf(Count)`) and the current tier's deposit.

### (d) Stage 4 gate: automation plus stars
- `StageDef.GateFewPlayers` and the few-players check are removed (code, data, tests).
- `StageDef` gains `int GateMinStaff` and `int GateMinMachines`. Staff = a purchase with `StaffActionsPerMinute > 0`;
  machine = a stage 4 purchase with `ActionsSaved > 0` and no staff. Counts include copies.
- S4 data: `GatePurchaseId = "automation_plan"` (new Gate purchase, stage 4, $4,000), `GateMinStaff 2`, `GateMinMachines
  2`, stars 3.6 over the last 5 services. The gate purchase is a candidate only when staff, machines and stars hold.
  `StageDef` validation: the min counts are 0..16.

### (e) Stage 1 pace by crew size (question 24, measured in 053)
Measured in the game (053 notes, one scripted cook): a fries guest every 82 s, so about 4.4 guests per 360 s service; a
guest pays about $615 (bill $503 + tip $112), a potato costs $48, the pitch fee is $575, start $2,875, goal $34,500.
That is about 16.5 services (1.7 h) solo; crews are capped by the 12-potato slot at about 5.1 services (0.53 h). The
game's amounts are the sim's x 115 (053: game dish price 460 / sim 4).
- `StageDef` gains `int WorkPermille` (1000 = as today; scales every per-guest action cost of that stage, step 4) and
  `int TipPermille` (tip on revenue, per service, floor once). S1 starts at `WorkPermille 3900` (one average player,
  156 actions, about 35 actions a fries guest) and `TipPermille 220`; S2-S5 keep 1000 and 0 (Open question 3).
- Fries `IngredientCost` 1 -> 0 (the measured potato is about 10 % of the bill, 0.42 sim units, which floors to 0).
- S1 stock cap: a service serves at most `RowFor(N).StockPerService` stage 1 dishes (the supplier's drop); guests beyond
  it are turned away as "sold out", not missed (they do not hurt stars).
- S1 goal: the `rent_restaurant` cost is multiplied by `RowFor(N).GoalPermille / 1000` (floor). The gate is unchanged.
- **Calibration check C1** (`ClimbEvaluation`, counted in RESULT): with `--calibration` the engine runs S1 with the 053
  values (every row goal 1000, stock 12, `BetweenSeconds 15`, average profile, Balanced+Match). Median S1 services: 1
  player 16.5 +- 2.5; 4 and 16 players 5.1 +- 1.0. Only `WorkPermille` and `TipPermille` of S1 may be tuned to pass it.

### Game numbers (`GameNumbers.For(catalog, int players, int gameScale) -> GameNumbersRow`)
`GameNumbersRow(int Players, long GoalGame, int StockPerService, long PitchFeeGame, long StartGame, int MedianServices,
int TenthsOfHours)`: amounts x `gameScale` (CLI `--game-scale`, default 115); services and hours from the reference
runs at `BetweenSeconds 15` (the game's 15 s summary). The report prints a `GAME NUMBERS` section for N in
{1, 2, 3, 4, 6, 8, 12, 16}. Each row's hours must be in 0.75-1.5 (target `T1g`, counted in RESULT).

### CLI and report
New flags: `--calibration` (runs only C1), `--crew-scaling on|off` (default on; off = one row of 1000/0/1000/12),
`--game-scale <int>`. Report sections as in 052, plus per crew size: menu drops and re-adds per stage, repeat buys per
stage, the `INFO T4m` line, then `GAME NUMBERS`, then `RESULT`. Exit codes as in 052.

### Game-side follow-up (a later /game spec; named here, not built)
1. `FoodCornerRules` goal: `Scale(rent_restaurant cost x GoalPermille(N) / 1000)` instead of `Scale(rent)`.
2. The supplier drop (`StockRoom.Drop`, `ShopStage.ServerServiceOpened`): fill to `StockPerService(N)`; the potato slot
   capacity (12 today) must hold it, or the drop arrives in two loads.
3. Crew size N = players connected at service start (Open question 5); goal and drop are fixed for that service.
4. Pitch fee and starting money unchanged ($575, $2,875) unless tuning changes S1 upkeep or starting money.
5. Later stage 2 specs: guest count x `GuestsPermille(N)`, patience + `PatienceSeconds(N)`, menu on/off per dish,
   `bigger_place`, `automation_plan`, growing event stakes.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim/Runtime/Cooking sim/Runtime/Core
      sim/Runtime/Economy game` is empty.
- [ ] AC2: Determinism (052 AC2) still holds; `grep -rn "FewPlayers" sim` finds nothing.
- [ ] AC3: Crew scaling: `RowFor` picks the right row for 1, 2, 3, 4, 7, 8, 16; a solo channel ceiling equals
      `floor(ceiling x 450 / 1000)`; a solo wait of 170 s costs no star, 190 s does; catalog validation throws for an
      unsorted table and a last row below 16.
- [ ] AC4: `MenuFit` on hand-built states: drops the most-action dish at 101 permille missed, never the stage 1 dish,
      re-adds the fewest-action dish after 3 services under 30, at most one change per service, never empty, never
      above `MaxDishes`.
- [ ] AC5: Repeatables: `CostOf` for 0..4 copies at growth 1600 matches literals; `MaxCount` is never exceeded; upkeep
      and effects count per copy; `MaxCount 0` keeps buying while affordable.
- [ ] AC6: Big jobs: a won competition and an event raise the tier, a loss does not; tier stops at `MaxTier`; deposit,
      prize and guests match literals per tier.
- [ ] AC7: S4 gate: stars only, 1 staff + 2 machines, and 2 + 2 without stars each do not pass; 2 + 2 + stars buys
      `automation_plan` and enters S5.
- [ ] AC8: S1: the stock cap turns guests away as sold out (not missed); `GoalPermille` changes the gate cost;
      `WorkPermille` and `TipPermille` change actions and revenue by exactly their permille.
- [ ] AC9: T4 (reference), C1 and T1g each have a passing and a failing test on hand-built `ClimbRun` data; `INFO
      T4m` is printed and not counted.
- [ ] AC10: `bash scripts/climb-report.sh --calibration` prints C1 for 1, 4 and 16 players; the full report prints
      `GAME NUMBERS` with 8 rows; both under 60 s.
- [ ] AC11 (tuning): `bash scripts/climb-report.sh --players 1,4,16 --seeds 5` exits 0 (T1-T8, C1, T1g pass). Tuning
      rounds change only numbers in `CreateDefault()` and the crew table, each logged. After 3 rounds that still fail:
      stop, write the failing lines and a best guess under Blockers (an allowed outcome, as in 052).
- [ ] AC12: Every new public method in `sim/Runtime/Climb` has a test; Climb tests add < 15 s to `dotnet test`.

## Test plan
- `sim/Sim.Tests~/Climb/`: `CrewScalingTests` (AC3), `MenuFitTests` (AC4), `RepeatPurchaseTests` (AC5), `BigJobTierTests`
  (AC6), `AutomationGateTests` (AC7), `FoodCornerPaceTests` (AC8), `ClimbEvaluationTests` (AC9, extended),
  `GameNumbersTests`, `ClimbReportTests` (sections, INFO line, RESULT). Update `ClimbPolicyTests` (few-players tests
  removed) and the `TestClimb.cs` fixture (new fields with neutral defaults: one row 1000/0/1000/large stock).
- Command: `bash scripts/verify.sh`; fast loop `dotnet test sim/Sim.sln`; report `bash scripts/climb-report.sh`.

## Human steps
Read the report and the GAME NUMBERS table; say whether the stage 1 times per crew size feel right; answer or accept
the open questions. Then approve the game-side spec.

## Open questions (recommended default in brackets)
1. Crew table rows and breakpoints. [as in (a); tuning may change numbers, not the breakpoints]
2. Menu choice: automatic crew behaviour in the sim, a menu board in the game. [yes; the game UI is a later spec]
3. Stages 2-5 work and tips: the game is about 4x slower per fries guest than the sim's S2 assumes. [calibrate S1
   only now; measure and calibrate S2 when the restaurant is built; this is a known gap, reported in Implementation
   notes]
4. Is the scripted tour cook a fair "average" player? [yes, the only measurement; the 053 AC11 play-test can correct
   `WorkPermille`]
5. Crew size in the game: players at service start, or the most seen so far. [at service start; a friend who drops in
   raises that service's goal, but money already earned stays]
6. Sold-out guests at S1 do not cost stars. [yes, the shop is closed for them, not slow]
7. Repeat limits: `bigger_place` 4, `staff_cook` 6, decor and cars unlimited. [yes]
8. Lost competitions keep the stake tier. [yes; stakes only grow with success]

## Implementation notes
(Builder fills this in: interpretations, tuning log per round, the final report summary and the GAME NUMBERS table.)

## Blockers
(Builder fills this in after 3 failed tuning rounds.)
