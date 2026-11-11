# Load test results (spec 004, run in spec 045)

## 2026-11-11: 16 players in one restaurant on a host (D36)

**Setup.** `bash tools/bots/run-loadtest.sh --bots 16 --ramp 120 --hold 300` on the development builds of spec 045.
One windowed host (`-host`, Direct3D 11, 1280x720: the server plus the host's own player, as players run it, D33) and
16 headless bots (`-batchmode -nographics -connect 127.0.0.1 -bot -seed n`) over Tugboat on loopback, all on one PC. The
Graybox restaurant with its NPC guests running. Each bot walks between random item spots, every 5-15 s tries to pick up the
nearest free item (walk there, the normal pick-up request), carries it 10-30 s and drops it. Bots start evenly over 2 minutes,
then 5 minutes hold; the numbers are the hold only (300 one-second samples of the host, 16 x 300 of the bots).

**Hardware.** Intel Core i7-13700K (16 cores, 24 threads), 64 GB RAM, RTX 5080, Windows 11 Pro. The host and the 16 bots
share this machine, so CPU numbers are pessimistic for the host.

**Numbers (hold phase, 17 connected clients the whole time, 80 networked objects).**

| What | Value | Spec 004 target |
|---|---|---|
| Server tick work (OnPreTick to OnPostTick), p95 of the per-second p95 | **1.01 ms** (avg 0.63, max 3.4) | under 20 ms at 30 Hz: met |
| Ticks per second | 30.0 | 30 |
| Outbound per client (bytes each bot received, FishNet payload) | **49.6 KB/s** avg, 49.9 worst bot avg, 53.0 worst second | under 50 KB/s: at the limit, no headroom (see below) |
| Host upload, all clients (16 x the above) | about 0.8 MB/s (6.4 Mbit/s) | - |
| Inbound at the host, all clients | 30.4 KB/s | - |
| Round trip (bots, loopback) | 22 ms avg, 33 ms p95 | - |
| Host frame time | 2.06 ms avg, 2.73 ms p95, 10.2 ms max | - |
| Host CPU | 376 % of one core avg, 591 % max | - |
| Host memory | 473 MB working set (Unity reserved 291 MB, managed heap 18 MB) | - |
| Bot memory | 280 MB working set each | - |
| Processes alive at the end | 17 of 17 | - |

**Chart (30 s buckets from the start of the ramp; one # = 2 KB/s per client).**

```
  t(s) clients  tick p95  KB/s/client    cpu %  out per client
     0       5     20.66          8.8      270  ####
    30       9      0.73         19.8      292  #########
    60      13      1.00         32.3      327  ################
    90      17      1.23         44.7      378  ######################
   120      17      0.93         49.9      385  ########################
   150      17      1.09         50.0      372  ########################
   180      17      1.19         49.9      393  ########################
   210      17      0.91         49.6      372  ########################
   240      17      0.98         49.1      383  ########################
   270      17      1.14         49.7      358  ########################
   300      17      0.91         49.6      381  ########################
   330      17      1.03         49.5      364  ########################
   360      17      0.94         49.5      359  ########################
   390      17      1.08         49.2      383  ########################
   420      17      1.14         48.1      370  ########################
```

The one tick spike (20.7 ms) is in the first seconds, while the first bots joined (scene load and spawn on the host).

**Verdict on bandwidth.** At the limit, no headroom: 49.6 KB/s payload only. Wire bytes, with UDP/IP and LiteNetLib headers, are higher; the worst second is 53.0 KB/s; loopback; bots do not cook.

**Reading.**
- The tick is cheap: 16 players cost about 1 ms of a 33 ms tick. CPU is not what limits a 16-player host.
- Bandwidth is the limit. Outbound per client grows by about 3 KB/s for every other player (8.8 KB/s with 4 bots, 49.6 with
  16), so the host's upload grows with the square of the player count: 0.8 MB/s at 16. That is fine on fibre, tight on many
  home uplinks (a 10 Mbit/s upload is two thirds full), and Steam relay adds its own overhead.
- The host runs at about 500 frames per second (FishNet sets the frame rate to its maximum of 500 when the server starts), which
  is most of its CPU. A frame cap (spec 048 settings) would give that back.

**Top 3 suspected causes of the outbound volume (not measured per component yet).**
1. Player prediction: every player's inputs and the server's reconcile states go to every other player at 30 Hz
   (PlayerController [Replicate]/[Reconcile] with FishNet's spectator forwarding), so each player costs every observer about
   100 bytes a tick.
2. Carried items: the server moves a held item every tick (Pickup follows the hold point) and its NetworkTransform sends
   it to all; the bots carry something about half the time.
3. NPC guests and station state: walking guests' transforms and the station bytes (heat rings, flags) change all the time.

**Next steps (required, not optional; for a later spec, before Early Access):**
1. Per-packet traffic stats: turn on FishNet's network traffic statistics for one run and split the bytes by packet type
   (and measure wire bytes with headers, not only payload).
2. A 15 Hz send rate for remote players (with interpolation), or stop forwarding inputs to spectators.

**Not covered.** Steam transport (bots use Tugboat; Steam would need 16 accounts), real network latency and loss (loopback),
a host on a weaker PC, bots cooking (they only pick up, carry and drop).

**Raw data.** `.verify/loadtest16/` (host.jsonl, bot<n>.jsonl, logs) and `.verify/loadtest.json` (the summary).

## 2026-11-11: network headroom (spec 054), traffic per packet type, before and after

**Setup.** `bash tools/bots/run-loadtest.sh --bots 15 --ramp 120 --hold 300 --net-stats --kbps-budget 35,40`: one windowed host and
15 headless bots (16 players, D36), Tugboat on loopback, the same PC as above. `--net-stats` turns on FishNet's per-packet
statistics on the host (Dev/NetTrafficProbe); bytes per second **per connected client** (16), averaged over the 300 s hold. Bots
walk, pick up, carry and drop, as in spec 045 (they move about a third of the time and do not cook). "Before" is part A (the
measurement only, spec 045's netcode); "15 Hz" is part B as built; "30 Hz" is part B with `-remote-hz 30` (other players every tick).

**Bytes per second per client, by packet type** (share of all packet types in brackets).

| Packet type | Before | After, 15 Hz | After, 30 Hz |
|---|---|---|---|
| Replicate (other players' inputs, forwarded) | 23 678 (56.0 %) | 0 | 0 |
| Reconcile (states; before: of every player, now: of the own player only) | 17 341 (41.0 %) | 1 140 (44.8 %) | 1 140 (35.3 %) |
| ObserversRpc (NetworkTransform updates go here: players, customers, items) | 533 (1.3 %) | 804 (31.6 %) | 1 485 (46.0 %) |
| Unattributed (socket minus all types: packet and SyncType fan-out headers) | 381 (0.9 %) | 273 (10.7 %) | 275 (8.5 %) |
| StateUpdate | 300 (0.7 %) | 300 (11.8 %) | 300 (9.3 %) |
| SyncType, BulkSpawnOrDespawn, TimingUpdate, PingPong, TargetRpc | 30 | 30 | 30 |
| **Socket total on the host** | **42 264** | **2 546** | **3 230** |

**Top objects (bytes per second per client).** Player 41 020, 1 836, 2 515; Customer 192, 102, 103; the carried items (Plate 66,
Pot 50, Bowl 47, Wipe cloth 44, Pan 37, Knife 35, Spatula 35, Cutting board 33: about 350 together) 1 to 3 each after part B.

**Payload and wire per client (what each bot received).**

| | Before | After, 15 Hz | After, 30 Hz |
|---|---|---|---|
| FishNet payload, average | 43.9 KB/s | **2.5 KB/s** | 3.2 KB/s |
| FishNet payload, worst second (any bot) | 47.7 KB/s | **4.0 KB/s** | 4.6 KB/s |
| Wire (LiteNetLib bytes + 28 B IPv4/UDP per datagram), average | 45.7 KB/s | 3.6 KB/s | 4.3 KB/s |
| Wire, worst second | 49.6 KB/s | 5.0 KB/s | 5.7 KB/s |
| Host upload, all clients (wire) | 705 KB/s (5.6 Mbit/s) | 58 KB/s (0.5 Mbit/s) | 69 KB/s |
| Budget 35 / 40 KB/s (`--kbps-budget 35,40`) | FAIL | PASS | PASS |

**Reading.** The measured ranking agrees with spec 045's first guess but is far steeper: forwarding every player's inputs and
states to everyone (B1) was 97 % of the traffic; carried items (B2) were under 1 % and customers (B3) under 0.5 %. The spec's
assumption that NetworkTransform bytes would land in "Unattributed" was wrong: FishNet sends them as ObserversRpc, so they are
counted there (times the observers). With B1 a bot receives its own reconcile (1.1 KB/s, 30 Hz), the others' NetworkTransform
updates (about 0.7 KB/s at 15 Hz while a third of them walk; NetworkTransform sends only changed axes, 2 bytes each) and the
fixed StateUpdate/tick traffic. Upper bound with all 15 others walking all the time: about 2 KB/s more at 15 Hz. Cooking bots,
more NPCs and the Steam relay now have well over 10 times headroom under the 35 KB/s budget.

**Feel guard (B4, bots, 30 fps, loopback).** See spec 054's implementation notes for the discussion.

| | Before | After, 15 Hz | After, 30 Hz |
|---|---|---|---|
| remote_jitter_p95 (p95 of the per-second p95, guard 0.15) | 0.733 | 0.492 | 0.499 |
| remote_jitter, median second | 0.463 | 0.369 | 0.394 |
| remote_teleports (jumps over 0.5 m, outside spawns) | 3 | 0 | 0 |
| owner_corrections per bot-minute | 43.5 | 208.3 | 164.5 |

Two experiments on the corrections, both removed again (15 Hz): a client-side capsule placed where the server most likely has
each other player (newest update extrapolated): 204.1 per minute; no player-player collision on the client: 1126.2 per minute.
The corrections come from bumping into other players, which forwarding used to re-simulate tick by tick (spec 054 notes).
Raw data: `.verify/loadtest054-after15b/`, `.verify/loadtest054-exp-nobody/`.

**Raw data.** `.verify/loadtest054-before/`, `.verify/loadtest054-after15/`, `.verify/loadtest054-after30/` (host.jsonl with
the pid_/obj_ fields, bot<n>.jsonl, summary.json).
