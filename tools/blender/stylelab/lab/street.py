"""Street test models for the style lab (spec 046): small town car, shop-house, street pieces.

Axes: X right, Y away from the camera, Z up. The house front faces -Y. Sizes in metres.
"""

import math

import bmesh

from . import build as b
from .build import Parts, rad

SIGN_TEXT = "TOMORROW KITCHEN"


# --- car --------------------------------------------------------------------------------------------------------------

def _wheel(p, x, y, shape, r=0.3):
    side = 1 if y > 0 else -1
    p.add(b.torus("tyre", r - 0.085, 0.085, (x, y, r), 24, 10, b.mat("rubber", rough=0.8), rad(90, 0, 0)))
    hub = b.sphere("hub", r * 0.62, (x, y - side * 0.03, r), 20, (1, 0.3, 1), b.chrome())
    p.add(hub)
    if not shape.streamline:
        p.add(b.cyl("hub_ring", r * 0.5, 0.01, (x, y - side * 0.075, r), 20, material=b.mat("cream", rough=0.4),
                    rot=rad(90 * side, 0, 0)))


def car(shape, loc, yaw=0.0):
    """About 3.8 m long, 1.6 m wide, 1.45 m high. Front is -X."""
    p = Parts()
    body = b.mat("car", rough=0.3)
    cream = b.mat("cream", rough=0.35)
    if shape.streamline:
        # Teardrop body: lathe along X, flat underside, bubble canopy, rear fins, ribbed oval grille.
        prof = [(0, 0), (0.3, 0.06), (0.5, 0.3), (0.6, 0.8), (0.6, 1.7), (0.52, 2.7), (0.36, 3.4), (0.12, 3.78),
                (0, 3.8)]
        shell = b.lathe("car_body", prof, 28, (0, 0, 0), body, rad(0, 90, 0), scale=(0.62, 1.32, 1))
        for v in shell.data.vertices:
            if v.co.x > 0.3:
                v.co.x = 0.3
        shell.location = (0, 0, 0.62)
        p.add(shell)
        p.add(b.sphere("canopy", 0.62, (1.7, 0, 0.88), 24, (1.25, 0.82, 0.68), b.glass()))
        p.add(b.torus("canopy_ring", 0.62, 0.025, (1.7, 0, 0.9), 32, 6, b.chrome(), None))
        p.items[-1].scale = (1.25, 0.82, 1)
        for sy in (-1, 1):
            fin = [(2.8, 0.8), (3.72, 0.8), (3.8, 1.12), (3.55, 1.1)]
            p.add(b.prism("fin", fin, 0.05, (0, sy * 0.62, 0), body, radius=0.02, segments=2))
            p.add(b.box("band", (2.9, 0.02, 0.05), (1.85, sy * 0.79, 0.62), 0.01, 2, b.chrome()))
            p.add(b.box("skirt", (0.85, 0.06, 0.3), (2.95, sy * 0.76, 0.4), 0.14, 5, body))
        # Grille: chrome oval with vertical ribs (reference picture 1).
        p.add(b.torus("grille_ring", 0.27, 0.025, (0.07, 0, 0.55), 24, 8, b.chrome(), rad(0, 90, 0)))
        for i in range(-5, 6):
            h = 0.48 * math.sqrt(max(0.0, 1 - (i / 5.6) ** 2))
            p.add(b.box("grille_rib", (0.02, 0.016, h), (0.06, i * 0.045, 0.55 - h / 2), 0.006, 2, b.chrome()))
        for sy in (-1, 1):
            p.add(b.sphere("headlight", 0.075, (0.28, sy * 0.48, 0.78), 16, (0.6, 1, 1), b.lamp(2.0)))
            p.add(b.sphere("taillight", 0.05, (3.68, sy * 0.3, 0.86), 12, (0.6, 1, 1.4), b.lamp(1.5, "neon")))
        wheels = ((0.85, 0.66), (0.85, -0.66), (2.95, 0.7), (2.95, -0.7))
    else:
        # Bubble car: chunky rounded body, glass cabin, two-tone roof.
        p.add(b.box("car_body", (3.6, 1.56, 0.66), (1.9, 0, 0.24), 0.32, 6, body))
        p.add(b.box("cabin_glass", (1.95, 1.34, 0.5), (2.0, 0, 0.8), 0.3, 6, b.glass()))
        p.add(b.box("roof", (2.05, 1.44, 0.2), (2.0, 0, 1.18), 0.1, 5, cream))
        p.add(b.box("cabin_floor", (1.7, 1.2, 0.3), (2.0, 0, 0.75), 0.1, 3, b.mat("dark", rough=0.6)))
        p.add(b.box("belt", (3.62, 1.58, 0.06), (1.9, 0, 0.6), 0.03, 3, cream))
        for sy in (-1, 1):
            p.add(b.torus("headlight_ring", 0.1, 0.022, (0.12, sy * 0.52, 0.62), 20, 8, b.chrome(), rad(0, 90, 0)))
            p.add(b.sphere("headlight", 0.09, (0.13, sy * 0.52, 0.62), 16, (0.5, 1, 1), b.lamp(2.0)))
            p.add(b.sphere("taillight", 0.06, (3.69, sy * 0.55, 0.66), 12, (0.5, 1, 1), b.lamp(1.5, "neon")))
        wheels = ((0.85, 0.7), (0.85, -0.7), (2.95, 0.7), (2.95, -0.7))
    bw = 0.9 if shape.streamline else 1.5
    for sx in ((0.12, 3.68) if shape.streamline else (0.04, 3.76)):
        p.add(b.box("bumper", (0.12, bw, 0.1), (sx, 0, 0.3), 0.045, 4, b.chrome()))
    for wx, wy in wheels:
        _wheel(p, wx, wy, shape)
    # Pivot: centre of the car.
    for o in p.items:
        o.location.x -= 1.9
    return p.place("Car", loc, rad(0, 0, yaw))


# --- shop-house -------------------------------------------------------------------------------------------------------

def _plan_block(name, w, d, h, r, loc, material, steps=6):
    """A block with rounded corners seen from above (plan), w along X, d along Y, height h, bottom at loc.z."""
    outline = b.rounded_rect_outline(w, d, r, steps, -w / 2, -d / 2)
    obj = b.prism(name, outline, h, (0, 0, 0), material, radius=0.0)
    # The prism extrudes along Y; turn it so the outline lies in the ground plane and the height is Z.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for v in bm.verts:
        x, y, z = v.co
        v.co = (x, z, y + h / 2)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.polygons.foreach_set("use_smooth", [False] * len(obj.data.polygons))
    b.bevel(obj, 0.03, 2)
    obj.location = loc
    return obj


def _sign(p, cx, z, y, w, h, streamline):
    frame = b.mat("trim", rough=0.4)
    outline = b.rounded_rect_outline(w, h, h / 2 - 0.01 if not streamline else 0.08, 6, cx - w / 2, z)
    p.add(b.prism("sign", outline, 0.12, (0, y, 0), frame, radius=0.015, segments=2))
    p.add(b.text("sign_text", SIGN_TEXT, h * 0.42, (cx, y - 0.07, z + h / 2), rad(90), b.lamp(1.5, "cream"), 0.02))


def house(shape, loc):
    """A one-storey shop-house, about 6 m wide, 5 m deep, front at y = 0 facing -Y."""
    p = Parts()
    W, D, H = 6.0, 5.0, 3.3
    wall = b.mat("wall_out", rough=0.85)
    trim = b.mat("trim", rough=0.5)
    main = b.mat("main", rough=0.45)
    glass = b.window()
    if shape.streamline:
        # Streamline moderne: rounded corners in plan, horizontal bands, a curved canopy, a ribbed sign tower.
        p.add(_plan_block("walls", W, D, H, 1.1, (0, D / 2, 0), wall))
        p.add(_plan_block("parapet", W + 0.1, D + 0.1, 0.3, 1.15, (0, D / 2, H), trim))
        for zb in (H - 0.35, H - 0.55):
            p.add(_plan_block("band", W + 0.04, D + 0.04, 0.06, 1.12, (0, D / 2, zb), b.chrome()))
        # Shop window and door.
        p.add(b.box("window_frame", (2.7, 0.08, 1.75), (-0.9, -0.03, 0.55), 0.04, 2, trim))
        p.add(b.box("window", (2.5, 0.06, 1.55), (-0.9, -0.06, 0.65), 0.02, 2, glass))
        p.add(b.box("door", (1.0, 0.08, 2.15), (1.55, -0.03, 0.0), 0.03, 2, main))
        p.add(b.cyl("door_port", 0.18, 0.04, (1.55, -0.07, 1.45), 24, edge=0.01, material=glass, rot=rad(90)))
        p.add(b.torus("door_port_ring", 0.19, 0.025, (1.55, -0.08, 1.45), 24, 8, b.chrome(), rad(90)))
        p.add(b.box("door_handle", (0.04, 0.05, 0.5), (1.2, -0.1, 0.8), 0.015, 2, b.chrome()))
        # Curved canopy over the window and door.
        canopy = [(-2.5, 2.35), (2.3, 2.35), (2.35, 2.47), (-2.2, 2.55), (-2.6, 2.45)]
        c = b.prism("canopy", canopy, 1.1, (0, -0.55, 0), b.mat("accent2", rough=0.4), radius=0.03, segments=2)
        p.add(c)
        for i in range(10):
            p.add(b.box("canopy_rib", (0.04, 1.1, 0.04), (-2.3 + i * 0.5, -0.55, 2.31), 0.01, 1, b.chrome()))
        # Ribbed sign tower on the left corner, stepped.
        tx = -1.9
        for k, (tw, th) in enumerate(((1.1, 1.3), (0.8, 0.9), (0.5, 0.6))):
            z0 = H + 0.3 + sum(t for _, t in ((1.1, 1.3), (0.8, 0.9), (0.5, 0.6))[:k])
            p.add(b.box("tower", (tw, 0.7, th), (tx, 0.5, z0), 0.06, 3, trim))
            for i in range(int(tw / 0.12)):
                p.add(b.box("tower_rib", (0.04, 0.05, th - 0.1), (tx - tw / 2 + 0.08 + i * 0.12, 0.14, z0 + 0.05), 0.012, 2,
                            b.mat("cream", rough=0.4)))
        p.add(b.sphere("tower_ball", 0.16, (tx, 0.5, H + 0.3 + 2.8 + 0.16), 16, material=b.lamp(2.0, "neon")))
        _sign(p, 0.6, H - 0.15 - 0.62 + 0.62, -0.2, 3.6, 0.55, True)
    else:
        # Soft: rounded block, gable roof with soft edges, pill windows, striped scalloped awning.
        p.add(b.box("walls", (W, D, H), (0, D / 2, 0), 0.22, 5, wall))
        roof = [(-W / 2 - 0.3, 0), (W / 2 + 0.3, 0), (0, 1.5)]
        r = b.prism("roof", roof, D + 0.4, (0, D / 2, H - 0.05), b.mat("roof", rough=0.6), radius=0.08, segments=3)
        p.add(r)
        p.add(b.prism("gable", [(-W / 2 + 0.05, 0), (W / 2 - 0.05, 0), (0, 1.35)], 0.08, (0, -0.02, H - 0.05), wall,
                      radius=0.02, segments=2))
        p.add(b.cyl("gable_window", 0.28, 0.06, (0, -0.06, H + 0.5), 24, edge=0.02, material=b.lamp(0.6), rot=rad(90)))
        p.add(b.torus("gable_ring", 0.3, 0.04, (0, -0.08, H + 0.5), 24, 8, trim, rad(90)))
        outline = b.rounded_rect_outline(2.8, 1.8, 0.35, 6, -2.3, 0.5)
        p.add(b.prism("window_frame", outline, 0.12, (0, -0.02, 0), trim, radius=0.02, segments=2))
        inner = b.rounded_rect_outline(2.56, 1.56, 0.25, 6, -2.18, 0.62)
        p.add(b.prism("window", inner, 0.04, (0, -0.07, 0), glass))
        door = [(1.0, 0.0), (2.0, 0.0), (2.0, 1.7)] + [
            (1.5 + 0.5 * math.cos(math.radians(a)), 1.7 + 0.5 * math.sin(math.radians(a))) for a in range(15, 180, 15)
        ] + [(1.0, 1.7)]
        p.add(b.prism("door", door, 0.1, (0, -0.02, 0), main, radius=0.02, segments=2))
        p.add(b.cyl("door_port", 0.15, 0.04, (1.5, -0.08, 1.55), 24, edge=0.01, material=glass, rot=rad(90)))
        p.add(b.sphere("door_knob", 0.045, (1.85, -0.1, 1.0), 16, material=b.chrome()))
        # Awning: stripes and a scalloped edge.
        stripes = b.mat("stripe_a", rough=0.6, hex_override=_stripe(0))
        stripes_b = b.mat("stripe_b", rough=0.6, hex_override=_stripe(1))
        n = 10
        for i in range(n):
            x = -2.4 + i * 0.3 + 0.15
            m = stripes if i % 2 == 0 else stripes_b
            seg = b.box("awning", (0.3, 0.95, 0.05), (x, -0.42, 2.45), 0.0, 1, m)
            seg.rotation_euler = rad(-22, 0, 0)
            p.add(seg)
            half = [(0.15 * math.cos(math.radians(a)), -0.15 * math.sin(math.radians(a))) for a in range(0, 181, 20)]
            p.add(b.prism("scallop", half, 0.03, (x, -0.86, 2.28), m, rad(-22, 0, 0)))
        _sign(p, 0.6, H - 0.7, -0.08, 3.2, 0.5, False)
    # Planters by the door.
    from . import kitchen
    for px in (2.55, -2.75):
        p.add(b.box("planter", (0.5, 0.5, 0.45), (px, -0.4, 0), shape.small_radius * 4, 3, main))
        kitchen.plant(p, (px, -0.4, 0.42), seed=int(px * 10) + 20, scale=2.6)
    return p.place("House", loc)


def _stripe(i):
    from . import palettes
    return palettes.PALETTES[b.palette_id()]["stripe"][i]


# --- street pieces ----------------------------------------------------------------------------------------------------

def street_lamp(shape, loc):
    p = Parts()
    x, y, z = loc
    pole = b.mat("dark", rough=0.4)
    p.add(b.cyl("lamp_base", 0.14, 0.3, (x, y, z), 16, edge=0.04, material=pole))
    p.add(b.cyl("lamp_pole", 0.05, 3.4, (x, y, z), 12, material=pole))
    if shape.streamline:
        p.add(b.lathe("lamp_hood", [(0.0, 0.25), (0.32, 0.0), (0.3, -0.04), (0.0, -0.02)], 24, (x, y, z + 3.5), b.chrome()))
        p.add(b.sphere("lamp_glow", 0.16, (x, y, z + 3.42), 16, (1, 1, 0.5), b.lamp(3.0)))
    else:
        p.add(b.sphere("lamp_globe", 0.24, (x, y, z + 3.62), 20, material=b.lamp(3.0)))
        p.add(b.cyl("lamp_cap", 0.1, 0.06, (x, y, z + 3.84), 16, edge=0.02, material=pole))
    return p


def tree(shape, loc, seed=1):
    p = Parts()
    x, y, z = loc
    p.add(b.cyl("trunk", 0.12, 2.0, (x, y, z), 10, material=b.mat("wood", rough=0.8)))
    if shape.streamline:
        p.add(b.sphere("crown", 1.0, (x, y, z + 2.9), 16, (0.8, 0.8, 1.45), b.mat("plant", rough=0.7)))
    else:
        p.add(b.sphere("crown", 1.1, (x, y, z + 2.8), 16, (1, 1, 0.95), b.mat("plant", rough=0.7)))
        p.add(b.sphere("crown2", 0.7, (x + 0.6, y - 0.3, z + 2.4), 14, material=b.mat("plant_dark", rough=0.7)))
    return p


def ground(p, x0, x1, pavement_y0=-3.2):
    """Pavement from the house front to the kerb, a road, and a green verge behind the road."""
    cx = (x0 + x1) / 2
    p.add(b.box("pavement", (x1 - x0, 3.2 + 8.0, 0.15), (cx, pavement_y0 + (3.2 + 8.0) / 2, -0.15),
                0.0, 1, b.mat("pavement", rough=0.85)))
    p.add(b.box("kerb", (x1 - x0, 0.2, 0.15), (cx, pavement_y0 - 0.1, -0.15), 0.03, 2, b.mat("cream", rough=0.8)))
    p.add(b.box("road", (x1 - x0, 7.0, 0.05), (cx, pavement_y0 - 3.7, -0.17), 0.0, 1, b.mat("road", rough=0.9)))
    for i in range(int((x1 - x0) / 3)):
        p.add(b.box("road_line", (1.4, 0.12, 0.01), (x0 + 1.5 + i * 3, pavement_y0 - 3.7, -0.12), 0.0, 1,
                    b.mat("cream", rough=0.8)))
    p.add(b.box("verge", (x1 - x0, 40, 0.04), (cx, pavement_y0 - 7.2 - 20, -0.16), 0.0, 1,
                b.mat("ground", rough=0.95)))
    p.add(b.box("back_lawn", (x1 - x0, 40, 0.04), (cx, pavement_y0 + 11.2 + 20, -0.16), 0.0, 1,
                b.mat("ground", rough=0.95)))
