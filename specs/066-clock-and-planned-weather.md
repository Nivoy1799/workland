---
id: 066
title: Town clock and planned weather in /sim (the 40-minute day, night demand, Rain Hour, open-panel and storm days, season calendar as data)
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W4 of docs/town-plan.md section 10.

# 066: Town clock and planned weather

## Goal
One pure, seeded model of time under the Dome: the 40-minute game day (D46) with dawn, day, dusk and a played night,
the clock hours each real second maps to, the planned weather of each day (the daily Rain Hour, open-panel days, storm
days with lightning on the glass), the demand conditions they cause, and the season calendar as data only. Spec 069
draws it, a game-side follow-up (below) runs the game on it, and 064/065 take its conditions as plain permille.

## Depends on
D33, D37 (time runs only while the game runs), D46, D41; docs/town-plan.md section 6; spec 051 sections 6-7b; spec 052
(service length, open question 2), 052b (game numbers at `BetweenSeconds 15`), 053 (`ClockPart.playSeconds` in the
save, the 15 s summary); spec 008 (`IRandom`, `SeededRandom`, `EconomyRng.ForTick`). Runs in parallel with 064.

## Scope
- `sim/Runtime/Clock/` (new, namespace `Worktown.Sim.Clock`, one public type per file): `DayPhase.cs`, `PhaseDef.cs`,
  `ClockCatalog.cs`, `TownTime.cs`, `HourSpan.cs`, `TownClock.cs`, `DayKind.cs`, `DayWeather.cs`, `WeatherCatalog.cs`,
  `WeatherPlan.cs`, `LightningSchedule.cs`, `Conditions.cs`, `ConditionsTable.cs`, `Season.cs`, `FestivalDef.cs`,
  `SeasonCalendar.cs`, `ServiceFit.cs`.
- `sim/Tools~/ClockReport/ClockReport.csproj`, `Program.cs` (new console app, net10.0); `sim/Sim.sln` (change: add it);
  `scripts/clock-report.sh` (new).
- `sim/Sim.Tests~/Clock/` (new tests). This spec (Implementation notes).
- No change to other `sim/Runtime/` folders, `/game` or other specs.

## Out of scope
Running the game on this clock (the follow-up below); visuals (069); the bed, the "rested" bonus and the Crew House;
applying conditions to demand; seasons changing anything (D46: after v1); snow; the compost loop (parked); a forecast UI.

## Design

### Units and determinism
Integers only. Real time is `long playSeconds`: the seconds the host's game has run, saved in 053's
`ClockPart.playSeconds` and advanced only while the server runs. Every function here is pure in `(playSeconds,
worldSeed, catalogs)`; no `DateTime`, `Stopwatch`, `float` or `System.Random`. That is how D37 holds by construction:
nothing moves while nobody plays, and a save and reload gives the same time and weather.

### The day (`ClockCatalog.CreateDefault()`, validated: phases contiguous, seconds sum to `DayLengthSeconds`, minutes
sum to 1440)
| Phase | Real seconds | Clock | Real seconds per clock hour |
|---|---|---|---|
| Dawn | 180 | 05:00-07:00 | 90 |
| Day | 1440 | 07:00-19:00 | 120 |
| Dusk | 180 | 19:00-21:00 | 90 |
| Night | 600 | 21:00-05:00 | 75 |
`DayLengthSeconds = 2400` (D46: dawn 3, day 24, dusk 3, night 10 minutes). A game day runs from dawn to the end of the
night. `PhaseDef(DayPhase Phase, int Seconds, int StartMinute, int Minutes)`; `enum DayPhase { Dawn, Day, Dusk, Night }`.
A new game starts at day 1, 08:00 (`NewGameStartSecond = 300`, today's shift opening).

### Time (`TownClock`, static)
- `TownTime At(ClockCatalog c, long playSeconds)`: `total = playSeconds + NewGameStartSecond`; `Day = total / 2400 +
  1`; `DaySecond = total % 2400`; the phase containing it; `MinuteOfDay = (StartMinute + (DaySecond - phaseStart) *
  Minutes / Seconds) % 1440` (floor); `Hour = MinuteOfDay / 60`; `PhasePermille` (progress through the phase).
  `TownTime(long Day, int DaySecond, DayPhase Phase, int PhasePermille, int MinuteOfDay, int Hour)`.
- `int SecondsOfHour(c, int hour)`: 90, 120 or 75 by the table.
- `IReadOnlyList<HourSpan> Spans(c, long fromPlay, long toPlay)`: the clock hours an interval covers with the real
  seconds of each (`HourSpan(long Day, int Hour, int Seconds)`); the seconds sum to `toPlay - fromPlay`. This is how a
  later spec integrates 064's hourly footfall over a 360 s service.
- `long PlaySecondsAt(c, long day, int minuteOfDay)`: the inverse, for tests and tours.

### Planned weather (`WeatherPlan.For(WeatherCatalog w, ulong worldSeed, long day) -> DayWeather`)
`enum DayKind { Normal, OpenPanel, Storm }`. `DayWeather(long Day, DayKind Kind, int RainStartMinute, int
RainMinutes, int PanelsFromMinute, int PanelsToMinute, int StormFromMinute, int StormToMinute, int LightningSeed)`.
- RNG: `EconomyRng.ForTick(worldSeed ^ WeatherSalt, day)` with `WeatherSalt = 0x066`. **Exactly three draws per day**,
  in this order: (1) `roll = NextInt(0, 1000)`, (2) `rainHour = NextInt(RainFromHour, RainToHour + 1)`, (3)
  `LightningSeed = NextInt(0, 65536)`. No state carries between days.
- Kind: `roll < StormPermille` gives Storm, except on day 1 and when day - 1's own roll was also a storm roll (then
  Normal); else `roll < StormPermille + OpenPanelPermille` gives OpenPanel; else Normal. Day - 1's roll is recomputed
  from its own RNG (one step back, no recursion), so any day is computable alone.
- **Rain Hour, every day** (the Keepers water the town): starts at `rainHour:00`, lasts `RainMinutes = 60` clock
  minutes (120 real seconds in the day phase). Default hours 10..16.
- **Open panels** (OpenPanel days only): 09:00-18:00, closed during the Rain Hour. Otherwise `PanelsFrom = PanelsTo = -1`.
- **Storm** (Storm days only): 07:00-21:00 outside the glass; panels stay closed; the Rain Hour still runs inside.
- Defaults (`WeatherCatalog.CreateDefault()`, provisional): `StormPermille 100`, `OpenPanelPermille 300`, rain hours
  10..16, panels 09:00-18:00, storm 07:00-21:00. Validation: permille sum <= 1000, windows inside the day.
- `LightningSchedule.Strikes(DayWeather w, ClockCatalog c) -> IReadOnlyList<int>` (day seconds): only inside the storm
  window; gaps drawn from `new SeededRandom((ulong)w.LightningSeed)` in 20..90 real seconds; never two strikes closer
  than 4 s (069 adds its own flash rule for photosensitivity). Empty on non-storm days.
- Helpers: `bool InRainHour(w, TownTime t)`, `bool PanelsOpen(w, t)`, `bool InStorm(w, t)`.

### Conditions (`ConditionsTable.At(DayWeather w, TownTime t) -> Conditions`)
`Conditions(int StreetPermille, int OutdoorSeatsPermille, int IndoorPermille, int DeliveryPermille, int
DeliveryTimePermille)`. The four demand fields match 064's inputs by name (street walk-ups, outdoor seats, indoor
seats, delivery orders); `DeliveryTimePermille` scales delivery times. Result = weather row x phase row / 1000 per field
(floor). Rows (provisional, `ConditionsTable.CreateDefault()`):

| Weather row (first match) | Street | Outdoor seats | Indoor | Delivery | Delivery time |
|---|---|---|---|---|---|
| Rain Hour on a storm day | 500 | 0 | 1200 | 1300 | 1300 |
| Rain Hour | 700 | 0 | 1150 | 1300 | 1000 |
| Storm window | 600 | 300 | 1200 | 1200 | 1300 |
| Panels open | 1100 | 1400 | 950 | 900 | 1000 |
| Normal | 1000 | 1000 | 1000 | 1000 | 1000 |

| Phase row | Street | Outdoor seats | Indoor | Delivery | Delivery time |
|---|---|---|---|---|---|
| Dawn, Day | 1000 | 1000 | 1000 | 1000 | 1000 |
| Dusk | 1000 | 1100 | 1000 | 1000 | 1000 |
| Night | 1000 | 800 | 1000 | 1250 | 1000 |

**Night demand** is two parts: 064's footfall curves carry who is out at each clock hour (the Wreck market and the Low
Ring at night, Main Street quiet), and the night row above shifts the channel mix (late deliveries up, terraces down).
The night is never skipped (D46), so its hours exist in play.

### Seasons as data (`SeasonCalendar`, no consumer in v1)
`enum Season { Spring, Summer, Autumn, Winter }`; `SeasonCalendar(int DaysPerSeason = 7, Season First = Spring,
ImmutableArray<FestivalDef> Festivals)`; `SeasonOf(day)`, `DayInSeason(day)` (1..7), `FestivalOn(day)` (or null).
`FestivalDef(string Id, string Name, Season Season, int Day, string PlaceHint)`: `blossom_fair` (Spring 5,
`garden_promenade`), `lake_nights` (Summer 5, `lakefront`), `landing_day` (Autumn 4, `station_square`), `winter_lights`
(Winter 5, `wreck_market`). Names pass the D27 check. `WeatherPlan` and `ConditionsTable` never read the calendar.

### The service model fits (`ServiceFit.For(ClockCatalog c, int serviceSeconds, int betweenSeconds)`)
Returns `(int ServicesPerDay, int LeftoverSeconds, int ServicesPerPlayHourTimes100)`. Services run **back to back on
the town clock**, never aligned to the day and never skipping the night, so the climb's pace per play hour (052, 052b)
does not depend on the day length: 360 + 60 s gives 5 services a day (300 s left) and 857 per 100 play hours; the game's
360 + 15 s gives 6 (150 s left) and 960. A service may start in the day and end at night; its demand follows the hours
it covers (`Spans`).

### Game-side follow-up (a later /game spec, named here, not built; netcode owner)
1. The host's server owns `playSeconds` (already saved) and syncs a clock anchor; clients compute `TownTime` locally.
2. The economy ticks per **clock hour** of `TownClock` (90/120/75 s) instead of `EconomyRules.SecondsPerHour = 60`.
   Today a 360 s shift covers 6 clock hours; on the town clock it covers about 3 day hours, so the hour weights or
   `UnitsPerNpcPerDayCenti` must be rescaled to keep **guests per real second** equal, or 052b's stage 1 calibration
   (82 s per fries guest) breaks. The rescale is that spec's first test.
3. `Shift` opens services back to back on the town clock and drops the 14:00-to-08:00 skip (`HoursToNextOpen`); the HUD
   shows clock time; tours skip play seconds, not hours.
4. The server syncs the day's `DayWeather` (packed, a few bytes) for 069; `TickInput.DemandPermille` takes the
   conditions per market; the bed (D46) ends a player's shift when homes exist.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim/Runtime/Climb sim/Runtime/Cooking
      sim/Runtime/Core sim/Runtime/Economy sim/Runtime/Layout sim/Runtime/Town game` is empty.
- [ ] AC2: `grep -rnwE "DateTime|Stopwatch|TickCount|float|double" sim/Runtime/Clock` and `grep -rn "System.Random"
      sim/Runtime/Clock` find nothing.
- [ ] AC3: `TownClock.At` literals: play 0 is day 1 08:00 Day; play 420 is 11:30; play 1320 is 19:00 Dusk; play 1500
      is 21:00 Night; play 2100 is day 2 05:00 Dawn; `PlaySecondsAt` inverts each.
- [ ] AC4: `Spans`: a 360 s service from 08:00 gives 3 hours of 120 s; one from 18:30 gives hours 18-22 with 60, 90, 90, 75
      and 45 s; the seconds always sum to the interval (property over 1,000 seeded intervals).
- [ ] AC5: Weather: the same seed and day give an equal `DayWeather`; a counting fake `IRandom` sees exactly 3 draws
      per day; day 1 is never a storm; never two storm days in a row; over days 1..10,000 of seed 1 the storm share is
      70-100 permille and the open-panel share 280-320; every Rain Hour starts 10:00-16:00.
- [ ] AC6: Panels close during the Rain Hour on open-panel days; storm days have no open panels; each conditions row
      and phase row has a literal test, including a Rain Hour at dusk on a storm day.
- [ ] AC7: Lightning: no strike outside the storm window, gaps >= 4 s, the same `DayWeather` gives the same list, none
      on Normal and OpenPanel days.
- [ ] AC8: Calendar: day 1 is Spring 1, day 8 Summer 1, day 29 Spring 1 again; each festival on its day; `WeatherPlan`
      gives identical results with two different calendars.
- [ ] AC9: `ServiceFit` literals for 360/60 and 360/15; services per play hour are equal for day lengths 1200, 2400
      and 3600 (property).
- [ ] AC10: Save and reload (property): recomputing from a stored `playSeconds` and seed equals the live values; no
      public method takes a wall-clock type.
- [ ] AC11: `bash scripts/clock-report.sh --seed 1 --days 28` prints one line per day (kind, Rain Hour, panels,
      storm, strike count, season, festival) and the `ServiceFit` lines, exit 0, under 5 s.
- [ ] AC12: Every public method in `sim/Runtime/Clock` has a test; the Clock tests add under 5 s.

## Test plan
- `sim/Sim.Tests~/Clock/`: `TownClockTests` (AC3, AC4, AC10), `WeatherPlanTests` (AC5, AC6), `LightningScheduleTests`
  (AC7), `ConditionsTableTests` (AC6), `SeasonCalendarTests` (AC8), `ServiceFitTests` (AC9), `ClockCatalogTests`
  (validation throws: gap between phases, seconds not 2400, permille over 1000), `ClockReportTests` (AC11 format).
- Command: `bash scripts/verify.sh`; fast loop `dotnet test sim/Sim.sln`; report `bash scripts/clock-report.sh`.

## Human steps
None to build. The clock report shows a month of weather for one seed.

## Open questions (recommended default in brackets)
1. Clock hours per phase. [dawn 05-07, day 07-19, dusk 19-21, night 21-05; 120 s per day hour]
2. Day boundary. [at dawn, 05:00: the night belongs to the day before it]
3. New game start. [day 1, 08:00, like today's shift]
4. Weather mix. [storm 10 %, open panels 30 %, normal 60 %; never two storms in a row; day 1 normal]
5. Rain Hour. [every day, one clock hour, start 10:00-16:00 seeded; open panels close for it]
6. Storm window. [07:00-21:00; nights calm]
7. Conditions numbers. [the tables above, provisional; tuned once the game measures them]
8. Services and the day. [back to back, never skipping the night; 052's pace per play hour stays]
9. Seasons. [data only in v1 (D46): spring first, 7 days each]
10. Forecast. [today and tomorrow are computable now; boards and the town paper show them in a later spec]

## Implementation notes
- Tests project references the ClockReport tool (sim/Sim.Tests~/Sim.Tests.csproj, one line) so ClockReportTests can call Program.Run.
- Records hold IReadOnlyList instead of ImmutableArray (netstandard2.1 has no System.Collections.Immutable); lists compare by reference.
- Added public TownClock.DaySecondOf and a second WeatherPlan.For(w, IRandom, day, previousRolledStorm) overload (the counting-RNG test uses it).
- Spans walks second by second (fine for service-length intervals).
- Conditions: a storm-day night after 21:00 is plain night row; Rain Hour at dusk is tested with a hand-built DayWeather.

## Blockers
(Builder fills this in after 3 failed attempts.)

**066 review (2026-11-11):** approved with nits. Fixed: the weather salt moved to the high bits (`0x066UL << 48`), so weather and climb draws are no longer correlated (tests still green, 637). Open nits: `Spans` walks second by second (step by hour, or document a max interval); dead `Math.Max` in LightningSchedule; catalog arrays exposed as IReadOnlyList can be cast back (wrap with Array.AsReadOnly); `TownClock.At` does not validate the catalog.
