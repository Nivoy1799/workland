"""Round 5 kit for the style lab (spec 046): the town in 2222.

Streamline towers with ribbon windows and gardens, sky bridges, hover cars, delivery drones, an airship and garden
street pieces. Concept pieces, not final models. Objects whose name starts with "far_" stay out of the ink outlines.
"""

import math
import random

from mathutils import Vector

from . import build as b
from .build import rad
from .hero import clear_glass
from .street import _plan_block

LEAVES = ("#5E8F4F", "#3F6B3A", "#8FA77E", "#6E9A4A")
FLOWERS = ("#E58436", "#F2D27A", "#E58AA6", "#F3E9D2", "#9A7AA8")
WALLS = ("#F1E6CF", "#EFC9A9", "#E2EADD", "#EDE6D8", "#F3E9D2", "#D9E3E6")
TRIMS = ("#2F6E5E", "#C8553D", "#2E3F5C", "#8A5A3B", "#E58436", "#4F9A85")
CARS = ("#E58436", "#4F9A85", "#E3AE45", "#C8553D", "#F3E9D2", "#7FA3B8")


def _m(hex_, rough=0.5, metal=0.0, emit=0.0, alpha=1.0):
    return b.mat("future", metal=metal, rough=rough, emit=emit, alpha=alpha, hex_override=hex_)


def _win(rng):
    """A ribbon window: dark glass by day, mostly lit after sunset."""
    if b._glow >= 1.0 and rng.random() < 0.7:
        return b.mat("fwin", emit=0.3 * b._glow, rough=0.3, hex_override=rng.choice(("#FFD9A0", "#FFE8C8", "#F2C27A")))
    return _m("#4A6B7C", 0.15, 0.4)


# --- towers ----------------------------------------------------------------------------------------------------------

def tower(p, loc, kind="round", height=60.0, r=7.0, seed=1, pre="", garden=True, crown=None):
    """A streamline tower: stepped tiers with ribbon windows, garden belts, terrace trees and a crown
    (spire, glass garden dome, drone pad, or for a needle a saucer sky-deck). Returns the top height."""
    rng = random.Random(seed)
    wall = _m(rng.choice(WALLS), 0.8)
    trim = _m(rng.choice(TRIMS), 0.45)
    leaves = [_m(c, 0.7) for c in LEAVES]
    far = pre.startswith("far_")
    parts = []
    tiers = 1 if kind == "needle" else rng.randint(2, 4)
    weights = [2.2] + [rng.uniform(0.6, 1.2) for _ in range(tiers - 1)]
    floor_h = 3.6 * (2 if far else 1)
    depth = rng.uniform(0.55, 0.8)
    rr = r if kind != "needle" else r * 0.4
    z = 0.0
    for t in range(tiers):
        h = height * weights[t] / sum(weights)
        W, D = rr * 2, rr * 2 * depth
        R = min(rr * 0.45, D * 0.49)
        if kind == "slab":
            parts.append(_plan_block(pre + "t_body", W, D, h, R, (0, 0, z), wall))
        elif kind == "ribbed":
            # Art Deco: a glass core wrapped in tall vertical ribs (reference picture 1), no ribbon windows.
            parts.append(b.cyl(pre + "t_body", rr - 0.1, h, (0, 0, z), 32, material=_win(rng)))
            n_ribs = 14 if far else 22
            for k in range(n_ribs):
                a = 2 * math.pi * k / n_ribs
                parts.append(b.box(pre + "t_rib", (0.55, 0.9, h), (math.cos(a) * rr, math.sin(a) * rr, z), 0.2, 2,
                                   wall, rad(0, 0, math.degrees(a) + 90)))
        else:
            parts.append(b.cyl(pre + "t_body", rr, h, (0, 0, z), 32, edge=0.5, material=wall))
        n = 0 if kind == "ribbed" else max(1, int((h - 1.0) / floor_h))
        for f in range(n):
            zf = z + 1.2 + f * floor_h
            if kind == "slab":
                parts.append(_plan_block(pre + "t_win", W + 0.16, D + 0.16, 1.4, R + 0.08, (0, 0, zf), _win(rng)))
            else:
                parts.append(b.cyl(pre + "t_win", rr + 0.08, 1.4, (0, 0, zf), 32, material=_win(rng)))
            if garden and f % 4 == 2:
                if kind == "slab":
                    parts.append(_plan_block(pre + "t_garden", W + 0.9, D + 0.9, 0.7, R + 0.45, (0, 0, zf - 0.75),
                                             rng.choice(leaves)))
                else:
                    parts.append(b.torus(pre + "t_garden", rr + 0.35, 0.42, (0, 0, zf - 0.35), 32, 6,
                                         rng.choice(leaves)))
        # Vertical gardens: green strips down the facade of round towers.
        if garden and kind == "round" and not far:
            for k in range(rng.randint(2, 4)):
                a = rng.uniform(0, 2 * math.pi)
                parts.append(b.box(pre + "t_vgarden", (2.0, 0.6, h * 0.8),
                                   (math.cos(a) * (rr + 0.2), math.sin(a) * (rr + 0.2), z + h * 0.1), 0.25, 2,
                                   rng.choice(leaves), rad(0, 0, math.degrees(a) + 90)))
        z += h
        if kind == "slab":
            parts.append(_plan_block(pre + "t_cap", W + 0.4, D + 0.4, 0.45, R + 0.2, (0, 0, z - 0.1), trim))
        else:
            parts.append(b.cyl(pre + "t_cap", rr + 0.2, 0.45, (0, 0, z - 0.1), 32, edge=0.1, material=trim))
        if t < tiers - 1:
            nr = rr * rng.uniform(0.6, 0.8)
            if garden:
                k_n = 6 if far else max(6, int(rr * 1.6))
                for k in range(k_n):
                    a = 2 * math.pi * k / k_n + rng.uniform(-0.2, 0.2)
                    d = (nr + rr) / 2
                    parts.append(b.sphere(pre + "t_tree", rng.uniform(0.7, 1.3),
                                          (math.cos(a) * d, math.sin(a) * d * (depth if kind == "slab" else 1.0),
                                           z + 0.9), 10, (1, 1, 1.15), rng.choice(leaves)))
            rr = nr
    crown = "deck" if kind == "needle" else (crown or rng.choice(("spire", "dome", "pad", "flat")))
    if crown == "spire":
        parts.append(b.cyl(pre + "t_spire", 0.35, height * 0.15, (0, 0, z), 10, material=trim))
        parts.append(b.sphere(pre + "t_beacon", 0.6, (0, 0, z + height * 0.15), 12, material=b.lamp(3.0, "neon")))
        parts.append(b.torus(pre + "t_ring", 1.4, 0.12, (0, 0, z + height * 0.1), 24, 6, trim))
    elif crown == "dome":
        dr = (rr if kind != "slab" else rr * depth) * 0.85
        parts.append(b.sphere(pre + "t_dome", dr, (0, 0, z), 24, (1, 1, 0.75), clear_glass("#CFE6E2", 0.3)))
        for k in range(4):
            a = rng.uniform(0, 6.28)
            parts.append(b.sphere(pre + "t_dometree", dr * 0.25, (math.cos(a) * dr * 0.4, math.sin(a) * dr * 0.4,
                                                                  z + dr * 0.25), 10, material=rng.choice(leaves)))
    elif crown == "pad":
        parts.append(b.cyl(pre + "t_pad", rr * 0.7, 0.3, (0, 0, z), 32, edge=0.08, material=_m("#3A3532", 0.6)))
        parts.append(b.torus(pre + "t_padring", rr * 0.55, 0.08, (0, 0, z + 0.32), 32, 4, b.lamp(2.5, "lamp_warm")))
    elif crown == "deck":
        # A saucer sky-deck (a restaurant with a view) on top of a needle.
        parts.append(b.lathe(pre + "t_deck", [(0, -2.0), (r * 1.1, -0.6), (r * 1.2, 0.0), (r * 1.0, 1.8), (0, 2.6)], 32,
                             (0, 0, z + 2.0), wall))
        parts.append(b.torus(pre + "t_deckwin", r * 1.12, 0.55, (0, 0, z + 2.7), 32, 6, _win(rng)))
        parts.append(b.cyl(pre + "t_spire", 0.3, height * 0.12, (0, 0, z + 4.4), 10, material=trim))
        parts.append(b.sphere(pre + "t_beacon", 0.5, (0, 0, z + 4.4 + height * 0.12), 12,
                              material=b.lamp(3.0, "neon")))
        z += 4.6
    p.add(b.group(pre + "tower", parts, loc))
    return z


def sky_bridge(p, a, c, z, seed=1, pre=""):
    rng = random.Random(seed)
    a = Vector((a[0], a[1], 0.0))
    c = Vector((c[0], c[1], 0.0))
    d = c - a
    mid = (a + c) / 2
    ang = -math.degrees(math.atan2(d.x, d.y))
    parts = [b.box(pre + "bridge", (3.2, d.length, 2.8), (0, 0, 0), 1.2, 4, _m("#F3E9D2", 0.45)),
             b.box(pre + "bridge_win", (3.3, d.length * 0.98, 1.1), (0, 0, 0.9), 0.5, 3, _win(rng)),
             b.box(pre + "bridge_roof", (2.6, d.length, 0.35), (0, 0, 2.75), 0.15, 2, _m(LEAVES[0], 0.7))]
    p.add(b.group(pre + "bridge", parts, (mid.x, mid.y, z), rad(0, 0, ang)))


# --- things flying ---------------------------------------------------------------------------------------------------

def hover_car(p, loc, yaw=0.0, colour="#E58436", pre="", trail=False):
    """A teardrop hover car, about 3.4 m, front at -X: bubble canopy, fins, four glowing lift jets underneath,
    light trails after sunset."""
    parts = []
    body = _m(colour, 0.25)
    prof = [(0, 0), (0.28, 0.08), (0.48, 0.35), (0.55, 0.9), (0.55, 1.9), (0.45, 2.7), (0.25, 3.25), (0, 3.4)]
    shell = b.lathe(pre + "hc_body", prof, 24, (0, 0, 0), body, rad(0, 90, 0), scale=(0.55, 1.15, 1))
    for v in shell.data.vertices:
        if v.co.x > 0.22:
            v.co.x = 0.22
    parts.append(shell)
    parts.append(b.box(pre + "hc_seat", (0.5, 0.5, 0.3), (1.7, 0, -0.1), 0.08, 2, _m("#F3E9D2", 0.6)))
    parts.append(b.sphere(pre + "hc_canopy", 0.55, (1.5, 0, 0.18), 20, (1.3, 0.85, 0.75), clear_glass("#BFD9E0", 0.45)))
    for sy in (-1, 1):
        parts.append(b.prism(pre + "hc_fin", [(2.6, 0.1), (3.35, 0.1), (3.45, 0.55), (3.2, 0.5)], 0.04,
                             (0, sy * 0.4, 0), body))
        parts.append(b.box(pre + "hc_spear", (2.6, 0.02, 0.04), (1.6, sy * 0.62, -0.02), 0.01, 1, b.chrome()))
        parts.append(b.sphere(pre + "hc_head", 0.06, (0.12, sy * 0.25, 0.04), 8, material=b.lamp(2.5)))
        parts.append(b.sphere(pre + "hc_tail", 0.05, (3.3, sy * 0.25, 0.1), 8, material=b.lamp(2.0, "neon")))
        for sx in (0.7, 2.7):
            parts.append(b.torus(pre + "hc_jet", 0.13, 0.04, (sx, sy * 0.42, -0.26), 16, 4, b.chrome()))
            parts.append(b.sphere(pre + "hc_glow", 0.1, (sx, sy * 0.42, -0.3), 10, (1, 1, 0.4),
                                  b.lamp(3.0, "lamp_warm")))
        if trail:
            parts.append(b.capsule(pre + "hc_trail", 0.03, 3.0, (3.4, sy * 0.25, 0.1), 6,
                                   b.mat("trail", emit=0.35 * b._glow, rough=0.5, alpha=0.35, hex_override="#FF7A5C"),
                                   rad(0, 90, 0), taper=0.2))
    for o in parts:
        o.location.x -= 1.7
    p.add(b.group(pre + "hovercar", parts, loc, rad(0, 0, yaw)))


def drone(p, loc, yaw=0.0, body="#F3E9D2", accent="#E58436", package=True, pre="", scale=1.0):
    """A delivery drone: rounded body, four arms with see-through rotors, a lamp eye, a parcel on a line."""
    s = scale
    parts = [b.box(pre + "dr_body", (0.5 * s, 0.5 * s, 0.16 * s), (0, 0, 0), 0.08 * s, 3, _m(body, 0.35)),
             b.sphere(pre + "dr_eye", 0.05 * s, (0, -0.25 * s, 0.07 * s), 10, material=b.lamp(3.0, "neon"))]
    for k in range(4):
        a = math.radians(45 + 90 * k)
        arm = b.capsule(pre + "dr_arm", 0.025 * s, 0.42 * s, (0, 0, 0.08 * s), 8, _m(accent, 0.4))
        arm.rotation_euler = rad(0, 90, math.degrees(a))
        parts.append(arm)
        ex, ey = math.cos(a) * 0.42 * s, math.sin(a) * 0.42 * s
        parts.append(b.cyl(pre + "dr_hub", 0.035 * s, 0.07 * s, (ex, ey, 0.08 * s), 10, material=b.chrome()))
        parts.append(b.cyl(pre + "dr_rotor", 0.2 * s, 0.008 * s, (ex, ey, 0.15 * s), 24,
                           material=_m("#C9CDD0", 0.3, alpha=0.35)))
    if package:
        parts.append(b.cyl(pre + "dr_cable", 0.006 * s, 0.35 * s, (0, 0, -0.35 * s), 6, material=_m("#2C2826", 0.6)))
        parts.append(b.box(pre + "dr_box", (0.32 * s, 0.28 * s, 0.24 * s), (0, 0, -0.6 * s), 0.02 * s, 2,
                           _m("#B8925E", 0.85)))
        parts.append(b.box(pre + "dr_tape", (0.34 * s, 0.06 * s, 0.25 * s), (0, 0, -0.605 * s), 0.0, 1, _m(accent, 0.5)))
    p.add(b.group(pre + "drone", parts, loc, rad(0, 0, yaw)))


def airship(p, loc, yaw=0.0, length=40.0, text="", pre=""):
    L, R = length, length / 7
    hull = _m("#F3E9D2", 0.45)
    parts = [b.sphere(pre + "ship_hull", 1.0, (0, 0, 0), 32, (L / 2, R, R), hull),
             b.cyl(pre + "ship_band", R * 1.01, 3.0, (-L * 0.05 - 1.5, 0, 0), 32, material=_m("#E58436", 0.45),
                   rot=rad(0, 90, 0))]
    fin = [(L * 0.32, R * 0.4), (L * 0.5, R * 0.4), (L * 0.53, R * 1.15), (L * 0.42, R * 1.05)]
    fin_dn = [(x, -z) for x, z in reversed(fin)]
    parts.append(b.prism(pre + "ship_fin", fin, 0.4, (0, 0, 0), _m("#2F6E5E", 0.5)))
    parts.append(b.prism(pre + "ship_fin", fin_dn, 0.4, (0, 0, 0), _m("#2F6E5E", 0.5)))
    for sgn in (90, -90):
        parts.append(b.prism(pre + "ship_fin", fin, 0.4, (0, 0, 0), _m("#2F6E5E", 0.5), rad(sgn, 0, 0)))
    parts.append(b.box(pre + "ship_gondola", (L * 0.2, R * 0.6, R * 0.45), (-L * 0.05, 0, -R - R * 0.3), R * 0.2, 3,
                       _m("#2F6E5E", 0.45)))
    rng = random.Random(9)
    parts.append(b.box(pre + "ship_win", (L * 0.17, R * 0.62, R * 0.12), (-L * 0.05, 0, -R - R * 0.15), R * 0.05, 2,
                       _win(rng)))
    if text:
        parts.append(b.text(pre + "ship_text", text, R * 0.4, (-L * 0.08, -R * 1.02, R * 0.05), rad(90, 0, 0),
                            _m("#2F3A35", 0.5), 0.05))
    p.add(b.group(pre + "airship", parts, loc, rad(0, 0, yaw)))


# --- garden street ---------------------------------------------------------------------------------------------------

def garden_tree(p, loc, seed=1, h=3.4, pre=""):
    rng = random.Random(seed)
    x, y, z = loc
    p.add(b.capsule(pre + "gt_trunk", 0.1, h * 0.75, (x, y, z), 8, _m("#8A5A3B", 0.8), taper=0.6))
    for k in range(4):
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0.2, 0.6)
        p.add(b.sphere(pre + "gt_crown", rng.uniform(0.7, 1.1),
                       (x + math.cos(a) * d, y + math.sin(a) * d, z + h * 0.75 + rng.uniform(-0.2, 0.5)), 12,
                       (1, 1, 0.85), _m(rng.choice(LEAVES), 0.7)))


def garden_bed(p, loc, w=3.0, d=1.2, h=0.55, seed=1, edge="#C8A27A", tree=False):
    rng = random.Random(seed)
    x, y, z = loc
    p.add(b.box("bed", (w, d, h), loc, 0.12, 3, _m(edge, 0.7)))
    p.add(b.box("bed_soil", (w - 0.16, d - 0.16, 0.05), (x, y, z + h - 0.02), 0.03, 1, _m("#5C4030", 0.9)))
    for k in range(int(w * d * 6)):
        bx = x + rng.uniform(-w / 2 + 0.2, w / 2 - 0.2)
        by = y + rng.uniform(-d / 2 + 0.2, d / 2 - 0.2)
        p.add(b.sphere("bed_bush", rng.uniform(0.12, 0.26), (bx, by, z + h + 0.05), 10, (1, 1, 0.8),
                       _m(rng.choice(LEAVES), 0.7)))
        if rng.random() < 0.35:
            p.add(b.sphere("bed_flower", 0.05, (bx, by, z + h + 0.25), 8, material=_m(rng.choice(FLOWERS), 0.5)))
    if tree:
        garden_tree(p, (x, y, z + h), seed)


def bench(p, loc, yaw=0.0, colour="#B98B5E"):
    parts = [b.box("bench_seat", (1.6, 0.45, 0.06), (0, 0, 0.42), 0.02, 2, _m(colour, 0.7)),
             b.box("bench_back", (1.6, 0.06, 0.4), (0, 0.2, 0.5), 0.02, 2, _m(colour, 0.7))]
    for sx in (-0.65, 0.65):
        parts.append(b.box("bench_leg", (0.06, 0.42, 0.42), (sx, 0, 0), 0.01, 1, _m("#2F6E5E", 0.5)))
    p.add(b.group("bench", parts, loc, rad(0, 0, yaw)))


def fountain(p, loc, r=1.8):
    x, y, z = loc
    stone = _m("#E2D5BC", 0.8)
    water = _m("#7FB8C8", 0.05, 0.1, alpha=0.75)
    p.add(b.lathe("fount_basin", [(0, 0), (r, 0), (r, 0.5), (r - 0.15, 0.5), (r - 0.15, 0.12), (0, 0.12)], 40, loc,
                  stone))
    p.add(b.cyl("fount_water", r - 0.15, 0.3, (x, y, z + 0.12), 40, material=water))
    p.add(b.cyl("fount_col", 0.18, 1.1, (x, y, z), 16, material=stone))
    p.add(b.lathe("fount_bowl", [(0, 0), (0.2, 0), (0.7, 0.25), (0.68, 0.3), (0, 0.2)], 32, (x, y, z + 1.1), stone))
    p.add(b.capsule("fount_jet", 0.05, 0.7, (x, y, z + 1.3), 10, _m("#BFE3EC", 0.05, alpha=0.6)))
    rng = random.Random(5)
    for k in range(16):
        a = 2 * math.pi * k / 16
        p.add(b.sphere("fount_drop", 0.03, (x + math.cos(a) * 0.72, y + math.sin(a) * 0.72, z + rng.uniform(0.45, 1.2)),
                       6, (1, 1, 2), water))


def umbrella_table(p, loc, colour="#E58436", seed=1, guests=2):
    from . import concept
    x, y, z = loc
    concept.table(p, loc, r=0.4)
    p.add(b.cyl("umb_pole", 0.025, 2.3, (x, y, z), 8, material=b.chrome()))
    p.add(b.lathe("umb", [(0.0, 0.35), (1.2, 0.0), (1.18, -0.03), (0.0, 0.32)], 16, (x, y, z + 2.0),
                  _m(colour, 0.7), smooth=False))
    spots = [(x - 0.55, y, 90), (x + 0.55, y, -90)][:guests]
    for sx, sy, yaw in spots:
        concept.chair(p, (sx + (-0.1 if yaw == 90 else 0.1), sy, z), yaw, "#2F6E5E")
    concept.crowd(spots, seed=seed, sit=True)
