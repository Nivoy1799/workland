"""Round 8 (spec 046, plan in spec 051 section 5b): the Dome over the whole of Haven Nine and the Storm Ring outside.

The Dome is an organic glass shell, not a flat roof: an ellipse in plan, a bubble in section (steep near the ground,
round at the top), with a slight lean of its crown and soft lumps so it does not read as a perfect half sphere.
Its ribs branch like the veins of a leaf. Close frames use `wall`, a local piece of the same shell.
"""

import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from .build import rad
from .future import _m

OUTSIDE = ("#8A8272", "#7C7566", "#6F6A5E", "#958B78")  # the grey-brown land outside the calm
STORM = ("#23272F", "#2C313A", "#353B45", "#1D2128")


def tube(name, pts, r, material):
    """A bevelled poly curve through pts: ribs, pipes, cables."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = 1
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for k, q in enumerate(pts):
        sp.points[k].co = (q[0], q[1], q[2], 1.0)
    obj = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    return obj


class Dome:
    """centre (x, y), half axes rx, ry, crown height top; lean shifts the crown (x, y) as a fraction of rx, ry."""

    def __init__(self, centre, rx, ry, top, lean=(-0.12, 0.05), sink=8.0, seed=1):
        self.cx, self.cy = centre
        self.rx, self.ry, self.top = rx, ry, top
        self.lean = lean
        self.sink = sink  # how far the shell goes below z=0 at its foot (into the lake and the hills)
        rng = random.Random(seed)
        self.lumps = [(rng.randint(2, 5), rng.uniform(0, 6.28), rng.uniform(0.02, 0.05)) for _ in range(3)]

    def s_of(self, x, y):
        return math.sqrt(((x - self.cx) / self.rx) ** 2 + ((y - self.cy) / self.ry) ** 2)

    def inside(self, x, y):
        return self.s_of(x, y) < 1.0

    def point(self, t, s):
        """Plan angle t, radial fraction s (0 crown, 1 foot) to a point on the shell."""
        lx, ly = self.lean
        x = self.cx + self.rx * (s * math.cos(t) + lx * (1 - s) ** 2)
        y = self.cy + self.ry * (s * math.sin(t) + ly * (1 - s) ** 2)
        lump = 1.0 + sum(a * math.sin(n * t + ph) * math.sin(math.pi * s) for n, ph, a in self.lumps)
        z = self.top * max(0.0, 1.0 - s ** 2.4) ** 0.5 * lump - self.sink * s ** 6
        return (x, y, z)

    def build(self, nt=120, ns=26, glass_alpha=0.14, ribs_lit=False, open_fn=None, name="far_dome"):
        """Glass skin, a fine lattice and the main ribs. Returns the objects."""
        verts = {}
        bm = bmesh.new()
        lat = bmesh.new()
        lat_v = {}

        def v(bmx, cache, i, j):
            key = (i % nt, j)
            if key not in cache:
                cache[key] = bmx.verts.new(self.point(2 * math.pi * (i % nt) / nt, j / ns))
            return cache[key]

        for j in range(1, ns + 1):
            for i in range(nt):
                if j == 1:
                    tri = [self.point(0, 0)]
                    a = v(bm, verts, i, 1)
                    c = v(bm, verts, i + 1, 1)
                    if "crown" not in verts:
                        verts["crown"] = bm.verts.new(tri[0])
                    try:
                        bm.faces.new([verts["crown"], a, c])
                    except ValueError:
                        pass
                    continue
                quad = [v(bm, verts, i, j - 1), v(bm, verts, i + 1, j - 1), v(bm, verts, i + 1, j), v(bm, verts, i, j)]
                t = 2 * math.pi * (i + 0.5) / nt
                cx, cy, _ = self.point(t, (j - 0.5) / ns)
                if open_fn and open_fn(cx, cy, j / ns):
                    pass
                else:
                    try:
                        bm.faces.new(quad)
                    except ValueError:
                        pass
                lq = [v(lat, lat_v, i, j - 1), v(lat, lat_v, i + 1, j - 1), v(lat, lat_v, i + 1, j), v(lat, lat_v, i, j)]
                try:
                    lat.faces.new(lq)
                except ValueError:
                    pass
        objs = {}
        for key, bmx, mat in (("glass", bm, _m("#CFE3E8", 0.05, 0.3, alpha=glass_alpha)),
                              ("lattice", lat, _m("#E9E4DA", 0.4, 0.5))):
            mesh = bpy.data.meshes.new(name + "_" + key)
            bmx.to_mesh(mesh)
            bmx.free()
            obj = b.new_mesh_object(name + "_" + key, mesh)
            obj.data.materials.append(mat)
            objs[key] = obj
        objs["glass"].visible_shadow = False
        wf = objs["lattice"].modifiers.new("lattice", "WIREFRAME")
        wf.thickness = 0.35
        wf.use_replace = True
        # Main ribs: meridians from the crown to the foot, each branching at mid height like a leaf vein.
        rib_m = _m("#FFD9A0", 0.4, emit=6.0) if ribs_lit else _m("#F1ECE2", 0.35, 0.4)
        n_main = 18
        for k in range(n_main):
            t = 2 * math.pi * (k + 0.25) / n_main
            pts = [self.point(t, j / 40) for j in range(0, 41)]
            tube(name + "_rib", pts, 1.6, rib_m)
            for side in (-1, 1):
                t1 = t + side * math.pi / n_main
                branch = [self.point(t + (t1 - t) * ((j - 16) / 24) ** 0.7, j / 40) for j in range(16, 41)]
                tube(name + "_vein", branch, 0.9, rib_m)
        for s in (0.42, 0.7, 0.9):
            ring = [self.point(2 * math.pi * k / 160, s) for k in range(161)]
            tube(name + "_ring", ring, 1.0, rib_m)
        self.objs = objs
        return objs


def outside_height(town_height, box=(-340.0, 330.0, -310.0, 260.0)):
    """The land outside the town model: the town's own height field, pushed under the town's terrain inside its box."""
    x0, x1, y0, y1 = box

    def h(x, y):
        z = town_height(x, y)
        if x0 + 14 < x < x1 - 14 and y0 + 14 < y < y1 - 14:
            return z - 6.0
        far = math.hypot(x, y)
        return z + 12.0 * math.sin(x / 61.0) * math.cos(y / 47.0) * min(1.0, max(0.0, (far - 420.0) / 300.0))
    return h


def storm_ring(p, centre, radius, depth=320.0, height=620.0, seed=1, bolts=6, gap=None):
    """The Storm Ring: a wall of black cloud all round the calm, with lightning inside it.

    gap=(angle, width) leaves a thinner part (the old Ark flight gap) so it does not read as a solid wall.
    """
    rng = random.Random(seed)
    cx, cy = centre
    n = 150
    for k in range(n):
        t = 2 * math.pi * k / n + rng.uniform(-0.01, 0.01)
        thin = 1.0
        if gap and abs((t - gap[0] + math.pi) % (2 * math.pi) - math.pi) < gap[1]:
            thin = 0.55
        for j in range(5):
            r = radius + rng.uniform(0, depth)
            z = (j + 0.3) * height / 5 * thin + rng.uniform(-20, 20)
            size = rng.uniform(70, 130) * (1.25 if j == 4 else 1.0) * radius / 1650.0
            p.add(b.sphere("far_storm", size, (cx + r * math.cos(t), cy + r * math.sin(t), z), 10,
                           (1.3, 1.3, 0.75 if j < 4 else 0.45), _m(rng.choice(STORM), 0.95)))
    for k in range(bolts):
        t = 2 * math.pi * rng.random()
        r = radius + rng.uniform(20, depth * 0.5)
        x, y = cx + r * math.cos(t), cy + r * math.sin(t)
        _bolt(p, (x, y, rng.uniform(0.4, 0.6) * height), (x + rng.uniform(-40, 40), y + rng.uniform(-40, 40), 0.0),
              seed=seed * 31 + k, width=2.2)


def _bolt(p, a, c, seed=1, width=0.5, strength=90.0, branches=3):
    """A jagged emissive bolt without the flash light (the far ring has many; one sun is enough)."""
    rng = random.Random(seed)
    a, c = Vector(a), Vector(c)
    n = 12
    path = [a]
    for k in range(1, n):
        q = a + (c - a) * (k / n)
        q += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * (c - a).length * 0.04
        path.append(q)
    path.append(c)
    glow = _m("#E8EEFF", 0.3, emit=strength)
    tube("far_bolt", [tuple(q) for q in path], width, glow)
    for k in range(branches):
        start = path[rng.randint(2, n - 3)]
        pts = [tuple(start)]
        for j in range(3):
            q = Vector(pts[-1]) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), -1.2)) * (c - a).length * 0.06
            pts.append(tuple(q))
        tube("far_bolt_branch", pts, width * 0.5, glow)


def dead_tree(p, loc, seed=1, h=6.0, pre="far_"):
    rng = random.Random(seed)
    x, y, z = loc
    grey = _m("#6E665C", 0.9)
    p.add(b.cyl(pre + "dead_trunk", 0.25 * h / 6, h, (x, y, z - 0.3), 6, material=grey,
                rot=rad(rng.uniform(-8, 8), rng.uniform(-8, 8), 0)))
    for k in range(3):
        a = rng.uniform(0, 360)
        p.add(b.cyl(pre + "dead_branch", 0.1 * h / 6, h * 0.4, (x, y, z + h * rng.uniform(0.5, 0.8)), 5,
                    material=grey, rot=rad(rng.uniform(35, 60), 0, a)))


# --- a local piece of the Dome wall, for the close frames -----------------------------------------------------------

class Wall:
    """A piece of the Dome's foot: a glass wall along Y at x = x0, leaning in (towards +X) as it rises.

    x(y, z) = x0 + lean * z + bend * y^2, so the wall curves away on both sides like the real shell.
    """

    def __init__(self, x0, lean=0.22, bend=1.0 / 900.0, y_range=(-120.0, 120.0), z_top=70.0, z_bottom=-6.0):
        self.x0, self.lean, self.bend = x0, lean, bend
        self.y0, self.y1 = y_range
        self.z_top, self.z_bottom = z_top, z_bottom

    def x(self, y, z):
        return self.x0 + self.lean * max(z, 0.0) + self.lean * 0.004 * max(z, 0.0) ** 2 + self.bend * y * y

    def build(self, p, panel=(9.0, 7.0), hole=None, glass_alpha=0.2, ribs_lit=False, name="wall"):
        """hole(y, z) -> True leaves a panel out (where the Wreck or a gate goes through)."""
        ny = int((self.y1 - self.y0) / panel[0])
        nz = int((self.z_top - self.z_bottom) / panel[1])
        bm = bmesh.new()
        cache = {}

        def vt(i, j):
            if (i, j) not in cache:
                y = self.y0 + i * panel[0]
                z = self.z_bottom + j * panel[1]
                cache[(i, j)] = bm.verts.new((self.x(y, z), y, z))
            return cache[(i, j)]

        lat = bmesh.new()
        lcache = {}

        def lv(i, j):
            if (i, j) not in lcache:
                y = self.y0 + i * panel[0]
                z = self.z_bottom + j * panel[1]
                lcache[(i, j)] = lat.verts.new((self.x(y, z), y, z))
            return lcache[(i, j)]

        for i in range(ny):
            for j in range(nz):
                yc = self.y0 + (i + 0.5) * panel[0]
                zc = self.z_bottom + (j + 0.5) * panel[1]
                if hole and hole(yc, zc):
                    continue
                bm.faces.new([vt(i, j), vt(i + 1, j), vt(i + 1, j + 1), vt(i, j + 1)])
                lat.faces.new([lv(i, j), lv(i + 1, j), lv(i + 1, j + 1), lv(i, j + 1)])
        objs = {}
        for key, bmx, mat in (("glass", bm, _m("#CFE3E8", 0.04, 0.3, alpha=glass_alpha)),
                              ("frame", lat, _m("#E9E4DA", 0.4, 0.5))):
            mesh = bpy.data.meshes.new(name + "_" + key)
            bmx.to_mesh(mesh)
            bmx.free()
            obj = b.new_mesh_object(name + "_" + key, mesh)
            obj.data.materials.append(mat)
            objs[key] = obj
        objs["glass"].visible_shadow = False
        wf = objs["frame"].modifiers.new("frame", "WIREFRAME")
        wf.thickness = 0.22
        wf.use_replace = True
        rib_m = _m("#FFD9A0", 0.4, emit=5.0) if ribs_lit else _m("#F1ECE2", 0.35, 0.4)
        for i in range(0, ny + 1, 3):
            y = self.y0 + i * panel[0]
            pts = [(self.x(y, z), y, z) for z in [self.z_bottom + k * (self.z_top - self.z_bottom) / 20
                                                   for k in range(21)]]
            p.add(tube(name + "_rib", pts, 0.7, rib_m))
        # The foot: a heavy concrete sill the glass stands on.
        for i in range(ny):
            y = self.y0 + (i + 0.5) * panel[0]
            if hole and hole(y, 0.5):
                continue
            p.add(b.box(name + "_sill", (3.0, panel[0] + 0.05, 2.4), (self.x(y, 0.0), y, -1.2), 0.2, 1,
                        _m("#B9B3A8", 0.85), rad(0, 0, math.degrees(math.atan(2 * self.bend * y)))))
        self.objs = objs
        return objs


def seal_collar(p, centre, radius, axis_rot, n_bolts=28, name="seal"):
    """A thick ring of dark rubber and steel where the wall is sealed round the hull, with bolts and a few patches."""
    rubber = _m("#2C2A28", 0.7)
    steel = _m("#8E8A84", 0.45, 0.6)
    p.add(b.torus(name, radius + 0.6, 1.1, centre, 48, 10, rubber, axis_rot))
    p.add(b.torus(name + "_band", radius + 1.9, 0.5, centre, 48, 6, steel, axis_rot))
    return rubber, steel


def vault_door(p, loc, r=7.0, yaw=0.0, open_deg=0.0, name="vault"):
    """A round pressure door: thick rim, concentric rings, a hub with spokes, bolts round the edge."""
    steel = _m("#9A958C", 0.4, 0.7)
    dark = _m("#4A4743", 0.6, 0.4)
    warn = _m("#E3AE45", 0.6)
    parts = [b.cyl(name + "_slab", r, 1.6, (0, 0, 0), 48, edge=0.3, material=steel, rot=rad(90, 0, 0))]
    for k, rr in enumerate((r * 0.82, r * 0.6)):
        parts.append(b.torus(name + "_ring", rr, 0.18, (0, -1.7, 0), 48, 6, dark, rad(90, 0, 0)))
    parts.append(b.cyl(name + "_hub", r * 0.18, 0.6, (0, -1.6, 0), 24, material=dark, rot=rad(90, 0, 0)))
    for k in range(6):
        a = k * 60
        parts.append(b.box(name + "_spoke", (r * 0.5, 0.3, 0.25), (math.cos(math.radians(a)) * r * 0.3, -1.9,
                                                                  math.sin(math.radians(a)) * r * 0.3), 0.05, 1, steel,
                           rad(0, -a, 0)))
    for k in range(24):
        a = 2 * math.pi * k / 24
        parts.append(b.cyl(name + "_bolt", 0.22, 0.3, (math.cos(a) * r * 0.92, -1.6, math.sin(a) * r * 0.92), 8,
                           material=dark, rot=rad(90, 0, 0)))
    for k in range(10):  # yellow and black warning segments round the rim
        a = 360 * k / 10
        seg = b.torus(name + "_warn", r + 0.05, 0.25, (0, -1.6, 0), 48, 6, warn if k % 2 else dark, rad(90, 0, 0),
                      arc=36)
        seg.rotation_euler = rad(90, a, 0)
        parts.append(seg)
    g = b.group(name, parts, (0, 0, 0))
    # Hinge on the left side: the door swings about a vertical axis at -r.
    hinge = b.group(name + "_hinge", [g], loc, rad(0, 0, yaw))
    g.location = (r, 0, 0)
    hinge.location = (loc[0] - r * math.cos(math.radians(yaw)), loc[1] - r * math.sin(math.radians(yaw)), loc[2])
    hinge.rotation_euler = rad(0, 0, yaw + open_deg)
    p.add(hinge)
    return hinge
