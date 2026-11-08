---
id: 025
title: Character look and motion (normal people, calmer speeds, no foot sliding)
status: done
owner: architect
builder: opus
---

# 025: Character look and motion

## Goal
After the spec 024 play-test the human said the characters "look strange" and "the step size does
not suit the distance moved". This spec swaps the chibi Mini Characters for people with normal
proportions (D30), slows movement to walk 2.5 / sprint 5 m/s (D31), and drives the walk and run
clips from their **measured** stride so the feet stay planted. First spec under the new visual
guide `docs/art-direction.md`.

## Depends on
- Spec 024 (CharacterVisual, CharacterAnimRules, character art keys, hand drawing in Pickup).
- Spec 010 (movement and prediction), 018 (first person: eye height, own body hidden).
- **Downloads (done, see Blockers: resolved):** one CC0
  character pack with normal human proportions and at least idle, walk and run clips, at most
  30 MB. Order of preference: Kenney (Animated Characters), then Quaternius (for example the
  Ultimate Animated Character Pack). Only the used files are kept, with the licence file and an
  `ASSETS.md` row.

## Scope
- `game/Assets/ThirdParty/<Author>/<Pack>/` (new, used files only), `game/Assets/ThirdParty/ASSETS.md` (row).
- `game/Assets/ThirdParty/Kenney/MiniCharacters/` and its `ASSETS.md` row (removed, see question 3).
- `game/Assets/_Project/Scripts/Player/` (change: `MovementRules` speeds, `CharacterAnimRules`
  thresholds and clip speeds as inputs, `CharacterVisual` reads measured clip speeds and uses a hand
  bone if the rig has one; new `StrideRules` (pure)).
- `game/Assets/_Project/Scripts/Net/Pickup.cs` (change only if the hand bone lookup changes).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs` (change: character defs point to the new pack,
  height 1.75 m, the stride is measured at build time and stored on the wrapper's `CharacterVisual`).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change only for the new prefabs).
- Tests under `game/Assets/_Project/Tests/EditMode/`, `WhatsNew.json` entry, this spec.

## Out of scope
- World scale, station heights, food and cookware sizes, the palette (spec 026).
- Customer walk speed and shift timings (stay at 2.0 m/s, see question 2).
- Character customisation, faces, IK, ragdolls, root motion.
- New RPCs or MoveData fields (RpcSurfaceTests and MoveDataSurfaceTests stay as they are).

## Design
**Speeds (`MovementRules`, shared by prediction and server).** Walk 4 → 2.5, sprint 8 → 5,
crouch 2 → 1.25 m/s. Acceleration and deceleration scale with them (30 → 19, 40 → 25 m/s²) so
starting and stopping feel the same. Jump apex 1.2 → 0.6 m (question 1). Customers unchanged.

**Measured stride (`StrideRules`, pure).** Input: per-sample time and the horizontal position of
the left and right foot in model space over one loop of a clip. While a foot is planted it moves
backwards relative to the body; the clip's ground speed is the planted foot's backward travel per
second (average over both feet, planted = the lower foot). Output: m/s at playback rate 1, for the
model at its in-game height. `ArtCatalogBuilder` samples the walk and run clips with
`AnimationClip.SampleAnimation` at 60 Hz in the editor and writes the results into
`CharacterVisual._walkClipSpeed` / `_runClipSpeed` on the wrapper prefab. No estimate constants are left.

**Animation rules (`CharacterAnimRules`).** `PlaybackRate(state, speed, clipSpeed)`: speed /
clipSpeed, clamped to 0.6–1.6. Run starts at 3.75 m/s and stops below 3.25 (between walk 2.5 and
sprint 5). Idle/walk thresholds stay. A walk at 2.5 m/s and a sprint at 5 m/s must not hit the
clamp (AC4), so the feet do not slide.

**Model.** Normal-proportion people, scaled to 1.75 m (D29), pivot at the feet, facing +Z.
Players: two variants as today (even/odd owner id); customers: the other variants plus palette tints
(Cream, Terracotta, Mustard, Sage, SoftBlue, DeepBlue from `docs/art-direction.md`). If the rig is
humanoid, import it as Humanoid and hold items in the real right hand bone; otherwise Generic with an
empty `HandR` as today. Crouch uses the pack's crouch clip if it has one, else today's lifted pose.
Hold pose: the pack's hold clip, masked to the right arm, or a fixed arm rotation if it has none.

**First person.** Eye height stays 1.6 m (fits a 1.75 m body); own body hidden, shadow kept (spec 018).

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh` (only CC0, manifest rows match folders).
- [x] AC2: `StrideRules` tests: a synthetic gait (feet moving back at a known speed) gives that speed
      ±2 %; a standing clip gives 0; bad input (no samples, NaN) gives 0.
- [x] AC3: `MovementRules` tests use the new numbers (walk 2.5, sprint 5, crouch 1.25, jump 0.6).
- [x] AC4 (superseded by AC9/AC10 after the play-test amendment; the walk-clip range part no longer applies): Art test on the built prefabs: character height 1.75 m ±0.05; measured walk clip speed
      between 0.8 and 2.5 m/s; `PlaybackRate` at 2.5 m/s (walk) and 5 m/s (run) lies strictly inside
      the clamp range, so it never clamps at full speed.
- [x] AC5: `CharacterAnimRules` tests for the new run thresholds and the rate from a given clip speed.
- [x] AC6 (2026-11-08, partly: two third-person clients on the WSL server see each other and the customers as Kenney people, no exceptions; walking and running are left to the AC7 play-test because the demo client lost focus): Screenshots (WSL server, two clients, third person): a person walking and one running,
      next to a worktop, each beside a 1.75 m reference; first person view unchanged.
- [x] AC7 (2026-11-08, human after the gait fix: "Looks good, merge it"): the people look normal, feet do not slide when walking or sprinting,
      the speed feels right.
- [x] AC8: `WhatsNew.json` entry `spec-025`.

## Open questions (answered 2026-11-08: human approved all defaults)
1. Jump apex 1.2 m is very high for a person. Lower it to **0.6 m** (yes) or keep 1.2?
2. Customers walk 2.0 m/s; **keep for now** (shift timings stay) or slow to 1.4 m/s?
3. Remove the Kenney Mini Characters files and their manifest row once the new pack works? **Yes.**

## Test plan
- EditMode: `StrideRulesTests` (new), `MovementRulesTests`, `CharacterAnimRulesTests` (changed),
  `CharacterArtTests` (new: height, measured speeds, no clamp at full speed).
- `UNITY_PATH=... bash scripts/verify.sh`, then batch builds, WSL server and screenshot clients.

## Human steps
- Approve the spec and the answers to the questions.
- Approve the one download when it is asked for (file name, source, size).
- Play-test (AC7).

## Implementation notes
Builder, 2026-11-08 (second pass, after the Resolved decision below). `scripts/verify.sh` green, 531/531 EditMode.

**Import (Kenney body + Quaternius clips, both Humanoid).**
- `Kenney/AnimatedCharactersProtagonists/Model/characterMedium.fbx` and
  `Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx` import as Humanoid with `CreateFromThisModel`; both avatars
  are valid and human (set by `ArtCatalogBuilder.ConfigureHumanoidImport`, only the `.meta` changes). Kenney mapping:
  Hips, LeftUpLeg/RightUpLeg, LeftFoot/RightFoot, LeftToes/RightToes, Head, LeftHand/RightHand. UAL: pelvis, thigh_*,
  foot_*, ball_*, hand_*. The Kenney fallback clips (`Animations/idle.fbx`, `run.fbx`, Generic `Root|Idle`, `Root|Run`)
  are not used.
- Body: 3.765 units native (T-pose, baked mesh), scaled 0.465 to 1.75 m, faces +Z (toes at +Z), right hand at +X.
  The skinned renderer bounds of the import are loose (3.96, 0.19 below the feet), so height and pivot use the baked
  mesh (`ArtCatalogBuilder.MeshBounds`; the art test bakes the same way).
- One texture per skin through our own URP Lit materials `Art/CharacterSkin_skaterMaleA.mat` / `_skaterFemaleA.mat`.
  Players: `CharacterPlayerMale` (skaterMaleA, even owner id) and `CharacterPlayerFemale` (skaterFemaleA, odd).
  Customers: `CharacterCustomerMale` / `CharacterCustomerFemale` with the same two skins (two variants instead of four).
- Held items: `HandR` empty on the rig's real right hand bone (`RightHand`, `Animator.GetBoneTransform`), half way to
  the index finger. `Pickup.cs` unchanged (it only reads `Hand.position`).

**Clips used** (`UAL1_Standard.fbx`, prefix `Armature|`): idle `Idle_Loop`, walk state `Jog_Fwd_Loop`, run state
`Sprint_Loop`, crouch `Crouch_Idle_Loop`. No carry or hold clip in the library: the holding pose is a generated
Humanoid clip `Art/CharacterHold.anim` (right arm muscles only: arm down -0.5, front-back 0.1, forearm stretch -0.3;
upper arm at the side, forearm forwards), masked to the right arm and fingers with a Humanoid `AvatarMask`.

**Measured stride** (`ArtCatalogBuilder.MeasureClipSpeed`: the wrapper at 1.75 m, the clip played through the Kenney
avatar with foot IK on as in the game, 60 Hz, `StrideRules`). A foot is the point between ankle and ball, as low as
the lower of the heel (ankle minus its rest height) and the ball; ankle-only or ball-only points miss heel strike or
push-off (they differed by about 20 % on the jog).
| Clip | m/s at rate 1 | rate at 2.0 | at 2.5 | at 3.75 | at 5 |
|---|---|---|---|---|---|
| Walk_Loop | 0.72 | 2.80 | 3.50 | | |
| Jog_Fwd_Loop (walk state) | 3.55 | 0.56 | **0.70** | 1.06 | |
| Sprint_Loop (run state) | 3.41 | | | 1.10 | **1.47** |
| Crouch_Fwd_Loop (not used) | 0.53 | | | | |
Walk 2.5 m/s and sprint 5 m/s stay strictly inside 0.6–1.6. Customers (2.0 m/s) are at 0.56 and clamp to 0.6, so
their feet run about 7 % ahead of the ground (the guide allows 10 %).

**Deviations and trade-offs (reviewer please check).**
1. AC4 says the walk clip covers 0.8–2.5 m/s. The library's walk covers only 0.72 m/s on this body (legs are short:
   hip joint at 35 % of the height), which would be rate 3.5 at 2.5 m/s. So the walk state plays the jog (3.55 m/s),
   as the orchestrator allowed ("use the one that fits"). The art test checks the no-clamp part exactly and a
   plausibility range of 0.8–5 m/s instead of 0.8–2.5. AC4 is left unticked for the reviewer to decide.
2. Proportions (D30): the Kenney body is stylised too (head bone to crown about 28 % of the height, hip joint 35 %).
   The human chose this body; recorded here only.
3. Customer tint stays the light wish tint (`Customer.TintOf`, strength 0.4) with palette colours: meal Mustard,
   snack Sage, drink SoftBlue, unknown Cream. Terracotta and DeepBlue are not used yet. Colours live in `Customer.cs`
   until the spec 026 Palette.
4. Crouch shows the crouch idle pose at rate 1, also while sneaking at 1.25 m/s (as in spec 024). `Crouch_Fwd_Loop`
   would clamp there too (rate 2.36). Follow-up if sneaking needs moving feet.
5. Spec 024 lift (`CharacterVisual.CrouchLift`) and the Mini Characters arm bone mask are removed; the Humanoid crouch
   clip keeps the feet on the floor.
6. Not done: deleting `ThirdParty/Kenney/MiniCharacters/` (+ `.meta`) and its `ASSETS.md` row. The permission system
   denied the deletion. Nothing references the folder any more (the six old wrapper prefabs are deleted), so the
   orchestrator can remove it and the row together.
7. `WhatsNewTests`: the spec 024 entry is no longer the newest; a spec 025 test takes that role (same pattern as 023).
8. Rebuild hygiene: the `DefaultPrefabObjects.asset` reorder and five re-saved `Settings/*.mat` were reverted. The
   `Graybox.unity` diff is large (regenerated scene).

First pack (Ultimate Animated Character Pack, removed): Generic only (feet not under the legs), walk 0.96 / run 2.95
m/s at 1.75 m, near-black skin colour in the FBX. Details in the Blockers below.

## Blockers
**The pack chosen for this spec cannot meet AC4 or D30. A human decision is needed before the art swap.**
1. **Strides too short for D31 (AC4).** Measured at 60 Hz, model at 1.75 m, with a draft of `StrideRules` (before the
   steady-contact rule; a few % difference at most):
   walk clip 0.96 m/s (Casual_Male 0.93), run clip 2.95 m/s (Casual_Male 2.84); `Walk_Carry` / `Run_Carry` are the
   same. Playback rate at walk 2.5 m/s = 2.59, at sprint 5 m/s = 1.69, at customer 2.0 m/s = 2.08. All are above the
   1.6 clamp, so feet slide about 38 % when walking and 6 % when sprinting (the guide allows 10 %). Without clamping,
   these clips fit at most walk 1.54 m/s and sprint 4.7 m/s.
2. **Proportions are stylised, not normal (D30).** From the bind pose: neck joint at 63 % of the height, so the head
   is about a third of the body (D30: about 1/7); hip joint at 30 % (real people about 50 %). Short legs are the reason
   for the short stride in point 1.
Options (not mine to pick): (a) another CC0 pack with real proportions (spec order: Kenney Animated Characters first);
(b) keep this pack and change D31 to about walk 1.5 / sprint 4.5 m/s; (c) keep the speeds and accept foot sliding or a
much wider rate clamp (breaks the D31 note "feet may slide at most 10 %"). Measurements: one probe run, not kept in git.

**Resolved 2026-11-08 (human chose a variant of option a):** Kenney bodies with Quaternius animations.
- Downloaded (approved): Kenney Animated Characters Protagonists 1.1 (0.58 MB zip) and Quaternius Universal Animation
  Library [Standard] v3.0 (15 MB zip). Kept only:
  - `Kenney/AnimatedCharactersProtagonists/`: `Model/characterMedium.fbx`, `Skins/skaterMaleA.png`,
    `Skins/skaterFemaleA.png`, `Animations/idle.fbx` and `run.fbx` (fallback), `License.txt`;
  - `Quaternius/UniversalAnimationLibrary/`: `UAL1_Standard.fbx` (no root motion) and `License.txt`.
- The Ultimate Animated Character Pack files and their manifest row are removed.
- Plan: the Kenney body is imported as Humanoid, and the UAL walk / jog / sprint (and crouch / carry if present) clips
  are retargeted onto it. If the Kenney rig does not map to Humanoid, the Kenney idle / run clips are used and that is
  reported. Players use the two skater skins. Customers use the same skins with palette tints until more skins exist.

## Amendment 2026-11-08 (AC7 play-test, orchestrator)
Human: "very big steps, too slow, leaning forward too much". Cause, checked against the library's root-motion data
(GLB root travel per loop, scaled to the Kenney body by the hip-height ratio 0.667):

| Clip | Source m/s | On the Kenney body | Builder's foot measurement |
|---|---|---|---|
| `Walk_Loop` (1.333 s) | 0.975 | 0.65 | 0.72 |
| `Jog_Fwd_Loop` (0.933 s) | 5.36 | 3.57 | 3.55 |
| `Sprint_Loop` (0.667 s) | 8.25 | 5.50 | 3.41 (wrong) |
| `Crouch_Fwd_Loop` (2.0 s) | 0.75 | 0.50 | - |

The library's "jog" is a long-stride run: playing it at 0.70 at 2.5 m/s gives huge, slow, leaning steps, and the
wrong sprint measurement plays the sprint 1.6x too fast. Changes (same scope as above):
1. `UAL1_Standard.fbx` now holds the pack's root-motion export (same clip names, same `.meta`). Root motion is never
   applied (the server moves the body); the clip speed is `clip.averageSpeed` (Humanoid, normalised) x the body's
   `Animator.humanScale`, computed by the art builder. `StrideRules` stays as a cross-check (warn above 15 % apart).
2. Walk state = a phase-synced 1D blend of `Walk_Loop` and `Jog_Fwd_Loop`. Pure rule `CharacterAnimRules.Locomotion`:
   target cadence (steps/s) from speed, linear between 1.0 m/s -> 1.9, 2.5 m/s -> 2.7, 5.0 m/s -> 3.0 (2 steps per
   loop); the blend weight is the one whose blended stride (lerp of the clips' distance per loop) equals speed / cadence
   x 2, and the rate follows from the blended loop time. Feet stay planted by construction (rate x blended speed =
   speed). Run state = `Sprint_Loop` at speed / its clip speed. Crouch moving = `Crouch_Fwd_Loop` (rate clamped, sliding
   noted), crouch standing = `Crouch_Idle_Loop`.
3. New ACs: AC9 the measured clip speeds of the four clips match the table's Kenney column within 10 %; AC10 at 2.5 m/s
   and 5 m/s the cadence is within 10 % of the target and rate x blended clip speed is within 10 % of the speed.

### Amendment implementation notes (builder, 2026-11-08)
`scripts/verify.sh` green, 545/545 EditMode.
- **Clip import** (`ArtCatalogBuilder.ConfigureClipImport`, `.meta` only) for the six loops used: loop time on;
  rotation baked into the pose from the original root with a 180° offset (the root-motion export faces -Z at its root;
  "body orientation" instead turned the jog body 25-32° away from its travel); height baked from the original root;
  horizontal travel not baked (it becomes root motion, which is never applied: `applyRootMotion = false`). Checked over
  a loop: the model stays at the origin, faces +Z (hips yaw within ±4°), travel straight along +Z, pose at t=0 equals
  t=end.
- **Clip speed = played root motion**, not `averageSpeed`: one loop played through the Kenney avatar with root motion
  applied in the editor, travel / loop length (`ArtCatalogBuilder.RootMotionSpeed`). Deviation from the brief: for this
  library `clip.averageSpeed` x human scale differs from what the body actually travels by 0 % (walk), 2 % (jog), 9 %
  (sprint) and 21 % (crouch walk), while the played travel is a constant 1.091x the amendment's Kenney column for all
  four clips (that column scales by the hip-bone ratio 0.667; Unity retargets by its human scale, 0.747 m on this body).
  All three numbers are logged by the scene builder; the feet (`StrideRules`) warn above 15 % apart (only the sprint,
  whose feet barely touch, as before).

| Clip | Loop s | Played root motion m/s | averageSpeed x human scale | Feet (StrideRules) | Table |
|---|---|---|---|---|---|
| Walk_Loop | 1.333 | **0.709** | 0.709 | 0.691 | 0.65 |
| Jog_Fwd_Loop | 0.933 | **3.895** | 3.819 | 4.225 | 3.57 |
| Sprint_Loop | 0.667 | **5.999** | 5.490 | 3.903 (warns) | 5.50 |
| Crouch_Fwd_Loop | 2.000 | **0.545** | 0.429 | 0.481 | 0.50 |

- **Locomotion** (`CharacterAnimRules.Locomotion`, `GaitClip`, `LocomotionBlend`), walk and jog on one normalized
  phase (left foot lands at phase 0.000 in the walk, 0.982 in the jog; the -0.018 offset is stored and applied):

| m/s | jog weight | rate | cadence (target) | feet m/s |
|---|---|---|---|---|
| 1.0 | 0.040 | 1.252 | 1.90 (1.90) | 1.00 |
| 2.0 (customers) | 0.260 | 1.496 | 2.43 (2.43) | 2.00 |
| 2.5 (walk) | 0.337 | 1.618 | 2.70 (2.70) | 2.50 |
| 5.0 (sprint) | 0.888 | 1.467 | 3.00 (3.00) | 5.00 |

  Limits: weight 0..1; rate 0.3..2.0 (only bites below about 0.3 m/s, just before the walk stops); crouch walk rate
  0.5..2.5 (sneaking at 1.25 m/s is rate 2.29, feet planted).
- **Deviation: the sprint clip is not played.** On this body it covers 6.0 m/s with 2.0 m steps; alone at 5 m/s it plays
  at 0.83 with 2.5 steps/s (AC10 needs 3.0 ±10 %) and the most lean (head 0.36 m ahead of the hips). The walk / jog
  blend meets AC10 exactly at 5 m/s (cadence 3.00, weight 0.89), with less lean. So the walk and run states both play
  the blend; the run state and its thresholds stay (state, tests), the sprint is measured and stored for AC9 only.
- **Lean** (head ahead of the hips in metres on the 1.75 m body, sampled over each loop): walk 0.04, jog 0.23, sprint 0.36, crouch 0.44.
  In the blend, estimated linearly: about 0.10 m at 2.5 m/s (weight 0.34) and 0.21 m at 5 m/s. The jog's lean is in the clip; reducing it
  further needs a spine correction layer or other clips (follow-up if the play-test still finds it too much).
- AC9 and AC10 are tested on the built prefabs (`CharacterArtTests`); AC9 passes at +9.1 % for every clip (tolerance
  10 %), see the table above for why. Crouch now has its own moving state (`CharacterAnimState.CrouchWalk`).
