# Art direction (visual guide)

The one place that says how Worktown looks. Every spec that adds or changes art follows it; a
deviation needs a human decision and a line in `docs/decisions.md`. Decided by the human on
2026-11-08 (D29–D32). How to add a model technically: `docs/assets.md`.

## 1. Look in one sentence
A warm, calm, low-poly small town: real-world proportions, soft muted colours, flat-shaded models
from one pack family per slot type (Kenney first, D26), readable at a glance in first and third person.

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

## 5. Colour (D32: warm muted palette)
One palette, defined once in code (`Art/Palette.cs`) and once for UI (`UI/Palette.uss`
variables). No hard-coded `new Color(...)` outside these two files (the spec 026 test enforces it
for `game/Assets/_Project/Scripts`). Kenney textures are kept; models are only recoloured through
material tints from the palette when they clash.

| Name | Hex | Use |
|---|---|---|
| Cream | `#F2E6D0` | walls, light surfaces, UI text on dark |
| Plaster | `#E4D5BC` | secondary walls, floor tiles |
| WoodLight | `#C8A27A` | worktops, furniture |
| WoodDark | `#8A5A3B` | frames, handles, doors |
| Terracotta | `#C8664A` | roofs, accents, UI "bad" |
| Brick | `#A4503C` | building walls |
| Mustard | `#D9A441` | highlights, UI accent, stars |
| Sage | `#8FA77E` | plants, UI "good" |
| Leaf | `#5E7F4F` | trees, deep greens |
| SoftBlue | `#7FA3B8` | sky tint, water, info |
| DeepBlue | `#3F5F78` | uniforms, signs |
| Steel | `#9AA0A3` | cookware, metal |
| Charcoal | `#3A3532` | dark furniture, outlines |
| Ink | `#2A2624` | UI text on light |
| Panel | `#2E2A27` at 85 % | UI panels |
| Interact | `#F5D27A` | interaction highlight only |

Food keeps natural colours (potato brown, tomato red), slightly muted to sit in the palette
(saturation at most about 70 %). Customer tints pick from Cream, Terracotta, Mustard, Sage,
SoftBlue, DeepBlue.

## 6. Light and materials
- URP Lit, flat-shaded low-poly, no normal maps, no photo textures.
- One warm key light (sun) plus soft ambient; shadows on.
- Emissive only for things that are on (burners, screens).

## 7. Text in the world and UI
- World labels: Cream on a Panel background, never overlapping (stack or fade by distance).
- UI uses the palette variables only; one font family.

## 8. Checklist for a spec that adds art
- [ ] Size from the table above (add a row if the thing is new).
- [ ] Colours only from the palette.
- [ ] Pack family per slot type unchanged, or a decision row.
- [ ] Animated things: measured clip speed, no foot sliding.
- [ ] Screenshot in first and third person next to a person for scale.
