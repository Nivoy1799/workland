---
id: 051
title: The town plan (districts, map size, natural edges, terrain, weather, day and night)
status: draft
owner: architect
builder: opus
---

# 051: The town plan (DRAFT, 2026-11-11, not decided; first answers in, see "Answers")

## Goal
Decide where everything in the town happens, how big the map must be, where and how the world ends without an
invisible wall, how the ground rises and falls, and how weather and daylight change the town and the game. This plan is
the base for the graybox, for every location-based number (D34 "location", rent, footfall, delivery distances) and for
spec 050 (street life).

Pictures: style lab round 6 (spec 046) and the plan map on the round 6 page. Round 7 adds the Canopy, the four
seasons and three tries for the Wreck. The town's story is drafted in `docs/town-story.md`.

## Fixed inputs (decided elsewhere)
- D8: one hand-built small town. D33: co-op, player-hosted, 1–16 players, the host's PC runs the server.
- D31: walk 2.5 m/s, sprint 5 m/s, so distances must stay short. D29: 1 unit = 1 m.
- D34: the climb from food corner to restaurant to services, automation, events, competitions and cars.
- D27, D28: native-vegan, peaceful, the dark past only as history. docs/setting.md (draft): the year 2222 as 1950–80
  imagined it.

## Scope
- `specs/051-town-layout.md` (this file) and `docs/town-story.md` (the backstory, draft). Later, after approval:
  `docs/town-plan.md` (the binding plan), and
  follow-up specs for the graybox (`game/`), the location and weather model (`sim/`), and the light and weather
  visuals (`game/`). Each follow-up names its own folders.

## Out of scope
Building anything in Unity or /sim. Final art. (Seasons, snow and player homes were out of scope in the first
draft. The human said yes to both on 2026-11-11, so they are now sections 7b and 4b.)

## Design

### 1. The shape: a town in a bowl
The town sits in a shallow bowl, open to a lake in the east. Nothing is flat: the ground climbs about 45 m from the
lake to the hill.

| Place | Height | How you get there |
|---|---|---|
| Lake shore, beach, pier | 0–2 m | the lowest point |
| Main Street | 2 m (east) to 10 m (west) | a gentle slope, below 4 % |
| Station Square | 10 m | flat square at the top of Main Street |
| Tower Rise (west plateau) | 16 m | a ramp and a lift from Station Square |
| Greenhouse Hill (north) | 15–45 m | terraces with ramps, plus a funicular |
| Hills behind the Wildbelt (south) | 20–60 m | not reachable, backdrop |

Rules for older players:
- Every path stays below 5 %. Each stair has a ramp, lift or funicular next to it. No jumping, no climbing puzzles.
- Four landmarks work as a compass and can be seen from almost everywhere:
  - north: the glass dome on the hill;
  - east: the lighthouse on the pier;
  - south: the arena;
  - west: the tallest tower and the wreck.

### 2. How big
At 2.5 m/s you walk 150 m in a minute. The town is sized so that daily work needs no vehicle.

| Area | Size | Walk time |
|---|---|---|
| Core (Station Square, Main Street, Fountain Cross, Market Hall) | about 230 × 160 m | every core point within 90 s of Fountain Cross |
| Playable bowl (everything you can enter) | about 640 × 480 m (0.3 km²) | edge to edge 4 min walking, 2 min sprinting |
| Backdrop (the city skyline, the far lake shore, the hills) | out to about 8 km | never reachable; it is LOD models and haze |

For comparison: the street in today's build is about 40 m long. The bowl holds about 15 times today's ground.

Getting around:
- walking and sprinting;
- the town **monorail**, a loop with four stops (Station, Lakefront, Arena, Workshop Lane), a train every 60 s;
- the **funicular** from the north end of the Promenade up Greenhouse Hill;
- stage 5 cars on fixed lanes. Players never fly freely.

### 3. The districts: where what happens

| # | District | What happens there | Climb stage |
|---|---|---|---|
| 1 | **Station Square** | monorail station, NPC visitors arrive (the source of walk-up demand), the free food printer, the rankings board, the Town Hall with the rent office and the town model | 1–5 |
| 2 | **Main Street** | cart pitches, small fast-food places, rival stalls, the museum and the statue at the west end | 1–2 |
| 3 | **Fountain Cross** | where Main Street meets the Garden Promenade; the busiest and most expensive corner | 1–4 |
| 4 | **Market Hall** | ingredient suppliers (spec 034), crates from named farmers, decor and equipment stalls | 1–5 |
| 5 | **Garden Promenade** | the car-free green spine from the hill to the meadow; café terraces, outdoor tables | 2–4 |
| 6 | **Lakefront** | pier with the lighthouse, beach, ferry (deliveries only); the most beautiful places to rent | 3–4 |
| 7 | **Homes** (south and north of Main Street) | NPC homes: delivery addresses, drone ports, regulars living there; the players' own flats (4b) | 1–5 |
| 8 | **Workshop Lane** | garage and car dealer (stage 5 cars), the delivery depot, a hardware and decor store | 3–5 |
| 9 | **Greenhouse Hill** | vertical farms, the compost and recycling works (the circular economy), the ingredient lab | 5 |
| 10 | **Festival Meadow and Arena** | event days, the fair, the cooking competitions against the best NPC teams | 5 |
| 11 | **Tower Rise** | five towers; their ground floors are open, and the top of the tallest has the **Sky Deck** restaurant | 5 |

Places you can rent or take, with a location value (footfall) from /sim:
- **Stage 1, cart pitches C1–C8:**
  - Station Square;
  - Main Street west;
  - Fountain Cross;
  - the Market Hall door;
  - the pier;
  - the beach;
  - the Promenade park;
  - the Arena gate (event days only).

  You push the cart, so you can try a new pitch each day.
- **Stage 2, small places S1–S6** (60–90 m², fast food): three on Main Street, one each at Station Square, the
  Promenade and Workshop Lane (cheap).
- **Stages 3–4, restaurants R1–R4** (150–300 m², tables, a delivery hatch, a roof with a drone pad): Fountain Cross
  corner, Lakefront, Promenade garden and Station Square.
- **Stage 5 places:** the Sky Deck, the Wreck night market, the Arena, the Festival Meadow and the ingredient lab.

NPC rival teams run the places you do not have. They move up and down too.

### 4. Everything is open unless it is locked
- Every building with a sign can be entered while it is open: rival restaurants (eat there and see how they do it),
  shops, the Market Hall, the museum, the Town Hall, the station, the garage, the lab, the arena and the Wreck.
- Interiors are **seamless**: no loading screens, and you can see in through the windows (round 4's guest view).
  Occlusion culling and portals keep this cheap.
- Door states, decided by the server:
  - **Open:** anyone can enter.
  - **Closed:** outside opening hours; the door is shut, the sign says when it opens.
  - **Staff only:** rival kitchens and back rooms. You may eat in their dining room, but not enter their kitchen.
  - **Private:** NPC homes. You can ring the bell, deliver to the door or the drone port, and look at the window boxes.
- Your own restaurant: the team sets its opening hours and which doors are open to guests.

### 4b. Player homes (yes, 2026-11-11)
Basics are free in 2222 (docs/setting.md, money option C), so every player gets a flat for free.
- **Where:** the Homes district. The **Crew House**, a block of 16 small flats by the Promenade, one per player slot,
  plus a shared kitchen table and a roof garden. Later, with credits, a player can move to a house of their own.
- **What it is for:**
  - your own corner to decorate: furniture, plants and posters bought with credits (the round 2 personalisation);
  - a wardrobe to change your look;
  - a bed that ends your day. (Proposed: the clock only skips the night when every player in the game is in bed,
    the usual co-op rule.)
  - your mailbox: fan letters, rival challenges, the town paper.
- **Visits:** your door is Private by default. You can set it to Open (friends walk in) or invite one player.
  The same door rules as section 4.
- **Save:** flats are part of the host's save (D33). A player who joins another host's game gets a flat there too.

### 5. Where the world ends: natural edges, never a bare wall
Each edge has a reason in the story and a soft way to turn you back.

| Edge | What you see | What stops you |
|---|---|---|
| **East: the lake** | water to the haze, the ferry leaving, the far shore as faint towers | buoys mark the swim zone. Beyond them a friendly **lifeguard drone** tows you back ("Too far out, friend!") |
| **South: the Wildbelt** | a tall flowering hedge with a wooden gate. Behind it, the old pasture land given back to nature, where cows, pigs, deer and hens live free (D27: people and animals live together) | the gate is closed: "Sanctuary: animals only beyond this point". A ranger robot waves. A lookout platform lets you watch the animals |
| **West: the Wreck** | the **Albatross**, a giant 2100s cargo airship that came down in a storm a hundred years ago across the valley mouth. Moss and trees grow on it, and there is a night market in its belly. Next to it, the old concrete highway ends in mid-air ("the Road to Nowhere", built when cars still drove) | you can walk into the market inside the Wreck. Behind it, the landslide and the canyon walls are too steep |
| **North: over the hill** | farm terraces and the glass dome, the monorail tunnel into the ridge, the big city's skyline peeking over the top | a farm fence ("Robots at work") and the steep ridge. The tunnel gate opens for trains only |
| **Up: the sky** | flying lanes at 30–80 m, drones, the airship | players never fly; stage 5 cars stay on their lanes |

Last resort, in case a player gets through anyway:
- an invisible collider 15 m behind every natural edge;
- a server check: a player outside the play area, or below the fall height, is moved to the nearest safe spot (the
  server owns positions, AGENTS rule 6).

The user's idea of a crashed plane became the Wreck. An airship fits 2222 better, and it is useful as a place, not
just a barrier.

**Round 7: three tries for the Wreck** (the human said "try something"). All three have the night market inside and
work as the west edge. They change the story a little (docs/town-story.md):

| Try | What it is | Story fit |
|---|---|---|
| **A. Albatross, Ark Flight 9** (proposed) | the airship, now with the cargo ramp down which the animals walked out, and the seed library ports | best: an airship lands softly enough for people and animals to walk away, and it explains the Wildbelt and Greenhouse Hill |
| B. Pelican | a 2100s freight plane, broken behind the wing, its cargo door swung up, a T-tail on the skyline | good: your original idea. The landing reads harder, so the story needs a lucky belly landing |
| C. Stella | a 2100s starliner (a retro rocket) lying on its side, nose in the hillside, rows of lit portholes | the most retro-future picture, but a rocket carrying cows needs a bigger story |

### 6. Day and night
- **One town clock**, run by the host. It runs only while the game runs (game-design v1).
- Proposed length: **1 game day = 40 real minutes**: dawn 3, day 24, dusk 3, night 10. See open question 1.
- Light moods come from the style lab: morning, noon, sunset, night, overcast and storm.
- At dusk the lights switch on:
  - street lamps first;
  - then shop signs and windows, each with a small fixed (seeded) delay, so the town lights up in a wave;
  - the round 5 night look: neon, window bands, light trails, searchlights.
- **Night is never pitch dark.** It keeps a minimum brightness so that older eyes can still read the street.
- Technology (client only; the host's server part does no rendering):
  - URP with one realtime sun or moon with shadows;
  - Adaptive Probe Volumes with lighting scenarios (day, night), blended over the cycle;
  - emissive windows and signs with bloom;
  - realtime point lights only near the camera (a light LOD).

### 7. Weather that matters
Weather is planned by the town, which suits 2222, and sometimes it comes in wild.
- **The Weather Office** waters the gardens with scheduled rain at night (02:00–04:00). It also posts the forecast for
  today and tomorrow on boards and in the town paper, so you can plan stock.
- **Wild weather** comes over the Wildbelt hills now and then: fog, storms. The forecast shows its chance.
- Deterministic: the weather of each day comes from the save's seed and the day number (a seeded RNG injected into
  /sim). The host decides it, and clients only render it. Over the network it is a weather id plus the next change
  time, a few bytes.

| Weather | What you see | What it does to the game (/sim inputs) |
|---|---|---|
| Clear | sun, sharp shadows | outdoor tables fill, more cold drinks |
| Warm haze | golden haze, slow people | cold drinks up, hot dishes down |
| Cloudy | soft light | neutral |
| Morning fog (from the lake) | lamps stay on, a foghorn, the lighthouse beam | fewer walk-ups early, lifts by noon |
| Rain | umbrellas, wet reflective streets, puddles | fewer walk-ups, more delivery orders, outdoor tables close. Rain washes the street, but guests carry mud in, so there is more cleaning inside (spec 035) |
| Storm | dark sky, heavy rain, **lightning hits the rods on the towers** (never people), thunder, neon flickers | like rain, plus drones are grounded: deliveries go by ground courier and take longer |
| Windy | leaves, flapping umbrellas, the airship moors | outdoor tables fewer, otherwise neutral |

### 7b. Seasons and the Canopy (yes to seasons, 2026-11-11)
The human's idea: a shield over the town that keeps the rain off and collects everything, with the leaves saved to
make soil. Here it is the **Canopy** (locals call it "the Sail"): a high glass roof over the town core, built in
2161 after a hail summer, with its lattice copied from the Albatross's envelope (docs/town-story.md).

**What it is:**
- a glass vault on Googie tree columns over the core: Station Square, Main Street, Fountain Cross, the Market Hall
  and the middle of the Promenade (an ellipse of about 280 × 190 m on the plan map);
- the rim sits at 40 m and the top at 58 m. The monorail (26 m) and every building pass under it. Flying lanes and
  the Sky Deck stay outside, over Tower Rise;
- **open at the sides**, so it is a roof, not a box: you walk in and out anywhere, and wind and birds pass;
- its panels slide open on fine days (spring, summer) and close for rain, storms and snow. The Weather Office runs it.

**What it collects:**
- **Rain** runs into the gutter, down the columns and into cisterns at their feet. It waters Greenhouse Hill.
- **Leaves:** in autumn the leaves of the hill woods and street trees land on the glass. They slide into the
  gutter and down a glass leaf chute into a hopper ("leaves to soil"), and robots carry them up to the Compost Works.
- **Kitchen scraps** from every restaurant (players and NPCs) go the same way (the cleaning loop of spec 035).
- **Soil** from the Compost Works feeds the greenhouses. (Proposed for /sim: a town soil stock that nudges the
  supply and price of greenhouse ingredients a little. More scraps and leaves composted means slightly cheaper
  produce next season.)

**What it changes in the game:**
- Under the Canopy, weather never closes outdoor tables. The street stays dry, so footfall is steady.
- Outside it (the Lakefront, the Homes, the Meadow and Arena, the Wreck, Workshop Lane, Greenhouse Hill) weather
  counts in full (section 7): sunny days are great at the lake, and rainy ones empty it.
- So the choice of pitch is a trade-off: steady and expensive under the glass, or cheaper and weather-dependent
  outside it.
- Storms: lightning hits the rods on the Canopy and on the towers, and you see it run across the lattice. It never
  hits people.

**Seasons:**
- Four seasons of **7 game days each** (proposed). That is a 28-day year, about 19 hours of play at 40 minutes a day.
- Deterministic like the weather: from the save's seed and the day number.

| Season | Look | Weather mix | Demand and supply (/sim inputs) | Festival |
|---|---|---|---|---|
| Spring | blossom, petals on the paving, a fresh green hill, the Canopy partly open | mild, showers | salads and greens up; the first garden crops | Blossom Fair (Promenade) |
| Summer | deep green, the Canopy wide open with lattice shadows, parasols | clear, warm haze, storms now and then | cold drinks and light dishes up; the Lakefront at its best | Lake Nights (Lakefront) |
| Autumn | orange and red woods, leaves on the glass and the ground, sweeper robots, the leaf chute | windy, fog, rain | harvest dishes, soups; the most produce at the lowest prices | **Landing Day**, the town's birthday and harvest supper |
| Winter | snow outside the rim, dry and warm under it, bare trees with lights, a snowman on the hill path | snow, cold clear days | hot drinks and stews up; greenhouse produce only, a bit dearer | Winter Lights market in the Wreck |

Rules for older players: seasons change the look and the demand, never the paths. The core is always dry and clear.
Outside it, robots clear the paths of snow and leaves before the morning.

Comfort settings (spec 048):
- lightning flash strength, from full to off (photosensitivity);
- rain loudness;
- less camera rain.

### 8. Performance (the host's PC)
- One scene for the whole bowl. The host simulates the town, and only clients render.
- FishNet observers by distance (interest management): far NPCs send less.
- Ambient life (spec 050) is client-side and deterministic.
- Proposed budget, to be confirmed by spec 049:
  - about 120 simulated NPCs in town;
  - about 40 animated near each player;
  - the backdrop as impostors.

### 9. Build order (each a follow-up spec)
1. **Graybox the bowl:** terrain, district blocks, paths, navmesh, the four edges as blocks, the monorail as a
   teleport-free train.
2. **/sim location model:**
   - footfall per pitch and place, from the NPC arrival points (the station, the pier) and the paths;
   - the day clock and the weather schedule, pure and tested.
3. **Day and night light:** the light moods, the lights switching on, probe scenarios.
4. **Weather visuals:** rain, wetness, fog, lightning, the comfort settings.
5. **The edges dressed:** the Wreck, the Wildbelt with its animals, the lake drone, the tunnel.
6. **Districts dressed** in the order the climb needs them: Main Street and Station Square first, the Lakefront and
   Promenade with stage 3, the Arena, Wreck and Tower Rise with stage 5.

## Acceptance criteria (for the follow-up specs, sketch)
- [ ] AC1: Graybox: a bot walks from Fountain Cross to every core place in 90 s or less at 2.5 m/s, and the slope on
      every path stays below 5 % (an editor test reads the navmesh).
- [ ] AC2: No player can leave the play area. A bot test runs at every edge, and the server returns the bot to a safe
      spot within 2 s.
- [ ] AC3: The /sim weather schedule is deterministic: the same seed and day always give the same weather. Unit
      tests cover every demand effect.
- [ ] AC4: The day clock pauses when the game is not running. A save and reload keeps the time and the weather.
- [ ] AC5: 16 bots in the full town keep the 049 performance budget on the reference host PC.
- [ ] AC6 (manual): an older test player finds the Market Hall, the pier and the Town Hall without a map in 5 minutes.

## Answers (from the human, 2026-11-11)
1. Day length 40 minutes: "sounds good". **Accepted.**
2. Seasons: "yeah season would be nice (but maybe there is a shield over the world to protect against rain and
   collect everything. Leaves are saved to produce soil". **Yes**, worked out as the Canopy in section 7b.
3. The Wreck: "try something". Three tries in round 7 (section 5). The pick is still open (question 10).
4. Wildbelt animals: "yeah lets try". **Yes.** In the story they walked out of the Wreck (docs/town-story.md).
5. Player homes: "yeah". **Yes**, section 4b.
6. The monorail as a real train you ride: "good start". **Accepted** as the first version.
7. Town name: "yeah we need some identity -> backstory". Drafted in docs/town-story.md, with three names (question 11).

Decision lines (D numbers) for these get added to docs/decisions.md when this branch is merged, because main is
ahead and the numbers must not clash.

## Open questions (original list, answered above)
1. **Day length:** 40 real minutes per game day (proposed)? Shorter means more sunsets per evening; longer means calmer
   shifts.
2. **Seasons:** none in v1 (proposed), maybe a winter fair event later?
3. **The Wreck:** an airship (proposed), your crashed plane, or a fallen 2100s "starliner"? And its name.
4. **Wildbelt animals:** do free cows, pigs, deer and hens behind the hedge fit D27 and D28? (Proposed: yes, they
   show the peaceful present, never the past.)
5. **Player homes:** none in v1 (proposed). Later, a shared crew flat above the restaurant?
6. **The town monorail as fast travel:** a real train you ride (proposed), or no fast travel at all?
7. **Town name** (O1 is the game title; the town may need its own name).

## Open questions (round 7)
8. **Season length:** 7 game days per season (proposed)? Fewer days means more festivals per evening of play.
9. **Canopy coverage:** only the core (proposed), so the edges keep their weather? Or the whole bowl?
10. **The Wreck:** A, the Albatross as Ark Flight 9 (proposed); B, the Pelican freight plane; or C, the Stella
    starliner?
11. **Town name:** Seedfall (proposed), Albatross Landing, or Kettlebay? See docs/town-story.md.
12. **Sleeping:** the night skips only when every player is in bed (proposed), or never?
13. **Compost to soil in /sim:** a small effect on greenhouse prices (proposed), or only for the look?

## Implementation notes
(None yet; this is a design draft.)

## Blockers
