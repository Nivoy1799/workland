---
id: 046
title: Town time, shifts and progression (clock in and out, player levels, shop reputation)
status: draft
owner: architect
builder: opus
---

# 046: Town time, shifts and progression (v2, after the human's feedback)

## Why v2
The human (2026-11-10): "how would this work, on the server do all players have the same end of day? Think a little
more. Sure we need some gratification, but is this really it?" A global "end of day, skip the night" does not fit a
shared town where players come and go and other professions keep running. So time belongs to the town, the shift
belongs to the player, and progress has three loops.

## Design
**1. One town clock, never skipped.**
- The server runs the town clock all the time (default: 1 game day = 48 real minutes; night is shorter).
- Shops have opening hours on their sign (owner's choice).
- A shop with nobody clocked in is closed: guests go elsewhere, and the economy's demand (/sim) moves to other shops
  or NPC businesses.

**2. Clock in and clock out (your personal shift).**
- You clock in at the shop's time clock. Your shift summary appears when you clock out, or when you leave or
  disconnect.
- The summary covers your earnings, tips, orders, perfect dishes, best streak, XP gained, reviews that name you, and
  hygiene during your shift.
- Several players clock in and out independently, so nothing waits for the others.

**3. Gratification in three loops.**
- **Seconds (every order):** the perfect cue, stars, tip, coin sound, and a **streak** for perfect orders in a row
  (a small tip bonus, a visual flourish).
- **Your shift:** the clock-out summary (above), plus level-up moments.
- **Long term, two tracks:**
  - **Player (the person, follows you everywhere):**
    - profession XP and levels per craft (cook now; later the mechanic and others);
    - levels unlock techniques (the spit, combo orders, faster hands), recipes, titles and outfits.
  - **Shop (the business):**
    - reputation from reviews brings more and better-paying guests;
    - upgrades are bought with shop money through the 037 shop (more fryer baskets, an auto-fill drink machine, decor);
    - later, owning or renting a shop (docs/ideas.md).

**4. Money and roles.**
- The shop's cash belongs to its owner (the host in co-op).
- Helpers get a wage per shift plus their tips, in their own wallet.
- The owner sets the wage (default: a fixed rate per game hour).
- This leads into the "manage your own restaurant" idea.

**5. Persistence (one model, two stores).**
- The rules for clock, shifts, XP, reputation and wages live in /sim (pure, deterministic, tested).
- **Store A, now:** a host save on the host's PC (co-op sessions). It holds the shop and the host's player. Visiting
  players keep their player progress in a local profile until the backend exists.
- **Store B, later:** the backend (O4 open) for official persistent servers; the same /sim state.
- Never trust client data for money or XP (rule 6): the server computes, and a local profile is only read back by the
  same player's own host.

## Open questions (defaults in bold; also docs/human-inbox.md)
1. Game mode for EA: **co-op sessions now (host-owned shop), persistent town later with the same rules**, or
   persistent town first (needs O4)?
2. Progress tied to **player and shop** (both tracks), only the shop, or only the player?
3. Town day length **48 real minutes**?
4. Helper wage default **fixed per game hour, owner can change it**?

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Sim tests: clock, shift summary, XP and levels, reputation, wages, streak, save round trip.
- [ ] AC3: Server tests: independent clock in and out, closed shop, wages paid once, disconnect ends the shift.
- [ ] AC4: Visual check: clock in, shift, clock out summary, a level-up.
- [ ] AC5 (manual, human): play two shifts with a friend, clocking in at different times.
