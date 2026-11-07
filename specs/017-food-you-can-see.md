---
id: 017
title: Food you can see (3D food models for every cooking state)
status: draft
owner: architect
builder: opus
---

# 017: Food you can see

## Goal
Food stops being a coloured cube with a label. A potato looks like a potato, potato sticks look
like sticks, fried sticks look golden or dark, a plate shows what is on it, a bowl shows its mix,
and the pan and pot show what is cooking. This is the first of three steps the human asked for on
2026-11-07 ("the fun part"): **017 food you can see**, then **018 first-person view** (switchable
to third person, interaction aimed in first person), then **019 hands-on cooking** (hold the potato,
take the knife, cut with the mouse), later **020 human characters**.

The server stays the authority. Clients get a small, coarse "look" of each food (which ingredient,
which cut, how cooked, plated or mixed), never stats, quality or exact times (D2, D21).

## Depends on
- Specs 007, 009, 013, 016 (food, plating, bowl, timed pan/pot) and 015 (art swap layer, CC0 rules).
- Kenney Food Kit (CC0, https://kenney.nl/assets/food-kit, `kenney_food-kit.zip`, 4.6 MB),
  downloaded by the orchestrator after approval (Open question 1).

## Scope
- `game/Assets/ThirdParty/Kenney/FoodKit/` (new): only the used models, textures, License.txt;
  one row in `ASSETS.md` (spec 015 rules, `check-assets.sh` must stay green).
- `game/Assets/_Project/Scripts/Cooking/`:
  - `FoodLook.cs` (new, pure): a small value type, see Design.
  - `FoodLookRules.cs` (new, pure): `FoodLook From(Item item)`.
  - `Food.cs` (change: one `SyncVar<FoodLook>` replaces the colour-only display; label stays).
  - `StationContents.cs` (change: a `SyncList<FoodLook>` next to the names, so plates, bowls, pans
    and pots can show their contents as models).
  - `KitchenService.cs` / `KitchenState.cs` (change: publish looks wherever names are published).
- `game/Assets/_Project/Scripts/Art/` (change): food art keys and a `FoodVisual` client component that
  builds the model for a `FoodLook` from the catalog (pooled, no per-frame allocation).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs` (change: food
  wrappers, Food prefab gets a `FoodVisual` child, stations get content anchors).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- First-person view, tools, cutting gestures, characters (specs 018 to 020).
- Real mesh slicing, physics on food, particles, steam, sound.
- New ingredients or operations; balancing.
- Showing quality or stars in the model (the label keeps the stars as today).

## Design
### FoodLook (what clients may know)
```
struct FoodLook {
  byte Ingredient;   // index into DemoIngredients (first component), 255 = unknown
  byte Shape;        // 0 whole, 1 halves, 2 slices, 3 sticks, 4 cubes   (from the cut bucket)
  byte Doneness;     // 0 raw, 1 short, 2 medium, 3 long                 (from the heat time bucket)
  byte Kind;         // 0 single, 1 plated, 2 mixed
  byte PartCount;    // plated/mixed: number of parts shown (max 4), else 0
}
```
- `FoodLookRules.From(Item)` walks the sim item: the last cut gives the shape, the last heat op's
  time bucket gives the doneness, the "plated" tag gives Kind 1, a mix op gives Kind 2. Parts of a
  plate or mix are their inputs' looks (max 4, the rest are not drawn). Only bucket ids and the
  ingredient index leave the server; all values are coarse enums (D21).
- Synced as one small struct per food object; plates and bowls carry their part looks in
  `StationContents` while on the station, and in the food object (`Parts`) once taken.

### Models (Kenney Food Kit, builder picks the closest)
- **Whole:** the kit's potato/apple/tomato/onion models where they exist; else the closest round
  vegetable tinted with the ingredient colour.
- **Cut shapes:** per-ingredient cut models where the kit has them (e.g. tomato slices), else a
  generic piece mesh (slice, stick, cube) instanced 3 to 6 times in a small pile and tinted with the
  ingredient colour. Fries: if the kit has fries, use them for potato sticks.
- **Doneness:** darken the tint by 0 / 10 / 25 / 45 percent for raw / short / medium / long, plus a
  slight golden shift for fried (fry/saute) vs. none for boiled (Open question 3).
- **Plated:** the kit's plate model with up to 4 part visuals arranged on it. **Mixed:** the kit's
  bowl with up to 4 part visuals inside.
- **On stations:** the plating table and bowl show their contents as models on a content anchor;
  the pan and pot show the food inside (Open question 4). Labels stay for names and stars.
- Sizes: food fits a 0.25 m hand-held footprint (plates 0.35 m). The pickup collider stays.
- Fallback: unknown look or missing model gives today's coloured cube, never an error.

### Network
- No new RPC. One extra SyncVar per food (5 bytes) and one SyncList on stations; sent only on change.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets with the Food Kit row).
- [ ] AC2: `FoodLookRules` tests: raw potato = (potato, whole, raw, single); board option 3 =
      sticks; sticks fried 150 sim s = sticks, medium; boiled 30 sim s = short; a plate of fries and
      tomato slices = plated with 2 parts in input order; a mix of 3 = mixed with 3 parts; unknown
      base id gives Ingredient 255.
- [ ] AC3: Reflection test: `FoodLook` has only byte fields (no float, string or stats).
- [ ] AC4: Catalog test: every demo ingredient has a whole model; every shape has a model or the
      generic piece; plate and bowl models exist; wrappers have no colliders or scripts (spec 015 AC7).
- [ ] AC5: Two clients on the dedicated server (screenshots like specs 012 to 016): a potato in the
      hand looks like a potato, cut sticks look like sticks, fried sticks are darker than raw ones,
      a plate with fries and tomato shows both, the pan shows what is cooking.
- [ ] AC6: `WhatsNew.json` entry `spec-017` with test steps.

## Test plan
- EditMode: `FoodLookRulesTests`, `FoodLookSurfaceTests`, `FoodArtCatalogTests`.
- Screenshot run with server and two clients; manual play-test by the human.
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions
1. **Download the Kenney Food Kit (CC0, 4.6 MB)?** Default: yes, the orchestrator downloads it and
   copies only the used models.
2. **Cut shapes:** per-ingredient cut models where the kit has them, generic tinted pieces
   otherwise? Default: yes.
3. **Doneness look:** darkening 0/10/25/45 percent plus a golden shift for fried? Default: yes.
4. **Contents on stations:** show models on the plating table, in the bowl, pan and pot? Default:
   yes (the labels stay).

## Human steps
- Approve this spec and the open questions.
- Play-test AC5.

## Implementation notes
(Builder fills this in.)

## Blockers
