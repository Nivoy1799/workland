---
id: 018
title: First-person view (switchable to third person, aim to interact)
status: draft
owner: architect
builder: opus
---

# 018: First-person view

## Goal
You play from your own eyes. First person is the default view; V (or the right stick press)
switches to the third-person camera from spec 010 and back. In first person you interact with
what you look at: a small dot in the middle of the screen, a prompt next to it ("Board · E · 1-3"),
and E or the digits act on that station or food, not on whatever happens to be nearest. The food
you carry is drawn in the lower right of your view. This is step 2 of the human's order from
2026-11-07: 017 food you can see, **018 first person**, 019 hands-on cooking (hold the potato,
take the knife, cut with the mouse), 020 human characters. Spec 019 builds on the aim and the
held-item view from this spec.

The server stays the authority (rule 6). The client chooses what it aims at and sends the same
intents as today (station index, pickup object); the server keeps checking reach and rate and
decides the outcome.

## Depends on
- Spec 010 (Cinemachine third-person camera, input reader, predicted movement).
- Specs 003, 007, 013, 016 (pickup and station requests), 012 (ping aims through the camera centre),
  017 (food visuals, held food).

## Scope
- `game/Assets/_Project/Scripts/Player/`:
  - `ViewMode.cs` (new): `enum ViewMode : byte { FirstPerson, ThirdPerson }`.
  - `ViewRules.cs` (new, pure): toggle, pitch clamp, constants (eye height, FOV, ray length).
  - `PlayerCameraRig.cs` (change: binds both cameras, switches between them, feeds pan/tilt in
    first person and the orbit in third person; exposes `Mode` and `ModeChanged`).
  - `PlayerInputReader.cs` (change: `ConsumeToggleView()` for V / gamepad right stick press).
  - `MovementRules.cs` (change: `FacingYaw(...)`, see Design).
  - `FirstPersonBody.cs` (new, owner only): hides your own body and nose from your camera in
    first person (shadows only), shows them again in third person.
  - `HeldItemView.cs` (new, owner only): draws the held food's visual at a view anchor under the
    first-person camera; returns it to the hold point in third person or when dropped.
- `game/Assets/_Project/Scripts/Net/`:
  - `PlayerController.cs` (change: `MoveData.FirstPerson` bool; the server faces the camera yaw
    when it is set).
  - `PickupInteractor.cs` (change: in first person the target comes from the aim; in third person
    unchanged; exposes `Target` for the HUD).
  - `Rules/AimRules.cs` (new, pure): picks the target from ray hits, see Design.
  - `Rules/LaunchArgs.cs` (change: `-view first|third`, for screenshots and tests).
  - `NetBootstrap.cs` (change, only if needed to pass `-view` on to the camera rig).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: crosshair dot and target prompt in first
  person; the existing prompt in third person).
- `game/Assets/_Project/Scripts/Social/SocialInteractor.cs` (change only if the ping aim needs the
  shared ray helper; behaviour stays "camera centre").
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: first-person CinemachineCamera,
  `Eyes` anchor on the player prefab, near clip plane, view anchor for the held item).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry `spec-018`), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- Hands, arms, tools, knife, cutting gestures, any new cooking mechanic (spec 019).
- Human characters, animations, other players seeing where you look (head pitch) (spec 020).
- Moving the server hold point: others still see your food above your head as today.
- Head bob, footstep sway, FOV slider, sensitivity settings menu, key rebinding.
- Server-side line-of-sight or facing checks (Open question 4).
- Changes to /sim, stations, recipes or balancing.

## Design
### View mode (client only)
- `PlayerCameraRig` owns the mode. Start mode: `-view` launch arg if given, else the last mode the
  player used (PlayerPrefs, Open question 2), else first person.
- Toggle: V on keyboard, right stick press on gamepad, read once per frame through
  `PlayerInputReader.ConsumeToggleView()`. Ignored while a menu or banner is open, or while quick
  chat (T) or the emote wheel (G) is held (same guards as today's keys).
- Two Cinemachine cameras in the scene, one brain:
  - **ThirdPerson** (exists, spec 010): orbit, zoom, deoccluder. Unchanged.
  - **FirstPerson** (new): follows the player's `Eyes` anchor (1.6 m, 0.1 m forward) with no
    damping, rotation from `CinemachinePanTilt` (pan = yaw, tilt = pitch). FOV 70, pitch clamped to
    -80 .. 80 degrees, no deoccluder.
  - The active camera gets the higher priority. Blend 0.2 s ease in/out (Open question 3).
  - Switching keeps the look direction: going to first person sets pan = orbit yaw and tilt = 0;
    going back sets orbit yaw = pan.
- Main camera near clip 0.05 m so the held food and nearby counters do not clip.
- Mouse wheel zooms only in third person.

### Movement (predicted, server-checked)
- `MoveData` gets `bool FirstPerson`. It is a view hint for facing only; it does not change speed,
  reach or anything the server validates.
- `MovementRules.FacingYaw(bool firstPerson, float currentYaw, float cameraYaw, Vector3 direction, float delta)`:
  first person returns the sanitized camera yaw (body faces where you look, you strafe sideways
  and walk backwards); third person returns today's `TurnTowards` result. Move direction stays
  `WorldDirection(move, cameraYaw)` in both modes.
- The server already sanitizes the yaw; the new bool needs no clamp. Replays use the same
  `MoveData`, so prediction and reconcile stay consistent.

### Aim to interact (first person)
- Each frame the owner casts one ray from the camera centre, length `ViewRules.AimRange` (3 m),
  ignoring triggers and its own colliders (same pattern as `SocialInteractor.TryAimPoint`).
- `AimRules.Pick(hits, playerPosition)` (pure, works on plain data: distance along the ray, kind
  station / pickup / other, index or id, object position):
  - the closest hit wins; if the closest hit is "other" (wall, floor, counter top without a
    station), there is no target (no aiming through walls);
  - a station or a free pickup is a target only if `PickupRules.IsInReach(playerPosition,
    objectPosition)` is true, the same rule the server checks, so the client does not send requests
    the server will refuse. (The spec 003 manual check of server refusal stays possible in third
    person, which keeps the 4 m search.)
- `PickupInteractor` in first person:
  - target is a station: E and the digits go to that station exactly as they go to the nearest
    station today (`TryHandleStationKeys(station)`);
  - target is a free pickup and the hands are empty: E picks it up;
  - no target and something in hand: E drops it (today's behaviour);
  - no target, empty hands: E shows the note "nothing to use here".
- Third person keeps today's nearest-station / nearest-pickup logic unchanged.
- `PickupInteractor.Target` (station or pickup or none) replaces `NearestStation` for the HUD;
  `NearestStation` stays as an alias so other readers keep working.
- Requests are the existing ServerRpcs with the same arguments. No new RPC, no new argument;
  `RpcSurfaceTests` must stay green.

### What you see
- **Crosshair:** a 4 px dot in the screen centre, first person only, hidden while a menu is open.
  It turns a brighter colour while it is on a valid target.
- **Prompt:** under the dot, the station's prompt and keys hint as today (`StationRules` prompts,
  `KeysHint`), or "E · pick up <name>" for food, or "E · drop" when holding with no target.
- **Own body:** `FirstPersonBody` sets the local player's body and nose renderers to shadows only
  in first person, so you see your shadow but not the inside of the capsule. Others always see you.
- **Held food:** `HeldItemView` moves only the visual child (the `FoodVisual` / graphic) of the food
  you hold to a `ViewHold` anchor under the first-person camera (about 0.35 m right, 0.3 m down,
  0.55 m forward). The networked object, its collider and the server's hold point do not move.
  Back to the hold point in third person, on drop, or when the food leaves your hands.
- Emote bubble and labels above your own head are hidden in first person (you would look through
  them). Others still see them.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets).
- [ ] AC2: `ViewRules` tests: toggle flips the mode; pitch clamps to -80 .. 80; the start mode
      prefers the launch arg, then the saved mode, then first person.
- [ ] AC3: `MovementRules.FacingYaw` tests: first person returns the camera yaw (also when moving
      backwards or sideways, and when not moving); third person matches `TurnTowards`; a NaN or
      huge camera yaw is sanitized.
- [ ] AC4: `AimRules.Pick` tests: closest hit wins; a wall in front of a station gives no target; a
      station or pickup outside 2.5 m of the player gives no target; a pickup behind a station gives
      the station; no hits gives no target.
- [ ] AC5: `LaunchArgs` tests: `-view first` and `-view third` parse; an unknown value is an error
      like the other args; no `-view` leaves it unset.
- [ ] AC6: Reflection test: no ServerRpc gained a parameter (`RpcSurfaceTests` green) and `MoveData`
      has exactly one new field, a bool named `FirstPerson`.
- [ ] AC7: Two clients on the dedicated server, screenshots like specs 012 to 017: client A in first
      person looking at the board shows the dot and the board prompt; client B in third person sees
      client A's body facing where A looks; A holding a potato sees it in the lower right; after V,
      A's view is third person with the same yaw.
- [ ] AC8 (manual, human): play 5 minutes in first person: walk, strafe, jump, cut a potato, fry
      it, plate it, serve it, all by looking at the stations. Switch with V a few times. Nothing
      clips badly, the mouse feels right.
- [ ] AC9: `WhatsNew.json` entry `spec-018` with test steps.

## Test plan
- EditMode: `ViewRulesTests`, `FacingYawTests` (or added to `MovementRulesTests`), `AimRulesTests`,
  `LaunchArgsTests` (extended), `MoveDataSurfaceTests` (reflection, no FishNet reference, like
  `RpcSurfaceTests`).
- Screenshot run with the Linux server in WSL and two Windows clients (`-view first` and
  `-view third`, `-screenshot`), then the human play-test (AC8).
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions (defaults in bold; the human answers before approval)
1. **Default view:** first person at the start? **Default: yes.**
2. **Remember the view:** keep the last chosen view between game starts (PlayerPrefs, local only)?
   **Default: yes.**
3. **Switching feel:** a short 0.2 s camera blend, or an instant cut? **Default: 0.2 s blend.**
4. **Server facing check:** should the server also refuse station or pickup requests for things
   behind the player (more than 90 degrees from its yaw)? It would stop a modified client from
   using things it is not looking at, but adds a rule that can feel unfair with lag.
   **Default: no, keep reach only for now; revisit with spec 019.**
5. **Mouse sensitivity:** same as the third-person orbit (0.12 degrees per count)?
   **Default: yes; a settings slider is a later spec.**

## Human steps
- Answer the open questions and approve this spec.
- Play-test AC8.

## Implementation notes
(Builder fills this in.)

## Blockers
