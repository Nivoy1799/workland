---
id: 052b
title: Climb tuning v2 (crew scaling, menu choice, repeatable sinks, automation gate, stage 1 pace) in /sim
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 (inbox questions 25 and 26) with the recommended defaults of its Open questions.

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
Built 2026-11-11/12 (builder) on `spec/052b-climb-tuning` (on top of 037, 572cae0); committed as 68e3597, review fixes below (uncommitted). Sim tests 506 -> 556
(one 052 few-players test removed with the check, 51 new; Climb tests about 0.6 s). `env -u UNITY_PATH bash scripts/verify.sh`
green (Unity skipped). `git diff 572cae0 -- sim/Runtime/Cooking sim/Runtime/Core sim/Runtime/Economy game scripts` is empty.

**D40 replaces the crew table of (a) and the goal and stock rows of (e)** (coordinator, 2026-11-11):
- One pure function, `CrewScaling.For(catalog, players) -> CrewFactors`. Nothing else reads crew size. Its data is the
  catalog's `CrewTable` (`CrewScaleRow`: MaxPlayers, h(n), patience, and three Fixed overrides for C1 and
  `--crew-scaling off`). `RowFor` and the table validation are as specified in (a).
- Reference crew 4. h = 0.55 / 0.75 / 0.9 / 1.0 for 1/2/3/4, 1.0 up to 16 (data). Guest demand D = n/4. D also scales work
  spots, staff actions, event guests, running costs (stage, channel and purchase upkeep) and the stage 1 drop (rounded up).
  Without D on spots and staff, a 16-crew would be capacity-bound and break the plateau. G = n x h(n)/4 scales goals and all
  one-time money. Prices and the cost of each ingredient never scale.
- Progress is a fraction of the goal. `ClimbState.Balance` is reference (4-player) money: each service adds
  `CrewScaling.ToReference(net, G)`. Purchases, gates, deposits and the starting money stay at their catalog cost. A join or a
  leave changes only later services (test `ProgressIsNetOverTheGoalFactor...`). Real money: `ServiceResult` keeps its real
  amounts and adds `GoalPermille`, `NetReference`, `SoldOut`, `MenuDropped` and `MenuAdded` (init properties, so the
  positional constructor is unchanged). T7 reads `NetReference`.
- Patience (a) is kept as data and is 0 in every row: D40 makes solo easy through h, not through patience.
- Checks, adapted:
  - T1: the 4-player bands. For a crew with h < 1 the lower edge is multiplied by h (`ClimbTargets.BandOf(stage, h)`), and
    the upper edge stays.
  - T1p (new, counted): for every crew above 4, no stage may take longer than 4 players' median (`PlateauPermille` 1000).
  - T1g: the same h rule on the S2 band.
  - T4: compares strategies within one crew size, so an easy solo is never flagged as a shortcut.
- Report: the default `--players` is now 1,2,4,8,16. Each section gets a crew line. A crew-size table (h(n) and hours in each
  stage) is added, and the per-stage table gains hours, menu drops, re-adds and repeat buys.

**Other interpretations.**
- Only crews choose their menu (`MenuFit`, run by the engine right after `ClimbPolicy.Decide`). The missed share is per
  normal service, over the guests who chose you. Prices cover the owned menu, so a dish that comes back has its price.
- Stock cap: it counts served stage 1 dishes. Sold-out guests stay in `Chosen` and get no draws, actions, stars or wait.
- `PurchaseDef.CostOf` and `BigJobDef.DepositAt` and `PrizeAt` floor to whole units at each step, so cents give the dollar
  climb.
- Scope additions: `PurchaseEffect.WorkSpots` is required by (c). `ServiceResult`, `TargetCheck` (unchanged), `CrewScaling.cs`
  and `CrewFactors.cs` are new or changed files that the coordinator's single-function request needs. `CreateDefault` gains
  `crewTable` and `stage1Extras`.
- C1 and GAME NUMBERS use `stage1Extras: false`, the corner as 053 built it: no second basket and no sign. With the extras,
  the sim crew spends $140 that the game crew never spends.
- C1 targets were re-taken from the 037 notes: the corner pays about 10 % less per guest. That gives solo 19.0 +- 2.5
  services and crews 5.7 +- 1.0. The 037 fix round (quality back in the bill) is not on its branch yet, so these numbers
  stand until its notes carry the new measurement. They are in `ClimbTargets`.
- `--game-scale` defaults to 1, not 115: since 037 the game's money is the catalog's cents.
- 037 review nits: `ClimbReport.Format` reads the money scale from the runs, and the catalog rejects any money value that is
  not a whole multiple of `MoneyScale`. With today's constructor this holds by construction; the test checks the default.
- Game-pinned numbers (rent $300, pitch fee $5, start $25, dish prices, choice weights) were not tuned:
  FoodCornerRulesTests and MenuBoard read them.
- Two 052 tests were adapted to D40, not weakened. The ceiling test now includes the crew demand factor and every copy. The
  "16 do not out-earn 4 at stage 1" test now compares progress (`NetReference`).

**Tuning log** (only `CreateDefault` numbers and the crew table; start = the spec's defaults plus D40, S1 WorkSpots 2 -> 4
so the reference crew has no overflow):
- R0: 100/143. S1 was too fast in the game (T1g 0.3-0.5 h for every crew). Plus1 was a shortcut, and money sat idle in S4/S5.
- R1: S1 drop 16 -> 8 per 4 players, the only free S1 pace lever. Second basket and corner sign 60/80 -> 30/40. S4/S5
  upkeep 160/200 -> 100/130 (S4 earned about $117 a service on $285 upkeep). S1 WorkPermille 3900 -> 3700. Result: 119/143;
  T1, T1p and T1g pass.
- R2: S1 TipPermille 220 -> 250 (C1 solo, 4 guests a service). Cook-off rival 700 -> 800. Stake growth 1400 -> 1200, max
  tier 8 -> 5. Decor $4,000 and car collection $20,000 at growth 1500 -> flat $3,000 and $15,000. S4 items cheaper:
  runner 1000, fry dump 1500, drink filler 1500, dishwasher 2000, line cook 2000, automation plan 3000. Result: 128/143;
  C1 passes, T4 passes except 1p novice.
- R3: cook-off rival back to 700. At 800, average crews lost every cook-off and its deposit drained them. Result: 128/143.

**Final report** (`--seeds 5`, under 10 s; `--players 1,4,16`: 80/90; default 1,2,4,8,16: 128/143; `--calibration`
3/3). Average crew, reference strategy, hours to enter S2/S3/S4/S5:

| crew | h(n) | S2 | S3 | S4 | S5 |
|---|---|---|---|---|---|
| 1 | 0.55 | 0.7 | 2.1 | 4.4 | 9.2 |
| 2 | 0.75 | 0.9 | 2.8 | 6.4 | 12.6 |
| 4 | 1.0 | 1.2 | 3.9 | 7.9 | 15.3 |
| 8 | 1.0 | 1.2 | 3.9 | 7.9 | 15.2 |
| 16 | 1.0 | 1.2 | 3.9 | 7.9 | 15.1 |

Every crew size passes T1, T1p (8 and 16 match 4 at 1.0x), T1g, T2, T3, T6, T8 and C1. T4 and T5 pass except 1p novice.
T7 fails (Blockers).

**GAME NUMBERS** (the reference crew at the game's pace, 375 s a service, corner as built; game money):

| players | h | demand | goal x | S1 goal | drop | pitch | start | S2 rent | licence | services | hours |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 0.55 | 0.25 | 0.137 | $41.00 | 2 | $1.00 | $3.00 | $12.00 | $164.00 | 5 | 0.5 |
| 2 | 0.75 | 0.5 | 0.375 | $112.00 | 4 | $2.00 | $9.00 | $25.00 | $450.00 | 6 | 0.6 |
| 3 | 0.9 | 0.75 | 0.675 | $202.00 | 6 | $3.00 | $16.00 | $37.00 | $810.00 | 7 | 0.7 |
| 4 | 1 | 1 | 1 | $300.00 | 8 | $5.00 | $25.00 | $50.00 | $1,200.00 | 8 | 0.8 |
| 6 | 1 | 1.5 | 1.5 | $450.00 | 12 | $7.00 | $37.00 | $75.00 | $1,800.00 | 8 | 0.8 |
| 8 | 1 | 2 | 2 | $600.00 | 16 | $10.00 | $50.00 | $100.00 | $2,400.00 | 8 | 0.8 |
| 12 | 1 | 3 | 3 | $900.00 | 24 | $15.00 | $75.00 | $150.00 | $3,600.00 | 8 | 0.8 |
| 16 | 1 | 4 | 4 | $1,200.00 | 32 | $20.00 | $100.00 | $200.00 | $4,800.00 | 8 | 0.8 |

All amounts are floored to whole dollars, as the engine does. For the game, progress is net / (goal x G), so the shown goal
is that crew's real goal. A drop above 12 needs a bigger potato slot, or two loads.

Known gap (Open question 3): stages 2-5 are not calibrated to the game. The S1 pace now comes from the drop: 2 fries per
player per service. A 4-crew at full work would need a rent goal of about $600 to reach the T1 band.

## 052b review fixes
The review of 68e3597 asked for changes. These fixes are not committed yet. Sim tests 556 -> 566, and
`env -u UNITY_PATH bash scripts/verify.sh` is green. Nothing changed outside `/sim` and this spec.
- **Big jobs lost money, and the crew always took them (the real cause of T7).**
  - Before: event guests grew with each tier, but the crew's capacity stayed flat, so a tier-5 festival netted -$540 to
    -$750. Stadium day lost money even at tier 0, and average crews lost 28 of 33 cook-offs. The policy took any ready job
    once the balance held 2x its deposit.
  - Events: guests are now capped at what the crew can serve (`ClimbEngine.ServableGuests`: capacity over the active menu's
    average guest actions, with remakes at the mean mistake rate). Each tier raises the price per guest
    (`BigJobDef.PricePermilleAt`, which replaces `GuestsAt`) together with the deposit, and a cook-off's prize grows with the
    deposit. So stakes still grow with success.
  - Policy: `ClimbPolicy.ChooseJob` takes the first ready job whose deposit the balance covers twice and whose expected
    net is at least the last normal service's net (`ClimbState.LastNet`). The expected net (`ExpectedJobNet`) uses no
    random draws. For a cook-off it is the exact win chance (`WinPermille`, counted over both ±100 ranges) times the prize.
    For an event it is the servable guests at the menu price times the tier factor. Both subtract ingredients, upkeep and
    the deposit.
  - Result for 4 players, average profile, seed 1: cook-off +$1,069 (22 runs), festival +$1,029, stadium day +$2,116,
    against +$240 for a normal service.
- **T7 rule (review):**
  - The mean is taken over the last 5 *normal* services. Big-job services are skipped in the window and are never counted
    as idle.
  - A service is idle only when its balance is at least 3x that mean and no goal cost lies in 3-20x. The band is unchanged.
  - The old `if (mean <= 0) continue` let a losing crew that held money escape the check. Now a mean of 0 or less counts as
    one whole unit, under the same rule, and the mean is floored.
- **T5:** the share window is longer below 4 players (`ClimbTargets.ShareWindowFor`: 10 x 4 / n, so 40 for solo, 20 for 2,
  13 for 3).
- **T1p:** a big crew must also not be faster than 0.85x of 4 players in any stage (`PlateauMinPermille`). The 8- and
  16-player crews now sit at 0.95-1.0x.
- **Menu nits:**
  - A big-job service skips MenuFit (the job is chosen first) and records no missed share.
  - Any drop or re-add clears `RecentMissed`, so re-adds no longer chain.
  - `EnterNextStage` also clears `RecentMissed`.
- **Leave test:** 4 -> 1 (`ALeaveChangesOnlyLaterServices`).
- **Tuning, 3 rounds after the fixes** (numbers only):
  - F1: street festival price 1100 -> 1600 permille and deposit $600 -> $300; stadium day 2000 permille and $500 (was 1200
    and $1,000). Events are now worth taking.
  - F2: decor $3,000 -> $1,500 and cars $15,000 -> $7,500, so the two flat sinks cover normal-service means from $75 to
    $2,500. The line cook went $2,000 -> $1,500, but that broke T3 and T8, so it was reverted in F3.
  - F3: car upkeep $10 -> $0, because piled-up cars ate the solo crew's normal net.
- **Result:**
  - `--players 1,4,16`: 80/90 -> **85/90**. Default crews: 128/143 -> **135/143**. `--calibration`: 3/3.
  - T5 and T8 pass for every crew.
  - The table of hours per stage and the GAME NUMBERS are unchanged; stage 5 entry moved by 0.1 h or less.
  - Still failing: T7 for 1p novice/average/expert (13/12/7 idle services), for 2p novice/average (1/2) and for 4p novice
    (2, in S4); T4 for 1p and 2p novice.

## Blockers
Tuning stopped after 3 rounds, and again after 3 rounds on the review fixes (AC11 fails, an allowed outcome). Failing
lines after the fixes, `--players 1,4,16 --seeds 5`: `FAIL T7` for 1p novice/average/expert (13/12/7 idle services, in S4
and S5) and 4p novice (2, in S4), and `FAIL T4 1p novice fastest balanced+plus1 10.0 h >= 0.85 x reference 12.7 h`.
1. **T7 (rewritten after the review).**
   - **The true cause** was the big jobs, not the measurement. Events grew guests that the crew could not serve while the
     deposit grew too, stadium day lost money even at tier 0, and the policy took any ready job. So stage 5 crews kept
     paying deposits for losses: the income really did not grow, and goals fell out of the 3-20x band.
   - **Fixed** as the review decided (see "052b review fixes"). Big jobs now pay more than a normal service.
   - **What is left is small crews.** A solo or 2-crew earns most of its money from events, and its normal services net
     only $40-80 real. Under the review's normal-services-only mean, almost every cost is then above 20x. A 4-player
     novice waits in S4 for its stars while holding about $3,500, and the cheapest goal ($2,000) is above 20x its $75 mean.
   - **Best guess:**
     - a cheap repeatable stage 4 sink for poor crews;
     - or measure T7 for small crews over all services, now that jobs are profitable.
     - Either one is a design or target call.
2. **Plus1 is still faster where a channel has no rival** (custom orders, buffet, partly tables). Guests only compare you
   with the outside option, so +1 unit loses about 12 % of guests for about 14 % more per guest. The gain stays under 15 %
   for every crew except 1p and 2p novices. This is the out-of-scope price-unit finding of (b). The fix lies in the price
   weight and the outside weight, which the game's menu board shares (037), or in a rival per channel.
3. **Stage 1 versus the game's goal:** see the known gap above. Raising `rent_restaurant` breaks FoodCornerRulesTests
   (30000), so it needs a /game change first.
