"""Geometry and material helpers for the style lab (spec 046). Every helper is deterministic."""

import math

import bmesh
import bpy
from mathutils import Euler, Vector

from . import palettes

_material_cache = {}
_palette_id = "P1"


def reset(palette_id):
    """Start from an empty factory scene and select the palette for this build."""
    global _palette_id
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _material_cache.clear()
    _palette_id = palette_id
    set_grit(0.0)


def palette_id():
    return _palette_id


def set_palette(palette_id_):
    """Switch the palette for the following mat() calls (used by sheets that show two palettes)."""
    global _palette_id
    _palette_id = palette_id_


def rad(x, y=0.0, z=0.0):
    return Euler((math.radians(x), math.radians(y), math.radians(z)))


# --- materials -------------------------------------------------------------------------------------------------------

_grit = 0.0


def set_grit(level):
    """0 clean (rounds 1 and 2), 1 lived-in, 2 rough. Set before building; changes every material made after it."""
    global _grit
    _grit = level
    _material_cache.clear()


def grit():
    return _grit


GRIME = "#3B2E22"


def _add_grit(m, col, rough, metal):
    """Lived-in surface: per-object colour drift, grime in occluded spots and near the floor, rougher, uneven gloss."""
    g = _grit
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    L = nt.links
    info = nt.nodes.new("ShaderNodeObjectInfo")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    rgb = nt.nodes.new("ShaderNodeRGB")
    rgb.outputs[0].default_value = col

    def map_range(src, a, bb, c, d):
        n = nt.nodes.new("ShaderNodeMapRange")
        n.clamp = True
        L.new(src, n.inputs["Value"])
        n.inputs["From Min"].default_value, n.inputs["From Max"].default_value = a, bb
        n.inputs["To Min"].default_value, n.inputs["To Max"].default_value = c, d
        return n.outputs["Result"]

    def math(op, a, bb=None, value=None):
        n = nt.nodes.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = op in ("MULTIPLY_ADD",)
        if isinstance(a, float):
            n.inputs[0].default_value = a
        else:
            L.new(a, n.inputs[0])
        if bb is not None:
            if isinstance(bb, float):
                n.inputs[1].default_value = bb
            else:
                L.new(bb, n.inputs[1])
        return n.outputs[0]

    # Colour drift per object: a touch of hue, saturation and value.
    hsv = nt.nodes.new("ShaderNodeHueSaturation")
    L.new(map_range(info.outputs["Random"], 0, 1, 0.5 - 0.012 * g, 0.5 + 0.012 * g), hsv.inputs["Hue"])
    L.new(map_range(info.outputs["Random"], 0, 1, 1 - 0.08 * g, 1 + 0.04 * g), hsv.inputs["Saturation"])
    L.new(map_range(info.outputs["Random"], 0, 1, 1 - 0.07 * g, 1 + 0.03 * g), hsv.inputs["Value"])
    L.new(rgb.outputs[0], hsv.inputs["Color"])
    # Grime: occlusion plus a band near the floor, broken up by noise.
    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.inputs["Distance"].default_value = 0.35
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 3.5
    noise.inputs["Detail"].default_value = 6.0
    L.new(geo.outputs["Position"], noise.inputs["Vector"])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Position"], sep.inputs[0])
    floor_band = map_range(sep.outputs["Z"], 0.0, 0.6, 1.0, 0.0)
    # The band is for upright faces (wall feet, cabinet plinths); floors and worktops only get a trace of it.
    nsep = nt.nodes.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Normal"], nsep.inputs[0])
    upright = map_range(math("ABSOLUTE", nsep.outputs["Z"]), 0.3, 0.9, 1.0, 0.15)
    floor_band = math("MULTIPLY", floor_band, upright)
    occl = map_range(ao.outputs["AO"], 0.35, 1.0, 1.0, 0.0)
    dirt = math("ADD", math("MULTIPLY", occl, 0.9), math("MULTIPLY", floor_band, 0.55))
    patchy = map_range(noise.outputs["Fac"], 0.38, 0.68, 0.15, 1.4)
    fac = math("MULTIPLY", math("MULTIPLY", dirt, patchy), 0.42 * g)
    fac_c = math("MINIMUM", fac, 0.75)
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY" if metal < 0.5 else "MIX"
    L.new(fac_c, mix.inputs["Factor"])
    L.new(hsv.outputs["Color"], mix.inputs[6])
    mix.inputs[7].default_value = palettes.hex_to_linear(GRIME)
    L.new(mix.outputs[2], bsdf.inputs["Base Color"])
    # Rougher, with uneven gloss (fingerprints, wipe marks).
    base_r = (rough + 0.15 * g) if metal >= 0.5 else max(rough, 0.48 + 0.12 * g)
    rmap = map_range(noise.outputs["Fac"], 0.3, 0.7, base_r - 0.08 * g, base_r + 0.1 * g)
    L.new(math("ADD", rmap, math("MULTIPLY", fac_c, 0.4)), bsdf.inputs["Roughness"])


def mat(role, metal=0.0, rough=0.55, emit=0.0, alpha=1.0, hex_override=None):
    """A Principled material for a palette role (or a fixed hex). Cached by its settings."""
    hex_str = hex_override or palettes.colour(_palette_id, role)
    key = (hex_str, metal, rough, emit, alpha)
    if key in _material_cache:
        return _material_cache[key]
    m = bpy.data.materials.new(f"{role}_{hex_str}")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = palettes.hex_to_linear(hex_str)
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if _grit > 0 and emit <= 0 and alpha >= 1.0:
        _add_grit(m, col, rough, metal)
    if emit > 0:
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = emit
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except AttributeError:
            m.blend_method = "BLEND"
    _material_cache[key] = m
    return m


def chrome():
    return mat("chrome", metal=1.0, rough=0.18)


def glass():
    """Solid glossy glass: reads as glass in low-poly and needs no transparency sorting in the game."""
    return mat("glass", rough=0.1, metal=0.3)


_glow = 1.0


def set_glow(factor):
    """How strongly lit things glow: low at noon, high at night. Set by the scene before building."""
    global _glow
    _glow = factor


def lamp(strength=1.0, role="lamp_warm"):
    """An emissive material for things that are on (lamps, signs, lit windows), scaled by the time of day."""
    return mat(role, emit=strength * _glow, rough=0.4)


def window():
    """A shop or house window: glossy glass by day, a warm lit room after sunset."""
    if _glow >= 1.0:
        return mat("lamp_warm", emit=0.45 * _glow, rough=0.2)
    return mat("glass", rough=0.1, metal=0.3)


# --- objects ---------------------------------------------------------------------------------------------------------

def _finish(obj, material, loc, rot, smooth=True):
    obj.location = loc
    if rot is not None:
        obj.rotation_euler = rot
    if material is not None:
        obj.data.materials.clear()
        obj.data.materials.append(material)
    if smooth:
        obj.data.polygons.foreach_set("use_smooth", [True] * len(obj.data.polygons))
    return obj


def bevel(obj, width, segments, angle_deg=40.0):
    """Rounded edges with hardened normals: flat faces stay flat, the bevels read soft."""
    if width <= 0:
        return obj
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle_deg)
    m.harden_normals = True
    m.use_clamp_overlap = True
    return obj


def new_mesh_object(name, mesh):
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(name, size, loc=(0, 0, 0), radius=0.0, segments=3, material=None, rot=None):
    """A box with its bottom at loc.z (pivot bottom centre), optional rounded edges."""
    sx, sy, sz = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz + sz / 2))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = new_mesh_object(name, mesh)
    _finish(obj, material, loc, rot)
    r = min(radius, min(size) * 0.49)
    return bevel(obj, r, segments)


def lathe(name, profile, verts=24, loc=(0, 0, 0), material=None, rot=None, scale=(1, 1, 1), smooth=True):
    """Revolve a profile of (radius, z) points around Z. A radius of 0 closes that end with a pole."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z * scale[2]))])
            continue
        ring = []
        for i in range(verts):
            a = 2 * math.pi * i / verts
            ring.append(bm.verts.new((r * math.cos(a) * scale[0], r * math.sin(a) * scale[1], z * scale[2])))
        rings.append(ring)
    for a_ring, b_ring in zip(rings, rings[1:]):
        if len(a_ring) == 1 and len(b_ring) == 1:
            continue
        for i in range(verts):
            j = (i + 1) % verts
            if len(a_ring) == 1:
                bm.faces.new((a_ring[0], b_ring[i], b_ring[j]))
            elif len(b_ring) == 1:
                bm.faces.new((a_ring[i], b_ring[0], a_ring[j]))
            else:
                bm.faces.new((a_ring[i], b_ring[i], b_ring[j], a_ring[j]))
    # Close open ends with a flat cap.
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = new_mesh_object(name, mesh)
    _finish(obj, material, loc, rot, smooth)
    if smooth:
        # Keep the flat caps flat.
        m = obj.modifiers.new("Normals", "WEIGHTED_NORMAL")
        m.keep_sharp = True
        m.mode = "FACE_AREA"
    return obj


def cyl(name, r, h, loc=(0, 0, 0), verts=24, edge=0.0, segments=3, material=None, rot=None):
    """A cylinder standing on loc (bottom at loc.z), optional rounded rims."""
    obj = lathe(name, [(r, 0), (r, h)], verts, loc, material, rot)
    obj.modifiers.clear()
    return bevel(obj, min(edge, r * 0.95, h * 0.49), segments, 30)


def sphere(name, r, loc=(0, 0, 0), verts=24, scale=(1, 1, 1), material=None, rot=None):
    rings = max(6, verts // 2)
    profile = [(0, -r)]
    for i in range(1, rings):
        a = -math.pi / 2 + math.pi * i / rings
        profile.append((r * math.cos(a), r * math.sin(a)))
    profile.append((0, r))
    obj = lathe(name, profile, verts, loc, material, rot, scale)
    obj.modifiers.clear()
    return obj


def capsule(name, r, length, loc=(0, 0, 0), verts=16, material=None, rot=None, taper=1.0):
    """A pill along Z from loc (bottom) of total length; taper < 1 narrows the top end."""
    rings = 5
    r_top = r * taper
    body = max(length - r - r_top, 0.0)
    profile = []
    for i in range(rings + 1):
        a = math.pi / 2 * i / rings
        profile.append((r * math.sin(a), r - r * math.cos(a)))
    top_c = r + body
    for i in range(1, rings + 1):
        a = math.pi / 2 * i / rings
        profile.append((r_top * math.cos(a), top_c + r_top * math.sin(a)))
    obj = lathe(name, profile, verts, loc, material, rot)
    obj.modifiers.clear()
    return obj


def torus(name, major, minor, loc=(0, 0, 0), verts=24, minor_verts=8, material=None, rot=None, arc=360.0):
    bm = bmesh.new()
    closed = arc >= 359.9
    steps = verts if closed else verts + 1
    rings = []
    for i in range(steps):
        a = math.radians(arc) * i / verts
        ring = []
        for k in range(minor_verts):
            b = 2 * math.pi * k / minor_verts
            d = major + minor * math.cos(b)
            ring.append(bm.verts.new((d * math.cos(a), d * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for ra, rb in pairs:
        for k in range(minor_verts):
            n = (k + 1) % minor_verts
            bm.faces.new((ra[k], rb[k], rb[n], ra[n]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = new_mesh_object(name, mesh)
    return _finish(obj, material, loc, rot)


def prism(name, outline, depth, loc=(0, 0, 0), material=None, rot=None, radius=0.0, segments=3):
    """Extrude a closed 2D outline (x, z points, counter-clockwise) along Y by depth, centred on loc.y."""
    bm = bmesh.new()
    front = [bm.verts.new((x, -depth / 2, z)) for x, z in outline]
    back = [bm.verts.new((x, depth / 2, z)) for x, z in outline]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = new_mesh_object(name, mesh)
    _finish(obj, material, loc, rot)
    return bevel(obj, radius, segments)


def rounded_rect_outline(w, h, r, steps=4, x0=0.0, z0=0.0):
    """Counter-clockwise outline of a rounded rectangle with bottom-left at (x0, z0)."""
    r = min(r, w / 2 - 1e-4, h / 2 - 1e-4)
    pts = []
    corners = [(x0 + w - r, z0 + r, -90), (x0 + w - r, z0 + h - r, 0), (x0 + r, z0 + h - r, 90), (x0 + r, z0 + r, 180)]
    for cx, cz, start in corners:
        for i in range(steps + 1):
            a = math.radians(start + 90 * i / steps)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def text(name, body, size, loc, rot=None, material=None, extrude=0.01, align="CENTER"):
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = body
    curve.size = size
    curve.extrude = extrude
    curve.align_x = align
    curve.align_y = "CENTER"
    obj = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    if rot is not None:
        obj.rotation_euler = rot
    if material is not None:
        curve.materials.append(material)
    return obj


def group(name, children, loc=(0, 0, 0), rot=None):
    """An empty that parents the given objects, keeping their local placement."""
    empty = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(empty)
    for c in children:
        c.parent = empty
    empty.location = loc
    if rot is not None:
        empty.rotation_euler = rot
    return empty


class Parts:
    """Collects the objects of one model so they can be grouped and placed together."""

    def __init__(self):
        self.items = []

    def add(self, obj):
        self.items.append(obj)
        return obj

    def place(self, name, loc=(0, 0, 0), rot=None):
        return group(name, self.items, loc, rot)
