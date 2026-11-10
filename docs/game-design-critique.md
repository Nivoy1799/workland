# Critique of the game design draft (docs/game-design.md, v0)

Written 2026-11-10 by an outside reviewer (Claude, acting as a designer and producer with no loyalty to the draft).
This is not a decision document. Nothing here changes docs/decisions.md. Where a recommendation touches a DECIDED
row (D2, D7, D15, D16), it is a proposal for you to reopen that row or leave it as it is.

## 1. Verdict in 5 lines
1. Yes, there is a game here that people would pay $15-20 for: a co-op street-food kitchen where 1-4 friends climb from a stall to the best restaurant in town, against named rivals, with "big job" events as the payout moments.
2. No, the persistent official-server town with several player-run professions and crew-vs-crew seasons is not a game this team can ship, fill with players, or pay for in Early Access.
3. The draft describes both games at once and never says which one Early Access is. The tier 4 gate ("top 3 in the season ranking") and the town layer ("compete for guests and spots") only work in the second one.
4. Minute 1 (the kitchen) is built but not yet proven with outside players. Hour 1 and week 1 are named in the draft, not designed.
5. **Single biggest risk:** building the persistent shared town before the co-op climb is proven fun, and then launching into empty servers that cost money every month while reviews say "dead game".

## 2. What is strong
- **The fantasy sentence.** Concrete, visual, with an arc (street corner to main square). It works as a trailer script.
- **Three layers on three time scales.** The right structure. Most shop sims have only the hands layer and run out of
  goals after a few hours.
- **The five numbers as a constraint.** Every feature has to say which number it moves. That keeps scope honest and
  stays readable for players.
- **"Proof" gates** (a big job, the critic) next to money. A test you pass is remembered; a balance you reach is not.
  This is what makes status feel earned.
- **"Growing changes what you do"** names the late-game problem of shop sims before it happens.
- **NPCs fill every role and NPC rivals work "even alone".** The correct answer to low player counts.
- **Sim first:** a numbers-only climb simulation before content. Rare discipline, and the right tool for pacing and
  exploits.
- **Seasons reset rankings, never the business.** Protects what players built.
- **The hands layer that exists** is unusually deep for a week-old project (fryer, griddle, spit, assembly, register
  with change, stock, cleaning, tutorial), and docs/feel.md gives a measurable feel standard.
- **The tone.** Peaceful and vegan gives a clear identity and a store page that does not look like twenty others.
- **An unused strength:** the AI rule "the sim decides, the LLM describes" fits status perfectly. A town paper and a
  critic's review written from computed facts is a cheap, unique status mirror. The draft does not use it yet.

## 3. Problems, ranked by severity

### P1 (critical): the draft is two games, and the Early Access mode is not chosen
**Problem.** Game A is a co-op crew climbing in a host-saved town against NPC rivals (step 3 of the draft's build
order). Game B is a persistent town on official servers with 16-24 players, crews competing for guests and spots, a
player-run supply chain and seasonal rankings (the town layer, the tier 4 gate, step 5, D2/D7/D15/D16). The pillars and
the tier table are written for B. The build order builds A. Nobody has said which one goes on the Steam page.

**Why it matters.**
- B is an MMO-lite. It needs the backend (O4), a hoster (O5), Agones fleets, persistence and migrations, server
  merges, moderation (O6), abuse handling and 24/7 operations, plus an economy balanced across real players. Each of
  these is a project. Together they are a live-ops team.
- "Official servers only" makes a co-op game always-online (a common reason for negative Steam reviews), creates a
  monthly bill that does not depend on sales, and gives the game an end date: the day you stop paying.
- Most indie multiplayer launches peak at a few hundred concurrent players or fewer and decline within weeks. Shards of
  16-24 split that thin across time zones, so most towns are quiet most of the day.
- The network code you already have (FishNet host and client, dedicated server) supports A today.

**Fix.**
- Declare Early Access to be Game A: a "career" save hosted by a crew member (Steam invite, spec 047), 1-4 players,
  NPC rivals, competition between crews only asynchronous (P3).
- Keep the /sim rules shaped so that B can come later. Spec 046 v2 already says "one model, two stores"; keep that.
- Reopen D16 (and D15 for EA scope). Official servers become an option for later events, not a launch requirement.
- Put "persistent town" on the post-EA roadmap and do not promise it on the store page.

### P2 (critical): minute 1 is unproven, hour 1 and week 1 are undefined
**Problem.**
- **Minute 1:** the kitchen was built in about two days. The roadmap has a long "feels off" list, the manual play-test
  boxes of specs 026-038 are still open, and no outside player has touched it.
- **Hour 1:** the draft lists tiers, but not what a session is, which goal is on screen, or when the first unlock comes.
- **Week 1:** after tier 4 there is nothing. Four tiers are a campaign, not a loop.

**Why it matters.** Players decide in 10-30 minutes, and Steam refunds anything played under 2 hours. Shop sims live or
die on whether hours 2-10 give a steady stream of goals.

**Fix.** Before any new spec, write the loop per time scale with numbers (suggested defaults):
- **Minute:** one order. Read, cook, assemble, serve, get paid (exists).
- **Session, 15-25 min (one service):** open, quiet start, lunch rush, close. The summary shows money, stars and the
  next goal at 60-90 % done, so players stop on a cliff-hanger, not on a finished goal.
- **Evening, 2-3 services:** one unlock (dish or equipment) per service in the first 5 hours; a big job every 2-3
  services.
- **Week, 10-20 h:** the tiers, the rival's moves, the critic.
- **After:** the weekly challenge, mastery stars per dish, a voluntary "franchise" restart (P5).

Then put one service in front of 5-10 outside groups (Steam Playtest) **before** building tiers.

### P3 (high): crew competition breaks at small and at large player counts, and solo is undefined
**Problem.**
- "Compete for guests and spots" and "top 3 in the season ranking" need many crews playing at the same time. With 3
  crews on a server, top 3 is free. With 3,000 crews worldwide, top 3 is impossible for 99.9 % of them.
- A shared demand pool is zero-sum: the best crew starves every new crew.
- Solo: a kitchen with six stations, tuned for 2-4 cooks, overwhelms one player, and the crew fantasy needs a crew.

**Why it matters.** The final goal of the game (tier 4) depends on a number the player cannot influence. A large
share of people who buy co-op games play alone most of the time.

**Fix.**
- **Make the NPC rival the main opponent.** A named owner with a shop across the square who reacts to you: undercuts
  your prices, copies your most popular dish a week later, bids for the same stadium contract, mocks you in the town
  paper. This gives the rivalry you liked in Schedule I and the "beat the odds" of a heist, without PvP or crime.
- **Replace "top 3 in the season ranking"** with "beat the district's rival": out-earn or out-rate them over a week, or
  win the head-to-head festival.
- **Crew against crew becomes asynchronous:** a weekly challenge with the same seed, the same guests and the same
  rules, on Steam leaderboards split by crew size (solo, duo, 3-4). Ranks give trophies and titles, never gates.
- **Solo:** scale guest count and patience by the number of active cooks (a /sim formula), and give solo crews
  automation (P8) one tier earlier.

### P4 (high): absence is punished, and neglect becomes griefing
**Problem.**
- "Upkeep is real ... whether you are open or not", plus "reputation decays", plus the town clock in 046 v2 that never
  stops: a crew that plays twice a week pays rent seven days a week and loses rating while they are at work.
- In Game B, a crew that quits keeps the best spot (squatting), and a founder who leaves can freeze a business that
  others built.
- In Game A, the host owns the save. If that friend stops playing, the crew loses its restaurant.

**Why it matters.** Punishing absence is the most reliable way to lose players in persistent games. People who come
back to debt rarely stay. Friend groups play irregularly, and a game should welcome them back.

**Fix.**
- In Game A, time only passes while the crew plays. In Game B (later), rent is charged per day you open.
- A "closed for holidays" sign freezes reputation. Decay has a floor at the current tier's entry level. You never lose a
  tier because you were away.
- No bankruptcy. Debt blocks spending until a service pays it back; it never deletes the business.
- **Crew-owned saves:** copy the save to every member at the end of a session (with a version number, newest wins), so
  any member can host. The cheating risk is acceptable in a co-op save (see "Leaderboard integrity" in section 4).
- For Game B later: the best spots are leases renewed by activity. An inactive business moves to storage with its tier
  intact.

### P5 (high): the economy has a solvable optimum, a runaway loop and no late sink
**Problem.**
- **Price has one right answer.** With demand = base x (reference / price)^e (docs/economy-and-professions.md) and a
  unit cost c, profit is highest at p = c x e / (e - 1), or at the 200 % cap when e <= 1. One elasticity gives one
  markup rule. Someone posts it on day two and price stops being a decision.
- **Runaway leader.** Reputation brings guests, guests bring money, money buys a better location, the location brings
  guests. This loop compounds, and only decay pulls the leader back.
- **Integration is strictly better.** "Own grocery: cheaper, better ingredients, a real edge" means every serious crew
  integrates. The grocer profession becomes a chore inside a crew instead of a market.
- **No late sink.** After tier 4 money piles up with nothing to buy, and "a lot needs to be worked for" fails exactly
  at the top.
- **Catching up.** A crew that starts in month 3 of a persistent town finds every good spot taken.
- **The base is not set.** The price scale itself (a $1,525 wrap, inbox question 7) is still open.

**Why it matters.** "Real effort" in the climb depends on the economy. An exploit becomes the meta and kills the
earned feeling.

**Fix.**
- **Demand as a choice between shops.** A guest's choice depends on your price, quality, wait and reputation relative to
  the rival's, plus district tastes and a daily special. The right price then depends on the rival and the day.
- **Saturation.** Demand per district has a ceiling. Growth comes from new channels (food truck, delivery, events) and
  new districts, not from squeezing one corner harder.
- **Upkeep grows faster than income per tier,** so each tier needs better play, not only more time.
- **Integration trades money for control:** fresh supply, no stock-outs, one special ingredient. It costs time and
  upkeep. It is never cheaper and better at once.
- **Late sinks:** decor and signage (earned-only status items can still cost money), deposits for big jobs, and a
  voluntary franchise restart (a new town save with a legacy perk and a trophy wall).
- **Climb sim (draft step 2):** add bot player profiles (novice, average, expert; solo, duo, four) and a simple
  strategy search. Report the time-to-tier spread per profile. If one strategy beats the others by more than about
  15 %, fix it before slice 1.

### P6 (high): the tiers are too few, too far apart, partly passive and expensive to build
**Problem.**
- Four tiers for the whole game.
- "Rating 3 stars over a week" is a wait, not a test. With a 48-minute game day (046 v2) that is 5.6 hours of real
  play.
- Gate 4 depends on rankings (P3). Money amounts are undefined.
- Each tier is a new building plus a new feature set (tables, three employees, a food truck, signature dishes, a second
  location). Each tier is a production milestone in itself.
- Tier 4's "second location" splits a duo across two kitchens. Hands cannot be in two places, so it becomes idle
  management.
- Location is one of the five numbers, but in the table it is a reward, not a choice.

**Why it matters.** Players need a goal within reach every 15-60 minutes. Four gates are one goal every five hours.

**Fix.**
- Keep the four tiers as chapters and add steps inside them: 3-5 rating stars per tier, each unlocking a dish, a station
  upgrade or decor.
- **Gates are tests, not waits.** Tier 2: serve a lunch rush of 20 guests at a 3-star average (one session). Tier 3:
  pass the stadium rush. Tier 4: the critic's visit plus beating the rival.
- **Location as a choice.** At tier 2 or 3, pick one of two spots with trade-offs (cheap quiet street, expensive busy
  square, near the stadium for events). Then it really is a number the player moves.
- **Reuse buildings.** Tier 1 is the existing shop with most of it boarded up (a stall front). Tier 2 is the full shop.
  Tier 3 is the shop with the unit next door knocked through, plus the food truck. Only tier 4 needs a new building.
  That saves two hand-built interiors.
- **Second location:** cut from EA, or make it an NPC-run shop with a weekly summary, never a play space.

### P7 (high): other professions cannot be equally fun on this budget, and NPC fill makes them optional
**Problem.** Cooking is fun because it has four ingredients:
1. discrete, readable orders;
2. timers with failure (burning);
3. parallel tasks that split across a crew;
4. visible quality.

Checked against the candidates:
- **Delivery:** orders and timers yes, parallel no (one rider alone). Needs vehicle handling and a road network.
- **Mechanic:** diagnosis puzzles, slow, no rush. Every part of every vehicle must be modelled, which is a content
  pipeline of its own.
- **Grocer:** restock plus till. A proven loop, but with low pressure.
- **Hairdresser:** hard to make skill-expressive. Risks becoming a quick-time event.
- **Tofu maker, baker, drinks bar:** mostly more stations. Cheap, because they reuse the kitchen tech.

The cooking layer took about 15 specs, each with review rounds, to reach "good enough to show". Every other profession
needs the same. And because NPCs fill every role, a player mechanic is never needed, so the work feels optional. If you
make them needed (only a player can fix the food truck), the mechanic becomes a tax on the cooks.

**Why it matters.** "Life and profession simulator" on the store page sets an expectation the EA build cannot meet.
Reviews will say "it is just a cooking game".

**Fix.**
- Pitch EA as a co-op street-food restaurant game, with professions on the roadmap.
- The second "profession" is a role inside the crew, not a separate business: a delivery rider for your own restaurant
  (scooter, short routes through the small town, food goes cold) or the food truck at events. It shares the crew's
  money and goals.
- The next real professions are ones that reuse kitchen verbs: bakery or café, tofu shop, drinks bar. Mechanic and
  hairdresser only after a persistent town exists.
- Rule: a profession ships only when a play-tester plays it alone for 30 minutes by choice.

### P8 (medium-high): "growing changes what you do" needs NPC staff, the hardest thing to build in this kitchen
**Problem.**
- Employees who use the same physical, networked stations as players need:
  - pathfinding in a 1.5 m aisle;
  - reservation of stations;
  - coexistence with predicted player actions (spec 027b);
  - readable intent, and never blocking a player.
- That is an AI and netcode project.
- At "big", if staff do the cooking, the best part of the game (the hands) leaves. Many shop sims fade exactly when
  employees take over.

**Why it matters.** It is a production risk and a fun risk at the same time.

**Fix.**
- Grow through **automation appliances** first: a dish machine, auto-fill drink taps, a fry dump station, a heated
  holding shelf. They are deterministic, live in /sim, read clearly and are cheap.
- If you need a person, add one NPC role (dishwasher or runner) on a fixed path.
- Keep the hands central by raising volume and order complexity as the business grows. Staff and machines take the
  dull work, never the timing work.

### P9 (medium-high): no reason to come back tomorrow, and friend groups are irregular
**Problem.** The draft's long-term hooks are a ranking (far away) and decay (a stick). Retention in a co-op game is
mostly "when can we all play", and the draft does nothing for a crew when only one member is online.

**Fix.**
- End every session with something booked: a catering contract for next time, a rumour that the critic is coming, a
  delivery on its way, the rival's announced grand opening.
- A weekly challenge reset (P3) for the groups that want competition.
- **Partial-crew play:** a member alone can run a small service, prep, test recipes or decorate. Progress is shared.
  Big jobs scale with the number of players present instead of requiring the full crew.
- Each member collects their own titles and mastery stars per dish, so personal play also counts.
- No daily-login chores and no punishment for absence (P4).

### P10 (medium): "status earned, never bought" next to Early Access money and running costs
**Problem.** The draft says no pay-to-win, but not how the game earns money. Official 24/7 servers, AI calls and
moderation cost more as players grow, while a one-time EA price brings in less every month after launch. The usual
answer (cosmetics) collides with the pillar, because the status in this draft *is* visible items: the sign, trophies,
decor, crew titles.

**Fix.**
- Decide now and say it on the store page: buy-to-play, no microtransactions in EA.
- Write two lists:
  - **status items**, earned only, forever: sign size, location, trophies, titles, critic stars, paper features;
  - **expression items**, which may become a paid cosmetic pack later: outfits, colour sets.
- Keep running costs near zero for EA: player-hosted co-op, and AI text generated ahead of time through the gateway,
  cached (D6) and reviewed by a human, with no LLM calls at runtime.

### P11 (medium): "few rules, many connections" is a slogan the economy plan contradicts
**Problem.**
- docs/economy-and-professions.md has 12 professions, byproducts (compost, okara, recycling, worn parts), lead times,
  minimum orders, shelf lives, elasticities, reorder points and wages. Every connection is a rule and a balance
  coupling.
- With NPC fill, connections are invisible: the cook cannot tell whether the grocer is a player.
- "No other hidden numbers matter" is already false in the build: hygiene, patience, freshness and tips exist.
- "No spreadsheet management" sits next to reorder points and elasticity.

**Fix.**
- A connection counts only if it creates a decision or an event the player sees.
- For EA, at most two connections:
  - **supplier tier:** cheap or fresh ingredients, which changes dish quality;
  - **channel:** counter, food truck or delivery, which changes reach.
- Byproducts become flavour (a compost bin and an NPC line), not systems.
- Map the existing numbers under the five explicitly: hygiene under reputation, freshness under quality, patience
  under speed.
- Keep feedback coarse, in the D21 style: "guests find this expensive".

### P12 (medium): onboarding covers the kitchen, not the game
**Problem.** The first-shift tutorial teaches the stations. Nothing teaches money, stock, prices, tiers, events or the
rival. Nothing handles a friend who joins in the middle of a climb. No screen answers "what am I trying to do?".

**Fix.**
- **Tier 1 is the tutorial:** one station, three dishes, no stock ordering (the supplier drops a fixed delivery).
- **Each tier introduces one management concept:**
  - tier 2: stock and prices;
  - tier 3: automation and events;
  - tier 4: the rival and the critic.
- A visible goals board in the shop (and on the HUD) with the next one to three goals.
- A friend who joins gets a short station tutorial of their own without pausing the host.
- An assist setting (patience, cooking windows) so mixed-skill friend groups can play together.

### P13 (medium): the vegan, peaceful identity is an asset when quiet and a niche when loud
**Problem.** These are two different things:
- **(a) A native vegan food world:** falafel, a seitan spit, fries. To most players this is simply good street food.
  An asset.
- **(b) The post non-vegan backstory,** with NPCs joking about "the old ones". Non-vegan players can read this as
  moralising about them. It is the part that invites review bombs and culture-war threads.

Also, "peaceful" removes the transgression that gives Schedule I and heists much of their thrill. The draft does not
say what replaces it.

**Fix.**
- **Show, don't tell.** Food and town are vegan without comment. The past lives in optional places (a museum, a plaque,
  rare NPC lines). It never appears in the tutorial or the core loop, and it never addresses the player's real-world
  choices.
- Lore is hand-written only, never LLM text about the past.
- Market the game as cozy co-op street food. Use "vegan" as a proud secondary tag; that niche will spread the word on
  its own.
- **Replace transgression with drama:** the rival, the critic, events against the clock, a town paper that gossips.
- Free animals walking around town are charming and cheap. Put them in the trailer.

### P14 (medium): personal progression can split the crew
**Problem.**
- The draft feeds spec 046 v2, which says "levels unlock techniques (the spit, combo orders, faster hands)". A friend
  who joins in week 2 cannot use the spit, and the veteran is literally faster. Power gaps between friends hurt co-op
  games.
- Helper wages paid by the owner (046 v2) turn an evening with friends into a job.

**Fix.**
- Power unlocks belong to the business: anyone working in the shop can use its spit.
- Personal progress is mastery stars per dish, titles, outfits and a visible craft level, with no personal speed or
  stat bonuses.
- Crews of friends share one crew wallet. Keep wages for strangers on later persistent servers.

## 4. Missing pieces the draft must address
- **EA mode and time model:** who hosts, and whether time passes while nobody plays.
- **Crew rules:**
  - size cap and roles or permissions;
  - join, leave and kick;
  - shared wallet or owner;
  - what a member keeps when leaving, and who keeps the save.
- **Solo play:** how demand, patience and automation scale with one cook.
- **Failure and its floor:** what a failed big job, a bad week or debt costs, and what can never be lost.
- **The rival or rivals:** who they are, how they behave, and how they react to the crew.
- **The big job as a designed structure** (the heist feeling, without crime):
  - **book:** choose a contract and pay a deposit;
  - **prep:** stock up, pre-chop, plan roles, kit out the truck;
  - **run:** an 8-12 minute rush with special rules or modifiers;
  - **payout:** 3-5 times a normal service, a trophy and a paper headline;
  - **optional bonus goals** for extra pay (nothing burnt, every order under 60 s).
- **Endgame:** what a crew does after tier 4.
- **Pacing numbers:**
  - target hours per tier per crew size;
  - session length;
  - game-day length against session length (a 48-minute day against a 20-minute session);
  - the money scale.
- **User-generated text:** crew names, shop signs and chat need a filter. O6 is framed around AI text only, and
  "Wild Joe's" on a sign is player text seen by others.
- **Social and sharing:**
  - voice chat (proximity voice in a busy kitchen is a strong co-op feature, and Steamworks has voice APIs);
  - emotes;
  - a photo mode or a shareable shop card. Status that shows outside the game is free marketing.
- **Where AI fits.** The draft does not mention it. The best fit is the town paper, the critic's review and the
  signature-dish names, all from computed facts. Decide whether EA needs AI at runtime at all.
- **The "life" in "life simulator":** a home, outfits, town activities? If none is planned for EA, drop the word
  from the pitch.
- **Target player, price and content budget for EA:** number of dishes, locations, events and rivals.
- **Play-test and telemetry plan:** Steam Playtest, plus a few metrics (session length, sessions per player, quit
  points, time to each tier).
- **Leaderboard integrity:** host-run sessions can be edited, so decide whether the weekly challenge is just for fun
  (flag outliers) or runs on an official short-lived server.
- **Accessibility:**
  - assist modes;
  - colour-blind safe cues (the cooking rings are colour-coded cream, mustard and red);
  - rebinding (spec 048).
- **What a "season" means in a co-op save.**

## 5. Comparable games and what they teach (genre lessons only)
| Game | What it shares | Lesson for Worktown |
|---|---|---|
| Overcooked 1-2 | co-op kitchen, shared stations | Co-op cooking is great in short bursts. Star goals per level make targets clear. Without variety, chaos gets tiring. An assist mode keeps mixed-skill groups together. |
| PlateUp! | co-op restaurant, day by day, kitchen layout | Growth comes from layout and automation appliances, not NPC staff. Runs end and restart with lasting unlocks, which solves endgame and inflation with a reset the player chooses. |
| First-person co-op shop sims (kebab, fast food, cooking) | hands-on cooking with friends | Hands-on detail sells and streams well. Reviews turn when hours 5-15 repeat the same service with bigger numbers. |
| Supermarket and shop-keeping sims | restock, till, expansion | A very simple loop with a steady drip of unlocks (new products, more floor) holds players for dozens of hours. A grocer works on its own as a loop. |
| Schedule I | a co-op crew grows a business from nothing | The "crew empire" feeling comes from co-op expansion against the world (new areas, properties, workers who automate the dull parts) in a host's save, not from crews fighting on shared servers. Its edgy theme carries part of the appeal; a peaceful game must replace that with rivalry and spectacle. |
| GTA Online heists and businesses | the big job and its payout | Plan, prep, execute, payout, with roles. One big payout beats ten small ones emotionally. Businesses that demand defence or attention become chores players resent. |
| Two Point Hospital / Campus | a climb by star rating across locations | Each new location is a new constraint puzzle, not just a bigger number. Star goals per location give steady targets. |
| Stardew Valley | a town, routines, a community | A town that notices you (NPC lines, festivals) is cheap and strong status. A visible goals board gives week-scale goals. |
| Eco | persistent server, professions that depend on each other | Interdependence works on curated servers with committed groups and a shared goal. With few or random players it becomes a grind, and servers empty out. |
| Survival games with server wipes | persistent servers | Runaway leaders are solved by periodic full resets. Worktown never resets businesses, so it needs other brakes (P5). |
| Car Mechanic Simulator | the mechanic profession | Diagnosis and repair are satisfying but slow, solo, and need a lot of content (every part modelled). A game of its own, not a side job. |

## 6. Recommendation: the next 3 milestones
The milestones are sized for one human plus agents. Code is not the bottleneck: the agents shipped about 15 specs in
two days. Your play-testing, art direction and decisions are. So each milestone ends with an outside play-test, not
with a merge, and its length is set by play-test rounds (at least two per milestone). The week counts are rough.

### M1 "One great service" (about 3-4 weeks)
**Goal:** one 15-25 minute service for 1-4 players that outside people want to play again.
- **Build:**
  - the current queue (039-045);
  - 037 with the price rescale to street-food money;
  - a slim 046: the service structure (open, quiet start, rush, close), a summary that shows the next goal, and a
    host save of the shop;
  - solo scaling of guests and patience;
  - the goals board.
- **Test:** Steam Playtest with 5-10 outside groups and 5 solo players. Measure how many start a second service, and
  where people quit.
- **Cut or defer:**
  - the always-running town clock;
  - wages between friends;
  - techniques locked behind personal XP;
  - AI in gameplay;
  - any persistence beyond the host save;
  - Blender models beyond the stations players touch most.
- **Exit:** most testers start a second service unprompted, and their "feels off" list fits on one page.

### M2 "The climb" (about 6-8 weeks)
**Goal:** tiers 1-3 as a co-op career, 6-10 hours for an average duo.
- **Build, in this order:**
  1. The climb sim in /sim, with bot profiles and the choice-between-shops demand model (P5). Tune it before building
     content.
  2. Tier 1 as a stall front (the existing shop, mostly boarded up), tier 2 as the full shop, tier 3 as the shop
     extended next door plus the food truck.
  3. One named rival across the street with 3-4 reactions.
  4. Two big jobs (the stadium rush, a street festival) with book, prep, run and payout, plus optional bonus goals. Keep
     the crowd cheap: a long queue at the truck, not a full stadium scene.
  5. The equipment shop, free layout (039) and 2-3 automation appliances.
  6. The town paper as hand-written templates filled with sim facts.
  7. A choice between two locations at tier 2 or 3.
- **Cut or defer:**
  - sit-down table service with waiters;
  - NPC employees beyond one simple runner;
  - the second location;
  - the full recipe lab. A minimal version is fine: pick three parts, the sim rates the dish, and the name comes from
    a pre-generated, reviewed list.
- **Exit:**
  - play-test crews reach tier 3 within the target time band;
  - the sim finds no strategy more than about 15 % better than the others;
  - testers come back for a second evening.

### M3 "Early Access" (about 6-8 weeks)
**Goal:** a store-ready build with a 12-20 hour climb and a reason to return every week.
- **Build:**
  - tier 4: the critic's visit, beating the rival, and the main square as the one new building;
  - delivery or the food truck as the crew's second role;
  - the weekly challenge with Steam leaderboards per crew size;
  - trophies and titles;
  - specs 047 (menu and joining), 048 (settings) and 049 (stability);
  - a name filter for crew names and signs;
  - the Steam page, a trailer and an EA FAQ: what is in, what is planned, no microtransactions.
- **Cut from EA:**
  - official persistent servers, Agones, and the Postgres backend (O4 can wait);
  - the player-run supply chain and byproducts;
  - the mechanic and hairdresser professions;
  - live crew-against-crew ranking;
  - LLM calls at runtime (generate ahead of time and cache instead).
- **Keep:** the dedicated Linux server build compiling in CI, so Game B stays possible.

**After EA:** if the co-op game holds an audience, test the shared town as an event before you commit to persistence.
For example, a weekend "market day" server where crews from different saves sell at the same festival.

## 7. Questions only you can answer
1. Is Early Access the co-op career (host saves) or the persistent official town? Will you reopen D16 and D15 for the
   EA scope?
2. Who is the opponent at EA: NPC rivals plus asynchronous leaderboards, or live crews on the same server?
3. Is solo a first-class way to play?
4. How many hours should an average duo take from stall to main square? What does a crew do after that?
5. Does time pass while nobody plays? Can a business ever be lost?
6. Is tier 3 still a street-food counter, or a sit-down restaurant with waiters (a new hands loop and a large new
   system)?
7. Does the business grow through NPC staff or through appliances?
8. Is the second profession a crew role (delivery, food truck) or a separate business?
9. Crew money: one shared wallet, or owner cash plus wages?
10. Monetization: buy-to-play only, for good? Which items count as status and are never sold?
11. How loud is the vegan backstory: optional lore, or part of the main path?
12. Is "Wild Joe's" a fixed story (the crew inherits Joe's stall, a narrative hook) or the crew's own chosen name (which
    needs a name filter)?
13. What goes on the Steam page: "multiplayer life and profession simulator" or "co-op street-food restaurant"?
14. Who are your first outside play-testers, and when is their first session?
