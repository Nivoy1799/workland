"""Spec 046 style lab, round 3: the lived-in pass and the climb (JPEG, quality 92).

Run (from the repo root):
    "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b --factory-startup \
        -P tools/blender/stylelab/render_round3.py -- [--only TEXT] [--quick] [--out DIR]
"""

import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from lab import post, scene3  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))


def jobs():
    """(variant, palette, shot, time, grit)"""
    out = []
    # How much grit: the same kitchen clean, lived-in and rough.
    for g in (0.0, 1.0, 2.0):
        out.append(("S3", "P1", "kitchen", "noon", g))
    # The dirt layer the game switches on during service.
    out.append(("S3", "P1", "kitchen-rush", "noon", 1.0))
    # People on a normal working day.
    out += [("S3", "P1", "faces-expr", "noon", 1.0), ("S3", "P1", "cast-lived", "noon", 1.0)]
    # The climb.
    out += [("S3", "P1", "stage1", "noon", 1.6), ("S3", "P1", "stage1", "sunset", 1.6),
            ("S3", "P1", "stage2", "overcast", 2.0),
            ("S3", "P1", "stage3", "noon", 0.8), ("S3", "P1", "stage3", "sunset", 0.8)]
    # New models.
    out += [("S3", "P1", "cart", "noon", 1.6), ("S3", "P1", "van", "noon", 0.8)]
    # The round 2 street, lived-in.
    out += [("S3", "P1", "street-lived", "overcast", 1.0), ("S3", "P1", "street-lived", "sunset", 1.0)]
    return out


def name(job):
    s, p, shot, t, g = job
    return f"{shot}_{s}_{p}_{t}_g{int(g * 10):02d}.jpg"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--out", default=os.path.join(REPO, "docs", "art", "stylelab", "round-3"))
    args = ap.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)
    samples, scale = (16, 0.5) if args.quick else (64, 1.0)
    filters = [f for f in args.only.split(",") if f]
    todo = [j for j in jobs() if not filters or any(f in name(j) for f in filters)]
    for i, job in enumerate(todo, 1):
        t0 = time.time()
        s, p, shot, t, g = job
        scene3.render(s, p, shot, t, os.path.join(args.out, name(job)), g, samples, scale)
        print(f"STYLELAB [{i}/{len(todo)}] {name(job)} {time.time() - t0:.1f}s", flush=True)
    print("STYLELAB done", flush=True)


if __name__ == "__main__":
    main()
