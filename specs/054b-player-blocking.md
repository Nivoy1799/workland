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

## Implementation notes
Builder (Opus), 2026-11-11, branch `spec/054b-player-blocking` from 075c101. Not committed (the coordinator asked for no commits).

**What is built.**
- `Net/Rules/PlayerSeparation` (pure): the spec's push (k 8 /s, max 2.5 m/s, contact radius 0.35 m) plus one addition, a
  no-pass-through floor: inside 0.45 m the own step towards the other is removed (the sideways part stays). Without it two sprinters
  head-on (closing 10 m/s against 2 x 2.5 m/s of push) walk through each other. Coincident players split by object id.
- `Net/Rules/CorrectionBlend` (pure, immutable): below 0.30 m the drawn body keeps its place and the offset shrinks to zero in
  0.12 s; at 0.30 m or more it snaps. The offset is capped at 0.30 m (a run of small corrections in one direction cannot build a
  growing lag; what goes over the cap is drawn at once and counted as visible).
- `PlayerController`: soft mode adds the separation in `PerformReplicate` (server: real positions; owner: the drawn roots). The blend
  rides on FishNet's public tick-smoother tracker API (`UniversalTickSmoother.TrySetGraphicalTrackerLocalProperties`), so no
  prefab or FishNet change: a replayed tick puts the tracker back on what was drawn for that tick (the replay does not move the drawn
  path), and the live tick after a reconcile blends. The camera follows the same graphic, so it blends too.
- Design 4: a client's copy of another player is drawn at most 5 cm into the own drawn body. The push-out moves at no more than
  3 m/s and keeps its side, because a hard clamp flipped sides when a drawn root crossed the own centre (15 remote teleports in the
  first 15-bot run; 0 after).
- `SessionPhysics.PlayerCollision` is a `SyncVar<byte>` (`PlayerCollisionMode` Off 0, Hard 1, Soft 2), default soft. Release builds
  always use soft and warn on the flag. `-player-collision soft|hard|off`; `on` is no longer accepted. `ProtocolSurface.Revision` 5.

**Decision for the reviewer: replays use the positions drawn at that tick's live run.** The owner records the other players' drawn
positions per live tick (128-tick ring, owner only) and its replays push against those, not against where they are drawn now. This
moves no bodies (only the numbers fed to the pure rule), so it is not the out-of-scope rewind to server positions. Without it, every
replay re-pushed against positions a few ticks newer: 4-bot smoke run, 50 shifts per minute before, 6.5 after.

**Scope deviation.** `Scripts/Dev/RemoteMotionProbe.cs` and `RemoteMotionStats.cs` gained the smoothing bins (the AC4 numbers cannot
be measured otherwise). Read only, like the 054 probes. Test re-pins outside the new files: `NetReview054Tests` (revision 5, the mode
byte, soft default), `NetTrafficTests` (the flag), `MenuBoardTests` ("revision 5"), `MoveDataSurfaceTests` (ViewTick). With the
netcode owner's go-ahead: `Scripts/Dev/TourRules.cs` (the bump walk) and `TourRulesTests`.

**Metrics (bots, dev builds).** Kept from 054: `owner_corr_*`, a reconcile more than 5 cm from the live prediction of its tick. New:
a *shift* is a tick whose reconcile and replay moved the own current position more than 5 cm (what the player would see without
smoothing). Its *visible* size is how far the drawn body moves in the next tick because of it (a snap: the whole jump). This is per
tick (33 ms); at 60 fps a blended step is half of it per frame. `owner_visible_corrections_per_min` counts ticks where the drawn body
is moved more than 5 cm.

**Option (a) (the netcode owner's decision after the first report; the floor and the replay positions approved).**
- Every input carries `MoveData.ViewTick`: the newest server tick the client has received, minus the remote display delay
  (`LagCompensation.DisplayTicks` = 2 x 2 = 4). The server can't read a client's RTT (FishNet keeps it client-side only), so the
  claim comes with the input. That is a wire change, so `ProtocolSurface.Revision` stays at 5 (unreleased, both changes).
  `MoveDataSurfaceTests` is re-pinned.
- The server keeps a 32-tick `PositionHistory` per player. `LagCompensation.ServerViewTick` clamps the claim: never ahead of the
  server, at most 9 ticks (300 ms) back. Its push uses the others' history at that tick. The floor uses the real positions.
- Deep contact: bodies that really overlap more than 10 cm (centres under 0.5 m) are pushed at the full 2.5 m/s along the real
  line. Without that, two players crossing at a right angle with 9/0 ticks of view delay stayed more than 10 cm into each other
  for 0.97 s. With it, every case in `LagCompensationTests` (walk and sprint into a stander, head-on, crossing, delays 0 to 20
  ticks) stays under 0.5 s. One exception: two players who both keep pushing dead centre stand pressed at the 0.45 m floor
  (15 cm of overlap) for as long as they push. They never pass. A separate test covers it.
- Extra round: deciding the deep push on the seen positions (with a real one at 0.47 m) measured worse (visible p95 21.6 cm,
  9.2/min), so it was reverted.
- Dev diagnostic in host.log every 30 s: `[Net] view delay ticks`.

**AC4 (15 bots + host, ramp 120, hold 300, `--kbps-budget 35,40`; `.verify/loadtest054b-{soft,a-soft,hard,off}`).**

| Mode | 054 corrections/min (p50 / p95) | Shifts/min (p50 / p95) | Visible after smoothing (p50 / p95) | Visible > 5 cm per min | Payload KB/s avg / worst s | Teleports |
|---|---|---|---|---|---|---|
| soft | 228 (17.7 / 48.8 cm) | 15.8 (7.9 / 28.7 cm) | 5.3 / 25.0 cm | 9.5 | 3.4 / 4.5 | 0 |
| soft + (a) | 146 (10.3 / 45.6 cm) | 13.0 (8.6 / 21.8 cm) | 4.3 / 14.9 cm | 6.3 | 3.4 / 4.6 | 0 |
| hard | 206 (17.6 / 64.8 cm) | 47.3 (7.7 / 61.6 cm) | 5.7 / 65.9 cm | 31.7 | 2.4 / 4.1 | 0 |
| off | 0 | 0 | 0 | 0 | 3.2 / 4.6 | 0 |

Wire 4.4 / 4.4 / 3.5 / 4.3 KB/s per client. BANDWIDTH PASS in all four. `remote_jitter_p95` 0.39 / 0.39 / 0.51 / 0.41.

**AC4 is not met: visible p95 is 14.9 cm with (a), against 5 cm (and over the 10 cm fallback).** The cause is the 300 ms clamp.
Under this load the clients' real view delay is 7 to 17 ticks (233 to 567 ms, median about 11). About 60 % of the inputs
(about 8,000 of 13,500 per 30 s) were clamped, so the server pushed against positions up to 270 ms newer than the client drew.
RTT is only 17 ms (p95 33). The rest is FishNet's server-side input queue: 16 processes on one PC, bots at about 30 fps.
The shift histogram peaks at 8 to 10 cm, one full step (2.5 m/s x 33 ms): the deep and floor zones decide differently on the
two sides. The 30 to 150 cm tail is bot stalls.
Follow-ups for the netcode owner (not built): raise `MaxViewTicks` to about 20 (the 32-tick history covers it), or shorten
FishNet's server input queue. Both need a decision on how far back a client may reach.

**MaxViewTicks 20 (the netcode owner, 2026-11-12), soft + (a), ramp 120, hold 300 (`.verify/loadtest054b-a20-{15,4}`).** No input
is clamped now. 15 bots: visible p50 3.8 / p95 18.6 cm, 3.5 visible > 5 cm per minute, 8.7 shifts per minute (p95 23.9 cm),
054 metric 111/min. 4 bots: visible p50 4.5 / p95 23.1 cm, 2.8 per minute, 5.8 shifts per minute (p95 25.9 cm), 054 metric 51/min.
Teleports 0, payload 3.3 and 2.2 KB/s. **Not accepted:** the 4-bot p95 is over 10 cm and the 15-bot p95 is worse than 14.9 cm.
The count of visible corrections fell (9.5, then 6.3, then 3.5 per minute), but the p95 of what is left did not.
Even with 4 bots the measured view delay is 8 to 15 ticks, not the expected about 6, so the rig's input queue is still there.
Not committed; stopped as instructed.

**AC5.** `scripts/visual-check.sh --tours cook,menu --out .verify/visual054b`: PASS, 0 Exception lines, no crashes. The sheets have
cook 988 tour and 332 observer cells, and menu 30 and 4. No observer cell shows two bodies overlapping. First run, before the bump step.
Then the cook tour gained a bump walk (`TourRules.BumpSteps`, allowed by the netcode owner): in third person the tour player walks
into the observer's standing player 5 cm off its centre, slides round, walks back past it and parks. Frames `bump-NN`;
`TourRulesTests.CookTour_WalksIntoTheObserver_AndFilmsItInThirdPerson`. Rerun with option (a): PASS, 0 Exception lines, no stuck
walks. Cook has 986 tour cells (40 of them bump) and 372 observer cells, menu 30 and 4. In bump-12 to bump-16 the tour player walks
up behind the observer, meets it shoulder to shoulder, and the observer is nudged aside as the walker slides past. The bodies touch
but do not sink into each other.

**Feel (from the rule; AC6 is the human's).** Walking into a standing player: you stop about 0.57 m centre to centre (3 cm of visible
overlap at most), slide round them if you are even slightly off centre, and they get nudged 0.5-0.7 m aside. Dead centre you push
them along at about 1.25 m/s. A sprinter shoves at about 2 m/s and cannot get closer than the 0.45 m floor. Moving contacts
correct mostly as smooth drift (blended), with an occasional snap after a hitch.

**Accepted by the netcode owner (2026-11-12):** AC4 target not met. Soft + lag-compensated push (MaxViewTicks 20) cuts visible jumps over 5 cm from 31.7 (hard) to 3.5 per minute (15 bots) and 2.8 (4 bots); the p95 of the remaining jumps stays at about 19-23 cm, largely from the test rig's server input queue (view delay 8-15 ticks even with 4 bots). Merged as the default because the human wants players to block (D48). AC6, the human's feel test, is the final judge. Compare with `-player-collision soft|hard|off` in dev builds. Follow-up idea if it feels bad: shorten FishNet's server input queue, then re-measure on two real PCs.

**Review fixes (APPROVE WITH NITS, 2026-11-12; after merging main: Revision 6 = 062a + 054b).**
- `PlayerSeparation.Separate` is the one pure push-then-floor rule; the owner, the server and the fallback all call it. The summed
  push is capped at `MaxPushSpeed` on both sides. That stops crowd shoves, and a forged ViewTick cannot add up a bigger push.
- A test runs that rule to show that ViewTick feeds only the soft push: the floor and the deep push use real positions.
- `PlaceTracker` uses the tracker's own base position plus the offset in the root's frame. `MaxOthers` is 15 (16 players, D36).
- New tests: a walker cannot squeeze between two standing players (three-player floor), the summed-push cap, and the dead-centre
  pressing case at a delay of 20 ticks.
- **Host view:** the display clamp (design 4) runs only on clients. On the host the other players are drawn where they really are,
  so at the floor (two players pressing dead centre) the host sees bodies up to about 15 cm into its own.
