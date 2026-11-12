---
id: 050
title: Street life and world storytelling (the world explains itself, no dialogue)
status: draft
owner: architect
builder: opus
---

# 050: Street life and world storytelling (DRAFT, not decided)

> **Partly superseded by the Haven Nine specs (2026-11-11).** Signs and posters, the museum and statue texts, the
> short exclamations and robot lines move to **070** (story in the world). Street robots as part of the town's look
> (slumped or working) and people density move to **068** (revival looks). Location, footfall and walking times are
> **064**. What stays here: the food printer moment, the coin ritual, the rankings board's flip-letter dressing, rival
> stall fans and pennants (058 builds the working board and fronts), the start cart, and ambient moving robots
> (sweepers, carriers, delivery capsules). The setting is now docs/town-plan.md and docs/town-story.md (D41), not
> the clean-utopia draft of docs/setting.md.

## Goal
Make the setting (docs/setting.md, draft) understandable without dialogue, as storyboarded in
docs/storyboard-first-minutes.md. Waits for the setting to be decided (D37) and for spec 040's models.

## Scope (sketch)
- **Street robots:** sweepers, carriers, waterers, flying delivery capsules and pneumatic tubes; ambient, client-side,
  deterministic paths.
- **Food printer:** NPCs take a free bar, shrug and eat; some walk past it to your stall.
- **Coin ritual:**
  - guests pay with a coin purse animation, clinking coins and a cash-box sound;
  - a tip jar that fills;
  - a chrome coin machine on the street.
- **Rankings board** (flip letters) showing the shop's rank among NPC rival teams; rival stalls with fans and pennants.
- **Short exclamations** (2–4 words, speech bubbles plus a sound, rare and not overlapping): a small pool, tested for
  content rules (D27, D28).
- **Signs and posters:** "Free nutrition bar" against "Real food, made by hands"; ingredient crates named after
  farmers; the town paper headlines.
- **The museum of the old era** and a statue (D28: own history only).
- **The start:** a dusty food cart and a robot handing you the knife (ties into the food corner stage 1).

## Acceptance criteria (sketch)
- [ ] AC1: `verify.sh`; all ambient life is client-side or cheap; the 16-player performance budget (049) holds.
- [ ] AC2: Visual check: every storyboard shot reproduced as a screenshot.
- [ ] AC3 (manual, play-test with outsiders): after 5 minutes, most testers can answer "Why does this person cook when
      there are robots?" and "Why does the guest pay in cash?"

## Open questions
See docs/setting.md.
