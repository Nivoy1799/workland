---
id: 003
title: Netcode graybox, two players and pickups on a dedicated server
status: approved
owner: architect
builder: opus
---

# 003: Netcode graybox

## Goal
Prove the network foundation: a headless Linux dedicated server, clients that join
via Steam, walk around a graybox and pick up and drop objects, all server-authoritative.

## Scope
- `game/` Unity project (created by human, see Human steps).
- `game/Packages/manifest.json` (change: add `com.worktown.sim` as `file:../../sim`).
- `game/Assets/_Project/Scenes/Graybox.unity` (new): flat ground, a few boxes, 20 pickup cubes.
- `game/Assets/_Project/Scripts/Net/` (new): bootstrap (server vs client by command line),
  player controller, pickup interaction.
- Build scripts in `game/Assets/_Project/Editor/Build/` for Windows client and Linux dedicated server.

## Out of scope
- Cooking gameplay, UI beyond a debug overlay, art, AI, backend.

## Design
- **Transports:** FishNet Multipass with two transports:
  - FishySteamworks for real players.
  - Tugboat (plain UDP) for direct IP connections, used by bots in spec 004 and local testing.
- **Steam on a dedicated server:** a headless server has no logged-in Steam client, so it cannot
  use the normal client API. It must run as a Steam game server (`SteamGameServer.Init`,
  `SteamGameServerNetworkingSockets`) and needs the Steam game server libraries next to the
  build (`steamclient.so` on Linux, from SteamCMD). Whether FishySteamworks covers this mode
  unchanged is unknown, which is why AC2b is a spike (see Acceptance criteria).
- **Steam App ID:** development reads `game/steam_appid.txt` (480, Valve's Spacewar test app).
  Release builds take the real App ID from build configuration, never from a tracked file.
  Replacing 480 is out of scope until we have our own App ID.
- **Startup:** `-server` starts headless server, `-connect <ip:port>` connects via Tugboat,
  default client flow uses Steam.
- **Authority:**
  - Movement: client-side prediction is not required. Server-authoritative movement with
    client input sent as intents and NetworkTransform interpolation is acceptable for now.
  - Pickup: client sends `RequestPickup(objectId)` ServerRpc. Server checks distance (max 2.5 m)
    and that the object is free, then sets owner. Same for drop.
- **Interest management:** enable FishNet's distance condition (e.g. 40 m) from the start.
- **Debug overlay:** ping, server tick, player count, held object.

## Acceptance criteria
- [x] AC1: Linux dedicated server build runs headless and logs "Server started" within 30 s.
      Done 2026-11-06: in WSL Ubuntu, "Server started (Tugboat)" after 3.8 s.
- [ ] AC2a: Two clients on different machines connect via Steam (FishySteamworks, App ID 480)
      and see each other move. One of them may host (server plus client in one process); the
      server-side code paths are the same as on the dedicated server.
- [ ] AC2b (spike): A client joins the headless dedicated server via Steam. Done when either
      it works and the steps are documented in `game/README.md`, or it does not and the spec
      gets a `## Blockers` note: what was tried, what failed, and the proposed path (patch
      FishySteamworks, another Steam transport, or Steam for lobby only plus Tugboat for traffic).
      A failed spike does not block merging this spec; the follow-up becomes its own spec.
- [x] AC3: Two clients join via Tugboat with `-connect` and see each other move.
      Done 2026-11-06: two Windows client builds on the WSL Linux server; server log shows
      both spawned with 2 observers; human confirmed movement syncs, also while carrying a cube.
- [x] AC4: A client cannot pick up an object further than 2.5 m away (EditMode or PlayMode test of the server validation).
      Done: `PickupRulesTests` (EditMode), plus the real RPC path in an Editor host
      (5.8 m refused with TooFar, 2.0 m accepted with ownership, drop releases it).
- [x] AC5: Two clients requesting the same object at the same time: exactly one gets it (test).
      Done: `PickupRegistryTests` (two clients and 32 racing clients, first claim wins).
- [x] AC6: A `Sim` type is callable from a game script, proving the package link.
      Done: `SimLinkTests` matches the pinned potato hash; the overlay also shows it at runtime.
      Builds use Mono, so no `link.xml` yet.
      An EditMode test builds `potato` with `ItemFactory` (spec 005) and asserts its pinned
      `Item.Hash` (same value as the /sim unit test). `SHA256.Create()` can be stripped under IL2CPP with
      high managed stripping: add a `link.xml` preserving it if server or client builds use IL2CPP.
- [x] AC7: `bash scripts/verify.sh` passes on the Windows PC with `UNITY_PATH` set to the
      6000.3.28f1 editor, so the Unity EditMode tests (AC4 to AC6) run instead of being skipped.
      The Editor must be closed for the batch run.
      Done 2026-11-06: PASS (5 ran): Unity EditMode 27/27, /sim 191/191.

## Test plan
- Server validation logic extracted into plain C# where possible and unit tested.
- PlayMode test for pickup race if feasible, otherwise documented manual test.
- Manual: two Steam accounts on two PCs (AC2a), then a client against the Linux server (AC2b).

## Notes from the build (2026-11-06)
- **Tugboat and path MTU.** FishNet's Tugboat sends UDP packets up to 1350 bytes with
  DontFragment set (`MAXIMUM_UDP_MTU`, LiteNetLib). On a path with a smaller MTU the large
  packets (spawns) are dropped silently while small ones (tick, ping) still arrive: clients
  connect but nothing spawns. Seen locally because WSL inherited MTU 1280 from Tailscale;
  fixed for testing with `sudo ip link set dev eth0 mtu 1500` in WSL (or Tailscale off).
  Risk for players or bots on VPN or IPv6-tunnel paths (MTU 1280) using Tugboat; Steam
  transport fragments on its own. Check in spec 004; do not patch FishNet sources.
- For the AC2b follow-up: the dedicated server runs as a Steam game server with
  `eServerModeNoAuthentication`, so client SteamIDs are not verified. Real authentication
  (Steam auth tickets) belongs in the follow-up spec before any SteamID is trusted.
- FishNet logs only errors in `UNITY_SERVER` builds by default. The scene uses
  `FishNetLogging.asset` (headless: Common), and the server logs each join step
  (`[Net] player N joined / authenticated / spawned / loaded start scenes`).

## Human steps
- Done (task 0.6, 2026-11-05): Unity 6.3 LTS (6000.3.28f1) with Windows, Linux and Linux
  Dedicated Server build support; URP project in `/game`; FishNet 4.7.4, FishySteamworks,
  Steamworks.NET (pinned) and MCP for Unity (pinned) imported.
- Two Steam accounts on two machines, both running Steam, for AC2a.
- For AC2b: a Linux machine or VM to run the dedicated server, and SteamCMD on it for the
  Steam game server libraries.
- Review the netcode diff yourself before merging.
