---
id: 054
title: Network headroom (traffic per packet type, 15 Hz remote players, 35 KB/s per client)
status: draft
owner: architect
builder: opus         # netcode (CLAUDE.md: netcode stays on Opus)
---

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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
