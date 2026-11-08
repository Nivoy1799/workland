---
id: 026
title: Scale and palette pass (apply the visual guide to the whole world)
status: done
owner: architect
builder: opus
---

# 026: Scale and palette pass

## Goal
The human: "colors, asset sizes are all random". This spec makes every catalog asset match the
size table in `docs/art-direction.md` (D29) and replaces every hard-coded colour with the warm
muted palette (D32). Tests then keep it that way: a new asset that does not fit its size class, or
a new `new Color(...)` outside the palette, fails `verify.sh`.

## Depends on
- Spec 025 (characters at 1.75 m, the reference for everything else). Build after 025.
- Specs 015/017/019/021/023 (art catalog, food, tools, cookware, worktops).
- No downloads.

## Scope
- `game/Assets/_Project/Scripts/Art/` (new `Palette.cs` (named colours), new `ArtSizeTable.cs`
  (size class and target metres per `ArtKey`, mirroring the guide); change: `FoodArtRules`,
  `StoveButtonVisual`, `VesselSteam` use the palette).
- `game/Assets/_Project/Scripts/` UI, Npc, Player, Social files that hold `new Color(...)`
  (change: palette names only; today `HudView`, `ShiftSummaryView`, `TicketRailView`, `Customer`,
  `InteractionHighlight`, `PingMarker`).
- `game/Assets/_Project/UI/` (new `Palette.uss` variables; change: `Worktown.uss` uses them).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs` (change: fit every def by its target size from
  `ArtSizeTable`, no free scale factors; worktops built from repeated 0.6 m cabinet modules instead
  of one stretched cabinet).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: station and worktop height 0.90 m,
  serving counter 1.05 m, place surfaces follow; materials from the palette).
- Tests under `game/Assets/_Project/Tests/EditMode/`, `WhatsNew.json` entry, this spec.

## Out of scope
- New models, new packs, new rooms or a new kitchen layout (positions stay; only heights and sizes change).
- Lighting changes beyond the colour of the existing lights.
- Label overlap above the stove and the queue (own small spec later).
- Gameplay rules: capacities, reach distances, place grid (0.25 m) stay. If a rule depends on a
  height that moves (for example a reach test from the eyes to a surface), the rule's numbers stay
  and only the scene moves; failing tests are reported, not silently retuned.

## Design
**Size table (`ArtSizeTable`).** `SizeClass { World, Cookware, Small }` with factors 1.0 / 1.15 /
1.5 and a real-world size per key; `TargetSize(key) = real x factor`. Values from the guide, for
example FoodApple 0.08 → 0.12, FoodPan 0.45 → 0.52, FoodPot 0.30 → 0.35, ToolKnife 0.30 → 0.35,
BuildingHouse* 7.5 m to the ridge, PropTreeLarge 8 m, PropLamp 4.5 m. Pieces (half, slice, stick,
cube) follow the same rule from their real size. The guide and this table must agree; the test
reads both (the guide's table rows are parsed by key name).

**Fitting.** `ArtCatalogBuilder` fits each def to `TargetSize(key)` along its largest dimension
(food, cookware, tools, props) or its height (buildings, trees, people, furniture). Stations keep
their footprint but the furniture is 0.90 m high. Worktops: N cabinet modules of 0.60 m along the
run (the last one scaled to fill at most ±0.15 m), so the texture is no longer stretched.

**Heights.** `StationHeight` 1.0 → 0.90 m, worktops at the stove top (0.90 m), the serving counter
1.05 m. Place surfaces, vessel colliders and labels follow the new tops (they are computed from the
models today).

**Palette.** `Palette` is a static class of `static readonly Color` fields named as in the guide
(Cream, Plaster, WoodLight, ... Interact) plus `Palette.Customer(int index)` for customer tints.
`Palette.uss` defines the same names as `--wt-cream` etc.; `Worktown.uss` uses only those variables.
Food colours in `FoodArtRules` are moved to the palette as `FoodPotato`, `FoodTomato`, ... (natural,
saturation at most 70 %).

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes.
- [x] AC2: `ArtSizeTests`: every `ArtKey` with a catalog prefab has a row in `ArtSizeTable` and its
      built bounds are within ±15 % of `TargetSize`; the guide's size table and `ArtSizeTable` agree.
- [x] AC3: `PaletteTests`: the palette has the 16 guide colours with the guide's hex values; no
      `.cs` file under `game/Assets/_Project/Scripts` other than `Art/Palette.cs` contains
      `new Color(` or `new Color32(`; no `.uss` file other than `Palette.uss` contains a `#` hex
      colour or `rgb(`.
- [x] AC4: Scene test: worktops, stove tops and place surfaces at 0.90 m ±0.02, serving counter at
      1.05 m ±0.02; worktops are made of 0.6 m modules.
- [x] AC5: All existing interaction, cookware and order tests still pass unchanged.
- [x] AC6 (2026-11-09, local host: kitchen third person, first person demo with pan and pot, crates at 0.9 m; WSL path blocked by MTU 1280, see docs/human-inbox.md): Screenshots (WSL server, clients): kitchen in third person with a person for scale; first
      person holding the pan (no longer covering a third of the view); the street with houses and trees.
- [ ] AC7 (manual, human; merged under the 24 h grant, open for the digest): sizes feel right, colours belong together, nothing floats or sinks.
- [x] AC8: `WhatsNew.json` entry `spec-026`.

## Open questions (answered 2026-11-08: human approved all defaults)
1. Kenney textures that clash with the palette (for example the bright cabinet doors): **recolour
   with a palette tint** (yes) or leave the original texture colours?
2. Graybox fallback primitives (slots without a model): **Plaster** for walls and floors, **Steel** for
   machines (yes), or a separate debug colour so missing art stands out?

## Test plan
- EditMode: `ArtSizeTests`, `PaletteTests`, a scene height test (new); all existing tests unchanged.
- `UNITY_PATH=... bash scripts/verify.sh`, rebuild scene and builds, WSL server, screenshot clients.

## Human steps
- Approve the spec and the answers to the questions.
- Play-test (AC7).

## Implementation notes
Builder, 2026-11-08, revised after review. `verify.sh` green, EditMode 598/598 (baseline 545 + 53 new).

**What was built**
- `Art/Palette.cs` (16 guide colours, `FoodPotato/Apple/Tomato/Onion/Golden` at most 70 % saturation, `Customer(int)`,
  `WithAlpha`, `Shade`, `Hex`), `UI/Palette.uss` (`--wt-*`: exactly the 16; the What's-new backdrop, 35 % Ink, is set by `WhatsNewView` with
  `Palette.WithAlpha`, the selected wheel slot is plain Mustard).
  `Worktown.uss` imports it and uses only variables; `WorktownTheme.tss` imports it too, so the variables sit at the panel root.
- `Art/ArtSizeTable.cs` (+ `ArtSize`, `SizeClass`, `SizeAxis`, one type per file): one row per catalog key, `TargetSize`,
  `WorktopHeight` 0.90, `ServingCounterHeight` 1.05, `WorktopModules(length)`. `ArtCatalogBuilder` has no free scale factors
  left. Every model is built exactly at its target (largest error 0.000 m, see the `[Art] ... size` log lines).
- Stations: the footprint is fitted into 1.2 m (uniform in XZ) and the height set to the target, so the furniture is squashed
  non-uniformly (cabinet and stove 1.147 x 0.90 x 1.20 m, 1.33:1). Kenney furniture has flat colours, so nothing stretches a texture.
- Worktops: n = max(1, round(L / 0.6)) equal cabinet modules (`Art_Worktop` is one 0.6 x 0.9 x 1.2 m module), named
  `Module n`, so any run length builds. Module widths per run: 0.62 / 0.63 / 0.587 / 0.63 / 0.62 m.
  The collider, the body cube and the `PlaceSurface` (0.25 m grid from `_size`) stay one piece per run.
- Heights: stations 0.90 m and the counter 1.05 m (body, collider, root at half height). Stove tops, worktops, burners and
  every place spot are at 0.900 m. The stove knob row keeps its place on the model (fractions of the old 1.1 m stove).

**Deviations (please check)**
1. *Street positions changed.* At 7.5 m the houses are up to 11.7 x 9.3 m, so at the old x positions they overlapped each
   other and the dumpster. The ground also ends at x = 50. New positions: HouseA x 28 to 31.5, HouseC x 35 to 43, and HouseF
   south of the road at (44.5, -0.5), facing north, east of the crossroad's south arm (x 34 to 39), which stays open. The east
   lamp moved from x 38 to 33 (its post stood in that arm) and the dumpster from x 24.2 to 24.5 (it was 3 cm inside the shop).
   The wrappers are centred now, so the lamps are placed by their bounds (the back of the post at z 4.6, arm over the road)
   and the awning by the shop's front (back edge 2 cm into the wall, bottom at `AwningMountHeight` 2.3 m). A scene test checks
   buildings and props (lamps, trees, dumpster, trash can, planters, fence, awning) against each other's buildings, the road,
   the customers' path and the south arm (lamp posts only; the arms hang over the road), the awning on the shop's front, and the
   ground. This goes against "positions stay"; it needs a human OK or another layout.
2. *Roads stay 5 m tiles* (two 2.5 m lanes) instead of the guide's 3.0 m lane. A 6 m road would need a new street layout.
3. *Guide rows* (resolved): `ArtSizeTable` names each key's guide row (`GuideRow`, plus `GuideIndex` for "4 / 8"); the test
   parses the guide's Thing / Real / In game columns by that name and checks both values and the class factors. Planter,
   Dumpster, Trash can (height), Fence section and Awning now come from the guide rows the coordinator added. Only the road
   tiles (5.0) and the generic pieces (half 0.08, slice 0.07 x 0.01, stick 0.09, cube 0.03 real) have no guide row.
4. Worktop depth stays at the station depth of 1.2 m (the guide says 0.60 deep); the layout is unchanged.
5. `Scripts/Art/FoodVisual.cs` changed although it is not in the change list: it has separate part scale and radius for
   bowl dishes, because the bowl shrank from 0.30 to 0.21 m.

**Fixes found on the way**
- `ArtCatalogBuilder` assigned the scale instead of multiplying it, so models that import with a root scale came out too big:
  the trashcan was 1.9 m (root scale 0.47) and the bowl 8 % over. It now multiplies (`ScaleBy`).
- Cookware contents: the anchor sat over the bounds centre. For the pan that is 10 cm towards the handle. It now sits over
  the base centre (the vertices in the lowest quarter of the model). Part radii are stored for the new sizes (board 0.055, pan 0.056,
  pot 0.041, plate 0.064, bowl 0.041 m; part scale kept). Tests check that the anchor is over the base and that four halves or four potatoes stay inside the rim of the
  pan, pot, plate, bowl and board, and on plated and bowl dishes.
- The knife collider is 0.345 m long now, like the knife model.

**Colours.** Furniture Kit materials are recoloured in the wrappers through generated copies in `Art/Recolour/`: wood to
WoodLight, woodDark to WoodDark, metal and metalMedium to Steel, metalDark to Charcoal, carpetWhite to Cream; glass is kept.
The other packs use one shared colour-map texture and are not tinted. Graybox fallbacks: the ground and the boxes (new
`Settings/Wall.mat`) are Plaster, the stove fallback is Steel, counter, shelf and worktop are WoodLight, and the crates use the
food colours. The light is Cream. The world labels default to Cream. Customer tints are named palette colours (meal
Mustard, snack Sage, drink SoftBlue, other Cream).
The UI mapping is: crosshair Cream 55 %, crosshair on a target Interact, prompt Cream on Panel, reason Steel, and titles Mustard.
Highlights: take Interact, use Sage (the place marker is Sage 45 %), refuse Steel.
Mood colours: ticket Cream, happy Sage, ok Mustard, unhappy Terracotta, missed Steel.
Stove: knob Sage when on, dark Brick when off, fry Terracotta, sauté Mustard, ring Terracotta or Charcoal. Pings are Mustard and Terracotta.

**Test fixtures updated** (old scene heights or the replaced single cabinet; no interaction, cookware or order test changed):
- `ArtCatalogTests.BuiltScene_StationsKeepCollidersAndPositionAndShowCatalogArt`: station centre y 0.5 becomes height / 2, and
  the body scale (1.2, 1, 1.2) becomes (1.2, height, 1.2), with the height from `ArtSizeTable`.
- `KitchenPolishTests.BuiltScene_WorktopsShowTheStretchedCabinet_...` is renamed `..._WorktopsShowTheCabinet_...`. It checks
  the combined bounds of the modules against the collider (width and height as before, plus depth) instead of the scale of
  one stretched instance.
- `WhatsNewTests`: spec 025 is no longer the newest entry; a new test makes spec 026 the newest.

**Palette checks** (`PaletteTests`): `Scripts/` and `Editor/` may not build a colour (`new Color(`, `new UnityEngine.Color32(`,
or a target-typed `new(` into a Color / Color32 / Color[]); `.uss` and `.tss` files may not hold hex, `rgb(a)` or colour keywords
(only `Palette.uss` holds hex values, and only the 16 `--wt-*` names). The patterns have their own test cases.

**Checked, nothing to change**
- First person (eye 1.6 m): the tops are now 0.7 m below the eyes; aim range and reach are unchanged and all aim and reach
  tests pass. Things on the tops are now lower, so they sit nearer the feet and stay in reach.
- `HeldItemView` keeps each child's local offset, so food stays in the pan in view too.

**Follow-ups**
- AC6: the pan in view is only 6 % smaller (0.55 to 0.52 m) at the same `ViewRules.ViewHoldOffset`, so it may still cover a
  lot of the view. Changing the hold offset or scale is in `Player/` and was out of scope.
- Steam still rises from the root centre (about 10 cm off the pan's middle).

**Hygiene**
- The FishNet reorder of `DefaultPrefabObjects` and the unchanged re-save of `Steam.mat` were reverted.
- Changed materials (palette colours): `Crate_*`, `Ground`, `Pickup`, `PlaceMarker`, `Station_Counter`, `Station_Pan`,
  `Station_Plating`, `TicketCard`, `TicketRail`, `Worktop`. `PlaceMarker` also picked up URP's `disabledShaderPasses` lines
  when it was re-saved.

## Blockers
(Builder fills this in after 3 failed attempts.)
