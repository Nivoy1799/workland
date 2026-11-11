# Style lab (spec 046)

Blender scripts that build the art test set (person, kitchenware, kitchen corner, car, shop-house, food, knob sheet)
in several style variants and render comparison pictures. Nothing here goes into the game; the chosen style becomes
the guide in `docs/art-direction.md`.

## Run

Blender 5.3, headless, from the repo root:

```bash
"/c/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup -P tools/blender/stylelab/render_all.py -- --round 1
```

Output: `docs/art/stylelab/round-<n>/*.jpg` (JPEG, quality 92, 1600 x 1000) plus `compare.html`, the comparison page.

| Option | Meaning |
|---|---|
| `--round N` | output folder `round-N` (default 1) |
| `--hero S3P1` | the variant also rendered at sunset and night, with greyscale and colour-blind checks |
| `--only TEXT` | render only file names containing TEXT (comma-separated) |
| `--quick` | half size, 16 samples, for trying things out |
| `--check` | render one image twice and compare the pixels (determinism) |

A full round takes about 30 seconds on the GPU.

Round 2 (people, hero models, themes) has its own entry point, same options except `--round`:

```bash
"/c/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup -P tools/blender/stylelab/render_round2.py
```

Round 3 (lived-in pass, the climb) the same way with `render_round3.py`. File names end in `_gNN` (grit x 10).

Round 4 (vibe frames with ink outlines) with `render_round4.py`; `--only morning,guest` picks frames, `--no-ink`
renders without outlines. Several frames run in separate Blender processes.

Round 5 (the town in 2222) with `render_round5.py` and round 6 (the whole town plan, its edges, weather and light,
spec 051) with `render_round6.py`, same options as round 4.

## Layout

| File | What it holds |
|---|---|
| `lab/palettes.py` | P1 garden pastel, P2 warm streamline, shared food and skin colours |
| `lab/shapes.py` | S1 soft, S2 streamline, S3 mix: radius, bevel segments, which group uses which |
| `lab/build.py` | mesh helpers (rounded box, lathe, capsule, torus, prism, text) and materials |
| `lab/kitchen.py` | stove, cabinets, hood, pot, pan, plant, floor, knob sheet |
| `lab/people.py` | the cook and the older customer, potato and tomato (whole and cut) |
| `lab/street.py` | car, shop-house, street lamp, tree, ground |
| `lab/scene.py` | shots, cameras, the three light moods, render settings |
| `lab/character.py` | round 2 people: Skin-modifier bodies, layered clothes, faces, hair, props, the cast |
| `lab/hero.py` | round 2 stove, both cars and the shop front at full detail |
| `lab/themes.py` | business themes (one grammar, many dialects) and themed kitchen palettes |
| `lab/scene2.py` | round 2 shots |
| `lab/mess.py` | round 3 wear and mess kit (decals and small props) |
| `lab/climb.py` | round 3 food cart (stage 1) and delivery van |
| `lab/scene3.py` | round 3 shots: grit levels, mid-rush kitchen, the climb, lived-in street |
| `lab/concept.py` | round 4 concept kit: robots, dining, crowd, animals, town blocks, dome, monorail |
| `lab/scene4.py` | round 4 vibe frames and the ink-outline look |
| `lab/future.py` | round 5 kit: towers, sky bridges, hover cars, drones, airship, garden street pieces |
| `lab/scene5.py` | round 5 frames: skyline street (sunset, night), garden promenade, roof drone pad, aerial |
| `lab/world.py` | round 6 kit: low-poly terrain, the Wreck, the old highway, Wildbelt hedge and gate, cows, pigs, deer, ranger robot, lookout, lighthouse, pier, buoys, lifeguard drone, ferry, rain, lightning, clouds, umbrellas |
| `lab/scene6.py` | round 6 frames: the town model (plan of spec 051 in 3D), the Wreck, the Wildbelt, storm, lake fog |
| `lab/post.py` | greyscale, colour-blind simulation (Machado 2009), pixel compare |

## Rules the scripts keep

- Real sizes in metres (art guide 2), person 1.75 m.
- Every variant changes in one place: a palette in `palettes.py`, a shape level in `shapes.py`.
- Deterministic: fixed seeds, no screen-space ray tracing (it made renders differ by a few pixels; URP does not
  have it either). `--check` must report `max=0.0`.
- Glass is solid and glossy, not transparent, so it reads the same in the game.
- No downloads, no outside models or textures.
