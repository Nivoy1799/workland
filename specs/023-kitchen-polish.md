---
id: 023
title: Kitchen polish (sounds, steam, real counter models, readable prompts)
status: approved
owner: architect
builder: sonnet
---

# 023: Kitchen polish

## Goal
The kitchen feels alive and reads clearly: a knife chop sound, sizzling in a hot pan, bubbling pot,
soft click on buttons and pick-ups, steam over cooking pans and pots, worktops and stoves that look
like kitchen furniture instead of grey boxes, and prompts under the dot that are always readable.
Written on 2026-11-07 for the overnight build.

## Depends on
- Specs 015 (art swap layer, CC0 rules, `check-assets.sh`), 020 (prompt, highlight), 021 (worktops,
  stoves, cookware).
- Kenney Furniture Kit (already in the project, CC0): kitchen cabinet, stove and sink models.
- **Download (approved with this spec):** one CC0 sound pack from kenney.nl, e.g. "Impact Sounds"
  and/or "Interface Sounds", at most 30 MB in total. The orchestrator copies only the used .ogg
  files into `game/Assets/ThirdParty/Kenney/<Pack>/` with License.txt and an `ASSETS.md` row.

## Scope
- `game/Assets/ThirdParty/Kenney/` (new sound pack folder, only used files), `ASSETS.md` row.
- `game/Assets/_Project/Scripts/Audio/` (new): `SoundCatalog` (ScriptableObject, ArtKey-like keys),
  `KitchenSounds` (client only: plays on local actions and on synced state changes; no network).
- `game/Assets/_Project/Scripts/Art/` (change: steam particle on vessels that are on a burner that
  is on; worktop/stove art keys mapped to Furniture Kit models via the art catalog).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: prompt with a dark rounded background, white
  text, 16 px, always readable over the glow).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs` (change: worktops
  and stoves use Furniture Kit models; colliders unchanged).
- `WhatsNew.json` entry, tests, this spec.

## Out of scope
- Music, voice, ambient town sounds; volume settings menu (one master volume key is enough: none).
- New mechanics.

## Design
- Sounds are client-side reactions to things the client already knows (own click, synced contents,
  synced stove flags, FoodLook doneness changes). The server plays nothing (headless).
- Steam: one small pooled particle system per hot vessel, cheap (max 30 particles).
- Furniture: worktop = Furniture Kit kitchen cabinet stretched to the worktop size by the art
  wrapper (spec 015 fit math), stove = Furniture Kit stove model; burner spots stay where the
  colliders say.
- Prompt: background panel behind the text; the grey "Too far / can't" stays grey text on the panel.

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh` with the new pack row.
- [x] AC2: `SoundCatalog` test: every sound key has a clip; every clip is under ThirdParty with a
      licence row.
- [x] AC3: Art catalog tests: worktop and stove keys map to Furniture Kit models; wrappers have no
      colliders or scripts.
- [x] AC4 (2026-11-08 night, `-demo-cookware`: cabinet worktops, steam over the pot on the burner that is on, prompt on a dark panel): Screenshot: kitchen with furniture models, steam over a pan on a burner, readable prompt.
- [ ] AC5 (manual, human): sounds play on chop, sizzle, boil, click; nothing is too loud.
- [x] AC6: `WhatsNew.json` entry `spec-023`.

## Open questions (answered 2026-11-07: human approved all defaults, incl. the download)
1. **Download** one CC0 Kenney sound pack (≤ 30 MB), only used files kept? **Default: yes.**
2. **Furniture Kit** models for worktops and stoves? **Default: yes.**

## Human steps
- Approve; listen and look (AC5).

## Implementation notes
Built 2026-11-07 (builder). `verify.sh` PASS, EditMode 502/502 (baseline 492). Not committed.
- **Deviation (orchestrator): no sound download** (Kenney returned HTTP 403, not worked around). Sounds are synthesised
  in code: `Audio/ProceduralSounds` (pure seeded sample generators: chop = high-passed noise burst + 150 Hz thump, tick =
  short sine pair, sizzle = 2 s high-passed noise with crackles (loop), bubble = 3 s random low blips (loop, quiet tail)),
  wrapped in AudioClips by `SoundCatalog` (plain class, not a ScriptableObject, since there are no files; lazily built,
  `MasterVolume` 0.5 plus a per-key gain). So AC1 has no new pack row and `check-assets` is unchanged; AC2 became "every
  key has a clip, deterministic, peak below 0.8" (`KitchenPolishTests`).
- `Audio/KitchenSounds` (scene object, client only, removes itself in batch mode / no graphics): polls every 0.1 s:
  chop when a cutting board's synced looks change while its count stays > 0, tick when the local player starts/stops
  holding something or a stove's synced flags change, sizzle (pan) / bubble (pot) loops on the vessel while
  `VesselSteam.IsHot`, faded in/out. The first poll after joining only records state. 6 pooled one-shot sources.
- `Art/VesselSteam` (added to the Pan and Pot prefabs by the scene builder): hot = holds something, not carried, pivot
  over a burner (`BurnerRules.IsOver`, 0.1 m margin) whose synced knob is on. One particle system built in code, max 30
  particles, soft round sprite generated at runtime, material `Settings/Steam.mat` (URP Particles/Unlit, transparent).
- Furniture: stoves already used the Furniture Kit stove since spec 015/021. New `ArtKey.Worktop` (appended): the
  `kitchenCabinet` model squeezed into a 1 x 1 x 1 m box (`Fit.Unit`), the scene builder scales the instance to each
  worktop (width x stove-top height x 1.2). Cube Body, collider, place surface and everything else are unchanged (the
  Body loses only its MeshRenderer, like the stations). The cabinet texture is stretched on the long worktops.
- HUD: the aim prompt is a label with a dark rounded panel (80 % opaque), 16 px white text, centred in a full-width row;
  grey reasons keep the grey colour. Outline removed.
- `WhatsNew.json` entry `2026-11-08-spec-023`; `WhatsNewTests` for spec 022 now asserts it is not the newest any more
  (same change as spec 021 made), new test for 023.
- Rebuild: FishNet "same assetPath hash of 0" errors expected; `DefaultPrefabObjects.asset` was a pure reorder and
  reverted; re-saved `PlaceMarker/TicketCard/TicketRail/Worktop.mat` reverted. Changed: ArtCatalog.asset (+Art_Worktop),
  Pan/Pot prefabs (+VesselSteam), Graybox.unity, new `Steam.mat`, `Art_Worktop.prefab`.
- Not visually verified: steam look/size, cabinet stretch, sound levels. Check with `-demo-cookware` (burner on, pot on it).

## Blockers
