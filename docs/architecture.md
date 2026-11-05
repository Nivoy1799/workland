# Architecture

## Big picture

```
 Player PC (Steam client)                    Our infrastructure (k3s cluster)
 ┌───────────────────────┐                   ┌──────────────────────────────────────────┐
 │ Unity client          │   FishNet over    │ Game server pods (Agones)                │
 │  rendering, input, UI │◄─── Steam ───────►│  Unity headless, server-authoritative    │
 │  sends intents only   │   networking      │  runs /sim: jobs, economy, NPC demand    │
 └───────────────────────┘                   │        │                    │            │
                                             │        ▼                    ▼            │
                                             │  Backend API          AI Gateway         │
                                             │  (global state)       (OpenAI-compatible)│
                                             │        │               │  cache          │
                                             │        ▼               │  schema check   │
                                             │  Postgres              │  moderation     │
                                             │  discoveries,          ▼                 │
                                             │  recipe cache,     Upstream LLM          │
                                             │  leaderboards      cloud now, own GPU    │
                                             │                    later (vLLM)          │
                                             └──────────────────────────────────────────┘
```

## Components

### /sim (pure C#, the heart of the game)
- All game rules: ingredient stats, cooking quality, job flow, economy ticks, NPC demand.
- No `UnityEngine` references. Plain C#, .NET Standard 2.1, fully unit tested.
- **One source, two consumers:**
  - Unity consumes it as a local UPM package (`/sim/package.json` + an `.asmdef` with
    `noEngineReferences: true`). In `game/Packages/manifest.json`: `"com.worktown.sim": "file:../../sim"`.
  - `dotnet test` builds the same `.cs` files through `/sim/Sim.csproj` and runs
    `/sim/Sim.Tests` (xUnit). Agents iterate here without opening Unity.
- Deterministic: injected seeded RNG and clock. Same inputs give the same outputs.

### /game (Unity)
- One project, two build targets: Windows client and Linux Dedicated Server.
- Networking: FishNet with the FishySteamworks transport.
- Server is authoritative. Clients send intents ("put ingredient X in pan"), the server
  validates with /sim and replicates results.
- MonoBehaviours are thin adapters between Unity, FishNet and /sim.

### /ai-gateway
- Speaks the OpenAI-compatible chat completions API to the game server.
- Pipeline per request: rate limit, cache lookup (hash of inputs), upstream call,
  JSON schema validation, moderation, cache write, response.
- Upstream is configured by two env vars. Cloud now, own vLLM server later.
- Schemas live in `/ai-gateway/schemas`, one per content type (recipe, npc_line, ...).

### /backend
- Small HTTP API + Postgres for **global** state: discoveries ("first cooked by"),
  recipe cache, leaderboards, player profiles keyed by Steam ID.
- Only official game servers can write (service tokens). Clients never talk to it directly.

### /infra
- k3s cluster on rented servers, Agones for game server fleets, Ansible for provisioning.
- Monitoring: Prometheus + Grafana (game server tick time, player count, AI latency, costs).

## Data ownership

| Data | Owner | Scope |
|---|---|---|
| Player position, inventory, money | Game server | Per server, persisted to backend on save |
| Market prices, NPC demand | Game server (/sim economy) | Per server |
| Discoveries, recipe texts, leaderboards | Backend | Global |
| Generated AI text | AI gateway cache, then backend | Global |

## Circular economy (summary, details in a later spec)

- NPCs generate demand per good per tick (needs: hunger, car condition, hair, ...).
- Players or NPC businesses supply it.
- **NPC supplier = price ceiling:** if no player supplies, an NPC business does, worse and pricier.
- **NPC buyer = price floor:** surplus is bought by an NPC wholesaler, cheap.
- Floor and ceiling margin must make NPC-to-NPC arbitrage unprofitable.
- **Byproducts flow between professions:** food scraps to a farm, used oil and scrap
  parts to the mechanic or recycler. No good is a dead end.
- Economy ticks every in-game hour on the game server, snapshots persisted.
