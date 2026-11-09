---
id: 038
title: First-shift tutorial (guided steps through every station, skippable)
status: approved
owner: architect
builder: opus
---

# 038: First-shift tutorial

## Goal
The kitchen now has stock, prep, a griddle, a fryer, stoves, a spit, assembly, drinks, a register, cleaning and
ordering. A new player (and the human, play-testing after a long autonomous run) needs a calm, guided first shift.
The tutorial shows one step at a time:
- a short instruction ("Take a potato from the shelf");
- a highlight on the target station or slot;
- the step completes when the player actually does it, as detected from the real game state;
- it can be skipped at any time and replayed from the menu.

It teaches by doing, not by text walls (docs/feel.md 8). Spec 037 (pricing) comes later because it waits for the
human's answer on the price scale. Approved by the orchestrator under the human's 24 h grant.

## Depends on
Everything from 027a to 036 (stations, prompts, highlights, calm view, orders, register, cleaning, stock). It uses the
existing highlight (027b) and prompt system. The steps are client-side guidance only.

## Scope
- `game/Assets/_Project/Scripts/Tutorial/` (new):
  - `TutorialSteps` (pure: an ordered list of steps, each with a goal, a target selector and a done-predicate over
    an observable snapshot of the local player's state: held item, aimed target, last outcome, station bytes);
  - `TutorialRunner` (a MonoBehaviour, owner client only);
  - `TutorialPanelView` (UI Toolkit: a small card at the top centre with the step text, a progress "3 / 14", and
    "Skip tutorial");
  - a persistent "done" flag in PlayerPrefs.
- The steps of the default first shift:
  1. move and look;
  2. take a potato from the shelf;
  3. cut it into sticks on the board;
  4. fry the sticks (load the basket, lower, shake, raise);
  5. tip the fries into a box;
  6. take a wrap and build a falafel wrap (fryer falafel, lettuce, sauce hold);
  7. fold and wrap it;
  8. serve a guest at the counter;
  9. take the payment at the register (count change);
  10. fill a lemonade and lid it;
  11. wash a dirty plate;
  12. order stock at the terminal;
  13. (optional) shave the spit;
  14. done.
  The steps adapt: if the player already holds the needed item, that step completes at once.
- Guidance:
  - the target gets the 027b highlight in a tutorial colour (Mustard pulse), even when it is not aimed at;
  - an arrow at the screen edge points to it when it is off-screen;
  - the prompt line stays as it is.
- Tutorial mode on the server: no new rules. The first shift's guests are calm (patience × 3) only while the local
  tutorial is running. That is a server flag set through an existing request, or simply a host-only option when
  starting as host with `-tutorial`. Choose the simplest server-authoritative way, with no new RPC, and explain it.
  A dedicated-server game never changes its rules for one player.
- The What's-new panel gets a "Play the tutorial" button. The pause menu, or F1 help if it exists, gets "Replay
  tutorial".
- Tests:
  - TutorialSteps predicates (pure, over snapshots);
  - order and adaptivity;
  - skip and replay;
  - the panel is absent when done.
- Visual check: a new tour `tutorial` runs the steps through the tour actor and shoots each step's card and
  highlight. `WhatsNew.json` entry, this spec.

## Out of scope
- Voice-over, localisation, achievements.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: TutorialSteps tests: each predicate fires on the right snapshot only; adaptivity; skip and replay.
- [ ] AC3: Visual check `tutorial`: every step's card plus highlight, an off-screen arrow, the done card.
- [ ] AC4: A dedicated server never changes its rules for one player (test or code review note).
- [ ] AC5: `WhatsNew.json` entry `spec-038`.

## Defaults to review
Step list and wording; patience × 3 for tutorial guests on a host; the Mustard pulse.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
