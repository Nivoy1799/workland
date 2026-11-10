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
| `lab/post.py` | greyscale, colour-blind simulation (Machado 2009), pixel compare |

## Rules the scripts keep

- Real sizes in metres (art guide 2), person 1.75 m.
- Every variant changes in one place: a palette in `palettes.py`, a shape level in `shapes.py`.
- Deterministic: fixed seeds, no screen-space ray tracing (it made renders differ by a few pixels; URP does not
  have it either). `--check` must report `max=0.0`.
- Glass is solid and glossy, not transparent, so it reads the same in the game.
- No downloads, no outside models or textures.
