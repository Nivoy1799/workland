---
id: 016
title: Cooking takes time; pan heat and fry/saute mode
status: draft
owner: architect
builder: opus
---

# 016: Cooking takes time; pan heat and fry/saute mode

## Goal
Today the pan and pot cook instantly: hold food, press 1 to 3, done. This spec makes cooking feel
like cooking: you put food in the pan or pot, it cooks while it sits there, and you take it off
when you think it is right. The pan gets a heat setting and a fry/saute mode. All of it runs on
the server through the existing spec 005 `ItemFactory` (time and temperature are already op
parameters), clients only send intents.

The ideas come from the Mac draft of spec 007 (`specs/007-cooking-station.md`, superseded). They
are rebuilt here on top of the spec 007 we kept, with keys that fit specs 010 to 012.

## Depends on
- Specs 007, 009, 013, 014 (branch `spec/014-npc-customers`). Built on a branch stacked on it.
- Spec 005 `OperationCatalog`: fry and saute take `temp` 120..220 C and `time` 10..600 s,
  boil takes `time` 30..1800 s at a fixed 100 C. No /sim change.

## Scope
- `game/Assets/_Project/Scripts/Cooking/`:
  - `CookingTime.cs` (new, pure): time scale, tick to sim seconds, clamping, the "too short" check,
    doneness bucket labels.
  - `HeatSession.cs` (new, pure): what is cooking where (food id, op id, start tick, temperature).
  - `PanSettings.cs` (new, pure): temperature in 10 C steps and the fry/saute mode, per pan.
  - `StationRules.cs` (change: pan and pot options become heat controls, prompts updated).
  - `KitchenState.cs` (change: start and finish a heat session).
  - `KitchenService.cs` (change: E on pan/pot puts food in or takes it out; options 1 to 3 on the pan
    change heat or mode).
  - `StationContents.cs` (change: label shows heat, mode and the doneness bucket while cooking).
  - `CookOutcome.cs` (change: `TooShort`, `PanBusy`).
- `game/Assets/_Project/Scripts/Net/PickupInteractor.cs` (change: E on pan/pot goes to the station
  like on the plating table).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: toasts for the new outcomes).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: pan and pot get `StationContents`
  and a NetworkObject like the plating table).
- `game/Assets/_Project/Resources/WhatsNew.json` (new entry spec-016).
- Tests in `game/Assets/_Project/Tests/EditMode/`: `CookingTimeTests.cs`, `HeatSessionTests.cs`
  (new), `CookingRules`/`KitchenState` tests (change).
- This spec file (implementation notes).

## Out of scope
- Burning, smoke, fire, sound, particles. Overcooking is only what the sim's doneness already does.
- Changing the heat while food is in the pan (Open question 3).
- New operations, /sim changes, balancing (raw-item prices stay a separate topic).
- Showing exact temperature or seconds to clients (D21): only bucket labels.
- Client-side prediction of cooking.

## Design

### Flow
- **Put in:** player holds food, presses E at the pan or pot. The food goes into the station
  (despawned from the hand, kept in `KitchenState` like on the plating table). A `HeatSession`
  starts at the current FishNet tick with the pan's current temperature and mode. One item per
  pan or pot; a second one gets `StationFull`.
- **Cooking:** the server updates the station label when the doneness bucket changes (not every
  tick): `Pan (hot, fry): potato sticks, medium`.
- **Take out:** empty hands, E. The session ends: `simSeconds = elapsedTicks / tickRate * TimeScale`,
  clamped to the op's `time` max. Below the op's `time` min the food comes back unchanged and the
  outcome is `TooShort`. Otherwise `ItemFactory.Apply(op, [food], { temp, time })` (boil: time only)
  and the result goes into the player's hands. Anyone can take food out, not only who put it in.
- **Pan controls** (only while the pan is empty, otherwise `PanBusy`): `1` heat down, `2` heat up
  (10 C steps, 120 to 220, new pans start at 170), `3` switch fry/saute. The pot has no options.
- Disconnect: food in a pan or pot stays and keeps cooking. Server stop clears everything
  (existing `KitchenState.Clear`).

### Time
- `CookingTime.TimeScale = 6` sim seconds per real second (Open question 2): fries at
  150 sim s take about 25 real seconds, a long boil (900 sim s) 2.5 minutes, the pan max
  (600 sim s) 100 seconds.
- Elapsed time comes from FishNet's tick counter, not `Time.time`, so host and dedicated server
  agree and frame rate does not matter.
- Doneness buckets for the label are the sim op's `time` param bucket ids (`short`, `medium`,
  `long`; boil at 30, 300, 900 sim s, pan at 10, 60, 180). No new numbers.

### Network surface
- No new RPC. The existing `RequestStation(index, option)` carries pan options 1 to 3 as bytes,
  E reuses the plating table path (`option 0`). Nothing from the client is a temperature, a time
  or an item property.

### Keys (fit specs 010 to 012)
E put in / take out; 1/2 heat; 3 fry/saute. G (emote wheel) and T (quick chat) stay untouched;
the existing guards (UI open, T or G held) apply.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes with `UNITY_PATH` set (Unity EditMode tests run).
- [ ] AC2: `CookingTimeTests`: at tick rate 30 and scale 6, 750 ticks give exactly 150 sim s;
      9000 ticks on fry clamp to 600 s; fry taken out below 10 sim s is `TooShort`.
- [ ] AC3: `HeatSessionTests`/`KitchenState` tests: food put in a pan at 190 C fry and taken out
      after 750 ticks has the same `Hash` as `ItemFactory.Apply("fry", [food], {temp 190, time 150})`
      built directly; `TooShort` returns the unchanged food with its original hash; a second item
      in a busy pan is `StationFull`; heat changes with food in the pan are `PanBusy`; heat stops
      at 120 and 220; saute mode produces a saute result.
- [ ] AC4: Pot: boil after 300 sim s matches `Apply("boil", [food], {time 300})`.
- [ ] AC5: A reflection test over `Worktown.Game.Cooking` still finds no `float`, `double`,
      `string`, `long` or `Item` ServerRpc parameter.
- [ ] AC6 (manual, human): two clients on the dedicated server: A puts potato sticks in the pan,
      B takes them out after about 25 s and sees "crispy fried potato sticks"; the label changed while cooking.
- [ ] AC7: `WhatsNew.json` has a spec-016 entry with these test steps.

## Test plan
- Pure rules (`CookingTime`, `HeatSession`, `PanSettings`) and `KitchenState` carry the logic and
  are covered by EditMode tests with a fake tick source. FishNet glue stays thin; manual AC6 plus a
  two-client screenshot run like specs 012 to 014.
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions
1. **Keep the instant 1 to 3 presets as well?** Default: no, the pan and pot are timed only.
   Alternative: keep presets as a "quick cook" for the demo.
2. **Time scale.** Default: 6 (fries about 25 s). Alternative: 1:1 (fries 2.5 min) or another factor.
3. **Changing the heat while cooking.** Default: not allowed. Alternative: allowed, using the
   time-weighted mean temperature.
4. **How many pans and pots.** Default: keep one each. Alternative: two pans (the Mac draft had two),
   which makes co-op cooking easier.

## Human steps
- Approve the open questions (or change them).
- AC6 with a second window or person.

## Implementation notes
(Builder fills this in.)

## Blockers
