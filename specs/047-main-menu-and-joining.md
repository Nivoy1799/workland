---
id: 047
title: Main menu and joining (title screen, host, join via Steam or IP, lobby, rejoin)
status: approved
owner: architect
builder: opus
---

# 047: Main menu and joining

## Goal
Today the game starts from command-line flags (`-host`, `-connect`). Early Access needs a title screen.

## Design
- **Title screen:**
  - Continue / New shop (host, with the 046 save);
  - Join a friend (Steam invite, friends list, rich presence; FishySteamworks);
  - Join by address (testing);
  - Settings (048);
  - Quit.
  The town is visible behind the menu.
- **Lobby:** the host's shop with up to the O7 player cap (default 4 until the load test decides). Players appear as
  they join. The host can kick.
- **Connection feel:**
  - a loading screen with progress;
  - clear errors ("version mismatch", "server full", "connection lost");
  - automatic rejoin for 30 s after a drop;
  - the held item goes back to its spot when a player leaves.
- **Version check:** client and server compare a build hash, and a mismatch refuses cleanly.
- Dedicated servers are not listed yet (O5, Phase 4).

## Acceptance criteria
- [ ] AC1: `verify.sh`; RPC surface tests (only connection messages may change, documented).
- [ ] AC2: Tests for the version check, kick, rejoin and errors.
- [ ] AC3: Visual check: menu, host, join by address, lobby, a disconnect message.
- [ ] AC4 (manual, human): join through Steam on two PCs (finishes spec 003 AC2).
