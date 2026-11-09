---
id: 036
title: Seitan spit (rotating, outer layer cooks, shave slices with the knife)
status: approved
owner: architect
builder: opus
---

# 036: Seitan spit

## Goal
The signature station of the vegan street-food shop (docs/cooking-roadmap.md): a vertical rotisserie with a large
seitan cone that turns in front of a heat panel. The outer layer browns over time. You **shave** thin slices with the
knife (hold to shave). Fresh slices fall into a catch tray, and you scoop them into a wrap or flatbread on the
assembly board. Shave too early and the slices are pale; leave the outside too long and it chars. Shaving exposes the
next layer, which starts cooking again. It is the most "genre" moment of the shop. Approved by the orchestrator under
the human's 24 h grant.

## Depends on
019 (knife, cutting gestures), 027b/c (feel, rings, cues), 031/031b (assembly layers, hold patterns, money pattern),
034 (stock: the seitan cone is a stock item ordered at the terminal, about 30 portions per cone).

## Scope
- `sim/Runtime/Cooking` (pure):
  - a spit model: a cone with layers;
  - the outer-layer doneness by heat time;
  - `shave` (an amount from the server-measured hold) produces "shaved seitan" with that doneness;
  - after a shave the outer layer resets toward the inner one (partly cooked);
  - the cone runs out after N portions.
  - Deterministic; old hashes and ratings unchanged.
- `game/Assets/_Project/Scripts/Cooking/`:
  - station kind `Spit` with a heat knob, a cone mount (load a new cone from stock) and a catch tray (a fixed vessel);
  - shave = holding the knife on the cone (press/release through the existing pattern, server-timed, 0.2 s gate in
    third person);
  - the tray holds up to 3 portions, and a scoop takes one portion into the hand or onto a held build (a 031 layer);
  - the 027c ring for the cone's outer layer;
  - no new RPCs, presentation bytes pinned.
- `Scripts/Art/`:
  - the turning cone (graybox stacked discs in WoodLight/WoodDark, shrinking as it is used);
  - a browning colour per outer layer;
  - a glowing heat panel;
  - shaving curls falling into the tray;
  - the knife motion while shaving.
- `Scripts/Audio/`: a sizzle by heat, a shave rasp, a motor hum.
- Menu: "Seitan wrap" and "Seitan flatbread" (031) take shaved seitan (spit) or grilled strips (griddle), both
  valid, but the review mentions which one.
- Layout: the spit stands in the heat block, its knob and cone facing the aisle. FloorPlanTests stay green.
- Tests, visual check (load a cone, heat, shave at the right time, scoop into a wrap, serve), `WhatsNew.json`, this
  spec.

## Out of scope
- Cone preparation (stacking a raw cone from seitan). A rotisserie for vegetables.

## Design notes
- The outer layer: good at 40 s on high heat, perfect at 55 s, burnt at 90 s. After a shave the new outer layer starts
  at 50 % of the good level.
- A 1 s hold is one portion (about 60 g). The hold amount maps to portions in steps, measured on the server.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: layer doneness, shave, layer reset, running out, determinism.
- [ ] AC3: Server tests:
  - shave timing (the re-press rule);
  - the tray capacity;
  - the scoop onto a build;
  - loading a cone from stock (charged at order time only, refusals change nothing).
- [ ] AC4: Visual check: the turning cone browns, the shave curls fall, a wrap with shaved seitan is served.
- [ ] AC5: `WhatsNew.json` entry `spec-036`.

## Defaults to review
Timings, portion size, 30 portions per cone, the cone price at the terminal.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
