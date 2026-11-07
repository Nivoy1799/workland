---
id: 017
title: Food you can see (3D food models for every cooking state)
status: approved
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
- [x] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets with the Food Kit row).
- [x] AC2: `FoodLookRules` tests: raw potato = (potato, whole, raw, single); board option 3 =
      sticks; sticks fried 150 sim s = sticks, medium; boiled 30 sim s = short; a plate of fries and
      tomato slices = plated with 2 parts in input order; a mix of 3 = mixed with 3 parts; unknown
      base id gives Ingredient 255.
- [x] AC3: Reflection test: `FoodLook` has only byte fields (no float, string or stats).
- [x] AC4: Catalog test: every demo ingredient has a whole model; every shape has a model or the
      generic piece; plate and bowl models exist; wrappers have no colliders or scripts (spec 015 AC7).
- [x] AC5: Two clients on the dedicated server (screenshots like specs 012 to 016): a potato in the
      hand looks like a potato, cut sticks look like sticks, fried sticks are darker than raw ones,
      a plate with fries and tomato shows both, the pan shows what is cooking. Confirmed by the human play-test on 2026-11-07.
- [x] AC6: `WhatsNew.json` entry `spec-017` with test steps.

## Test plan
- EditMode: `FoodLookRulesTests`, `FoodLookSurfaceTests`, `FoodArtCatalogTests`.
- Screenshot run with server and two clients; manual play-test by the human.
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions (answered 2026-11-07: human approved all defaults)
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
- Built: `Cooking/FoodLook.cs`, `FoodLookRules.cs`, `FoodLookSerializer.cs` (explicit FishNet writer/reader,
  6 bytes; the compiled assembly uses it, no codegen fallback), `Art/FoodArtRules.cs` (pure: model, pile,
  tint, layout), `Art/FoodPiece.cs`, `Art/FoodVisual.cs`; 16 new `ArtKey`s (`Food*`), 16 wrappers in
  `Prefabs/Art/`, `Art/FoodPiece.mat`. Tests: `FoodLookRulesTests`, `FoodLookSurfaceTests`,
  `FoodArtCatalogTests` (32 new, 228 total, all green).
- Food Kit: 12 models unmodified (egg, apple, tomato, onion, apple-half, onion-half, tomato-slice, fries,
  plate, bowl, frying-pan, pot), `Textures/colormap.png`, `License.txt`; manifest row added.
- Mapping (ingredient x shape; "generic" = Unity primitive with a white material, tinted with the
  ingredient colour; own = kit colours):

  | | whole | halves | slices | sticks | cubes |
  |---|---|---|---|---|---|
  | potato | egg, tinted (stand-in) | generic half x2 | generic slice x4 | generic stick x5; fries (own) once fried to medium or longer | generic cube x6 |
  | apple | apple (own) | apple-half x2 (own) | generic slice x4 | generic stick x5 | generic cube x6 |
  | tomato | tomato (own) | generic half x2 | tomato-slice x3 (own) | generic stick x5 | generic cube x6 |
  | onion | onion (own) | onion-half x2 (own) | generic slice x4 | generic stick x5 | generic cube x6 |

  Generic half = flattened sphere, slice = flat cylinder, stick = long box, cube = box. Plated: plate
  (0.35 m) + up to 4 parts at half size; mixed: bowl (0.3 m) + parts. Whole food 0.16 to 0.18 m, fries
  0.22 m. `HandHeldPiles_FitTheFootprint` builds every ingredient x shape (raw and fried long) with
  `FoodVisual` and checks the drawn bounds stay under 0.25 m, and a full plate under 0.35 m.
- Stand-in: the kit has no potato; the egg tinted potato brown is used for the whole potato.
- Potato sticks are generic potato-tinted sticks while raw, boiled or lightly fried, and become the
  kit's fries model only once fried (fry or saute) to the medium bucket or longer (`FoodArtRules.IsFries`),
  so raw sticks never look like finished fries. The fries model then gets the normal darkening (25 / 45 %).
- `egg-half.fbx` was considered for potato halves and not used: it is a cracked egg shell with a jagged
  rim and a yolk, which reads as an egg cup even when tinted. Potato halves stay the flattened sphere.
- Deviation: `FoodLook` has a sixth byte `Heat` (0 none, 1 fried/sauteed, 2 boiled). The spec's five
  bytes cannot tell fried from boiled, which the golden shift (open question 3) needs. Still bytes only.
- Stations: the builder places a static tool model on top of the furniture (`Tool_FoodPlate`,
  `Tool_FoodBowl`, `Tool_FoodPan`, `Tool_FoodPot`) and a `Contents` anchor with a `FoodVisual` that draws
  the parts. The bowl stands at x -0.32 next to the spec 015 blender. The pan/pot content look follows
  the doneness bucket while cooking (`KitchenState.HeatLook`, the same bucket the label already shows).
- `FoodVisual` rebuilds only when a look changes (dirty flag, applied in `LateUpdate`), pools instances
  per prefab, caches catalog lookups, and is skipped on a server-only instance (dedicated server draws
  nothing). Unknown ingredient or missing model shows the old coloured cube (now the fallback renderer).
- Ingredient of a plate or mix is the first sim component (sorted by base id). The old colour byte
  plumbing was removed (review L1): `AssemblyEntry.Color`, `HeatSession.Color`, the `color` parameters of
  `TryAddToStation` / `TryPutOnHeat` and the `color` out params of the take methods; tests adjusted (the two
  colour asserts went with the feature). "Input order" for parts is the item's `Inputs` order, which the
  sim sorts canonically (by hash) for plate and mix: fries + tomato slices come out tomato first; the
  station keeps the order added. Tests assert the concrete looks in that order.
- Mixes (review M1): a mix that was not cut as a whole takes its first part's shape, so it never draws as a
  whole ingredient (in the hand as a part, in the pan or pot, on a plate). Heat on the whole mix is carried
  onto each part when it is more done than the part, so a fried mix shows fried parts. A mix on a plate is
  one part (Kind mixed) drawn as a pile of its first ingredient in that shape, with the fried tint.
- D21 (review M2): `Food.Quality` (synced float) was removed; clients get only `Stars`. The server keeps
  using `KitchenState.QualityOf` for stars, logs and selling. No client code read the float.
  `FoodLookSurfaceTests` now allows only string, int and `FoodLook` in synced members of `Food` and
  `StationContents`.
- Spawning (review L5): `SpawnFoodInHands` publishes name, stars and look before `Spawn`, then registers the
  item and hands it over (unchanged failure path), so the values arrive with the spawn and a pooled object
  never shows its old model or the cube for a tick.
- Batch build: FishNet logs its known "same assetPath hash of 0" errors while prefabs are saved; the full
  refresh right after fixes them (unchanged behaviour). The `DefaultPrefabObjects.asset` reorder was reverted.
- Needs the visual check (AC5): orientation of apple-half / onion-half / tomato-slice and the pan handle,
  whether the pan and pot sit on the stove tops (bounds may include a backsplash), the egg stand-in tint,
  darkening on textured models, food sizes in the hand, bowl next to the blender. All are single numbers
  in `ArtCatalogBuilder.Defs`, `FoodArtRules` or `GrayboxSceneBuilder.AddStationContents` calls.

## Blockers
