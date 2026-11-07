---
id: 021
title: Movable cookware on normal worktops (board, pan, pot, plates, bowl as items; several ingredients in a pot)
status: approved
owner: architect
builder: opus
---

# 021: Movable cookware

## Goal
The kitchen stops being a row of special counters. The furniture is plain worktops and stoves; the
cutting board, the pan, the pot, the plates and the bowl are **things lying on top** that you can
pick up and carry, like the knife. You can put a potato on any free worktop spot, also on the stove
counter next to the pan. You put a pan on a burner, switch the burner on with its knob, put food
into the pan, and pour the result onto a plate. A pot takes **several ingredients**. With free hands,
clicking a pan or pot picks it up; holding food that fits, clicking it puts the food in (spec 020's
highlight shows which before you click).

Asked by the human on 2026-11-07 after playing spec 020: "the cutting board is just on a normal
board ... the cooking station is not the whole counter, only the layer on top ... so a potato could
be placed on it even if not in the pan", "pan still not pick-up possible", "all items should be
movable, pans and so on", "I want to put multiple ingredients into a pot", "when I have free hands I
pick up the pot".

The server stays the authority (rule 6). Clients send intents about objects ("use what I hold on
that pot", "pick up that pan", "place on spot N"); the server checks reach, hands, contents and
the sim, and decides. No client value decides money, quality, time or position.

## Depends on
- Specs 007, 009, 013, 016 (cooking ops, plating, bowl, timed heat with on/off), 017 (FoodLook,
  FoodVisual), 019 (board, knife, cut strokes), 020 (mouse interaction, `InteractionRules`,
  highlight, place spots, `RequestPlace`).

## Scope
- `game/Assets/_Project/Scripts/Cooking/`:
  - `Vessel.cs` (new, NetworkBehaviour): marks a pickup as cookware and names its kind
    (`VesselKind`: Board, Pan, Pot, Plate, Bowl); a `SyncList<FoodLook>` and `SyncList<string>` of its
    contents (replaces what `StationContents` did for these stations), a `SyncVar<byte>` for the pan
    mode (fry / sauté).
  - `VesselKind.cs` (new), `VesselRules.cs` (new, pure): capacity and what fits, see Design.
  - `KitchenState.cs` (change: board, heat and assembly sessions are keyed by the vessel's object id
    instead of a station index; the pot holds several ingredients; burners).
  - `KitchenService.cs` (change: use-on-object handling, pour, burners, respawn of lost cookware).
  - `StationKind.cs`, `StationRules.cs` (change: `Stove` (burners and knobs) and `PlateShelf`
    replace Board, Pan, Pot, PlatingTable and Bowl as stations; Crate and Counter stay).
  - `CookOutcome.cs` (change: new outcomes as needed, e.g. `VesselFull`, `NotOnBurner`).
  - `StationContents.cs` (change: only what stoves and the counter still need).
- `game/Assets/_Project/Scripts/Net/`:
  - `PickupInteractor.cs` (change: new `RequestUse(NetworkObject target, int action)` ServerRpc).
  - `PickupService.cs`, `Pickup.cs` (change: cookware is a normal pickup; burners are place spots).
  - `PlaceSurface.cs` (change: a surface can be a burner with one spot and a heat flag).
  - `Rules/InteractionRules.cs`, `Held.cs`, `AimTarget.cs`, `AimRules.cs` (change: rows below).
- `game/Assets/_Project/Scripts/Art/` (change: cookware wrappers reuse the spec 017 models; a plate
  shelf model or a simple stack of plate models).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: prompts and toasts).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: new kitchen layout, see Design).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry `spec-021`), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- Washing plates, breaking things, physics, throwing.
- New ingredients, new sim operations, balancing. (No /sim change: the pot uses existing `boil`
  and `mix`, see Design.)
- Third-person cooking (Open question 5).
- Hands and characters (spec 022).

## Design
### Kitchen layout (scene builder)
- The station row becomes: crates (unchanged), **worktops** with place spots, **two stoves** (each a
  worktop-height unit with 2 burners and a knob plus a fry/sauté button per burner on its front),
  the **counter** (sell, unchanged), a **plate shelf** (click with empty hands: take a clean plate).
- Lying on the worktops at start: the cutting board with the knife next to it, a pan and a pot (on
  burners, switched off), the bowl, and 2 plates. Every worktop and stove top outside the burners
  has place spots (spec 020 grid).
- Each cookware item has a home spot; a lost item (out of the world or missing for 5 s) respawns
  there, like the knife (spec 019). Plates come from the shelf (unlimited, at most 8 in the world).

### Cookware (`Vessel`) and what fits (`VesselRules`, pure)
| Kind | Holds | Fits | Comes out |
|---|---|---|---|
| Board | 1 food | unplated food (spec 019 rule) | take with empty hands (cut as in 019) |
| Pan | 1 food (Open question 2) | unplated food the sim can fry/sauté | pour onto a plate or bowl, or take with empty hands |
| Pot | up to 4 ingredients | unplated food the sim can boil | pour onto a plate or into a bowl |
| Plate | up to 4 parts (`MaxPlateInputs`) | unplated food | sell the plate at the counter |
| Bowl | up to 6 parts (`MaxMixInputs`) | unplated food | pour onto a plate, or take the mix with empty hands |

- **Cooking needs a burner:** a pan or pot cooks only while it stands on a burner spot whose knob is
  on (spec 016 timing: on-ticks only). Carried or on a worktop it does not cook. Picking it up from a
  burner stops its clock; putting it back continues it.
- **Several ingredients in a pot:** each ingredient remembers its own on-ticks since it went in. When
  the pot is poured, every ingredient is boiled for its own time (existing `boil`) and, if there are
  two or more, the results are mixed (existing `mix`, canonical order). One ingredient gives today's
  boiled item. Deterministic, no /sim change; tests compare hashes with direct sim calls.
- **Fry/sauté:** the mode belongs to the burner's button (as in 016/019) and applies to the pan on
  that burner; a pot ignores it.
- **Pour:** holding a pan, pot or bowl with contents and clicking a plate (or bowl, for pan/pot)
  moves the finished food onto it (the server applies the heat or mix ops at that moment, exactly
  like today's take). Pouring into a full plate is refused before anything changes.
- **Selling:** holding a plate with food, click the counter: sells the plated item (plate op on its
  parts, today's selling and stars), the plate goes away with the meal. Holding loose food at the
  counter still works as today (the counter plates it).

### Interaction rows (extends spec 020's `InteractionRules`, still pure, first match wins)
| Held | Target | Action | Prompt |
|---|---|---|---|
| anything | knob / button | `Press` | "Click · burner on/off", "Click · fry → sauté" |
| knife | board with food (lying on a surface) | `Cut` | "Drag down · cut   Right click · turn" |
| food that fits | board / pan / pot / plate / bowl with room | `PutInto` | "Click · put potato into the pot" |
| food that does not fit | cookware | `None` (grey) | "Pot is full" / "can't go in there" |
| pan / pot / bowl with food | plate / bowl with room | `Pour` | "Click · pour into the plate" |
| plate with food | counter | `Sell` | "Click · sell <meal>" |
| anything (incl. cookware) | free spot or free burner | `Place` | "Click · put pan on the burner" |
| nothing | board / pan / bowl with food | `Take` | "Click · take potato sticks" |
| nothing | pot with food | `PickUp` | "Click · pick up the pot" (a pot is poured, not emptied by hand) |
| nothing | any free item or empty cookware | `PickUp` | "Click · pick up the pan" |
| nothing | crate / plate shelf | `Buy` / `Take` | "Click · take potato ($60)" / "Click · take a plate" |

The rule "empty hands + pan with food" is `Take` (food first), "empty hands + empty pan" is
`PickUp`; Open question 3 asks whether a full pan should rather be picked up as a whole.

### Network
- One new ServerRpc: `RequestUse(NetworkObject target, int action)`, action 0 use (put into, pour,
  sell, take), 1 cut stroke, 2 turn. The server reads what the player holds and the target's state
  and decides. `RequestStation` stays for crates, counter, shelf and stove knobs/buttons;
  `RequestPickup`, `RequestPlace` (also for burners) and `RequestDrop` as in spec 020.
- `RpcSurfaceTests` stays green (NetworkObject and int only); `MoveDataSurfaceTests` is updated on
  purpose to the five RPCs.
- Server checks for every request: reach to the target object (2.5 m), rate limit, hands, capacity,
  sim refusal before anything changes; hand over first and clear only on success (spec 016 order).
- Vessel contents sync through the vessel's own SyncLists; a vessel in someone's hands shows its
  contents to everyone.

### Migration
- Board, Pan, Pot, PlatingTable and Bowl stop being stations. Their tests move to vessel keys; the
  sim results (hashes) must stay the same as before for the same inputs and times. Third person keeps
  walking, looking, pick up and drop, crates and the counter (Open question 5).

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes.
- [x] AC2: `VesselRules` tests: capacities per kind; plated food never fits; a full pot refuses the
      5th ingredient; a pan accepts one food.
- [x] AC3: Pot tests: two ingredients put in at different ticks, burner on, pour = the hash of
      `mix(boil(a, ta), boil(b, tb))`; one ingredient = today's boiled item; off the burner no time counts.
- [x] AC4: Pan and board tests keyed by vessel id give the same hashes as the station versions did
      (fry 150 s, sauté, cut slices/sticks).
- [x] AC5: Pour and sell tests: pan to plate, pot to bowl, bowl to plate; full plate refuses and
      nothing changes; selling a plate gives today's price and stars.
- [x] AC6: `InteractionRules` tests for every new row, including empty hands + empty pan = `PickUp`,
      empty hands + pot with food = `PickUp`, potato + pot with room = `PutInto`, pan with food +
      plate = `Pour`, potato + free stove-top spot = `Place`.
- [x] AC7: Reflection: `RequestUse` has only NetworkObject and int; the interactor's ServerRpcs are
      exactly the five listed; `Vessel` syncs only bytes, strings and `FoodLook`.
- [x] AC8 (2026-11-07 night, `-demo-cookware` on one client: pan moved to the free burner, potato on the stove top, pot with potato and onion on a burner that is on; no exceptions on client or server): Screenshots (dev demo arg on one client): the pan picked up and placed on a burner; a
      potato on the stove counter next to it; the pot with two ingredients on a burner.
- [ ] AC9 (manual, human): mouse only: buy a potato and an onion, cut the potato, put both into the
      pot, boil, pour into a plate, sell; move the pan to the other stove and fry there; place food on
      the stove counter. A second player sees the pan move and the pot's contents.
- [x] AC10: `WhatsNew.json` entry `spec-021` with test steps.

## Test plan
- EditMode: `VesselRulesTests`, `PotTests`, `VesselHeatTests`, `PourAndSellTests`,
  `InteractionRulesTests` (extended), surface tests updated; existing cooking tests migrated to vessel
  keys with unchanged expected hashes.
- Screenshot run (dev demo arg, single client), human play-test (AC9).
- Command: `bash scripts/verify.sh`.

## Open questions (answered 2026-11-07: human approved all defaults)
1. **Kitchen layout:** worktops + 2 stoves with 2 burners each + counter + plate shelf, cookware lying
   on top as described? **Default: yes.**
2. **Pan capacity:** one food at a time (several only in the pot)? **Default: yes; can grow later.**
3. **Empty hands on a pan with food:** take the food out (`Take`), and you pick up the pan when it is
   empty? Or always pick up the whole pan? **Default: take the food; the pot is always picked up whole.**
4. **Plates:** a plate shelf gives clean plates for free, a sold plate goes away with the meal (no
   washing yet)? **Default: yes.**
5. **Third person:** cooking only in first person; third person keeps walking, pick up, drop, crates
   and the counter? **Default: yes.**
6. **Ingredients in the pot** each count their own time and are mixed when poured (no /sim change)?
   **Default: yes.**

## Human steps
- Answer the open questions and approve this spec.
- Play-test AC9.

## Implementation notes
Built 2026-11-07 (builder). `verify.sh` PASS, Unity EditMode 458/458 (baseline 422). Not committed.

**Pure layer.** New `VesselKind`, `VesselRules` (capacity board 1, pan 1, pot 4, plate 4, bowl 6; plated never fits =
`AlreadyPlated`; full = new `VesselFull`; pour matrix pan/pot -> plate/bowl, bowl -> plate; empty hands take out of
board/pan/bowl, pot and plate are picked up; `MaxPlatesInWorld` 8; RequestUse actions 0/1/2). `KitchenState` is keyed by
vessel object id: board sessions unchanged (int key = vessel id), heat is a list of `HeatSession` per vessel (pan 1,
pot up to 4, each started at its own put-in tick), plates/bowls as before (`TryAddToVessel`, `TryPreviewTake`,
`ClearVessel`, `NamesIn`, `LooksIn`). Burners keyed by place-surface index: `BurnerOf`, `TryUseBurner(burner, button)`,
`SetVesselBurner(vessel, burner|NoBurner)`; a vessel's clocks run only while it stands on a burner that is on. Pot pour =
each ingredient `boil`ed for its own on-time (unchanged if below the 30 s minimum), then `mix` (2+), no /sim change.
`TryPour` is atomic (computes, checks target capacity/plated/sim dry run, then moves). Generic dispatchers
`TryPutInto`, `TryPreviewTakeOut` (pot -> new `PourInstead`), `ClearContents`, `ContentNames/Looks`, `ResultName`.
The pan's op (fry/saute) is the mode of the burner it stands on when the food goes in (fry when on none) and stays with
the food; the mode button is refused (`PanBusy`) while a pan with food stands on that burner. `StationKind` is now
Crate, Counter, Stove, PlateShelf; `StationRules` keeps the switch/mode numbers, adds `StoveOption(burner, button)`
(burner 0 = 1/2 as in spec 016, burner 1 = 3/4) and two flag bits per burner. `StationAction.cs` deleted (board digit
presets); `TryApply`, digit options, `FirstPersonKeysHint/Prompt`, `KnifeStroke/Turn` removed with their stations.
- Hash safety net: pinned golden fries `2ab1f862...` unchanged (`KitchenStateTests`, `VesselHeatTests`, also after
  carrying the pan between burners); cut tests now cut on a board vessel through `HeatTestKit.Cut` (same item as the old
  digit keys, proved in spec 019); every migrated heat/plate/bowl/look/economy test kept its expected hashes and values.

**Network/service.** `RequestUse(NetworkObject target, int action)` (0 use, 1 stroke, 2 turn), five ServerRpcs in
total. `KitchenService.TryUseVessel`: target spawned cookware nobody holds, reach 2.5 m to the vessel, rate limit (the
interactor's 0.2 s), then hands: food -> put in; holding cookware -> pour; empty -> take out (hand over first, clear
on success); knife -> stroke/turn on a board. `RequestStation` stays for crates, counter (sells loose food as before,
or a held plate: plate op on its parts, plate despawned with the meal), plate shelf (spawns a plate into the hands, at
most 8 plates spawned, else `NoPlatesLeft`) and stove knobs/buttons. `Vessel` (NetworkBehaviour, no RPCs): SyncLists
`Names`/`Looks`, `SyncVar<string> Title` (name of what you would get), `SyncVar<byte> Mode`; draws contents with a
`FoodVisual`, shows a billboard label with the lines (empty when empty). `PickupService.ItemMoved(item, surface)` is
raised on pickup, place, drop, hand-over and take-away; the kitchen uses it to start/stop pan and pot clocks.
`PlaceSurface` has `IsBurner`, `StoveIndex`, `Burner`. `StationContents` keeps only `StoveFlags`. Lost knife and
cookware (gone or below -10 m for 5 s) respawn at their start position (contents lost); lost plates are only removed.
`Pickup.ServerDetach` now rests the collider bottom on the floor (`y = BottomOffset`): food 0.2 (was 0.25), cubes 0.25
(same), knife 0.04, cookware 0 (their pivot is at the bottom; with the old fixed 0.25 they would float).

**Client.** `InteractionRules` implements the spec 021 table (first match wins). `AimKind.Vessel` (new value) is a target
that is never transparent with full hands; `InteractionAction.Pour` added, `PutOnBoard` removed (board = `PutInto`).
Prompts: "Click · burner on/off", "Click · put potato into the pot / on the board / on the plate", "Pot is full",
"Click · pour into the plate", "Click · sell <meal name>", "Click · put pan on the burner", "Click · take a plate",
"Click · pick up the pot". Extra grey reasons: empty plate at the counter "Put food on the plate first", pan/pot/bowl
with food at the counter "Pour it onto a plate first". HUD: held line "Pan: crispy fried potato sticks" / "Pan (empty)";
toasts for the new outcomes. Third person: E picks up/drops (cookware too), crates, counter (incl. a held plate), shelf;
no stove or cooking keys (open question 5).

**Scene** (31 network objects). Row at z 13: crates x -8.5..-2.5 (unchanged); Stove 1 x 3, Stove 2 x 5.5 (spec 015
stove model, `ArtKey.StationPan`), Counter x 8.5, Plates shelf x 11 (cabinet `StationPlatingTable` + a static stack of
5 plate models at local (0, top, -0.1)). Worktops (plain, 1.2 deep, at the stove top height): x -0.1..2.38,
3.62..4.88, 6.12..7.88, 9.12..10.38, 11.62..14.1. Per stove: burners at local x -0.3 / +0.3, z -0.12, 0.42 x 0.42
(one spot), a dark ring 0.3 m that glows orange when on, the stove top behind them z 0.09..0.6 (4 x 2 spots); per
burner a knob ("on/off") at local x -0.09 and a mode button ("fry") at +0.09 from the burner's x, y 0.3, z -0.64.
Surfaces: 0 worktop, 1-3 stove 1 top/burner 0/burner 1, 4 worktop, 5-7 stove 2, 8-10 worktops. Start: cutting board
(first worktop, local x 0, z -0.125) with the knife 0.5 m right of it, pan on stove 1 burner 0, pot on stove 2 burner 0,
two plates on the counter-shelf worktop (local x -0.25 / +0.25), bowl on the last worktop (x -0.5). Prefabs
`Prefabs/Board|Pan|Pot|Plate|Bowl.prefab` (generated): collider = model bounds, at least 6 cm high, pivot at the bottom;
sizes board 0.354 x 0.06 x 0.5, pan 0.354 x 0.065 x 0.55 (handle included), pot 0.336 x 0.184 x 0.42, plate 0.35 x 0.06
x 0.35, bowl 0.279 x 0.119 x 0.323; content anchor at fill x height (board 1.0, pan 0.35, pot 0.75, plate 0.4, bowl 0.3)
with the spec 017 part scales. `DefaultPrefabObjects.asset`: the five new entries appended, the reorder reverted. The
rebuild's re-serialised `PlaceMarker.mat` / `Worktop.mat` were reverted; FishNet's "same assetPath hash of 0" errors appeared.

**Deviations / outside the listed files (reviewer please check).** (1) `AimKind.cs`, `InteractionAction.cs` (Net/Rules),
`AssemblyRules.cs`, `StationButton.cs`/`FoodLook.cs` (doc only), `ArtKey.cs` (doc only) changed; `StationAction.cs`
deleted; generated prefabs added. (2) No `NotOnBurner` outcome: food may go into a pan or pot anywhere, it just does not
cook off a burner. (3) The `-demo-cut` and `-demo-place` dev args were removed (their stations no longer exist); replaced
by `-demo-cookware`. (4) `Knife.BoardIndex` is no longer used (Knife.cs untouched). (5) Tests that tested removed station
features (board digit presets, `DigitOption`, first-person station prompts, `KeysHint`) were replaced by tests of the
new rules (stove options, burner flags, button prompts); the scene tests now check the new layout. (6) A respawned item
appears at its home spot even if something else lies there now (as the spec 019 knife did). (7) `InteractionHighlight`
(Player/, out of scope) still mentions `PutOnBoard` in a doc comment; `HeldItemView` is unchanged, so a held pan is drawn
at the view anchor at full size (0.55 m) - check in the screenshots.

**Dev demo (AC8)**, development builds only, one client (simulated keys and mouse need window focus), first spawn point:
`<client exe> -connect <server> -view first -skip-whatsnew -demo-cookware -screenshot shot.png -screenshot-after 18 -screenshot-every 3`.
Steps, each "aim, wait for the expected action, click" (fallback: the same request directly, logged as a warning):
walk to stove 1, pick up the pan (`RequestPickup`); stove 2, put it on the free burner 1 (`RequestPlace`); potato crate,
take a potato (`RequestStation`); stove 2, put it on the stove top spot nearest the pan (`RequestPlace`); take a potato and
put it into the pot, take an onion and put it into the pot (`RequestUse`); press the pot burner's knob
(`RequestStation(stove 2, 1)`); then look at the stove for 40 s. Estimated times after the client starts (4 m/s walk,
every step logs `[Demo] ... (t s)`): pan picked up ~15 s, pan on the burner ~20 s, potato next to it ~36 s, potato in
the pot ~51 s, onion in the pot ~62 s, burner on ~65 s, done ~65-105 s.

**What to check in the screenshots.** Whether the pan (handle direction, 0.55 m long) and the pot sit on the burners and
the rings show under them; the ring glow and knob colours; the stove top spot next to the pan (the 0.4 m food collider
may overlap the pan's); the label lines above the pot ("potato · warming up", "onion · ..."); that the knobs sit on the
stove model's front; the plate stack on the shelf; worktop height against the stove and counter models; the held pan's
size in view.

## Blockers
