---
id: 037
title: Street-food prices and a menu board (rescale the economy, set your own prices)
status: approved
owner: architect
builder: opus
---

# 037: Street-food prices and a menu board

## Goal
Inbox question 7, default A: today a wrap costs about $1,525 and a guest pays with a $1,900 note. That reads like a
luxury restaurant. This spec rescales all money to street-food values and lets the cook set dish prices on a menu board.

## Design
- **Cents internally.** All money in the sim and game is integer cents.
  - A wrap is about $9.00, fries $4.00, a drink $3.00.
  - Ingredients cost $0.30–2.00, packaging $0.05–0.20.
  - The player starts with $200.
  - Bulk, portion, floor, SurplusCap and anti-faucet rules carry over, scaled. Every pinned number is re-pinned with a
    written reason.
- **Cash and change.** Coins are 0.10, 0.20, 0.50, 1 and 2; notes 5, 10, 20. Change stays exact in cents (032 rules).
- **Menu board** (a station above the counter): the cook sets each dish's price in steps (±$0.50).
  - Guests compare it with a market reference: too expensive means fewer guests and lower tips; cheap means more
    guests and less money.
  - Deterministic demand curve in the sim.
- **Shop rating** from reviews unlocks more menu items over the days (the roadmap pillar 6 start).
- Existing guarantees stay: pay once, refunds, money from stock orders.

## Acceptance criteria
- [ ] AC1: `verify.sh`; RPC surface tests unchanged.
- [ ] AC2: Sim tests:
  - cents everywhere;
  - anti-faucet for every item at the new scale;
  - the demand curve;
  - determinism (every pinned value re-pinned with a reason).
- [ ] AC3: Visual check: the board, a guest paying $9.50 with a $10 note, change counted in coins.
- [ ] AC4 (manual, human): prices feel like a street-food shop.
- [ ] AC5: `WhatsNew.json`.

## Defaults to review
Every price above; ±$0.50 steps; the reference price is the start price; rating unlock thresholds.
