---
id: 012
title: Non-verbal communication: emote wheel, world pings, quick chat
status: draft
owner: architect
builder: opus
---

# 012: Non-verbal communication

## Goal
Players can communicate without voice, which the human called very important (2026-11-06): an emote
wheel, pings that mark a spot in the world, and short predefined quick-chat phrases. Standard genre
patterns (emote wheels and pings as in most co-op and team games), nothing invented. No character
animations yet (the player is still a capsule): emotes show as a bubble above the head plus a small
body motion.

## Scope
Only `game/` (branch stacked on `spec/011-hud-debug-whatsnew`).
- `game/Assets/_Project/Scripts/Social/` (new):
  - `EmoteCatalog.cs`, `QuickChatCatalog.cs`: fixed lists (ids, text, icon character).
  - `SocialRules.cs`: pure rules: wheel slot from a direction, rate limit, ping range and validity.
  - `SocialInteractor.cs` (NetworkBehaviour on the player): emote, ping and quick-chat requests to
    the server; the server validates, rate-limits and broadcasts.
  - `EmoteBubble.cs`: world-space bubble above a player, shown for a few seconds, plus a small bob.
  - `PingMarker.cs`: world marker (pillar + label "Player 1: Look here") that fades after ~6 s.
  - `EmoteWheelView.cs` (UI Toolkit): radial wheel while the key is held.
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: quick-chat lines appear as toasts with the
  player's name).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: components on the player prefab).
- `game/Assets/_Project/Resources/WhatsNew.json` (change: entry for this spec).
- `game/Assets/_Project/Tests/EditMode/SocialRulesTests.cs` (new).

## Out of scope
- Voice chat, free text chat, character animations or models, emote unlocks, a ping on the minimap.

## Design
- **Emote wheel:** hold G (gamepad: hold LB) to open a wheel of 8 emotes; move the mouse or right stick
  towards one and release to send. Default emotes: Wave, Thumbs up, Thumbs down, Point, Cheer,
  Follow me, Help, Thanks. The camera does not turn while the wheel is open.
- **Ping:** middle mouse (gamepad: RB) marks the spot the camera looks at; double-tap marks
  "Danger / not here". Everyone sees the marker and a short toast. Range about 40 m (same as the
  network interest radius).
- **Quick chat:** hold T (gamepad: D-pad) for a small list: "On my way", "I'll cook", "Need
  potatoes", "Selling now", "Good job", "Sorry". Shown as a toast for everyone nearby.
- **Authority and abuse:** clients send ids and a ping position only. The server checks the id
  exists, the ping position is finite and within range of the sender, and rate-limits (about one
  social action per second, short burst allowed). Then it broadcasts to observers with
  `ObserversRpc`. No free text means nothing to moderate.

## Acceptance criteria
- [ ] AC1: `SocialRules` (EditMode tests): direction to wheel slot for 8 slots and dead zone; rate
      limit allows a short burst then refuses; ping refused when non-finite or out of range.
- [ ] AC2: Catalogs: unique ids, every emote and phrase has text; unknown ids are refused (tests).
- [ ] AC3: Two clients on the WSL server: an emote shows above the sender on both screens, a ping
      marker appears for both, quick chat shows as a toast for both (manual; screenshots).
- [ ] AC4: Spamming the key does not flood others (rate limit visible in the server log).
- [ ] AC5: `bash scripts/verify.sh` passes with `UNITY_PATH` set; What's-new entry added.

## Test plan
- EditMode tests for the pure rules and catalogs.
- Manual with two clients, plus screenshots via `-screenshot`.

## Open questions (human decides; recommended default in brackets)
1. Keys. [G emote wheel, middle mouse ping, T quick chat; gamepad LB / RB / D-pad.]
2. Emote and phrase lists. [As listed above; easy to change later, they are data.]
3. Should pings be visible to everyone or only to nearby players? [Nearby, within the 40 m interest
   radius, like everything else in the world.]

## Human steps
- Approve, answer the open questions, play it with a second person.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
