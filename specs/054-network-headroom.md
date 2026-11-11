---
id: 054
title: Network headroom (traffic per packet type, 15 Hz remote players, 35 KB/s per client)
status: approved
owner: architect
builder: opus         # netcode (CLAUDE.md: netcode stays on Opus)
---

> Approved by the human on 2026-11-11 (inbox questions 13 and 21) with the recommended defaults of its Open questions.

# 054: Network headroom

## Goal
With 16 players (D36) the host sends each client about 48 KB/s of FishNet payload (perf-check 2-hour soak: 47.9 avg,
51.9 worst second; spec 045 load test with 17 players: 49.6 / 53.0), against a 50 KB/s target. Wire bytes are higher, and
bots do not cook yet. This spec (1) measures what costs the most, per packet type and per object, and in wire bytes;
(2) cuts the biggest sources without making the game feel worse; (3) sets a budget with headroom that the scripts enforce.

## Scope
Two parts, two commits, in this order. Only these files.

**Part A: measure (no gameplay change)**
- `game/Assets/_Project/Scripts/Dev/NetTrafficProbe.cs` (new, dev builds only: the per-packet stats, see Design A1)
- `game/Assets/_Project/Scripts/Dev/WireBytesProbe.cs` (new, dev builds only: LiteNetLib datagram counters, A2)
- `game/Assets/_Project/Scripts/Dev/TrafficTable.cs` (new, pure: aggregation and field names)
- `game/Assets/_Project/Scripts/Dev/LoadTestRunner.cs` (change: new metric fields, see A3)
- `game/Assets/_Project/Scripts/Net/Rules/LaunchArgs.cs` (change: `-net-stats`, `-remote-hz`)
- `tools/bots/run-loadtest.sh` (change: summary fields, `--net-stats`, `--kbps-budget`)
- `scripts/perf-check.sh` (change: bandwidth verdict and column)
- `docs/performance.md` (change: budget rows, new column), `docs/loadtest-results.md` (change: the per-type table)
- `docs/workflow.md` (change: "Perf check" names the bandwidth budget)
- `game/Assets/_Project/Tests/EditMode/NetTrafficTests.cs` (new)

**Part B: cut**
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: player prefab, `AddServerAuthoritativeTransform`)
- `game/Assets/_Project/Scripts/Net/PlayerController.cs` (change: spectator path, see B1)
- `game/Assets/_Project/Scripts/Net/Pickup.cs` (change: held items, B2), plus the client-side readers of a held item's
  root that B2's Grep finds (inside `game/Assets/_Project/Scripts` only; list them in the notes)
- `game/Assets/_Project/Scripts/Net/Rules/NetRateRules.cs` (new, pure: every rate and interval as a constant)
- `game/Assets/_Project/Scripts/Dev/RemoteMotionProbe.cs` (new, dev: drawn motion of remote players, B4)
- `game/Assets/_Project/Scripts/Dev/RemoteMotionStats.cs` (new, pure)
- `game/Assets/_Project/Scripts/Player/CharacterVisual.cs` (change only if B1 needs it: speed from drawn motion)
- `game/Assets/_Project/Tests/EditMode/NetHeadroomTests.cs` (new)
- Regenerated assets the scene builder writes (Graybox scene, prefabs). Revert the DefaultPrefabObjects re-save as usual.
- This spec (`## Implementation notes`)

## Out of scope
- Editing FishNet, Tugboat, LiteNetLib or FishySteamworks sources (hard rule 9). Reflection into their members is allowed
  in dev-only code, behind a test that fails loudly when a member is renamed.
- The Steam transport's own overhead and relay (bots use Tugboat; O10 stays OPEN).
- Inbound traffic (30 KB/s at the host in total, not a problem), MoveData quantization (inbound only once B1 is in).
- New bot behaviour (cooking bots). Interest management changes (the restaurant fits inside the 40 m condition anyway).
- Changing the 30 Hz tick, the owner's prediction, reconcile contents, or any server rule.

## Design

### A1. Per-packet-type stats without editing FishNet
FishNet already counts outbound bytes per `PacketId` (`NetworkTrafficStatistics`), including per-object detail, but:
- its enable mode is a private serialized field (`StatisticsManager._networkTraffic._enableMode`), and every manager
  caches the statistics object at init (`TryGetNetworkTrafficStatistics` in TimeManager, TransportManager, ServerManager,
  NetworkBehaviour ...), so it must be on before `NetworkManager.Awake`;
- `IsEnabled()` tests `#if !DEVELOPMENT` (not `DEVELOPMENT_BUILD`): in a development player the mode must be `Release` (2)
  unless that symbol is defined. Verify while building.
- the per-tick data (`BidirectionalNetworkTraffic.OutboundTraffic`, `NetworkTraffic._packetGroups`,
  `PacketGroup._packets`) is internal or private; `PacketGroup.PacketId`/`Bytes` and `Packet` are public.
- `NetworkTransform` makes no statistics call (read 2026-11-11), so its bytes are only in the socket total.

Plan (all in `NetTrafficProbe`, `#if DEVELOPMENT_BUILD || UNITY_EDITOR`):
1. `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` (Graybox is the only build scene; objects are loaded, no Awake yet):
   only when `-net-stats` is on the command line, find the scene's `StatisticsManager` (or add one to the NetworkManager
   object) and set `_enableMode` by reflection. Without the flag nothing changes, so perf-check's GC numbers stay
   comparable. Collection allocates; a stats run is never a perf baseline (perf-check marks it, A4).
2. Subscribe to the public `OnNetworkTraffic` event; each tick read the server's outbound groups by reflection and add
   them into `TrafficTable` (copy numbers only, never keep FishNet objects: they are reset after the event).
3. Buckets: one per `PacketId` name (`Replicate`, `Reconcile`, `SyncType`, `ObserversRpc`, `TargetRpc`, `StateUpdate`,
   `TimingUpdate`, ...), `Unattributed = socket bytes - sum of groups` (clamped at 0; mostly NetworkTransform), and the
   top 8 objects by bytes, keyed by the root object's name without "(Clone)" (Player, Food, Customer, Pan, ...).

```csharp
public sealed class TrafficTable {                       // pure, no UnityEngine types
    public void AddGroup(string packetId, ulong bytes);
    public void AddObject(string objectName, ulong bytes);
    public void AddSocket(ulong bytes);
    public IReadOnlyList<KeyValuePair<string, double>> Fields(int clients, int topObjects); // per second, per client
    public void Clear();
}
```
Field names (flat, so `LoadStats.Line` and the perl summary need no JSON parser): `pid_<PacketId>_bps`,
`pid_Unattributed_bps`, `obj_<Name>_bps`, all bytes per second divided by the connected client count.

### A2. Wire bytes
`WireBytesProbe` (dev only): reflect Tugboat's `ClientSocket`/`ServerSocket` internal `NetManager` field (`CommonSocket.NetManager`),
set its public `EnableStatistics = true` once the socket is started, read the public `Statistics.BytesReceived/PacketsReceived`
(bot) and `BytesSent/PacketsSent` (host) every second. Wire bytes = LiteNetLib bytes + 28 x datagrams (IPv4 20 + UDP 8);
the formula is a pure function in `TrafficTable` (`WireBytes(bytes, datagrams)`). On Steam transport the probe reports nothing.

### A3. Metric fields and scripts
- Bot line gains `wire_in_bytes`, `datagrams_in`. Host line gains `wire_out_bytes`, `datagrams_out` and, with `-net-stats`,
  the `pid_*`/`obj_*` fields.
- `run-loadtest.sh`: summary adds `wire_bytes_per_client_avg`, `wire_bytes_per_client_max_second`, `host_wire_out_bytes_per_s`,
  and per bucket `pid_<x>_bps_avg` plus its share in percent; `--net-stats` passes `-net-stats` to the host;
  `--kbps-budget AVG,WORST` (KB = 1000 bytes, matching the existing numbers) prints `BANDWIDTH: PASS|FAIL (...)` and exits 4
  when the payload average or worst second per client is over (process deaths keep exit 1).
- `perf-check.sh`: passes `--kbps-budget` (constants at the top of the verdict part), maps exit 4 to a bandwidth FAIL, adds
  a column `KB/s/client (worst s)` before Result (older rows read as `-`), and `--self-test` covers the rule. A run with
  `--net-stats` is recorded as partial (never a baseline).

### A4. Report before cutting
Commit A ends with one `--net-stats` run (`--bots 15 --ramp 120 --hold 300`) and a table in docs/loadtest-results.md:
bytes per second per client by packet type and the top objects, payload and wire. Part B's order below is the expected
ranking (spec 045's guess); if the measured table disagrees, B still does B1 to B3 but the notes say which cut paid what.

### B1. Remote players: 15 Hz, interpolated, no input forwarding
Today `NetworkObject._enableStateForwarding = true`: every player's replicate inputs and reconcile states go to every other
player at 30 Hz (about 100 B per tick per player per observer, so ~3 KB/s for each other player).
- Player prefab: `_enableStateForwarding = false`. The owner keeps [Replicate]/[Reconcile] exactly as now (prediction and
  correction for yourself are unchanged; the server still simulates every player).
- Spectators get the player root through a server-authoritative `NetworkTransform` on the root: position and rotation
  (yaw only if the component allows it), no scale, `_sendToOwner = false` (the owner predicts), `_interval = 2` (15 Hz at
  the 30 Hz tick), interpolation 2 intervals. `NetRateRules.RemotePlayerInterval = 2`.
- The `Graphic` child's `NetworkTickSmoother` keeps its owner settings; its spectator settings are checked so the two do
  not fight (NetworkTransform moves the root, the smoother smooths the graphic). Builder decides and notes which.
- Crouch for others stays the `CrouchedForOthers` SyncVar. `CharacterVisual` already measures speed from drawn motion.
- Spectator branch in `PerformReplicate` (`!IsServerStarted && !IsOwner`) becomes dead: remove it.
- `-remote-hz 30|15` (dev builds, host only, default 15) sets the interval at spawn, for the human's A/B (docs/feel.md 9).
- Expected delay of a remote player: 2 intervals of 66.7 ms = 133 ms plus half the round trip. Spectator prediction (1 tick
  ahead) is gone; that is the trade.
- If FishNet refuses a NetworkTransform on a predicted object with forwarding off (check `_favorPredictionNetworkTransform`
  and FishNet's prediction docs), stop and write a Blocker: the fallback (a custom 15 Hz state RPC) is a new design.

### B2. Held items: no transform sends while carried
Every client already draws a held item at the holder's hand (spec 024), so the 30 Hz NetworkTransform updates of a carried
item are not seen. While `HolderClientId != NoHolder`, the server turns its NetworkTransform's position and rotation sync
off (public `SetSynchronizePosition/Rotation`). Client code that reads a held item's root position (Grep `Pickup` users:
aim, sounds, exit flight, `ItemTween` start) must read the holder's hold point instead (one helper on `Pickup`); moving the
root locally is allowed only if it does not fight the client's NetworkTransform (check; note which). On release, sync goes
back on before the new position is set, so the drop/place position is sent in the next tick (and the 027b settle still works).
Late joiners: a held item's position is irrelevant until release (the hand draws it). `NetRateRules.SyncWhileHeld = false`.

### B3. Cheap config cuts
- All `AddServerAuthoritativeTransform` objects: `SynchronizeScale = false` (no root is scaled at run time; the 031b draw
  scale is visual-only; verify with Grep for `localScale` writes on networked roots).
- Customers (NPC guests): `_interval = 2` with interpolation 2 (`NetRateRules.CustomerInterval`).
- Not changed: SyncVar send rates of stations (unless A4 shows them in the top 3; then note it as a follow-up, do not tune).

### B4. Feel guard (docs/feel.md 1, 5; prediction)
- Owner: nothing in the owner's prediction path changes; FeelPredictionTests, MoveDataSurfaceTests, HandsOnSurfaceTests
  and the spec 027 tours stay green.
- Remote players: `RemoteMotionProbe` (dev, bots with `-metrics`) records each remote player's drawn graphic position every
  frame. `RemoteMotionStats` (pure) reports per second `remote_jitter_p95` (p95 of |drawn speed - smoothed speed| / 2.5 m/s
  over frames where the remote player moves) and `remote_teleports` (frames with a drawn jump over 0.5 m, spawns excluded).
- Owner corrections: bots count `owner_corrections` per minute (a reconcile whose state is more than 5 cm from the client's
  own predicted position for that tick; a small per-tick ring buffer in the probe). Compared before (part A run) and after.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: A `--net-stats` run writes `pid_*_bps` fields; the per-type table (payload and wire, per client, top objects) is in
      docs/loadtest-results.md, dated, before and after part B.
- [ ] AC3: `NetTrafficTests`: `TrafficTable` sums, per-client division, `Unattributed` never negative, top-N order, field
      names; `WireBytes(1000, 10) == 1280`; every reflected FishNet/LiteNetLib member exists with the expected type (fails
      on a FishNet upgrade, names the member).
- [ ] AC4: Without `-net-stats` the host never enables FishNet statistics (test on the probe's decision function), and the
      perf-check GC per frame stays within the 10 % rule against the current baseline.
- [ ] AC5: `bash scripts/perf-check.sh` (15 bots + host, 16 players) after part B: payload per client **avg <= 35.0 KB/s
      and worst second <= 40.0 KB/s**, wire avg recorded in the new column; frame budget and memory bounds still PASS.
- [ ] AC6: `bash tools/bots/run-loadtest.sh --bots 15 --ramp 120 --hold 300 --kbps-budget 35,40` prints `BANDWIDTH: PASS`;
      the same with a budget of `1,1` prints `FAIL` and exits 4.
- [ ] AC7: `NetHeadroomTests`: the player prefab has forwarding off, a root NetworkTransform with interval
      `RemotePlayerInterval`, scale off, not sent to the owner; every server-authoritative item and customer has scale off;
      customers use `CustomerInterval`; held-item sync rule: off while held, on before the release position is set.
- [ ] AC8: Feel numbers from the after run, 15 bots: `remote_teleports == 0` outside spawns, `remote_jitter_p95 <= 0.15`,
      `owner_corrections` per minute not more than 10 % above the part A run (or both under 1/min).
- [ ] AC9: Visual check `--tours look,cook,tutorial,menu` PASS, 0 Exception lines.

## Test plan
- EditMode: `NetTrafficTests` (part A), `NetHeadroomTests` (part B; loads the generated prefabs with AssetDatabase and reads
  the serialized fields; `RemoteMotionStats` on made-up tracks: steady walk, a 1 m jump, a stop).
- `scripts/perf-check.sh --self-test` (the bandwidth rule on made-up rows).
- Runs: part A `run-loadtest.sh --bots 15 --net-stats`; part B the same, then `perf-check.sh` (full, recorded).
- Command: `bash scripts/verify.sh`.

## Human steps
- A/B play-test with 2+ real players: host with `-remote-hz 30` and with `-remote-hz 15`; say if remote players look worse.
- Later, with real Steam accounts: one session over the Steam transport to compare wire bytes (not a gate for this spec).

## Open questions (recommended defaults)
1. Budget: payload avg 35 KB/s and worst second 40 KB/s per client at 16 players; wire only recorded, no budget yet.
   [default: yes; about 30 % headroom for cooking traffic, Steam relay and more NPCs; 16 x 35 = 4.5 Mbit/s host upload]
2. Remote players at 15 Hz with ~133 ms interpolation delay, no spectator prediction. [default: yes, human A/B decides]
3. Held items sync nothing while carried. [default: yes]
4. Customers at 15 Hz. [default: yes]
5. If the measured table shows another top source (e.g. station SyncVars) that is not cut here: a follow-up spec, not a
   change of this one. [default: yes]

## Implementation notes
Builder (Opus), 2026-11-11, branch `spec/054-network-headroom` on top of 796da9f. Not committed (the coordinator asked for no
commits), so parts A and B are one working tree; the "before" run was made with a part A only build.

**Measured (15 bots + host, ramp 120, hold 300, `--net-stats`; full table in docs/loadtest-results.md).** Per client: Replicate
23.7 KB/s (56 %) and Reconcile 17.3 KB/s (41 %) were 97 % of all traffic (object "Player" 41.0 of 42.3 KB/s); carried items about
0.35 KB/s, customers 0.19 KB/s. Payload per client 43.9 avg / 47.7 worst second before; **2.5 / 4.0 after (15 Hz)**, 3.2 / 4.6 with
`-remote-hz 30`. Wire 45.7 -> 3.6 KB/s. B1 paid for practically everything; B2 and B3 are each under 1 % of the old total.

**Deviations and decisions.**
- The spec assumed NetworkTransform bytes land in "Unattributed". They do not: FishNet's NetworkTransform sends through an
  ObserversRpc (`ObserversUpdateClientAuthoritativeTransform`), counted per observer. Unattributed is packet headers and the
  SyncType fan-out (FishNet counts a SyncType write once, not per observer). The per-type numbers are FishNet's own counts;
  the socket total and the bots' bytes are exact.
- Statistics mode `Development` is enough in a development player: FishNet's statistics file defines DEVELOPMENT for
  `UNITY_EDITOR || DEVELOPMENT_BUILD` (checked in the run: the pid_ fields are there). Every reflected member is checked by
  `NetTrafficProbe.CheckMembers` / `WireBytesProbe.CheckMembers` (NetTrafficTests). FishNet sources are untouched.
- B1: `NetworkObject._networkTransform` points at the root NetworkTransform, so FishNet configures it for prediction (not client
  authoritative, not sent to the owner). The Graphic's NetworkTickSmoother keeps its owner settings and `Favor NetworkTransform`
  (FishNet's default, set explicitly): for spectators it stands aside while the NetworkTransform smooths the root, so they never
  fight. Rotation goes as the whole packed quaternion (snapping only changes how it is applied, not what is sent). Teleport off
  (the server never teleports a player). A spectator's controller takes its height from `CrouchedForOthers` (it runs no replicate).
  `-remote-hz 30` calls `NetworkTransform.SetInterval(1)` in `OnStartServer` (dev builds; FishNet buffers it for late joiners).
- B2: the server turns position and rotation sync off in `ServerAttach` before the move, and `Pickup.ReleaseTo` turns it on, calls
  `Teleport()` and `ForceSend()` (its last send was before the pick-up, possibly to the same spot) and only then sets the pose.
  A client moves a held item's root to the holder's hold point itself every frame, with its own NetworkTransform sync off
  meanwhile (no fight: there is nothing new to move to), so every reader of the root sees what it saw before. Helper:
  `Pickup.TryGetHolderHoldPoint`. Readers found by Grep (unchanged, they read the followed root): PickupInteractor (pick sound
  743, snapshot 987, put-into poses 1256/1282, lid and pour sounds 1883/3169/3179, spot offset 2012, aim 2055, nearest 3008),
  FoodVisual sound (508), VesselSteam (144), Vessel.ContentPoint, the item labels (Food, Vessel, SupplyBox), ItemTween/ItemLook
  root pose. Edge: an item picked up within about 100 ms of landing still has a NetworkTransform goal; at its release a client
  may drift towards that goal for a frame before the teleport arrives.
- B3: `AddServerAuthoritativeTransform` sets scale off, the interval and the interpolation; Grep found no run-time `localScale`
  write on a networked root (only children and visual copies).
- The feel guard: owner corrections come from two dev-only, read-only events in PlayerController (`DevOwnerTicked` after a live
  owner tick, `DevOwnerReconciled`), compared in a 128-tick ring (RemoteMotionStats). A teleport is a jump over 0.5 m that is also
  faster than 10 m/s (twice the sprint), so a long frame of a busy bot is not one. The summary's `remote_jitter_p95` is the p95 of
  the per-second p95 values.
- Tests: `Pickup` derives from a FishNet type and the EditMode assembly does not reference FishNet, so NetHeadroomTests calls
  `Pickup.ApplyHeldSync` / `ReleaseTo` by reflection (it fails naming the method if one is renamed).
- perf-check: `KBPS_AVG_BUDGET` / `KBPS_WORST_BUDGET` above the verdict, exit 4 of the load test is a bandwidth FAIL, the column
  "KB/s/client (worst s)" (payload avg (worst), wire avg; older rows "-"; the 2-hour row got its 47.9 (51.9) from its note),
  `--net-stats` rows are marked "(partial run: net-stats)" and are never a baseline. `run_rows` reads Result as the last column.

**AC8 is not met (needs a decision).** Teleports: 0 (before 3). Jitter: 0.49 at 15 Hz and 0.50 at 30 Hz against 0.73 before, so
the 15 Hz rate adds nothing; the 0.15 guard is not met by any variant, and since 15 Hz equals 30 Hz the bots' own uneven motion
(30 fps clients on a loaded PC, the server running their inputs unevenly) is the likely floor of this metric, not the transport.
Owner corrections: **208 per bot-minute at 15 Hz, 165 at 30 Hz, against 43 before** (27 % of seconds have one, 10 % before). Cause:
collisions with other players. With forwarding the client re-simulated every other player tick by tick, also during the own
reconcile's replay; now the own prediction bumps into others where they are drawn (133 ms late) while the server has them
elsewhere. Two experiments, both removed again: a client-only capsule placed where the server most likely has the other player
(newest update extrapolated by its speed): 204/min, no better; no player-player collision on the client at all: 1126/min (the
bots bump all the time on the server). A real fix rewinds the other players' bodies to their server positions for each replayed
tick (from the NetworkTransform history), client only: a new design, not tuned here. The human A/B (`-remote-hz 30|15`) should
say whether bumping into others now feels worse.

**Player pass-through option and correction size (coordinator follow-up, 2026-11-11).** New dev-build flag
`-player-collision on|off` (host or server only, default on). The host decides and puts it in each player's
`CollidesWithPlayers` SyncVar. The server and every client apply `Physics.IgnoreLayerCollision(Player, Player)` from it and
recompute the body probes' mask, so the server and the owner's prediction always agree. The world, stations and NPCs are
untouched. The bots also record the size of each correction (a reconcile more than 5 cm from the prediction) in bins with edges from 6 to 150 cm per
second. The summary gives the run's p50/p95, interpolated inside a bin, and the exact maximum. 15 bots, ramp 120, hold 300.

| Option | Payload KB/s per client (avg / worst s) | Corrections per bot-minute | Size p50 | Size p95 | Size max |
|---|---|---|---|---|---|
| Before: forwarding, spectators predicted | 43.9 / 47.7 | 43.5 | not measured | not measured | not measured |
| 15 Hz, player collision on (default) | 2.6 / 4.3 | 224.4 | 15.9 cm | 63.7 cm | 130 cm |
| 15 Hz, `-player-collision off` | 3.2 / 4.7 | 0 | - | - | - |

The size of the old forwarding corrections was not measured. The size probe came after that build, and the netcode is gone from
this tree (it would need the part A state rebuilt with the new probe). Jitter p95 was 0.49 with collision on and 0.39 with it off.
There were 0 teleports in both. With collision off the bots are never blocked, so they move more and send a little more.
Raw data: `.verify/loadtest054-col-on/`, `.verify/loadtest054-col-off/`.

**Follow-up review fixes** (796da9f review, folded in at the coordinator's request; outside 054's scope list).
1. `FoodVisual.ShownFill` removed (no caller).
2. `PickupService.PlanPlace`: the neighbour test is a `readonly struct NeighbourTest` and a cached static delegate through a new
   `PlaceTurn.TurnOverlaps<TState>` overload, so a place request allocates no closure (the `Func<int, bool>` overload stays for tests).
3. `Pickup.ColliderCenter` (box centre, scaled) and `PlaceTurn.Footprint(half, center, ...)` with a positive and a negative reach
   per axis; `PlanPlace` checks the footprint where the box lies. Items without a box collider are not checked (documented).
4. `PlaceTurn.IsHalfTurn`: `PlanPlace` skips the overlap check for a half turn, so it is never refused by construction.
5. `PickupInteractor.AnswerContents`: only `ContentsPrediction.RefusalEnds` is checked; a yes leaves the note (ids never repeat).
Test: `MouseControlsFollowUpTests.Footprint_OffThePivot_IsCheckedWhereTheBoxLies_AndAHalfTurnIsNeverRefused`.

**Follow-ups.** The replay rewind above (or a human decision that the corrections are acceptable). Bots at 30 fps limit what the
jitter metric can show; a 60 fps bot or a human capture would tell more. Wire bytes over the Steam transport (Human steps).

## Blockers
(Builder fills this in after 3 failed attempts.)
