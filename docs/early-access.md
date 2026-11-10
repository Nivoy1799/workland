# Early Access: what is missing (goal set 2026-11-10)

The human's goal: "the game has early access state". This lists what an Early Access release on Steam needs,
what already exists, and what is approved, open or blocked by a decision. Claude builds only approved specs. Missing
specs are drafted and go to docs/human-inbox.md for approval. Decisions marked OPEN in docs/decisions.md stay with the
human.

## 1. A shop that is fun for an hour (the core loop)
- Done: specs 025–038b (stations, orders, register, stock, cleaning, tutorial).
- Approved, being built: 039 (place anywhere), 041 (mouse controls), 042 (cup on the tray), 043 (cooking
  consistency), 044 (feel polish 3), 037 (street-food prices and menu board).
- Owned by the second session: 040 (Blender models, a coherent look).
- Missing (draft): **046 days and progression.** A day ends with a summary; money carries over; the shop rating grows;
  unlocks (new menu items, equipment) come from the 037 shop; a save file for the host's shop.

## 2. Playing together (the edge of the game)
- Done: FishNet host and client, dedicated Linux server, co-op kitchen.
- Approved, unbuilt: 003 AC2 (the Steam test on two PCs: the human), 004 load test (inside 045).
- Missing (draft): **047 main menu and joining.** A title screen with Host, Join (Steam friends and invite, a direct
  IP for testing) and Settings; the lobby; a clean disconnect and rejoin.

## 3. A shippable client
- Missing (draft): **048 settings and comfort:**
  - graphics (quality presets, resolution, vsync, FOV);
  - audio volumes;
  - mouse sensitivity, invert;
  - key rebinding;
  - first- and third-person defaults;
  - saved per player.
- Missing (draft): **049 stability for release:**
  - crash reports (local log plus an opt-in upload later, OPEN O6-like decision);
  - the 045 crash-on-quit fix;
  - a performance budget (60 fps on a mid PC with 8 players);
  - memory leaks.

## 4. Online services (Phase 4 in the plan)
- Approved, unbuilt: **006 AI gateway skeleton** (item names via an OpenAI-compatible API, schema validation, cache).
  Claude can build it after the queue; it needs an API key in `.env` from the human to run for real (no secrets in git).
- OPEN decisions that block more: O4 backend stack, O5 server hoster, O6 moderation approach, O7 player cap (after the
  load test). The human decides these.

## 5. Steam release (the human)
- A Steamworks app ID, a store page, capsule art, trailer, pricing, age rating, the EA questionnaire. Claude can prepare
  text drafts and checklists; the human submits.

## Proposed order after the current queue
006 (AI gateway, approved) → 046 → 047 → 048 → 049. Specs 046–049 need approval first.
