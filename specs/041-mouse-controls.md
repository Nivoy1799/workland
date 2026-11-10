---
id: 041
title: Mouse-first controls (left click does, right click puts down; no Q)
status: approved
owner: architect
builder: opus
---

# 041: Mouse-first controls

## Goal
The human, after playing (2026-11-10): "Q = drop, I am not a fan of. Should it not also be a mouse click, like left
picking and right dropping? Left with an item on something: interacting." The hands should live on the mouse, like in
the genre's best games. docs/feel.md 8 says one input has one meaning everywhere.

## The mapping (the contract)
| Input | Meaning everywhere |
|---|---|
| **Left click** | Do the obvious thing with what you aim at. Empty hands: pick up, take, press. Item in hand: use it on the target (put into the pot, slide onto the plate, place on the counter, cut, flip, scoop, serve). |
| **Hold left** | The hold actions: squeeze, pour, scrub, shave, wipe (as today). |
| **Right click** | **Put down what you hold**, at the aimed spot if you aim at a surface (with the 027b ghost preview), else drop at your feet. |
| **Mouse wheel** | Turn: the food on the cutting board (today on right click), and the held item's rest rotation before you put it down (in 45° steps). |
| **E** (third person) | Same as left click on the nearest target. In third person the mouse orbits the camera, so left click also works on the nearest target. |
| **Q** | Removed from the prompts and the tutorial. It is kept only as a hidden alias for keyboard-only players, unless the human says remove it entirely (question 1). |
| **Gamepad** | Right trigger = left click, left trigger = right click, bumpers = turn. |

- Every prompt names the mouse button: "Left click · put into pot", "Right click · put down here".
- The right-click flip on the griddle (spec 030) goes away: a flip is left click with the spatula, like every tool use.
- The tutorial (038) and the What's-new texts are updated, and the F1 help shows the new mapping.
- Server authority is unchanged: the same requests go out, only the input that triggers them changes. No new RPCs.

## Scope
- `game/Assets/_Project/Scripts/Player/PlayerInputReader.cs` and the input actions asset.
- `Scripts/Net/PickupInteractor.cs` (input routing), `Scripts/Net/Rules/InteractionRules.cs` (prompts).
- `Scripts/Player/CutGesture` (turn on the wheel).
- `Scripts/Tutorial/*` (step texts), `Scripts/UI` (help).
- Tests, `WhatsNew.json`, this spec.

## Acceptance criteria
- [ ] AC1: `verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Pure mapping tests: each input and context gives the expected action; right click never does anything but
      put down or drop.
- [ ] AC3: Visual check: put down by right click on the front counter (with the ghost), drop on the floor, turn food on
      the board with the wheel, flip on the griddle with left click.
- [ ] AC4 (manual, human): play a shift with the mouse only.
- [ ] AC5: `WhatsNew.json` entry `spec-041`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Keep Q as a hidden alias for put down? **Yes, hidden** (not in prompts or tutorial).
2. Right click while aiming at a pot or pan with food in hand: put the food **next to it on the counter** (yes) or into
   it? Left click puts it in, so right click stays "put down here".

## Implementation notes
(Builder fills this in.)
