# Art direction (visual guide)

The one place that says how Worktown looks. Every spec that adds or changes art follows it; a
deviation needs a human decision and a line in `docs/decisions.md`. Decided by the human on
2026-11-08 (D29–D32). How to add a model technically: `docs/assets.md`.

Status: **v2, approved 2026-11-11 (D49-D51).** Folded in from the style lab (spec 063); sections 2-4 are
unchanged from D29-D31. The world it serves is `docs/town-plan.md` and `docs/town-story.md`.

## 1. The look in one sentence
**The year 2222 as people in 1950–80 imagined it, fifty years after its golden age: low-poly and flat-shaded, rounded
retro-future shapes in garden pastels, worn and grey where the town has faded, warm wood and lamplight where people
cook by hand, and brighter street by street as the players bring the town back.**

What it is not:
- **not steampunk:** no brass, gears, rivets for show or Victorian shapes (docs/setting.md);
- **not a clean "heaven":** no sterile white, and no level is ever spotless (section 6);
- **not grimdark:** even the darkest street has one warm light.

## 2. Scale (D29: real metric, readable props)
1 Unity unit = 1 metre. Everything is built at **real-world size**, except hand-held items, which
get a fixed readability factor so they read well in first person and are easy to aim at.

| Class | Factor | Rule |
|---|---|---|
| World (people, furniture, buildings, streets) | x1.0 | real size |
| Cookware and tools (pan, pot, plate, bowl, knife, cutting board) | x1.15 | real size x1.15 |
| Small items (real size under 15 cm: food, pieces) | x1.5 | real size x1.5 |

### Reference sizes (target in the game, metres)
Largest dimension unless stated. Tolerance for the automated check: ±15 %.

| Thing | Real | In game | Notes |
|---|---|---|---|
| Person (player) | 1.75 | 1.75 | customers vary 1.60–1.85 |
| Eye height (first person) | 1.63 | 1.60 | `ViewRules.EyeHeight` |
| Door | 2.10 x 0.90 | same | |
| Room height | 2.70–3.00 | same | one storey = 3.0 m |
| Kitchen worktop / stove top | 0.90 high, 0.60 deep | same | place surfaces sit at 0.90 |
| Serving counter | 1.05 high | same | |
| Table / chair seat | 0.75 / 0.45 high | same | |
| Fridge | 1.80 high | same | |
| Frying pan (with handle) | 0.45 | 0.52 | x1.15 |
| Pot | 0.30 | 0.35 | x1.15 |
| Plate | 0.26 | 0.30 | x1.15 |
| Bowl | 0.18 | 0.21 | x1.15 |
| Chef's knife | 0.30 | 0.35 | x1.15 |
| Cutting board | 0.40 | 0.46 | x1.15 |
| Fryer basket (with handle) | 0.45 | 0.52 | x1.15; added in spec 029b (the basket model of spec 029) |
| Spatula | 0.30 | 0.35 | x1.15; the griddle spatula (Kenney cooking-spatula); added in spec 030 |
| Apple / onion | 0.08 | 0.12 | x1.5 |
| Potato | 0.09 | 0.135 | x1.5 |
| Tomato | 0.07 | 0.105 | x1.5 |
| Tomato slice | 0.07 | 0.105 | x1.5 |
| Fries (portion) | 0.14 | 0.21 | x1.5 |
| Flatbread | 0.22 | same | over 15 cm: real size; added in spec 028 |
| Wrap | 0.25 | same | over 15 cm: real size (the hand footprint is 0.25); added in spec 028 |
| Seitan block | 0.12 | 0.18 | x1.5; added in spec 028 |
| Falafel mix | 0.10 | 0.15 | x1.5; added in spec 028 |
| Falafel ball | 0.035 | 0.0525 | x1.5; four to a portion; added in spec 029b (the ball of spec 029) |
| Lettuce | 0.18 | same | over 15 cm: real size; added in spec 028 |
| Cucumber | 0.20 | same | over 15 cm: real size; added in spec 028 |
| Red cabbage | 0.16 | same | over 15 cm: real size; added in spec 028 |
| Lemon | 0.07 | 0.105 | x1.5; the half is the same width; added in spec 028 |
| Sauce bottle | 0.20 | same | over 15 cm: real size; added in spec 028 |
| Paprika | 0.10 | 0.15 | x1.5; added in spec 030 |
| Eggplant | 0.20 | same | over 15 cm: real size; added in spec 030 |
| Mushroom | 0.06 | 0.09 | x1.5; added in spec 030 |
| Wrapped wrap (paper) | 0.25 | same | over 15 cm: real size; the folded wrap in wrap paper; added in spec 031 |
| Takeaway box | 0.20 | same | over 15 cm: real size; the Kenney styrofoam box; added in spec 031 |
| Paper bag | 0.28 | same | over 15 cm: real size; the Kenney bag; added in spec 031 |
| Register | 0.24 | same | height above the counter top (the display); 1.0 x 0.5 m with its change mat and card terminal; graybox; added in spec 032 |
| Drink dispenser | 0.56 | same | height above the counter top; 0.84 x 0.42 m with three taps, the cup stack and the lid stack; graybox; added in spec 033 |
| Takeaway cup (400 ml) | 0.12 | 0.18 | x1.5; the Kenney soda glass, a lid and the fill line drawn on it; added in spec 033 |
| Ordering terminal | 0.46 | same | height of the wall screen; 1.0 x 0.46 m, 2 cm deep, on the back wall above the landing worktop; graybox; added in spec 034 |
| Delivery box | 0.40 | same | largest side; the Kenney cardboard box (Furniture Kit), carried with both hands; added in spec 034 |
| Dish return | 0.30 | same | height above the counter top; a 0.60 x 0.40 m rack on the front counter where eat-in guests' dirty plates come back; graybox; added in spec 035 |
| Kitchen trash can | 0.85 | same | height; 0.46 x 0.46 m with a flip lid, by the staff gap; graybox; added in spec 035 |
| Bin (outside) | 1.10 | same | height; a 0.62 x 0.74 m wheelie bin outside the staff gap; graybox; added in spec 035 |
| Trash bag | 0.45 | same | largest side (world size: carried with both hands); graybox; added in spec 035 |
| Wipe cloth | 0.25 | 0.29 | x1.15 (a tool, like the knife); folded flat, 2 cm thick; graybox; added in spec 035 |
| Seitan spit | 0.78 | same | height above the counter top; a 0.56 x 0.52 m base with the heat panel, the turning cone (0.5 m of stacked discs, 0.17 to 0.27 m across) and the catch tray; graybox; added in spec 036 |
| Menu board | 0.50 | same | height of the hanging board; 0.70 x 0.50 m, 3 cm deep, above the front counter between the serving counter and the register, its face and four buttons towards the cook; graybox; added in spec 037 |
| Small house (2 storeys, to the ridge) | 7–8 | same | |
| Shop (ground floor 4 m + 1 storey) | 7–8 | same | |
| Tree small / large | 4 / 8 | same | |
| Street lamp | 4.5 | same | |
| Road lane / sidewalk | 3.0 / 2.0 wide | same | target; today the road tiles are 5.0 m (two 2.5 m lanes) until a street layout spec |
| Planter | 1.6 | same | added in spec 026 |
| Dumpster | 1.8 | same | added in spec 026 |
| Trash can (height) | 0.9 | same | added in spec 026 |
| Fence section | 2.4 | same | added in spec 026 |
| Awning | 2.4 | same | largest side (width); added in spec 026 |

Fit by **target size in metres from this table**, never by a free scale factor (`Scaled(..., 5f)`
style entries go away in spec 026).

## 3. Proportions and characters (D30)
- People have **normal human proportions** (head about 1/7 of the body), no chibi or toy style.
- Players and customers come from the same pack family; customers differ by variant and palette tints.
- Animations come from the pack (idle, walk, run at least). Clip playback speed follows the
  **measured** stride of the clip (foot travel per loop), never an estimate, so feet do not slide.

## 4. Motion (D31)
| | m/s |
|---|---|
| Walk | 2.5 |
| Sprint | 5.0 |
| Crouch | 1.25 |
| Customers walk | 2.0 (current; shift timings are tuned to it, may drop to 1.4 later) |

Animation, camera and sounds are tuned to these speeds. Feet may slide at most 10 % (checked from
the measured stride and the clamp range of the playback rate).

## 5. Colour (revises D32)
One palette, defined once in code (`Art/Palette.cs`) and once for UI (`UI/Palette.uss`), as today. The names in the
first table keep D32's names where the use is the same, so code changes stay small.

### 5.1 The town (P1 garden pastel with the round 2 contrast fix)
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
- **Wear follows the revival**. Wear is made of:
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

## 8. Shapes
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

Not drawn yet: the same street at the four revival levels. That is the most useful next lab frame.

## 14. Checklist for a spec that adds art
- [ ] Size from the table in section 2 (add a row if the thing is new).
- [ ] Colours only from the palette in section 5; emissive only on things that are on.
- [ ] Greyscale check: neighbouring surfaces do not merge.
- [ ] Pack family per slot type unchanged, or a decision row.
- [ ] Animated things: measured clip speed, no foot sliding.
- [ ] If it stands in a district: its look at levels 0-3 (section 9), as swaps, not new shells.
- [ ] Wear at the level's grade (section 6), never spotless.
- [ ] No steampunk cues.
- [ ] Screenshot in first and third person next to a person for scale.
