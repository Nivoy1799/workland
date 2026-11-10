"""Person and food test models for the style lab (spec 046). Person 1.75 m, normal proportions (D30)."""

import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from .build import Parts, rad


def _hair(name, r, center, scale, material, fringe_z=0.035):
    """A hair cap: a sphere cut by a tilted plane, so the face stays free and the back hangs lower."""
    obj = b.sphere(name, r, (0, 0, 0), 20, scale, material)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    # The cut rises towards the face (-Y) and drops at the back of the head.
    no = Vector((0, 0.55, 1)).normalized()
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, fringe_z - 0.03),
                           plane_no=no, clear_inner=True)
    bm.to_mesh(obj.data)
    bm.free()
    obj.location = center
    return obj


def person(shape, loc, yaw=0.0, older=False, apron_on=True):
    """A standing adult facing -Y: the cook (apron) or an older customer (grey hair, cardigan). Static, no rig."""
    p = Parts()
    skin = b.mat("skin", rough=0.6)
    shirt = b.mat("shirt" if not older else "main", rough=0.7)
    trousers = b.mat("trousers", rough=0.75)
    apron = b.mat("apron", rough=0.6)
    dark = b.mat("dark", rough=0.5)
    hair = b.mat("hair" if not older else "hair_grey", rough=0.7)
    v = 16
    for sx in (-1, 1):
        p.add(b.box("shoe", (0.1, 0.26, 0.07), (sx * 0.1, -0.03, 0.0), 0.035, 3, dark))
        p.add(b.capsule("leg", 0.066, 0.9, (sx * 0.1, 0.0, 0.03), v, trousers, taper=1.15))
    p.add(b.box("hips", (0.33, 0.2, 0.17), (0, 0, 0.84), 0.08, 4, trousers))
    taper = 0.82 if shape.streamline else 0.92
    torso_prof = [(0.165, 0), (0.17 * taper + 0.015, 0.2), (0.19, 0.38), (0.175, 0.46), (0.12, 0.52), (0.05, 0.545),
                  (0.0, 0.55)]
    p.add(b.lathe("torso", torso_prof, v, (0, 0, 0.94), shirt, scale=(1.0, 0.64, 1)))
    if apron_on:
        # Apron: one shaped piece (narrow bib, wide skirt) just in front of the body, a neck strap and a pocket.
        a = [(-0.18, 0.0), (0.18, 0.0), (0.19, 0.5), (0.12, 0.56), (0.11, 0.9), (-0.11, 0.9), (-0.12, 0.56),
             (-0.19, 0.5)]
        p.add(b.prism("apron", a, 0.014, (0, -0.128, 0.46), apron, radius=0.006, segments=2))
        p.add(b.box("apron_pocket", (0.15, 0.008, 0.08), (0, -0.138, 0.74), 0.01, 2, b.mat("cream", rough=0.6)))
        p.add(b.box("apron_belt", (0.39, 0.25, 0.03), (0, 0.0, 0.99), 0.012, 2, apron))
        p.add(b.torus("apron_strap", 0.075, 0.008, (0, -0.06, 1.39), 16, 6, apron, rad(25, 0, 0), arc=180))
    else:
        # Cardigan buttons.
        for i in range(3):
            p.add(b.sphere("button", 0.012, (0, -0.122, 1.05 + i * 0.1), 8, material=b.mat("cream", rough=0.4)))
    for sx in (-1, 1):
        arm = b.capsule("arm", 0.048, 0.62, (sx * 0.215, 0.0, 0.8), v, shirt, taper=1.05)
        arm.rotation_euler = rad(0, sx * -5, 0)
        p.add(arm)
        p.add(b.sphere("hand", 0.045, (sx * 0.24, -0.01, 0.8), 12, (0.8, 0.65, 1.1), skin))
    p.add(b.cyl("neck", 0.05, 0.1, (0, 0, 1.45), 12, material=skin))
    head_c = 1.62
    p.add(b.sphere("head", 0.105, (0, 0, head_c), 20, (0.9, 0.98, 1.15), skin))
    p.add(b.sphere("nose", 0.017, (0, -0.1, head_c - 0.012), 10, (0.9, 0.8, 1.2), skin))
    p.add(b.torus("smile", 0.026, 0.0045, (0, -0.093, head_c - 0.04), 12, 6, b.mat("mouth", rough=0.5),
                  rad(90, 0, 180), arc=150))
    p.items[-1].rotation_euler = rad(90, 195, 0)
    for sx in (-1, 1):
        p.add(b.sphere("eye", 0.012, (sx * 0.037, -0.092, head_c + 0.02), 10, (1, 0.5, 1.2), dark))
        p.add(b.box("brow", (0.035, 0.008, 0.008), (sx * 0.038, -0.094, head_c + 0.045), 0.003, 1, hair))
        p.add(b.sphere("cheek", 0.016, (sx * 0.055, -0.082, head_c - 0.02), 8, (1, 0.4, 0.8),
                       b.mat("blush", rough=0.7)))
        p.add(b.sphere("ear", 0.022, (sx * 0.095, 0.0, head_c), 10, (0.4, 0.8, 1.2), skin))
    if older:
        for sx in (-1, 1):
            p.add(b.torus("glasses", 0.022, 0.003, (sx * 0.038, -0.1, head_c + 0.02), 16, 4, dark, rad(90)))
        p.add(b.box("glasses_bridge", (0.03, 0.004, 0.004), (0, -0.1, head_c + 0.022), 0.0, 1, dark))
    if older:
        # Short grey hair with a soft top.
        p.add(_hair("hair", 0.114, (0, 0.008, head_c + 0.006), (0.97, 1.02, 1.1), hair, 0.045))
        p.add(b.sphere("hair_back", 0.092, (0, 0.045, head_c - 0.04), 16, (1.1, 0.75, 0.7), hair))
    elif shape.streamline:
        # Swept-back hair with a little quiff.
        p.add(_hair("hair", 0.113, (0, 0.006, head_c + 0.015), (0.94, 1.02, 1.1), hair, 0.07))
        quiff = b.capsule("quiff", 0.04, 0.16, (0, -0.05, head_c + 0.1), 12, hair, taper=0.6)
        quiff.rotation_euler = rad(-105, 0, 0)
        p.add(quiff)
    else:
        # Rounded bob that frames the face.
        p.add(_hair("hair", 0.118, (0, 0.008, head_c + 0.01), (0.97, 1.02, 1.1), hair, 0.06))
        p.add(b.sphere("hair_back", 0.1, (0, 0.05, head_c - 0.05), 16, (1.08, 0.75, 0.75), hair))
    return p.place("Person", loc, rad(0, 0, yaw))


# --- food -------------------------------------------------------------------------------------------------------------

def _displace(obj, seed, amp, freq=3.0):
    """Lumpy surface: move each vertex along its direction from the centre by seeded smooth noise."""
    rng = random.Random(seed)
    waves = [(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized(),
              rng.uniform(0, 2 * math.pi)) for _ in range(5)]
    for vert in obj.data.vertices:
        d = vert.co.normalized() if vert.co.length > 1e-6 else Vector((0, 0, 1))
        n = sum(math.sin(freq * d.dot(w) * math.pi + ph) for w, ph in waves) / len(waves)
        vert.co = vert.co * (1.0 + amp * n)


def _cut(obj, inner_material):
    """Cut the object at z = 0 (local), keep the bottom half and close the cut with the inner material."""
    obj.data.materials.append(inner_material)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, 0), plane_no=(0, 0, 1), clear_outer=True)
    cut_edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    filled = bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
    for f in filled["faces"]:
        f.material_index = 1
        f.smooth = False
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def potato(loc, cut=False, seed=3):
    r = 0.045
    obj = b.sphere("potato", r, (0, 0, 0), 14, (1, 1, 1), b.mat("potato_skin", rough=0.8))
    _displace(obj, seed, 0.09)
    for vert in obj.data.vertices:
        vert.co = Vector((vert.co.x, vert.co.y * 0.72, vert.co.z * 0.6))
    if cut:
        # Keep the bottom half; the cut face looks up so the flesh shows.
        _cut(obj, b.mat("potato_flesh", rough=0.6))
        obj.rotation_euler = rad(0, 0, 25)
    low = min(v.co.z for v in obj.data.vertices)
    obj.location = (loc[0], loc[1], loc[2] - low)
    return obj


def tomato(loc, cut=False):
    p = Parts()
    r = 0.036
    obj = b.sphere("tomato", r, (0, 0, 0), 24, (1, 1, 1), b.mat("tomato", rough=0.3))
    for vert in obj.data.vertices:
        a = math.atan2(vert.co.y, vert.co.x)
        flat = 0.78
        rib = 1.0 + 0.035 * math.cos(6 * a) * (1 - abs(vert.co.z) / r)
        vert.co = Vector((vert.co.x * rib, vert.co.y * rib, vert.co.z * flat))
    x, y, z = loc
    top = r * 0.78
    if cut:
        # Keep the bottom half, cut face up, with seed chambers on it.
        _cut(obj, b.mat("tomato_flesh", rough=0.4))
        obj.location = (x, y, z + top)
        p.add(obj)
        face_z = z + top
        for i in range(4):
            a = 2 * math.pi * i / 4 + 0.4
            seeds = b.sphere("seeds", 0.008, (x + math.cos(a) * 0.017, y + math.sin(a) * 0.017, face_z), 10,
                             (1.0, 0.6, 0.12), b.mat("tomato_seed", rough=0.3))
            seeds.rotation_euler = rad(0, 0, math.degrees(a))
            p.add(seeds)
        p.add(b.cyl("core", 0.007, 0.001, (x, y, face_z), 10, material=b.mat("tomato_seed", rough=0.4)))
        return p
    obj.location = (x, y, z + top)
    p.add(obj)
    for i in range(5):
        a = 2 * math.pi * i / 5
        leaf = b.sphere("calyx", 0.013, (x + math.cos(a) * 0.009, y + math.sin(a) * 0.009, z + 2 * top - 0.002), 8,
                        (1.0, 0.35, 0.15), b.mat("leaf_green", rough=0.6))
        leaf.rotation_euler = rad(0, 0, math.degrees(a))
        p.add(leaf)
    p.add(b.cyl("stem", 0.003, 0.012, (x, y, z + 2 * top - 0.003), 8, material=b.mat("leaf_green", rough=0.6)))
    return p


def food_board(loc):
    """A wooden board with a whole and a cut potato and tomato."""
    x, y, z = loc
    p = Parts()
    p.add(b.box("board", (0.34, 0.22, 0.02), (x, y, z), 0.008, 2, b.mat("wood", rough=0.6)))
    top = z + 0.02
    p.add(potato((x - 0.11, y + 0.04, top), False, 3))
    p.add(potato((x - 0.03, y - 0.05, top), True, 3))
    p.items += tomato((x + 0.05, y + 0.05, top), False).items
    p.items += tomato((x + 0.12, y - 0.04, top), True).items
    return p
