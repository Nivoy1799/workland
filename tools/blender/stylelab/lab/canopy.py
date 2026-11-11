"""Round 7 kit for the style lab (spec 046, plan in spec 051): the Canopy over the town core and the four seasons.

The Canopy is a high glass roof on tree columns, open at the sides. It keeps rain and snow off the core streets, runs
the water into cisterns and catches the autumn leaves for the compost works. Concept pieces, not final models.
"""

import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from . import future, world
from .build import rad
from .future import _m

# --- season palettes --------------------------------------------------------------------------------------------------

CROWNS = {
    "spring": ("#F2C6D0", "#F7E1E6", "#B8D99A", "#E8A9BC", "#A9CF8A"),
    "summer": ("#4F7F42", "#3F6B3A", "#5E8F4F", "#6E9A4A"),
    "autumn": ("#D9822B", "#C8553D", "#E3AE45", "#B5652E", "#9A8A3A"),
    "winter": ("#3F5F4A", "#4A6B52"),  # evergreens only; leaf trees stand bare
}
GROUND = {
    "spring": ("#A6C98A", "#98BF7E", "#B3D196", "#8FB878"),
    "summer": ("#8FA77E", "#7E9B6A", "#9BB07F", "#86A070"),
    "autumn": ("#B3A46A", "#A39A5E", "#BFA972", "#9C9058"),
    "winter": ("#F4F6F7", "#E9EEF1", "#F9FAFB", "#E2E8EC"),
}
LEAF_FALL = ("#D9822B", "#C8553D", "#E3AE45", "#B5652E", "#8A5A3B")
PETALS = ("#F2C6D0", "#F7E1E6", "#E8A9BC")


def use_season(season):
    """Swap the leaf colours the older kits use, so every tree and bush in the frame follows the season."""
    future.LEAVES = CROWNS[season]
    world.LEAVES = CROWNS[season]


def tree(p, loc, season, seed=1, h=5.0, pre="", snow=False):
    """A garden tree in its season: blossom, green, autumn colour, or bare branches in winter."""
    rng = random.Random(seed)
    x, y, z = loc
    bark = _m("#7A5638", 0.85)
    p.add(b.capsule(pre + "st_trunk", 0.14 * h / 5, h * 0.62, (x, y, z), 8, bark, taper=0.6))
    if season == "winter":
        for k in range(6):
            a = rng.uniform(0, 360)
            tilt = rng.uniform(25, 50)
            ln = h * rng.uniform(0.28, 0.42)
            z0 = z + h * rng.uniform(0.38, 0.6)
            br = b.cyl(pre + "st_branch", 0.05 * h / 5, ln, (x, y, z0), 5, material=bark, rot=rad(tilt, 0, a))
            p.add(br)
            if snow:
                d = Vector((0, 0, ln))
                d.rotate(br.rotation_euler)
                p.add(b.sphere(pre + "st_snow", 0.18 * h / 5, (x + d.x, y + d.y, z0 + d.z), 6, (1.6, 1.6, 0.6),
                               _m("#FBFCFD", 0.6)))
        return
    for k in range(5):
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0.2, 0.7) * h / 5
        p.add(b.sphere(pre + "st_crown", rng.uniform(0.9, 1.4) * h / 5,
                       (x + math.cos(a) * d, y + math.sin(a) * d, z + h * 0.62 + rng.uniform(-0.2, 0.6) * h / 5), 12,
                       (1, 1, 0.85), _m(rng.choice(CROWNS[season]), 0.7)))


def conifer(p, loc, seed=1, h=8.0, snow=False, pre=""):
    """A low-poly fir: stacked cones, with snow on top in winter."""
    rng = random.Random(seed)
    x, y, z = loc
    green = _m(rng.choice(("#3F5F4A", "#4A6B52", "#36574A")), 0.8)
    p.add(b.cyl(pre + "fir_trunk", 0.25, h * 0.25, (x, y, z), 6, material=_m("#6B4A33", 0.85)))
    for k in range(3):
        r = h * (0.32 - k * 0.08)
        zc = z + h * (0.18 + k * 0.24)
        p.add(b.lathe(pre + "fir", [(r, 0.0), (0.0, h * 0.42)], 7, (x, y, zc), green, smooth=False))
        if snow:
            p.add(b.lathe(pre + "fir_snow", [(r * 0.55, h * 0.42 * 0.45), (0.0, h * 0.42)], 7, (x, y, zc + 0.05),
                          _m("#FBFCFD", 0.6), smooth=False))


def confetti(name, points, size, colours, seed=1, upright=False):
    """Many small flat pieces in one mesh (fallen leaves, petals, snowflakes): points are (x, y, z)."""
    rng = random.Random(seed)
    bm = bmesh.new()
    mats, index = [], {}
    for (x, y, z) in points:
        s = size * rng.uniform(0.6, 1.3)
        a = rng.uniform(0, 6.28)
        if upright:   # a flake facing the camera side (-Y)
            v = [bm.verts.new((x + math.cos(a + k * 2.09) * s, y, z + math.sin(a + k * 2.09) * s)) for k in range(3)]
        else:
            v = [bm.verts.new((x + math.cos(a + k * 2.09) * s * (1.0 if k else 1.6),
                               y + math.sin(a + k * 2.09) * s * (1.0 if k else 1.6), z + rng.uniform(0, 0.02)))
                 for k in range(3)]
        f = bm.faces.new(v)
        hex_ = rng.choice(colours)
        if hex_ not in index:
            index[hex_] = len(mats)
            mats.append(hex_)
        f.material_index = index[hex_]
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    for hex_ in mats:
        obj.data.materials.append(_m(hex_, 0.7))
    return obj


def snowman(p, loc, yaw=0.0):
    x, y, z = loc
    snow = _m("#FBFCFD", 0.6)
    parts = [b.sphere("sm_base", 0.55, (0, 0, 0.5), 14, material=snow),
             b.sphere("sm_mid", 0.4, (0, 0, 1.25), 14, material=snow),
             b.sphere("sm_head", 0.28, (0, 0, 1.8), 14, material=snow),
             b.lathe("sm_carrot", [(0.05, 0.0), (0.0, 0.25)], 8, (0, -0.26, 1.8), _m("#E58436", 0.6), rad(90, 0, 0)),
             b.torus("sm_scarf", 0.27, 0.06, (0, 0, 1.55), 16, 6, _m("#C8553D", 0.7)),
             b.cyl("sm_hat", 0.2, 0.25, (0, 0, 2.0), 12, material=_m("#2F3A35", 0.6))]
    for side in (-1, 1):
        parts.append(b.cyl("sm_arm", 0.025, 0.7, (side * 0.35, 0, 1.3), 5, material=_m("#6B4A33", 0.8),
                           rot=rad(0, side * 65, 0)))
    for k in range(2):
        parts.append(b.sphere("sm_eye", 0.035, ((k - 0.5) * 0.14, -0.25, 1.88), 6, material=_m("#2F3A35", 0.5)))
    p.add(b.group("snowman", parts, loc, rad(0, 0, yaw)))


# --- the Canopy -------------------------------------------------------------------------------------------------------

class Canopy:
    """A glass vault over an ellipse: rim height at the edge, top height in the middle (heights are absolute).

    Built as rings of triangles (a geodesic-like disc), so panels stay about the same size everywhere.
    """

    def __init__(self, centre, rx, ry, rim_z, top_z, panel=9.0):
        self.cx, self.cy = centre
        self.rx, self.ry = rx, ry
        self.rim_z, self.top_z = rim_z, top_z
        self.panel = panel

    def q(self, x, y):
        return ((x - self.cx) / self.rx) ** 2 + ((y - self.cy) / self.ry) ** 2

    def inside(self, x, y):
        return self.q(x, y) <= 1.0

    def z(self, x, y):
        return self.rim_z + (self.top_z - self.rim_z) * max(0.0, 1.0 - self.q(x, y))

    def rim(self, t):
        return (self.cx + self.rx * math.cos(t), self.cy + self.ry * math.sin(t))

    def build(self, p, seed=1, open_frac=0.0, open_fn=None, snow_frac=0.0, rods=6, name="far_canopy"):
        """Glass, frame, gutter. open_fn(x, y) or open_frac leave panels out; snow_frac panels near the rim go white."""
        rng = random.Random(seed)
        rings = max(3, int(max(self.rx, self.ry) / self.panel))
        pts = [[(0.0, 0.0)]]
        for k in range(1, rings + 1):
            n = 6 * k
            off = rng.uniform(0, 0.3) if k < rings else 0.0
            pts.append([(k / rings * math.cos(2 * math.pi * (i + off) / n), k / rings * math.sin(2 * math.pi * (i + off) / n))
                        for i in range(n)])
        tris = []
        for k in range(rings):
            inner, outer = pts[k], pts[k + 1]
            if len(inner) == 1:
                for i in range(len(outer)):
                    tris.append((inner[0], outer[i], outer[(i + 1) % len(outer)]))
                continue
            ang = lambda q: math.atan2(q[1], q[0]) % (2 * math.pi)
            ia = sorted(range(len(inner)), key=lambda i: ang(inner[i]))
            oa = sorted(range(len(outer)), key=lambda i: ang(outer[i]))
            inner = [inner[i] for i in ia]
            outer = [outer[i] for i in oa]
            i = j = 0
            ni, no = len(inner), len(outer)
            while i < ni or j < no:
                a_next = ang(inner[(i + 1) % ni]) + (2 * math.pi if i + 1 >= ni else 0)
                b_next = ang(outer[(j + 1) % no]) + (2 * math.pi if j + 1 >= no else 0)
                if j >= no or (i < ni and a_next < b_next):
                    tris.append((inner[i % ni], outer[j % no], inner[(i + 1) % ni]))
                    i += 1
                else:
                    tris.append((inner[i % ni], outer[j % no], outer[(j + 1) % no]))
                    j += 1

        def world_pt(u, v):
            x, y = self.cx + self.rx * u, self.cy + self.ry * v
            return (x, y, self.z(x, y))

        glass_bm, frame_bm, snow_bm = bmesh.new(), bmesh.new(), bmesh.new()
        cache_g, cache_f, cache_s = {}, {}, {}

        def vert(bm, cache, uv):
            key = (round(uv[0], 6), round(uv[1], 6))
            if key not in cache:
                cache[key] = bm.verts.new(world_pt(*uv))
            return cache[key]

        self.edges = []
        for tri in tris:
            cen = world_pt(sum(t[0] for t in tri) / 3, sum(t[1] for t in tri) / 3)
            qq = self.q(cen[0], cen[1])
            fv = [vert(frame_bm, cache_f, uv) for uv in tri]
            try:
                frame_bm.faces.new(fv)
            except ValueError:
                pass
            is_open = open_fn(cen[0], cen[1]) if open_fn else rng.random() < open_frac
            if is_open:
                continue
            if snow_frac and qq > 0.55 and rng.random() < snow_frac * (qq - 0.4) / 0.6:
                try:
                    snow_bm.faces.new([vert(snow_bm, cache_s, uv) for uv in tri])
                except ValueError:
                    pass
                continue
            try:
                glass_bm.faces.new([vert(glass_bm, cache_g, uv) for uv in tri])
            except ValueError:
                pass
        for e in frame_bm.edges:
            self.edges.append((tuple(e.verts[0].co), tuple(e.verts[1].co)))
        objs = {}
        for key, bm, mat in (("glass", glass_bm, _m("#CFE3E8", 0.06, 0.2, alpha=0.36)),
                             ("frame", frame_bm, _m("#EDE8DE", 0.35, 0.6)),
                             ("snow", snow_bm, _m("#F7F9FA", 0.7))):
            if not bm.faces:
                bm.free()
                continue
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            mesh = bpy.data.meshes.new(name + "_" + key)
            bm.to_mesh(mesh)
            bm.free()
            obj = b.new_mesh_object(name + "_" + key, mesh)
            obj.data.materials.append(mat)
            objs[key] = obj
        if "glass" in objs:
            objs["glass"].visible_shadow = False
        if "frame" in objs:
            wf = objs["frame"].modifiers.new("lattice", "WIREFRAME")
            wf.thickness = 0.45
            wf.use_replace = True
            wf.use_even_offset = True
        # The gutter round the rim: a channel that takes the water to the columns.
        n = 160
        gut = _m("#D8D2C6", 0.5, 0.4)
        for k in range(n):
            t0, t1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
            a, c = Vector(self.rim(t0) + (self.rim_z,)), Vector(self.rim(t1) + (self.rim_z,))
            d = c - a
            seg = b.box(name + "_gutter", (d.length + 0.1, 1.6, 1.2), tuple((a + c) / 2 - Vector((0, 0, 0.9))), 0, 1,
                        gut)
            seg.rotation_euler = (0, 0, math.atan2(d.y, d.x))
            p.add(seg)
        # Lightning rods on the vault.
        for k in range(rods):
            t = 2 * math.pi * k / max(rods, 1) + 0.4
            x, y = self.cx + self.rx * 0.35 * math.cos(t), self.cy + self.ry * 0.35 * math.sin(t)
            p.add(b.cyl(name + "_rod", 0.12, 5.0, (x, y, self.z(x, y)), 6, material=b.chrome()))
        self.objs = objs
        return objs

    def columns(self, p, ground, every=26.0, inner=(), cistern=True, name="canopy"):
        """Tree columns round the rim (they double as downpipes) and a few inside; a cistern at the foot of each."""
        perim = math.pi * (3 * (self.rx + self.ry) - math.sqrt((3 * self.rx + self.ry) * (self.rx + 3 * self.ry)))
        n = max(6, int(perim / every))
        spots = [self.rim(2 * math.pi * (k + 0.5) / n) for k in range(n)]
        white = _m("#EDE8DE", 0.4, 0.3)
        tank = _m("#4F9A85", 0.5)
        made = []
        for k, (x, y) in enumerate(list(spots) + list(inner)):
            g = ground(x, y)
            top = self.z(x, y) - 0.4
            h = top - g
            on_rim = k < len(spots)
            p.add(b.cyl(name + "_col", 0.7, h * 0.72, (x, y, g), 12, material=white))
            # The column branches into four arms that hold the vault (Googie tree column).
            for j in range(4):
                a = math.radians(45 + 90 * j)
                ln = h * 0.36
                arm = b.cyl(name + "_arm", 0.3, ln, (x, y, g + h * 0.7), 8, material=white,
                            rot=rad(38, 0, math.degrees(a)))
                p.add(arm)
            p.add(b.sphere(name + "_knot", 1.0, (x, y, g + h * 0.71), 12, material=white))
            if cistern and on_rim and k % 2 == 0:
                cx, cy = x + (x - self.cx) * 0.02, y + (y - self.cy) * 0.02
                p.add(b.cyl(name + "_cistern", 2.0, 2.6, (cx, cy, g), 16, edge=0.3, material=tank))
            made.append((x, y))
        return made


def rain_around(canopy, box, n=8000, length=2.0, width=0.03, seed=1, hex_="#C9D6DE", alpha=0.3, slant=(0.1, 0.0),
                flakes=False, name="far_rain"):
    """Rain (or snow) everywhere in the box except under the Canopy: drops stop on the glass."""
    rng = random.Random(seed)
    (x0, x1), (y0, y1), (z0, z1) = box
    bm = bmesh.new()
    made = 0
    tries = 0
    while made < n and tries < n * 6:
        tries += 1
        x, y, z = rng.uniform(x0, x1), rng.uniform(y0, y1), rng.uniform(z0, z1)
        if canopy and canopy.inside(x, y) and z < canopy.z(x, y) + 0.5:
            continue
        if flakes:
            s = width * rng.uniform(0.7, 1.3)
            a = rng.uniform(0, 6.28)
            v = [bm.verts.new((x + math.cos(a + k * 2.09) * s, y, z + math.sin(a + k * 2.09) * s)) for k in range(3)]
        else:
            L = length * rng.uniform(0.7, 1.3)
            top = Vector((x + slant[0] * L, y + slant[1] * L, z + L))
            v = [bm.verts.new((x - width, y, z)), bm.verts.new((x + width, y, z)),
                 bm.verts.new((top.x + width, top.y, top.z)), bm.verts.new((top.x - width, top.y, top.z))]
        bm.faces.new(v)
        made += 1
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    obj.data.materials.append(_m(hex_, 0.4, emit=0.25 if flakes else 0.15, alpha=alpha))
    obj.visible_shadow = False
    return obj


def strike_glow(canopy, point, branches=10, steps=(3, 6), strength=60.0, width=0.14, seed=1):
    """The lightning runs off the rod across the lattice: short random walks that fork from each other."""
    glow = _m("#E8EEFF", 0.3, emit=strength)
    rng = random.Random(seed)
    key = lambda v: (round(v[0], 2), round(v[1], 2), round(v[2], 2))
    nbrs = {}
    for a, c in canopy.edges:
        nbrs.setdefault(key(a), []).append(c)
        nbrs.setdefault(key(c), []).append(a)
    px, py = point
    start = min(nbrs, key=lambda v: math.hypot(v[0] - px, v[1] - py))
    used = set()
    visited = [start]
    for k in range(branches):
        cur = start if k < 3 else rng.choice(visited)
        for j in range(rng.randint(*steps)):
            options = [n for n in nbrs[key(cur)] if (key(cur), key(n)) not in used]
            # Prefer moving away from the strike point, so the walk spreads like a crack.
            options.sort(key=lambda n: -math.hypot(n[0] - px, n[1] - py) + rng.uniform(0, 12))
            if not options:
                break
            nxt = options[0]
            used.add((key(cur), key(nxt)))
            used.add((key(nxt), key(cur)))
            a, c = Vector(cur), Vector(nxt)
            d = c - a
            s = b.cyl("far_strike", width * (1.0 - j * 0.05), d.length, tuple(a + Vector((0, 0, 0.3))), 5,
                      material=glow)
            s.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
            cur = nxt
            visited.append(cur)


def banner(p, a, c, z, text, colour="#C8553D", ink="#F3E9D2", h=1.6):
    """A cloth banner hung across a street between two points (x, y) at height z."""
    a, c = Vector((a[0], a[1], z)), Vector((c[0], c[1], z))
    d = c - a
    mid = (a + c) / 2
    yaw = math.degrees(math.atan2(d.y, d.x))
    w = min(d.length - 1.0, len(text) * h * 0.5 * 0.62 + 1.4)
    p.add(b.cyl("banner_line", 0.02, d.length, tuple(a + Vector((0, 0, 0.05))), 4, material=_m("#2C2826", 0.6),
                rot=rad(0, 90, yaw)))
    p.add(b.box("banner", (w, 0.06, h), (mid.x, mid.y, z - h), 0.05, 1, _m(colour, 0.7), rad(0, 0, yaw)))
    off = Vector((math.sin(math.radians(yaw)), -math.cos(math.radians(yaw)), 0)) * 0.05
    p.add(b.text("banner_text", text, h * 0.5, tuple(mid + off - Vector((0, 0, h / 2))), rad(90, 0, yaw), _m(ink, 0.6),
                 0.01))
