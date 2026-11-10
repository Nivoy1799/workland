---
id: 042
title: Drinks 2 (put the cup under the tap, then fill it)
status: approved
owner: architect
builder: opus
---

# 042: Drinks 2: put the cup under the tap, then fill

## Goal
The human, after playing (2026-11-10): "the drink station also works, but it would be nice if you had to place the cup
at the machine and then click to fill." Today you hold the cup in your hand under the tap (spec 033). Real machines and
the genre's best games work the other way round:
1. take a cup;
2. **put it on the drip tray under a tap** (a fixed spot, so it snaps into place);
3. **hold left click on that tap's lever** to fill; the level rises in the cup on the tray;
4. lid it there, or take it off and lid it;
5. take the full cup to the order.

Your hands are free while it pours, so you can turn to the next task (a short pour is about 1.8 s).

## Design
- Each of the three taps gets a **cup spot** on its drip tray. It is a fixed vessel like a fryer basket, with capacity
  one cup. Only an empty or open cup of the same drink, or an empty cup, fits there. The spot works with the 039
  placement rules (left click with the cup in hand, the ghost preview, the right-click put down from 041).
- **Pour:** hold left on the lever. The server pours into the cup on that tap's spot, keeping the 033 rules:
  - server-measured ml;
  - the auto-stop at the spill;
  - the re-press keeps its start;
  - a player who leaves is freed;
  - the same money and stock pattern (034).
  The cup no longer needs to be in your hands, and the pour record references the cup on the spot.
- **Lid:** left click the lid stack while the filled cup stands on the spot (or with the cup in hand, as today).
- **Take:** left click the cup on the spot with empty hands.
- Third person: E (or left click) on the tap with a cup on its spot pours, through the 0.2 s hold gate.
- Spill: an overfill spills over the tray and sets the 035 spill flag, as today.
- The 033 "cup in hand under the tap" path is removed. One way to do it, and the tutorial step 10 is updated.

## Scope
- `Scripts/Cooking/` (DrinkRules, DrinkDesk, KitchenState pours, three cup spots as fixed vessels).
- `Scripts/Art/` (tray spot, the cup snapping onto it, the level rising there, the lever).
- `Scripts/Net/` (InteractionRules prompts and aim for the spots).
- `Editor/GrayboxSceneBuilder.cs`, `Scripts/Tutorial` (step 10), tests, `WhatsNew.json`, this spec.

## Acceptance criteria
- [ ] AC1: `verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Server tests:
  - a pour needs a cup on that tap's spot;
  - the wrong drink is refused;
  - money and stock exactly once;
  - a cup taken away mid-pour stops it with no charge;
  - another player can't take the cup during a pour.
- [ ] AC3: Visual check: cup put on the tray, lever held, level rising, lid, taken to the order.
- [ ] AC4 (manual, human): fill a drink while doing something else in between.
- [ ] AC5: `WhatsNew.json` entry `spec-042`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Must you keep holding the lever, or does one click fill to the line by itself (an auto-fill machine, an upgrade
   later)? **Hold to fill now**; auto-fill as a later equipment upgrade (spec 037 shop).

## Implementation notes
(Builder fills this in.)
