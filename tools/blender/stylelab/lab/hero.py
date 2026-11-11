"""Round 2 hero models for the style lab (spec 046): the stove, both cars and the shop front at "ship" detail.

Axes: X right, Y away from the camera, Z up, fronts face -Y (cars: front is -X). Sizes in metres.
"""

import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import build as b
from . import kitchen
from .build import Parts, rad


def _m(hex_, rough=0.55, metal=0.0, emit=0.0, alpha=1.0):
    return b.mat("h", metal=metal, rough=rough, emit=emit, alpha=alpha, hex_override=hex_)


def clear_glass(tint="#9FC3CF", alpha=0.32):
    return _m(tint, rough=0.05, metal=0.0, alpha=alpha)


def _cap(name, a, bb, c, center, keep_above, material, segments=(32, 18)):
    """Top part of an ellipsoid (z above keep_above, in unit-sphere terms -1..1), closed underneath."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments[0], v_segments=segments[1], radius=1.0)
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, keep_above),
                                 plane_no=(0, 0, 1), clear_inner=True)
    edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    for v in bm.verts:
        v.co = Vector((v.co.x * a, v.co.y * bb, v.co.z * c))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = b.new_mesh_object(name, mesh)
    obj.location = center
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    mesh.materials.append(material)
    return obj


# --- stove -----------------------------------------------------------------------------------------------------------

def stove(shape, x0=0.0, towel="#E58436"):
    """A 0.6 m cooker with grates, a tilted control panel with ticked dials and a lamp, an oven with a visible rack,
    a storage drawer, hinges, a plinth, a tea towel on the handle and (streamline) a backguard with a clock."""
    p = Parts()
    W, D, H = 0.6, kitchen.DEPTH, kitchen.WORKTOP_H
    cx = x0 + W / 2
    body_role = "accent2" if not shape.streamline else "cream"
    body = b.mat(body_role, rough=0.3)
    dark = _m("#2C2826", 0.45)
    iron = _m("#3A3532", 0.6, 0.3)
    r = shape.radius * 0.55
    p.add(b.box("stove_body", (W - 0.01, D - 0.03, H - 0.09), (cx, D / 2 + 0.005, 0.07), r, shape.segments, body))
    p.add(b.box("stove_plinth", (W - 0.07, D - 0.12, 0.08), (cx, D / 2 + 0.02, 0.0), 0.01, 2, dark))
    # Hob: dark enamel, four burners with caps and cast-iron grates.
    p.add(b.box("hob", (W - 0.02, D - 0.05, 0.025), (cx, D / 2 + 0.01, H - 0.025), 0.012, 2, _m("#2E2A27", 0.25)))
    for bx, by, br in ((0.15, 0.18, 0.055), (0.45, 0.18, 0.045), (0.15, 0.44, 0.045), (0.45, 0.44, 0.055)):
        x, y = x0 + bx, by
        p.add(b.cyl("burner", br, 0.012, (x, y, H), 20, edge=0.004, material=_m("#9AA0A3", 0.35, 0.8)))
        p.add(b.cyl("burner_cap", br * 0.55, 0.012, (x, y, H + 0.01), 20, edge=0.004, material=dark))
        p.add(b.torus("grate_ring", br + 0.025, 0.006, (x, y, H + 0.03), 20, 6, iron))
        for a in (0, 90):
            bar = b.box("grate_bar", (2 * br + 0.07, 0.012, 0.012), (x, y, H + 0.024), 0.004, 1, iron, rad(0, 0, a))
            p.add(bar)
    # Tilted control panel with four dials, tick marks and a pilot lamp.
    panel_z = H - 0.135
    panel = b.box("panel", (W - 0.03, 0.03, 0.105), (cx, -0.008, panel_z), 0.012, 2,
                  b.mat("cream" if body_role != "cream" else "main", rough=0.35), rad(-14, 0, 0))
    p.add(panel)
    for i in range(4):
        dx = x0 + 0.1 + i * 0.12
        kitchen.knob_dial(p, (dx, -0.03, panel_z + 0.055), shape, 0.9, "dark" if not shape.streamline else "main_dark")
        for k in range(5):
            a = math.radians(-60 + 30 * k)
            p.add(b.box("tick", (0.003, 0.004, 0.009), (dx + math.sin(a) * 0.037, -0.03, panel_z + 0.055 + math.cos(a) * 0.037),
                        0.0, 1, dark, rad(0, -math.degrees(a), 0)))
    p.add(b.sphere("pilot", 0.008, (x0 + W - 0.035, -0.03, panel_z + 0.055), 10, material=b.lamp(2.5, "neon")))
    # Oven door: inset, window with a chrome frame and a rack inside, hinges, bar handle and a tea towel.
    door_z, door_h = 0.25, 0.47
    p.add(b.box("oven_door", (W - 0.05, 0.03, door_h), (cx, -0.008, door_z), shape.radius * 0.45, shape.segments, body))
    win_w, win_h, win_z = 0.38, 0.2, door_z + 0.12
    p.add(b.box("oven_cavity", (win_w + 0.02, 0.2, win_h + 0.02), (cx, 0.12, win_z - 0.01), 0.01, 1, _m("#1E1A18", 0.8)))
    for k in range(5):
        p.add(b.box("rack", (0.006, 0.18, 0.006), (cx - 0.15 + k * 0.075, 0.12, win_z + 0.07), 0.0, 1, b.chrome()))
    p.add(b.box("rack_front", (win_w - 0.02, 0.006, 0.006), (cx, 0.035, win_z + 0.07), 0.0, 1, b.chrome()))
    p.add(b.box("oven_lamp", (0.03, 0.01, 0.02), (cx + 0.15, 0.2, win_z + win_h - 0.04), 0.0, 1, b.lamp(1.5)))
    p.add(b.box("oven_window", (win_w, 0.012, win_h), (cx, -0.026, win_z), 0.05 if shape.streamline else 0.07, 4,
                clear_glass("#3A3532", 0.45)))
    p.add(b.box("oven_window_frame", (win_w + 0.035, 0.008, win_h + 0.035), (cx, -0.021, win_z - 0.0175),
                0.06 if shape.streamline else 0.085, 4, b.chrome()))
    for hx in (cx - 0.2, cx + 0.2):
        p.add(b.cyl("hinge", 0.012, 0.04, (hx, -0.022, door_z + 0.005), 12, material=b.chrome(), rot=rad(0, 90, 0)))
    hz = door_z + door_h - 0.05
    kitchen.handle_bar(p, (cx, -0.024, hz), 0.42)
    # Tea towel folded over the handle: two hanging halves and a soft fold, with two stripes.
    tw = _m(towel, 0.8)
    stripe = _m("#F3E9D2", 0.8)
    tx = cx - 0.08
    p.add(b.box("towel_front", (0.16, 0.008, 0.2), (tx, -0.077, hz - 0.2), 0.004, 1, tw))
    p.add(b.box("towel_back", (0.16, 0.008, 0.14), (tx, -0.05, hz - 0.14), 0.004, 1, tw))
    p.add(b.cyl("towel_fold", 0.016, 0.16, (tx - 0.08, -0.064, hz + 0.005), 12, material=tw, rot=rad(0, 90, 0)))
    for sz in (0.035, 0.06):
        p.add(b.box("towel_stripe", (0.162, 0.009, 0.01), (tx, -0.078, hz - 0.2 + sz), 0.0, 1, stripe))
    # Storage drawer under the oven.
    p.add(b.box("drawer", (W - 0.05, 0.025, 0.13), (cx, -0.006, 0.095), shape.radius * 0.35, 3, body))
    kitchen.handle_bar(p, (cx, -0.02, 0.16), 0.22)
    if shape.streamline:
        for zb in (0.235, door_z + door_h + 0.01):
            p.add(b.box("stove_band", (W - 0.004, 0.012, 0.012), (cx, -0.0, zb), 0.005, 2, b.chrome()))
        for sx in (x0 + 0.003, x0 + W - 0.003):
            p.add(b.box("side_trim", (0.008, D - 0.06, 0.012), (sx, D / 2, H - 0.06), 0.003, 1, b.chrome()))
        outline = b.rounded_rect_outline(W - 0.02, 0.26, 0.11, 6, x0 + 0.01, H)
        p.add(b.prism("backguard", outline, 0.045, (0, D - 0.035, 0), b.mat("cream", rough=0.35), radius=0.012,
                      segments=2))
        p.add(b.cyl("clock", 0.058, 0.014, (cx, D - 0.056, H + 0.14), 32, edge=0.005, material=b.chrome(),
                    rot=rad(90)))
        p.add(b.cyl("clock_face", 0.048, 0.004, (cx, D - 0.07, H + 0.14), 32, material=b.mat("cream", rough=0.3),
                    rot=rad(90)))
        for a in (60, 200):
            hand_len = 0.035 if a == 60 else 0.025
            p.add(b.box("clock_hand", (0.004, 0.003, hand_len), (cx, -0.0745 + D, H + 0.14), 0.0, 1, dark,
                        rad(0, a, 0)))
        for lx in (x0 + 0.1, x0 + W - 0.1):
            p.add(b.sphere("guard_lamp", 0.012, (lx, D - 0.06, H + 0.14), 10, material=b.lamp(2.0, "neon")))
    else:
        p.add(b.box("backlip", (W - 0.02, 0.04, 0.07), (cx, D - 0.03, H), 0.02, 3, body))
    return p


# --- cars ------------------------------------------------------------------------------------------------------------

def _whitewall_wheel(p, x, y, r=0.31):
    side = 1 if y > 0 else -1  # outward is +side along Y
    p.add(b.torus("tyre", r - 0.09, 0.09, (x, y, r), 28, 12, _m("#2C2826", 0.8), rad(90, 0, 0)))
    p.add(b.torus("whitewall", r - 0.1, 0.035, (x, y + side * 0.07, r), 28, 8, _m("#F3E9D2", 0.6), rad(90, 0, 0)))
    p.add(b.sphere("hubcap", r * 0.5, (x, y + side * 0.05, r), 20, (1, 0.35, 1), b.chrome()))
    p.add(b.sphere("hub_nut", r * 0.12, (x, y + side * 0.09, r), 12, (1, 0.5, 1), b.mat("car", rough=0.3)))


def _seats(p, x_front, x_back, z, width=1.0):
    seat = _m("#F3E9D2", 0.7)
    for sy in (-width / 4, width / 4):
        for sx in ((x_front,) if x_back is None else (x_front, x_back)):
            p.add(b.box("seat", (0.4, 0.4, 0.12), (sx, sy, z), 0.05, 3, seat))
            p.add(b.box("seat_back", (0.11, 0.4, 0.3), (sx + 0.2, sy, z + 0.08), 0.045, 3, seat))
    p.add(b.torus("steering", 0.11, 0.013, (x_front - 0.3, -width / 4, z + 0.24), 20, 6, _m("#2C2826", 0.4),
                  rad(0, 60, 0)))


def car_streamline(loc, yaw=0.0):
    """Teardrop rocket car: bubble canopy with seats inside, fins with tail lights, ribbed grille, bezelled
    headlights, chrome spear, wheel spats, white-wall tyres. About 3.8 m."""
    p = Parts()
    body = b.mat("car", rough=0.22)
    prof = [(0, 0), (0.3, 0.06), (0.5, 0.3), (0.6, 0.8), (0.6, 1.7), (0.52, 2.7), (0.36, 3.4), (0.12, 3.78), (0, 3.8)]
    shell = b.lathe("car_body", prof, 36, (0, 0, 0), body, rad(0, 90, 0), scale=(0.62, 1.32, 1))
    for v in shell.data.vertices:
        if v.co.x > 0.3:
            v.co.x = 0.3
    shell.location = (0, 0, 0.62)
    p.add(shell)
    # Cockpit: seats and wheel under a clear canopy, chrome ring around its base.
    _seats(p, 1.55, 2.2, 0.74, 1.0)
    p.add(b.box("dash", (0.12, 0.9, 0.12), (1.05, 0, 0.95), 0.04, 3, _m("#2C2826", 0.5)))
    p.add(b.sphere("canopy", 0.62, (1.75, 0, 0.9), 32, (1.35, 0.85, 0.7), clear_glass()))
    ring = b.torus("canopy_ring", 0.62, 0.028, (1.75, 0, 0.93), 40, 8, b.chrome())
    ring.scale = (1.35, 0.85, 1)
    p.add(ring)
    for sy in (-1, 1):
        fin = [(2.8, 0.8), (3.72, 0.8), (3.8, 1.14), (3.55, 1.12)]
        p.add(b.prism("fin", fin, 0.05, (0, sy * 0.6, 0), body, radius=0.02, segments=2))
        p.add(b.capsule("taillight", 0.035, 0.22, (3.62, sy * 0.6, 1.0), 12, b.lamp(1.8, "neon"), rad(0, 90, 0)))
        p.add(b.box("spear", (2.7, 0.02, 0.035), (1.95, sy * 0.795, 0.66), 0.012, 2, b.chrome()))
        p.add(b.box("spat", (0.85, 0.06, 0.3), (2.95, sy * 0.76, 0.42), 0.14, 5, body))
        p.add(b.box("door_seam", (0.012, 0.01, 0.34), (1.15, sy * 0.808, 0.6), 0.0, 1, _m("#2C2826", 0.6)))
        p.add(b.box("door_seam", (0.012, 0.01, 0.34), (2.25, sy * 0.79, 0.6), 0.0, 1, _m("#2C2826", 0.6)))
        p.add(b.cyl("arch", 0.37, 0.02, (0.85, sy * 0.74, 0.31), 28, material=_m("#2C2826", 0.8), rot=rad(90, 0, 0)))
        hx = 0.3
        p.add(b.torus("bezel", 0.075, 0.018, (hx, sy * 0.47, 0.78), 20, 8, b.chrome(), rad(0, 90, 0)))
        p.add(b.sphere("headlight", 0.07, (hx - 0.01, sy * 0.47, 0.78), 16, (0.55, 1, 1), b.lamp(1.6)))
    # Grille: chrome oval with ribs; hood ornament.
    p.add(b.torus("grille_ring", 0.27, 0.025, (0.07, 0, 0.55), 28, 8, b.chrome(), rad(0, 90, 0)))
    for i in range(-5, 6):
        h = 0.48 * math.sqrt(max(0.0, 1 - (i / 5.6) ** 2))
        p.add(b.box("grille_rib", (0.02, 0.016, h), (0.06, i * 0.045, 0.55 - h / 2), 0.006, 2, b.chrome()))
    p.add(b.prism("ornament", [(0.0, 0.0), (0.22, 0.0), (0.02, 0.1)], 0.015, (0.42, 0, 0.98), b.chrome(), radius=0.004))
    for sx, bw in ((0.12, 0.9), (3.68, 0.9)):
        p.add(b.box("bumper", (0.12, bw, 0.1), (sx, 0, 0.3), 0.045, 4, b.chrome()))
    p.add(b.cyl("exhaust", 0.03, 0.14, (3.66, 0.3, 0.3), 12, edge=0.008, material=b.chrome(), rot=rad(0, 90, 0)))
    for wx, wy in ((0.85, 0.66), (0.85, -0.66), (2.95, 0.7), (2.95, -0.7)):
        _whitewall_wheel(p, wx, wy)
    for o in p.items:
        o.location.x -= 1.9
    return p.place("Car", loc, rad(0, 0, yaw))


def car_soft(loc, yaw=0.0, colour_role="car"):
    """Bubble car: an egg-shaped two-tone body, a glass greenhouse with seats inside under a cream roof, round
    headlights on fender bulges, wheel pods, curved chrome bumpers, white-wall tyres. About 3.45 m."""
    p = Parts()
    body = b.mat(colour_role, rough=0.22)
    cream = b.mat("cream", rough=0.3)
    prof = [(0, 0), (0.36, 0.05), (0.56, 0.25), (0.68, 0.7), (0.72, 1.4), (0.7, 2.2), (0.6, 2.9), (0.4, 3.32), (0, 3.45)]
    shell = b.lathe("car_body", prof, 36, (0, 0, 0), body, rad(0, 90, 0), scale=(0.62, 1.08, 1))
    for v in shell.data.vertices:
        if v.co.x > 0.33:
            v.co.x = 0.33
    shell.location = (0, 0, 0.6)
    p.add(shell)
    # Cream belt line around the body.
    belt = b.torus("belt", 1.0, 0.025, (1.72, 0, 0.78), 48, 6, cream)
    belt.scale = (1.66, 0.76, 1)
    p.add(belt)
    _seats(p, 1.6, None, 0.72, 0.95)
    p.add(_cap("greenhouse", 0.98, 0.66, 0.5, (1.78, 0, 0.92), -0.05, clear_glass()))
    p.add(_cap("roof", 1.0, 0.68, 0.52, (1.8, 0, 0.92), 0.7, cream))
    frame = b.torus("window_frame", 1.0, 0.022, (1.78, 0, 0.93), 48, 6, b.chrome())
    frame.scale = (0.98, 0.66, 1)
    p.add(frame)
    for sy in (-1, 1):
        for wx in (0.8, 2.7):
            p.add(b.sphere("fender", 0.46, (wx, sy * 0.6, 0.47), 24, (1.0, 0.27, 0.55), body))
        p.add(b.torus("bezel", 0.085, 0.02, (0.42, sy * 0.62, 0.72), 20, 8, b.chrome(), rad(0, 90, 0)))
        p.add(b.sphere("headlight", 0.08, (0.41, sy * 0.62, 0.72), 16, (0.5, 1, 1), b.lamp(1.6)))
        p.add(b.sphere("taillight", 0.05, (3.3, sy * 0.45, 0.78), 12, (0.5, 1, 1.3), b.lamp(1.5, "neon")))
    for x, a in ((0.08, 0), (3.38, 180)):
        bump = b.torus("bumper", 0.55, 0.035, (x + (0.5 if a == 0 else -0.5), 0, 0.36), 28, 8, b.chrome(),
                       rad(0, 0, a + 90), arc=150)
        bump.rotation_euler = rad(0, 0, 105 + a)
        p.add(bump)
    p.add(b.box("luggage_rack", (0.4, 0.7, 0.02), (3.05, 0, 1.0), 0.01, 1, b.chrome(), rad(0, -25, 0)))
    for wx, wy in ((0.8, 0.66), (0.8, -0.66), (2.7, 0.66), (2.7, -0.66)):
        _whitewall_wheel(p, wx, wy, 0.29)
    for o in p.items:
        o.location.x -= 1.72
    return p.place("Car", loc, rad(0, 0, yaw))


# --- shop front ------------------------------------------------------------------------------------------------------

def _interior(p, x0, x1, y0, depth, z0, height, theme):
    """A shallow lit room behind the shop window: back wall, floor, counter, shelves with jars, pendant lamps."""
    cx = (x0 + x1) / 2
    w = x1 - x0
    p.add(b.box("int_back", (w + 0.4, 0.05, height + 0.2), (cx, y0 + depth, 0), 0, 1, _m(theme["inside"], 0.9)))
    p.add(b.box("int_floor", (w + 0.4, depth, 0.02), (cx, y0 + depth / 2, 0.0), 0, 1, _m(theme["inside_floor"], 0.8)))
    p.add(b.box("int_counter", (w * 0.6, 0.45, 1.0), (cx + 0.1, y0 + depth * 0.55, 0), 0.04, 2, _m(theme["counter"], 0.5)))
    p.add(b.box("int_counter_top", (w * 0.62, 0.5, 0.04), (cx + 0.1, y0 + depth * 0.55, 1.0), 0.01, 1, b.chrome()))
    rng = random.Random(3)
    for k, sz in enumerate((1.45, 1.9)):
        p.add(b.box("int_shelf", (w * 0.8, 0.22, 0.03), (cx, y0 + depth - 0.12, sz), 0.0, 1, _m("#B98B5E", 0.6)))
        for j in range(9):
            col = theme["jars"][rng.randrange(len(theme["jars"]))]
            p.add(b.cyl("jar", 0.045, 0.14, (x0 + 0.2 + j * (w * 0.8 / 9) + 0.05, y0 + depth - 0.12, sz + 0.03), 12,
                        edge=0.01, material=_m(col, 0.3)))
    for lx in (cx - w * 0.25, cx + w * 0.25):
        p.add(b.cyl("int_cord", 0.005, 0.5, (lx, y0 + depth * 0.45, height - 0.5), 6, material=_m("#2C2826", 0.6)))
        p.add(b.lathe("int_shade", [(0.0, 0.18), (0.16, 0.0), (0.15, -0.01), (0.0, 0.02)], 24,
                      (lx, y0 + depth * 0.45, height - 0.68), _m(theme["accent"], 0.4)))
        p.add(b.sphere("int_bulb", 0.05, (lx, y0 + depth * 0.45, height - 0.68), 12, material=b.lamp(4.0)))


def shop_front(shape, theme, loc=(0, 0, 0), show_interior=True):
    """A one-storey shop-house front, about 6 m wide, front at y = 0 facing -Y, dressed by a theme."""
    p = Parts()
    W, D, H = 6.0, 5.0, 3.4
    wall = _m(theme["wall"], 0.85)
    trim = _m(theme["trim"], 0.45)
    accent = _m(theme["accent"], 0.45)
    door_m = _m(theme["door"], 0.4)
    if shape.streamline:
        from .street import _plan_block
        p.add(_plan_block("walls", W, D, H, 1.1, (0, D / 2, 0), wall))
        p.add(_plan_block("parapet", W + 0.1, D + 0.1, 0.3, 1.15, (0, D / 2, H), trim))
        for zb in (H - 0.38, H - 0.58):
            p.add(_plan_block("band", W + 0.04, D + 0.04, 0.06, 1.12, (0, D / 2, zb), b.chrome()))
        p.add(_plan_block("plinth", W + 0.05, D + 0.05, 0.42, 1.12, (0, D / 2, 0), trim))
    else:
        p.add(b.box("walls", (W, D, H), (0, D / 2, 0), 0.22, 5, wall))
        p.add(b.box("plinth", (W + 0.06, D + 0.06, 0.42), (0, D / 2, 0), 0.2, 4, trim))
        p.add(b.box("cornice", (W + 0.2, D + 0.2, 0.22), (0, D / 2, H - 0.05), 0.1, 4, trim))
        p.add(b.box("cornice_cap", (W + 0.3, D + 0.3, 0.1), (0, D / 2, H + 0.15), 0.05, 3, wall))
    # Shop window: frame, mullions, transom, sill, clear glass and the room behind.
    wx0, wx1, wz0, wz1 = -2.45, 0.55, 0.62, 2.35
    wcx, ww, wh = (wx0 + wx1) / 2, wx1 - wx0, wz1 - wz0
    rad_ = 0.25 if not shape.streamline else 0.06
    outline = b.rounded_rect_outline(ww + 0.16, wh + 0.16, rad_ + 0.08, 6, wx0 - 0.08, wz0 - 0.08)
    inner = b.rounded_rect_outline(ww, wh, rad_, 6, wx0, wz0)
    frame = b.prism("window_frame", outline, 0.12, (0, -0.03, 0), trim, radius=0.015, segments=2)
    p.add(frame)
    # Cutters are parented with the shop, so the holes move with it.
    hole = p.add(b.prism("window_hole", inner, 0.3, (0, -0.03, 0), None))
    mod = frame.modifiers.new("Cut", "BOOLEAN")
    mod.object = hole
    mod.operation = "DIFFERENCE"
    hole.hide_render = True
    # Cut the wall too, so the room behind is visible.
    walls = p.items[0]
    cut = p.add(b.box("wall_hole", (ww - 0.02, 0.6, wh - 0.02), (wcx, 0.05, wz0 + 0.01), 0, 1, None))
    cut.hide_render = True
    m2 = walls.modifiers.new("Cut", "BOOLEAN")
    m2.object = cut
    m2.operation = "DIFFERENCE"
    for mx in (wx0 + ww / 3, wx0 + 2 * ww / 3):
        p.add(b.box("mullion", (0.05, 0.08, wh), (mx, -0.02, wz0), 0.01, 1, trim))
    p.add(b.box("transom", (ww, 0.08, 0.05), (wcx, -0.02, wz1 - 0.42), 0.01, 1, trim))
    p.add(b.box("window_glass", (ww, 0.01, wh), (wcx, 0.0, wz0), 0.0, 1, clear_glass("#BFD9E0", 0.18)))
    p.add(b.box("sill", (ww + 0.3, 0.2, 0.06), (wcx, -0.08, wz0 - 0.14), 0.02, 2, trim))
    if show_interior:
        _interior(p, wx0, wx1, 0.15, 1.6, 0, H - 0.1, theme)
    # Door: frame, lower panels, upper glass porthole or arch, push plate, kick plate, step, number.
    dx0, dx1, dz1 = 1.05, 2.15, 2.25
    dcx, dw = (dx0 + dx1) / 2, dx1 - dx0
    p.add(b.box("door_frame", (dw + 0.18, 0.1, dz1 + 0.09), (dcx, -0.02, 0.0), 0.03, 2, trim))
    p.add(b.box("door", (dw, 0.06, dz1), (dcx, -0.04, 0.0), 0.02, 2, door_m))
    for pz in (0.18, 0.66):
        p.add(b.box("door_panel", (dw - 0.3, 0.02, 0.36), (dcx, -0.075, pz), 0.04, 3, _m(theme["door"], 0.5)))
    if shape.streamline:
        p.add(b.cyl("door_port", 0.2, 0.03, (dcx, -0.07, 1.6), 28, material=clear_glass("#BFD9E0", 0.4), rot=rad(90)))
        p.add(b.torus("door_port_ring", 0.21, 0.028, (dcx, -0.085, 1.6), 28, 8, b.chrome(), rad(90)))
    else:
        arch = [(dcx - 0.3, 1.25), (dcx + 0.3, 1.25)] + [
            (dcx + 0.3 * math.cos(math.radians(a)), 1.85 + 0.3 * math.sin(math.radians(a))) for a in range(0, 181, 15)]
        arch = [arch[0], arch[1]] + arch[2:]
        p.add(b.prism("door_glass", arch, 0.03, (0, -0.072, 0), clear_glass("#BFD9E0", 0.45)))
    p.add(b.box("push_plate", (0.08, 0.02, 0.38), (dx0 + 0.14, -0.085, 0.95), 0.02, 2, b.chrome()))
    p.add(b.box("kick_plate", (dw - 0.1, 0.015, 0.14), (dcx, -0.078, 0.02), 0.005, 1, b.chrome()))
    p.add(b.box("step", (dw + 0.5, 0.45, 0.12), (dcx, -0.25, 0.0), 0.03, 2, _m("#D8CDB8", 0.85)))
    p.add(b.box("number_plate", (0.2, 0.02, 0.2), (dx1 + 0.3, -0.03, 1.75), 0.04, 3, _m("#F3E9D2", 0.4)))
    p.add(b.text("number", theme.get("number", "12"), 0.11, (dx1 + 0.3, -0.045, 1.85), rad(90), _m("#2C2826", 0.4), 0.004))
    # Wall lamps beside the door.
    for lx in (dx0 - 0.3, dx1 + 0.3):
        p.add(b.box("lamp_bracket", (0.06, 0.18, 0.06), (lx, -0.09, 2.15), 0.02, 2, b.chrome()))
        p.add(b.sphere("wall_lamp", 0.1, (lx, -0.2, 2.1), 16, material=b.lamp(2.5)))
    # Sign: backplate, extruded letters and a neon border.
    sw, sh_, sz = 3.6, 0.55, H - 0.68
    scx = 0.0
    so = b.rounded_rect_outline(sw, sh_, sh_ / 2 - 0.02 if not shape.streamline else 0.1, 6, scx - sw / 2, sz)
    p.add(b.prism("sign", so, 0.1, (0, -0.08, 0), _m(theme["sign"], 0.4), radius=0.015, segments=2))
    border = b.rounded_rect_outline(sw - 0.12, sh_ - 0.12, sh_ / 2 - 0.08 if not shape.streamline else 0.06, 6,
                                    scx - sw / 2 + 0.06, sz + 0.06)
    for i, (a, c) in enumerate(zip(border, border[1:] + border[:1])):
        mid = ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2)
        length = math.dist(a, c)
        ang = math.degrees(math.atan2(c[1] - a[1], c[0] - a[0]))
        p.add(b.cyl("neon", 0.012, length + 0.01, (mid[0] - length / 2, -0.14, mid[1]), 6,
                    material=b.lamp(2.2, "neon"), rot=rad(0, -ang + 90, 0)))
        p.items[-1].location = (mid[0] - math.cos(math.radians(ang)) * (length / 2 + 0.005), -0.14,
                                mid[1] - math.sin(math.radians(ang)) * (length / 2 + 0.005))
        p.items[-1].rotation_euler = rad(0, 90 - ang, 0)
    p.add(b.text("sign_text", theme["name"], sh_ * 0.4, (scx, -0.15, sz + sh_ / 2), rad(90),
                 _m(theme["letters"], 0.35, emit=0.6 * b._glow), 0.03))
    # Awning or canopy over window and door.
    if shape.streamline:
        canopy = [(-2.75, 2.5), (2.55, 2.5), (2.6, 2.62), (-2.45, 2.7), (-2.85, 2.6)]
        p.add(b.prism("canopy", canopy, 1.15, (0, -0.58, 0), accent, radius=0.03, segments=2))
        for i in range(11):
            p.add(b.box("canopy_rib", (0.04, 1.15, 0.04), (-2.5 + i * 0.5, -0.58, 2.46), 0.01, 1, b.chrome()))
        p.add(b.box("canopy_light", (5.0, 0.04, 0.02), (-0.1, -1.1, 2.47), 0.0, 1, b.lamp(1.5)))
    else:
        stripes = theme["stripes"]
        n = 18
        for i in range(n):
            x = wx0 - 0.15 + i * 0.2 + 0.1
            m = _m(stripes[i % len(stripes)], 0.7)
            seg = b.box("awning", (0.2, 1.0, 0.04), (x, -0.45, 2.42), 0.0, 1, m, rad(-22, 0, 0))
            p.add(seg)
            half = [(0.1 * math.cos(math.radians(a)), -0.1 * math.sin(math.radians(a))) for a in range(0, 181, 20)]
            p.add(b.prism("scallop", half, 0.025, (x, -0.92, 2.23), m, rad(-22, 0, 0)))
        for ax in (wx0 - 0.1, wx0 + 3.45):
            p.add(b.capsule("awning_arm", 0.012, 1.05, (ax, 0.0, 2.02), 8, b.chrome(), rad(-68, 0, 0)))
    # Window boxes with flowers under the window, drainpipe at the corner.
    rng = random.Random(7)
    p.add(b.box("window_box", (ww + 0.1, 0.24, 0.2), (wcx, -0.22, wz0 - 0.38), 0.03, 2, accent))
    for i in range(16):
        fx = wx0 + 0.1 + i * (ww / 16)
        p.add(b.sphere("leafball", 0.07, (fx, -0.22 + rng.uniform(-0.05, 0.05), wz0 - 0.14), 10,
                       (1, 1, 0.8), _m("#5E8F4F", 0.7)))
        if i % 2 == 0:
            p.add(b.sphere("flower", 0.035, (fx + 0.03, -0.27, wz0 - 0.08), 10,
                           material=_m(theme["flowers"][i // 2 % len(theme["flowers"])], 0.5)))
    p.add(b.cyl("drainpipe", 0.045, H + 0.2, (W / 2 - 0.25, -0.06, 0.0), 12, material=trim))
    theme_props = theme.get("props")
    if theme_props:
        theme_props(p, shape)
    return p.place("Shop", loc)


def menu_board(p, loc, theme):
    """A-frame chalkboard on the pavement with a few chalk lines."""
    x, y, z = loc
    wood = _m("#9A6542", 0.6)
    for a in (-12, 12):
        side = b.box("board_frame", (0.6, 0.04, 0.95), (x, y + (0.1 if a > 0 else -0.1), z), 0.02, 2, wood,
                     rad(a, 0, 0))
        p.add(side)
    p.add(b.box("board", (0.5, 0.01, 0.75), (x, y - 0.125, z + 0.12), 0.0, 1, _m("#2F3A35", 0.8), rad(-12, 0, 0)))
    for i, lw in enumerate((0.36, 0.28, 0.32, 0.2, 0.3)):
        p.add(b.box("chalk", (lw, 0.006, 0.018), (x - 0.02, y - 0.135 - i * 0.025, z + 0.75 - i * 0.12), 0.0, 1,
                    _m("#F3E9D2", 0.9), rad(-12, 0, 0)))
