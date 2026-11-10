"""Spec 046 style lab: render one round of test images (JPEG, quality 92).

Run (Windows, from the repo root):
    "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b --factory-startup \
        -P tools/blender/stylelab/render_all.py -- --round 1

Options after "--":
    --round N        output folder docs/art/stylelab/round-N (default 1)
    --hero S3P1      the variant also rendered at sunset and night, with greyscale and colour-blind checks
    --only TEXT      render only images whose file name contains TEXT (comma-separated for several)
    --quick          half size, few samples (for trying things out)
    --check          render one image twice and compare the pixels (determinism, AC5)
"""

import argparse
import os
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from lab import post, scene  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
VARIANTS = ("S1", "S2", "S3")
PALETTES = ("P1", "P2")
SHOTS = ("lineup", "kitchen", "street", "people", "food")


def jobs(hero):
    hs, hp = hero[:2], hero[2:]
    out = []
    for s in VARIANTS:
        for p in PALETTES:
            for shot in SHOTS:
                out.append((s, p, shot, "noon"))
    out.append((hs, hp, "knobs", "noon"))
    for t in ("sunset", "night"):
        for shot in SHOTS:
            out.append((hs, hp, shot, t))
    return out


def name(job):
    s, p, shot, t = job
    if shot == "knobs":
        return "knobs.jpg"
    return f"{s}_{p}_{shot}_{t}.jpg"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--round", type=int, default=1)
    ap.add_argument("--hero", default="S3P1")
    ap.add_argument("--only", default="")
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args(argv)

    out_dir = os.path.join(REPO, "docs", "art", "stylelab", f"round-{args.round}")
    os.makedirs(out_dir, exist_ok=True)
    samples, scale = (16, 0.5) if args.quick else (64, 1.0)

    if args.check:
        tmp = os.path.join(tempfile.gettempdir(), "stylelab_check")
        os.makedirs(tmp, exist_ok=True)
        paths = [os.path.join(tmp, f"check_{i}.jpg") for i in (1, 2)]
        for pth in paths:
            scene.render("S3", "P1", "kitchen", "noon", pth, samples, scale)
        worst, mean, share = post.difference(*paths)
        print(f"STYLELAB_CHECK max={worst:.1f} mean={mean:.3f} pixels_off={share * 100:.2f}%")
        return

    filters = [f for f in args.only.split(",") if f]
    todo = [j for j in jobs(args.hero) if not filters or any(f in name(j) for f in filters)]
    for i, job in enumerate(todo, 1):
        path = os.path.join(out_dir, name(job))
        t0 = time.time()
        scene.render(*job, path, samples, scale)
        print(f"STYLELAB [{i}/{len(todo)}] {name(job)} {time.time() - t0:.1f}s", flush=True)

    # Readability checks on the hero variant.
    hs, hp = args.hero[:2], args.hero[2:]
    for shot in ("kitchen", "street"):
        src = os.path.join(out_dir, f"{hs}_{hp}_{shot}_noon.jpg")
        if not os.path.exists(src):
            continue
        post.greyscale(src, os.path.join(out_dir, f"{hs}_{hp}_{shot}_noon_grey.jpg"))
        post.colour_blind(src, os.path.join(out_dir, f"{hs}_{hp}_{shot}_noon_deuteranopia.jpg"), "deuteranopia")
        post.colour_blind(src, os.path.join(out_dir, f"{hs}_{hp}_{shot}_noon_protanopia.jpg"), "protanopia")
    print("STYLELAB done", flush=True)


if __name__ == "__main__":
    main()
