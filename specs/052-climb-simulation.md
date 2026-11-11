---
id: 052
title: Climb simulation (stages 1-5 for 1, 4 and 16 players against NPC rivals) in /sim
status: draft
owner: architect
builder: sonnet
---

# 052: Climb simulation

> **Depends on the human approving docs/game-design.md v1 (inbox question 13).** Do not build this before that,
> even if this spec is approved. If v1 changes the climb (D34 stages), this spec is revised first.

## Goal
A numbers-only simulation of the D34 climb (food corner, first restaurant, service mix, automation, beyond the kitchen)
for crews of 1, 4 and 16 players against NPC rivals. It is a tuning tool: before any stage is built we check, in
numbers, that every stage takes real effort, no strategy is a shortcut, the leader does not run away, upkeep rises with
size and money always has a use (docs/game-design.md "The five numbers", critique P3, P5, P6). Spec 053 reads the
stage 1 numbers from here.

## Depends on
Design v1 (question 13); D23 (whole dollars) and open question 17 (cents); D27 (native vegan); spec 008
(`IRandom`, `SeededRandom`, `EconomyRng.ForTick`); `ContentRules.FindForbidden` (the D27 word check).

## Scope
Only `/sim`, plus one wrapper script.
- `sim/Runtime/Climb/` (new, namespace `Worktown.Sim.Climb`, one public type per file): `ClimbStage.cs`,
  `PlayerProfile.cs`, `DishDef.cs`, `ChannelDef.cs`, `RivalDef.cs`, `PurchaseKind.cs`, `PurchaseEffect.cs`,
  `PurchaseDef.cs`, `StageDef.cs`, `BigJobKind.cs`, `BigJobDef.cs`, `ChoiceWeights.cs`, `ClimbCatalog.cs`,
  `PricePolicy.cs`, `SpendPolicy.cs`, `ClimbStrategy.cs`, `CrewSetup.cs`, `RivalState.cs`, `ClimbState.cs`,
  `ServiceResult.cs`, `GuestChoice.cs`, `ClimbPolicy.cs`, `ClimbEngine.cs`, `ClimbRun.cs`, `ClimbTargets.cs`,
  `TargetCheck.cs`, `ClimbEvaluation.cs`, `ClimbReport.cs`.
- `sim/Tools~/ClimbReport/ClimbReport.csproj` and `Program.cs` (new console app, net10.0, references
  `../../Sim.csproj`; the `~` keeps Unity out). `sim/Sim.sln` (change: add this project).
- `sim/Sim.Tests~/Climb/` (new tests, see Test plan).
- `scripts/climb-report.sh` (new): `dotnet run --project sim/Tools~/ClimbReport -c Release -- "$@"`.
- This spec (Implementation notes: the final report summary and every tuning change).
- No change to any existing file under `sim/Runtime/` (Cooking, Core, Economy).

## Out of scope
- Anything in `/game`; building any stage (053 and later); the spec 008 hourly market engine (this is a separate,
  service-level model; it does not call `EconomyEngine`).
- Mixed-skill crews, partial crews (players who skip a service), player-set prices beyond the fixed policies, a
  general strategy optimiser, LLM text, saving, UI.
- Changing D23 or any price in the game. Choosing the service length or the pacing bands (open questions).

## Design

### Units, time and determinism
- Money is `long`, whole dollars (D23). `ClimbCatalog.MoneyScale` (default 1) multiplies every money value in the data,
  so a "cents" answer to question 17 is a data change (100), not a code change. No float anywhere in Runtime/Climb.
- All formulas use integers (permille, tenths); divisions floor on non-negative operands.
- **Injected clock:** time is `ClimbState.PlaySeconds`, advanced only by the engine by `ServiceSeconds +
  BetweenSeconds` per service (both from `CrewSetup`). Runtime/Climb never reads `DateTime`, `Stopwatch` or
  `Environment.TickCount`; the CLI may print its own run time to stderr only.
- **Seeded RNG:** one RNG per service, `EconomyRng.ForTick(setup.Seed, serviceIndex)`, with the draw order below. No
  static mutable state. Same `CrewSetup` and catalog give an identical `ClimbRun` and identical report text.

### Data (immutable records; the `ClimbCatalog` constructor validates and throws `ArgumentException`)
```csharp
public enum ClimbStage { FoodCorner = 1, FirstRestaurant = 2, ServiceMix = 3, Automation = 4, BeyondKitchen = 5 }
public sealed record PlayerProfile(string Id, int ActionsPerMinute, int SkillPermille);
public sealed record DishDef(string Id, string Name, int Actions, long IngredientCost, long ReferencePrice, int BaseQualityPermille);
public sealed record ChannelDef(string Id, string Name, ClimbStage Stage, int GuestsPerService, int ActionsPermille,
    int PricePermille, int WorkSpots, long Upkeep);                       // a "service" of D34: counter, delivery, tables...
public sealed record RivalDef(string Id, string Name, string ChannelId, int QualityPermille, int PriceOffset,
    int WaitSeconds, int ReputationPermille, int MaxPriceCuts, int MaxQualitySteps);
public enum PurchaseKind { Gate, Productive, Status }
public sealed record PurchaseEffect(int ActionsSaved, string? SavedOn /* dish id or "cleaning" */, int QualityBonus,
    int CostSavedPermille, int CeilingBonusPermille, string? CeilingChannel /* null = all */, int ReputationBonus,
    int StaffActionsPerMinute, int StaffSkillPermille, string? UnlocksChannel, string? UnlocksDish);
public sealed record PurchaseDef(string Id, string Name, ClimbStage Stage, PurchaseKind Kind, long Cost,
    long UpkeepPerService, PurchaseEffect Effect);
public sealed record StageDef(ClimbStage Stage, int WorkSpots, int OverflowPermille, int PaymentActions,
    int CleaningActions, long Upkeep, int MaxDishes, string? GatePurchaseId, int GateStarsTenths, int GateWindow,
    int GateMinChannels, bool GateFewPlayers, int ReputationFloorPermille);   // gate = how you move to the next stage
public enum BigJobKind { Competition, Event }
public sealed record BigJobDef(string Id, string Name, BigJobKind Kind, long Deposit, int Guests, int PricePermille,
    int RivalScorePermille, long Prize, string? RequiresPurchase, int CooldownServices);
public sealed record ChoiceWeights(int Quality, int Reputation, int Price, int Wait, int Variety, int Outside, int Min);
```
`ClimbCatalog(profiles, dishes, channels, rivals, purchases, stages, bigJobs, weights, stage1DishId, startingMoney,
moneyScale)` copies its inputs; `static ClimbCatalog CreateDefault()`. Validation: ids `^[a-z0-9_]{1,32}$`, unique per
kind; every `Name` passes `ContentRules.FindForbidden(name) == null` (D27; names are hand-written, never generated);
exactly one `StageDef` per stage; stage 1 `MaxDishes == 1`; references (channel, dish, purchase) exist; permille
fields in 0..5000, actions >= 1, money >= 0; `ReferencePrice > IngredientCost`.

### Default catalog (all numbers PROVISIONAL; tuning changes only these)
- **Profiles** (APM, skill): novice 18/450, average 26/650, expert 36/850.
- **Dishes** (actions, cost, ref price, quality): fries 8/1/4/600 (the stage 1 dish), falafel_wrap 14/3/9/650,
  seitan_wrap 16/3/10/680, grilled_veggies 12/2/8/620, lemonade 4/1/3/600. Starting money $25.
- **Stages** (work spots, overflow, pay actions, cleaning, upkeep, max dishes, gate, rep floor):
  1: 2, 250, 3 (cash), 0, $5, 1, buy `rent_restaurant` $400, 300.
  2: 6, 200, 1 (card), 1, $50, 4, 3.5 stars over 5 services + buy `service_license` $1,500, 450.
  3: 6, 200, 1, 1, $80, 5, 2 extra channels owned + 4.0 stars over 5, 550.
  4: 6, 200, 1, 1, $120, 5, few-players check + 4.0 stars over 5, 600.  5: 6, 200, 1, 1, $160, 5, none, 600.
- **Channels** (stage, guests per service, actions, price, extra work spots, upkeep): corner_counter 1/40/1000/1000/0/0
  (stage 1 only); street_counter 2/90/1000/1000/0/0 (replaces the corner from stage 2); delivery 3/50/1300/1100/2/$25;
  custom_orders 3/20/1600/1600/1/$10; tables 3/45/1200/1150/3/$20; buffet 3/40/500/900/1/$30.
- **Rivals** (channel, quality, price offset, wait s, rep, max cuts, max quality steps): "Corner Kiosk"
  corner_counter 450/0/45/500/2/3; "Mira's Wraps" street_counter 600/0/75/600/3/4; "Dash Bowls" delivery
  520/-1/90/550/3/3; "The Long Table" tables 650/+1/120/650/2/4. Custom orders and buffet: no rival.
- **Purchases** (stage, kind G gate / P productive / S status, cost, upkeep, effect): S1 `second_basket` P $60 (fries -2 actions), `corner_sign` P $80
  (+100‰ ceiling), `rent_restaurant` G $400. S2 `dish_falafel_wrap` P $200, `dish_seitan_wrap` P $250,
  `dish_grilled_veggies` P $200, `dish_lemonade` P $150, `bulk_supplier` P $400 (-200‰ ingredient cost), `decor_1` P
  $500 (+30 rep), `service_license` G $1,500. S3 `delivery_kit` P $1,800, `custom_counter` P $900, `tables_room` P
  $1,500, `buffet_bar` P $2,000 (each unlocks its channel), `decor_2` P $1,200 (+30 rep). S4 `staff_runner` P $600
  ($35 wage; 22 APM, skill 600), `staff_cook_1` and `staff_cook_2` P $900 ($45; 26 APM, skill 650),
  `auto_fill_drinks` P $1,200 ($5; lemonade -3), `dishwasher` P $1,500 ($5; cleaning -1), `fry_dump` P $1,000 ($5;
  fries -2). S5 `food_truck` P $6,000 ($20), `delivery_car` P $3,000 ($10; +200‰ delivery ceiling), `ingredient_lab`
  P $4,000 ($20; +60 quality), `campaign_1..3` P $1,000/$2,000/$4,000 (+100‰ ceiling each), `show_car` S $15,000
  (+20 rep), `decor_3..6` S $2,500/$5,000/$10,000/$20,000 (+10 rep each).
- **Big jobs** (stage 5, repeatable): `cook_off` Competition, deposit $800, rival score 800, prize $4,000, cooldown 4;
  `street_festival` Event, $1,500, 150 guests, price 1100‰, needs `food_truck`, cooldown 4; `stadium_day` Event,
  $2,500, 250 guests, price 1200‰, needs `food_truck`, cooldown 6.
- **Choice weights:** Quality 900, Reputation 700, Price 1500, Wait 300, Variety 80, Outside 700, Min 20.

### One service (`ClimbEngine.RunService(ClimbState, CrewSetup) -> (ClimbState, ServiceResult)`)
1. **Policy** (`ClimbPolicy`, before the service, no draws): buy per `SpendPolicy`, set prices per `PricePolicy`
   (below). A gate whose conditions hold is bought (or passed, if it has no purchase): `Stage + 1`, record
   `StageEnteredSeconds[stage] = PlaySeconds`. A stage is never lost.
2. **Capacity:** `min = ServiceSeconds / 60`; `W = stage.WorkSpots + sum(owned channel WorkSpots)`; actions =
   `APM * min` for the first `min(N, W)` players, `APM * min * OverflowPermille / 1000` for each player beyond `W`,
   plus `StaffActionsPerMinute * min` per owned staff purchase.
3. **Guests (demand ceiling per channel):** for each active channel in id order, `ceiling = GuestsPerService *
   (1000 + sum CeilingBonusPermille) / 1000`. Attraction per shop (you, each rival on that channel, and one "outside"
   option = `Outside`): `A = max(Min, 1000 + Quality*(q-500)/1000 + Reputation*(rep-500)/1000 -
   Price*(offset*1000/avgRef)/1000 - Wait*max(0, wait-60)/60 + Variety*(dishes-1))`. You use last service's average
   quality and wait; `offset` is your average (price - ref). For each of `ceiling` guests, one draw
   `NextInt(0, sum A)` picks the shop; your guests then draw a dish uniformly from your menu. Guests never exceed
   the ceiling.
4. **Serve:** your guests in draw order; each costs `max(1, dish.Actions * channel.ActionsPermille / 1000 - saved)
   + PaymentActions + CleaningActions - savedCleaning`. A mistake draw `NextInt(0,1000) < (1000 - skill) / 8` costs
   the actions and the ingredients again. Out of actions: the guest is missed. `skill` is the action-weighted average
   of players and staff. Quality per dish: `clamp(base + (skill-500)*600/1000 + QualityBonus + NextInt(-40, 41), 0,
   1000)`.
5. **Stars, wait, reputation:** `wait = 40 + 40 * chosen / max(1, served)` s; stars = `1 + min(4, q / 200)`, minus 1
   (min 1) if `wait > 120`; a missed guest counts 1 star at half weight. `rep += (starsPermille - rep) * 300 / 1000`,
   clamped to `[stage.ReputationFloorPermille, 1000]` (critique P4: never below the stage floor), plus owned rep bonuses.
6. **Money:** revenue = sum of `price * channel.PricePermille / 1000`; costs = ingredients (less `CostSavedPermille`)
   incl. remakes; upkeep = stage upkeep + channel upkeep + purchase upkeep (wages). Balance may go negative (no
   bankruptcy); while it is below 0 the policy buys nothing.
7. **Rivals react** (`RivalState`): after 3 services in a row where your share of its channel beats the rival's, it
   cuts its price by 1 unit (up to `MaxPriceCuts`) or, when cuts are used up, raises quality by 25 (up to
   `MaxQualitySteps`); after 3 services in a row where its share is twice yours it undoes one step. Bounded, no draws.
8. **Big jobs** (stage 5): if a job is off cooldown, its requirement owned and `Balance >= 2 * Deposit`, the first
   ready one by id replaces the normal service. Competition: crew score = average skill of the best `min(N, 4)`
   players + quality bonuses + `NextInt(-100, 101)` against `RivalScorePermille + NextInt(-100, 101)`; a win pays the
   prize and adds a trophy. Event: `Guests` guests, no rivals, served as in step 4 at `PricePermille`.
9. `PlaySeconds += ServiceSeconds + BetweenSeconds`. **Draw order:** step 3 (per channel, per guest: shop, then dish),
   step 4 (per served guest: mistake, then quality), step 8.

**Few-players check** (stage 4 gate, no draws): with `F = min(N, 2)` players plus staff and machines, the expected
capacity (mistakes at their mean rate) serves at least 900‰ of the guests who chose you last service.

### Policies (the strategy grid: 5 price x 3 spend = 15 strategies)
- `PricePolicy { Minus1, Match, Plus1, Plus2, Undercut }`: price = ref + {-1, 0, +1, +2} units; Undercut = the counter
  rival's price - 1 (Match without a rival). Never below `IngredientCost + 1` unit.
- `SpendPolicy { Rush, Balanced, Builder }`; candidates in order (stage, cost, id):
  - **Rush:** the gate when ready; a Productive item only if `Balance - cost >= gate cost`.
  - **Balanced** (the reference strategy with `Match`): the gate when ready; a Productive item if `Balance >= 2 * cost`.
  - **Builder:** every affordable Productive item of the stage first; the gate only when none is left unowned.
  - All policies: Status items only in stage 5 when no Productive item is left, if `Balance >= 2 * cost`.

### Runs, targets and the report
- `CrewSetup(int Players, string ProfileId, ClimbStrategy Strategy, ulong Seed, int ServiceSeconds = 360,
  int BetweenSeconds = 60)`; `ClimbEngine.Run(CrewSetup, long horizonSeconds) -> ClimbRun` (hours to each stage or
  DNF, per-stage services, net money per hour, upkeep share of revenue, your share per channel, missed share, average
  stars, idle-money services, a canonical text and its SHA-256 digest).
- `ClimbTargets` (defaults; open question 1) and `ClimbEvaluation.Evaluate(runs, targets) -> IReadOnlyList<TargetCheck>`
  (`TargetCheck(string Id, bool Passed, string Detail)`). Medians are over seeds:
  - **T1 pacing bands** (average profile, Balanced+Match, every crew size), cumulative play hours to enter a stage:
    S2 0.75-1.5, S3 3-5, S4 7-11, S5 12-20.
  - **T2 real effort:** every stage lasts at least 3 services for every profile and strategy.
  - **T3 skill matters:** expert hours to S5 <= 0.8 x novice; novice reaches S5 within the 40 h horizon.
  - **T4 no shortcut:** per crew size and profile, the fastest strategy to S5 is >= 0.85 x the median of the
    strategies that finish (critique P5: no strategy more than about 15 % better).
  - **T5 no runaway:** your share of every rival channel, averaged over the last 10 services of each stage, <= 75 %;
    each stage lasts at least 0.5 x the previous one (no snowball).
  - **T6 upkeep rises:** median upkeep share of revenue S1 < S2 < S3 < S4, and <= 70 % in every stage.
  - **T7 money has a use:** at every service end before the horizon there is an unowned purchase or a ready big job
    with cost between 3x and 20x the mean net income of the last 5 services (something worth saving for, within
    reach); zero idle-money services.
  - **T8 no stuck debt:** the reference strategy never stays below $0 for more than 5 services in a row.
- `ClimbReport.Format(runs, checks) -> string`: one section per player count (rows profile x strategy: hours to S2..S5
  as median [min-max], DNF count; then per stage for the reference strategy: services, net $/h, upkeep %, share %,
  missed %, stars), then one line per target (`PASS T1 4p average S2 1.1 h in 0.75-1.5`), last line `RESULT: PASS
  (k/k)` or `RESULT: FAIL (k/n)`.
- CLI: `bash scripts/climb-report.sh [--players 1,4,16] [--profiles novice,average,expert] [--seeds 5]
  [--hours 40] [--service-seconds 360] [--between-seconds 60] [--out file]`. Exit 0 all pass, 2 a target fails,
  1 bad arguments.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (sim tests, no UnityEngine); `git diff --stat main -- sim/Runtime/Cooking
      sim/Runtime/Core sim/Runtime/Economy` is empty.
- [ ] AC2: Determinism: the same `CrewSetup` twice gives equal `ClimbRun` digests and an identical report string;
      seeds 1 and 2 give different digests. `grep -rnE "DateTime|Stopwatch|TickCount|System.Random" sim/Runtime/Climb`
      finds nothing.
- [ ] AC3: Guest choice: two equal shops and no outside option split 10,000 guests within 50 % +- 3 %; higher quality,
      higher reputation, lower price or shorter wait each raise the share; attraction is never below `Min`; guests
      per channel never exceed the ceiling with all bonuses.
- [ ] AC4: Demand ceiling: at stage 1, 16 average players earn at most 1.15 x the median net income per service of 4.
- [ ] AC5: Capacity: players beyond the work spots add exactly `OverflowPermille`; a solo novice serves fewer guests
      than a solo expert; a mistake costs actions and ingredients twice.
- [ ] AC6: Floors: a state forced to -$100 buys nothing until its balance is >= 0; the stage never decreases;
      reputation never goes below the stage floor.
- [ ] AC7: Rivals: three services ahead trigger exactly one reaction; cuts and quality steps never exceed their
      maximums; the undo rule works.
- [ ] AC8: Each `PricePolicy` and `SpendPolicy` decision is tested on a hand-built state; Builder buys the gate only
      when no Productive item of the stage is unowned.
- [ ] AC9: Catalog validation: duplicate id, bad id, missing reference, stage 1 with 2 dishes, a name with a D27
      forbidden word, and `ReferencePrice <= IngredientCost` each throw; `CreateDefault()` does not.
- [ ] AC10: Each target T1-T8 has a passing and a failing test on hand-built `ClimbRun` data.
- [ ] AC11: `bash scripts/climb-report.sh --players 1,4,16 --seeds 5` prints one section per player count, all target
      lines and the RESULT line, in under 60 s, exit 0 or 2.
- [ ] AC12 (tuning): the default catalog passes T1-T8 for 1, 4 and 16 players (exit 0). Tuning only changes numbers
      in `ClimbCatalog.CreateDefault()`, each change logged in Implementation notes. If a target still fails after 3
      tuning rounds, stop: that is a design finding. Write the failing lines and your best guess under Blockers.
- [ ] AC13: Every public method in `sim/Runtime/Climb` has at least one test; the Climb tests add < 15 s to
      `dotnet test`.

## Test plan
- `sim/Sim.Tests~/Climb/`: `ClimbCatalogTests` (AC9, D27 names), `GuestChoiceTests` (AC3), `ClimbEngineTests`
  (AC4-AC7, one service step by step on a tiny catalog), `ClimbPolicyTests` (AC8), `ClimbEvaluationTests` (AC10),
  `ClimbDeterminismTests` (AC2), `ClimbReportTests` (format, sections, RESULT line). A small `TestClimb.cs` fixture
  catalog (1 channel, 1 rival, 2 stages) keeps unit tests fast; the full grid runs only in the CLI (AC11, AC12) and
  in one smoke test (4 average players, Balanced+Match, 1 seed, 20 h).
- Command: `bash scripts/verify.sh`; fast loop `dotnet test sim/Sim.sln`; report `bash scripts/climb-report.sh`.

## Human steps
Answer the open questions (or accept the defaults), then read the report and say whether the pacing feels right.

## Open questions (recommended default in brackets)
1. Pacing bands (hours of play to enter S2/S3/S4/S5 for an average crew). [0.75-1.5 / 3-5 / 7-11 / 12-20 h, from the
   critique's milestones: a 6-10 h climb to stage 3 and a 12-20 h Early Access climb]
2. Service length. [360 s, today's 08:00-14:00 shift at 60 s per hour, plus 60 s between services; the critique
   suggests 15-25 min; the CLI takes `--service-seconds` so both can be compared]
3. Solo scaling (critique P3: fewer guests and more patience for small crews). [off in the data; the report shows the
   solo missed-guest share, and a later spec decides]
4. Every player present every service. [yes; partial crews later]
5. Money unit (question 17, D23). [whole dollars, `MoneyScale = 1`; cents would be `MoneyScale = 100`. Not decided here]
6. Price level. [street-food values as in spec 037: fries $4, a wrap $9-10]
7. The stage 1 dish. [fries, the same default as 053]
8. Shortcut threshold. [15 % (T4)]
9. Staff quality (docs/setting.md, a draft, says robots never reach top quality). [no special cap: staff skill 600-650
   already keeps them below an average player's best]
10. Which numbers become binding for the game. [053 reads stage 1 (gate, upkeep, starting money, the dish); later
    specs read the rest after tuning]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
