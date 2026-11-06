---
id: 010
title: Standard third-person controls and camera (feel)
status: draft
owner: architect
builder: opus
---

# 010: Standard third-person controls and camera

## Goal
Movement and camera that feel like other modern third-person games, instead of the graybox debug
controls (arrow-key turning, fixed camera). Nothing new is invented: Unity's standard camera package
(Cinemachine), Unity's default Input System actions, and FishNet's own client-side prediction pattern.
Keyboard + mouse and gamepad both work.

## Scope
Only `game/` (branch stacked on `spec/009-sell-dishes`).
- `game/Packages/manifest.json` (change: add `com.unity.cinemachine`, pinned version for Unity 6.3).
- `game/Assets/_Project/Scripts/Net/PlayerController.cs` (rewrite: FishNet prediction, see Design).
- `game/Assets/_Project/Scripts/Player/` (new): `PlayerCameraRig.cs` (spawns and binds the local
  Cinemachine camera to the owner), `PlayerInputReader.cs` (reads the default `Player` action map),
  `MovementRules.cs` (pure: camera-relative direction, speeds, turn rate, input validation).
- `game/Assets/_Project/Scripts/Net/PickupInteractor.cs` (change: E and 1-4 come from the action map,
  Interact action; keyboard keys stay the defaults).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: camera rig, player prefab parts).
- `game/Assets/_Project/Tests/EditMode/MovementRulesTests.cs` (new).

## Out of scope
- Animations and a character model (still the capsule), jump pads, climbing, swimming, vehicles.
- Rebinding UI, settings menu, mobile/touch.
- Changing what the server validates for pickups, stations or the economy.

## Design (the genre standard)
- **Camera:** Cinemachine `CinemachineCamera` with Orbital Follow + Rotation Composer behind the
  player, Cinemachine Deoccluder so walls never block the view, damping for a smooth follow.
  Mouse or right stick orbits (yaw free, pitch clamped about -30 to 70 degrees), mouse wheel zooms
  (about 2 to 8 m). Only the owner gets a camera; other players' objects have none.
- **Mouse style:** see open question 1.
- **Movement:** WASD / left stick moves relative to the camera's view; the character turns smoothly
  toward the direction it moves (no strafing turn on its own). Walk about 4 m/s, Shift / stick-click
  sprints about 6.5 m/s, short acceleration and deceleration so it does not feel like ice or a wall.
  Space / south button jumps (small, mainly for feel). Gravity and slopes from `CharacterController`.
- **Netcode (state of the art, FishNet standard):** client-side prediction with reconciliation, following
  FishNet's own `CharacterControllerPrediction` example (`[Replicate]` / `[Reconcile]` on a
  `TickNetworkBehaviour`). The owner moves instantly; the server runs the same input and stays the
  authority, corrections are smoothed. Input data stays intents only (move vector, camera yaw,
  sprint, jump); the server clamps them (`MovementRules`), so a modified client cannot move faster.
- **Input:** the project's existing `InputSystem_Actions` asset (`Player` map: Move, Look, Jump,
  Sprint, Interact); keyboard, mouse and gamepad bindings come with it.

## Acceptance criteria
- [ ] AC1: `MovementRules` (EditMode tests): camera-relative direction for all 8 key directions and a
      rotated camera; sprint speed only while sprinting; input vectors longer than 1, NaN or infinite
      are clamped or rejected; turn rate limits the yaw change per tick.
- [ ] AC2: Two clients on the WSL server: each moves without visible input delay, sees the other move
      smoothly, and a client with 150 ms simulated latency still moves instantly on its own screen
      (manual, FishNet's latency simulator).
- [ ] AC3: The camera orbits with mouse and right stick, zooms with the wheel, and does not go through
      the kitchen stations or boxes (manual).
- [ ] AC4: Spec 003, 007 and 009 behavior still works (pickups, stations, selling); their tests pass.
- [ ] AC5: `bash scripts/verify.sh` passes with `UNITY_PATH` set.

## Test plan
- EditMode tests for `MovementRules`.
- Manual play on the WSL server with two clients, then with the latency simulator on one.

## Open questions (human decides; recommended default in brackets)
1. Mouse style. [**Action style**: cursor hidden and locked while playing, the mouse always turns the
   camera, Esc or Alt frees the cursor for the overlay. Alternative: **life-sim / MMO style**, cursor
   always visible and holding the right mouse button drags the camera.]
2. Jump. [Yes, small, mainly for feel; it can be removed later without touching anything else.]

## Human steps
- Approve, answer the open questions, play it and say if it feels right.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
