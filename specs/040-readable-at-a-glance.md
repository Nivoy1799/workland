---
id: 040
title: Readable at a glance (things explain themselves by their look; labels readable from every angle)
status: approved
owner: architect
builder: opus
---

# 040: Readable at a glance

## Goal
The human, after playing (2026-11-10): "the display of assets is a good starting point. Goal: it must be clear what
something is without it being written. For that we need better assets. Also the text is not readable from a lot of
angles."

So the target is: **you recognise every station, ingredient and state by its look**, and text is only a backup. Where
text or a symbol stays, it is readable from wherever you stand.

## Part A: recognisable assets (art)
- An **asset audit**: list every graybox or stand-in look the player meets. For each, note what it should look like,
  and whether a CC0 model exists (Kenney first, then Quaternius).
  - Stations: fryer, griddle, spit, fridge, shelf, terminal, sink, drink dispenser, register, trash can.
  - Food: potato (today the egg model), cucumber (today a green eggplant), lettuce and red cabbage (both today the same
    cabbage model), seitan, falafel, sauces.
  - Packaging: cups, wrap paper.
- **Silhouette first:** every ingredient has its own shape and colour, so no two share a model, and each fits its real
  look (a potato is a lumpy brown oval, a cucumber a long green cylinder).
- **States you can see:** raw, cut (halves, slices, sticks), cooking (browning), burnt (black and smoking), dirty plate,
  full or empty slot. Each one is a visible change, not a word.
- **Stations by their shape:** a fryer with oil wells and baskets, a griddle plate, a turning spit, a glass-door fridge
  with goods visible inside, a sink with a tap and basin. Recognisable from the aisle.
- New models need downloads. The 24 h download grant is used up, so the human approves each pack (file name, source,
  size) before it is fetched.

## Part B: text and symbols readable from every angle
- World labels **turn to face the camera** (billboard, about the vertical axis), so they never read mirrored or edge-on,
  and keep a minimum size on screen.
- **Icons before words:** stock slots, order bubbles, tickets and prompts show a small icon of the item (rendered from
  its own model at build time), with the word only as a small caption.
- Station signs stay on the station front, but are tilted towards the cook's eye height and readable from the whole
  aisle (angle up to about 60 degrees).
- The 027d rules (no overlap, calm view, no text through walls) stay.

## Part C: one coherent, lived-in look (added after the human's note, 2026-11-10)
The human: "the assets still feel kind of thrown together. Sure, we have low-poly art, but scale and something
organic should be met." Today graybox boxes, Kenney kits, Quaternius animations and generated shapes sit side by
side with different detail levels, edge sharpness and colours. The shop reads as parts, not as one place. Targets:
- **One style family.** Every visible model keeps to the same rules:
  - flat-shaded low-poly;
  - similar polygon density per metre;
  - the same bevel on edges;
  - palette colours only;
  - no pure graybox boxes left in the player's view.
  Graybox stations get proper models (Part A) or modelled stand-ins built in the same style (bevels, panels, handles,
  knobs), never plain cubes.
- **Scale and proportion pass.** Check every prop against a 1.75 m person in one lineup shot:
  - doors, worktops, the fryer, cups, bottles;
  - the size table (D29) holds;
  - nothing looks like a toy next to a full-size thing.
- **Organic touches (lived-in, not grid-perfect):**
  - items in the stock slots and on the shelves get a small random tilt, offset and rotation (seeded, the same on
    every client), not lined up like a grid;
  - used surfaces show wear (a few stains on the worktops near the stoves, a worn floor path in the aisle);
  - kitchen props make it a place: a utensil rail with hanging ladles, a menu board above the counter, a plant, a
    clock, a radio, posters about the vegan town (D27, D28: own fictional history), a doormat, a window to the street;
  - small motion: steam, a flickering neon "Open" sign, a fan turning, guests idling with small animations.
- **Light and atmosphere:**
  - a warm key light with soft shadows, baked ambient occlusion in corners, and light from the street through the
    window;
  - URP post-processing kept subtle: tonemapping, a warm colour grade, light bloom on lit things (neon, burners),
    vignette.
  - The art guide (docs/art-direction.md 6) gets these rules.
- **Reference board.** Before building, Claude makes a one-page mood board artifact: 6–8 described target shots from
  the genre's best low-poly kitchen sims, in our palette, for the human to approve. It contains no copied art, only
  descriptions and our own graybox shots.

## Acceptance criteria (to detail when approved)
- [ ] AC1: `verify.sh` passes.
- [ ] AC2: an audit table in this spec, with the old look, the target, the model source and its status.
- [ ] AC3: a test that no two ingredients share a model and colour.
- [ ] AC4: visual check from 5 angles per station (front, 45° left and right, from the counter, from far): every label
      readable, no mirrored text; ingredients told apart in a "no text" shot (labels hidden).
- [ ] AC5 (manual, human): walk the kitchen with labels hidden (a toggle). Can you name everything?

## Open questions
1. **Which art direction for the new models?** Default: stay with Kenney (low-poly, flat colours, already most of the
   kitchen) plus Quaternius where Kenney has nothing.
2. **Downloads:** Claude lists the packs (name, source, size); the human approves the list once.

## Implementation notes
(Builder fills this in.)
