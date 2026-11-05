---
id: 003
title: Netcode graybox, two players and pickups on a dedicated server
status: draft
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
  - FishySteamworks for real players (Steam App ID from env, 480 for now).
  - Tugboat (plain UDP) for direct IP connections, used by bots in spec 004 and local testing.
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
- [ ] AC2: Two clients on different machines join via Steam (App ID 480) and see each other move.
- [ ] AC3: Two clients join via Tugboat with `-connect` and see each other move.
- [ ] AC4: A client cannot pick up an object further than 2.5 m away (EditMode or PlayMode test of the server validation).
- [ ] AC5: Two clients requesting the same object at the same time: exactly one gets it (test).
- [ ] AC6: A `Sim` type (e.g. `DishScorer`) is callable from a game script, proving the package link.
- [ ] AC7: `bash scripts/verify.sh` passes.

## Test plan
- Server validation logic extracted into plain C# where possible and unit tested.
- PlayMode test for pickup race if feasible, otherwise documented manual test.
- Manual: two Steam accounts on two PCs.

## Human steps
- Install Unity 6 LTS via Unity Hub with modules: Windows Build Support, Linux Build Support,
  **Linux Dedicated Server Build Support**.
- Create the Unity project in `/game` (3D, URP).
- Import FishNet (Asset Store or GitHub release) and FishySteamworks + Steamworks.NET.
- Install MCP for Unity: Package Manager, add from git URL
  `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`, then start its server.
- Two Steam accounts on two machines for AC2.
- Review the netcode diff yourself before merging.
