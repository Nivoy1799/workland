---
id: 028
title: Street-food ingredients and the vegan content check
status: approved
owner: architect
builder: sonnet
---

# 028: Street-food ingredients and the vegan content check

## Goal
docs/cooking-roadmap.md sets the menu direction: a vegan street-food shop (seitan, falafel, flatbread, wraps,
fries, salads, sauces, drinks). This spec adds the ingredients those stations will need (specs 029–036) as
buyable, holdable, cuttable items with stats and art. It also does the D27 content audit: the sim still lists
`cheese` at the grocer. A new test blocks animal-product words in every catalog, menu and generated-name list.

Approved by the orchestrator under the human's 24 h grant. The download is approved: CC0 Kenney Food Kit, at most
30 MB, used files only.

## Depends on
- 008/009 (sim items, operations, economy), 017 (FoodLook, food art), 019 (cutting), 026 (sizes: small items
  x1.5, palette).

## Scope
- `sim/Runtime/Cooking`, `sim/Runtime/Economy`: new base items and grocer prices, the cut results; rename or
  remove `cheese`; tests in `sim/Sim.Tests~`.
- `game/Assets/_Project/Scripts/Cooking/` (`DemoIngredients` or wherever crates get their ingredient list),
  `Scripts/Art/` (`ArtKey`, `ArtSizeTable` rows, food looks), `Editor/ArtCatalogBuilder.cs` (defs).
- `game/Assets/ThirdParty/Kenney/FoodKit/`: add the used models (unchanged files) and update the `ASSETS.md` note.
- `docs/art-direction.md` size rows for the new items (the orchestrator commits doc edits).
- New test `ContentRulesTests` (sim and game), `WhatsNew.json`, this spec.

## Out of scope
- New stations: fryer, griddle, assembly and spit come in their own specs. The new items can be bought, held,
  placed, cut and put into the pan or pot like today, and nothing more.
- Recipes and menu dishes using them (they come with the stations).

## Design
**New base items** (id, real size in m, what cutting gives):
- flatbread 0.22 (halves), wrap 0.25 (no cut);
- seitan block 0.12 (strips);
- falafel mix, a bowl-shaped portion 0.10 (no cut; shaped into balls in the fryer spec);
- lettuce 0.18 (shredded), cucumber 0.20 (slices), red cabbage 0.16 (shredded);
- lemon 0.07 (halves);
- garlic-herb sauce bottle 0.20 and chili sauce bottle 0.20 (no cut; dispense comes with assembly).

Stats are plausible flavour values in the existing sim stat space. Every item has a doneness window.

**Crates:** two more crate rows on the back line if space allows, or a "fridge" station graybox with selectable
contents. Keep within the 027a layout rules (FloorPlanTests must still pass); a fridge counts as stock in the dish
flow.

**Art:**
- Kenney Food Kit models where they fit: bread, lettuce or cabbage, cucumber, lemon, sauce bottles, a cheese-free
  model for the seitan block (a cube with a WoodLight/Mustard tint is fine).
- Primitives with palette tints where nothing fits.
- Every model is measured against `ArtSizeTable` (x1.5 small-item rule) before use.

**Vegan content check (D27):**
- `ContentRules.ForbiddenWords`: cheese, milk, butter, egg, meat, beef, pork, chicken, fish, bacon, ham, gelatin,
  honey, cream, yoghurt, plus plurals.
- The check covers every item id, display name, grocer entry, menu dish name, name-catalog entry and
  `WhatsNew.json` text. It ignores the word inside other words (for example "eggplant" is fine).
- `cheese` becomes `vegan_cheese` (display "cashew cheese"), or is removed if nothing uses it.
- The egg model may stay as the potato's art stand-in. Art model file names are not content.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (sim tests and Unity EditMode).
- [ ] AC2: Sim tests: every new item exists with valid stats and doneness; cutting gives the listed result;
      grocer prices exist; determinism tests unchanged.
- [ ] AC3: `ContentRulesTests` fails on a planted forbidden word (meta-test) and passes on the real content.
- [ ] AC4: Art tests: every new item key has an `ArtSizeTable` row and is built within ±15 %.
- [ ] AC5: Visual check `look` sheet shows the new crates or fridge with readable items. The cook tour can buy
      and cut a cucumber (extend it).
- [ ] AC6: `WhatsNew.json` entry `spec-028`.

## Defaults to review
Prices (comparable to today's grocer: 40–120 each), stats, the fridge vs. crates choice, "cashew cheese".

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
