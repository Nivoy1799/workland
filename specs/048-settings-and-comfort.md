---
id: 048
title: Settings and comfort (graphics, audio, controls, rebinding)
status: draft
owner: architect
builder: sonnet
---

# 048: Settings and comfort

## Design
A settings menu (title screen and Esc) with:
- **Graphics:** quality presets, resolution, window mode, vsync, a frame cap, FOV 70–100, the motion-blur and head-bob
  toggles.
- **Audio:** master, effects, ambience and UI volume.
- **Controls:** mouse sensitivity, invert Y, key and button rebinding (the 041 mapping as the default), gamepad.
- **Comfort:** UI scale, colour-blind friendly ring colours (an alternative palette set), and a labels on/off toggle
  (the 040 test).

Saved per player (PlayerPrefs, the same pattern as the tutorial flag) and applied live.

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Tests that settings save, load and apply; rebinding conflicts are detected.
- [ ] AC3: Visual check of every page.
- [ ] AC4 (manual, human): rebind a key and play.
