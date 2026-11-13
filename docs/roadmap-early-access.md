# Roadmap: from now to Early Access (rough, 2026-11-13)

A rough plan, not a spec. It lists everything an Early Access (EA) release still needs: game logic, custom assets,
audio, the shippable client and the Steam side. Spec numbers in brackets exist already; **new** means no spec yet.
Built from docs/game-design.md (v1, D37), docs/town-plan.md, docs/art-direction.md and docs/early-access.md.

## Where we are
- **Done (merged):** the kitchen (stations, fryer, griddle, seitan spit, drinks, assembly, packaging, cleaning, stock),
  orders and the register, the tutorial, mouse controls, settings, stability, the title screen with host/join/lobby,
  the 16-player load test, the climb simulation (052/052b), stage 1 food corner (053), stage 2 first restaurant (057),
  the office terminal (059), the layout as data and build mode part A (062a, 062b), the town, revival and clock models
  in /sim (064–066), offline dish names (056), player blocking (054b).
- **Look:** graybox plus Kenney CC0 packs. The real look (art direction v2) has style-lab renders only.
- **Sound:** none. There is no audio file in the project.
- **AI:** off for EA (D38): names and story texts are generated offline and shipped as data.

## The milestones
Each milestone ends in something you can play-test. The order follows the climb (stages 1–5).

### M1 "One great service" (stages 1–2 feel finished)
- 057 review nits (in progress, local cards).
- 062b part B: appliances attach to counters, the joined-run view.
- **new** Audio pass 1: sounds for every station, the register, guests and the UI, plus one music loop. CC0 sounds.
- **new** Stage 2 tutorial hints (the board, rent, the menu).
- Play-test with an outside group (the human). Tune from their notes.

### M2 "Your restaurant" (build, brand, rivals)
- 062c furniture shop and upgrades.
- 060 brand and marketing (name, logo, colours, campaigns).
- 058 NPC rivals (teams with names and faces that compete for the same guests). Netcode-heavy, Opus.
- **new** Rent a location: choose which place you rent (districts from 064, footfall per district).

### M3 "Services" (stage 3)
- **new** Sit-down tables: seating, waiting at tables, dirty dishes and the dish return.
- **new** Delivery: orders by phone or app, packing, a delivery role (bike first) or an NPC courier, delivery routes.
- **new** Custom orders (guests change a dish; ties into assembly).
- **new** Menu growth: 4–6 new dishes across the services, each with its cooking steps (stations exist).
- **new** Reviews and ratings shown in the world (the town paper, NPC lines). Question 27 decides the unlocks.
- Buffet: after EA unless play-tests ask for it.

### M4 "Automation" (stage 4)
- **new** Hire NPC staff: cook, server, cleaner, courier, with wages and skills (deterministic in /sim).
- **new** Machines: dishwasher, auto-fill drinks, a prep machine.
- **new** Refurbished robots as staff (town plan 4.5), shown in the grey look first.

### M5 "Beyond the kitchen" (the first of stage 5)
- **new** One cooking competition against the best NPC rival team (rules, judging, a trophy).
- **new** One event location (a festival on the square): a timed big job with a food truck or a stall.
- **new** The food truck (also the first car). Show cars and delivery cars come after EA.
- Ingredient lab: after EA (it needs live AI names).

### M6 "Haven Nine" (the town around you)
- 067 town graybox (the bowl, 11 districts as blocks, paths, the navmesh, the Dome shell, the borders).
- 050 street life (walking NPCs, guests come from the street).
- **new** Revival in the game: the server's district levels feed footfall and ingredient quality.
- 068 revival looks for Station Square and Main Street (4 looks each).
- 069 day, night and Dome light; Rain Hour and storm days.
- 070 the story in the world (plaques, posters, signs, NPC lines, generated offline).

### M7 "Shippable"
- Save robustness: versioned saves (now v6), autosave, a backup save, the load-error screen.
- Performance on a minimum-spec PC; frame budget with the full town and 16 players.
- Steam: own app ID (the game still uses 480), the Steam join tested on two PCs (open human test), rich presence,
  a few achievements. Cloud saves only if O4 allows it.
- Credits and licences screen, the EA "known issues" and feedback link, an opt-in crash report (human decision).
- English only for EA. Localization and gamepad after EA (decide).

## Custom assets (Blender, after the weekly reset on 2026-11-17)
Spec 040 is the art pass. The style lab (063) sets the look. Every row is a kit, built to docs/art-direction.md.

| Kit | What is in it | Needed for |
|---|---|---|
| Kitchen stations | fryer, griddle, seitan spit, drink tap, sauce dispenser, sink, register, prep board, stock shelf, bin | M1 |
| Counters | modular counter pieces that join (ends, corners, runs), counter tops for each appliance | M1 (062b) |
| Food and cookware | each dish in its readable steps (raw, cooking, done, burnt), pans, baskets, cups, trays, packaging | M1 |
| Food corner | the stage 1 cart or counter, its awning and sign | M1 |
| Restaurant shell | walls, windows, doors, floors, the shop front, the door sign, for build mode | M2 |
| Furniture | tables, chairs, booths, the order screen, decor, upgrade tiers (062c) | M2, M3 |
| Office | the terminal or holo tablet, the goal board | M2 |
| Brand | logo frames, sign blanks, colour sets, posters (060) | M2 |
| Characters | player bodies with clothing choices; guest NPCs; rival teams with faces; staff | M2–M4 |
| Animations | cook, carry, serve, wipe, sit, eat, wait, cheer (Quaternius library covers part) | M1–M4 |
| Delivery | a delivery bike, bags, the phone or app screen | M3 |
| Machines and robots | dishwasher, drink machine, robots broken and working (1950s style) | M4 |
| Events | the food truck, a festival stall, a stage, the trophy | M5 |
| The town | Station Square and Main Street, each in 4 revival looks; the other 9 districts in a simple look | M6 |
| The Dome and borders | Dome ribs and panels, the wall, the Storm Ring view, the Wreck (the Albatross) | M6 |
| Low Ring | the kit in art-direction section 11 | M6 |
| VFX | steam, smoke, oil sparkle, clean shine, rain, the dusk lighting wave | M1, M6 |
| UI | icons for dishes, stations, services; the HUD and board skins | M1–M3 |
| Store art (human) | capsule images, the logo, screenshots, the trailer | M7 |

**Audio** (not Blender): station sounds, guest voices without words, ambience per district and for rain, 3–5 music
tracks, UI clicks. CC0 first; a composer is a human decision.

## What only the human can do
- **Decisions:** O1 title, O10 transport, inbox questions 27, 30, 31, 32, the EA price, the music source.
- **Tests:** the Steam join on two PCs, play-tests with outside groups at M1, M3 and M5.
- **Steam:** pay for the app ID, store page and capsule art, the trailer, the age rating, the EA questionnaire. The
  store page must be visible as "Coming soon" at least 2 weeks before launch; Valve reviews the page and the build
  first (a few days each).

## Rough timeline
A guess at today's pace with a normal weekly budget (5–8 specs a week, the local builder for small cards).

| Weeks from now | What |
|---|---|
| 1–2 | M1 finished, art pass starts (from 2026-11-17), outside play-test 1 |
| 3–5 | M2 and the counter, shell and furniture kits |
| 5–8 | M3 services and M6 town graybox, play-test 2 |
| 8–11 | M4 automation, M5 competition and event, the town looks |
| 11–13 | M7 shippable, Steam page up, the trailer, play-test 3 |
| 13–16 | fixes from play-tests, Steam review, **Early Access** |

So **about 3–4 months**, around **March 2027**. The art pass and the play-test notes are the two big unknowns.

## After Early Access (not needed for EA)
Buffet, the ingredient lab with live AI, show cars and delivery cars, more event locations, seasons and festivals,
the Crew House, the mechanic role (robot repair), going outside the Dome, the other districts' revival looks,
localization, gamepad.
