---
id: 069
title: Day, night and Dome light in /game (light moods, the dusk wave, the Dome's lit ribs, Rain Hour and storm visuals, comfort settings)
status: approved
owner: architect
builder: sonnet       # the one synced weather field is reviewed by the netcode owner (opus)
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W7 of docs/town-plan.md section 10.

# 069: Day, night and Dome light

## Goal
The town under the Dome looks like the hour and the planned weather: morning, noon, sunset and night moods; at dusk
the lamps, then signs and windows, then the Dome's ribs come on in a wave; the ribs light only over districts at revival
level 2 or higher; the daily Rain Hour and storm days show rain under the glass and lightning on it. Night is never
pitch dark. Older eyes and photosensitive players get comfort settings (048): lightning flash strength, rain loudness,
screen rain. All of it is client-side and computed from the same inputs on every client.

## Depends on
**Specs 066, 067 and 068 built**; spec 048 (`GameSettings`, the Comfort page); D32, D46; docs/town-plan.md section 6;
docs/art-direction-v2.md sections 5.2 and 6 (light moods); spec 051 section 6 (light technology).
**Build after question 29** (art direction v2) is answered: this spec adds v2's light colours to `Palette.cs`; the
values are a reference that the art pass tunes (the plan: W7 "follows the art pass").
**The clock in the game** is 066's game-side follow-up. Until that lands, this spec runs on an interim time source
(below), so it can be built and tested first.

## Scope
Only `/game` (plus the tour hook in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.
- `Scripts/Light/` (new, namespace `Worktown.Game.Light`), pure: `ITownTimeSource.cs`, `LightMood.cs`,
  `LightMoodTable.cs`, `LightMoodBlend.cs`, `NightFloor.cs`, `DuskWave.cs`, `RibRules.cs`, `LightningFlash.cs`,
  `LightLod.cs`, `WeatherPacking.cs`. Components: `ShiftTimeSource.cs` (interim), `DevTimeSource.cs`,
  `TownWeatherSync.cs` (server NetworkBehaviour), `TownLightDirector.cs`, `TownLight.cs`, `RainFx.cs`, `LightningFx.cs`.
- `Scripts/Art/Palette.cs` and `UI/Palette.uss` (change: art direction v2 sections 5.2 and 6 light colours, one block).
- `Scripts/Settings/GameSettings.cs`, `SettingsRules.cs`, `SettingsPage.cs`, `Scripts/UI/SettingsView.cs` (Comfort
  page), `Scripts/Audio/AudioMix.cs` (rain volume) (change).
- `Scripts/Net/ProtocolSurface.cs` (`Revision` + 1), `Scripts/Net/Rules/LaunchArgs.cs` (dev flags).
- `Editor/TownGrayboxBuilder.cs` (change: `TownLight` on lamps, signs, windows and every `RibSegment`; the director),
  the regenerated scene.
- `Scripts/Dev/LightTourRunner.cs` (new), `scripts/visual-check.sh` (change: `light` tour).
- `Tests/EditMode/`: one test file per pure type, `BuiltLightTests`, settings tests, surface tests;
  `Resources/WhatsNew.json`; this spec.
- No change in `/sim`, the economy, the shift or the save.

## Out of scope
Running the shift and economy on the town clock (066 follow-up); weather changing demand; Adaptive Probe Volumes and
lighting scenarios, searchlights and light trails (the art pass); seasons and snow (D46: after v1); revival sets (068);
final rain and lightning art; the Low Ring's sodium-lit look beyond the shared rules.

## Design

### Time and weather on the client
- `ITownTimeSource.Now -> Worktown.Sim.Clock.TownTime`. **Interim** `ShiftTimeSource`: maps the economy's
  `GameMinute` to a `TownTime` (today the shift runs 08:00-14:00, so the game shows morning to noon only). The follow-up
  replaces it with the synced town clock; nothing else in this spec changes then.
- `DevTimeSource` (development builds and the Editor): `-town-time HH:MM` holds the clock there, `-town-time-run`
  lets it run from there at 066's rates, `-town-weather normal|panels|rain|storm` forces the day kind (rain = inside
  the Rain Hour).
- `TownWeatherSync` (server): computes 066's `DayWeather` for the current day from the save's `worldSeed` and syncs
  `SyncVar<long> WeatherPacked` (`WeatherPacking`: day 24 bits, kind 2, Rain Hour start hour 5, lightning seed 16;
  written once per day). Clients rebuild `DayWeather` with 066's `WeatherCatalog` (the fixed panel and storm windows)
  and run its `LightningSchedule` locally, so every client flashes at the same moment. `ProtocolSurface.Revision` + 1; surface tests change only by this line.

### Light moods (`LightMoodTable`, `LightMoodBlend`, pure)
- `LightMood(sunColour, sunElevationDeg, sunIntensity, ambientTop, ambientBottom, fogDensity, moon)`. Six moods from
  art direction v2 section 6: Morning (warm, 16 degrees), Noon (34-48 degrees), Sunset (9 degrees, the lights start),
  Night (cold, weak; LampWarm carries the street), Storm and Rain Hour (dim, `#AEB8D0` light). The colours become
  `Palette` entries (`SunMorning`, `SunNoon`, `SunSunset`, `MoonNight`, `SunStorm`, `SkyNightTop` ...); intensities are
  first guesses for the art pass.
- `Blend(TownTime t, DayWeather w)`: dawn blends Night to Morning over the 180 s dawn; the day phase Morning to Noon
  (07:00-11:00), Noon (11:00-16:00), Noon to Sunset (16:00-19:00); dusk Sunset to Night; Rain Hour and storm blend in
  and out over 10 s on top. Pure: the same inputs give the same mood on every client.
- **Night floor** (`NightFloor`): ambient intensity never below `MinAmbient` (default 0.35 of Noon's), lamps on paths
  and counters never off at night; checked in the tour by luminance (AC9).
- `TownLightDirector` applies the mood to one realtime directional light (sun by day, moon by night, shadows on), the
  URP ambient gradient, fog and a global volume weight for storm grading.

### The dusk wave (`DuskWave`, `RibRules`, pure)
- Each `TownLight` has a kind (`Lamp`, `Sign`, `Window`, `Rib`), a stable id (hash of its scene path) and a district.
- On-time after dusk start (real seconds): lamps 0-30, signs and windows 30-120, ribs 120-150; the offset inside each
  band comes from the id (`id % band`), so the town lights up in a wave and every client agrees. At dawn they go off
  in reverse order. A light's state is a pure function of `(TownTime, id, kind)`.
- 068's sets still decide whether a lamp exists or is dead at the district's level; the wave only times the ones that
  are on.
- `RibRules.IsLit(segment, time, levelOfDistrict)`: after its wave time and the district under it at level 2 or
  higher (art direction v2 section 6); segments over "none" stay dark. Ribs are `Rib` colour by day, `LampWarm` lit.
- `LightLod`: only the 16 realtime point lights nearest the camera are enabled; the rest show their emissive only.

### Rain Hour and storm (`RainFx`, `LightningFx`)
- **Rain Hour:** rain particles in a 40 m box around the camera under the glass; path and street materials get a wet
  smoothness boost that fades over 60 s after the rain; outdoor tables are not touched (demand is the follow-up's).
- **Open panels:** 12 panel quads near the crown (added by this spec's builder change, in the shell's glass) slide
  open over 20 s while `PanelsOpen`; a little more sun (the Noon mood's intensity + 10 %); no other change.
- **Storm days:** the storm mood, rain on the glass (a scrolling layer on the shell), the Storm Ring stand-in darker,
  neon flicker on lit signs, and lightning at 066's strike times: a branching flash on the glass at a seeded point,
  a short directional pulse, and thunder 1-3 s later on the ambience bus.
- **Photosensitivity** (`LightningFlash`): a strike is at most 2 flashes; never more than 3 flashes in any 1 s window;
  peak brightness change scales with `LightningFlashPercent`; at 0 % the strike shows only as a soft dim glow on the
  glass, no change to the scene's light.

### Comfort settings (spec 048, the Comfort page)
`GameSettings` gains `lightningFlashPercent` (0-100, default 100), `rainVolumePercent` (0-100, default 100; scales rain
and thunder) and `screenRain` (default on; off hides the rain particles within 3 m of the camera). `Sanitized` clamps
them; scripted runs use the defaults (048's rule); the page shows them with live preview.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `LightMoodBlend` literals: 08:00 Normal between Morning and Noon; 12:00 Noon; mid-dusk half Sunset, half
      Night; 23:00 Night; inside the Rain Hour the Rain mood after 10 s; inside the storm window the Storm mood.
- [ ] AC3: Night floor: over every 10 s of a day for each `DayKind`, the ambient intensity is at least `MinAmbient`.
- [ ] AC4: `DuskWave`: every lamp comes on before every sign and window, those before every rib; offsets are the same
      for the same id; dawn reverses the order; the state is the same for the same `(time, id, kind)`.
- [ ] AC5: `RibRules`: a segment is lit only at night after its wave time and only over a district at level 2 or 3
      (068 levels); level 0 or 1 or "none" stays dark.
- [ ] AC6: `LightningFlash`: at 0 % no scene light change; at 100 % the table's peak; over 1,000 seeded storm days never
      more than 3 flashes in any 1 s window.
- [ ] AC7: Settings: the three fields clamp, round-trip in `SettingsStore`, stay at defaults in scripted runs, and show
      on the Comfort page (048's settings tour frame).
- [ ] AC8: `WeatherPacking` round-trips every field at its maximum; the server's value equals 066's plan for the
      save's seed and day; surface tests change only by `WeatherPacked`; `Revision` + 1.
- [ ] AC9: `--tours light` (dev time and weather flags): Main Street at 08:00, 12:00, 19:45 (mid-wave), 23:00, in the
      Rain Hour, and in a storm with flashes at 100 % and 0 %; the Dome from Station Square at night with Main Street at
      level 2 (ribs lit over it only); the 23:00 frame's mean luminance over the path area is at least 0.10; 0
      Exception lines. The existing `look,walk,cook,menu,corner` tours pass.
- [ ] AC10: `BuiltLightTests`: every lamp, sign, window and rib segment has a `TownLight`; from 10 fixed camera points
      no more than 16 realtime point lights are enabled; every light colour comes from `Palette`.
- [ ] AC11: Budget: at 23:00 with every light on, the host frame time p95 is within 10 % of 12:00 (tour metric);
      `WhatsNew.json` entry.

## Test plan
- EditMode: `LightMoodBlendTests` (AC2, AC3), `DuskWaveTests` (AC4), `RibRulesTests` (AC5), `LightningFlashTests`
  (AC6), `LightLodTests`, `WeatherPackingTests` (AC8), settings tests (AC7), `BuiltLightTests` (AC10), surface tests.
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours light --out .verify/visual069`, then
  `--tours look,walk,cook,menu,corner`.

## Human steps
Look at the light tour's frames; check the lightning at 0 % and 100 % on your own screen. The art pass tunes the values.

## Open questions (recommended default in brackets)
1. Time before the clock follow-up. [the shift's clock (morning to noon) plus dev flags; the follow-up swaps the source]
2. Weather on clients. [one synced packed field per day; lightning and moods computed locally]
3. Wave timing. [lamps 0-30 s, signs and windows 30-120 s, ribs 120-150 s of the 180 s dusk]
4. Night floor. [ambient at least 0.35 of noon's; path luminance at least 0.10 in the tour]
5. Lightning default and limits. [100 %; at most 3 flashes a second at every setting]
6. Probe volumes and lighting scenarios. [the art pass, not here]
7. Light colours. [v2's light and mood colours join `Palette.cs` after question 29]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
