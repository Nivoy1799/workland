"""Wear and mess kit for the style lab (spec 046, round 3). Every piece is seeded and deterministic.

In the game these become decals (stains, cracks, paper signs) and a small prop kit (boxes, crates, bags, dishes).
Handwritten signs use the Windows font Ink Free for the mock-up only; the game needs a licensed font.
"""

import math
import os
import random

import bpy
from mathutils import Vector

from . import build as b
from .build import rad

HAND_FONT = r"C:\Windows\Fonts\Inkfree.ttf"
_font = None


def _m(hex_, rough=0.6, metal=0.0, alpha=1.0, emit=0.0):
    return b.mat("mess", metal=metal, rough=rough, alpha=alpha, emit=emit, hex_override=hex_)


def _hand_font():
    """The handwriting font, loaded once per scene (a scene reset removes it)."""
    if not os.path.exists(HAND_FONT):
        return None
    for f in bpy.data.fonts:
        if os.path.normcase(bpy.path.abspath(f.filepath)) == os.path.normcase(HAND_FONT):
            return f
    return bpy.data.fonts.load(HAND_FONT)


def _blob(rng, r, n=14, wobble=0.35):
    """An irregular closed outline around the origin."""
    pts = []
    phase = rng.uniform(0, 6.28)
    for i in range(n):
        a = 2 * math.pi * i / n
        k = 1 + wobble * (0.6 * math.sin(3 * a + phase) + 0.4 * rng.uniform(-1, 1))
        pts.append((r * k * math.cos(a), r * k * math.sin(a)))
    return pts


def _flat(name, outline, loc, plane, material, thick=0.0015):
    """A thin decal. plane 'z' lies on a floor or worktop, 'y' sits on a wall facing -Y."""
    obj = b.prism(name, outline, thick, (0, 0, 0), material)
    if plane == "z":
        obj.rotation_euler = rad(90, 0, 0)
    obj.location = loc
    return obj


# --- decals ----------------------------------------------------------------------------------------------------------

def stain(p, loc, r, hex_="#5A3A22", plane="z", seed=1, alpha=0.55):
    rng = random.Random(seed)
    p.add(_flat("stain", _blob(rng, r), loc, plane, _m(hex_, 0.35, alpha=alpha)))


def splatter(p, centre, radius, n, hex_="#7A3A1E", plane="z", seed=2, alpha=0.7):
    rng = random.Random(seed)
    cx, cy, cz = centre
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        d = radius * math.sqrt(rng.random())
        r = rng.uniform(0.004, 0.018) * (1.4 if i < 3 else 1.0)
        if plane == "z":
            loc = (cx + d * math.cos(a), cy + d * math.sin(a), cz)
        else:
            loc = (cx + d * math.cos(a), cy, cz + d * math.sin(a) * 0.7)
        p.add(_flat("splat", _blob(rng, r, 9, 0.3), loc, plane, _m(hex_, 0.3, alpha=alpha)))


def crack(p, start, length, plane="y", seed=3, hex_="#4A4038", width=0.006):
    """A zig-zag crack of thin segments, with a short branch."""
    rng = random.Random(seed)
    x, y, z = start
    ang = rng.uniform(-70, -20)
    m = _m(hex_, 0.9)
    pos = Vector((x, y, z))
    segs = int(length / 0.06)
    for i in range(segs):
        ang += rng.uniform(-35, 35)
        a = math.radians(ang)
        step = rng.uniform(0.04, 0.08)
        d = Vector((math.cos(a) * step, 0, math.sin(a) * step)) if plane == "y" else \
            Vector((math.cos(a) * step, math.sin(a) * step, 0))
        mid = pos + d / 2
        seg = b.box("crack", (step + 0.004, 0.003 if plane == "y" else width, width if plane == "y" else 0.003),
                    tuple(mid), 0.0, 1, m)
        seg.rotation_euler = rad(0, -ang, 0) if plane == "y" else rad(0, 0, ang)
        seg.location = mid if plane == "y" else (mid.x, mid.y, mid.z)
        if plane == "y":
            seg.location.z -= width / 2
        p.add(seg)
        pos = pos + d
        if i == segs // 2:
            crack(p, tuple(pos), length * 0.35, plane, seed + 11, hex_, width * 0.7)


def tape(p, loc, w=0.12, rot=0.0, plane="y", hex_="#D9CFA8"):
    obj = b.box("tape", (w, 0.003, 0.035) if plane == "y" else (w, 0.035, 0.003), loc, 0.0, 1,
                _m(hex_, 0.4, alpha=0.85))
    obj.rotation_euler = rad(0, rot, 0) if plane == "y" else rad(0, 0, rot)
    p.add(obj)


def paper_sign(p, loc, lines, w=0.3, h=0.21, rot=3.0, ink="#2A2624", paper="#F7F2E6", seed=4, size=0.042):
    """A paper sign on a wall (facing -Y), handwritten, taped at the top corners, slightly crooked."""
    rng = random.Random(seed)
    x, y, z = loc
    sheet = b.box("paper", (w, 0.002, h), (x, y, z), 0.0, 1, _m(paper, 0.85))
    sheet.rotation_euler = rad(0, rot, 0)
    p.add(sheet)
    font = _hand_font()
    for i, line in enumerate(lines):
        t = b.text("hand", line, size, (x + rng.uniform(-0.01, 0.01), y - 0.003, z + h - 0.05 - i * size * 1.25),
                   rad(90, rot + rng.uniform(-2, 2), 0), _m(ink, 0.6), 0.0005)
        if font:
            t.data.font = font
        p.add(t)
    for sx in (-1, 1):
        tape(p, (x + sx * (w / 2 - 0.02), y - 0.003, z + h - 0.03), 0.07, sx * 35 + rot)


def poster(p, loc, w, h, colours, title, rot=0.0, seed=5, faded=True):
    """A faded poster with a sun shape and a title, one corner peeling off."""
    rng = random.Random(seed)
    x, y, z = loc
    fade = 0.35 if faded else 0.0

    def washed(hex_):
        c = [int(hex_[i:i + 2], 16) for i in (1, 3, 5)]
        c = [int(v + (235 - v) * fade) for v in c]
        return "#%02X%02X%02X" % tuple(c)

    p.add(b.box("poster", (w, 0.002, h), (x, y, z), 0.0, 1, _m(washed(colours[0]), 0.8), rad(0, rot, 0)))
    p.add(b.cyl("poster_sun", w * 0.28, 0.002, (x, y - 0.002, z + h * 0.58), 24, material=_m(washed(colours[1]), 0.8),
                rot=rad(90)))
    for k in range(3):
        p.add(b.box("poster_band", (w * 0.86, 0.002, h * 0.05), (x, y - 0.003, z + h * 0.18 + k * h * 0.08), 0.0, 1,
                    _m(washed(colours[2 + k % 2]), 0.8)))
    t = b.text("poster_title", title, h * 0.07, (x, y - 0.004, z + h * 0.1), rad(90, rot, 0), _m(washed("#2A2624"), 0.7),
               0.0005)
    p.add(t)
    # Peeling corner: a small triangle bent away from the wall.
    corner = b.prism("peel", [(0, 0), (0.09, 0), (0, 0.09)], 0.002, (x + w / 2 - 0.09, y - 0.03, z + h - 0.09),
                     _m("#EEE6D3", 0.8), rad(0, 0, 0))
    corner.rotation_euler = rad(-35, 180, 0)
    corner.location = (x + w / 2, y - 0.004, z + h)
    p.add(corner)
    tape(p, (x - w / 2 + 0.03, y - 0.004, z + h - 0.02), 0.06, 40)


def puddle(p, loc, r, seed=6):
    rng = random.Random(seed)
    p.add(_flat("puddle", _blob(rng, r, 18, 0.4), loc, "z", _m("#4A5A62", 0.04, metal=0.2, alpha=0.6), 0.002))


def gum(p, x0, x1, y0, y1, z, n=12, seed=7):
    rng = random.Random(seed)
    for _ in range(n):
        loc = (rng.uniform(x0, x1), rng.uniform(y0, y1), z)
        p.add(_flat("gum", _blob(rng, rng.uniform(0.008, 0.018), 8, 0.2), loc, "z", _m("#8F8A84", 0.8)))


def weeds(p, loc, n=7, seed=8, h=0.14):
    rng = random.Random(seed)
    x, y, z = loc
    for i in range(n):
        blade = b.capsule("weed", 0.006, rng.uniform(0.6, 1.0) * h, (x + rng.uniform(-0.05, 0.05),
                          y + rng.uniform(-0.03, 0.03), z), 6, _m("#5E7F3A" if i % 2 else "#7A9A4A", 0.8),
                          taper=0.3)
        blade.rotation_euler = rad(rng.uniform(-25, 25), rng.uniform(-25, 25), 0)
        p.add(blade)


# --- props -----------------------------------------------------------------------------------------------------------

def crate(p, loc, yaw=0.0, hex_="#A9825A", seed=9, size=(0.5, 0.36, 0.3)):
    """A wooden crate of slats with gaps, slightly weathered."""
    rng = random.Random(seed)
    w, d, h = size
    x, y, z = loc
    wood = _m(hex_, 0.85)
    parts = []
    slats = 3
    for k in range(slats):
        sz = z + 0.02 + k * (h / slats)
        for sy in (-d / 2, d / 2):
            parts.append(b.box("slat", (w, 0.018, h / slats - 0.025), (0, sy, sz - z), 0.004, 1, wood))
        for sx in (-w / 2, w / 2):
            parts.append(b.box("slat", (0.018, d, h / slats - 0.025), (sx, 0, sz - z), 0.004, 1, wood))
    parts.append(b.box("crate_floor", (w, d, 0.015), (0, 0, 0.0), 0.0, 1, wood))
    for cx in (-w / 2 + 0.02, w / 2 - 0.02):
        for cy in (-d / 2 + 0.02, d / 2 - 0.02):
            parts.append(b.box("post", (0.04, 0.04, h), (cx, cy, 0), 0.006, 1, _m("#8E6A48", 0.85)))
    g = b.group("crate", parts, loc, rad(0, 0, yaw + rng.uniform(-4, 4)))
    p.add(g)
    return g


def cardboard_box(p, loc, size=(0.45, 0.35, 0.32), yaw=0.0, open_flaps=False, seed=10, label=""):
    x, y, z = loc
    w, d, h = size
    card = _m("#B8925E", 0.9)
    parts = [b.box("box", (w, d, h), (0, 0, 0), 0.006, 1, card)]
    if open_flaps:
        for sy, ang in ((-1, 35), (1, -40)):
            flap = b.box("flap", (w * 0.96, 0.005, d / 2), (0, sy * d / 2, h), 0.0, 1, card)
            flap.rotation_euler = rad(ang, 0, 0)
            parts.append(flap)
    else:
        parts.append(b.box("box_tape", (0.06, d + 0.004, 0.003), (0, 0, h), 0.0, 1, _m("#C9B78A", 0.4)))
        parts.append(b.box("box_tape_front", (0.06, 0.003, 0.08), (0, -d / 2 - 0.0015, h - 0.08), 0.0, 1,
                           _m("#C9B78A", 0.4)))
    if label:
        t = b.text("box_label", label, 0.05, (0, -d / 2 - 0.003, h * 0.45), rad(90), _m("#2A2624", 0.8), 0.0005)
        f = _hand_font()
        if f:
            t.data.font = f
        parts.append(t)
    g = b.group("cardboard", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def trash_bag(p, loc, seed=11, hex_="#2F3532", r=0.26):
    rng = random.Random(seed)
    bag = b.sphere("bag", r, (0, 0, 0), 20, (1.0, 0.9, 1.0), _m(hex_, 0.25))
    for v in bag.data.vertices:
        k = 1 + 0.12 * math.sin(5 * v.co.x / r + rng.uniform(0, 3)) * math.cos(4 * v.co.y / r)
        v.co = v.co * k
        if v.co.z < -r * 0.5:
            v.co.z = -r * 0.5 + (v.co.z + r * 0.5) * 0.2
    x, y, z = loc
    bag.location = (x, y, z + r * 0.52)
    p.add(bag)
    p.add(b.capsule("bag_knot", 0.04, 0.14, (x, y, z + r * 1.4), 8, _m(hex_, 0.25), taper=0.5))


def dented_bin(p, loc, seed=12, hex_="#7F8A84", lid_off=True):
    rng = random.Random(seed)
    x, y, z = loc
    prof = [(0, 0), (0.2, 0), (0.22, 0.04), (0.235, 0.65), (0.25, 0.7), (0.24, 0.72), (0, 0.7)]
    binm = b.lathe("bin", prof, 24, (x, y, z), _m(hex_, 0.5, metal=0.6))
    for v in binm.data.vertices:
        a = math.atan2(v.co.y, v.co.x)
        dent = 0.035 * math.exp(-((a - 1.0) ** 2) / 0.08) * math.exp(-((v.co.z - 0.35) ** 2) / 0.02)
        r = math.hypot(v.co.x, v.co.y)
        if r > 0.01:
            s = (r - dent) / r
            v.co.x *= s
            v.co.y *= s
    p.add(binm)
    for k in range(4):
        p.add(b.torus("bin_rib", 0.236, 0.006, (x, y, z + 0.15 + k * 0.14), 24, 4, _m(hex_, 0.5, metal=0.6)))
    if lid_off:
        lid = b.lathe("bin_lid", [(0.0, 0.06), (0.25, 0.0), (0.26, -0.02), (0.0, 0.0)], 24,
                      (x + 0.32, y - 0.1, z + 0.02), _m(hex_, 0.5, metal=0.6))
        lid.rotation_euler = rad(70, 10, 20)
        p.add(lid)
        trash_bag(p, (x, y, z + 0.5), seed + 1, "#3A4A3E", 0.2)


def dish_stack(p, loc, n=5, seed=13):
    """Dirty plates, a bit askew, with smears, and a cup on top."""
    rng = random.Random(seed)
    x, y, z = loc
    plate = _m("#F3EFE6", 0.35)
    for i in range(n):
        ox, oy = rng.uniform(-0.012, 0.012), rng.uniform(-0.012, 0.012)
        p.add(b.lathe("plate", [(0, 0), (0.07, 0), (0.12, 0.018), (0.125, 0.022), (0, 0.012)], 24,
                      (x + ox, y + oy, z + i * 0.022), plate))
        if i == n - 1:
            stain(p, (x + ox + 0.02, y + oy, z + i * 0.022 + 0.014), 0.04, "#A0522D", "z", seed + i, 0.75)
            splatter(p, (x + ox, y + oy, z + i * 0.022 + 0.015), 0.07, 6, "#C9733A", "z", seed + 20, 0.8)
    cz = z + n * 0.022
    p.add(b.lathe("cup", [(0, 0), (0.035, 0), (0.042, 0.08), (0.044, 0.082), (0.0, 0.075)], 20,
                  (x + 0.03, y - 0.01, cz), plate))
    p.add(b.torus("cup_handle", 0.02, 0.006, (x + 0.075, y - 0.01, cz + 0.045), 12, 4, plate, rad(90, 0, 0)))


def drip_pipe(p, x, y, z_top, z_floor=0.0):
    pipe = _m("#9AA0A3", 0.5, metal=0.7)
    p.add(b.cyl("pipe", 0.025, z_top - 0.3, (x, y, 0.3), 12, material=pipe))
    p.add(b.torus("pipe_joint", 0.03, 0.01, (x, y, 0.9), 12, 4, _m("#7A5A3A", 0.7, metal=0.3)))
    tape(p, (x, y - 0.027, 0.88), 0.07, 0, "y", "#8F8F8A")
    for k, dz in enumerate((0.75, 0.42)):
        p.add(b.sphere("drop", 0.008, (x, y - 0.03, dz), 8, (1, 1, 1.5), _m("#8FB5C4", 0.05, alpha=0.7)))
    puddle(p, (x + 0.05, y - 0.15, z_floor + 0.002), 0.18, 21)


def stool(p, loc, kind="round", hex_="#C8553D", seed=14):
    """A cheap stool: round with three legs, or square with four. Mismatched on purpose."""
    rng = random.Random(seed)
    x, y, z = loc
    leg = _m("#7F8A84", 0.5, metal=0.6)
    if kind == "round":
        p.add(b.cyl("stool_seat", 0.17, 0.05, (x, y, z + 0.6), 20, edge=0.02, material=_m(hex_, 0.6)))
        for k in range(3):
            a = 2 * math.pi * k / 3 + rng.uniform(-0.1, 0.1)
            l = b.cyl("stool_leg", 0.014, 0.62, (x + math.cos(a) * 0.13, y + math.sin(a) * 0.13, z), 8, material=leg)
            l.rotation_euler = rad(-math.sin(a) * 8, math.cos(a) * 8, 0)
            p.add(l)
    else:
        p.add(b.box("stool_seat", (0.32, 0.32, 0.04), (x, y, z + 0.44), 0.015, 2, _m(hex_, 0.7)))
        for sx in (-0.13, 0.13):
            for sy in (-0.13, 0.13):
                p.add(b.box("stool_leg", (0.035, 0.035, 0.45), (x + sx, y + sy, z), 0.006, 1, _m("#9A7452", 0.8)))


def cable(p, points, hex_="#E8E2D2"):
    m = _m(hex_, 0.6)
    for a, c in zip(points, points[1:]):
        a, c = Vector(a), Vector(c)
        d = c - a
        seg = b.cyl("cable", 0.006, d.length, tuple(a), 6, material=m)
        seg.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p.add(seg)


def fruit_crate_full(p, loc, yaw, fruit_hex, seed=15):
    """A crate with produce on top (potatoes, tomatoes, lemons)."""
    rng = random.Random(seed)
    crate(p, loc, yaw, seed=seed)
    x, y, z = loc
    for i in range(10):
        p.add(b.sphere("produce", rng.uniform(0.035, 0.045),
                       (x + rng.uniform(-0.18, 0.18), y + rng.uniform(-0.12, 0.12), z + 0.27 + rng.uniform(0, 0.04)),
                       10, (1, 0.9, 0.85), _m(fruit_hex, 0.5)))
