"""Spec 046 style lab, round 7: the Canopy, the four seasons and three tries for the Wreck (JPEG, quality 92). One Blender process per frame.

Run (from the repo root):
    "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b --factory-startup \
        -P tools/blender/stylelab/render_round7.py -- [--only NAME,NAME] [--quick] [--no-ink] [--out DIR]
"""

import argparse
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from lab import scene7  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--no-ink", action="store_true")
    ap.add_argument("--out", default=os.path.join(REPO, "docs", "art", "stylelab", "round-7"))
    args = ap.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)
    samples, scale = (16, 0.5) if args.quick else (64, 1.0)
    names = [n for n in scene7.FRAMES if not args.only or n in args.only.split(",")]
    if len(names) > 1:
        import bpy
        for i, n in enumerate(names, 1):
            cmd = [bpy.app.binary_path, "-b", "--factory-startup", "-P", os.path.abspath(__file__), "--", "--only", n,
                   "--out", args.out] + (["--quick"] if args.quick else []) + (["--no-ink"] if args.no_ink else [])
            res = subprocess.run(cmd, capture_output=True, text=True)
            ok = "STYLELAB done" in res.stdout
            tail = "" if ok else " | " + " ".join((res.stdout + res.stderr).strip().splitlines()[-3:])
            print(f"STYLELAB [{i}/{len(names)}] r7_{n}.jpg {'ok' if ok else 'FAILED'}{tail}", flush=True)
        print("STYLELAB all done", flush=True)
        return
    for n in names:
        t0 = time.time()
        scene7.render(n, os.path.join(args.out, f"r7_{n}.jpg"), samples, scale, not args.no_ink)
        print(f"STYLELAB r7_{n}.jpg {time.time() - t0:.1f}s", flush=True)
    print("STYLELAB done", flush=True)


if __name__ == "__main__":
    main()
