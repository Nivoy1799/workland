---
id: 026
title: Scale and palette pass (apply the visual guide to the whole world)
status: draft
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
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: `ArtSizeTests`: every `ArtKey` with a catalog prefab has a row in `ArtSizeTable` and its
      built bounds are within ±15 % of `TargetSize`; the guide's size table and `ArtSizeTable` agree.
- [ ] AC3: `PaletteTests`: the palette has the 16 guide colours with the guide's hex values; no
      `.cs` file under `game/Assets/_Project/Scripts` other than `Art/Palette.cs` contains
      `new Color(` or `new Color32(`; no `.uss` file other than `Palette.uss` contains a `#` hex
      colour or `rgb(`.
- [ ] AC4: Scene test: worktops, stove tops and place surfaces at 0.90 m ±0.02, serving counter at
      1.05 m ±0.02; worktops are made of 0.6 m modules.
- [ ] AC5: All existing interaction, cookware and order tests still pass unchanged.
- [ ] AC6: Screenshots (WSL server, clients): kitchen in third person with a person for scale; first
      person holding the pan (no longer covering a third of the view); the street with houses and trees.
- [ ] AC7 (manual, human): sizes feel right, colours belong together, nothing floats or sinks.
- [ ] AC8: `WhatsNew.json` entry `spec-026`.

## Open questions (defaults in bold)
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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
