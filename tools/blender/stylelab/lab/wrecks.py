"""Round 7: two more tries for the Wreck at the west edge (spec 051, open question 3), next to the airship.

Both take the same arguments as world.wreck and keep its layout, so the same valley frame can show each one:
built along +X from 0 to about `length`, the near side is -Y, and the market opening sits at x = length * 0.29 on
the near side, at the ground.

  wreck_plane: the Pelican, a 2100s heavy freight plane, broken behind the wing, its side cargo door swung up.
  wreck_liner: the Stella, a 2100s starliner (a retro rocket) lying on its side, nose in the hillside.
"""

import math
import random

from . import build as b
from .build import rad
from .future import LEAVES, _m


def _moss_and_trees(parts, rng, xs, radius, axis, n_moss=50, n_trees=7, skip=None):
    for k in range(n_moss):
        x = rng.uniform(*xs)
        if skip and skip(x):
            continue
        rr = radius(x)
        a = rng.uniform(math.radians(40), math.radians(140))
        parts.append(b.sphere("wreck_moss", rng.uniform(1.0, 2.4), (x, math.cos(a) * rr * 0.97,
                                                                   axis(x) + math.sin(a) * rr * 0.97), 8,
                              (1.3, 1.0, 0.45), _m(rng.choice(LEAVES), 0.85)))
    for k in range(n_trees):
        x = rng.uniform(xs[0] + 6, xs[1] - 6)
        if skip and skip(x):
            continue
        z = axis(x) + radius(x) * 0.96
        parts.append(b.cyl("wreck_trunk", 0.3, 3.4, (x, rng.uniform(-2, 2), z - 0.5), 8, material=_m("#7A5638", 0.8)))
        parts.append(b.sphere("wreck_crown", rng.uniform(2.2, 3.2), (x, 0, z + 3.8), 10, (1, 1, 1.1),
                              _m(rng.choice(LEAVES), 0.75)))


def _rust(parts, rng, xs, radius, axis, n=30, sides=(-1.1, 1.2), skip=None, rib=None):
    rust = _m("#9A6A48", 0.85)
    for k in range(n):
        x = rng.uniform(*xs)
        if skip and skip(x):
            continue
        a = rng.uniform(*sides)
        rr = radius(x) - 0.08
        parts.append(b.box("wreck_patch", (rng.uniform(2.5, 5.0), 0.3, rng.uniform(1.2, 2.6)),
                           (x, -math.cos(a) * rr, axis(x) + math.sin(a) * rr), 0.08, 1,
                           rust if k % 3 else _m("#3D3A36", 0.8), rad(-math.degrees(a), 0, 0)))
        parts.append(b.box("wreck_streak", (0.4, 0.2, 4.0), (x + rng.uniform(-1, 1), -math.cos(a) * (rr + 0.1),
                                                             axis(x) + math.sin(a) * rr - 4.0), 0, 1,
                           _m("#8A5E40", 0.9), rad(-math.degrees(a), 0, 0)))


def _market_opening(parts, ox, y, lit, w=18.0, h=8.0, z=0.0):
    parts.append(b.box("wreck_opening", (w, 4.0, h), (ox, y, z), 1.5, 3,
                       b.mat("market_dark", rough=0.9, hex_override="#2A211C")))
    if lit:
        parts.append(b.box("wreck_glow", (w, 0.2, h - 0.5), (ox, y - 1.95, z + 0.4), 1.2, 2, b.lamp(0.35, "lamp_warm")))


# --- the Pelican: a freight plane --------------------------------------------------------------------------------------

def wreck_plane(p, loc, yaw=0.0, length=150.0, r=17.0, seed=7, lit=True, pitch=0.0, roll=4.0, ark=False):
    rng = random.Random(seed + 100)
    skin = _m("#E4DCCB", 0.6, 0.2)
    teal = _m("#2F6E5E", 0.6)
    rib = _m("#6E6A64", 0.6, 0.5)
    R = 9.0                    # fuselage radius
    x0, x1 = 6.0, 128.0        # nose and tail ends
    brk, gap = 84.0, 5.0       # the break behind the wing
    parts = []

    def radius(x):
        u = (x - x0) / (x1 - x0)
        if u < 0.14:
            return R * math.sin(math.pi / 2 * u / 0.14) ** 0.6
        if u > 0.78:
            return R * (1 - 0.72 * ((u - 0.78) / 0.22) ** 1.4)
        return R

    def axis(x):
        u = (x - x0) / (x1 - x0)
        return R - 1.2 + (5.5 * ((u - 0.78) / 0.22) ** 1.6 if u > 0.78 else 0.0)  # the tail sweeps up

    def piece(name, a, c, closed0, closed1):
        prof = []
        steps = 28
        for k in range(steps + 1):
            x = a + (c - a) * k / steps
            prof.append((radius(x), x))
        if closed0:
            prof.insert(0, (0.0, a))
        if closed1:
            prof.append((0.0, c))
        obj = b.lathe(name, prof, 24, (0, 0, 0), skin, rad(0, 90, 0), smooth=False)
        return obj

    # The fuselage as slices so the tail can sweep up: the front piece, the rear piece behind the break.
    front = piece("wreck_fuse", x0 + 16.0, brk, False, False)
    front.location = (0, 0, axis(40.0))
    parts.append(front)
    rear_parts = []
    for k in range(6):
        a = brk + gap + k * (x1 - brk - gap) / 6
        c = brk + gap + (k + 1) * (x1 - brk - gap) / 6
        s = piece("wreck_fuse", a, c, False, k == 5)
        s.location = (0, 0, axis((a + c) / 2))
        rear_parts.append(s)
    # The nose: the cargo nose hinged up at the top, as freight planes open it, frozen half open.
    nose = piece("wreck_nose", x0, x0 + 16.0, True, False)
    nose.location = (-x0 - 16.0, 0, 0)
    hinge = b.group("wreck_nose_hinge", [nose], (x0 + 16.0, 0, axis(40.0) + R * 0.9), rad(0, 38, 0))
    nose.location = (-(x0 + 16.0), 0, -(R * 0.9))
    parts.append(hinge)
    parts.append(b.box("wreck_cockpit", (5.0, 6.0, 1.2), (x0 + 6.0, 0, axis(40) + R * 0.55), 0.5, 2,
                       _m("#3A4A55", 0.15, 0.4)))
    parts[-1].parent = hinge
    parts[-1].location = (-9.0, 0, -2.9)
    parts.append(b.cyl("wreck_floor_dark", R * 0.95, 0.4, (x0 + 16.5, 0, axis(40)), 24,
                       material=b.mat("market_dark", rough=0.9, hex_override="#2A211C"), rot=rad(0, 90, 0)))
    # Ribs and stringers in the break.
    for k in range(4):
        x = brk - 1.0 + k * (gap + 2.0) / 3
        parts.append(b.torus("wreck_rib", radius(x) * 0.98, 0.3, (x, 0, axis(x)), 24, 5, rib, rad(0, 90, 0),
                             arc=360 if k in (0, 3) else rng.choice((200, 250))))
    for k in range(7):
        a = math.radians(-20 + k * 34)
        parts.append(b.box("wreck_girder", (gap + 4.0, 0.35, 0.35), (brk + gap / 2, math.cos(a) * R * 0.95,
                                                                     axis(brk) + math.sin(a) * R * 0.95), 0, 1, rib,
                           rad(rng.uniform(-6, 6), rng.uniform(-5, 5), rng.uniform(-5, 5))))
    # The livery: a teal cheat line along the windows, the name, the freight line.
    for k in range(18):
        x = x0 + 18.0 + k * 3.6
        if brk - 2 < x < brk + gap + 2:
            continue
        parts.append(b.box("wreck_cheat", (3.7, 0.2, 0.9), (x, -radius(x) - 0.02, axis(x) + 2.4), 0, 1, teal))
    for k in range(10):
        x = x1 - 30.0 + k * 2.8
        if x > x1 - 4:
            break
        parts.append(b.box("wreck_cheat", (2.9, 0.2, 0.9), (x, -radius(x) - 0.02, axis(x) + 2.4), 0, 1, teal,
                           rad(0, -8, 0)))
    nm = b.text("wreck_name", "PELICAN", 4.2, (52.0, -R - 0.25, axis(52) + 5.0), rad(90, 0, 0), _m("#2F6E5E", 0.7), 0.03)
    nm.visible_shadow = False
    parts.append(nm)
    sub = b.text("wreck_sub", "LAKE & VALLEY AIR FREIGHT", 1.1, (52.0, -R - 0.25, axis(52) - 0.6), rad(90, 0, 0),
                 _m("#8A7558", 0.8), 0.02)
    sub.visible_shadow = False
    parts.append(sub)
    # Wings (high wing, as on freight planes). The near wing broke off short; the outer piece lies in the grass by
    # the nose. The far wing still reaches into the hillside.
    wz = axis(50) + R * 0.8
    wing = [(0.0, 0.0), (26.0, 0.0), (36.0, 64.0), (28.0, 64.0)]

    def wing_slab(name, outline, depth, loc, rot):
        return b.prism(name, outline, depth, loc, skin, rot, 0.3, 2)

    # The plane lies tilted on the slope: the far wing rests up the hillside, the near stub points at the sky.
    far = wing_slab("wreck_wing", wing, 1.6, (44.0, 0.0, wz), rad(-90 + 18, 0, 0))
    parts.append(far)
    stub = wing_slab("wreck_wing", [(0.0, 0.0), (26.0, 0.0), (29.0, 20.0), (4.0, 21.0)], 1.6, (44.0, 0.0, wz),
                     rad(90 - 28, 0, 0))
    parts.append(stub)
    for k in range(6):  # the torn spar ends of the stub
        parts.append(b.box("wreck_spar", (0.4, 3.0, 0.4), (48.0 + k * 4.2, -18.6, wz + 9.2), 0, 1, rib,
                           rad(rng.uniform(-20, 20) - 28, 0, rng.uniform(-15, 15))))
    loose = wing_slab("wreck_wing_loose", [(0.0, 0.0), (24.0, 0.0), (32.0, 46.0), (24.0, 46.0)], 1.4,
                      (2.0, -14.0, 0.5), rad(80, 8, -20))
    parts.append(loose)
    # Engines: big ducted fans under the wing; one fell off and lies by the market, grown over.
    for (ex, ey, ez, rot, nm_) in ((50.0, 22.0, wz - 3.2 + 22.0 * 0.32, rad(0, 90, 0), "wreck_engine"),
                                   (50.0, 40.0, wz - 2.6 + 40.0 * 0.32, rad(0, 90, 0), "wreck_engine"),
                                   (62.0, -30.0, 2.6, rad(8, 84, 30), "wreck_engine_fallen")):
        parts.append(b.lathe(nm_, [(3.2, 0.0), (3.5, 2.0), (3.3, 8.0), (2.6, 9.5), (2.3, 9.5), (2.8, 1.0), (2.8, 0.0)], 20,
                             (ex - 4.0, ey, ez), _m("#C9C1B2", 0.5, 0.4), rot))
    for k in range(6):
        parts.append(b.sphere("wreck_moss", rng.uniform(0.8, 1.4), (58.0 + rng.uniform(-4, 4), -30.0 + rng.uniform(-2, 2),
                                                                    5.2), 8, (1.3, 1.0, 0.5), _m(rng.choice(LEAVES), 0.85)))
    # The T-tail.
    tz = axis(x1 - 8)
    parts.append(b.prism("wreck_fin", [(0.0, 0.0), (16.0, 0.0), (20.0, 19.0), (12.0, 19.0)], 1.2, (x1 - 20.0, 0.0, tz + 1.0),
                         teal, None, 0.2, 2))
    parts.append(b.prism("wreck_tailplane", [(0.0, 0.0), (9.0, 0.0), (12.0, 15.0), (8.0, 15.0)], 0.8,
                         (x1 - 9.0, 0.0, tz + 19.5), skin, rad(90, 0, 0), 0.2, 2))
    parts.append(b.prism("wreck_tailplane", [(0.0, 0.0), (9.0, 0.0), (12.0, 15.0), (8.0, 15.0)], 0.8,
                         (x1 - 9.0, 0.0, tz + 19.5), skin, rad(-90, 0, 0), 0.2, 2))
    # The side cargo door at the market, swung up like a gull wing.
    ox = length * 0.29
    _market_opening(parts, ox, -R * 0.82, lit, 16.0, 7.0)
    parts.append(b.box("wreck_door", (16.5, 0.6, 7.5), (ox, -R * 0.95, 7.2), 0.3, 2, skin, rad(60, 0, 0)))
    skip = lambda x: brk - 3 < x < brk + gap + 3 or abs(x - ox) < 10
    _rust(parts, rng, (x0 + 18.0, x1 - 6.0), radius, axis, 36, skip=skip)
    _moss_and_trees(parts, rng, (x0 + 18.0, x1 - 10.0), radius, axis, 50, 6, skip=skip)
    for k in range(10):
        my = rng.uniform(10, 60)
        parts.append(b.sphere("wreck_moss", rng.uniform(1.0, 2.0), (rng.uniform(46, 70), my, wz + 1.0 + my * 0.32), 8,
                              (1.4, 1.0, 0.4), _m(rng.choice(LEAVES), 0.85)))
    rear = b.group("wreck_rear", rear_parts, (brk + gap, 0, 0), rad(2, 2, 6))
    for s in rear_parts:
        s.location = (s.location.x - (brk + gap), s.location.y, s.location.z)
    parts.append(rear)
    for obj in list(parts):  # everything behind the break (tail, livery, rust, moss) rides on the rear piece
        if obj is not rear and obj.parent is None and obj.location.x > brk + gap + 0.5:
            obj.parent = rear
            obj.location = (obj.location.x - (brk + gap), obj.location.y, obj.location.z)
    g = b.group("wreck", [o for o in parts if o.parent is None], loc, rad(roll, pitch, yaw))
    p.add(g)
    return g


# --- the Stella: a starliner -------------------------------------------------------------------------------------------

def wreck_liner(p, loc, yaw=0.0, length=150.0, r=17.0, seed=7, lit=True, pitch=0.0, roll=0.0, ark=False):
    rng = random.Random(seed + 200)
    cream = _m("#F1E6CF", 0.45, 0.15)
    cherry = _m("#C8553D", 0.5)
    teal = _m("#4F9A85", 0.5)
    rib = _m("#6E6A64", 0.6, 0.5)
    R = 13.0
    x0, x1 = -8.0, 120.0   # the nose is buried in the slope at x0

    def radius(x):
        u = (x - x0) / (x1 - x0)
        if u < 0.42:
            return R * math.sin(math.pi / 2 * u / 0.42) ** 0.75
        if u > 0.9:
            return R * (1 - 0.3 * (u - 0.9) / 0.1)
        return R

    def axis(x):
        return R - 2.0

    prof = [(0.0, x0)] + [(radius(x0 + (x1 - x0) * k / 40), x0 + (x1 - x0) * k / 40) for k in range(1, 41)]
    body = b.lathe("wreck_body", prof, 32, (0, 0, axis(0)), cream, rad(0, 90, 0), smooth=False)
    parts = [body]
    # Bands, the chrome nose ring, the engine skirt and three bells.
    for x, w, m in ((x0 + 30.0, 2.0, cherry), (x0 + 34.0, 0.8, teal), (x1 - 14.0, 3.0, cherry)):
        parts.append(b.cyl("wreck_band", radius(x + w / 2) + 0.08, w, (x, 0, axis(0)), 32, material=m, rot=rad(0, 90, 0)))
    for k in range(3):
        a = math.radians(90 + 120 * k)
        parts.append(b.lathe("wreck_bell", [(2.2, 0.0), (3.4, 4.0), (4.4, 7.0), (4.2, 7.2), (2.0, 0.4)], 20,
                             (x1, math.cos(a) * 5.0, axis(0) + math.sin(a) * 5.0), _m("#8E8174", 0.5, 0.6),
                             rad(0, 90, 0)))
    # Three swept tail fins, one up in the sky, one half in the ground, one into the valley side.
    fin = [(0.0, 0.0), (26.0, 0.0), (40.0, 14.0), (36.0, 15.0), (14.0, 8.0)]
    for k, ang in enumerate((100.0, 220.0, 340.0)):
        theta = ang - 90.0
        f = b.prism("wreck_fin", [(x, z + R - 1.5) for x, z in fin], 1.4, (x1 - 30.0, 0.0, axis(0)),
                    cherry if k == 0 else teal, rad(theta, 0, 0), 0.3, 2)
        parts.append(f)
    # Two rows of portholes on the near side, lit from inside: the market and the little inn upstairs.
    port = b.lamp(0.9, "lamp_warm") if lit else _m("#4A6B7C", 0.15, 0.4)
    ox = length * 0.29
    for row, dz in enumerate((1.0, 5.2)):
        for k in range(22):
            x = 26.0 + k * 3.4
            if abs(x - ox) < 10 and row == 0:
                continue
            rr = math.sqrt(max(radius(x) ** 2 - dz ** 2, 1.0))
            parts.append(b.cyl("wreck_port", 0.75, 0.5, (x, -rr + 0.25, axis(0) + dz), 14, material=port,
                               rot=rad(90, 0, 0)))
            parts.append(b.torus("wreck_port_rim", 0.8, 0.12, (x, -rr - 0.2, axis(0) + dz), 14, 5, b.chrome(),
                                 rad(90, 0, 0)))
    nm = b.text("wreck_name", "STELLA", 6.0, (78.0, -math.sqrt(R * R - 81.0) - 0.4, axis(0) + 9.0), rad(90, 0, 0),
                cherry, 0.04)
    nm.rotation_euler = rad(90 - 44, 0, 0)
    nm.visible_shadow = False
    parts.append(nm)
    sub = b.text("wreck_sub", "STARLINE 7  EARTH - MOON - EARTH", 1.0, (46.0, -12.9, axis(0) - 2.6), rad(90, 0, 0),
                 _m("#8A7558", 0.8), 0.02)
    sub.visible_shadow = False
    parts.append(sub)
    # The passenger door at the market, with its airstair folded down; a landing leg sticking out, broken.
    _market_opening(parts, ox, -R * 0.78, lit, 14.0, 7.0, 2.5)
    parts.append(b.box("wreck_door", (6.0, 0.5, 7.0), (ox + 10.0, -R - 1.2, 0.2), 0.4, 2, cream, rad(0, 0, -60)))
    for k in range(6):
        parts.append(b.box("wreck_stair", (4.0, 1.0, 0.25), (ox - 12.0, -R - 1.0 - k * 1.0, 3.2 - k * 0.55), 0.05, 1,
                           _m("#B9B3A8", 0.6, 0.4)))
    parts.append(b.cyl("wreck_leg", 0.7, 14.0, (x1 - 24.0, -R * 0.6, axis(0) - R * 0.5), 10, material=rib,
                       rot=rad(70, 0, -20)))
    parts.append(b.cyl("wreck_foot", 2.0, 0.6, (x1 - 24.0, -R * 0.6 - 12.5, axis(0) - 2.0), 12, material=rib,
                       rot=rad(70, 0, -20)))
    skip = lambda x: abs(x - ox) < 9
    _rust(parts, rng, (x0 + 20.0, x1 - 6.0), radius, axis, 28, sides=(-0.9, -0.2), skip=skip)
    _moss_and_trees(parts, rng, (x0 + 12.0, x1 - 18.0), radius, axis, 46, 6)
    # The buried nose: a scar of earth and rocks where it went in.
    for k in range(9):
        parts.append(b.sphere("wreck_earth", rng.uniform(3.0, 6.0), (x0 + rng.uniform(-6, 14), rng.uniform(-14, 14),
                                                                     rng.uniform(0, 8)), 8,
                              (1.3, 1.0, 0.6), _m(rng.choice(("#8E7A62", "#7E6A52", "#9A876C")), 0.95)))
    g = b.group("wreck", parts, loc, rad(roll, pitch, yaw))
    p.add(g)
    return g
