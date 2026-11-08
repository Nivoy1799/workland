---
id: 027b
title: Feel pass 1 (instant feedback, no teleports, forgiving aim, tuning panel)
status: approved
owner: architect
builder: opus
---

# 027b: Feel pass 1 (moving and interacting)

## Goal
The human: "everything feels off: how you move, how you interact". This spec applies docs/feel.md sections 1–5,
8 and 9 to what exists today: picking up, putting down, putting into cookware, pressing buttons, cutting, moving
and looking. It adds a live tuning panel (F2) with two presets, so the human can say "A" or "B" without needing
words for it. The server stays the authority for every rule (AGENTS rule 6); only the **look** is predicted.

Approved by the orchestrator under the human's 24 h grant (docs/cooking-roadmap.md). Build after 027a.

## Depends on
- 018–021 (views, cutting, mouse interaction, cookware), 023 (sounds), 025/026 (characters, sizes),
  027 (visual check, to verify), 027a (floor plan).

## Scope
- `game/Assets/_Project/Scripts/Player/`:
  - new `FeelSettings` (all feel numbers in one place, with presets A and B);
  - new `FeelRules` (pure easing, arcs, cone aim scoring, stickiness);
  - change `InteractionHighlight` (outline plus prompt, out-of-reach grey);
  - change `PlayerCameraRig` (numbers from FeelSettings);
  - change `HeldItemView`.
- `game/Assets/_Project/Scripts/Net/`: `PickupInteractor` (cone aim, sticky target, predicted visuals,
  click throttle) and `Pickup` (visual tween of the model child on holder or position changes, for every viewer).
- `game/Assets/_Project/Scripts/Art/`: new `PlacementGhost`, `ItemTween`.
- `game/Assets/_Project/Scripts/Audio/`: new sounds in `SoundCatalog` / `ProceduralSounds` (pick, place tock per
  material: wood, metal, ceramic, food; nope; button click).
- `game/Assets/_Project/Scripts/UI/`: new `FeelPanelView` (F2, development builds only).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs`: third-person camera numbers from FeelSettings.
- Tests, `WhatsNew.json`, this spec.

## Out of scope
- New RPCs or MoveData fields (RpcSurfaceTests and MoveDataSurfaceTests unchanged).
- Server rule numbers: reach, aim range, place grid, capacities and server rate limits stay.
- Cooking feedback (spec 027c).

## Design
**1. Instant answer, predicted look.**
- On a click, the client immediately starts the visual of the expected result: the item flies to the hand, onto
  the spot, or into the pot. It also plays the sound and sends the same RPC as today.
- The server's normal state change (holder, position, contents) confirms it, and the visual just continues.
- If no confirming change arrives within `FeelSettings.ConfirmTimeout` (default 0.35 s), the visual flies back
  and a short "nope" plays.
- The prediction is a local visual offset on the item's model child only. The networked transform and
  colliders never move on the client.

**2. No teleports.**
- When an item's holder or resting position changes, every viewer (not only the owner) tweens the model child
  from where it was drawn to the new place:
  - pick up: 0.15 s ease-out arc with a 0.08 m lift;
  - place: 0.12 s ease-out with a 3 % settle;
  - into a vessel: 0.12 s drop.
- The tween is cancelled cleanly if the state changes again mid-way.

**3. Forgiving, stable aim (client selection only; the server checks reach as before).**
- Candidates are the interactables within `AimCone` (default 6°) of the view ray and within aim range.
- Score: the angle to the crosshair, with a bonus when the candidate accepts the held item (pot or pan for a
  potato, a plate for a cooked dish) and a bonus for the current target.
- **Stickiness:** switch targets only when the new score beats the current one by `StickMargin` (default 25 %).
- The exact ray hit always wins when it lands directly on an interactable.

**4. Highlight and prompt.**
- Outline in the Interact palette colour, plus a short prompt naming the left-click action.
- Out of reach: the outline is grey and the prompt says "Too far".
- The highlight never flickers for single frames (minimum show time 0.1 s).

**5. Placement ghost.** While holding an item and aiming at a place surface or vessel, a translucent copy
shows exactly where it will land (the spot the server would pick for this click).

**6. Clicks are never swallowed.**
- The client click throttle (`MinRequestInterval` 0.2 s today) becomes an input buffer: a click inside the
  interval is queued (at most 1) and sent when allowed, so fast double actions work.
- The server rate limit stays as it is.

**7. Movement and camera.**
- Keep the movement numbers (they already meet feel.md 5); verify them with tests.
- Third-person position damping goes from 0.2 to 0.08 and composer damping from 0.1 to 0.05.
- Mouse smoothing stays off.
- Head bob is a FeelSettings option, off by default.

**8. Sounds.** Pick, place tock (by the material tag of the target surface or item), nope, and button click.
Generated in code like spec 023. Every action has a sound within 1 frame.

**9. Tuning panel (F2, development builds only).**
- Sliders for every FeelSettings value.
- Buttons "Preset A" and "Preset B":
  - A = this spec's defaults;
  - B = a smoother, heavier variant: tweens about 1.6× longer, cone 9°, damping 0.12.
- "Save" writes `feel.json` next to the build's log and logs the path, so the human's choice can be committed
  as the new defaults.
- The panel shows which preset is active.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC and MoveData surface tests unchanged.
- [ ] AC2: `FeelRules` tests:
  - easing curves (start, end, monotonic);
  - arc height;
  - cone scoring (angle order, held-item bonus);
  - stickiness (no switch below the margin, switch above);
  - the click buffer (a second click inside the interval is sent once, never more).
- [ ] AC3: Prediction tests, with the server confirm and deny paths simulated in EditMode:
  - a confirmed pickup keeps the visual;
  - a denied one returns within ConfirmTimeout plus the tween time and plays nope;
  - the networked transform never changes on the client.
- [ ] AC4: Visual check sheet, frames 0.1 s apart, shows:
  - the pan travelling in an arc into the hand (no single-frame jump);
  - a potato dropping into the pot;
  - the ghost before placing;
  - the highlight and prompt.
- [ ] AC5: The demo cook sequence completes. In the log, measured click-to-visual latency is 1 frame and
      click-to-confirm on a local host is under 100 ms.
- [ ] AC6: F2 panel works in a development build: the presets switch live and Save writes feel.json. It is
      absent from release builds (test).
- [ ] AC7 (human): try Preset A and B and pick one (docs/human-inbox.md).
- [ ] AC8: `WhatsNew.json` entry `spec-027b`.

## Defaults to review
ConfirmTimeout 0.35 s; tween times 0.15 / 0.12 s; lift 0.08 m; AimCone 6°; StickMargin 25 %; highlight
minimum 0.1 s; third-person damping 0.08 / 0.05; Preset B values above.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
