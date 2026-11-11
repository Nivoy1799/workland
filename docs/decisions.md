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
| D23 | Currency | (revised by D39: cents) Dollar ($), whole dollars only, no sub-units | 2026-11-05 | Simple to read; integer money keeps the sim deterministic |
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
| D35 | Professions | (robot repair returns after Early Access, D47) **Mechanic dropped.** The game is about the restaurant; other "professions" become restaurant roles or business branches (delivery, events, ingredient creation) | 2026-11-10 | Decided by the human |
| D36 | Player cap (was O7) | 16 players per game | 2026-11-10 | Decided by the human (D33); spec 045 load test now tests 16 players |
| D37 | Game design v1 | docs/game-design.md v1 is binding: co-op restaurant climb in 5 stages (food corner first), NPC rivals, 16 players, time runs only while playing | 2026-11-11 | Approved by the human (inbox question 13); specs 052 and 053 follow it |
| D38 | AI content for Early Access | No live AI gateway at Early Access; names are generated offline and shipped with the game. Spec 055 (gateway go-live) is deferred | 2026-11-11 | Decided by the human ("AI live we skip"): no hosting, no running cost, no public-token risk |
| D39 | Currency sub-units (revises D23) | Dollars **with cents**; all money is integer cents in the sim and game (spec 037) | 2026-11-11 | Decided by the human (inbox question 17): street-food prices like $9.50 need cents |
| D40 | Crew-size difficulty | The climb is tuned for **4 players** (the optimum). Per-player difficulty rises from very easy solo to 1.0 at 4 players, then stays flat (no harder for 8-16, coordination is hard enough). Demand grows with the crew; goals and big one-time purchases grow by players x difficulty; prices never change with the crew; progress is stored as a fraction of the goal, so joining or leaving is fair | 2026-11-11 | Decided by the human (inbox question 24): "only as hard as players are there", "focus on 4 people", "at some point it stagnates" |
| D41 | Setting: Haven Nine (replaces the clean-utopia draft of docs/setting.md) | A town under a glass Dome. Climate change killed the Earth outside; people built Domes to survive, and robots made life inside a utopia. Then it broke down: the knowledge to repair things was lost, drugs and addiction spread, society fell apart. The game starts there: you recultivate a small town as if Earth were an unknown planet. Plan and story: docs/town-plan.md, docs/town-story.md | 2026-11-11 | Approved by the human (inbox question 28): "a good starting point, lacking some details" |
| D42 | Why everyone is vegan (frames D27) | Native vegan out of **need**, not ethics: after the collapse outside there was no other option. D27 (no animal products as goods or text) stays unchanged | 2026-11-11 | Decided by the human |
| D43 | Revival | Each district has a level 0-3 (grey, patched, lively, thriving), computed in /sim from the quality of meals served there, never going back. In Unity the looks are swaps, not rebuilds | 2026-11-11 | Town plan (D41) |
| D44 | Pillar 5 and the Low Ring | Pillar 5 reads "peaceful, not perfect". Gambling and addiction appear only as world content (places, NPC stories, recovery shown with respect), never as a player loop; all vices are made up; no crime, no harm by players | 2026-11-11 | Town plan (D41); the human named drugs and addiction as part of the story |
| D45 | Borders and the outside | v1: the Dome wall with the Storm Ring behind it, no outside. Later releases: exploring outside needs oxygen and protection | 2026-11-11 | Town plan (D41) and the human |
| D46 | Time | A 40-minute game day; night is played, never skipped; the bed ends your shift. Seasons come after v1 | 2026-11-11 | Town plan (D41) |
| D47 | Mechanic, later (revises D35 for after Early Access) | Robot repair returns as a role after Early Access (fix your own robots, then the town's); not in v1 | 2026-11-11 | The human: "in later stage, just to make it doable, early access release" |
| D48 | Players block each other (reverses the question 23 default) | Players do not pass through each other. Blocking must not cause visible rubber-banding (spec 054b) | 2026-11-11 | Decided by the human: "please not let player pass through each other" |

## OPEN

| # | Topic | Options | Needed by |
|---|---|---|---|
| O1 | Working title | "Worktown" is a placeholder | Steam page |
| O4 | Backend stack | Own API + Postgres vs Nakama. Mostly moot after D33 (host saves); only needed later for the AI gateway, leaderboards or cloud saves | later |
| O5 | Server hoster | Only for the AI gateway after D33 (no game servers) | before the AI gateway goes live |
| O6 | Moderation approach | Rule-based filter, small classifier model, or provider moderation API | Before Early Access |
| O10 | Direct-IP transport over VPN (MTU 1280, spec 045) | Proposed: players connect through Steam; Tugboat (direct IP) stays for LAN and dev only, because it hard-codes a 1350-byte MTU. Alternative: patch FishNet (vendor code) | Before Early Access |
