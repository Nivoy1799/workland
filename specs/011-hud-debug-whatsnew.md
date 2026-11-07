---
id: 011
title: Player HUD, F1 debug menu and "What's new" banner
status: approved
owner: architect
builder: opus
---

# 011: HUD, debug menu and "What's new"

## Goal
Replace the one big debug box with what players and testers expect: a clean HUD with the player's
own information that is always visible, all technical details in a debug menu toggled with a
function key, and a "What's new" banner that shows what changed in this build and what to test,
so the human is always up to date without reading commits.

## Scope
Only `game/` (branch stacked on `spec/010-player-controls`), plus one line in `docs/workflow.md`.
- `game/Assets/_Project/UI/` (new): `Hud.uxml`, `DebugMenu.uxml`, `WhatsNew.uxml`, `Worktown.uss`,
  `PanelSettings.asset` (Unity UI Toolkit, Unity 6's standard runtime UI).
- `game/Assets/_Project/Scripts/UI/` (new):
  - `HudView.cs`: wallet, clock, held item with stars, station prompt, a short toast feed for
    sales and kitchen results (fades after a few seconds).
  - `DebugMenuView.cs`: everything the current overlay shows (mode, status, Steam, sim link, tick,
    ping, players, market details, connect buttons), toggled with F1.
  - `WhatsNewView.cs`: the banner; reads `WhatsNew.json`, shows the newest entry, Enter or a click
    closes it, F2 opens it again.
  - `WhatsNewData.cs`, `WhatsNewRules.cs`: pure parsing and "show once per build" logic.
- `game/Assets/_Project/Resources/WhatsNew.json` (new): entries with date, title, what is new and
  what to test. First entries describe specs 007, 009 and 010.
- `game/Assets/_Project/Scripts/Net/DebugOverlay.cs` (remove; its content moves into the views).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: UI objects in the scene).
- `game/Assets/_Project/Tests/EditMode/WhatsNewTests.cs` (new).
- `docs/workflow.md` (change: every spec that changes something playable adds a `WhatsNew.json` entry).

## Out of scope
- Main menu, settings menu, rebinding, localization, art pass of the UI, chat, emotes (own spec).
- Any change to game rules, networking authority or the economy.

## Design
- **Layout:** HUD top-left: wallet (large), clock and day below. Bottom-centre: station prompt
  ("Pan: 1 light, 2 golden, 3 dark") only when near a station. Bottom-left: held item with stars.
  Right: toasts ("Sold crispy fried potato sticks ★★★★☆ +$478"). Readable at 720p, dark
  translucent panels, no technical text in the HUD.
- **Debug menu (F1):** a panel with the current debug content, off by default. When not connected
  (menu mode) the connect buttons show regardless, since there is nothing else to do.
- **What's new (F2):** shown automatically when the build is newer than the last one the player
  closed (stored in `PlayerPrefs` per build id, which is the newest entry's id). The banner lists
  "New" and "Please test" as bullets. Content is plain JSON so agents add an entry with every spec.
- **Cursor:** while the debug menu or the banner is open the cursor is free (spec 010 locks it while
  playing); closing them locks it again.

## Acceptance criteria
- [x] AC1: `WhatsNewRules` (EditMode tests): parses the JSON, picks the newest entry by date,
      "show on start" is true for an unseen build id and false after it was closed; bad JSON gives an
      empty list instead of an exception.
- [x] AC2: The shipped `WhatsNew.json` parses and has entries for 007, 009 and 010 (EditMode test).
- [~] AC3: In the game, the HUD shows wallet, clock, held item and prompt; F1 toggles the debug menu;
      the banner opens on first start, closes with Enter and reopens with F2 (manual).
- [x] AC4: Spec 003, 007, 009 and 010 tests pass; nothing in the HUD shows technical text (manual).
- [x] AC5: `bash scripts/verify.sh` passes with `UNITY_PATH` set.

## Test plan
- EditMode tests for the pure What's-new logic and the shipped JSON.
- Manual play with two clients on the WSL server.

## Open questions (answered 2026-11-07: human approved both defaults)
1. Keys. [F1 debug menu, F2 What's new. F1 is the classic help/debug key; F3 is Minecraft's debug key
   if you prefer that convention.]
2. Who writes What's-new entries. [The agent that builds a spec adds one entry as part of "done";
   the human can edit the JSON at any time.]

## Human steps
- Approve, answer the open questions, play it and say if the HUD is clear enough.

## Implementation notes
- Review 2026-11-07, outside the listed scope: `PickupInteractor.cs` (result counter, enum outcomes,
  keys ignored while a menu is open) and `DevScreenshot.cs`. HUD toasts map `CookOutcome` /
  `PickupOutcome` enums instead of matching strings. Movement input is zero while a menu is open.
Built 2026-11-07 on `spec/011-hud-debug-whatsnew` (stacked on spec 010).
- UI Toolkit with the tree built in C# (`HudView`, `DebugMenuView`, `WhatsNewView`) and one
  stylesheet `UI/Worktown.uss` plus theme `UI/WorktownTheme.tss`; no UXML files (simpler to
  generate and review). `PanelSettings` scales with screen size from 1280x720.
- AC1, AC2: `WhatsNewTests` (parse, bad JSON, newest, show once per build, shipped file has entries
  for 007, 009, 010, 011 with items and tests).
- AC3 (partly): screenshot from a Windows client on the WSL server shows the HUD (wallet, clock) and
  the banner on first start. F1, F2, Enter, the station prompt and toasts need a human at the keys.
- AC4/AC5: verify.sh PASS, Unity EditMode 125/125, /sim 191/191.
- Dev tool `-screenshot path [-screenshot-after s] [-screenshot-every s]` (`DevScreenshot`) so agents
  can check the UI without seeing the screen.
- Found by the screenshot check: crate labels overlapped; labels shortened and crates spaced 2 m.
- `docs/workflow.md`: What's-new entry rule.

## Blockers
(Builder fills this in after 3 failed attempts.)
