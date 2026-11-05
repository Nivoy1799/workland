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
- [ ] AC1: Linux dedicated server build runs headless and logs "Server started" within 30 s.
- [ ] AC2a: Two clients on different machines connect via Steam (FishySteamworks, App ID 480)
      and see each other move. One of them may host (server plus client in one process); the
      server-side code paths are the same as on the dedicated server.
- [ ] AC2b (spike): A client joins the headless dedicated server via Steam. Done when either
      it works and the steps are documented in `game/README.md`, or it does not and the spec
      gets a `## Blockers` note: what was tried, what failed, and the proposed path (patch
      FishySteamworks, another Steam transport, or Steam for lobby only plus Tugboat for traffic).
      A failed spike does not block merging this spec; the follow-up becomes its own spec.
- [ ] AC3: Two clients join via Tugboat with `-connect` and see each other move.
- [ ] AC4: A client cannot pick up an object further than 2.5 m away (EditMode or PlayMode test of the server validation).
- [ ] AC5: Two clients requesting the same object at the same time: exactly one gets it (test).
- [ ] AC6: A `Sim` type is callable from a game script, proving the package link.
      An EditMode test builds `potato` with `ItemFactory` (spec 005) and asserts its pinned
      `Item.Hash` (same value as the /sim unit test). `SHA256.Create()` can be stripped under IL2CPP with
      high managed stripping: add a `link.xml` preserving it if server or client builds use IL2CPP.
- [ ] AC7: `bash scripts/verify.sh` passes on the Windows PC with `UNITY_PATH` set to the
      6000.3.28f1 editor, so the Unity EditMode tests (AC4 to AC6) run instead of being skipped.
      The Editor must be closed for the batch run.

## Test plan
- Server validation logic extracted into plain C# where possible and unit tested.
- PlayMode test for pickup race if feasible, otherwise documented manual test.
- Manual: two Steam accounts on two PCs (AC2a), then a client against the Linux server (AC2b).

## Human steps
- Done (task 0.6, 2026-11-05): Unity 6.3 LTS (6000.3.28f1) with Windows, Linux and Linux
  Dedicated Server build support; URP project in `/game`; FishNet 4.7.4, FishySteamworks,
  Steamworks.NET (pinned) and MCP for Unity (pinned) imported.
- Two Steam accounts on two machines, both running Steam, for AC2a.
- For AC2b: a Linux machine or VM to run the dedicated server, and SteamCMD on it for the
  Steam game server libraries.
- Review the netcode diff yourself before merging.
