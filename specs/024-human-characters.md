---
id: 024
title: Human characters (players and customers as low-poly people)
status: approved
owner: architect
builder: opus
---

# 024: Human characters

## Goal
Players and customers stop being capsules. Everyone is a low-poly person with idle and walk
animations; other players see you hold food or the knife in your hands (not above your head); in
first person you still see your own shadow and the held item in view (spec 018). Customers use the
same characters in other colours. This was step 4 of the human's order from 2026-11-07 ("humans").

## Depends on
- Specs 010 (movement), 012 (emotes), 014 (customers), 018 (first person, own body hidden),
  020/021 (held items).
- **Download (approved with this spec):** one CC0 character pack with a humanoid rig and idle/walk
  animations, from kenney.nl (preferred) or quaternius.com, at most 30 MB. Only used files are kept,
  with License.txt and an `ASSETS.md` row. If no CC0 pack with animations fits, use a CC0 pack without
  animations and a simple procedural bob/sway (noted in the implementation notes).

## Scope
- `game/Assets/ThirdParty/<Author>/<Pack>/` (new, used files only), `ASSETS.md` row.
- `game/Assets/_Project/Scripts/Player/` (change: `CharacterVisual` (new) drives the animator from
  the synced velocity and crouch; `FirstPersonBody` hides the new model instead of the capsule).
- `game/Assets/_Project/Scripts/Net/Pickup.cs` (change: the server hold point stays for rules; a
  client-side visual follows the character's right hand bone for others).
- `game/Assets/_Project/Scripts/Npc/Customer.cs` (change: character model with a tint).
- `game/Assets/_Project/Scripts/Art/` and `Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs`
  (change: character art keys, player and customer prefabs use them; CharacterController unchanged).
- `WhatsNew.json` entry, tests, this spec.

## Out of scope
- Character customisation, clothes, faces, lip sync; IK for picking up; ragdolls.
- Changing movement rules or the CharacterController size.

## Design
- Visual only: the networked capsule controller and all rules stay. The model is a child of the
  graphic, scaled to 1.8 m, animator parameters Speed and Crouch from data every client already has.
- Held items: rules and server position unchanged; for remote viewers the held item's visual is
  drawn at the holder's right hand (client side), so it looks carried.
- Emote bubble stays above the head.

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh`.
- [x] AC2: Art tests: player and customer character keys exist, no colliders or scripts in wrappers.
- [x] AC3: `CharacterVisual` rule test: speed → animation state (idle / walk / run), crouch flag.
- [x] AC4 (2026-11-08 night, partly: a third-person client sees itself and the other player as Mini Characters people; first person unchanged with the held pan in view; customers as people and a potato in the hand left to the AC5 play-test): Screenshots, two clients: B sees A as a person walking, holding a potato in the hand;
      customers as people at the counter.
- [ ] AC5 (manual, human): looks right, no floating items, first person unchanged.
- [x] AC6: `WhatsNew.json` entry `spec-024`.

## Open questions (answered 2026-11-07: human approved all defaults, incl. the download)
1. **Download** one CC0 character pack (≤ 30 MB) chosen by the orchestrator? **Default: yes.**
2. **Customers** use the same characters with different colours? **Default: yes.**

## Human steps
- Approve; look at it (AC5).

## Implementation notes
Built 2026-11-08 (builder, overnight). `verify.sh` PASS, Unity EditMode 517/517 (baseline 502). Not committed.

**Pack.** Kenney Mini Characters 2.0 (already copied by the orchestrator, 4.3 MB). Rig: 7 bones (`root`, `leg-left/right`,
`torso`, `arm-left/right`, `head`), no hand bones, so the import is **Generic** (not Humanoid). The six FBX share bone names
and positions. Only `.meta` import settings were touched, by `ArtCatalogBuilder.ConfigureCharacterImport` (idempotent):
`avatarSetup` = CreateFromThisModel (the default "no avatar" gives no Animator), Generic, no colliders. Clip settings stay at
the defaults (all 32 takes, not looping); `CharacterVisual` loops them itself. Materials import as URP Lit with
`Textures/colormap.png` (no remap needed). Native height 0.67 m; facing +Z (feet point +Z, `arm-right` at +X), yaw 0.

**Clips used** (all from `character-male-a.fbx`, played on every variant): `idle` (1.33 s), `walk` (0.67 s), `sprint`
(0.5 s), `crouch` (a static pose), `holding-right` (static pose, masked to the right arm). No procedural bob was needed.

**Art layer.** `ArtKey` + `CharacterMaleA, CharacterFemaleA` (players), `CharacterMaleB/C, CharacterFemaleB/C` (customers).
`ArtCatalogRules.PlayerCharacterKeys/CustomerCharacterKeys` (variant order), `CharacterVariant(id, count)` (wraps negatives),
`PlayerCharacterKey(ownerId)` (even = male-a, odd = female-a), `CustomerCharacterKey(objectId)`. New fit mode `Character`:
uniform scale to 1.8 m (bounds incl. hair, so characters with tall hair have slightly smaller bodies), pivot bottom centre,
Animator without controller (no root motion, AlwaysAnimate so the first-person shadow animates), and an empty `HandR` under
`arm-right` at the end of the arm (bone space (0.258, 0, 0.076), measured once on male-a's arm vertices; female-a has an extra
piece on her right arm out to 0.45 that would push a per-model anchor outwards). Wrappers have no colliders or scripts.

**CharacterVisual** (Player/, MonoBehaviour on the player and customer root). Variants are prebuilt children of a
`Character` container (player: under the smoothed `Graphic`; customer: under the root); the first is active until spawn,
then the owner client id (players) or object id (customers) picks one, the same on every client. Speed = horizontal delta of
the tracked transform (Graphic / customer root) per frame, smoothed (`CharacterAnimRules.SmoothSpeed`, teleports > 20 m/s
ignored); crouch = `PlayerController.IsCrouched` (already synced). `CharacterAnimRules.StateFor(speed, crouch, previous)`:
idle below 0.5 m/s (back to idle below 0.25), run from 6 m/s (back to walk below 5), crouch wins. Playback rate follows the
speed (walk 2.4 m/s, sprint 4.5 m/s at rate 1, clamped 0.6..1.8). **Deviation:** no AnimatorController asset with Speed/Crouch
parameters: a small PlayableGraph (mixer of the four base clips + an override layer with a runtime AvatarMask on the right
arm) is driven by the tested rule, so the tested rule is exactly what plays and no generated controller churns on rebuilds.
Holding: every carried item calls `MarkHolding()` each frame, the hold layer fades in over 0.15 s. Headless server
(graphics device Null): the Animators and the component are switched off.

**Crouch.** The capsule squash is gone (`PlayerController._body` is no longer wired, so its squash code is idle; the eyes still
drop to 1 m). The pack's `crouch` pose is a kneel that would sink 0.19 m into the floor, so the model is lifted by
`CharacterVisual.CrouchLift` (0.19) while crouched. **It does not make the person shorter** (the pose keeps the head at about
1.9-2.0 m against the 1.2 m capsule) and it does not walk (sneaking slides in the pose). Needs the human's eye (AC5); options
are a squash on top or `CrouchLift = 0`.

**Held items** (`Pickup.cs`, client only): server hold point, NetworkTransform and rules unchanged. While held, each client
finds the holder's `CharacterVisual` (`FindPlayer(ownerId)`, retried every 0.5 s for late joiners) and, during each camera
render (`beginCameraRendering`), moves the item's visual children (every child except TextMesh labels; the spec 003 cube
moves as a whole) to `HandR` + (0, -0.02, 0.06) in the holder's yaw frame, and puts them back in `endCameraRendering`. So
nothing outside rendering ever sees the moved pose, and `HeldItemView` (owner, first person) is untouched: the hand drawing
is skipped while `FirstPersonBody.IsFirstPerson` (new read-only property). Your own item in third person is in your hand too.
The item's name label stays at the server hold point above the head.

**Prefabs/scene.** Player: capsule `Body` and nose `Facing` removed (nothing else referenced them), two variants,
`FirstPersonBody._body` = all their renderers (shadows only in first person), the emote wobble turns the `Character`
container. CharacterController unchanged (2 m, r 0.4). Customer: capsule removed, four variants, `Customer._tinted` = their
renderers, tint = white lerped 40 % to the wish colour (`Customer.TintOf`, `TintStrength`). `StreetEntry`/`QueueHead` moved
from y 0.1 to y 0 so the feet are on the ground. Rebuild: FishNet "same assetPath hash of 0" errors as expected;
`DefaultPrefabObjects.asset` unchanged; re-saved `PlaceMarker/Steam/TicketCard/TicketRail/Worktop.mat` reverted. Changed
generated files: ArtCatalog.asset (+6), six `Art_Character*.prefab`, Player.prefab, Customer.prefab, Graybox.unity (fileID churn
plus the queue y). New `.meta` files for the pack.

**Tests.** `CharacterAnimRulesTests` (AC3: idle/walk/run, crouch flag, hysteresis, bad speeds, rate, smoothing, fade, loop),
`CharacterArtTests` (AC2: variants and keys, catalog has all six with an Animator, 1.8 m, centred, HandR under the right arm,
no colliders/scripts; player prefab without capsule and nose, controller unchanged, CharacterVisual wired, first person hides
every renderer; customer prefab four variants, tint list, no collider; tint is light). `WhatsNewTests`: spec 023 is no longer
the newest, new spec 024 test. `WhatsNew.json` entry `2026-11-08-spec-024`.

**Visual check (AC4/AC5), not verified (batch mode only).** Model facing (assumed +Z), size against the 1 m counters, the walk
and sprint cadence (rate numbers above), the hold pose (right arm forward) and where items sit in the hand (`Pickup.HandOffset`;
plates and the 0.55 m pan may look big in one hand), the knife's direction in the hand, female-a's right-arm extra next to a held
item, the crouch pose and lift, the customer tint strength, the label/bubble heights (customer 2.1 m, emote 3.2 m) above a 1.8 m
head, the first-person shadow.

## Blockers
