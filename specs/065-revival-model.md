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
Built 2026-11-11 (builder) on `spec/065-revival` from main 3f61ca5. Sim tests 723 -> 820 (+89 Revival, about 0.5 s; +8
Town). `env -u UNITY_PATH bash scripts/verify.sh` green (Unity skipped). Nothing under `sim/Runtime/Climb`, `Cooking`,
`Core`, `Economy` or `game` changed.

**Needs a human nod (Open question 1): the crew curve's premise does not hold.** 052b's guest demand is D = n / 4 with no
plateau (spots and staff scale too), so a 16-player crew serves 4x the meals of 4 (Main Street meals over 40 h: 1p 6,819,
4p 25,741, 16p 104,065). With the spec's rows (1000 at 16) 16 players revived 2.4-2.6x faster than 4 (R0: V1, V2 and V3
failed for 16p). Round 1 sets the rows from 4 players to 250 x n (4000 at 16, inside the validated 1..5000): D40's "goals
grow with players x difficulty". AC5's "16 players need the same as 4" is tested on a catalog with the spec's literal rows
(`TestRevival.Spec()`); the default now gives every crew the same pace instead.

Interpretations and deviations:
- Scope additions: `RevivalTrace.cs` (the replay result needs a public type of its own). The `RevivalCatalog` constructor
  takes the `TownCatalog` first, so "one row per town district" can be validated. Lists are `IReadOnlyList`, not
  `ImmutableArray` (as 064: no System.Collections.Immutable in /sim). `stageBonusPpm` is validated 0..999,999 (not listed).
- Engine: Thriving's progress is always 1,000,000 (there is no next goal). A level-up's rest is rescaled by the ratio of the
  level goals (district and crew factors cancel; `EnterStage` has no crew). Each call floors its ppm, so split calls can
  lose 1 ppm against one call. Level-ups list home first, then neighbours. `FromSaved` reads up to the shortest of the
  three lists, the first entry of a repeated id wins, a null save gives the start state.
- Replay: the first service has no "service before"; a jump of several stages gives each stage's bonus. Meals of one service
  are added per district in catalog order. `RevivalPlan` adds `HomeDistrict` (channels without a share) and
  `CompetitionJobIds` (`cook_off`; a competition serves 0 guests anyway).
- Targets: V1, V2 compare the median level hour with the median stage entry hours (as T1); a DNF S5 bounds Thriving by the
  horizon. V3 runs for every crew other than 4, V5 for every crew, V4 for 4 players. V6 is counted at the top footfall
  level; INFO lines show the lower levels and the plan's 1100, 1200 and 1300. `RevivalEvaluation.WithFootfall` rebuilds
  the climb catalog (money divided back by `MoneyScale`); at 1000 it gives the same run digest (test).

**064 review nits** (the coordinator's request; outside this spec's Scope and AC1's Town line, so listed here):
1. Node 1101 moved from (-212, 95), in no district, to (-222, 100) in Tower Rise (ramp slopes 39 and 45 permille). The
   catalog now rejects a node outside its district's areas (a district without areas stays C8's finding).
2. `TownRect.Metres(x0, z0, x1, z1)` now has the constructor's order; every caller was swapped.
3. Documented: the area check tests corners only, exact for a convex play area (a concave one is still allowed, a 064
   test uses an L-shape); a test pins the default as convex.
4. `TownCheck` details, `TownPoint.ToString` and `TownRect.ToString` use the invariant culture (tested with a culture
   whose minus sign is "~").
5. Node 605 and Airlock 2 stay at x 240-260: the edge 604-605 is now a 4 m jetty deck over the lake, like 607.
`bash scripts/town-report.sh`: PASS (12/12); only Tower Rise walks change (+4.3 s: sky_deck, tower_1, tower_2, airlock_1).

**Tuning log** (`--players 1,4,16 --seeds 5`, under 5 s):
- R0 (the spec's numbers): FAIL (7/19). 16p 2.4-2.6x fast (above); solo Lively 6.2 h after its S4 (4.4 h), Thriving
  19.3 h; Workshop Lane and Greenhouse Hill never Patched (spill does not travel, the stage bonus is only 40 %), so V4
  and V5 failed; V6 at 1.3x moved 4p S4 to 6.1 h (band 7-11).
- R1: crew rows 290 / 540 / 800, then 250 x n from 4; level goals 300 / 4,000 / 20,000 -> 400 / 3,100 / 15,000 (solo
  must reach Lively before its own S4 but no faster than 0.67x of 4p); delivery Homes 350, Workshop Lane 120,
  Greenhouse Hill 120, Tower Rise 100, Festival Meadow 100, Garden Promenade 60, Low Ring 50, Station Square 50;
  footfall 1000 / 1040 / 1070 / 1100 (1.2x already fails T1 S4). Result: **PASS (19/19)**, exit 0. Rounds 2-3 not needed.

**Final report** (median hours; S2/S3/S4/S5 entry 1p 0.7/2.1/4.4/9.2, 4p 1.2/3.9/7.9/15.3, 16p 1.2/3.9/7.9/15.1):

| crew | Main Street Patched / Lively / Thriving | last district Patched | Low Ring Patched |
|---|---|---|---|
| 1 | 1.1 / 4.2 / 11.9 | Low Ring | 34.4 |
| 4 | 1.4 / 4.1 / 13.9 | Low Ring | 22.5 |
| 16 | 1.3 / 4.1 / 13.8 | Low Ring | 24.2 |

Main Street turns Patched near the first restaurant and Lively at the start of the service mix. Margins are thin: solo
Lively 4.2 h against its S4 at 4.4 h, 4p Lively 4.1 h against S3 at 3.9 h. Other districts (4p) reach Patched at 3-23 h
(spill and delivery) and seven reach Lively at 19-34 h; only Main Street and the Festival Meadow (events) reach Thriving within
40 h. V6 at 1.1x: 4p S2/S3/S4/S5 1.2/3.5/7.2/13.9 h (S4 is 0.2 h inside its band).

Climb report: identical before and after (`RESULT: FAIL (135/143)`, exit 2, as on main); revival feeds nothing back.

## 065 review fixes
Review: APPROVE WITH NITS. The coordinator decided Open question 1 as option (b), D40 literally. Not committed. Sim
tests 820 -> 823; `env -u UNITY_PATH bash scripts/verify.sh` green.
- **Crew factors:** 1-3 players now use n x h(n) / 4 with 052b's h = 0.55 / 0.75 / 0.9, which gives 137 / 375 / 675. From 4
  players the factor stays 250 x n.
- **V3 rule:** the lower edge is `FairMinPermille` x h(n) / 1000, as T1's lower edge (0.36x solo, 0.50x for 2, 0.60x for 3).
  The upper edge stays at 1.5x. So a crew whose climb is h times faster may revive h times faster, never slower than 1.5x
  of 4 players. V1 and V2 tie Patched to the crew's own S2 and Lively to its own S3..S4. `RevivalTrace.DifficultyPermille`
  carries h(n) from the run's `CrewFactors`.
- **Nits:**
  - `EnterStage`: the dead `Math.Min(ppm, Ppm - 1)` guard is removed; a comment says why one level per call holds.
  - `pointsByStars` is bounded at 1000 per meal, and `AddMeals` uses `checked` arithmetic (overflow throws).
  - `RevivalReplay.Replay` has an XML doc.
- **Report** (`--players 1,2,3,4,8,16 --seeds 5`): **PASS (37/37)**, exit 0. No other number changed. Main Street median
  hours (Patched / Lively / Thriving; own S2 / S3 / S4 / S5 entry; Low Ring Patched):

| crew | Main Street | stages | Low Ring |
|---|---|---|---|
| 1 | 0.8 / 2.2 / 7.9 | 0.7 / 2.1 / 4.4 / 9.2 | 25.8 |
| 2 | 1.1 / 3.2 / 11.1 | 0.9 / 2.8 / 6.4 / 12.6 | 20.3 |
| 3 | 1.2 / 3.6 / 12.5 | 1.1 / 3.3 / 7.0 / 13.9 | 20.3 |
| 4 | 1.4 / 4.1 / 13.9 | 1.2 / 3.9 / 7.9 / 15.3 | 22.5 |
| 8 | 1.3 / 4.1 / 13.9 | 1.2 / 3.9 / 7.9 / 15.2 | 24.6 |
| 16 | 1.3 / 4.1 / 13.8 | 1.2 / 3.9 / 7.9 / 15.1 | 24.2 |

  Thin margin: solo Lively is at 2.2 h against its S3 at 2.1 h.

## Blockers
None. Tuning passed in round 1.
