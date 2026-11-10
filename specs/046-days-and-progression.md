---
id: 046
title: Days and progression (end of day, save, rating, unlocks)
status: draft
owner: architect
builder: opus
---

# 046: Days and progression

## Goal
Early Access needs a reason to come back. A shift ends with a day summary (money, guests, reviews, hygiene). The next
day starts with the money, stock and rating carried over. The shop rating from reviews unlocks menu items and
equipment (bought in the 037 shop). The host's shop is saved to disk and loaded on the next host start.

## Design
- **Day loop:**
  - the shift ends at closing;
  - the summary (the 032 report plus the rating change);
  - "Next day" (the host) skips the night;
  - stock deliveries placed in the evening arrive in the morning.
- **Rating:** 0–5 stars as the rolling mean of the last 20 reviews, shown on the HUD and the shop sign.
- **Unlocks** by rating:
  - 2 stars: falafel and drinks;
  - 3 stars: the spit;
  - 4 stars: a second fryer basket pair;
  - 5 stars: a bigger menu board.
  Locked stations are covered and labelled. Defaults to review.
- **Save:**
  - a versioned JSON in the player's app data (the host only), holding the economy snapshot (sim), the stock,
    plates, rating, unlocks and day;
  - never client data;
  - the load validates the version, and a corrupt save starts a new game with a backup kept.
  - Deterministic: save then load gives the same sim snapshot (test).

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Sim tests for rating, unlocks and save/load round trip.
- [ ] AC3: Server tests: day end, carry-over, locked stations refuse, a corrupt save handled.
- [ ] AC4: Visual check: the day summary, the next day, an unlock.
- [ ] AC5 (manual, human): play two days.
