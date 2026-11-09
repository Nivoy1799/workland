---
id: 027b
title: Feel pass 1 (instant feedback, no teleports, forgiving aim, tuning panel)
status: done
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
- **Where the look lives.** `Pickup` draws every item through an `ItemTween` (Art) for every viewer: the model
  children (labels excluded) are moved for each camera render and put back right after, as spec 024's hand drawing
  did, so the networked transform, colliders and rules never see a moved item (AC3 test checks the root every frame).
  `HeldItemView` no longer moves anything; it only answers the first-person view pose. A goal change (rest, hand, view,
  hold point, into) picks the tween with `FeelRules.StyleFor` (arc, settle, none for a view toggle). After a put-down
  the networked position needs 2–3 ticks (NetworkTransform interpolation 2 at 30 Hz), so the model waits where it was
  drawn until the position stopped moving (at most `Pickup.RestSettleMax` 0.3 s), then settles onto it; with a
  prediction it waits on the predicted spot instead, so nothing jumps back towards the head.
- **Prediction.** `PickupInteractor` starts the look in the click's frame (`Pickup.PredictHeld/PredictRest/PredictInto`,
  the drop pose mirrors `ServerDetach`'s in-front spot) and judges it with `FeelPrediction` from synced state only
  (holder, position of a lying item, the vessel's content count). The timeout counts from the click or the last
  relevant change, and needs two observations past the deadline (one long frame never denies). The outcome the server
  already reports (ReportResult/Place/Cook) is used too: a refusal denies at once, "ok" restarts the timeout. No new
  RPCs or MoveData fields; the surface tests are unchanged.
- **Into a pot.** The food flies to just above the contents (`DropHeight`), the server despawns it, and `FoodVisual`
  drops the new part in from that height for every viewer. Other viewers see the food leave the hand without a flight
  (the despawn has no destination); a follow-up could fly it from the hand.
- **Aim.** Exact ray hits on a station, button or place surface always win; an item counts as hit directly only within
  `DirectHitRadius` 0.11 m of its centre (the pan's handle lay over the free spot next to it: that was the finding).
  Otherwise the cone (only actionable targets, in reach, in sight) with stickiness decides. The 0.1 s minimum only
  holds a target that was *lost*; a new target replaces the old one at once (an early version also held new targets
  for 0.1 s, and the tour then clicked the stove top behind the burner it aimed at; fixed).
- **Stride while standing (finding).** Measured, not the state machine: the observer log (`[Tour] observer anim`)
  shows the standing remote cook in Idle at 0.00 m/s, with no tick jitter even frame to frame. The library's
  `Idle_Loop` itself stands with the left foot 0.29 m ahead of the right (a walk stride is 0.44 m), which reads as a
  frozen stride from the side and while turning. Fix: `CharacterVisual` blends the legs of the library's `A_TPose`
  (legs and foot IK only) over the idle at `CharacterAnimRules.StandLegsWeight` 0.7, scaled by the idle's weight, so
  walking and crouching keep their legs. `StandingPoseTests` samples the real clips on the real body (alone > 0.25 m,
  with the layer < 0.12 m). Customers use the same idle and get the same fix.
- **Camera.** Third person 3.2 m (was 5), look point 1.3 m (was 1.6), pitch 12° (was 20), damping 0.08 / 0.05,
  wheel range 1.6–7 m; preset B 3.8 m, 15°, 0.12 / 0.08. `PlayerCameraRig` re-applies them when the panel changes them.
- **F2.** In development builds F2 opens the feel panel and the What's-new banner moves to Shift+F2 (release builds
  keep F2; spec 011 had F2 for the banner, so this needs the human's nod). The panel adds itself at runtime and the
  whole file is inside the dev guard (`FeelPanelTests`). Save writes `feel.json` next to the player log.
- **Sounds.** Pick, place tock per material (item first, else the surface), nope, button: played by the interactor in
  the click's frame. The old polled "tick on a change of hands" in `KitchenSounds` is gone (it came up to 0.1 s late
  and would double them). Other players' put-downs make no tock yet.
- **Scope.** Beyond the listed folders: `Scripts/Dev` (tours: frames around clicks, slow motion x4 so the arc shows at
  0.1 s frames, observer animation log; asked for by the orchestrator), `Scripts/Art/FoodVisual.cs` (the drop),
  `Scripts/UI/WhatsNewView.cs` (Shift+F2), `Tests/EditMode/WhatsNewTests.cs` (027b is now the newest entry, same pattern
  as earlier specs). Not done: a visible sink of a pressed button (feel.md 2), only its click sound.
- **Movement.** Kept: walk full speed in 0.13 s, stop in 0.10 s (tests). Sprint from standing takes 0.26 s.
- **Measured (AC5, visual check on a local host, `[Feel]` lines in `.verify/visual/*-tour.log`).** Click to visual: the
  click's own frame for every predicted click (one tour click shows "1 frame later" because the tour clicks after that
  frame's render). Click to confirm: 44 to 98 ms (pick-up 46 to 98, place 53 to 82, put-into 44 to 77). The 98 ms is
  close to the 100 ms limit; at 30 Hz the request and the state both wait for a tick.
- **Visual check (AC4).** The look sheet has the pick-up and the put-back at normal speed and again at 4x tween
  length (`*-x4` cells), because a 0.15 s arc only spans one or two 0.1 s frames. The cook sheet shows the highlight and
  prompt before each click, the ghost on the burner, the potato dropping into the pot, and the onion in slow motion.
  The observer sheets only take guests'-view shots every 6 s, so they do not show remote arcs frame by frame.
- **Review fix round (on top of 027c).**
  - Request spacing: clicks, Q, E and knife strokes keep `PickupRules.ClientRequestInterval(tick)` = 0.2 s + one tick
    + 0.01 s (0.243 s at 30 Hz), so a queued click never lands inside the server's limit.
  - Answers: every answered request is numbered (`PredictionBook`); the n-th answer reaches only the prediction of the
    n-th request with the matching kind (ReportCook maps to PutInto only). Requests dropped by the server's rate limit
    are written off after 1 s without requests.
  - Confirm timeout: `FeelRules.ConfirmTimeout` = max(setting, 1.5 x round trip + 2 ticks).
  - Networked roots: never moved. A mesh on the root (the spec 003 cube) gets a "TweenVisual" child at client start
    (root renderer off; collider and network components stay on the root); without a visual child nothing moves.
  - Pure state machines: the look logic moved from `Pickup` into `ItemLook` (goal, tween to start, settle, prediction).
    The waiting predictions and answer matching are in `PredictionBook`. The AC3 tests drive these classes directly.
    Settling is cleared whenever a holder is drawn.
  - Superseded clicks: a prediction is replaced only by a newer one for the same item, so a quick second click on
    another item never makes the first bounce.
  - Queued clicks: E goes through the buffer as well. A queued click is dropped when it did nothing when clicked, or
    when the target (or spot) changed before it could go out.
  - Put-into: confirmed only when our food is despawned; a changed content count only restarts the timeout.
  - Exact hits: a direct exact hit now goes through `FeelRules.PickTarget` as a Direct candidate.
  - Hot paths:
    - `Vessel.ContentsAnchor` / `ContentPoint` are cached;
    - `ItemTween` caches its child list;
    - pickups come from `Pickup.ClientList` (filled in OnStartClient / OnStopClient);
    - stove buttons and surfaces are looked up once;
    - KitchenSounds is looked up once;
    - spot occupancy is cached for 0.1 s.
  - Shutdown: pending predictions end in `PickupInteractor.OnStopClient`.
  - Legs layer: weight is `CharacterAnimRules.LegsLayerWeight(idle, moving)`, which is 0 outside idle and gone by the
    middle of an idle-to-walk fade. The avatar masks are destroyed once the layer has copied them.
- **Second review round (on top of 027d).**
  - Server rate limit: a token bucket per player (`RequestBucket`: burst 2, one token per 0.2 s), so bunched arrivals
    of well-spaced requests pass and the average stays capped. A refused request is now answered with the new
    `PickupOutcome.TooFast` / `CookOutcome.TooFast` through the existing report RPCs (no new RPC, no new parameter).
    The client only counts that answer and shows nothing for it.
  - Numbering: `PredictionBook` applies a refusal only when its request was the only one in flight. "Ok" answers and
    ambiguous refusals cannot deny anything; the synced state or the timeout decides those. The quiet-spell reset
    waits for the longer of 1 s and three round trips. A late answer with nothing in flight is ignored.
  - Round trip: assumed 150 ms until the first ping (`FeelRules.RoundTrip`).
  - Queued click: dropped when the action changed as well (`FeelRules.KeepQueuedClick` compares the action).
  - Pickup: no closure allocation per click.
  - 027c fixes:
    - heat timelines cached per (item hash, op, temperature) in `KitchenState`;
    - surface test pinning ServeFeed and ServeReward;
    - `ServeRewardView` resets `_seen` while the feed is not spawned, caches the `_BaseColor` id, and plays the coin
      only after the view-distance check;
    - `HeatRingView` destroys its overlay material, looks up the rig at most once a second, and uses
      `Vessel.ContentsAnchor`.
- **Third review round (on top of 027d).**
  - Heat timeline key: the cache key (`KitchenState.TimelineKeyOf`) is now the exact state the heat op reads: the hash,
    op and temperature, plus the float bits of the surface, the heat tolerance, and each component's doneness and ideal
    window. Two onions sautéed 60 s and 170 s share a hash but get their own timelines.
  - Label pass: no per-frame allocations. `LabelCull` keeps its buffers and uses an insertion sort; queue ranks use a
    cached comparer; icon letters come from a table. The camera rig lookup is throttled to once a second.
  - `HeatRingView.TryGetScreenRect` returns false for a ring behind the camera.
  - `RequestBucket` uses double time; its tests are in `RequestBucketTests`.
  - Customer looks: hashed (`ArtCatalogRules.CustomerVariant`). An odd id avoids its two even neighbours' looks, so 027d's
    "neighbours differ" still holds. The looks and both sexes are spread evenly for id steps 1, 2, 3, 6 and 7 (tested).
  - The WorldText shader stays in Resources for clients. `Editor/Build/ServerShaderStripping` compiles no variant of it
    in a dedicated server build.
  - Nits:
    - the unused `MinRequestInterval` is removed;
    - the `PredictionBook` comment is fixed;
    - the dead label hiding is removed from `HeldItemView`;
    - the late-answer test uses a number the book gave out.

## Blockers
(Builder fills this in after 3 failed attempts.)
