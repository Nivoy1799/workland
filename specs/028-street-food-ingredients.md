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
Builder (Sonnet), spec 028.

**Sim** (`sim/Runtime/Cooking`): `StreetFoodItems` (ten `BaseItemDef`s, ids flatbread, wrap, seitan, falafel_mix, lettuce, cucumber, red_cabbage, lemon,
garlic_herb_sauce, chili_sauce; `CutShape(id)`) and `ContentRules` (`ForbiddenWords`, whole-word `FindForbidden`, plurals; "eggplant" is fine).
Grocer prices in `EconomyCatalog.CreateDefault`: lettuce 45, cucumber 50, red cabbage 55, flatbread 60, lemon 60, wrap 70, garlic-herb sauce 80,
chili sauce 80, falafel mix 90, seitan 110.

**Deviations and decisions**
- **`cheese`** is removed, not renamed: the spec's own forbidden list contains "cheese", so "vegan_cheese" / "cashew cheese" would fail the new check.
  The grocer entry (150) is now `cashew`, and the sim test fixture `cheese` is now `cashew` (same stats; sort order unchanged). `TestEconomy.PlatedTomatoCheese`
  stays as an alias of `PlatedTomatoCashew` because `DeterminismTests` must stay unchanged and calls it.
- **Cuts.** The sim cut op has halves, slices, sticks, dice only. "Shredded" (lettuce, red cabbage) = slices, "strips" (seitan) = sticks;
  `StreetFoodItems.CutShape` says which. A real shred bucket would change the op catalog (follow-up with the assembly/slicing specs).
- **Sizes.** Items over 15 cm (flatbread, wrap, lettuce, cucumber, red cabbage, sauce bottles) are class World (real size, no x1.5), because
  `FoodArtCatalogTests` limits a held item to a 0.25 m footprint; seitan block 0.12, falafel mix 0.10, lemon 0.07 are Small (x1.5).
- **Crates: a bank, not a fridge.** Ten narrow crates (0.52 m wide, 0.56 m apart) between the landing worktop and the four old crates. The kitchen is
  5.6 m longer at the left (KitchenMinX -7.6); the four crates, board, stoves, counter and everything right of them keep their positions.
  FloorPlanTests pass with the crate count generalised from 4 to `DemoIngredients.All.Count` (14). Cost: the walk from the first bank crate to the
  board is about 8 m (dish-flow limit only measures the last crate). A fridge with selectable contents would need new netcode; left out.
- **Tour points moved** (the wall moved): `TourRules.ParkX` -10.8, observer exit x -8.9; the look tour has two new shots (`stock-third`, `stock-first`).
- Every crate now shows two or three of its food models sticking out of the box (`CreateCrateSample`), so the items read at a glance.
- Stand-in models (patty for seitan, round loaf for falafel mix, eggplant for cucumber, cabbage for red cabbage, mustard bottle for garlic-herb sauce)
  use the flat white piece material, so the ingredient colour tints them cleanly (`ArtCatalogBuilder.FlatFoodDef`).
- Test updates besides new tests: `FloorPlanTests` (crate count), `ArtCatalogTests` (crate x and width, bank), `TourRulesTests` (look shots),
  `WhatsNewTests` (spec 027d no longer the newest), sim `EconomyCatalogTests` (18 grocer entries).
- Cook tour (`CookSequence.CutCucumber`): buys a cucumber from the bank, puts it on the board, takes the knife, two strokes, ends with "cucumber slices".

## Blockers
(Builder fills this in after 3 failed attempts.)
