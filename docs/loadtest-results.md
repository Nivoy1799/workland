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
| Outbound per client (bytes each bot received, FishNet payload) | **49.6 KB/s** avg, 49.9 worst bot avg, 53.0 worst second | under 50 KB/s: just met |
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

Next step for a later spec: turn on FishNet's network traffic statistics for one run to split the bytes by packet type, then
lower the send rate of remote players (for example 15 Hz with interpolation) or stop forwarding inputs to spectators.

**Not covered.** Steam transport (bots use Tugboat; Steam would need 16 accounts), real network latency and loss (loopback),
a host on a weaker PC, bots cooking (they only pick up, carry and drop).

**Raw data.** `.verify/loadtest16/` (host.jsonl, bot<n>.jsonl, logs) and `.verify/loadtest.json` (the summary).
