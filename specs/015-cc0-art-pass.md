---
id: 015
title: Free CC0 art for the kitchen and street, behind a swap layer
status: approved
owner: architect
builder: sonnet
---

# 015: Free CC0 art pass

## Goal
Replace the graybox cubes of the kitchen and its surroundings with free CC0 low-poly art (decision
D26), so the playground looks like a small town. All art goes through a swap layer: gameplay objects
keep their colliders and components, and only a visual child comes from an `ArtCatalog`. A later
switch to another pack family (e.g. Synty, bought outright) then means swapping catalog entries, not
touching gameplay code or scenes by hand. Imported packs are tracked in a licence manifest that
`scripts/verify.sh` checks.

## Scope
- `game/Assets/ThirdParty/` (new): imported packs, one folder per pack, `<Creator>/<Pack>/`
  (e.g. `Kenney/CityKitSuburban/`). Only the model and texture files that are used, unmodified.
  Imported by the human (see Human steps); the builder only arranges folders and import settings.
- `game/Assets/ThirdParty/ASSETS.md` (new): licence manifest, one row per pack folder.
- `scripts/check-assets.sh` (new): manifest and size check, see Design.
- `scripts/tests/check-assets/` (new): small fixture folders and manifests for the self-test.
- `scripts/verify.sh` (change: run `check-assets.sh`).
- `game/Assets/_Project/Scripts/Art/` (new):
  - `ArtKey.cs`: enum of visual slots (one per `StationKind`, plus `Ground`, `Road`, `Building`,
    `Prop`; extendable).
  - `ArtCatalog.cs` (ScriptableObject): list of `ArtKey` to prefab entries; `TryGet(ArtKey, out GameObject)`.
  - `ArtCatalogRules.cs` (pure): lookup with fallback, duplicate-key detection.
- `game/Assets/_Project/Art/ArtCatalog.asset` (new): the CC0 catalog.
- `game/Assets/_Project/Prefabs/Art/` (new): one wrapper prefab per catalog entry. The wrapper holds
  the pack model as a child, scaled and offset to the gameplay footprint, with no colliders and no
  scripts.
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change): stations and environment use the
  catalog visual when one is assigned, else the current primitive. Adds a short street east of the
  kitchen (road tiles, 3 to 5 building facades, a few props).
- `game/Assets/_Project/Resources/WhatsNew.json` (change: entry).
- `game/Assets/_Project/Tests/EditMode/ArtCatalogTests.cs` (new).
- `docs/assets.md` (new): one page of rules for adding art (folder, manifest row, wrapper prefab,
  catalog entry, never edit pack files).

## Out of scope
- Players and customers stay capsules (open question 2). No rigs, animations or Mixamo.
- Food and dish visuals, including building dishes from parts. That gets its own spec.
- Interiors beyond the kitchen row, new gameplay objects, NavMesh, lighting or post-processing
  overhaul, URP settings.
- Any change to colliders, station positions, interaction ranges, `/sim` or networking.
- Paid or subscription assets of any kind (D26).
- Stripping art from the headless server build.

## Design
- **Swap layer:** the builder creates each station exactly as today (root with `Station`, the cube
  body child with its `BoxCollider`, label). If `ArtCatalog.TryGet(key)` finds a prefab, the builder
  removes only the cube's `MeshRenderer` and adds the wrapper as a sibling child of the body. The
  cube body object and its collider stay at today's size (1.2 x 1 x 1.2 m), so movement,
  interaction and tests do not change. If no prefab is
  assigned, the cube stays (graceful fallback, so a half-filled catalog still builds).
- **Wrapper prefabs** absorb the differences between packs: scale, pivot, rotation and material
  overrides live there. Gameplay code only knows `ArtKey`.
- **Street:** visual only, placed by the builder from a small fixed layout in code (positions and
  keys), static, no colliders except the existing obstacle boxes. The obstacle boxes keep their
  colliders. Their renderer may be swapped for a building visual of the same footprint.
- **Manifest** `ASSETS.md`, one table row per pack folder:
  `| Folder | Pack | Creator | Licence | Source URL | Imported |`, for example
  `| Kenney/CityKitSuburban | City Kit (Suburban) | Kenney | CC0-1.0 | https://kenney.nl/assets/city-kit-suburban | 2026-11-08 |`.
- **check-assets.sh** fails if:
  1. a folder `game/Assets/ThirdParty/<Creator>/<Pack>/` has no manifest row,
  2. a manifest row points to a folder that does not exist,
  3. a row's licence is not on the allow-list (only `CC0-1.0` for now; adding a licence is a human
     decision),
  4. `game/Assets/ThirdParty/` is larger than the size cap (100 MB, open question 3).
  It prints which rule failed and for which folder. No network access.
- **Style rule:** one pack family per slot type. Kenney first. KayKit or Quaternius only where
  Kenney has nothing, and that is noted in the manifest's last column.

## Acceptance criteria
- [ ] AC1: `scripts/check-assets.sh` passes on the real tree and fails with a clear message on each
      of the four rules (tested against fixture folders under `scripts/tests/check-assets/`).
- [ ] AC2: `scripts/verify.sh` runs the asset check and stays green.
- [ ] AC3: `ArtCatalogRules` (EditMode): returns the prefab for an assigned key, reports missing
      keys, and detects duplicate keys.
- [ ] AC4: `ArtCatalog.asset` has an entry for every `StationKind` (EditMode test iterates the enum).
- [ ] AC5: After rebuilding the graybox scene, every station has its catalog visual and an unchanged
      `BoxCollider` size and world position (EditMode test against the built scene or builder output).
- [ ] AC6: All existing EditMode tests and `/sim` tests still pass. No file outside Scope changed.
- [ ] AC7: No wrapper prefab under `Prefabs/Art/` contains a collider or a MonoBehaviour (EditMode test).
- [ ] AC8: `WhatsNew.json` has an entry with id ending in `spec-015`.

## Test plan
- EditMode: `ArtCatalogTests.cs` for AC3 to AC5 and AC7.
- Shell: fixture folders for AC1, run by `check-assets.sh --self-test` from `verify.sh`.
- Manual, 2 clients: walk the kitchen and street, cook one dish. Interaction ranges and camera feel
  must be unchanged.
- Command: `scripts/verify.sh`

## Open questions (answered 2026-11-07: human approved all defaults)
1. Which packs for this pass? [Proposal: Kenney only: City Kit (Suburban), City Kit (Commercial),
   City Kit (Roads), Furniture Kit for the kitchen stations. One creator keeps the look consistent.]
2. Characters: keep capsules or use a CC0 character pack now? [Proposal: keep capsules. Characters
   need rigs and animations and get their own spec.]
3. Binary files in git: plain git with a 100 MB cap, or Git LFS now? [Proposal: plain git with the
   cap. Kenney packs are small; move to LFS when the cap is reached.]
4. Kitchen station mapping, e.g. pan to stove, pot to stove with pot, board to counter with cutting
   board, crate to crate. [Proposal: builder picks the closest Kenney model per station, human checks
   it in the manual test.]

## Human steps
- Download the chosen packs from kenney.nl (CC0, free; optional donation) and copy only the needed
  `.fbx`/`.glb` and texture files into `game/Assets/ThirdParty/<Creator>/<Pack>/`.
- Add one manifest row per pack (or give the builder the pack name and URL to fill in).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
