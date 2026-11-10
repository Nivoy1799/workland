"""Kitchen test models for the style lab (spec 046): kitchen corner, stove, kitchenware, knob and handle sheet.

Axes: X right, Y away from the camera, Z up. Fronts face -Y. Sizes in metres (art guide 2).
"""

import math
import random

from . import build as b
from .build import Parts, rad

WORKTOP_H = 0.90
DEPTH = 0.60


# --- knobs and handles -----------------------------------------------------------------------------------------------

def knob_round(p, loc, s=1.0):
    """K1: small round chrome knob (cabinet doors)."""
    x, y, z = loc
    p.add(b.cyl("k1_stem", 0.006 * s, 0.012 * s, (x, y, z), 12, material=b.chrome(), rot=rad(90)))
    p.add(b.sphere("k1_ball", 0.013 * s, (x, y - 0.018 * s, z), 16, (1, 0.75, 1), b.chrome()))


def knob_dial(p, loc, shape, s=1.0, role="cream"):
    """K2: big ribbed dial with a pointer (stove)."""
    x, y, z = loc
    r = 0.028 * s
    p.add(b.cyl("k2_base", r * 1.25, 0.004, (x, y, z), 32, material=b.chrome(), rot=rad(90)))
    body = b.cyl("k2_body", r, 0.026 * s, (x, y - 0.004, z), 32, edge=r * 0.35, segments=3,
                 material=b.mat(role, rough=0.4), rot=rad(90))
    p.add(body)
    ribs = 14 if shape.streamline else 10
    for i in range(ribs):
        a = 2 * math.pi * i / ribs
        p.add(b.box("k2_rib", (0.006 * s, 0.02 * s, 0.006 * s),
                    (x + math.cos(a) * r, y - 0.006 - 0.01 * s, z + math.sin(a) * r - 0.003 * s), 0.002, 2,
                    b.mat(role, rough=0.4)))
    p.add(b.box("k2_pointer", (0.006 * s, 0.004, r * 0.9), (x, y - 0.031 * s, z), 0.002, 2, b.mat("dark")))


def handle_d(p, loc, shape, width=0.12):
    """K3: D-handle (a half ring)."""
    x, y, z = loc
    minor = 0.008 if shape.streamline else 0.011
    p.add(b.torus("k3_d", width / 2, minor, (x, y, z), 20, 10, b.chrome(), rad(90, 0, 0), arc=180))


def handle_bar(p, loc, length=0.3, vertical=False):
    """K4: long bar handle on two posts."""
    x, y, z = loc
    rot = rad(0, 0, 0) if vertical else rad(0, 90, 0)
    if vertical:
        p.add(b.capsule("k4_bar", 0.011, length, (x, y - 0.04, z - length / 2), 12, b.chrome(), rot))
        for dz in (-length * 0.38, length * 0.38):
            p.add(b.cyl("k4_post", 0.007, 0.04, (x, y, z + dz), 12, material=b.chrome(), rot=rad(90)))
    else:
        p.add(b.capsule("k4_bar", 0.011, length, (x - length / 2, y - 0.04, z), 12, b.chrome(), rot))
        for dx in (-length * 0.38, length * 0.38):
            p.add(b.cyl("k4_post", 0.007, 0.04, (x + dx, y, z), 12, material=b.chrome(), rot=rad(90)))


def handle_lever(p, loc, s=1.0):
    """K5: chrome lever with a ball end."""
    x, y, z = loc
    p.add(b.cyl("k5_hub", 0.02 * s, 0.02 * s, (x, y, z), 20, edge=0.005 * s, material=b.chrome(), rot=rad(90)))
    p.add(b.capsule("k5_arm", 0.008 * s, 0.11 * s, (x, y - 0.025 * s, z), 12, b.chrome(), rad(0, 90, 0), taper=0.8))
    p.add(b.sphere("k5_ball", 0.016 * s, (x + 0.11 * s, y - 0.025 * s, z), 16, material=b.mat("dark", rough=0.3)))


# --- kitchenware ------------------------------------------------------------------------------------------------------

def pot(shape, loc):
    """A cooking pot with lid and side handles, about 0.22 m across and 0.16 m high with lid."""
    p = Parts()
    r = 0.11
    if shape.streamline:
        body_mat = b.chrome()
        profile = [(0, 0), (r * 0.8, 0), (r * 0.97, 0.012), (r, 0.04), (r, 0.11), (r * 1.03, 0.115), (r * 1.03, 0.12)]
    else:
        body_mat = b.mat("accent", rough=0.3)
        profile = [(0, 0), (r * 0.82, 0), (r * 0.98, 0.02), (r * 1.02, 0.06), (r, 0.11), (r * 1.04, 0.118), (r * 1.04, 0.124)]
    p.add(b.lathe("pot_body", profile, shape.round_verts, (0, 0, 0), body_mat))
    lid_h = 0.122 if shape.streamline else 0.126
    lid_profile = [(r * 1.02, 0), (r * 0.9, 0.015), (r * 0.5, 0.03), (0, 0.035)]
    lid_mat = b.mat("cream", rough=0.35) if not shape.streamline else b.chrome()
    p.add(b.lathe("pot_lid", lid_profile, shape.round_verts, (0, 0, lid_h), lid_mat))
    knob_mat = b.mat("dark", rough=0.3)
    if shape.streamline:
        p.add(b.sphere("pot_knob", 0.016, (0, 0, lid_h + 0.045), 16, (1, 1, 0.7), knob_mat))
        p.add(b.cyl("pot_band", r * 1.005, 0.012, (0, 0, 0.06), shape.round_verts, material=b.mat("accent2", rough=0.4)))
        for sx in (-1, 1):
            p.add(b.capsule("pot_handle", 0.012, 0.07, (sx * (r + 0.005), 0, 0.085), 12, knob_mat,
                            rad(0, sx * 90, 0), taper=0.5))
    else:
        p.add(b.sphere("pot_knob", 0.02, (0, 0, lid_h + 0.045), 16, (1, 1, 0.8), knob_mat))
        for sx in (-1, 1):
            p.add(b.torus("pot_handle", 0.022, 0.009, (sx * (r + 0.004), 0, 0.09), 16, 8, knob_mat,
                          rad(90, 0, -90 if sx > 0 else 90), arc=180))
    return p.place("Pot", loc)


def pan(shape, loc, yaw=0.0):
    """A frying pan, 0.26 m across, with a long handle along +X."""
    p = Parts()
    r = 0.13
    body = b.mat("dark", rough=0.45) if not shape.streamline else b.mat("main_dark", rough=0.35)
    p.add(b.lathe("pan_body", [(0, 0), (r * 0.82, 0), (r * 0.95, 0.01), (r, 0.045), (r * 0.985, 0.05)],
                  shape.round_verts, (0, 0, 0), body))
    p.add(b.cyl("pan_inner", r * 0.84, 0.004, (0, 0, 0.004), shape.round_verts, material=b.mat("rubber", rough=0.6)))
    if shape.streamline:
        p.add(b.capsule("pan_handle", 0.013, 0.22, (r - 0.01, 0, 0.042), 12, b.chrome(), rad(0, 80, 0), taper=0.6))
    else:
        p.add(b.capsule("pan_handle", 0.016, 0.21, (r - 0.01, 0, 0.04), 12, b.mat("wood", rough=0.6),
                        rad(0, 82, 0), taper=0.8))
    return p.place("Pan", loc, rad(0, 0, yaw))


# --- stove and kitchen corner -----------------------------------------------------------------------------------------

def stove(shape, x0):
    """A 0.6 m cooker: hob, control strip with four dials, oven door with window and bar handle."""
    p = Parts()
    w = 0.60
    body_role = "accent2" if not shape.streamline else "cream"
    rad_ = shape.radius * 0.6
    p.add(b.box("stove_body", (w - 0.01, DEPTH - 0.02, WORKTOP_H - 0.02), (x0 + w / 2, DEPTH / 2, 0.0), rad_,
                shape.segments, b.mat(body_role, rough=0.35)))
    p.add(b.box("stove_top", (w - 0.02, DEPTH - 0.04, 0.02), (x0 + w / 2, DEPTH / 2, WORKTOP_H - 0.02), 0.01, 2,
                b.mat("dark", rough=0.5)))
    for bx, by in ((0.15, 0.17), (0.45, 0.17), (0.15, 0.43), (0.45, 0.43)):
        p.add(b.torus("burner", 0.06, 0.008, (x0 + bx, by, WORKTOP_H + 0.004), 20, 6, b.mat("rubber", rough=0.5)))
        p.add(b.cyl("burner_cap", 0.03, 0.01, (x0 + bx, by, WORKTOP_H), 16, material=b.mat("rubber", rough=0.5)))
    # Control strip.
    strip_z = WORKTOP_H - 0.12
    p.add(b.box("stove_strip", (w - 0.04, 0.02, 0.09), (x0 + w / 2, -0.005, strip_z - 0.03), 0.01, 2,
                b.mat("cream" if body_role != "cream" else "main", rough=0.4)))
    for i in range(4):
        knob_dial(p, (x0 + 0.105 + i * 0.13, -0.016, strip_z + 0.015), shape, 1.0,
                  "dark" if not shape.streamline else "main_dark")
    # Oven door.
    door_h, door_z = 0.5, 0.12
    p.add(b.box("oven_door", (w - 0.06, 0.03, door_h), (x0 + w / 2, -0.01, door_z), shape.radius * 0.5,
                shape.segments, b.mat(body_role, rough=0.35)))
    win_w, win_h = 0.36, 0.2
    if shape.streamline:
        # Rounded window with chrome frame and ribbed lower panel.
        p.add(b.box("oven_window", (win_w, 0.012, win_h), (x0 + w / 2, -0.026, door_z + 0.2), 0.05, 4,
                    b.mat("dark", rough=0.15)))
        p.add(b.box("oven_window_frame", (win_w + 0.03, 0.008, win_h + 0.03), (x0 + w / 2, -0.023, door_z + 0.185),
                    0.06, 4, b.chrome()))
        for i in range(7):
            p.add(b.box("oven_rib", (0.012, 0.012, 0.11), (x0 + 0.16 + i * 0.047, -0.026, door_z + 0.04), 0.005, 2,
                        b.chrome()))
        for zb in (0.08, door_z + door_h + 0.08):
            p.add(b.box("stove_band", (w - 0.008, 0.012, 0.014), (x0 + w / 2, -0.0, zb), 0.005, 2, b.chrome()))
    else:
        p.add(b.box("oven_window", (win_w, 0.012, win_h), (x0 + w / 2, -0.026, door_z + 0.18), 0.08, 5,
                    b.mat("dark", rough=0.15)))
    handle_bar(p, (x0 + w / 2, -0.02, door_z + door_h - 0.06), 0.4)
    # Plinth.
    p.add(b.box("stove_plinth", (w - 0.06, DEPTH - 0.1, 0.07), (x0 + w / 2, DEPTH / 2, 0.0), 0.0, 1,
                b.mat("dark", rough=0.7)))
    if shape.streamline:
        # 1950s backguard with rounded top.
        outline = b.rounded_rect_outline(w - 0.02, 0.24, 0.1, 5, x0 + 0.01, WORKTOP_H)
        p.add(b.prism("stove_backguard", outline, 0.04, (0, DEPTH - 0.03, 0), b.mat("cream", rough=0.35),
                      radius=0.01, segments=2))
        p.add(b.cyl("stove_clock", 0.055, 0.012, (x0 + w / 2, DEPTH - 0.05, WORKTOP_H + 0.13), 32, edge=0.005,
                    material=b.chrome(), rot=rad(90)))
        p.add(b.cyl("stove_clock_face", 0.045, 0.004, (x0 + w / 2, DEPTH - 0.061, WORKTOP_H + 0.13), 32,
                    material=b.mat("cream", rough=0.3), rot=rad(90)))
    return p


def base_cabinet(shape, p, x0, w, door_role="accent"):
    body = b.mat("main", rough=0.5)
    p.add(b.box("cab_body", (w - 0.006, DEPTH - 0.02, WORKTOP_H - 0.05), (x0 + w / 2, DEPTH / 2, 0.0),
                shape.radius * 0.3, shape.segments, body))
    if shape.streamline:
        p.add(b.box("cab_door", (w - 0.04, 0.02, WORKTOP_H - 0.2), (x0 + w / 2, -0.005, 0.1), 0.02, 3,
                    b.mat(door_role, rough=0.45)))
        handle_bar(p, (x0 + w / 2, -0.015, WORKTOP_H - 0.17), w * 0.55)
    else:
        p.add(b.box("cab_door", (w - 0.04, 0.02, WORKTOP_H - 0.2), (x0 + w / 2, -0.005, 0.1), shape.radius * 0.6,
                    shape.segments, b.mat("main", rough=0.5)))
        knob_round(p, (x0 + w - 0.07, -0.016, WORKTOP_H - 0.2))
    p.add(b.box("cab_plinth", (w - 0.06, DEPTH - 0.1, 0.07), (x0 + w / 2, DEPTH / 2, 0.0), 0.0, 1,
                b.mat("dark", rough=0.7)))


def wall_cabinets(shape, p, x_start, count, z0=1.50):
    """S1: pill cabinets in a chain (reference picture 3). S2: one stadium-shaped unit with a chrome band."""
    w, h, d = 0.58, 0.52, 0.34
    y = DEPTH + 0.02 - d / 2
    if shape.streamline:
        total = w * count
        outline = b.rounded_rect_outline(total, h, h / 2 - 0.01, 8, x_start, z0)
        p.add(b.prism("wall_unit", outline, d, (0, y, 0), b.mat("main", rough=0.45), radius=0.015, segments=2))
        for i in range(count):
            cx = x_start + w * i + w / 2
            p.add(b.box("wall_door", (w - 0.06, 0.02, h - 0.12), (cx, y - d / 2 - 0.005, z0 + 0.06), 0.02, 3,
                        b.mat("accent", rough=0.45)))
            handle_d(p, (cx, y - d / 2 - 0.02, z0 + 0.1), shape, 0.1)
        p.add(b.box("wall_band", (total + 0.004, d + 0.004, 0.018), (x_start + total / 2, y, z0 + h * 0.5 - 0.009),
                    0.004, 2, b.chrome()))
    else:
        for i in range(count):
            cx = x_start + w * i + w / 2
            outline = b.rounded_rect_outline(w, h, 0.14, 6, cx - w / 2, z0)
            p.add(b.prism("wall_frame", outline, d, (0, y, 0), b.mat("main", rough=0.45), radius=0.02, segments=3))
            door = b.rounded_rect_outline(w - 0.12, h - 0.12, 0.04, 4, cx - w / 2 + 0.06, z0 + 0.06)
            p.add(b.prism("wall_door", door, 0.02, (0, y - d / 2 - 0.006, 0), b.mat("accent", rough=0.45), radius=0.006,
                          segments=2))
            knob_round(p, (cx + w / 2 - 0.12, y - d / 2 - 0.02, z0 + h / 2))
            if i < count - 1:
                # The narrow bridge that links the pills.
                p.add(b.box("wall_link", (0.08, d * 0.8, h * 0.35), (cx + w / 2, y, z0 + h * 0.32), 0.03, 3,
                            b.mat("main", rough=0.45)))


def hood(shape, p, cx, z0=1.48, z1=2.75):
    if shape.streamline:
        prof = [(0.27, 0), (0.25, 0.04), (0.16, 0.18), (0.11, 0.32), (0.10, z1 - z0)]
        p.add(b.lathe("hood", prof, 32, (cx, DEPTH - 0.24, z0), b.chrome(), scale=(1, 0.75, 1)))
        for zz in (0.08, 0.2):
            p.add(b.torus("hood_band", 0.215 - zz * 0.45, 0.006, (cx, DEPTH - 0.24, z0 + zz), 32, 6,
                          b.mat("accent2", rough=0.4)))
    else:
        p.add(b.cyl("hood", 0.17, z1 - z0, (cx, DEPTH - 0.2, z0), 32, edge=0.02, material=b.chrome()))


def plant(p, loc, seed=7, scale=1.0):
    """A potted plant: a rounded pot and a bunch of leaves (seeded)."""
    x, y, z = loc
    rng = random.Random(seed)
    p.add(b.lathe("plant_pot", [(0, 0), (0.06 * scale, 0), (0.075 * scale, 0.11 * scale), (0.08 * scale, 0.12 * scale),
                                (0, 0.12 * scale)], 20, (x, y, z), b.mat("accent2", rough=0.5)))
    p.add(b.cyl("plant_soil", 0.072 * scale, 0.005, (x, y, z + 0.112 * scale), 16, material=b.mat("soil", rough=0.9)))
    for i in range(9):
        a = 2 * math.pi * i / 9 + rng.uniform(-0.2, 0.2)
        tilt = rng.uniform(25, 55)
        length = rng.uniform(0.13, 0.2) * scale
        leaf = b.sphere("leaf", length / 2, (x, y, z + 0.12 * scale), 8, (0.32, 0.1, 1),
                        b.mat("plant" if i % 2 else "plant_dark", rough=0.6))
        leaf.rotation_euler = rad(0, tilt, math.degrees(a))
        # Move the leaf so its base sits at the soil.
        leaf.location = (x + math.cos(a) * math.sin(math.radians(tilt)) * length / 2,
                         y + math.sin(a) * math.sin(math.radians(tilt)) * length / 2,
                         z + 0.12 * scale + math.cos(math.radians(tilt)) * length / 2)
        p.add(leaf)


def floor_tiles(p, x0, x1, y0, y1, tile=0.3, seed=11):
    """Patchwork floor (reference picture 3): square tiles in palette colours, seeded."""
    rng = random.Random(seed)
    from . import palettes
    colours = palettes.PALETTES[b.palette_id()]["tiles"]
    nx = int(round((x1 - x0) / tile))
    ny = int(round((y1 - y0) / tile))
    for i in range(nx):
        for j in range(ny):
            hex_ = colours[rng.randrange(len(colours))]
            p.add(b.box("tile", (tile - 0.006, tile - 0.006, 0.01), (x0 + (i + 0.5) * tile, y0 + (j + 0.5) * tile, -0.01),
                        0.003, 1, b.mat("tile", rough=0.4, hex_override=hex_)))
    p.add(b.box("grout", (x1 - x0, y1 - y0, 0.01), ((x0 + x1) / 2, (y0 + y1) / 2, -0.012), 0.0, 1,
                b.mat("cream", rough=0.8)))


def backsplash(p, x0, x1, z0, z1, y, tile=0.15):
    nx = int(round((x1 - x0) / tile))
    nz = int(round((z1 - z0) / tile))
    for i in range(nx):
        for j in range(nz):
            p.add(b.box("splash", (tile - 0.006, 0.01, tile - 0.006), (x0 + (i + 0.5) * tile, y, z0 + j * tile + 0.003),
                        0.002, 1, b.mat("cream", rough=0.3)))


def kitchen_corner(shape, small, with_room=True):
    """Kitchen corner centred on x = 0: cabinet, stove, cabinet, cabinet; wall units, hood, plant, worktop."""
    p = Parts()
    x_left = -0.9
    base_cabinet(shape, p, x_left, 0.6)
    st = stove(shape, x_left + 0.6)
    p.items += st.items
    base_cabinet(shape, p, x_left + 1.2, 0.6)
    base_cabinet(shape, p, x_left + 1.8, 0.6)
    # Worktops (left of stove, right of stove), steel with a coloured edge.
    for wx0, ww in ((x_left, 0.6), (x_left + 1.2, 1.2)):
        p.add(b.box("worktop", (ww, DEPTH + 0.02, 0.04), (wx0 + ww / 2, DEPTH / 2 - 0.01, WORKTOP_H - 0.05), 0.008, 2,
                    b.mat("chrome", metal=1.0, rough=0.35)))
        p.add(b.box("worktop_edge", (ww, 0.01, 0.04), (wx0 + ww / 2, -0.02, WORKTOP_H - 0.05), 0.003, 1,
                    b.mat("accent", rough=0.35)))
    wall_cabinets(shape, p, x_left, 1 if shape.streamline else 1, 1.5)
    wall_cabinets(shape, p, x_left + 1.2, 2, 1.5)
    hood(shape, p, x_left + 0.9)
    plant(p, (x_left + 2.2, 0.35, WORKTOP_H - 0.01), seed=5)
    if with_room:
        p.add(b.box("wall", (8.0, 0.1, 3.0), (0.0, DEPTH + 0.07, 0.0), 0.0, 1, b.mat("wall", rough=0.85)))
        backsplash(p, x_left, x_left + 2.4, WORKTOP_H - 0.01, 1.47, DEPTH + 0.015)
        floor_tiles(p, -3.9, 3.9, -3.3, DEPTH + 0.02)
    return p


def window_occluder(x, y0, y1, z_top=3.0):
    """A wall out of view with a window hole, so the sun makes a window-shaped light patch (picture 3)."""
    m = b.mat("dark", rough=1.0)
    wy, wz, ww, wh = -0.8, 1.0, 1.4, 1.5
    objs = [
        b.box("occ_low", (0.1, y1 - y0, wz), (x, (y0 + y1) / 2, 0), 0, 1, m),
        b.box("occ_high", (0.1, y1 - y0, z_top - wz - wh), (x, (y0 + y1) / 2, wz + wh), 0, 1, m),
        b.box("occ_front", (0.1, wy - ww / 2 - y0, wh), (x, (y0 + wy - ww / 2) / 2, wz), 0, 1, m),
        b.box("occ_back", (0.1, y1 - wy - ww / 2, wh), (x, (wy + ww / 2 + y1) / 2, wz), 0, 1, m),
        b.box("occ_mullion", (0.1, 0.06, wh), (x, wy, wz), 0, 1, m),
        b.box("occ_transom", (0.1, ww, 0.06), (x, wy, wz + wh * 0.62), 0, 1, m),
    ]
    for o in objs:
        o.visible_camera = False
    return objs


def knob_sheet(shape):
    """Two rows of swatches (P1 on top, P2 below), each with five knob or handle variants K1-K5."""
    p = Parts()
    for row, pid in enumerate(("P1", "P2")):
        b.set_palette(pid)
        z = 0.42 - row * 0.36
        for i in range(5):
            x = -0.72 + i * 0.36
            p.add(b.box("swatch", (0.32, 0.03, 0.3), (x, 0.0, z), shape.radius * 0.5, shape.segments,
                        b.mat("main" if i % 2 == 0 else "accent", rough=0.45)))
            fy = -0.016
            cz = z + 0.15
            if i == 0:
                knob_round(p, (x, fy, cz), 1.6)
            elif i == 1:
                knob_dial(p, (x, fy, cz), shape, 1.6, "cream")
            elif i == 2:
                handle_d(p, (x, fy - 0.02, cz - 0.03), shape, 0.14)
            elif i == 3:
                handle_bar(p, (x, fy, cz), 0.24)
            else:
                handle_lever(p, (x - 0.07, fy, cz), 1.4)
            p.add(b.text("label", f"K{i + 1}", 0.04, (x, fy, z + 0.03), rad(90), b.mat("dark"), 0.002))
        p.add(b.text("row", f"{pid}", 0.05, (-1.0, -0.02, z + 0.13), rad(90), b.mat("dark"), 0.002))
    p.add(b.box("sheet_back", (2.2, 0.02, 0.9), (-0.0, 0.05, -0.0), 0.0, 1, b.mat("cream", rough=0.9)))
    return p
