"""Spec 046 style lab, round 2: people, hero models and themes (JPEG, quality 92).

Run (from the repo root):
    "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b --factory-startup \
        -P tools/blender/stylelab/render_round2.py -- [--only TEXT] [--quick]
"""

import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from lab import post, scene2  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))


def jobs():
    out = []
    # People.
    for shot in ("cast", "turn-cook", "turn-woman", "turn-man", "faces"):
        out.append(("S3", "P1", shot, "noon"))
    # Hero models in both shape languages.
    for s in ("S1", "S2"):
        for shot in ("stove", "car", "shop"):
            out.append((s, "P1", shot, "noon"))
    # Themes: the row in both shape languages, close-ups and kitchens in the mix.
    out += [("S1", "P1", "themes", "noon"), ("S2", "P1", "themes", "noon")]
    for shot in ("shop-mex", "shop-jap", "kitchen", "kitchen-mex", "kitchen-jap"):
        out.append(("S3", "P1", shot, "noon"))
    # The whole street through the day.
    for t in ("noon", "sunset", "night"):
        out.append(("S3", "P1", "street", t))
    # Contrast: round 1 P1 against the new P1.
    out += [("S3", "P1r1", "kitchen", "noon")]
    return out


def name(job):
    s, p, shot, t = job
    return f"{shot}_{s}_{p}_{t}.jpg"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--out", default=os.path.join(REPO, "docs", "art", "stylelab", "round-2"))
    args = ap.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)
    samples, scale = (16, 0.5) if args.quick else (64, 1.0)
    filters = [f for f in args.only.split(",") if f]
    todo = [j for j in jobs() if not filters or any(f in name(j) for f in filters)]
    for i, job in enumerate(todo, 1):
        t0 = time.time()
        scene2.render(*job, os.path.join(args.out, name(job)), samples, scale)
        print(f"STYLELAB [{i}/{len(todo)}] {name(job)} {time.time() - t0:.1f}s", flush=True)
    # Greyscale for the contrast comparison and colour-blind checks on the new kitchen.
    for pal in ("P1", "P1r1"):
        src = os.path.join(args.out, f"kitchen_S3_{pal}_noon.jpg")
        if os.path.exists(src):
            post.greyscale(src, os.path.join(args.out, f"kitchen_S3_{pal}_noon_grey.jpg"))
    src = os.path.join(args.out, "kitchen_S3_P1_noon.jpg")
    if os.path.exists(src):
        post.colour_blind(src, os.path.join(args.out, "kitchen_S3_P1_noon_deuteranopia.jpg"), "deuteranopia")
    print("STYLELAB done", flush=True)


if __name__ == "__main__":
    main()
