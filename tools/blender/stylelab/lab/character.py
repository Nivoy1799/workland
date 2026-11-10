"""Character generator for the style lab (spec 046, round 2).

Bodies grow from a stick skeleton with Blender's Skin modifier (smooth, rig-ready), then get a light subdivision.
Clothes are separate skin or shell objects a little bigger than the body. Heads are shaped ellipsoids with friendly
faces. Everything faces -Y, feet on z = 0, sizes in metres (D30: normal proportions, head about 1/7 of the height).
"""

import math
from dataclasses import dataclass, field

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from .build import Parts, rad


@dataclass
class Look:
    """Everything that makes one person: body, clothes, colours (hex), hair and extras."""
    height: float = 1.75
    build: float = 1.0          # 0.9 slim, 1.0 average, 1.15 round (waist and hips)
    shoulders: float = 1.0      # shoulder width factor
    lean: float = 0.0           # 0..1 forward lean of the upper body (older people)
    skin: str = "#D9A786"
    hair: str = "#5A3A2A"
    hair_style: str = "short"   # short, bob, bun, balding, quiff, curly
    top: str = "#F3E9D2"        # shirt or blouse
    top_style: str = "shirt"    # shirt (collar, rolled sleeves), tee, blouse
    layer: str = ""             # cardigan or vest colour, empty for none
    layer_style: str = "cardigan"
    bottom: str = "#4E6E8E"
    bottom_style: str = "trousers"  # trousers, skirt
    legs: str = ""              # tights colour under a skirt (default skin)
    shoes: str = "#6B4A33"
    sole: str = "#2C2826"
    apron: str = ""             # apron colour, empty for none
    hat: str = ""               # "", "diner" (paper cook cap), "flat" (flat cap)
    hat_colour: str = "#F3E9D2"
    glasses: bool = False
    eyelids: bool = False       # softer, older eyes
    beard: bool = False
    prop: str = ""              # "", "spoon", "bag", "tote", "cane"
    prop_colour: str = "#C9734F"
    # Round 3, lived-in: expression and wear.
    expression: str = "smile"   # smile, neutral, focused, tired, yawn, grin
    seed: int = 0               # small asymmetries in the face
    apron_stains: bool = False
    untucked: bool = False      # a shirt worn out over the trousers
    messy: bool = False         # stray hair locks
    rolled: bool = False        # trouser legs rolled up once
    sit: bool = False           # seated on a 0.45 m chair or bench, hands forward on a table
    extras: dict = field(default_factory=dict)


# --- low-level helpers -----------------------------------------------------------------------------------------------

def _m(hex_, rough=0.6, metal=0.0):
    return b.mat("c", rough=rough, metal=metal, hex_override=hex_)


def skin_object(name, verts, edges, radii, material, levels=1, root=0):
    """A smooth tube network: Skin modifier over a stick skeleton, then subdivision."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], edges, [])
    obj = b.new_mesh_object(name, mesh)
    skin = obj.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.6
    data = mesh.skin_vertices[0].data
    for i, r in enumerate(radii):
        data[i].radius = r
    data[root].use_root = True
    if levels:
        sub = obj.modifiers.new("Subsurf", "SUBSURF")
        sub.levels = levels
        sub.render_levels = levels
    mesh.materials.append(material)
    return obj


def _mirror(points):
    """Mirror a list of (x, y, z) points to the other side (x -> -x)."""
    return [(-x, y, z) for x, y, z in points]


def _ellipsoid(name, a, bb, c, center, material, segments=(28, 18), jaw=0.0, chin=0.0):
    """An ellipsoid with radii a (x), bb (y, depth), c (z). jaw narrows the lower half, chin pushes it forward."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments[0], v_segments=segments[1], radius=1.0)
    for v in bm.verts:
        x, y, z = v.co
        if z < 0:
            k = 1.0 - jaw * (-z) ** 1.4
            x *= k
            y = y * (1.0 - jaw * 0.5 * (-z)) - chin * (-z) ** 2 * (1 if y < 0 else 0)
        v.co = Vector((x * a, y * bb, z * c))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    obj.location = center
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    mesh.materials.append(material)
    return obj


def _front(a, bb, c, x, z):
    """Y of the front surface of an ellipsoid at local (x, z); the front is -Y."""
    t = 1.0 - (x / a) ** 2 - (z / c) ** 2
    return -bb * math.sqrt(max(t, 0.0))


# --- the person ------------------------------------------------------------------------------------------------------

class Body:
    """Key heights and points for one person, scaled from a 1.75 m reference."""

    def __init__(self, look):
        s = look.height / 1.75
        self.s = s
        L = look.lean
        w, sh = look.build, look.shoulders
        self.w, self.sh = w, sh
        self.pelvis = Vector((0, 0.0, 0.94 * s))
        self.waist = Vector((0, 0.005 - 0.02 * L, 1.08 * s))
        self.chest = Vector((0, -0.005 - 0.05 * L, 1.25 * s))
        self.upper = Vector((0, 0.0 - 0.08 * L, 1.37 * s))
        self.neck = Vector((0, 0.01 - 0.1 * L, 1.47 * s))
        self.head = Vector((0, -0.005 - 0.12 * L, 1.625 * s - 0.02 * L))
        self.shoulder = Vector((0.175 * sh, 0.015 - 0.08 * L, 1.395 * s))
        self.elbow = Vector((0.225 * sh + 0.02 * (w - 1), 0.04 - 0.05 * L, 1.13 * s))
        self.wrist = Vector((0.245 * sh + 0.03 * (w - 1), -0.03 - 0.04 * L, 0.89 * s))
        self.hip = Vector((0.088 * w, 0.0, 0.9 * s))
        self.knee = Vector((0.095, -0.015, 0.5 * s))
        self.ankle = Vector((0.1, 0.015, 0.095 * s))
        self.dz = 0.0
        if look.sit:
            # Seated: the upper body drops onto the seat, thighs forward, shins down, hands forward on a table.
            self.dz = 0.5 - 0.94 * s
            for name in ("pelvis", "waist", "chest", "upper", "neck", "head", "shoulder"):
                getattr(self, name).z += self.dz
            self.elbow = Vector((self.elbow.x, -0.12, self.elbow.z + self.dz + 0.04))
            self.wrist = Vector((self.wrist.x - 0.04, -0.36, 0.78))
            self.hip = Vector((self.hip.x, 0.0, 0.48))
            self.knee = Vector((0.1, -0.43, 0.5))
            self.ankle = Vector((0.1, -0.46, 0.095))


def _sym(p):
    return [p, Vector((-p.x, p.y, p.z))]


def person(look, loc=(0, 0, 0), yaw=0.0, name="Person"):
    """Build a whole person and return its root empty."""
    p = Parts()
    bd = Body(look)
    s, w = bd.s, bd.w
    skin = _m(look.skin, 0.55)
    top = _m(look.top, 0.75)
    bottom = _m(look.bottom, 0.75)

    # Layering, inside to outside, with at least 1.5 cm between layers so nothing pokes through:
    # skin arms < sleeves; tucked top < trousers waist < apron; tights < skirt; top < cardigan or vest.
    trousers = look.bottom_style == "trousers"
    tucked = look.top_style in ("shirt", "blouse") and not look.untucked

    # Legs and pelvis (trousers, or tights under a skirt).
    leg_mat = bottom if trousers else _m(look.legs or look.skin, 0.6)
    dz = bd.dz
    if trousers:
        verts = [bd.pelvis, Vector((0, bd.waist.y, 1.035 * s + dz))]
        radii = [(0.162 * w, 0.116 * w), (0.156 * w, 0.112 * w)]
    else:
        verts = [bd.pelvis, Vector((0, bd.waist.y, 1.0 * s + dz))]
        radii = [(0.13 * w, 0.095 * w), (0.12 * w, 0.088 * w)]
    edges = [(0, 1)]
    for side in (1, -1):
        hip, knee, ankle = (Vector((side * v.x, v.y, v.z)) for v in (bd.hip, bd.knee, bd.ankle))
        i = len(verts)
        verts += [hip, knee, ankle]
        edges += [(0, i), (i, i + 1), (i + 1, i + 2)]
        r = 1.0 if trousers else 0.8
        radii += [(0.09 * w * r, 0.092 * w * r), (0.057 * r, 0.06 * r), (0.047 * r, 0.047 * r)]
    p.add(skin_object("legs", verts, edges, radii, leg_mat))
    if trousers and tucked:
        belt = b.torus("belt", 1.0, 0.014, (0, bd.waist.y, 1.0 * s + dz), 32, 6, _m("#3A2A22", 0.5))
        belt.scale = (0.163 * w, 0.118 * w, 1)
        p.add(belt)
    if trousers and look.rolled:
        for side in (1, -1):
            cuff = b.torus("trouser_cuff", 0.05, 0.016, (side * bd.ankle.x, bd.ankle.y, bd.ankle.z + 0.07), 16, 6,
                           bottom)
            cuff.scale = (1.0, 1.05, 1.0)
            p.add(cuff)

    # Shoes: a rounded upper with a raised heel cap on a slightly wider sole, toe forward (-Y).
    for side in (1, -1):
        x = side * bd.ankle.x
        fy = bd.ankle.y - 0.015
        sm = _m(look.shoes, 0.45)
        p.add(b.box("sole", (0.102, 0.262, 0.022), (x, fy - 0.035, 0.0), 0.01, 2, _m(look.sole, 0.7)))
        p.add(b.box("shoe", (0.094, 0.245, 0.07), (x, fy - 0.038, 0.018), 0.033, 4, sm))
        p.add(b.box("shoe_heel", (0.088, 0.1, 0.1), (x, fy + 0.03, 0.018), 0.035, 4, sm))

    # Torso and sleeves in the top colour. A tucked top starts inside the trousers; a tee hangs over them.
    if tucked:
        start, r0 = Vector((0, bd.waist.y, 0.975 * s + dz)), (0.132 * w, 0.094 * w)
    else:
        start, r0 = Vector((0, 0.0, 0.9 * s + dz)), (0.182 * w, 0.132 * w)
    verts = [start, bd.waist, bd.chest, bd.upper, bd.neck]
    edges = [(0, 1), (1, 2), (2, 3), (3, 4)]
    waist_r = (0.14 * w, 0.1 * w) if tucked else (0.17 * w, 0.122 * w)
    radii = [r0, waist_r, (0.162 * bd.sh, 0.108), (0.15 * bd.sh, 0.095), (0.06, 0.058)]
    # Sleeve length as a share of the arm from shoulder (0) over elbow (0.5) to wrist (1).
    sleeve = {"shirt": 0.72, "tee": 0.3, "blouse": 0.96}[look.top_style]

    def arm_point(side, f):
        sh, el, wr = (Vector((side * v.x, v.y, v.z)) for v in (bd.shoulder, bd.elbow, bd.wrist))
        return sh + (el - sh) * (f * 2) if f <= 0.5 else el + (wr - el) * ((f - 0.5) * 2)

    for side in (1, -1):
        pts = [arm_point(side, 0.0)]
        rs = [(0.06, 0.062)]
        if sleeve > 0.5:
            pts.append(arm_point(side, 0.5))
            rs.append((0.05, 0.051))
        pts.append(arm_point(side, sleeve))
        rs.append((0.047, 0.048) if sleeve > 0.5 else (0.053, 0.054))
        i = len(verts)
        verts += pts
        edges += [(3, i)] + [(i + k, i + k + 1) for k in range(len(pts) - 1)]
        radii += rs
    p.add(skin_object("torso", verts, edges, radii, top))

    # Arms: one skin tube from inside the shoulder to the fingertips, always under the sleeves.
    for side in (1, -1):
        sh, el, wr = (Vector((side * v.x, v.y, v.z)) for v in (bd.shoulder, bd.elbow, bd.wrist))
        palm = wr + Vector((side * 0.005, -0.01, -0.055))
        tips = wr + Vector((side * 0.008, -0.018, -0.135))
        thumb = wr + Vector((side * -0.016, -0.045, -0.06))
        verts = [sh + Vector((side * -0.02, 0, -0.01)), el, wr, palm, tips, thumb]
        edges = [(0, 1), (1, 2), (2, 3), (3, 4), (3, 5)]
        radii = [(0.042, 0.043), (0.035, 0.036), (0.029, 0.025), (0.046, 0.019), (0.04, 0.016), (0.013, 0.013)]
        p.add(skin_object("arm_skin", verts, edges, radii, skin))
        if look.top_style == "shirt":
            cuff = arm_point(side, sleeve)
            t = b.torus("cuff", 0.046, 0.012, tuple(cuff), 16, 6, top)
            t.rotation_euler = (wr - el).to_track_quat("Z", "Y").to_euler()
            p.add(t)

    # Neck.
    p.add(skin_object("neck", [bd.neck + Vector((0, 0, -0.04)), bd.head + Vector((0, 0.01, -0.06))], [(0, 1)],
                      [(0.048, 0.048), (0.043, 0.043)], skin))

    # Skirt.
    if look.bottom_style == "skirt":
        z0, z1 = bd.waist.z - 0.01, (0.45 * s if not look.sit else 0.42)
        prof = [(0.165 * w, z0 - z1), (0.19 * w, (z0 - z1) * 0.75), (0.225 * w, (z0 - z1) * 0.25), (0.24 * w, 0.0)]
        prof = [(r, z) for r, z in reversed(prof)]
        sk = b.lathe("skirt", prof, 28, (0, 0.005, z1), bottom, scale=(1.0, 0.78, 1.0))
        p.add(sk)

    # Collar for shirts and blouses.
    if look.top_style in ("shirt", "blouse"):
        for side in (1, -1):
            c = b.prism("collar", [(0.0, 0.0), (0.06, 0.012), (0.045, 0.05)], 0.008,
                        (0, bd.neck.y - 0.045, bd.neck.z - 0.035), top, rad(0, 0, side * -30), radius=0.003)
            c.scale = (side, 1, 1)
            p.add(c)

    # Cardigan or vest: a second, slightly bigger torso shell, open at the front for a cardigan.
    if look.layer:
        lay = _m(look.layer, 0.85)
        low = (0.93 * s if look.bottom_style == "skirt" else 0.96 * s) + dz
        verts = [Vector((0, bd.waist.y, low)), bd.waist, bd.chest, bd.upper]
        edges = [(0, 1), (1, 2), (2, 3)]
        radii = [(0.19 * w, 0.138 * w), (0.165 * w, 0.122 * w), (0.185 * bd.sh, 0.13), (0.172 * bd.sh, 0.117)]
        if look.layer_style == "vest":
            # A vest has no sleeves: it tapers into the shirt below the shoulders, so the edge runs clean.
            verts[3] = Vector((0, bd.upper.y, bd.upper.z - 0.03 * s))
            radii[3] = (0.128 * bd.sh, 0.082)
        if look.layer_style == "cardigan":
            for side in (1, -1):
                sh, el, wr = (Vector((side * v.x, v.y, v.z)) for v in (bd.shoulder, bd.elbow, bd.wrist))
                i = len(verts)
                verts += [sh, el, el + (wr - el) * 0.86]
                edges += [(3, i), (i, i + 1), (i + 1, i + 2)]
                radii += [(0.075, 0.077), (0.064, 0.065), (0.056, 0.057)]
        p.add(skin_object("layer", verts, edges, radii, lay))
        # Buttons down the front, on the layer's surface.
        for i in range(4):
            z = bd.waist.z - 0.06 + i * 0.075
            t = (z - bd.waist.z) / (bd.chest.z - bd.waist.z)
            ry = (0.122 + (0.13 - 0.122) * max(0.0, min(1.0, t))) * w
            yc = bd.waist.y + (bd.chest.y - bd.waist.y) * max(0.0, min(1.0, t))
            p.add(b.sphere("button", 0.009, (0, yc - ry * 0.93, z), 8, (1, 0.5, 1), _m("#F3E9D2", 0.4)))

    # Apron: a shell that wraps the front of the body, with a waist band, a bow and a neck strap.
    if look.apron:
        p.items += _apron(bd, look).items

    # Head and face.
    p.items += _head(bd, look).items

    # Props.
    if look.prop:
        p.items += _prop(bd, look).items

    root = p.place(name, loc, rad(0, 0, yaw))
    return root


def _apron(bd, look):
    p = Parts()
    w = bd.w
    am = _m(look.apron, 0.7)
    rows = []
    z_bottom, z_waist, z_top = 0.52 * bd.s + bd.dz, bd.waist.z - 0.01, bd.upper.z - 0.01
    levels = [z_bottom + (z_waist - z_bottom) * i / 6 for i in range(7)] + \
             [z_waist + (z_top - z_waist) * i / 5 for i in range(1, 6)]
    cols = 13
    bm = bmesh.new()
    extra = 0.016 if (look.untucked or look.top_style == "tee") else 0.0
    for z in levels:
        if z <= z_waist:
            t = (z_waist - z) / (z_waist - z_bottom)
            half = math.radians(78)
            rx, ry = 0.185 * w + 0.03 * t + extra, 0.135 * w + 0.025 * t + extra
            yc = bd.waist.y
        else:
            t = (z - z_waist) / (z_top - z_waist)
            half = math.radians(52 - 18 * min(1.0, t * 1.4))
            rx, ry = 0.18 * bd.sh, 0.128 - 0.02 * t
            yc = bd.waist.y + (bd.chest.y - bd.waist.y) * t
        row = []
        for c in range(cols):
            a = -half + 2 * half * c / (cols - 1)
            row.append(bm.verts.new((rx * math.sin(a), yc - ry * math.cos(a), z)))
        rows.append(row)
    for r0, r1 in zip(rows, rows[1:]):
        for c in range(cols - 1):
            bm.faces.new((r0[c], r0[c + 1], r1[c + 1], r1[c]))
    mesh = bpy.data.meshes.new("apron")
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object("apron", mesh)
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    mesh.materials.append(am)
    sol = obj.modifiers.new("Solidify", "SOLIDIFY")
    sol.thickness = 0.008
    sol.offset = 1.0
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1
    p.add(obj)
    # Waist band around the body, a bow at the back, a strap around the neck, a pocket.
    band = b.torus("apron_band", 1.0, 0.012, (0, bd.waist.y, z_waist), 32, 6, am)
    band.scale = (0.188 * w, 0.138 * w, 1)
    p.add(band)
    for side in (1, -1):
        loop = b.torus("apron_bow", 0.032, 0.009, (side * 0.032, bd.waist.y + 0.14 * w, z_waist), 12, 6, am,
                       rad(90, 0, 0))
        p.add(loop)
    p.add(b.sphere("apron_knot", 0.014, (0, bd.waist.y + 0.142 * w, z_waist), 10, material=am))
    # Neck straps: from the bib's top corners up to the sides of the neck (the loop behind is hidden).
    half_top = math.radians(52 - 18)
    top_y = bd.waist.y + (bd.chest.y - bd.waist.y) - 0.108 * math.cos(half_top)
    for side in (1, -1):
        a = Vector((side * 0.18 * bd.sh * math.sin(half_top) * 0.92, top_y, z_top))
        n = Vector((side * 0.055, bd.neck.y + 0.01, bd.neck.z + 0.01))
        d = n - a
        strap = b.capsule("apron_strap", 0.009, d.length, tuple(a), 8, am)
        strap.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p.add(strap)
    pz = z_bottom + (z_waist - z_bottom) * 0.5
    t = (z_waist - pz) / (z_waist - z_bottom)
    py = bd.waist.y - (0.135 * w + 0.025 * t + extra) - 0.012
    p.add(b.box("apron_pocket", (0.17, 0.012, 0.1), (0, py, pz - 0.05), 0.012, 2, _m(look.top, 0.7)))
    if look.apron_stains:
        # Wiped hands, a tomato splash and an old grease mark on the skirt.
        from . import mess
        for k, (sx, sz, r, col) in enumerate(((0.1, 0.18, 0.035, "#8E5A3A"), (-0.07, 0.3, 0.025, "#B8452F"),
                                              (0.02, 0.08, 0.045, "#7A6A4A"), (0.12, 0.26, 0.02, "#8E5A3A"))):
            zz = z_bottom + sz
            tt = (z_waist - zz) / (z_waist - z_bottom)
            yy = bd.waist.y - (0.135 * w + 0.025 * tt + extra) - 0.011
            mess.stain(p, (sx, yy, zz), r, col, "y", look.seed * 7 + k, 0.6)
        mess.splatter(p, (-0.02, bd.waist.y - 0.15 * w - extra - 0.011, z_bottom + 0.22), 0.06, 7, "#B8452F", "y",
                      look.seed + 3, 0.7)
    return p


HEAD_A, HEAD_B, HEAD_C = 0.083, 0.098, 0.112


def _head(bd, look):
    p = Parts()
    c = bd.head
    a, bb, cc = HEAD_A, HEAD_B, HEAD_C
    skin = _m(look.skin, 0.55)
    dark = _m("#2A2624", 0.3)
    hair = _m(look.hair, 0.7)
    p.add(_ellipsoid("head", a, bb, cc, tuple(c), skin, jaw=0.32, chin=0.012))

    def on_face(x, z, out=0.0):
        return Vector((c.x + x, c.y + _front(a, bb, cc, x, z) - out, c.z + z))

    import random as _r
    rng = _r.Random(look.seed * 31 + 5)
    ex = look.expression
    # Brow tilt per expression (degrees, positive lifts the inner end), lid cover (0 open .. 1 closed).
    brow_tilt = {"smile": -10, "grin": -14, "neutral": -4, "focused": 14, "tired": -22, "yawn": -18}[ex]
    lid_cover = {"smile": 0.0, "grin": 0.0, "neutral": 0.15, "focused": 0.3, "tired": 0.55, "yawn": 1.0}[ex]
    if look.eyelids:
        lid_cover = max(lid_cover, 0.35)
    # Eyes: dark ovals with a highlight; upper lids cover them by expression; closed eyes are soft arcs.
    for side in (1, -1):
        jitter = Vector((rng.uniform(-0.002, 0.002), 0, rng.uniform(-0.0025, 0.0025)))
        e = on_face(side * 0.032, 0.012, 0.0) + jitter
        if lid_cover < 0.95:
            p.add(b.sphere("eye", 0.0125, tuple(e), 14, (0.85, 0.55, 1.15), dark))
            p.add(b.sphere("eye_hi", 0.0035, tuple(e + Vector((side * -0.003, -0.006, 0.005))), 8,
                           material=_m("#FFFFFF", 0.2)))
            if lid_cover > 0.05:
                lid = b.sphere("lid", 0.0148, tuple(e + Vector((0, 0.002, 0.016 - 0.02 * lid_cover))), 14,
                               (0.95, 0.62, 0.62 + 0.35 * lid_cover), skin)
                p.add(lid)
        else:
            closed = b.torus("eye_closed", 0.011, 0.0025, tuple(e + Vector((0, -0.001, 0.004))), 12, 4, dark,
                             rad(90, 0, 0), arc=150)
            closed.rotation_euler = rad(90, 195, 0)
            p.add(closed)
        bl = on_face(side * 0.047, 0.042, 0.002) + Vector((0, 0, rng.uniform(-0.003, 0.004)))
        brow = b.capsule("brow", 0.0045, 0.03, tuple(bl), 8, hair, rad(0, 90, 0))
        brow.rotation_euler = rad(0, 90 + side * brow_tilt, 0)
        brow.location.x = c.x + side * 0.047 - 0.015
        p.add(brow)
        ear = b.sphere("ear", 0.024, (c.x + side * (a - 0.004), c.y + 0.008, c.z + 0.002), 12, (0.35, 0.7, 1.15), skin)
        p.add(ear)
        cheek = b.sphere("cheek", 0.016, tuple(on_face(side * 0.05, -0.02, -0.004)), 10, (1, 0.35, 0.75),
                         _m("#E39A84", 0.7))
        p.add(cheek)
    # Nose: a soft wedge.
    n = on_face(0, -0.006, 0.0)
    nose = b.lathe("nose", [(0.0, 0.0), (0.013, 0.004), (0.01, 0.026), (0.0, 0.031)], 12, tuple(n), skin,
                   rad(-10, 0, 0), scale=(0.95, 0.95, 1))
    nose.location.y += 0.004
    nose.location.z -= 0.02
    p.add(nose)
    # Mouth by expression, a little off-centre.
    mouth_m = _m("#8E4A3A", 0.5)
    m = on_face(rng.uniform(-0.004, 0.004), -0.045, 0.001)
    if ex in ("smile", "grin"):
        r_ = 0.019 if ex == "smile" else 0.024
        smile = b.torus("smile", r_, 0.0035, tuple(m), 14, 6, mouth_m, rad(90, 0, 0), arc=140 if ex == "smile" else 160)
        smile.rotation_euler = rad(90, 200 + rng.uniform(-6, 6), 0)
        p.add(smile)
    elif ex == "yawn":
        p.add(b.sphere("mouth_open", 0.016, tuple(m + Vector((0, 0.004, -0.006))), 14, (1.0, 0.5, 1.4),
                       _m("#4A2420", 0.6)))
    elif ex == "tired":
        frown = b.torus("frown", 0.016, 0.003, tuple(m + Vector((0, 0, -0.012))), 14, 6, mouth_m, rad(90, 0, 0),
                        arc=110)
        frown.rotation_euler = rad(90, 35, 0)
        p.add(frown)
    else:
        line = b.capsule("mouth_line", 0.003, 0.026, tuple(m + Vector((-0.013, 0, 0))), 8, mouth_m, rad(0, 90, 0))
        line.rotation_euler = rad(0, 90 + rng.uniform(-6, 6) + (4 if ex == "focused" else 0), 0)
        p.add(line)
    if look.beard:
        p.add(_ellipsoid("beard", a * 0.95, bb * 0.75, cc * 0.45, (c.x, c.y - 0.03, c.z - 0.065), hair))
    if look.glasses:
        gm = _m("#3A3532", 0.35)
        for side in (1, -1):
            g = b.torus("glasses", 0.019, 0.0028, tuple(on_face(side * 0.032, 0.012, 0.014)), 18, 5, gm, rad(90, 0, 0))
            p.add(g)
            arm = b.box("glasses_arm", (0.003, bb * 1.05, 0.003), (c.x + side * (a + 0.004), c.y - bb * 0.45, c.z + 0.016),
                        0.0, 1, gm)
            p.add(arm)
        p.add(b.box("glasses_bridge", (0.022, 0.003, 0.003), tuple(on_face(0, 0.018, 0.016)), 0.0, 1, gm))
    p.items += _hair(c, look, hair).items
    p.items += _hat(c, look).items
    return p


def _hair_shell(name, c, material, keep, scale=1.08, thickness=0.012):
    """A hair shell over the head: an ellipsoid a little bigger than the head, keeping only faces where keep(x,y,z)."""
    a, bb, cc = HEAD_A * scale, HEAD_B * scale, HEAD_C * scale
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=20, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * a, v.co.y * bb, v.co.z * cc))
    dead = [f for f in bm.faces if not keep(*f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    obj.location = tuple(c)
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    mesh.materials.append(material)
    sol = obj.modifiers.new("Solidify", "SOLIDIFY")
    sol.thickness = thickness
    sol.offset = -1.0
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1
    return obj


def _hair(c, look, mat):
    p = Parts()
    style = look.hair_style

    def face_free(x, y, z, brow=0.045, back=-0.03):
        # Keep the face free: in front (y < -0.02) only keep the part above the brow line.
        if y < -0.02 and z < brow + 0.2 * (y + 0.02):
            return False
        return z > back

    if style == "short":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: face_free(x, y, z, 0.05, -0.025)))
        for i, (x, z, rot) in enumerate(((-0.035, 0.075, 30), (0.0, 0.085, 0), (0.035, 0.078, -25))):
            lock = b.sphere("lock", 0.03, (c.x + x, c.y - 0.06, c.z + z), 10, (1.2, 0.7, 0.55), mat)
            lock.rotation_euler = rad(15, 0, rot)
            p.add(lock)
    elif style == "quiff":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: face_free(x, y, z, 0.055, -0.02)))
        q = b.capsule("quiff", 0.04, 0.14, (c.x, c.y - 0.045, c.z + 0.07), 14, mat, rad(-70, 0, 0), taper=0.55)
        p.add(q)
    elif style == "bob":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: face_free(x, y, z, 0.045, -0.085) and not (
            y < -0.035 and abs(x) < 0.07 and z < 0.045), 1.12, 0.016))
    elif style == "bun":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: face_free(x, y, z, 0.05, -0.035), 1.07))
        p.add(b.sphere("bun", 0.042, (c.x, c.y + 0.085, c.z + 0.075), 16, (1, 0.9, 0.9), mat))
        p.add(b.torus("bun_band", 0.032, 0.006, (c.x, c.y + 0.07, c.z + 0.065), 16, 6, _m(look.prop_colour, 0.5),
                      rad(-60, 0, 0)))
    elif style == "balding":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: y > -0.01 and -0.045 < z < 0.055, 1.06, 0.01))
    elif style == "curly":
        p.add(_hair_shell("hair", c, mat, lambda x, y, z: face_free(x, y, z, 0.05, -0.03), 1.06))
        import random
        rng = random.Random(5)
        for i in range(26):
            th = rng.uniform(0, 2 * math.pi)
            ph = rng.uniform(0.15, 1.35)
            x = math.cos(th) * math.sin(ph) * HEAD_A * 1.1
            y = math.sin(th) * math.sin(ph) * HEAD_B * 1.1
            z = math.cos(ph) * HEAD_C * 1.05
            if y < -0.02 and z < 0.06:
                continue
            p.add(b.sphere("curl", 0.026, (c.x + x, c.y + y, c.z + z), 10, material=mat))
    if look.messy:
        import random
        # Uneven tufts on the hair surface break the clean silhouette (no spikes).
        rng = random.Random(look.seed + 40)
        for i in range(9):
            th = rng.uniform(0, 2 * math.pi)
            ph = rng.uniform(0.25, 1.0)
            d = Vector((math.cos(th) * math.sin(ph), math.sin(th) * math.sin(ph) * 1.05, math.cos(ph)))
            if d.y < -0.4 and d.z < 0.75:
                continue
            pos = Vector((c.x + d.x * HEAD_A * 1.08, c.y + d.y * HEAD_B * 1.08, c.z + d.z * HEAD_C * 1.06))
            tuft = b.sphere("tuft", rng.uniform(0.018, 0.026), tuple(pos), 10, (1.0, 0.55, 0.45), mat)
            tuft.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
            p.add(tuft)
    return p


def _hat(c, look):
    p = Parts()
    if look.hat == "diner":
        # Folded paper cook cap: a long soft wedge sitting front to back.
        hm = _m(look.hat_colour, 0.8)
        cap = b.prism("cap", [(-0.105, 0.0), (0.105, 0.0), (0.085, 0.07), (-0.085, 0.07)], 0.13,
                      (c.x, c.y, c.z + 0.075), hm, rad(0, 0, 90), radius=0.012, segments=2)
        p.add(cap)
        p.add(b.box("cap_band", (0.135, 0.215, 0.018), (c.x, c.y, c.z + 0.07), 0.008, 2, _m(look.prop_colour, 0.6)))
    elif look.hat == "flat":
        hm = _m(look.hat_colour, 0.85)
        top = b.sphere("cap", 0.1, (c.x, c.y + 0.005, c.z + 0.065), 20, (1.0, 1.08, 0.42), hm)
        p.add(top)
        p.add(b.box("peak", (0.13, 0.07, 0.012), (c.x, c.y - 0.1, c.z + 0.05), 0.02, 3, hm, rad(-8, 0, 0)))
    return p


def _prop(bd, look):
    p = Parts()
    side = 1
    wr = Vector((side * bd.wrist.x, bd.wrist.y, bd.wrist.z))
    hand = wr + Vector((side * 0.01, -0.02, -0.075))
    pm = _m(look.prop_colour, 0.5)
    if look.prop == "spoon":
        # A wooden spoon held in the fist, bowl down.
        wood = _m("#B98B5E", 0.6)
        palm = wr + Vector((side * 0.006, -0.03, -0.07))
        p.add(b.capsule("spoon_handle", 0.008, 0.26, tuple(palm + Vector((0, 0, -0.2))), 10, wood))
        p.add(b.sphere("spoon_bowl", 0.026, tuple(palm + Vector((0, -0.006, -0.22))), 12, (0.75, 0.42, 1.25), wood))
    elif look.prop == "bag":
        p.add(b.box("bag", (0.07, 0.22, 0.17), tuple(hand + Vector((0.03, 0.0, -0.22))), 0.03, 3, pm))
        p.add(b.torus("bag_handle", 0.06, 0.007, tuple(hand + Vector((0.03, 0.0, -0.05))), 16, 6,
                      _m("#6B4A33", 0.5), rad(90, 0, 90), arc=180))
    elif look.prop == "tote":
        p.add(b.box("tote", (0.05, 0.3, 0.32), tuple(hand + Vector((0.035, 0.0, -0.36))), 0.01, 2, pm))
        p.add(b.torus("tote_strap", 0.09, 0.008, tuple(hand + Vector((0.035, 0.0, -0.04))), 16, 6, pm, rad(90, 0, 90),
                      arc=180))
        p.add(b.cyl("tote_leaf", 0.04, 0.004, tuple(hand + Vector((0.062, 0.0, -0.2))), 16,
                    material=_m("#5E8F4F", 0.6), rot=rad(0, 90, 0)))
    elif look.prop == "cane":
        p.add(b.cyl("cane", 0.011, 0.86 * bd.s, (hand.x + 0.01, hand.y - 0.05, 0.0), 10,
                    material=_m("#6B4A33", 0.45)))
        p.add(b.torus("cane_hook", 0.035, 0.012, (hand.x + 0.01, hand.y - 0.05 + 0.035, 0.86 * bd.s), 12, 6,
                      _m("#6B4A33", 0.45), rad(90, 0, 90), arc=180))
    return p


def cast_lived(palette):
    """Round 3: the same people on a normal working day, plus a helper. Mixed expressions and wear."""
    base = cast(palette)
    base["cook"].expression, base["cook"].apron_stains, base["cook"].messy, base["cook"].seed = "focused", True, True, 1
    base["older_woman"].expression, base["older_woman"].seed = "smile", 2
    base["older_man"].expression, base["older_man"].seed = "neutral", 3
    base["younger"].expression, base["younger"].seed = "yawn", 4
    base["helper"] = Look(height=1.7, build=1.16, shoulders=1.04, skin="#C99272", hair="#3A2A22", hair_style="bob",
                          top="#8CC5AC", top_style="tee", bottom="#5E554D", apron="#F3E9D2", apron_stains=True,
                          shoes="#2C2826", expression="grin", seed=5, rolled=True, messy=True)
    return base


# --- the cast --------------------------------------------------------------------------------------------------------

def cast(palette):
    """The round 2 cast, coloured from a palette dict (hex values)."""
    return {
        "cook": Look(height=1.76, build=1.02, top="#F3E9D2", top_style="shirt", bottom="#3F5F78",
                     apron=palette["accent"], hat="diner", hat_colour="#F7F1E3", prop_colour=palette["main_dark"],
                     hair="#5A3A2A", hair_style="short", prop="spoon"),
        "older_woman": Look(height=1.63, build=1.08, shoulders=0.92, lean=0.35, skin="#E4B597", hair="#D8D2CA",
                            hair_style="bun", top="#F3E9D2", top_style="blouse", layer=palette["main_dark"],
                            layer_style="cardigan", bottom=palette["accent2"], bottom_style="skirt", legs="#C9A58E",
                            shoes="#5A3A2A", glasses=True, eyelids=True, prop="bag", prop_colour=palette["accent"]),
        "older_man": Look(height=1.72, build=1.1, lean=0.45, skin="#C98E6B", hair="#C9C2B8", hair_style="balding",
                          top="#E8E0CF", top_style="blouse", layer=palette["accent"], layer_style="vest",
                          bottom="#5E554D", shoes="#3A2A22", glasses=True, eyelids=True, hat="flat",
                          hat_colour="#7E6A55", prop="cane", beard=False),
        "younger": Look(height=1.68, build=0.95, skin="#8E5B3E", hair="#1E1714", hair_style="curly", top="#E58436",
                        top_style="tee", bottom="#4E6E8E", shoes="#F3E9D2", sole="#D8CDB8", prop="tote",
                        prop_colour="#D9C7A6"),
    }
