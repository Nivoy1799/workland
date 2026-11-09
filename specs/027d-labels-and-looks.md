---
id: 027d
title: World labels that never clutter, and customers that look different
status: approved
owner: architect
builder: sonnet
---

# 027d: World labels and customer looks

## Goal
The visual checks of specs 027 and 027a showed problems with world text:
- it is huge and floats over everything;
- it overlaps into unreadable text ("ato Onion soVeggie mato sa...");
- it is mirrored from the guests' side ("ORDERS", station names);
- in first person, "Stove 1" sits across the view.

The guests are also almost identical (two skins, light tints). This spec applies docs/feel.md 3 and 7 and
docs/art-direction.md 7 ("never overlapping") to world text, and adds customer variety. Approved by the
orchestrator under the human's 24 h grant.

## Depends on
- 014/022 (customer order bubbles), 019/021 (station, crate and item labels), 026 (palette), 027a (floor plan).

## Scope
- `game/Assets/_Project/Scripts/Player/WorldLabelFade.cs` and a new pure `LabelRules` (show, hide, size and fade
  by distance, angle, view mode and focus).
- `game/Assets/_Project/Scripts/Art/`: new `StationSign` (a small flat sign on the station front, readable only
  from the cook side, never drawn through geometry).
- `game/Assets/_Project/Scripts/Npc/Customer.cs`: the order bubble (an icon plus a short name on a Panel
  background, stacked so bubbles never overlap; only the queue head and the next guest show full text, the others
  show an icon).
- `game/Assets/_Project/Scripts/Cooking/`, `Net/Pickup.cs`: item status as a small icon next to the item,
  shown only when the item is aimed at or held.
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs`: replace the floating station and crate names with
  `StationSign`s; the ticket rail text faces the aisle and is one-sided.
- Customer looks: 4 or more skin and tint variants from the existing Kenney skins plus palette tints for top and
  bottom (per material slot), chosen deterministically from the customer id.
- Tests, `WhatsNew.json`, this spec.

## Out of scope
- New downloads (if more skins are needed, a later spec downloads Kenney Animated Characters 1/2/3 skins).
- The HUD and ticket rail layout.

## Design
- **Never through walls, never mirrored.** World text uses a normal depth-tested material, and one-sided text is
  hidden when seen from behind (dot(view, normal) < 0.2).
- **Distance and focus.** Station signs are always visible but small (cap height 6 cm at 1 m). Item and status
  labels show only for the aimed or held item. Order bubbles show for queued guests within 8 m.
- **No overlap.** A screen-space pass hides lower-priority labels whose screen rectangles overlap a
  higher-priority one. Priority: the aimed target, then the held item, then the queue head, then the rest.
- **First person.** No world label within 15° of the screen centre, except the aimed target's prompt (027b).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: `LabelRules` tests: visibility by distance, angle, view and focus; the overlap culling keeps the
      higher priority; no label is shown backwards.
- [ ] AC3: Visual check `look` sheet:
  - no overlapping or mirrored text in any frame;
  - the first person at the stove shows no big station name across the view;
  - the guests' view reads the order bubbles.
- [ ] AC4: Customers: at least 4 visually distinct variants in the queue shot; the variant is stable per
      customer id (test).
- [ ] AC5: `WhatsNew.json` entry `spec-027d`.

## Defaults to review
Sign size; the 8 m bubble range; full text only for the queue head and the next guest; the 15° centre-free zone.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
