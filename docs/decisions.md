# Decision log

Agents: never decide anything marked OPEN. Ask the human.
Humans: when you decide, move the row to DECIDED with date and a one-line reason.

## DECIDED

| # | Topic | Decision | Date | Reason |
|---|---|---|---|---|
| D1 | Engine | Unity 6 LTS | 2026-11-05 | Proven for the genre, asset ecosystem, Claude tooling |
| D2 | Server model | Dedicated, server-authoritative | 2026-11-05 | Persistent world, larger servers, AI on proper hardware |
| D3 | AI hosting | Cloud API first, on-prem later, always via OpenAI-compatible gateway | 2026-11-05 | Fast start, switch is a config change |
| D4 | AI model | Qwen3.8-27B class | 2026-11-05 | Good quality per cost, open weights for on-prem later |
| D5 | AI scope | Flavor only at start, architecture prepared for AI-driven actions | 2026-11-05 | Balance and testability first |
| D6 | Cached content | Global state | 2026-11-05 | Shared discoveries, less compute |
| D7 | Player count | Larger servers (start 16 to 24, raise later) | 2026-11-05 | Social economy; cap grows with proven netcode |
| D8 | World | One hand-built small town | 2026-11-05 | Achievable, polishable |
| D9 | Economy | Circular: demand-driven, NPCs fill gaps both ways, byproducts flow between professions | 2026-11-05 | Works with few players, strong co-op hook |
| D10 | First profession | Cooking | 2026-11-05 | Best AI showcase, intuitive |
| D11 | Drug lab profession | Later, not in first release | 2026-11-05 | Rating and audience reach |
| D12 | Art style | Low-poly asset packs | 2026-11-05 | Cheap, cohesive, leaves GPU headroom |
| D13 | Release | Early Access | 2026-11-05 | Early feedback and revenue |
| D14 | Netcode | FishNet (confirm with load test, spec 004) | 2026-11-05 | Scales better for larger servers |
| D15 | Hosting | Self-hosted: rented servers, k3s, Agones | 2026-11-05 | Persistent servers, team knows the stack |
| D16 | Servers | Official only | 2026-11-05 | Protects global state |
| D17 | Unity editor AI | MCP for Unity (CoplayDev), not Unity AI | 2026-11-05 | Unity AI too expensive |
| D18 | Agents | Anthropic models only, via the Claude subscription: Opus for specs, netcode and review; Sonnet for routine building and tests; Haiku for trivial tasks | 2026-11-05 | One subscription, no per-token bills; model tiering stretches the budget (replaces the earlier Qwen Code builder plan) |
| D19 | Items | One `Item` type: ingredients and dishes are the same. Every item is servable; the rating decides if it is good (raw apple fine, raw potato bad) | 2026-11-05 | Simpler model, more emergent play |
| D20 | Item identity | Every operation creates a new item; identity = hash of its process tree with time/temperature in buckets. Exact values only affect quality | 2026-11-05 | Process matters (potato vs fries) while keeping discoveries and the AI text cache meaningful |
| D21 | Process visibility | Players see only buckets ("crispy fried"), never exact values | 2026-11-05 | Readable, keeps a skill element |
| D22 | Operations | Fixed set of operations per station to start; which items go in and what comes out stays open (data-driven, not a recipe list) | 2026-11-05 | Balanceable start without closing off emergent combinations |

## OPEN

| # | Topic | Options | Needed by |
|---|---|---|---|
| O1 | Working title | "Worktown" is a placeholder | Steam page |
| O2 | Cloud LLM provider | OpenRouter (pinned providers with strict JSON) vs direct provider | Spec for AI gateway (Phase 2) |
| O3 | Gateway language | Python (FastAPI) vs TypeScript (Node) vs C# (ASP.NET) | Spec for AI gateway (Phase 2) |
| O4 | Backend stack | Own API + Postgres vs Nakama | Phase 4 |
| O5 | Server hoster | Which rental provider, region (EU) | Phase 4 |
| O6 | Moderation approach | Rule-based filter, small classifier model, or provider moderation API | Before Early Access |
| O7 | Max players per server at Early Access | Depends on load test results | After spec 004 |
| O8 | Asset packs | Which low-poly pack family | Phase 2 graybox to art pass |
