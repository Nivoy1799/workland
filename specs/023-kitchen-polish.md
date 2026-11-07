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
- [ ] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh` with the new pack row.
- [ ] AC2: `SoundCatalog` test: every sound key has a clip; every clip is under ThirdParty with a
      licence row.
- [ ] AC3: Art catalog tests: worktop and stove keys map to Furniture Kit models; wrappers have no
      colliders or scripts.
- [ ] AC4: Screenshot: kitchen with furniture models, steam over a pan on a burner, readable prompt.
- [ ] AC5 (manual, human): sounds play on chop, sizzle, boil, click; nothing is too loud.
- [ ] AC6: `WhatsNew.json` entry `spec-023`.

## Open questions (answered 2026-11-07: human approved all defaults, incl. the download)
1. **Download** one CC0 Kenney sound pack (≤ 30 MB), only used files kept? **Default: yes.**
2. **Furniture Kit** models for worktops and stoves? **Default: yes.**

## Human steps
- Approve; listen and look (AC5).

## Implementation notes

## Blockers
