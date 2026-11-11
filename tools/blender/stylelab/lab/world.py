"""Round 6 kit for the style lab (spec 046, plan in spec 051): the land, the edges of the world and the weather.

Low-poly terrain from a height function, the airship wreck, the Wildbelt hedge and its animals, the lighthouse, buoys,
the lifeguard drone, rain, lightning and umbrellas. Concept pieces, not final models.
"""

import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from .build import rad
from .future import LEAVES, FLOWERS, _m


# --- land ------------------------------------------------------------------------------------------------------------

GRASS = ("#8FA77E", "#7E9B6A", "#9BB07F", "#86A070")
ROCK = ("#A39486", "#8E8174", "#B5A796")
SAND = "#E6D3A8"
PATH = "#D9C9AE"


def terrain(name, height, x0, x1, y0, y1, step, seed=1, colour=None, skirt=None):
    """A flat-shaded height field. colour(x, y, z, slope) picks a hex per face; skirt closes the sides like a model."""
    rng = random.Random(seed)
    nx, ny = int((x1 - x0) / step) + 1, int((y1 - y0) / step) + 1
    bm = bmesh.new()
    grid = []
    for j in range(ny):
        row = []
        for i in range(nx):
            x, y = x0 + i * step, y0 + j * step
            if 0 < i < nx - 1 and 0 < j < ny - 1:
                x += rng.uniform(-0.3, 0.3) * step
                y += rng.uniform(-0.3, 0.3) * step
            row.append(bm.verts.new((x, y, height(x, y))))
        grid.append(row)
    mats, index = [], {}
    faces = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            a, c, d, e = grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]
            for tri in ((a, c, d), (a, d, e)) if (i + j) % 2 else ((a, c, e), (c, d, e)):
                f = bm.faces.new(tri)
                faces.append(f)
    bm.normal_update()
    for f in faces:
        cen = f.calc_center_median()
        slope = 1.0 - abs(f.normal.z)
        hex_ = colour(cen.x, cen.y, cen.z, slope) if colour else rng.choice(GRASS)
        if hex_ not in index:
            index[hex_] = len(mats)
            mats.append(hex_)
        f.material_index = index[hex_]
    if skirt is not None:
        edge = [grid[0][i] for i in range(nx)] + [grid[j][nx - 1] for j in range(1, ny)] + \
               [grid[ny - 1][i] for i in range(nx - 2, -1, -1)] + [grid[j][0] for j in range(ny - 2, 0, -1)]
        low = [bm.verts.new((v.co.x, v.co.y, skirt)) for v in edge]
        if "#6B5440" not in index:
            index["#6B5440"] = len(mats)
            mats.append("#6B5440")
        for k in range(len(edge)):
            f = bm.faces.new((edge[k], edge[(k + 1) % len(edge)], low[(k + 1) % len(edge)], low[k]))
            f.material_index = index["#6B5440"]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    for hex_ in mats:
        obj.data.materials.append(_m(hex_, 0.92))
    return obj


def water(p, x0, x1, y0, y1, z=0.0, hex_="#5E97A8", alpha=0.85, name="water"):
    w = b.box(name, (x1 - x0, y1 - y0, 0.05), ((x0 + x1) / 2, (y0 + y1) / 2, z - 0.05), 0, 1,
              _m(hex_, 0.08, 0.1, alpha=alpha))
    p.add(w)
    return w


def rock(p, loc, r, seed=1, hex_=None, squash=0.8, pre=""):
    """A faceted boulder: a low-poly sphere pushed about."""
    rng = random.Random(seed)
    obj = b.sphere(pre + "rock", r, loc, 7, (rng.uniform(0.8, 1.3), rng.uniform(0.8, 1.3), squash),
                   _m(hex_ or rng.choice(ROCK), 0.9), rad(rng.uniform(0, 30), rng.uniform(0, 30), rng.uniform(0, 360)))
    for v in obj.data.vertices:
        v.co *= rng.uniform(0.82, 1.15)
    obj.data.polygons.foreach_set("use_smooth", [False] * len(obj.data.polygons))
    p.add(obj)
    return obj


def bush(p, loc, r=0.8, seed=1, flowers=0.0, pre=""):
    rng = random.Random(seed)
    x, y, z = loc
    for k in range(3):
        p.add(b.sphere(pre + "bush", r * rng.uniform(0.7, 1.0), (x + rng.uniform(-r, r) * 0.6, y + rng.uniform(-r, r) * 0.6,
                                                                z + r * 0.6), 10, (1, 1, 0.8), _m(rng.choice(LEAVES), 0.75)))
    for k in range(int(flowers * 8)):
        a = rng.uniform(0, 6.28)
        p.add(b.sphere(pre + "flower", r * 0.12, (x + math.cos(a) * r * 0.8, y + math.sin(a) * r * 0.8,
                                                 z + r * rng.uniform(0.6, 1.3)), 6, material=_m(rng.choice(FLOWERS), 0.6)))


# --- the Wildbelt (south edge) -----------------------------------------------------------------------------------------

def hedge(p, a, c, h=4.2, depth=2.2, seed=1, gate_at=None, gate_w=4.0):
    """A tall flowering hedge from a to c (x, y), with an opening for a gate at distance gate_at along it."""
    rng = random.Random(seed)
    a, c = Vector((a[0], a[1], 0)), Vector((c[0], c[1], 0))
    d = c - a
    n = int(d.length / 1.4)
    along = d.normalized()
    side = Vector((-along.y, along.x, 0))
    for k in range(n):
        t = (k + 0.5) / n
        s = t * d.length
        if gate_at is not None and abs(s - gate_at) < gate_w / 2 + 0.5:
            continue
        q = a + d * t
        for layer in range(3):
            z = layer * h / 3 + rng.uniform(0.2, 0.6)
            p.add(b.sphere("hedge", rng.uniform(1.0, 1.3), (q.x + side.x * rng.uniform(-0.4, 0.4),
                                                           q.y + side.y * rng.uniform(-0.4, 0.4), z), 9,
                           (1, depth / 2.2, 0.9), _m(rng.choice(("#4F7A3A", "#3F6B3A", "#5E8F4F")), 0.8)))
        for f in range(rng.randint(2, 5)):
            p.add(b.sphere("hedge_flower", rng.uniform(0.12, 0.2),
                           (q.x - side.x * depth * 0.55 + along.x * rng.uniform(-0.7, 0.7),
                            q.y - side.y * depth * 0.55 + along.y * rng.uniform(-0.7, 0.7), rng.uniform(0.6, h)), 6,
                           material=_m(rng.choice(FLOWERS), 0.6)))
    return a + along * gate_at if gate_at is not None else None


def gate(p, loc, yaw=0.0, w=4.0, h=3.4):
    """A wooden slat gate between two posts with an arch sign. You can see through the slats."""
    wood = _m("#A97C55", 0.8)
    dark = _m("#7A5638", 0.8)
    parts = []
    for sx in (-1, 1):
        parts.append(b.box("gate_post", (0.35, 0.35, h + 0.8), (sx * (w / 2 + 0.2), 0, 0), 0.05, 2, dark))
        parts.append(b.sphere("gate_knob", 0.24, (sx * (w / 2 + 0.2), 0, h + 0.95), 10, material=dark))
    for k in range(9):
        x = -w / 2 + 0.25 + k * (w - 0.5) / 8
        parts.append(b.box("gate_slat", (0.16, 0.08, h - 0.6), (x, 0, 0.25), 0.02, 1, wood))
    for z in (0.7, h - 0.7):
        parts.append(b.box("gate_rail", (w, 0.1, 0.18), (0, 0.04, z), 0.02, 1, wood))
    parts.append(b.box("gate_rail", (w * 1.12, 0.1, 0.18), (0, 0.04, 1.2), 0.02, 1, wood, rad(0, 34, 0)))
    parts.append(b.torus("gate_arch", w / 2 + 0.2, 0.12, (0, 0, h + 0.8), 24, 6, dark, rad(90, 0, 0), arc=180))
    board = b.box("gate_sign", (3.6, 0.12, 0.75), (0, -0.12, h + 1.55), 0.08, 2, _m("#F3E9D2", 0.6))
    parts.append(board)
    parts.append(b.text("gate_text", "WILDBELT SANCTUARY", 0.34, (0, -0.2, h + 1.92), rad(90, 0, 0),
                        _m("#2F6E5E", 0.5), 0.01))
    parts.append(b.text("gate_text2", "animals only beyond this point", 0.2, (0, -0.2, h + 1.6), rad(90, 0, 0),
                        _m("#5B4A3A", 0.5), 0.01))
    g = b.group("gate", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def _legs(parts, xs, ys, h, r, material, pre):
    for x in xs:
        for y in ys:
            parts.append(b.capsule(pre + "_leg", r, h, (x, y, 0.0), 8, material))


def cow(p, loc, yaw=0.0, seed=1, grazing=False):
    """A free cow, about 1.4 m at the shoulder: round body, patches, a soft face."""
    rng = random.Random(seed)
    base = rng.choice(("#F2EDE3", "#C99A6A", "#8A5A3B", "#E8DCC6"))
    spot = rng.choice(("#3A2E28", "#8A5A3B", "#F2EDE3"))
    fur = _m(base, 0.85)
    parts = [b.capsule("cow_body", 0.48, 1.6, (0, 0.8, 1.15), 16, fur, rad(90, 0, 0))]
    for k in range(4):
        parts.append(b.sphere("cow_spot", rng.uniform(0.2, 0.32), (rng.choice((-0.42, 0.42)), rng.uniform(-0.6, 0.6),
                                                                     rng.uniform(1.0, 1.4)), 10, (0.25, 1, 0.8),
                              _m(spot, 0.85)))
    hz = 0.75 if grazing else 1.35
    hy = -1.15 if grazing else -1.05
    parts.append(b.sphere("cow_head", 0.28, (0, hy, hz), 14, (0.85, 1.25, 0.95), fur))
    parts.append(b.sphere("cow_muzzle", 0.2, (0, hy - 0.3, hz - 0.1), 12, (1.05, 0.8, 0.8), _m("#E7B9A4", 0.7)))
    for sx in (-1, 1):
        parts.append(b.sphere("cow_ear", 0.1, (sx * 0.3, hy + 0.05, hz + 0.12), 8, (1.4, 0.5, 0.6), fur))
        parts.append(b.sphere("cow_eye", 0.03, (sx * 0.17, hy - 0.2, hz + 0.08), 6, material=_m("#2C2826", 0.3)))
    _legs(parts, (-0.28, 0.28), (-0.55, 0.6), 0.9, 0.09, fur, "cow")
    parts.append(b.capsule("cow_tail", 0.03, 0.7, (0, 0.95, 0.75), 6, fur, rad(10, 0, 0)))
    g = b.group("cow", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def pig(p, loc, yaw=0.0, seed=1):
    rng = random.Random(seed)
    skin = _m(rng.choice(("#F0B8A8", "#E8A898", "#C98E7A")), 0.75)
    parts = [b.sphere("pig_body", 0.42, (0, 0.1, 0.55), 14, (0.9, 1.45, 0.85), skin),
             b.sphere("pig_head", 0.26, (0, -0.55, 0.62), 12, material=skin),
             b.cyl("pig_snout", 0.11, 0.1, (0, -0.8, 0.58), 12, edge=0.02, material=_m("#E39A8A", 0.6),
                   rot=rad(90, 0, 0))]
    for sx in (-1, 1):
        parts.append(b.sphere("pig_ear", 0.1, (sx * 0.16, -0.55, 0.86), 8, (0.8, 0.4, 1.0), skin))
        parts.append(b.sphere("pig_eye", 0.025, (sx * 0.1, -0.76, 0.7), 6, material=_m("#2C2826", 0.3)))
    _legs(parts, (-0.2, 0.2), (-0.3, 0.4), 0.3, 0.07, skin, "pig")
    g = b.group("pig", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def deer(p, loc, yaw=0.0, seed=1):
    fur = _m("#B7865A", 0.8)
    parts = [b.capsule("deer_body", 0.26, 0.95, (0, 0.45, 1.0), 14, fur, rad(90, 0, 0)),
             b.capsule("deer_neck", 0.11, 0.55, (0, -0.48, 1.05), 10, fur, rad(35, 0, 0)),
             b.sphere("deer_head", 0.15, (0, -0.86, 1.6), 12, (0.8, 1.3, 0.85), fur),
             b.sphere("deer_tail", 0.09, (0, 0.55, 1.12), 8, material=_m("#F3E9D2", 0.8))]
    for sx in (-1, 1):
        parts.append(b.sphere("deer_ear", 0.08, (sx * 0.12, -0.78, 1.74), 8, (1.4, 0.4, 0.7), fur))
        parts.append(b.capsule("deer_antler", 0.02, 0.32, (sx * 0.06, -0.8, 1.72), 6, _m("#E6D6BC", 0.7),
                               rad(0, sx * 25, 0)))
    _legs(parts, (-0.14, 0.14), (-0.32, 0.42), 0.95, 0.045, fur, "deer")
    g = b.group("deer", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def ranger_bot(p, loc, yaw=0.0):
    """The sanctuary ranger: a tall friendly robot in a green hat, one arm waving."""
    body = _m("#F3E9D2", 0.45)
    green = _m("#4F7A3A", 0.6)
    parts = [b.cyl("ranger_wheel", 0.25, 0.2, (0, 0, 0.0), 16, edge=0.04, material=_m("#2C2826", 0.6),
                   rot=rad(0, 90, 0)),
             b.capsule("ranger_body", 0.32, 0.9, (0, 0, 0.25), 18, body),
             b.box("ranger_band", (0.66, 0.66, 0.1), (0, 0, 0.75), 0.3, 3, green),
             b.sphere("ranger_head", 0.27, (0, 0, 1.55), 18, material=body),
             b.box("ranger_screen", (0.32, 0.05, 0.16), (0, -0.25, 1.52), 0.05, 2, b.lamp(1.5, "neon")),
             b.cyl("ranger_hat", 0.42, 0.04, (0, 0, 1.72), 20, material=green),
             b.cyl("ranger_crown", 0.22, 0.2, (0, 0, 1.72), 16, edge=0.03, material=green)]
    parts.append(b.capsule("ranger_arm", 0.05, 0.55, (0.32, 0, 1.05), 8, body, rad(0, -40, 0)))
    parts.append(b.capsule("ranger_arm", 0.05, 0.5, (-0.32, 0, 0.6), 8, body, rad(0, 20, 0)))
    parts.append(b.sphere("ranger_hand", 0.08, (0.68, 0, 1.47), 8, material=body))
    g = b.group("ranger", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def lookout(p, loc, yaw=0.0, h=2.6):
    """A wooden viewing platform with a rail and stairs, so you can look over the hedge."""
    wood = _m("#B98B5E", 0.8)
    parts = [b.box("look_deck", (5.0, 3.0, 0.2), (0, 0, h), 0.03, 1, wood)]
    for sx in (-2.3, 2.3):
        for sy in (-1.3, 1.3):
            parts.append(b.box("look_post", (0.2, 0.2, h + 1.1), (sx, sy, 0), 0.02, 1, wood))
    for sy in (-1.4, 1.4):
        parts.append(b.box("look_rail", (5.0, 0.1, 0.1), (0, sy, h + 1.0), 0.02, 1, wood))
    parts.append(b.box("look_rail", (0.1, 2.8, 0.1), (2.4, 0, h + 1.0), 0.02, 1, wood))
    ramp = b.box("look_ramp", (1.3, 5.6, 0.12), (-3.2, -1.2, h / 2 - 0.1), 0.02, 1, wood, rad(-27, 0, 0))
    parts.append(ramp)
    g = b.group("lookout", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


# --- the Wreck (west edge) ---------------------------------------------------------------------------------------------

def wreck(p, loc, yaw=0.0, length=150.0, r=17.0, seed=7, lit=True, pitch=-3.0, roll=7.0):
    """The Albatross: an old cargo airship broken in two across the valley, overgrown, a market in its belly.

    Built along +X before turning: the nose piece lies tilted into the ground, a gap with bare ribs, the tail piece
    with its fins. A big opening in the nose piece's side holds the night market.
    """
    rng = random.Random(seed)
    skin = _m("#C4BBA9", 0.75, 0.15)
    rust = _m("#9A6A48", 0.85)
    rib = _m("#6E6A64", 0.6, 0.5)
    parts = []
    nose_len, gap = length * 0.52, 9.0

    def hull(name, z0, z1, rx0, rx1, closed0, closed1):
        prof = []
        steps = 10
        for k in range(steps + 1):
            t = k / steps
            z = z0 + (z1 - z0) * t
            u = z / length
            rr = r * math.sin(math.pi * min(max(u, 0.02), 0.98)) ** 0.55
            prof.append((rr, z))
        if closed0:
            prof.insert(0, (0.0, z0))
        if closed1:
            prof.append((0.0, z1))
        return b.lathe(name, prof, 28, (0, 0, 0), skin, rad(0, 90, 0), smooth=False)

    nose = hull("wreck_nose", 0.0, nose_len, 0, 0, True, False)
    tail = hull("wreck_tail", nose_len + gap, length, 0, 0, False, True)
    def axis(x):
        return r * 0.7 if x < nose_len + gap / 2 else r * 0.6

    def radius(x):
        return r * math.sin(math.pi * min(max(x / length, 0.02), 0.98)) ** 0.55

    nose.location = (0, 0, axis(0))
    tail.location = (0, 0, axis(length))
    parts += [nose, tail]
    # Bare ribs in the gap and at the broken ends.
    for k in range(5):
        x = nose_len - 3.0 + k * (gap + 6.0) / 4
        u = x / length
        rr = r * math.sin(math.pi * u) ** 0.55 * 0.98
        arc = 360 if k in (0, 4) else rng.choice((200, 240, 260))
        ring = b.torus("wreck_rib", rr, 0.45, (x, 0, axis(x)), 28, 5, rib, rad(0, 90, rng.uniform(-20, 20)),
                       arc=arc)
        parts.append(ring)
    for k in range(8):
        a = math.radians(-30 + k * 40)
        parts.append(b.box("wreck_girder", (gap + 6, 0.5, 0.5), (nose_len + gap / 2, math.cos(a) * r * 0.9,
                                                                    r * 0.65 + math.sin(a) * r * 0.9), 0, 1, rib,
                           rad(rng.uniform(-8, 8), rng.uniform(-6, 6), rng.uniform(-6, 6))))
    # Tail fins.
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        fx = length - 14.0
        rr = radius(fx) * 0.9
        fin = b.box("wreck_fin", (16.0, 0.8, 9.0), (fx, -math.sin(a) * rr, axis(fx) + math.cos(a) * rr), 0.3, 2, skin,
                    rad(ang, 0, 0))
        parts.append(fin)
    # Rust and missing skin panels (dark patches), moss on top, trees growing out of it.
    for k in range(64):
        x = rng.uniform(8, length - 8)
        if nose_len - 4 < x < nose_len + gap + 4:
            continue
        a = rng.uniform(-1.2, 1.4)
        rr = radius(x)
        parts.append(b.box("wreck_patch", (rng.uniform(4, 9), 0.4, rng.uniform(2, 5)),
                           (x, -math.cos(a) * rr, axis(x) + math.sin(a) * rr), 0.1, 1,
                           rust if k % 3 else _m("#3D3A36", 0.8), rad(-math.degrees(a), 0, 0)))
        if k % 3 == 0:
            for j in range(3):
                parts.append(b.box("wreck_bar", (0.35, 0.5, 5.5), (x - 2.5 + j * 2.5, -math.cos(a) * (rr + 0.2),
                                                                   axis(x) + math.sin(a) * rr - 2.7), 0, 1, rib,
                                   rad(-math.degrees(a), 0, 0)))
        else:
            parts.append(b.box("wreck_streak", (0.5, 0.2, 6.0), (x + rng.uniform(-2, 2), -math.cos(a) * (rr + 0.12),
                                                                 axis(x) + math.sin(a) * rr - 6.0), 0, 1,
                               _m("#8A5E40", 0.9), rad(-math.degrees(a), 0, 0)))
    for k in range(70):
        x = rng.uniform(6, length - 6)
        if nose_len - 2 < x < nose_len + gap + 2:
            continue
        rr = radius(x)
        a = rng.uniform(math.radians(35), math.radians(145))
        parts.append(b.sphere("wreck_moss", rng.uniform(1.5, 3.2), (x, math.cos(a) * rr * 0.98,
                                                                   axis(x) + math.sin(a) * rr * 0.98), 8,
                              (1.3, 1.0, 0.45), _m(rng.choice(LEAVES), 0.85)))
    for k in range(9):
        x = rng.uniform(10, length - 15)
        if nose_len - 4 < x < nose_len + gap + 4:
            continue
        z = axis(x) + radius(x) * 0.97
        parts.append(b.cyl("wreck_trunk", 0.35, 4.0, (x, rng.uniform(-3, 3), z - 0.5), 8, material=_m("#7A5638", 0.8)))
        parts.append(b.sphere("wreck_crown", rng.uniform(2.6, 3.6), (x, 0, z + 4.5), 10, (1, 1, 1.1),
                              _m(rng.choice(LEAVES), 0.75)))
    # The faded name and the gondola.
    name_obj = b.text("wreck_name", "ALBATROSS", 5.5, (length * 0.3, -math.sqrt(r * r - 1.0) - 0.15, r * 0.7 + 1.0),
                        rad(90, 0, 0), _m("#8A7558", 0.8), 0.03)
    name_obj.visible_shadow = False
    parts.append(name_obj)
    gondola = b.box("wreck_gondola", (22.0, 7.0, 4.0), (length * 0.66, 14.0, 0.0), 1.2, 3, skin, rad(0, 0, 18))
    parts.append(gondola)
    # The market opening on the near side of the nose piece.
    ox = nose_len * 0.55
    parts.append(b.box("wreck_opening", (20.0, 4.0, 9.0), (ox, -r * 0.84, 0.0), 2.0, 3,
                       b.mat("market_dark", rough=0.9, hex_override="#2A211C")))
    if lit:
        parts.append(b.box("wreck_glow", (20.0, 0.2, 8.5), (ox, -r * 0.84 - 1.95, 0.4), 1.5, 2, b.lamp(0.35, "lamp_warm")))
    g = b.group("wreck", parts, loc, rad(roll, pitch, yaw))
    p.add(g)
    return g


def overpass_stub(p, loc, yaw=0.0, length=46.0, h=11.0):
    """The old highway: a concrete deck on pillars that ends in mid-air, broken rebar, weeds, a faded sign."""
    rng = random.Random(3)
    con = _m("#B9B3A8", 0.9)
    parts = [b.box("hw_deck", (length, 12.0, 1.4), (length / 2, 0, h), 0.1, 1, con)]
    for x in (6.0, 22.0, 38.0):
        if x < length - 3:
            parts.append(b.box("hw_pillar", (2.0, 5.0, h), (x, 0, 0), 0.2, 2, con))
    for k in range(10):
        parts.append(b.cyl("hw_rebar", 0.08, rng.uniform(1.0, 2.5), (length + 0.1, -5.0 + k * 1.1, h + 0.3), 4,
                           material=_m("#7A4A32", 0.8), rot=rad(rng.uniform(60, 100), rng.uniform(-20, 20), -90)))
    for k in range(12):
        parts.append(b.sphere("hw_weed", rng.uniform(0.6, 1.4), (rng.uniform(2, length - 2), rng.choice((-5.5, 5.5)),
                                                                 h + 1.5), 8, (1, 1, 0.7), _m(rng.choice(LEAVES), 0.8)))
    for side in (-6.1, 6.1):
        parts.append(b.box("hw_barrier", (length, 0.4, 0.9), (length / 2, side, h + 1.4), 0.1, 1, con))
    sign = b.box("hw_sign", (6.0, 0.2, 2.2), (10.0, -6.4, h + 3.6), 0.1, 1, _m("#5E7F6A", 0.7))
    parts.append(sign)
    parts.append(b.cyl("hw_signpost", 0.12, 3.6, (10.0, -6.4, h + 1.4), 8, material=_m("#8E8A84", 0.5)))
    parts.append(b.text("hw_sign_text", "CITY  42 km", 0.8, (10.0, -6.55, h + 4.7), rad(90, 0, 0),
                        _m("#E8E2D2", 0.6), 0.01))
    g = b.group("overpass", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


# --- the lake (east edge) ----------------------------------------------------------------------------------------------

def lighthouse(p, loc, h=16.0, beam_target=None):
    """A streamline lighthouse in cream and cherry with a glowing lantern."""
    x, y, z = loc
    cream, red = _m("#F3E9D2", 0.5), _m("#C8553D", 0.5)
    prof = [(2.6, 0.0), (2.2, h * 0.5), (1.7, h)]
    p.add(b.lathe("lh_tower", prof, 24, (x, y, z), cream))
    for k in range(3):
        p.add(b.cyl("lh_band", 2.45 - k * 0.32, 1.2, (x, y, z + 2.5 + k * 4.4), 24, material=red))
    p.add(b.cyl("lh_deck", 2.5, 0.3, (x, y, z + h), 24, edge=0.05, material=red))
    p.add(b.cyl("lh_lantern", 1.3, 2.0, (x, y, z + h + 0.3), 16, material=b.lamp(4.0, "lamp_warm")))
    p.add(b.lathe("lh_cap", [(1.6, 0.0), (0.0, 1.5)], 16, (x, y, z + h + 2.3), red))
    if beam_target is not None:
        data = bpy.data.lights.new("lh_beam", "SPOT")
        data.energy = 3.0e6
        data.spot_size = math.radians(7)
        data.spot_blend = 0.5
        data.color = (1.0, 0.86, 0.62)
        obj = bpy.data.objects.new("lh_beam", data)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (x, y, z + h + 1.3)
        obj.rotation_euler = (Vector(beam_target) - Vector(obj.location)).to_track_quat("-Z", "Y").to_euler()


def pier(p, x0, x1, y, w=5.0, z=1.0):
    wood = _m("#A97C55", 0.85)
    p.add(b.box("pier_deck", (x1 - x0, w, 0.3), ((x0 + x1) / 2, y, z), 0.02, 1, wood))
    for k in range(int((x1 - x0) / 4) + 1):
        for sy in (-w / 2 + 0.2, w / 2 - 0.2):
            p.add(b.cyl("pier_post", 0.18, z + 2.0, (x0 + k * 4, y + sy, -1.0), 8, material=_m("#7A5638", 0.85)))
            p.add(b.box("pier_rail", (4.0, 0.08, 0.08), (x0 + k * 4 + 2.0, y + sy, z + 1.2), 0, 1, wood))


def buoy(p, loc, colour="#E58436"):
    x, y, z = loc
    p.add(b.sphere("buoy", 0.35, (x, y, z), 12, (1, 1, 0.8), _m(colour, 0.5)))
    p.add(b.cyl("buoy_band", 0.36, 0.1, (x, y, z), 12, material=_m("#F3E9D2", 0.5)))


def lifeguard_drone(p, loc, yaw=0.0):
    """A red and cream drone carrying a ring buoy on a line: it tows swimmers back to the swim zone."""
    from . import future
    future.drone(p, loc, yaw, body="#C8553D", accent="#F3E9D2", package=False, scale=1.6)
    x, y, z = loc
    p.add(b.cyl("lg_line", 0.01, 1.6, (x, y, z - 1.9), 4, material=_m("#2C2826", 0.6)))
    p.add(b.torus("lg_ring", 0.38, 0.1, (x, y, z - 2.1), 16, 8, _m("#E58436", 0.5)))
    for k in range(4):
        a = math.radians(45 + k * 90)
        p.add(b.box("lg_ring_band", (0.14, 0.24, 0.24), (x + math.cos(a) * 0.38, y + math.sin(a) * 0.38, z - 2.22),
                    0.03, 1, _m("#F3E9D2", 0.5), rad(0, 0, math.degrees(a))))


def ferry(p, loc, yaw=0.0, pre=""):
    hull = _m("#F3E9D2", 0.5)
    parts = [b.box(pre + "ferry_hull", (22.0, 7.0, 2.4), (0, 0, -0.8), 2.0, 3, hull),
             b.box(pre + "ferry_band", (22.2, 7.2, 0.4), (0, 0, 0.6), 1.8, 2, _m("#4F9A85", 0.5)),
             b.box(pre + "ferry_cabin", (12.0, 5.5, 3.0), (-1.0, 0, 1.0), 1.4, 3, hull),
             b.box(pre + "ferry_win", (11.0, 5.6, 0.8), (-1.0, 0, 2.6), 0.4, 2, b.window()),
             b.cyl(pre + "ferry_stack", 0.9, 2.6, (4.0, 0, 3.6), 14, edge=0.1, material=_m("#C8553D", 0.5))]
    for k in range(5):
        parts.append(b.box(pre + "ferry_crate", (1.6, 1.6, 1.4), (6.0 + (k % 2) * 1.8, -1.8 + (k // 2) * 1.8, 1.0),
                           0.1, 1, _m(("#E58436", "#E3AE45", "#4F9A85")[k % 3], 0.6)))
    g = b.group(pre + "ferry", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


# --- weather ----------------------------------------------------------------------------------------------------------

def rain(box, n=6000, length=0.7, seed=1, hex_="#C9D6DE", alpha=0.35, slant=(0.08, 0.0)):
    """Rain streaks as one mesh of thin quads facing -Y (the camera side), slightly slanted by the wind."""
    rng = random.Random(seed)
    (x0, x1), (y0, y1), (z0, z1) = box
    bm = bmesh.new()
    w = 0.012
    for _ in range(n):
        x, y, z = rng.uniform(x0, x1), rng.uniform(y0, y1), rng.uniform(z0, z1)
        L = length * rng.uniform(0.7, 1.3) * (1 + (y - y0) / max(1.0, (y1 - y0)))
        top = Vector((x + slant[0] * L, y + slant[1] * L, z + L))
        v = [bm.verts.new((x - w, y, z)), bm.verts.new((x + w, y, z)),
             bm.verts.new((top.x + w, top.y, top.z)), bm.verts.new((top.x - w, top.y, top.z))]
        bm.faces.new(v)
    mesh = bpy.data.meshes.new("rain")
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object("far_rain", mesh)
    obj.data.materials.append(_m(hex_, 0.3, emit=0.15, alpha=alpha))
    return obj


def lightning(points, seed=1, width=0.5, strength=60.0, hex_="#E8EEFF", branches=3, flash=2.0e6):
    """A lightning bolt: a jagged emissive line from the cloud to a rod, with a few short branches and a flash light."""
    rng = random.Random(seed)
    a, c = Vector(points[0]), Vector(points[1])
    n = 14
    path = [a]
    for k in range(1, n):
        t = k / n
        q = a + (c - a) * t
        q += Vector((rng.uniform(-1, 1), rng.uniform(-0.4, 0.4), 0)) * (c - a).length * 0.035
        path.append(q)
    path.append(c)
    glow = _m(hex_, 0.3, emit=strength)

    def seg_line(pts, wd, nm):
        for p0, p1 in zip(pts, pts[1:]):
            d = p1 - p0
            s = b.cyl(nm, wd, d.length, tuple(p0), 5, material=glow)
            s.rotation_euler = d.to_track_quat("Z", "Y").to_euler()

    seg_line(path, width, "far_bolt")
    for k in range(branches):
        start = path[rng.randint(3, n - 4)]
        pts = [start]
        for j in range(4):
            pts.append(pts[-1] + Vector((rng.uniform(-1, 1) * 6, 0, -rng.uniform(4, 8))))
        seg_line(pts, width * 0.5, "far_bolt_branch")
    data = bpy.data.lights.new("bolt_flash", "POINT")
    data.energy = flash
    data.color = (0.85, 0.9, 1.0)
    data.shadow_soft_size = 8.0
    obj = bpy.data.objects.new("bolt_flash", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = tuple(a + Vector((0, 8.0, 6.0)))


def umbrella(p, loc, colour="#E58436", r=0.55, tilt=(8, 0)):
    """An umbrella held at about 2 m over a standing person at loc."""
    x, y, z = loc
    can = b.lathe("umbrella", [(0.0, 0.32), (r * 0.6, 0.24), (r, 0.0), (r * 0.98, -0.04), (0.0, 0.1)], 8,
                  (x, y, z + 1.95), _m(colour, 0.45), rad(tilt[0], tilt[1], 0), smooth=False)
    p.add(can)
    p.add(b.cyl("umbrella_stick", 0.012, 0.75, (x, y, z + 1.3), 6, material=_m("#2C2826", 0.5)))


def clouds(p, centre, n=14, spread=(160.0, 60.0), r=(12.0, 26.0), hex_="#4A5260", seed=1, pre="far_"):
    """A heavy cloud bank: flattened grey blobs high over the town."""
    rng = random.Random(seed)
    x, y, z = centre
    for k in range(n):
        p.add(b.sphere(pre + "cloud", rng.uniform(*r), (x + rng.uniform(-spread[0], spread[0]),
                                                       y + rng.uniform(-spread[1], spread[1]), z + rng.uniform(-6, 6)),
                       12, (1.6, 1.0, 0.45), _m(hex_, 0.95)))
