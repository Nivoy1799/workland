---
id: 044
title: Feel polish 3 (the open "feels off" lists of specs 027b-038)
status: approved
owner: architect
builder: opus
---

# 044: Feel polish 3

## Goal
Work through the "feels off" and review follow-up lists at the end of docs/cooking-roadmap.md that no later spec
covers. The biggest items:
- other players see food fly into pots, and hear put-downs;
- buttons sink when pressed;
- sprint reaches full speed in 0.1–0.15 s;
- the scoop and the tip are predicted;
- the third-person carry and wipe poses;
- eat-in guests leave without carrying their plate (tables come later);
- the tutorial card no longer covers the view;
- the order bubbles are not hidden behind the ticket rail;
- the rail cards are readable from below.

Each item gets a test or a visual-check shot. Items already covered by 039–043 are skipped.

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Every list item is fixed, or moved with a reason.
- [ ] AC3: Visual check sheets for each item.
- [ ] AC4: `WhatsNew.json`.
