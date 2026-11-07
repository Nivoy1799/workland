---
id: 021
title: Movable cookware on normal worktops (board, pan, pot, plates, bowl as items; several ingredients in a pot)
status: draft
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
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: `VesselRules` tests: capacities per kind; plated food never fits; a full pot refuses the
      5th ingredient; a pan accepts one food.
- [ ] AC3: Pot tests: two ingredients put in at different ticks, burner on, pour = the hash of
      `mix(boil(a, ta), boil(b, tb))`; one ingredient = today's boiled item; off the burner no time counts.
- [ ] AC4: Pan and board tests keyed by vessel id give the same hashes as the station versions did
      (fry 150 s, sauté, cut slices/sticks).
- [ ] AC5: Pour and sell tests: pan to plate, pot to bowl, bowl to plate; full plate refuses and
      nothing changes; selling a plate gives today's price and stars.
- [ ] AC6: `InteractionRules` tests for every new row, including empty hands + empty pan = `PickUp`,
      empty hands + pot with food = `PickUp`, potato + pot with room = `PutInto`, pan with food +
      plate = `Pour`, potato + free stove-top spot = `Place`.
- [ ] AC7: Reflection: `RequestUse` has only NetworkObject and int; the interactor's ServerRpcs are
      exactly the five listed; `Vessel` syncs only bytes, strings and `FoodLook`.
- [ ] AC8: Screenshots (dev demo arg on one client): the pan picked up and placed on a burner; a
      potato on the stove counter next to it; the pot with two ingredients on a burner.
- [ ] AC9 (manual, human): mouse only: buy a potato and an onion, cut the potato, put both into the
      pot, boil, pour into a plate, sell; move the pan to the other stove and fry there; place food on
      the stove counter. A second player sees the pan move and the pot's contents.
- [ ] AC10: `WhatsNew.json` entry `spec-021` with test steps.

## Test plan
- EditMode: `VesselRulesTests`, `PotTests`, `VesselHeatTests`, `PourAndSellTests`,
  `InteractionRulesTests` (extended), surface tests updated; existing cooking tests migrated to vessel
  keys with unchanged expected hashes.
- Screenshot run (dev demo arg, single client), human play-test (AC9).
- Command: `bash scripts/verify.sh`.

## Open questions (defaults in bold; the human answers before approval)
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
(Builder fills this in.)

## Blockers
