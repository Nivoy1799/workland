# Art direction v2: the look of Haven Nine (DRAFT for approval, 2026-11-11)

Status: **draft, waiting for the human's approval.** This is the result of the style lab (spec 046, rounds 1–8). Once
approved, it is merged into `docs/art-direction.md` on main, which stays the one place that decides how the game
looks:
- sections 2 (scale, D29), 3 (proportions and characters, D30) and 4 (motion, D31) of that file **stay unchanged**;
- sections 1, 5, 6 and 7 are **replaced** by the matching sections here;
- sections 8–14 here are **new**.

The world it serves is `docs/town-plan.md` (v1) and `docs/town-story.md` (draft 3).

The picks marked *(proposed)* are the human's to confirm. They come from the lab's open question ("S1, S2 or S3, P1 or
P2, how much wear"). Every lab frame since round 3 already used them.

Pictures: round 8 https://claude.ai/artifact/VCszDibQnpUFGyD4oKEzNm; earlier rounds are listed in section 13.

## 1. The look in one sentence
**The year 2222 as people in 1950–80 imagined it, fifty years after its golden age: low-poly and flat-shaded, rounded
retro-future shapes in garden pastels, worn and grey where the town has faded, warm wood and lamplight where people
cook by hand, and brighter street by street as the players bring the town back.**

What it is not:
- **not steampunk:** no brass, gears, rivets for show or Victorian shapes (docs/setting.md);
- **not a clean "heaven":** no sterile white, and no level is ever spotless (section 6);
- **not grimdark:** even the darkest street has one warm light.

## 5. Colour (revises D32)
One palette, defined once in code (`Art/Palette.cs`) and once for UI (`UI/Palette.uss`), as today. The names in the
first table keep D32's names where the use is the same, so code changes stay small.

### 5.1 The town (P1 garden pastel with the round 2 contrast fix) *(proposed)*
| Name | Hex | Use |
|---|---|---|
| Cream | `#F3E9D2` | light walls, shirts, UI text on dark |
| Plaster | `#E2EADD` | interior walls (about L\*92) |
| WallOut | `#F1E6CF` | outside walls |
| Mint | `#8CC5AC` | doors, cars, retro accents (about L\*74) |
| Teal | `#4F9A85` | cabinets, main brand colour (about L\*58) |
| TealDark | `#2F6E5E` | trim, signs, outlines on light |
| Mustard | `#E3AE45` | highlights, aprons, stars |
| Orange | `#E58436` | cars, awnings, UI accent |
| Salmon | `#E8957A` | awnings, pastel accent (docs/setting.md) |
| Sky | `#9CC3D5` | sky, water tint, info |
| DeepBlue | `#4E6E8E` | trousers, uniforms, signs |
| Terracotta | `#C9734F` | roofs |
| WoodLight | `#B98B5E` | worktops, furniture: the hand-made warmth |
| WoodDark | `#8A5A3B` | frames, handles, carts |
| Chrome | `#C9CDD0` | trim bands, cookware, robot parts |
| Charcoal | `#3A3532` | dark furniture |
| Leaf, Plant | `#5E8F4F`, `#3F6B3A` | plants, trees |
| Ground | `#9DB585` | grass |
| Pavement, Road | `#D8CDB8`, `#6F6A66` | streets |

### 5.2 Light and signs (emissive only)
| Name | Hex | Use |
|---|---|---|
| LampWarm | `#FFD9A0` | windows, street lamps, string lights, the Dome's lit ribs |
| Sodium | `#FFA040` | Low Ring street lamps only |
| NeonCherry | `#FF5A6E` | signs |
| NeonPink | `#E58AA6` | signs (betting halls, the ODDS sign) |
| NeonTeal | `#5FD3C4` | signs, screens |
| DreamBlue | `#6E8BFF` | dream booths only |

### 5.3 The Low Ring, the Dome and outside
| Name | Hex | Use |
|---|---|---|
| Soot, SootLight | `#2B2826`, `#3B3632` | Low Ring walls |
| Rust | `#7A4A30` | rust streaks, broken robots, ducts |
| Asphalt | `#26282A` | wet Low Ring street |
| Glass | `#CFE3E8` at 12–22 % | Dome glass |
| Rib | `#F1ECE2` | Dome ribs and lattice by day |
| Storm, StormDark | `#2C313A`, `#1D2128` | the Storm Ring clouds |
| Outside | `#8A8272` | bare land outside the Dome |

### 5.4 UI (unchanged from D32)
Ink `#2A2624`, Panel `#2E2A27` at 85 %, Interact `#F5D27A`.

### 5.5 Rules
- **Value steps for older eyes:** walls light (about L\*92), doors middle (about L\*74), cabinets dark (about
  L\*58). Every new set is checked in greyscale: neighbours must not merge.
- **Food** keeps natural colours, slightly muted (saturation at most about 70 %), in every district and at every
  revival level.
- **Neon and LampWarm** only on things that are on. A dead sign uses its colour at 25 % brightness, not the emissive.

## 6. Light and materials
- URP Lit, flat-shaded low-poly, no normal maps, no photo textures (unchanged).
- **Wear follows the revival** *(proposed)*. Wear is made of:
  - colour drift per object (a few percent);
  - grime in corners and a dark band at the foot of walls;
  - streaks under windows and signs;
  - rougher, uneven gloss.

  In Unity it is vertex-colour grime plus a small set of flat decal quads (streaks, stains, cracks, posters), never
  photo textures.

| Revival level | Wear | In the lab |
|---|---|---|
| 0 Grey | rough (lab grit 2): grime, streaks, colour drift up to 8 %, dead signs | grit 0.9–1.0 |
| 1 Patched | rough in corners, cleaner on main surfaces | grit 0.7 |
| 2 Lively | lived-in (lab grit 1) | grit 0.5 |
| 3 Thriving | lived-in, **never 0**: wear on handles, worn paths, patina | grit 0.4 |

- **Hand-made places** (kitchens, carts, the players' restaurant) are always lived-in and warm, whatever the
  district's level: warm wood, handwritten signs, clutter in order.
- **Light moods** (the lab's values are a reference for direction and colour; Unity intensities are tuned in the
  W7 spec):

| Mood | Sun | Sky (top → bottom) | Notes |
|---|---|---|---|
| Morning | warm `#FFD9B0`, low (16°), from the side | `#9FC0D3` → `#F6DCC0` | long shadows |
| Noon (calm) | `#FFF1DC`, 34–48° | `#7FA6C4` → `#E9DCC6` | the calm eye over the Dome |
| Sunset | `#FF9A5C`, 9° | `#6E5490` → `#F4A27E` | the lights start |
| Night | cold `#8FA6FF`, weak | `#0B1020` → `#2C2645` | never pitch dark; LampWarm everywhere |
| Storm, Rain Hour | `#AEB8D0`, weak | `#1C212A` → `#4A505B` | lightning on the glass |
| Low Ring, any hour | sun mostly blocked | as the hour | sodium lamps and neon carry the light |

- **The lights at dusk come on in a wave:** street lamps first, then signs and windows, each after a small fixed
  (seeded) delay. Then the Dome's ribs come on, only over districts at level 2 or higher.
- **Night readability:** a minimum brightness on paths and counters at all times (spec 051 section 6).

## 7. Text in the world and UI
- World labels: unchanged (Cream on Panel, never overlapping).
- **Signs are part of the revival.** At level 0 a sign is dead or has missing letters ("HA EN NINE"). At level 1 it
  flickers. From level 2 it is lit. The text never changes, only its state.
- Posters and plaques carry the story (the Charter, the museum, the Albatross). They are readable at 2 m in first
  person, Cream or Ink on a plain field, one font family for signs.

## 8. Shapes *(proposed: S3 mix)*
- **S1 soft for interiors and small things:** pill-shaped cabinets, rounded counters, big radii, few details. Round
  2's kitchen is the reference.
- **S2 streamline for cars, buildings and big stations:** teardrops, fins, ribs, chrome bands, Googie motifs
  (boomerangs, starbursts, tree columns).
- Corners are always rounded. A hard box is only for the Low Ring and service structures.
- The Dome and big structures are organic: curves and branching ribs like leaf veins, never a flat lid or a plain
  grid.

## 9. The four revival looks (what Unity swaps)
One district component reads the level from the server and switches sets. The building shells never change.

| Element | 0 Grey | 1 Patched | 2 Lively | 3 Thriving |
|---|---|---|---|---|
| Street lamps | half dead, one flickering | all on | on, plus string lights | on, string lights, festoons |
| Signs | dead, missing letters | flickering, some lit | lit | lit, new neon |
| Windows and shutters | mostly shuttered, dark | half open | open, warm light | open, plants on sills |
| Litter and grime | overflowing bins, litter, posters peeling | bins emptied, less litter | clean, a little patina | clean, patina only |
| Plants | dead stalks, empty beds | potted plants by doors | beds replanted | full gardens, flowers by season |
| Robots | slumped, broken, looping lines | a few moving | working | working, polished |
| People | few, listless (sitting, staring) | some out, talking | busy, evening crowds | crowds, music, festivals |
| Dome ribs at night | dark over the district | dark | lit | lit |
| Wear (section 6) | rough | rough in corners | lived-in | lived-in |

Station Square and Main Street get all four looks first (v1); every other district follows.

## 10. The Dome, the borders and outside
- **The Dome:**
  - glass at 12–22 % with a fine lattice;
  - main ribs in Rib by day and LampWarm by night;
  - about 18 main ribs that branch, and three rings.
  - The glass casts no shadow; the ribs do.
- **The borders:** concrete sills at the foot of the wall, the riveted seal collar at the Wreck ("SEALED 2161"), the
  water gate with yellow and black warning bands, the round pressure door. Warning bands use Mustard and Charcoal.
- **Outside:** bare land in Outside tones, dead grey trees, dark water, and the Storm Ring as a black cloud wall on
  every horizon with lightning in it. It is never walkable in v1.

## 11. Kits
- **Low Ring:**
  - soot facades;
  - concrete service ducts and vent haze (industrial, never steampunk);
  - sodium lamps (some dead);
  - neon signs with dead letters;
  - betting screens;
  - dream booths;
  - scrap pile;
  - overflowing bins;
  - puddles on wet asphalt;
  - one warm food cart.
- **Robots** (1950s style, docs/setting.md: dome heads, antennas, a single wheel or tripod legs, tube arms), in two
  variants:
  - *Broken:* leaning or slumped, cracked dome, a missing arm, rust streaks, a screen that loops an old line.
  - *Working:* upright, clean Cream and Teal, eyes lit in LampWarm.
- **The Wreck:** the Albatross hull in a warm grey skin with Rust patches, moss, a few trees on top, the name
  faded; the market in its belly lit warm.

## 12. Outlines and concept art
- The style lab's ink outlines (Blender Freestyle) belong to **concept art only**. The game has **no outline
  shader** in v1: flat shading and value steps carry the shapes.
- Night concept frames render without ink, because in the dark the outlines read as a white wireframe.
- The Blender lab (`tools/blender/stylelab/`) is a reference, not a source of game assets. Game models come from the
  packs (D26) behind the swap layer, recoloured from this palette, plus simple custom pieces where no pack fits
  (Dome, borders, signs).

## 13. Reference frames (what each rule looks like)
Folder `docs/art/stylelab/`:

| Rule | Frame |
|---|---|
| Interiors, S3, palette, value steps | round-2 `kitchen_S3_P1_noon.jpg` and its `_grey` check, `stove_S1_P1_noon.jpg` |
| Streamline outside (S2) | round-2 `shop_S2_P1_noon.jpg`, `car_S2_P1_noon.jpg` |
| Wear from clean to rough | round-3 `kitchen_S3_P1_noon_g00`, `_g10`, `_g20`; `stage2_S3_P1_overcast_g20` |
| Hand-made warmth, the lived-in vibe | round-4 `vibe_morning`, `vibe_rush`, `vibe_guest` |
| The street at sunset and night | round-5 `r5_skyline-sunset`, `r5_skyline-night` |
| The whole town and its districts | round-6 `r6_model`, round-7 `r7_canopy` |
| Seasons (later) | round-7 `r7_spring`, `r7_summer`, `r7_autumn`, `r7_winter` |
| The Wreck | round-7 `r7_wreck_ark`; round-8 `r8_wreck_wall` |
| The Dome by day and night | round-8 `r8_dome_air`, `r8_dome_night` |
| Borders | round-8 `r8_wreck_wall`, `r8_lake_wall`, `r8_train_gate` |
| Low Ring, level 0 at night | round-8 `r8_low_ring` |

Not drawn yet: the same street at the four revival levels. That is the most useful next frame (open question 4).

## 14. Checklist for a spec that adds art (adds to the old section 8)
- [ ] Colours only from the palette in section 5; emissive only on things that are on.
- [ ] Greyscale check: neighbouring surfaces do not merge.
- [ ] If it stands in a district: its look at levels 0–3 (section 9), as swaps, not new shells.
- [ ] Wear at the level's grade (section 6), never spotless.
- [ ] No steampunk cues.

## Decision lines to add when approved
Numbers come at merge (after main's D40):
- **Look v2:** shapes S3 (soft inside, streamline outside), palette P1 with contrast fix plus light, Low Ring and
  Dome colours (revises D32).
- **Wear follows the revival**, never spotless.
- **No outline shader in the game**; ink is concept art only.

## Open questions
1. Shapes S3 *(proposed)*, or S1 or S2 everywhere?
2. Palette P1 with contrast fix *(proposed)*, or P2 warm streamline?
3. Wear following the revival *(proposed)*, from rough at level 0 to lived-in at level 3?
4. Next lab frame: the same street at revival levels 0 to 3, side by side *(proposed)*?
