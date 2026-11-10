---
id: 043
title: Cooking consistency (perfect means best, names match the windows, one pace)
status: approved
owner: architect
builder: opus
---

# 043: Cooking consistency

## Goal
Reviews found the same pattern on every heat station:
- the best quality comes at the end of the good window, not at the "perfect" cue (spit 70 s vs 55 s; the fryer and
  griddle the same);
- names and menu buckets start before the good window (the griddle's "grilled" at 9 s, the window at 12 s), so an
  undercooked plate can be an exact order.

Players learn the cue, so the cue must be the truth.

## Design
- **Perfect is best.** The rating's doneness term peaks exactly at the Perfect zone. Good scores a little less, and
  overdone falls off. This applies to the pan, pot, fryer, griddle and spit.
- **Names and menu buckets follow the zones.** "Golden", "grilled" and "cooked" start where Good starts. Undercooked
  food is named "pale" or "half-grilled" and matches an order only as Close.
- **One pace table** (docs/cooking-roadmap.md): every station's Good, Perfect and Burnt in one file, with the inbox's
  "2x faster" default.
- Old hashes change, because the buckets move. All pinned values are re-pinned with a written reason.

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Sim tests per station:
  - the quality maximum lies inside Perfect;
  - the name boundaries equal the zone boundaries;
  - an undercooked order is Close, not Exact.
- [ ] AC3 (manual, human): shave or raise at "perfect" and get the best stars.
- [ ] AC4: `WhatsNew.json`.
