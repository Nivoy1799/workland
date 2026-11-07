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
| D23 | Currency | Dollar ($), whole dollars only, no sub-units | 2026-11-05 | Simple to read; integer money keeps the sim deterministic |
| D24 | Cloud LLM provider (was O2) | OpenRouter, with pinned providers that support strict JSON output | 2026-11-05 | One key, easy model comparison; switching is a config change (D3) |
| D25 | Gateway language (was O3) | C# / ASP.NET on .NET 10 | 2026-11-05 | One language and toolchain for the project; can reuse /sim (item hash, process tree) without a second implementation |
| D26 | Asset packs (was O8) | Start with free CC0 packs, Kenney first; KayKit/Quaternius only to fill gaps. Art sits behind a swap layer (spec 015). Paid packs (e.g. Synty POLYGON) only if the project heads to release, and then bought outright; no subscription-licensed assets in the project | 2026-11-07 | Project is a playground for now: $0 and no licence risk. Synty's subscription licence requires an active subscription for any further development after cancelling |
| D27 | World rule: native vegan | The game world is native vegan: no animal products exist as goods, ingredients, recipes, items or generated text. Society is post non-vegan: it remembers a dark past of animal farming, people and animals now live together, and NPCs talk and joke about the past | 2026-11-07 | Decided by the human; core identity of the game. See docs/economy-and-professions.md |

## OPEN

| # | Topic | Options | Needed by |
|---|---|---|---|
| O1 | Working title | "Worktown" is a placeholder | Steam page |
| O4 | Backend stack | Own API + Postgres vs Nakama | Phase 4 |
| O5 | Server hoster | Which rental provider, region (EU) | Phase 4 |
| O6 | Moderation approach | Rule-based filter, small classifier model, or provider moderation API | Before Early Access |
| O7 | Max players per server at Early Access | Depends on load test results | After spec 004 |
| O9 | How the dark past is referenced | Own fictional history only (recommended), or direct comparisons to real atrocities such as the Holocaust (the human's wording on 2026-11-07) | Before the content-audit spec |
