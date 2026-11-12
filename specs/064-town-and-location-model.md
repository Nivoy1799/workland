---
id: 064
title: Town and location model in /sim (districts, places, paths, footfall per district and hour, walking times)
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W2 of docs/town-plan.md section 10.

# 064: Town and location model

## Goal
Haven Nine as plain data in /sim: 12 districts, the places of spec 051 (pitches C1-C8, small places S1-S6, restaurants
R1-R4, the stage 5 places, civic places, delivery addresses, border points), a walking graph with heights, the play area,
safe spots, footfall per district and clock hour, the location and rent factor of each place, and walking times. Spec
065 (revival) reads the districts, spec 067 (graybox) builds the town from this data, later specs read delivery times
and rent factors. Nothing in the game changes yet.

## Depends on
D8, D29, D31, D33, D37, D41, D43, D45; docs/town-plan.md sections 3-5; spec 051 sections 1-4 (history and numbers);
spec 052 (channel ids); spec 062a (integer millimetres). Nothing has to be built first. Runs in parallel with 066.

## Who owns what (overlaps resolved)
| Topic | Owner |
|---|---|
| Districts, places, paths, heights, play area, safe spots, border points as data; walking times | **064** |
| Footfall per district and clock hour; location factor and rent factor of a place; channels a place can run | **064** |
| Channels (counter, delivery, tables...), guests per service at the reference place, channel ceilings, rivals | 052 / 052b (unchanged) |
| Revival levels and their footfall bonus | 065 (064 takes it as a plain permille input) |
| Clock (real seconds to clock hours), weather, the conditions permille | 066 (064 takes it as a plain permille input) |
| Ambient street life: moving street robots, the food printer, the coin ritual, the rankings board dressing, the start cart | 050 |
| Revival look sets (lamps, signs, slumped or working robots, people density) | 068 (supersedes 050's "street robots" as a look) |
| Text in the world (signs, posters, museum, exclamations, robot lines) | 070 (supersedes 050's text parts) |

How 052 and 064 fit: 052 has no districts. Its numbers are "at the reference place", and `Footfall.LocationPermille` is
exactly 1000 at the reference place (`s1`, the shop of 053 and 057). So the climb sim and today's game are unchanged until
a later spec multiplies a channel ceiling (sim) or `TickInput.DemandPermille` (game) by the factor. 064 does not touch
`sim/Runtime/Climb`. Spec 050 got a header note listing the parts that moved to 068 and 070.

## Scope
- `sim/Runtime/Town/` (new, namespace `Worktown.Sim.Town`, one public type per file): `TownPoint.cs`, `TownRect.cs`,
  `FootfallCurve.cs`, `DistrictDef.cs`, `PlaceKind.cs`, `PlaceDef.cs`, `PathNode.cs`, `PathEdgeKind.cs`, `PathEdge.cs`,
  `SafeSpot.cs`, `TownCatalog.cs`, `WalkRoute.cs`, `TownPaths.cs`, `Footfall.cs`, `TownProblem.cs`, `TownCheck.cs`,
  `TownReport.cs`.
- `sim/Tools~/TownReport/TownReport.csproj`, `Program.cs` (new console app, net10.0, refs `../../Sim.csproj`);
  `sim/Sim.sln` (change: add it); `scripts/town-report.sh` (new).
- `sim/Sim.Tests~/Town/` (new tests). This spec (Implementation notes).
- No change to any other folder under `sim/Runtime/`, to `/game` or to other specs.

## Out of scope
Anything in Unity; revival (065); clock and weather (066); applying the factors to the climb sim or the game; rent
amounts in money; NPC crowds; the monorail ride, lifts and the funicular as moving things; seasons.

## Design

### Units and determinism
Integers only: millimetres in the plan (x east, z north, origin at the centre of Fountain Cross), millimetres of
height above the lake, milliseconds, permille. No `float`, `double`, `DateTime`, `System.Random` or static mutable state.
Integer square root (`TownPaths.ISqrt(long)`) for lengths. Every list keeps catalog order; ties break by id ordinal.

### Data (immutable records; the `TownCatalog` constructor validates and throws `ArgumentException`)
```csharp
public readonly record struct TownPoint(int XMm, int ZMm);
public readonly record struct TownRect(int X0Mm, int Z0Mm, int X1Mm, int Z1Mm);          // X0 < X1, Z0 < Z1
public sealed record FootfallCurve(string Id, ImmutableArray<int> HourPermille);          // 24 values, sum 1000
public sealed record DistrictDef(string Id, string Name, ImmutableArray<TownRect> Areas, int HeightMm, bool InCore,
    int DayFootfall, string CurveId, int CeilingPermille, ImmutableArray<string> Neighbours);
public enum PlaceKind { Pitch, Small, Restaurant, StageFive, Civic, Address, Stop, Border }
public sealed record PlaceDef(string Id, string Name, PlaceKind Kind, string DistrictId, TownPoint Anchor, int NodeId,
    int FromStage, int AreaM2, int FrontagePermille, int RentPermille, bool OutdoorSeats, bool EventDaysOnly,
    ImmutableArray<string> Channels);                                                      // channel ids of 052
public sealed record PathNode(int Id, TownPoint At, int HeightMm, string DistrictId);
public enum PathEdgeKind { Path, Ramp, Stairs, Lift, Funicular }
public sealed record PathEdge(int A, int B, PathEdgeKind Kind, int WidthMm, int FixedSeconds); // seconds: Lift, Funicular
public sealed record SafeSpot(int Id, int NodeId);
```
`TownCatalog(districts, curves, places, nodes, edges, playArea /* polygon, counter-clockwise */, safeSpots,
referencePlaceId, startPlaceId, fountainNodeId, domeCentre, domeRadiusXMm, domeRadiusZMm, domeCrownMm,
fallHeightMm)`; `static TownCatalog CreateDefault()`. Validation: ids `^[a-z0-9_]{1,32}$` unique per kind; every `Name`
passes `ContentRules.FindForbidden == null` (D27); curves have 24 values >= 0 summing to 1000; areas and anchors inside
the play area; areas of different districts do not overlap (touching is allowed); each non-Border anchor inside one
of its district's areas; `NodeId` exists and lies within 30 m of the
anchor; edges reference nodes, no self edge, `WidthMm` 1500..20000; neighbours exist and are symmetric; permille fields
0..5000; `FromStage` 1..5; every district has at least one safe spot.

### Default data (provisional; tuning changes numbers only)
Districts (metres; areas are x0..x1, z0..z1; heights above the lake; curves below):

| id | areas | height | core | day footfall | curve | ceiling |
|---|---|---|---|---|---|---|
| `station_square` | -210..-150, -40..40 | 10 | yes | 1400 | commute | 1300 |
| `main_street` | -150..-20, -20..20; 20..150, -20..20 | 10 west to 2 east | yes | 1200 | shops | 1300 |
| `fountain_cross` | -20..20, -20..20 | 6.4 | yes | 1600 | shops | 1300 |
| `market_hall` | -70..-20, -90..-20 | 6 | yes | 1000 | market | 1300 |
| `garden_promenade` | -15..15, 20..170; -15..15, -140..-20 | 6.4 to 12 north, 5 south | no | 700 | evening | 1300 |
| `lakefront` | 150..260, -120..140 | 2 | no | 600 | evening | 1300 |
| `homes` | 30..150, 25..130; 30..150, -130..-25 | 3..6 | no | 400 | commute | 1300 |
| `workshop_lane` | -150..-75, -170..-95 | 7 | no | 350 | shops | 1300 |
| `greenhouse_hill` | -140..120, 175..280 | 15..45 (terraces) | no | 150 | market | 1300 |
| `festival_meadow` | -60..120, -200..-145 | 5 | no | 100 | shops | 1300 |
| `tower_rise` | -380..-220, -40..170 | 16 | no | 500 | shops | 1300 |
| `low_ring` | -380..-220, -200..-40 | 10 | no | 450 | night | 1300 |

- Play area: the rectangle x -380..260, z -200..280 (640 x 480 m). Dome: centre (-60, 40), radii 385 x 320 m (770 x
  640), crown 250 m (town-plan 3.2). Fall height: 10 m below the lowest node. Lake level 0 m east of x 200 (the pier
  reaches x 255).
- Curves (24 permille each; the builder writes the numbers, tests check the shape): `commute` (peaks 07-09 and
  17-19), `shops` (most weight 10-19), `market` (most weight 06-13), `evening` (most weight 16-23), `night` (at least
  half its weight 20-03, the Wreck market and the Low Ring bars).
- Neighbours: districts whose areas touch or are joined by one path edge (the builder lists them; symmetric).
- Places (frontage / rent permille provisional; rent must follow the footfall order, check C9):
  - **Pitches** (stage 1, `corner_counter`, outdoor): `c1` station_square 1100/1100, `c2` main_street west 1000/1000,
    `c3` fountain_cross 1300/1400, `c4` market_hall door 1000/1000, `c5` lakefront pier 900/800, `c6` lakefront beach
    800/700, `c7` garden_promenade park 900/800, `c8` festival_meadow arena gate 1200/600 (event days only).
  - **Small** (stage 2, 60-90 m2, `corner_counter`, `street_counter`): `s1` main_street (**the shop**: anchor at its front
    door, (-80, 2.8) m, 2.8 m north of Main Street's axis like today's road, 75 m2, 1000/1000), `s2` main_street 1000/1000,
    `s3` main_street (a side lane, west of x 20) 900/900, `s4` station_square 1100/1150, `s5` garden_promenade 900/850 (outdoor seats), `s6`
    workshop_lane 700/600 (the cheap one).
  - **Restaurants** (stage 3, 150-300 m2, `street_counter`, `delivery`, `custom_orders`, `tables`, `buffet`): `r1`
    fountain_cross corner 1300/1400 (outdoor), `r2` lakefront 900/900 (outdoor), `r3` garden_promenade 1000/1000
    (outdoor), `r4` station_square 1100/1150.
  - **Stage five** (no channels; big jobs): `sky_deck` tower_rise, `wreck_market` low_ring (west wall), `arena` and
    `meadow_ground` festival_meadow, `ingredient_lab` greenhouse_hill.
  - **Civic:** `station`, `town_hall`, `printer_bar` (station_square), `museum` (main_street, west end), `market_floor`
    (market_hall), `fountain` (fountain_cross), `robot_workshop` (workshop_lane), `crew_house` (homes), `betting_hall`,
    `dream_booths` (low_ring; world content only, D44).
  - **Stops** (monorail, no edges): `stop_station`, `stop_lakefront`, `stop_arena`, `stop_workshop`.
  - **Addresses** (delivery doors): `home_1`..`home_6` (homes), `tower_1`, `tower_2` (tower_rise), `low_1`, `low_2`.
  - **Border** (anchor on the play-area edge): `wreck_bulkhead` (west), `wildbelt_lookout` (south), `water_gate_3`
    (east), `tunnel_door` (north), `airlock_1`..`airlock_3`.
- Nodes and edges (the builder authors them): a node at every place's `NodeId`; along Main Street (axis z = 0) and the
  Promenade (axis x = 0) at most 40 m apart; widths Main Street 12 m, Promenade 10 m, other paths 4-6 m. The Main Street
  nodes within 40 m of `s1` have `s1`'s height (067 keeps the shop on a flat pad). Station Square to Tower Rise: a ramp
  (switchback) plus a `Lift` (20 s); the Promenade's north end to Greenhouse Hill: switchback ramps plus a `Funicular`
  (60 s). `referencePlaceId` = `startPlaceId` = `s1`; `fountainNodeId` at (0, 0).

### Walking (`TownPaths`, built once from a catalog)
- Edge length: horizontal distance (mm). Walk time at 2.5 m/s (D31): `ms = lengthMm * 2 / 5`. `Lift` and `Funicular`
  cost `FixedSeconds * 1000` and are used only when `allowLifts` is true. Stairs walk like paths.
- `WalkRoute? Route(int fromNode, int toNode, bool allowLifts = false)`: Dijkstra; ties: fewer nodes, then the smaller
  node id sequence. `WalkRoute(ImmutableArray<int> Nodes, long LengthMm, long Ms)`. `long WalkMs(...)` returns -1 when
  unreachable. `long PlaceWalkMs(string fromPlace, string toPlace, bool allowLifts = false)`.

### Footfall (`Footfall`, static pure functions)
- `int Hourly(TownCatalog c, string placeId, int hour, int revivalPermille = 1000, int conditionsPermille = 1000, bool
  eventDay = false)`: `base = DayFootfall * curve[hour] / 1000 * FrontagePermille / 1000`; `boosted = base *
  revivalPermille / 1000 * conditionsPermille / 1000`; result `min(boosted, base * CeilingPermille / 1000)`. An
  `EventDaysOnly` place gives 0 unless `eventDay`. Hour 0..23 (clock hours; 066 maps real time to them).
- `int Daily(c, placeId)`: the sum over 24 hours at 1000 / 1000. `int LocationPermille(c, placeId)`: `Daily(place) *
  1000 / Daily(referencePlace)`; exactly 1000 for `s1`.

### Checks (`TownCheck.Run(TownCatalog) -> IReadOnlyList<TownProblem>`, `TownProblem(string Check, string Detail)`)
- C1 every `Path` and `Ramp` edge: `|dh| * 1000 / length < 50` (below 5 %, 051).
- C2 every `Stairs` edge has a route between its ends without stairs, lifts or funicular, at most 3x its length.
- C3 every node is reachable from the fountain node with `Path` and `Ramp` edges only.
- C4 every place in a core district is within 90 s of the fountain node (051).
- C5 the play area's bounding box is 640 x 480 m (+-5 m); the bounding box of the core places (every non-Address
  place in a core district; Main Street's east part has none) is at most 235 x 165 m (051: about 230 x 160).
- C6 the walk from `wreck_bulkhead` to `water_gate_3` and from `tunnel_door` to `wildbelt_lookout` is at most 300 s.
- C7 the Dome ellipse contains the play area with at least 20 m to spare at every corner; the crown is above every node.
- C8 every district has an area, a safe spot and at least one node; every place's node is in its district.
- C9 within Pitch, Small and Restaurant (event-only places excluded): a higher `Daily` never has a lower `RentPermille`.
- C10 `LocationPermille(referencePlaceId) == 1000`; `c3` has the highest `Daily` of all pitches (051: the busiest corner).
- C11 `low_ring`'s footfall over 21-03 is higher than `main_street`'s; over 10-16 it is lower than every core
  district's (dim by day, busy at night, town-plan 5).
- C12 every curve has the shape named above.

### Report (`TownReport.Format(TownCatalog, IReadOnlyList<TownProblem>) -> string`)
Sections: districts (footfall per day, peak hour, neighbours), places (kind, district, `Daily`, `LocationPermille`,
rent, walk seconds from the fountain), the check lines `PASS C1 ...` / `FAIL C1 ...`, last line `RESULT: PASS (12/12)`
or `RESULT: FAIL (k/12)`. CLI: `bash scripts/town-report.sh [--out file]`; exit 0 pass, 2 a check fails, 1 bad args.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim/Runtime/Climb sim/Runtime/Cooking
      sim/Runtime/Core sim/Runtime/Economy sim/Runtime/Layout game` is empty.
- [ ] AC2: `grep -rnwE "DateTime|Stopwatch|TickCount|float|double" sim/Runtime/Town` and `grep -rn "System.Random"
      sim/Runtime/Town` find nothing; the same inputs give an identical report string.
- [ ] AC3: Catalog validation throws for each rule above (one literal each: duplicate id, bad id, curve of 23 values,
      curve sum 999, anchor outside its district, unknown node, asymmetric neighbour, edge to a missing node, a D27
      forbidden word in a name, a district without a safe spot); `CreateDefault()` does not throw.
- [ ] AC4: `TownCheck.Run(CreateDefault())` is empty; each check C1-C12 has a failing literal catalog.
- [ ] AC5: `TownPaths` on a 5-node fixture: straight route, route around a gap, a lift used only with `allowLifts`,
      unreachable gives -1, a tie resolves the same way every run, `ms == lengthMm * 2 / 5`.
- [ ] AC6: `Footfall` literals: hourly base; revival 1300 capped by a ceiling of 1200; conditions 700 lower it; an
      event-only place is 0 on a normal day; `LocationPermille("s1") == 1000`.
- [ ] AC7: Every channel id in `CreateDefault()` places exists in `ClimbCatalog.CreateDefault()`; the 12 district ids
      are exactly those of the table; `s1` is the start and reference place.
- [ ] AC8: `bash scripts/town-report.sh` prints every section and `RESULT: PASS (12/12)`, exit 0, under 10 s.
- [ ] AC9: Every public method in `sim/Runtime/Town` has a test; the Town tests add under 5 s to `dotnet test`.

## Test plan
- `sim/Sim.Tests~/Town/`: `TownCatalogTests` (AC3, AC7), `TownCheckTests` (AC4), `TownPathsTests` (AC5),
  `FootfallTests` (AC6), `TownReportTests` (AC8 format, RESULT line). A `TestTown.cs` fixture (2 districts, 5 nodes,
  3 places) keeps unit tests small.
- Command: `bash scripts/verify.sh`; fast loop `dotnet test sim/Sim.sln`; report `bash scripts/town-report.sh`.

## Human steps
None to build. Read the report's place table if you want to see the town in numbers.

## Open questions (recommended default in brackets)
1. The Low Ring as its own (12th) district. [yes: the town plan calls it an area, and revival must be able to bring it
   back last]
2. Where the shop is. [`s1` on Main Street, the 057 "same place"; the cart pitches are data until a cart spec]
3. The reference place. [`s1` for every stage; per-stage references once the crew can move (a stage 3 spec)]
4. Footfall units. [relative points per day; tuned with 065's report and later game measurements]
5. Walk targets. [core within 90 s (051); the longest border-to-border walk 300 s; the plan's "4 minutes edge to
   edge" is straight-line]
6. Stairs, lifts, funicular. [every node reachable by ramp; lifts and the funicular are shortcuts only]
7. The monorail. [stops as places, no edges; riding it is a later spec]
8. Dome size. [770 x 640 m, crown 250 m (town plan 3.2, newer than 051's 760 x 600)]
9. Where stage 3 channels run. [only Restaurant places list them; whether the crew moves to an R place or `s1` grows
   is the stage 3 spec's call]

## Implementation notes
- **Deviation, needs a human nod (Open question 8): Dome radii 465 x 385 m (930 x 770), not 385 x 320 (770 x 640).**
  An ellipse of 770 x 640 cannot hold the corners of a 640 x 480 rectangle (corner (320, 240): 0.69 + 0.56 = 1.25 > 1),
  so C7 and AC4 cannot both pass with the plan's number. "Tuning changes numbers only", so the radius changed; C5 pins
  the play area. A test (`C7TheTownPlanDomeIsTooSmallForTheCorners`) keeps the plan's size as a failing literal. Town
  plan 3.2 may want the new size (or a foot that is not an ellipse).
- `IReadOnlyList<T>` instead of `ImmutableArray<T>` (System.Collections.Immutable is not in /sim or Unity without a
  package); the catalog copies every list, including the ones inside records. `TownPoint` and `TownRect` are readonly
  structs with value equality, not record structs (the sim is C# 9 for Unity).
- Extra public helpers (all tested): `TownPoint.Metres`, `TownRect.Metres/Contains/Overlaps/Touches/Corners`,
  `TownCatalog.District/Curve/Place/Node/HasPlace/InPlayArea/OnPlayAreaEdge`, `TownPaths.Distance`,
  `Footfall.DistrictHourly/DistrictSum` (inclusive, wraps midnight), `TownCheck.CheckIds/Describe`.
- Readings: C7 "20 m to spare" = each play-area vertex pushed 20 m outward on both axes is inside the ellipse (decimal
  maths, no float). C9 compares within each kind. C10: c3 strictly busier than every other pitch. C11 uses district
  footfall (no frontage), 21-03 and 10-16 inclusive. C12: commute = the max of 07-09 and of 17-19 each above every other
  hour; shops / market / evening > 500 in 10-19 / 06-13 / 16-23; night >= 500 in 20-03; other curve ids have no shape.
  C4 and C6 walk without lifts. `WalkRoute.LengthMm` includes ride edges; `Ms` = walked length x 2 / 5 + ride seconds
  (exact: costs are kept in fifths of a ms). `Daily` of the event-only c8 is 0, so its location is 0.
- Extra validation: walked edges need a length; FixedSeconds only (and > 0) on Lift/Funicular; nodes inside the play
  area; Border anchors on its edge; the play area counter-clockwise; the fall height below every node.
- Data: `DistrictDef.HeightMm` is the table's first value; nodes carry the terrain. Main Street: 10 m at x -150, the
  shop's flat pad 8.4 m at x -115..-45, 6.4 m at the fountain, then down to 2.06 m at x 148. Greenhouse Hill: a gentle
  east path to the tunnel door (18.5 m) plus switchbacks to the lab (45 m); the funicular is a shortcut. Steepest
  Path/Ramp 46 permille. Neighbours = areas touching (corners count) or one edge; a test derives and compares them.
  Report: c3 daily 2063 (location 1723), s1 1197 (1000); bulkhead to water gate 266 s; tunnel to Wildbelt 208 s.
- Place walks are node to node (the anchor's last <= 30 m is not counted). Follow-up for a delivery spec if it matters.
- Climb report: identical before and after (521 lines; only the "done in N ms" line differs). It was already
  `RESULT: FAIL (135/143)`, exit 2, on main; not from this spec.
- Tests: sim 592 -> 678 (+86 Town tests, about 0.1 s). `bash scripts/town-report.sh`: `RESULT: PASS (12/12)`, exit 0,
  about 3 s.

## Blockers
(Builder fills this in after 3 failed attempts.)
