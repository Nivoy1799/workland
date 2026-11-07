---
id: 024
title: Human characters (players and customers as low-poly people)
status: draft
owner: architect
builder: opus
---

# 024: Human characters

## Goal
Players and customers stop being capsules. Everyone is a low-poly person with idle and walk
animations; other players see you hold food or the knife in your hands (not above your head); in
first person you still see your own shadow and the held item in view (spec 018). Customers use the
same characters in other colours. This was step 4 of the human's order from 2026-11-07 ("humans").

## Depends on
- Specs 010 (movement), 012 (emotes), 014 (customers), 018 (first person, own body hidden),
  020/021 (held items).
- **Download (approved with this spec):** one CC0 character pack with a humanoid rig and idle/walk
  animations, from kenney.nl (preferred) or quaternius.com, at most 30 MB. Only used files are kept,
  with License.txt and an `ASSETS.md` row. If no CC0 pack with animations fits, use a CC0 pack without
  animations and a simple procedural bob/sway (noted in the implementation notes).

## Scope
- `game/Assets/ThirdParty/<Author>/<Pack>/` (new, used files only), `ASSETS.md` row.
- `game/Assets/_Project/Scripts/Player/` (change: `CharacterVisual` (new) drives the animator from
  the synced velocity and crouch; `FirstPersonBody` hides the new model instead of the capsule).
- `game/Assets/_Project/Scripts/Net/Pickup.cs` (change: the server hold point stays for rules; a
  client-side visual follows the character's right hand bone for others).
- `game/Assets/_Project/Scripts/Npc/Customer.cs` (change: character model with a tint).
- `game/Assets/_Project/Scripts/Art/` and `Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs`
  (change: character art keys, player and customer prefabs use them; CharacterController unchanged).
- `WhatsNew.json` entry, tests, this spec.

## Out of scope
- Character customisation, clothes, faces, lip sync; IK for picking up; ragdolls.
- Changing movement rules or the CharacterController size.

## Design
- Visual only: the networked capsule controller and all rules stay. The model is a child of the
  graphic, scaled to 1.8 m, animator parameters Speed and Crouch from data every client already has.
- Held items: rules and server position unchanged; for remote viewers the held item's visual is
  drawn at the holder's right hand (client side), so it looks carried.
- Emote bubble stays above the head.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes, including `check-assets.sh`.
- [ ] AC2: Art tests: player and customer character keys exist, no colliders or scripts in wrappers.
- [ ] AC3: `CharacterVisual` rule test: speed → animation state (idle / walk / run), crouch flag.
- [ ] AC4: Screenshots, two clients: B sees A as a person walking, holding a potato in the hand;
      customers as people at the counter.
- [ ] AC5 (manual, human): looks right, no floating items, first person unchanged.
- [ ] AC6: `WhatsNew.json` entry `spec-024`.

## Open questions (defaults in bold)
1. **Download** one CC0 character pack (≤ 30 MB) chosen by the orchestrator? **Default: yes.**
2. **Customers** use the same characters with different colours? **Default: yes.**

## Human steps
- Approve; look at it (AC5).

## Implementation notes

## Blockers
