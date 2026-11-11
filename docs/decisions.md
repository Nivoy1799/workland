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
| D28 | How the dark past is referenced (was O9) | Only the game's own fictional history (a named past era, museums, NPC memories); no names of or direct comparisons to the Holocaust or other real atrocities in game text or AI-generated text | 2026-11-07 | Decided by the human; avoids hurting players and Steam/moderation problems |

| D29 | Scale | Real-world metric (1 unit = 1 m); hand-held items get a fixed readability factor: cookware and tools x1.15, small items (under 15 cm) x1.5. Reference table in docs/art-direction.md | 2026-11-08 | Decided by the human after the spec 024 play-test: sizes were "all random" |
| D30 | Character style | Normal human proportions, no chibi; players and customers from one pack family, animation speed from the measured stride | 2026-11-08 | Decided by the human; Mini Characters looked strange and their feet slid |
| D31 | Movement speed | Walk 2.5 m/s, sprint 5 m/s, crouch 1.25 m/s (was 4 / 8 / 2) | 2026-11-08 | Decided by the human: calmer life-sim feel, animations can match |
| D32 | Colour palette | One warm muted palette (16 named colours in docs/art-direction.md), defined once in code and once for UI; no other hard-coded colours | 2026-11-08 | Decided by the human |

| D33 | Game mode (replaces D15, D16 for the game) | **Co-op restaurant simulator, player-hosted:** the host starts the game (Steam), up to **16 players** join. No persistent town, no official game servers ("game B" dropped). The host's game is saved on the host's PC | 2026-11-10 | Decided by the human after the design critique: the persistent town is too big, too costly and risks empty servers |
| D34 | Progression | Start as a **food corner: 1 dish, guests, cash only**. Then: rent a restaurant, choose the service (fast food first), add delivery or custom orders, mix as you like. Specialised content later. When the restaurant is automated: create new ingredients, compete against the best NPC teams in cooking competitions, serve at event locations, better marketing, buy cars | 2026-11-10 | Decided by the human: a restaurant simulator first |
| D35 | Professions | **Mechanic dropped.** The game is about the restaurant; other "professions" become restaurant roles or business branches (delivery, events, ingredient creation) | 2026-11-10 | Decided by the human |
| D36 | Player cap (was O7) | 16 players per game | 2026-11-10 | Decided by the human (D33); spec 045 load test now tests 16 players |

## OPEN

| # | Topic | Options | Needed by |
|---|---|---|---|
| O1 | Working title | "Worktown" is a placeholder | Steam page |
| O4 | Backend stack | Own API + Postgres vs Nakama. Mostly moot after D33 (host saves); only needed later for the AI gateway, leaderboards or cloud saves | later |
| O5 | Server hoster | Only for the AI gateway after D33 (no game servers) | before the AI gateway goes live |
| O6 | Moderation approach | Rule-based filter, small classifier model, or provider moderation API | Before Early Access |
| O7 | Direct-IP transport over VPN (MTU 1280, spec 045) | Proposed: players connect through Steam; Tugboat (direct IP) stays for LAN and dev only, because it hard-codes a 1350-byte MTU. Alternative: patch FishNet (vendor code) | Before Early Access |
