---
id: 046
title: Style lab (find our one art style from Blender test renders, then write the style guide)
status: approved
owner: architect
builder: opus
---

# 046: Style lab

## Goal
The human (2026-11-10): one unique style matters. Before anyone builds a lot of models, we want a real design guide.
Show variations on the same key objects as screenshots, pick, iterate, then build everything from that.

So this spec does **not** make game art. It makes **test renders** in Blender, lets the human choose, and ends with a
rewritten `docs/art-direction.md` that every later model follows (spec 040 and after).

## Who we design for
- **Older adults are the main players.** For the look this means:
  - big, clear silhouettes, recognisable from across the room;
  - strong value contrast between an object and its background (lightness, not only hue);
  - little visual noise: few small details, no busy textures;
  - calm motion, no flashing;
  - colour-blind safe: never tell two states apart by red against green only (today UI "good" is Sage and "bad" is
    Terracotta; the lab checks this pair in a deuteranopia and protanopia simulation);
  - nostalgia is welcome: the 1930s–70s hit this audience hardest.
- **The town's story (D27, D28):** a native vegan town after a dark past era. The mood is hopeful, warm, a fresh start.
  Friendly, never grim, never childish.
- **Fun:** things look pleasant to touch: rounded corners, a little chunk, friendly shapes.

## The idea: retro-future, one style, three lights
The town is **the future as people dreamed it in the 1930s–70s, finally built** after the dark era, with plants where
farms used to be. Story hook (not final text): the town was rebuilt from the old "world of tomorrow" plans.

The human liked three reference pictures (2026-11-10). They are inspiration only and stay **out of git**; this spec
describes them:
1. **Streamline painting (1930s retro-future):** a teardrop car with a glass bubble canopy and a ribbed chrome grille;
   buildings with long swooping curves and vertical ribs; stepped towers; one warm colour family (red, rust, ochre)
   with cream highlights; searchlights at night.
2. **Synthwave poster (1980s):** mostly light and colour: a pastel pink-violet sunset, a striped sun, haze, palm
   silhouettes, glowing shop signs.
3. **Pastel kitchen (60s/70s, modern render):** rounded "pill" wall cabinets joined in a chain, a chrome cylinder hood,
   a rounded fridge, a tulip-style chair, a layered pendant lamp, a patchwork tile floor, plants; mint and sage with
   mustard and orange accents and cream tiles; low warm sunlight with long shadows.

What ties them together: rounded, streamlined forms; chrome accents; warm low light; **one main colour family per
scene, plus one accent**.

How they combine:
- **Shapes:** interiors and kitchenware from picture 3; cars, buildings and signs from picture 1. One "streamline"
  language for both.
- **Light over the day:** morning and noon like picture 3 (bright sun); evening like picture 2 (pastel sunset, haze,
  signs lighting up); night like picture 1 (warm red glow, searchlights).
- **Plants everywhere** (picture 3): the quiet vegan "fresh start" signal.

We take ideas, never copies: no brand shapes or logos (no copy of a known fridge, car or designer chair or lamp), no
look of protected retro-future games or cartoons. Our own versions in that spirit.

## What we vary in the lab
- **Shape language, 3 levels:**
  - S1 soft: rounded, pill-like, few details (picture 3);
  - S2 streamline: teardrops, fins, ribs, chrome bands (picture 1);
  - S3 mix: S1 for interiors and small things, S2 for cars, buildings and big stations.
- **Palette, 2–3 options:**
  - P1 garden pastel: mint, sage, mustard, orange, cream (picture 3);
  - P2 warm streamline: rust, red, ochre, cream (picture 1);
  - P3 (optional) today's D32 palette, as a baseline.
- **Light, 3 times of day:** noon, sunset, night. Every shape and palette pair is rendered in all three.
- **Knobs and handles:** a sheet with 4–5 variants: small round chrome knob (picture 3), big ribbed dial, D-handle,
  long bar handle, chrome lever.

Round 1 renders S1, S2 and S3 in P1 and P2 at noon, plus the best-looking pair at sunset and night. Later rounds only
for what the human picks. Changing the palette or proportions means a new line in `docs/decisions.md` (D30, D32).

## The test set (same for every variant)
1. **Person:** one adult, standing in a neutral pose, apron, normal proportions (D30). Static: no rig, no animation.
2. **Kitchenware:** a pot and a frying pan, with lids and handles.
3. **Kitchen corner:** a stove front with 4 knobs and an oven door, a short worktop, one wall cabinet, a hood, a plant.
4. **Car:** a small town car (about 3.8 m).
5. **House:** a small one-storey shop-house with door, shop window, sign, awning and roof (about 6 x 5 m footprint).
6. **Food:** a potato and a tomato, each whole and cut.

Each object is built at real size (art guide 2 size table).

## What the human gets
- For every rendered variant: a **lineup shot** (all objects next to the 1.75 m person, same camera everywhere), a
  **kitchen corner close-up** at cook's eye height and a **street shot** (car in front of the house).
- The **knob and handle sheet**.
- For the strongest variant: the **same shots at sunset and night**, plus a **greyscale shot** and a **colour-blind
  simulation**, to check silhouette and contrast.

All renders go on **one comparison page** (a private artifact) with the variant code under each image (for example
"S3 P1 noon"), so the human can say "S3 with P1, knob 2". Then 1–2 more rounds on the choice.

## How it is made
- Blender 5.3, headless (`blender -b --factory-startup -P script.py`), at
  `C:\Program Files\Blender Foundation\Blender 5.3\blender.exe` or the `BLENDER_EXE` environment variable.
- **One style module per shape level and one per palette** (`tools/blender/stylelab/styles/`): bevel width, segment
  count, polygon budget per metre, proportions, colours. Object scripts only call these, so a variant changes in one
  place.
- **One scene module**: camera, background, render size, and the three light setups (noon, sunset, night).
- EEVEE with light close to what URP can show (art guide 6), so the pictures do not promise a look the game cannot
  reach: warm key light, soft ambient, light bloom on lit signs, a subtle haze at sunset. No ray-traced tricks.
- Fixed seeds; the same script makes the same image.
- Output PNGs go to `docs/art/stylelab/round-<n>/` (committed, 1600 x 1000, about 2 MB at most each).
- No downloads, no external models or textures. Everything is modelled by script.

## After the pick (part of this spec)
1. Rewrite `docs/art-direction.md` as the style guide of the chosen variant:
   - the look in one sentence, the retro-future idea and who we design for;
   - palette with hex values and uses, per scene (main family plus accent);
   - shape rules: bevel width, segment counts, polygon budget, corner radius, how chunky, when to use streamline;
   - proportions of people and props;
   - the knob, handle, trim and chrome language;
   - materials and shading; light and post-processing per time of day;
   - readability rules: contrast, silhouette, colour-blind checks;
   - do and don't, with images from the lab.
2. New lines in `docs/decisions.md`: the chosen style, and every changed decision (D30, D32).
3. Amend spec 040: our own Blender models in the chosen style replace downloads as the model source (its Part A and
   open questions 1 and 2), with the generator folders it may touch. Times of day (light per time) become their own
   spec later.

## Folders this spec may touch
- `tools/blender/stylelab/` (new): Python scripts for Blender.
- `docs/art/stylelab/` (new): render PNGs.
- `docs/art-direction.md`, `docs/decisions.md` (after the human picks), `specs/040-readable-at-a-glance.md` (the amendment).
- `specs/046-style-lab.md` (notes).

**Not** `game/`, `sim/` or anything else. No Unity changes in this spec. A one-time in-engine check of the winner
belongs to spec 040, once Unity and MCP are connected.

## Acceptance criteria
- [ ] AC1: `scripts/verify.sh` passes (nothing in it should change; the Blender scripts are not part of it).
- [ ] AC2: One command per round renders every image (`tools/blender/stylelab/render_all.py`), documented in a short
      `tools/blender/stylelab/README.md`.
- [ ] AC3: Round 1 has S1, S2 and S3 in P1 and P2 at noon (lineup, kitchen corner, street), the knob sheet, and the
      strongest variant at sunset and night with greyscale and colour-blind shots. All share the same cameras.
- [ ] AC4: The comparison page is published and linked in `docs/human-inbox.md`.
- [ ] AC5: Running the same script twice gives the same image (pixel compare on one render).
- [ ] AC6 (human): picks a variant, with or without mixes. Up to 2 more rounds on it.
- [ ] AC7: After the pick, `docs/art-direction.md` is rewritten as described, decision lines are added and spec 040 is
      amended. The human approves the new guide.

## Round 2 (human, 2026-11-10: "I wanna see the best you can")
Round 1 compared styles with quick primitive models. Round 2 shows the quality ceiling before the human picks.
1. **People.** A character generator: smooth bodies grown from a stick skeleton (Blender Skin modifier, rig-ready),
   shoulders, waist, bent elbows and knees, posture by age, mitten hands with a thumb, shoes with soles, layered
   clothes (collar, rolled sleeves, an apron that wraps and ties), friendly faces (eyes with a highlight, nose, ears,
   brows), several hair meshes. Cast: the cook, an older woman, an older man, a younger customer (no children in this round). Shots: a lineup, a turnaround (front, three-quarter, side, back) and face close-ups.
2. **Detail pass** on three hero models at "ship" quality: the stove, both cars (the soft car redesigned), the shop
   front (window with a visible interior, door panels, plinth, cornice, window boxes, menu board, wall lamps).
3. **Personalisation demo: one grammar, many dialects.** Fixed for everyone: shape language, bevels, proportions,
   light, palette structure (a base family plus one accent). Free per business: a theme colour set, patterns,
   signature props, a sign (name, retro font, neon colour). Shown on the same shop front and kitchen corner in three
   themes: the default, a Mexican-inspired one and a Japanese-inspired one. Themes are respectful and inspired by the
   cuisine and its craft (tiles, textiles, lanterns), never caricatures. Fictional shop names.
4. **P1 contrast fix:** darker cabinets, lighter wall, checked in greyscale.

Same folders as round 1. Output `docs/art/stylelab/round-2/` and a new comparison page. Round 1 pictures stay as
they are (made with commit 3552759; the scripts move on).

## Round 3 (human, 2026-11-10: "it looks like marketing ... some roughness is missing ... when all looks glossy it
gets creepy")
A lived-in pass, tied to the climb in `docs/game-design.md` (food corner, first restaurant, services, automation):
1. **Grit as a setting** (0 clean, 1 lived-in, 2 rough) instead of hand-placed dirt: mostly matte materials, small
   colour differences between objects, grime where it collects (floor level, corners, around the stove), brushed
   chrome. Game equivalent: baked occlusion plus a shared dirt texture and decals.
2. **Wear and mess kit:** stains and splatter, chipped and missing tiles, cracked plaster, a faded poster, pavement
   cracks with weeds, crates, boxes, a trash bag, dirty dishes, a puddle under a drip, duct tape, a dented bin,
   crooked signs, handwritten paper signs.
3. **People:** mixed expressions (neutral, focused, tired, small smile, yawn), slight asymmetry, more body variety,
   wear on clothes (apron stains, untucked shirt, messy hair).
4. **Light and camera:** eye-level, slightly off-centre framing; an overcast light.
5. **The climb in pictures:** stage 1 food corner (a new scrappy cart with stools, cash tin, taped menu), stage 2 a
   worn rented shop with the last tenant's leftovers, a later stage kept up but lived-in with a delivery van (new
   model). Plus the same kitchen clean and mid-rush, to show a dirt layer the game can switch on and off.

Same folders. Output `docs/art/stylelab/round-3/` and a new comparison page.

## Open questions
0. **Real-world cultures in the town?** So far the world has only its own fictional history (D27, D28). Can a
   business be "Mexican-inspired" or "Japanese-inspired", or do we invent fictional regional cuisines that echo real
   ones? Default for round 2: "inspired-by" themes with fictional shop names, no real place names in the game; needs a
   decision line before it goes into the game.
1. **Older adults as the main players:** is that the right reading of "focused on older generation"? It also shapes
   UI text size and controls later (not in this spec). Default: yes.
2. **Characters:** if the chosen style is far from the Quaternius people, our own characters need rigging and
   animation, which is a big task. Default: decide after the pick, in its own spec.
3. **Palm trees and beach mood (picture 2):** keep the sunset light only, or also a seaside town? Default: light only;
   the town keeps its own trees.

## Implementation notes
Round 1 (2026-11-10, Opus, branch `spec/046-style-lab`):
- Scripts in `tools/blender/stylelab/` (see its README). One command renders the round in about 30 s.
- Comparison page: https://claude.ai/artifact/Xog7ze64S12nDLkZwCqgFt (private; source
  `docs/art/stylelab/round-1/compare.html`). Not yet in `docs/human-inbox.md`: the autonomous run edits the inbox on
  its branch, so the link goes in when this branch merges, to avoid a conflict.
- Small additions to the plan, all inside the spec's folders:
  - two more shots per variant: **people** (the cook and an older customer with glasses, close up) and **food**
    (board with whole and cut potato and tomato), because both are too small to judge in the lineup;
  - an older customer next to the cook in every scene (older players are the main audience);
  - JPEG at quality 92 instead of PNG: 47 PNGs were 65 MB, the JPEGs are 5.5 MB (largest 170 KB).
- Determinism (AC5): `--check` gives `max=0.0`. EEVEE screen-space ray tracing made two renders differ by up to
  11/255 on 0.7 % of pixels, so it is off (URP has no equivalent either).
- Glass is solid glossy, not transparent: transparent glass gave sorting artefacts and would behave the same way in
  the game. Shop windows glow warm after sunset.
- Findings for the human (also on the page):
  - S1 kitchen (pill cabinets, patchwork floor) is the strongest interior; S2 gives the street character (ribbed
    tower, curved corners, rocket car).
  - P1 has too little value contrast: in greyscale the mint cabinets, mustard doors and sage wall merge.
  - With deuteranopia the orange stove and mustard doors turn the same yellow and the tomato loses its red, so shape
    must carry meaning.
  - Weak spots: the S1 car, stiff people (flat apron, tube bodies), night darker than a game can be.
- `verify.sh` passes (Unity step skipped: nothing in `game/` changed).

Acceptance status: AC1, AC2, AC3, AC5 done. AC4 page published (inbox link at merge). AC6 and AC7 wait for the
human's pick.

Round 2 (2026-11-10, Opus):
- Page: https://claude.ai/artifact/84rWfWi2tKQ81W5FYA37rk (source `docs/art/stylelab/round-2/compare.html`),
  25 pictures, rendered by `tools/blender/stylelab/render_round2.py` in about 30 s.
- `lab/character.py`: Skin-modifier bodies (rig-ready), layered clothes with at least 1.5 cm between layers, faces,
  six hair styles, hats, glasses, props. Cast of four: cook, older woman, older man, younger customer.
- `lab/hero.py`: stove, soft bubble car (redesigned), streamline rocket car, shop front with a room behind the
  window (boolean cut-outs parented to the shop).
- `lab/themes.py`: default, Casa Nopal (Mexican-inspired) and Yuzu Noodles (Japanese-inspired) shop themes and
  themed kitchen palettes. Open question 0 (real-world cultures) is still open.
- P1 contrast: cabinets #4F9A85, wall #E2EADD; round 1 values kept as `P1r1` for the before/after shots.
- Known weak spots, listed on the page: stiff pose, not rigged or animated, chunky shoes, apron bib off the chest
  from the side, simple shop interiors, dark nights. Round 1 can no longer be re-rendered exactly (scripts moved on;
  its pictures are from commit 3552759).

Round 3 (2026-11-10, Opus):
- Page: https://claude.ai/artifact/85pVE6NdKNjyJt1jBc1A4w (source `docs/art/stylelab/round-3/compare.html`),
  15 pictures, rendered by `tools/blender/stylelab/render_round3.py`.
- Grit (`build.set_grit`): per-object colour drift (Object Info random), grime from occlusion and a band at the foot
  of upright faces, broken up by noise, rougher and uneven gloss. Grit 0 leaves rounds 1 and 2 unchanged.
- `lab/mess.py`: stains, splatter, cracks, tape, handwritten paper signs (Windows Ink Free font, mock-up only),
  faded posters, puddles, gum, weeds, crates, boxes, trash bags, a dented bin, dish stacks, a dripping pipe, stools,
  cables.
- People: six expressions (brows, lids, mouth), small asymmetries, apron stains, untucked shirts, rolled trousers,
  hair tufts; a new helper.
- `lab/climb.py`: the stage 1 food cart and a delivery van. Overcast light added.
- Shots: grit 0/1/2, clean versus mid-rush kitchen, stage 1 (noon, sunset), stage 2 (overcast), a later stage
  (noon, sunset), the cart, the van, the round 2 street lived-in.
- For the game later: decals and a prop kit for the mess, a spec for the dirt layer (when it appears, how cleaning
  removes it), a licensed handwriting font.

## Blockers
(none)
