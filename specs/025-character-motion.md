---
id: 025
title: Character look and motion (normal people, calmer speeds, no foot sliding)
status: approved
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
- **Download (asked separately before it happens, with file name, source and size):** one CC0
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
- [ ] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh` (only CC0, manifest rows match folders).
- [ ] AC2: `StrideRules` tests: a synthetic gait (feet moving back at a known speed) gives that speed
      ±2 %; a standing clip gives 0; bad input (no samples, NaN) gives 0.
- [ ] AC3: `MovementRules` tests use the new numbers (walk 2.5, sprint 5, crouch 1.25, jump 0.6).
- [ ] AC4: Art test on the built prefabs: character height 1.75 m ±0.05; measured walk clip speed
      between 0.8 and 2.5 m/s; `PlaybackRate` at 2.5 m/s (walk) and 5 m/s (run) lies strictly inside
      the clamp range, so it never clamps at full speed.
- [ ] AC5: `CharacterAnimRules` tests for the new run thresholds and the rate from a given clip speed.
- [ ] AC6: Screenshots (WSL server, two clients, third person): a person walking and one running,
      next to a worktop, each beside a 1.75 m reference; first person view unchanged.
- [ ] AC7 (manual, human): the people look normal, feet do not slide when walking or sprinting,
      the speed feels right.
- [ ] AC8: `WhatsNew.json` entry `spec-025`.

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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
