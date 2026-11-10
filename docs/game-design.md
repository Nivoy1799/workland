# Worktown game design (v1, 2026-11-10)

Status: v1 after the human's decision (D33–D36), based on the brainstorm and docs/game-design-critique.md. Binding for
every spec once the human approves this text. v0 (persistent town, professions, crews on official servers) is dropped.

## The game in one sentence
**A co-op restaurant simulator for up to 16 friends: start at a food corner with one dish and cash only, and grow it
into a restaurant empire your way.**

## Pillars
1. **The work feels good** (docs/feel.md). Cooking, serving and cleaning stay fun when you are big.
2. **We build it together.** A host starts the game and friends join (1–16). The restaurant, its name, its money and
   its growth belong to the group.
3. **Growth is earned and visible.** Every step costs money and proves skill; the place changes as you grow.
4. **Your restaurant, your mix.** You choose the services you offer and combine them; specialised content comes later.
5. **A peaceful, native-vegan town (D27, D28).** Competition is against NPC teams and in events. No crime, no harm.

## How a game starts and continues
- The host creates a game (a save on the host's PC), and friends join through Steam. Anyone can leave and rejoin.
- **Time runs only while the game runs** (the critique's point 4). Nothing decays while nobody plays, and there is no
  bankruptcy while you are away.
- The save holds the business; players' cosmetics and titles are stored per player.

## The climb (D34)
| Stage | What you have | What you do | How you move on |
|---|---|---|---|
| **1 Food corner** | a cart or counter, **1 dish**, **cash only** | cook and sell that one dish to walk-up guests | save money for the rent |
| **2 Your first restaurant** | rent a place; choose **fast food** as the first service | menu (a few dishes), register with card, stock ordering, cleaning | ratings and money unlock the next service |
| **3 Your mix of services** | add **delivery** (orders by phone or app, a delivery role or NPC courier), **custom orders**, sit-down tables, buffet | combine services as you like; each needs its own stations and roles | a strong mix and good reviews |
| **4 Automation** | hire **NPC staff**, buy machines (auto-fill drinks, dishwasher) | the crew moves from doing everything to running the place | the restaurant runs well with a few players |
| **5 Beyond the kitchen** | **create new ingredients** (a lab, ties into the AI gateway for names); **cooking competitions** against the best NPC teams; **serve at event locations** (stadium, festival); **marketing** (signs, campaigns); **buy cars** (food truck, delivery cars, a car for show) | big, planned jobs with big payouts (the heist feeling) | trophies, fame, the town paper |

Today's build already covers much of stage 2 (kitchen, register, stock, cleaning, tutorial). Stage 1 needs a smaller
start, and stages 3–5 are new.

## The five numbers (unchanged from v0)
Quality, speed, price, location (later: which place you rent), reputation. The critique's economy warnings apply:
- guests choose between your restaurant and the NPC competitors;
- demand per area has a ceiling;
- upkeep rises with size;
- money always has something worth buying (cars, decor, expansion, events).

## Opponents and status
- **NPC rival teams** with names and faces: they react to you, compete for the same guests, and meet you in cooking
  competitions.
- **Visible status:** the place you rent, its look, the cars outside, trophies, the town paper, NPCs talking about you.
- Status is earned, never sold. Buy-to-play, no pay-to-win (cosmetics policy later).

## 16 players
- The kitchen and the restaurant scale with stages: a food corner fits 1–4; a full restaurant with services has work
  for 16: kitchen, front, delivery, events.
- Roles are free, not enforced; the work itself makes roles natural.
- Performance target: 16 players in one restaurant (spec 049, load test in 045).

## Not in the game (D35 and v0 drops)
Persistent town, official game servers, other professions as separate games (mechanic dropped), crime, weapons, PvP
harm, spreadsheet management, pay-to-win.

## How we build it
1. This document approved.
2. **Climb simulation in /sim:** 1, 4 and 16 players, stages 1–5 against NPC rivals. Tune until every stage takes real
   effort, no shortcut, no runaway, money always has a use.
3. **"One great service"** (the critique's milestone 1): stage 1 food corner plus today's kitchen polish, played by
   outside groups.
4. **"The climb":** stages 2–3 with rent, services, NPC rivals.
5. **Early Access:** stage 4 plus the first of stage 5 (one competition, one event location), main menu and joining
   (047), settings (048), stability (049).
