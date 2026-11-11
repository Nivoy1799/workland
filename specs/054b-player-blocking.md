---
id: 054b
title: Players block each other without rubber-banding (soft separation and smoothed corrections)
status: approved
owner: orchestrator (netcode)
builder: opus
---

> Approved by the human's instruction on 2026-11-11 (D48): "please not let player pass through each other". The
> approach below is the netcode owner's design, which the instruction leaves open.

# 054b: Players block each other without rubber-banding

## Goal
Spec 054 cut traffic from about 44 to 3 KB/s per player by no longer forwarding other players' states. Since then the
own player is predicted against where the others are *drawn* (about 133 ms in the past), while the server collides
with where they really are. With hard player-player collision, the 16-player test showed 224 owner corrections per
minute (p50 16 cm, p95 64 cm, max 130 cm), which is visible rubber-banding. Players must still block each other
(D48), so collisions have to stop producing those corrections.

## Design
1. **Soft separation instead of rigid bodies.** Player capsules no longer collide rigidly with each other; the world,
   stations and NPCs stay rigid. When two players overlap, each is pushed out along the line between them:
   `push = clamp(overlap × k, 0, maxPush)` per tick (defaults k = 8 /s, maxPush 2.5 m/s, contact radius 0.35 m each).
   It is a pure rule in `Scripts/Net/Rules` (for example `PlayerSeparation`), with unit tests. It must still feel like
   blocking: you cannot walk through someone, you slide around them.
2. **The same rule on both sides.** The server applies it with the real positions. The owner's prediction applies it
   with the interpolated positions it draws. The difference between the two is the small error that is left.
3. **Smoothed reconciliation for small errors.** If a reconciled position differs from the prediction by less than a
   threshold (default 0.30 m), the visible model blends to the corrected position over about 0.12 s instead of
   snapping. The simulation state still snaps, so the server stays the authority. Larger errors still snap, so a real
   teleport is never hidden.
4. **Remote players' contact look.** When two players touch, the remote player's drawn position is clamped so it never
   visibly overlaps the own capsule by more than about 5 cm. This is display only.
5. **Flags.** `-player-collision soft|hard|off`, default **soft**, for A/B tests in dev builds. Release builds always
   use soft. `SessionPhysics.PlayersCollide` becomes a mode byte. Bump `ProtocolSurface.Revision` and pin it.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; the RPC surface changes only as documented (the session mode byte).
- [ ] AC2: Unit tests for `PlayerSeparation`:
  - two overlapping players separate within 0.3 s;
  - a player walking into a standing one cannot pass through, but slides around them;
  - the push is symmetric and deterministic.
- [ ] AC3: Unit tests for the smoothing rule:
  - an error below the threshold blends, an error above it snaps;
  - the simulation state always equals the server's.
- [ ] AC4: Load test with 15 bots plus the host, using the feel guard from 054 (`tools/bots/run-loadtest.sh`):
  - with `soft`, the owner corrections that are *visible* after smoothing are p95 < 5 cm;
  - teleports 0;
  - bandwidth still within the 35/40 KB/s budget.

  Report soft against hard and off.
- [ ] AC5: Visual check (cook and menu): two players in the kitchen aisle bump and slide; the observer sheet shows no
  overlap.
- [ ] AC6 (human): in a dev build, walk into a friend. It must feel like blocking, without jumping back.

## Out of scope
Rewinding other players during replays, and forwarding state again.

## Folders
`game/Assets/_Project/Scripts/Net`, `Scripts/Player`, `Tests/EditMode`, `tools/bots`, and this spec.
