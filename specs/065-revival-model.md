---
id: 065
title: Revival model in /sim (district levels 0-3 from served meals by quality, thresholds by crew, never going back, effects) and its climb-sim report
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W3 of docs/town-plan.md section 10.

# 065: Revival model

## Goal
The core of the town plan (D43) as pure /sim code: each district has a revival level, 0 Grey, 1 Patched, 2 Lively, 3
Thriving. Meals the players serve add points to the district where they are eaten, weighted by quality; thresholds grow
with the crew (D40) and progress is stored as a fraction of the goal, so joining and leaving is fair; a level is never
lost. Levels raise footfall and the quality ceiling of ingredients on sale. A report replays the 052b climb to tune the
numbers so the town comes back at the pace of the climb. Spec 068 runs it in the game.

## Depends on
**Spec 064 built** (district ids, neighbours) and **spec 052b built** (crew table, `ClimbRun`, the reference strategy).
D37, D40, D41, D43, D44; docs/town-plan.md section 4; spec 066 is not needed.

## Scope
- `sim/Runtime/Revival/` (new, namespace `Worktown.Sim.Revival`, one public type per file): `RevivalLevel.cs`,
  `RevivalCrewRow.cs`, `DistrictRevivalDef.cs`, `RevivalUnlock.cs`, `RevivalCatalog.cs`, `DistrictRevival.cs`,
  `RevivalState.cs`, `SavedRevival.cs`, `LevelUp.cs`, `RevivalEngine.cs`, `RevivalEffects.cs`, `RevivalPlan.cs`,
  `RevivalReplay.cs`, `RevivalTargets.cs`, `RevivalEvaluation.cs`, `RevivalReport.cs`.
- `sim/Tools~/RevivalReport/RevivalReport.csproj`, `Program.cs` (new console app, net10.0); `sim/Sim.sln` (change);
  `scripts/revival-report.sh` (new).
- `sim/Sim.Tests~/Revival/` (new tests). This spec (Implementation notes, tuning log, Blockers).
- No change to `sim/Runtime/Climb`, `Town` or any other folder; no `/game` change.

## Out of scope
The game side (068), looks (068), text (070), applying the ingredient ceiling to the item rater or the supplier (a
later spec), deliveries in the game, NPC rivals' meals (only the players' meals count), robot repair (D47, later).

## Design

### Data (immutable; the `RevivalCatalog` constructor validates and throws `ArgumentException`)
```csharp
public enum RevivalLevel { Grey = 0, Patched = 1, Lively = 2, Thriving = 3 }
public sealed record RevivalCrewRow(int MaxPlayers, int GoalPermille);
public sealed record DistrictRevivalDef(string DistrictId, int GoalPermille, int StartProgressPpm,
    ImmutableArray<string> Neighbours);                                   // neighbours copied from TownCatalog
public sealed record RevivalUnlock(string Id, string DistrictId, RevivalLevel MinLevel);
public sealed record DistrictRevival(string DistrictId, RevivalLevel Level, int ProgressPpm);   // ppm 0..1,000,000
public sealed record LevelUp(string DistrictId, RevivalLevel NewLevel);
```
`RevivalCatalog(levelGoals /* 3 points: to L1, L2, L3 */, pointsByStars /* 5: stars 1..5 */, spillPermille, crew,
districts, stageBonusPpm, footfallPermille /* 4 */, stageIngredientCeiling /* 5 */, greenhouseCeilingBonus /* 4 */,
unlocks)`; `static RevivalCatalog CreateDefault(TownCatalog town)` (one row per town district, in town order).
Validation: one row per town district, no other; goals > 0 and rising; points >= 0 and non-decreasing; spill 0..500;
crew rows sorted, last `MaxPlayers >= 16`, permille 1..5000; start ppm 0..999,999; footfall non-decreasing from 1000;
ceilings 0..1000; unlock ids `^[a-z0-9_]{1,32}$`, districts known.

### Default numbers (provisional; tuning changes only these and `RevivalPlan.Default`)
- `levelGoals` 300 / 4,000 / 20,000 points; `pointsByStars` 0, 1, 2, 4, 8 (a poor dish counts almost nothing, a
  great one much more; printer food is not a player meal and counts nothing).
- District `GoalPermille` 1000, except `low_ring` 1500 (the last to come back, town-plan 4.1); `StartProgressPpm` 0,
  except `station_square` 500,000 ("a little warmth around the station").
- `spillPermille` 250: a quarter of each meal's points is split equally among the district's neighbours (they do not
  spill further), so the revival spreads from where the crew works (open question 3).
- Crew rows (the 052b demand curve, D40: demand per crew stops growing at 4, so goals do too): 1 player 450, 2: 650,
  3: 850, 7: 1000, 16: 1000.
- `stageBonusPpm` 100,000 for every district on entering stages 2-5 ("a small bonus at each climb stage").
- `footfallPermille` 1000 / 1100 / 1200 / 1300 (town-plan open question 5).
- Ingredient ceiling (the best quality on sale, permille): stage 1..5 650 / 720 / 790 / 860 / 930, plus Greenhouse
  Hill's level 0 / 20 / 40 / 70, at most 1000 (town-plan 4.4: worn seed stock first).
- Unlocks: `robot_workshop` (workshop_lane, Lively; town-plan 4.5), `greenhouse_stall` (greenhouse_hill, Patched:
  better seed stock in the Market Hall).

### The engine (`RevivalEngine`, static, pure; levels never go down)
- `RevivalState Start(RevivalCatalog c)`: every district at Grey with its start progress.
- `(RevivalState, IReadOnlyList<LevelUp>) AddMeals(RevivalState s, RevivalCatalog c, string districtId, int stars,
  int count, int players)`: `milli = pointsByStars[stars - 1] * 1000 * count`; the home district gets `milli - spill`,
  each neighbour `spill / n` (`spill = milli * spillPermille / 1000`, floor; the remainder stays home). For each
  district that gets points: `goalMilli = levelGoals[level] * 1000 * d.GoalPermille / 1000 * crew(players) / 1000`;
  `ppm += milli * 1,000,000 / goalMilli` (long, floor). On `ppm >= 1,000,000`: level + 1, the rest carries rescaled to
  the next goal (`(ppm - 1e6) * goal(L) / goal(L + 1)`), repeated while it holds; at Thriving ppm is clamped at 1e6.
  `stars` 1..5 and `count >= 0`, else `ArgumentOutOfRangeException`. `crew(players)` = the first row with `players <=
  MaxPlayers`; players clamp to 1..16.
- `EnterStage(s, c, int stage)`: for stage 2..5 every district gets `stageBonusPpm` at its level (at most one level each
  call); stage 1 does nothing.
- Joining and leaving: progress is stored as ppm of the goal, so a crew change only changes the size of the next steps.
- `RevivalState`: `Districts` in catalog order, `LevelOf(id)`, `ProgressPpm(id)`, `Canonical()` and `Digest` (SHA-256).
- Saved form: `SavedRevival(ImmutableArray<string> DistrictIds, ImmutableArray<int> Levels, ImmutableArray<int>
  ProgressPpm)`; `state.ToSaved()`; `RevivalState.FromSaved(c, saved)` clamps levels 0..3 and ppm 0..1e6, ignores
  unknown ids, gives missing districts their start values. Loading never rebuilds levels from points.

### Effects (`RevivalEffects`, static, pure)
`int FootfallPermille(c, RevivalLevel)` (064's `revivalPermille` input); `int IngredientCeilingPermille(c, int stage,
RevivalLevel greenhouseLevel)`; `bool IsUnlocked(c, RevivalState s, string unlockId)`. The ingredient ceiling sits on
top of today's item quality (specs 028, 034) and replaces nothing; how the game applies it is a later spec.

### The climb replay (`RevivalReplay.Replay(ClimbRun run, RevivalCatalog c, RevivalPlan plan) -> RevivalTrace`)
- Per service in order: a higher `Stage` than the service before calls `EnterStage`. A normal service splits `Served`
  over its channels in proportion to `YourGuests` (largest remainder, channel id order), then each channel's meals over
  `plan` districts (largest remainder); stars `= 1 + min(4, AvgQuality / 200)` (052 step 5); players `= Setup.Players`.
  An Event big job puts `Served` into `plan.EventDistrict`; a competition adds nothing.
- `RevivalPlan.Default`: walk-up channels (`corner_counter`, `street_counter`, `custom_orders`, `tables`, `buffet`) all
  to `main_street` (the shop at `s1`); `delivery` to `homes` 500, `tower_rise` 200, `low_ring` 150, `station_square`
  150; events to `festival_meadow`.
- `RevivalTrace`: per district the play hour (service end) of each level-up, and whether any level ever dropped.

### Targets (`RevivalTargets` defaults; `RevivalEvaluation.Evaluate` gives 052's `TargetCheck` lines; medians over seeds,
average profile, reference strategy Balanced+Match, crews 1, 4 and 16)
- **V1** the shop district (`main_street`) reaches Patched between S2 entry - 0.5 h and S2 entry + 1.0 h.
- **V2** it reaches Lively between S3 and S4 entry, and Thriving between S4 entry and S5 entry + 5 h.
- **V3** fair crews: for 1 and 16 players, hours to the shop district's Patched and Lively are 0.67-1.5x those of 4.
- **V4** every district reaches Patched before the 40 h horizon (4 players).
- **V5** the Low Ring comes back last: for each level it reaches, no other district reaches that level later.
- **V6** the footfall bonus is safe: a climb whose channel `GuestsPerService` are all multiplied by
  `footfallPermille[3] / 1000` (the worst case, from the first service) still passes 052's T1 bands for 4 players.
  `INFO V6` lines also show 1100 and 1200.
- **V7** no level ever drops in any replay.

### Report and tuning
`RevivalReport.Format(...)`: per crew size a table of districts x hours to each level; the shop district against the
stage entry hours; the `TargetCheck` lines; `RESULT: PASS (k/k)` or `FAIL (k/n)`. CLI: `bash scripts/revival-report.sh
[--players 1,4,16] [--seeds 5] [--hours 40] [--out file]`; exit 0 pass, 2 a target fails, 1 bad arguments; under 60 s.
Tuning: at most 3 rounds, changing only numbers in `RevivalCatalog.CreateDefault()` and `RevivalPlan.Default` (V6 may
lower `footfallPermille`), each change logged. Still failing after 3 rounds is an allowed outcome: write the failing
lines and a best guess under Blockers (as 052).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim/Runtime/Climb sim/Runtime/Town
      sim/Runtime/Cooking sim/Runtime/Core sim/Runtime/Economy game` is empty.
- [ ] AC2: `grep -rnwE "DateTime|Stopwatch|TickCount|float|double" sim/Runtime/Revival` and `grep -rn
      "System.Random" sim/Runtime/Revival` find nothing; the same inputs give equal digests and report text.
- [ ] AC3: Catalog validation: each rule above has a throwing literal; `CreateDefault(TownCatalog.CreateDefault())`
      does not throw and has 12 rows.
- [ ] AC4: `AddMeals` literals: a 4-star meal for 4 players at goal 300 adds the exact ppm; 1-star meals add nothing;
      spill splits 250 permille over 2 neighbours with the remainder at home; a level-up carries the rescaled rest; two
      level-ups in one call; Thriving clamps at 1e6; bad stars throw.
- [ ] AC5: Crew: 1 player reaches Patched with 450/1000 of the 4-player points; 16 players need the same as 4; a crew
      change between calls keeps every level and ppm.
- [ ] AC6: Never back: 10,000 seeded random calls (meals, stage entries, crew changes) never lower any level or a
      level's ppm except by a level-up.
- [ ] AC7: `EnterStage` gives the bonus for stages 2-5 only, at most one level per call; `FromSaved` clamps, ignores
      unknown ids and fills missing districts; `ToSaved` then `FromSaved` round-trips equal.
- [ ] AC8: Effects: footfall 1000/1100/1200/1300; ingredient ceiling literals for stage 1 at Grey (650) and stage 5 at
      Thriving (1000); `robot_workshop` unlocked only at Workshop Lane Lively.
- [ ] AC9: Replay on a hand-built `ClimbRun` (3 services, 2 channels, a stage change, an event): exact meals per
      district, one `EnterStage`, the event meals in `festival_meadow`.
- [ ] AC10: Each target V1-V7 has a passing and a failing test on hand-built traces.
- [ ] AC11: `bash scripts/revival-report.sh --players 1,4,16 --seeds 5` prints the sections and the RESULT line, under
      60 s, exit 0 or 2. (Tuning to exit 0 as above.)
- [ ] AC12: Every public method in `sim/Runtime/Revival` has a test; the Revival tests add under 10 s.

## Test plan
- `sim/Sim.Tests~/Revival/`: `RevivalCatalogTests` (AC3), `RevivalEngineTests` (AC4, AC5, AC7), `RevivalPropertyTests`
  (AC6, seeded `SeededRandom`), `RevivalEffectsTests` (AC8), `RevivalReplayTests` (AC9, a `TestRuns.cs` fixture),
  `RevivalEvaluationTests` (AC10), `RevivalReportTests` (format). One smoke test runs a 4-player 20 h replay.
- Command: `bash scripts/verify.sh`; fast loop `dotnet test sim/Sim.sln`; report `bash scripts/revival-report.sh`.

## Human steps
Read the report: does Main Street turn Patched near the first restaurant and Lively during the service mix? Answer or
accept the open questions.

## Open questions (recommended default in brackets)
1. Crew curve for revival goals. [052b's guest curve (450/650/850/1000/1000): meals per service stop growing at 4, so
   goals do too. The literal "players x difficulty" would make 16 players four times slower than 4]
2. Points per star. [0/1/2/4/8: a good dish counts much more]
3. Spill to neighbours. [on, 250 permille: without it only the shop's street ever changes; off would keep the plan's
   "where it is eaten" literally]
4. Station warmth at the start. [half way to Patched, still Grey]
5. Stage bonus. [10 % of the current step for every district at stages 2-5]
6. Footfall bonus. [+0/10/20/30 %, the town plan's default, checked by V6 in its worst case]
7. Ingredient ceilings. [the numbers above; applied to the game by a later spec, which also decides whether the
   ceiling caps the rated quality or the supplier's stock grade]
8. Do rival NPC meals count. [no, only the players' meals: the players bring the town back]

## Implementation notes
(Builder fills this in: interpretations, tuning log per round, the final report summary.)

## Blockers
(Builder fills this in after 3 failed tuning rounds.)
