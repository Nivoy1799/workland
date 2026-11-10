"""Business themes for the style lab (spec 046, round 2): one grammar, many dialects.

Fixed for everyone (the grammar): the shape language, bevels, proportions, light and the palette structure (a base
family plus one accent). Free per business (the dialect): a colour set, patterns, signature props and the sign.
Themes are inspired by a cuisine and its crafts, never caricatures. Shop names are fictional.
"""

import math
import random

from . import build as b
from . import palettes
from .build import rad


def _m(hex_, rough=0.55, emit=0.0):
    return b.mat("t", rough=rough, emit=emit, hex_override=hex_)


# --- palettes for themed kitchens (same roles as P1/P2) ---------------------------------------------------------------

palettes.PALETTES["MEX"] = dict(
    palettes.PALETTES["P1"], name="Casa Nopal",
    wall="#F3DEC8", wall_out="#EFC9A9", main="#2F8F8A", main_dark="#1F6763", trim="#1F6763",
    accent="#F2A93B", accent2="#C8553D", cream="#F6EBD9",
    tiles=["#F6EBD9", "#C8553D", "#2F8F8A", "#F2A93B", "#2E4A8C", "#E6D2B5"],
    splash=["#F6EBD9", "#2E4A8C"], stripe=["#2F8F8A", "#F6EBD9"])
palettes.PALETTES["JAP"] = dict(
    palettes.PALETTES["P1"], name="Yuzu",
    wall="#EFE9DD", wall_out="#EDE6D8", main="#C9A27A", main_dark="#8A5A3B", trim="#2E3F5C",
    accent="#2E3F5C", accent2="#C8462F", cream="#F4EFE4",
    tiles=["#D8D2C6", "#C9C2B4", "#E4DED2", "#B9B2A4"], splash=["#F4EFE4", "#E4DED2"], stripe=["#2E3F5C", "#F4EFE4"])


# --- shared prop helpers ----------------------------------------------------------------------------------------------

def _terracotta_pot(p, x, y, r=0.22, h=0.4, colour="#C8553D"):
    p.add(b.lathe("pot", [(0, 0), (r * 0.75, 0), (r, h * 0.9), (r * 1.08, h), (r * 0.95, h), (0, h * 0.95)], 24,
                  (x, y, 0), _m(colour, 0.7)))
    p.add(b.cyl("soil", r * 0.92, 0.01, (x, y, h * 0.9), 20, material=_m("#5C4030", 0.9)))
    return h * 0.9


def _column_cactus(p, x, y, z, height=0.9, seed=1):
    """A column cactus with two arms, ribbed by a little flattening."""
    g = _m("#4F7A3A", 0.6)
    p.add(b.capsule("cactus", 0.09, height, (x, y, z), 12, g))
    rng = random.Random(seed)
    for side, hz in ((1, 0.35), (-1, 0.5)):
        hz = hz * height + rng.uniform(-0.05, 0.05)
        p.add(b.capsule("cactus_arm_h", 0.06, 0.2, (x, y, z + hz), 10, g, rad(0, side * 90, 0)))
        p.add(b.capsule("cactus_arm_v", 0.06, 0.32, (x + side * 0.17, y, z + hz - 0.04), 10, g))
    p.add(b.sphere("cactus_flower", 0.035, (x, y, z + height + 0.01), 10, material=_m("#F2A93B", 0.5)))


def _prickly_pear(p, x, y, z, seed=2):
    g = _m("#6E9A4A", 0.6)
    rng = random.Random(seed)
    pads = [(0, 0, 0.16, 0), (-0.1, 0, 0.36, -25), (0.12, 0, 0.38, 30), (0.0, 0.0, 0.55, 5)]
    for px, py, pz, tilt in pads:
        pad = b.sphere("pad", 0.12, (x + px, y + py, z + pz), 14, (1.0, 0.28, 1.3), g)
        pad.rotation_euler = rad(0, tilt, rng.uniform(-20, 20))
        p.add(pad)


def _talavera_band(p, x0, x1, z0, rows, y, size=0.16):
    """Painted tiles: cream ground, a blue four-petal flower and a yellow centre, with blue corner dots."""
    ground, blue, yellow = _m("#F6EBD9", 0.3), _m("#2E4A8C", 0.35), _m("#F2A93B", 0.4)
    n = int((x1 - x0) / size)
    for r in range(rows):
        for i in range(n):
            cx = x0 + (i + 0.5) * size
            cz = z0 + (r + 0.5) * size
            p.add(b.box("tile", (size - 0.008, 0.012, size - 0.008), (cx, y, cz - size / 2 + 0.004), 0.004, 1, ground))
            for a in range(4):
                ang = math.radians(45 + 90 * a)
                petal = b.sphere("petal", 0.026, (cx + math.cos(ang) * 0.03, y - 0.008, cz + math.sin(ang) * 0.03), 8,
                                 (1, 0.25, 0.55), blue)
                petal.rotation_euler = rad(0, -math.degrees(ang) + 90, 0)
                p.add(petal)
            p.add(b.sphere("centre", 0.014, (cx, y - 0.01, cz), 8, (1, 0.4, 1), yellow))


def _papel_picado(p, x0, x1, y, z, sag=0.1, seed=4):
    """A string of cut-paper flags: small coloured sheets with a scalloped lower edge and a window cut out."""
    colours = ["#C8553D", "#F2A93B", "#2F8F8A", "#E58AA6", "#2E4A8C", "#8CC5AC"]
    rng = random.Random(seed)
    n = int((x1 - x0) / 0.32)
    string = _m("#F6EBD9", 0.6)
    for i in range(n + 1):
        t = i / n
        sx = x0 + (x1 - x0) * t
        sz = z - sag * 4 * t * (1 - t)
        if i < n:
            nt = (i + 1) / n
            nx, nz = x0 + (x1 - x0) * nt, z - sag * 4 * nt * (1 - nt)
            length = math.dist((sx, sz), (nx, nz))
            ang = math.degrees(math.atan2(nz - sz, nx - sx))
            p.add(b.cyl("string", 0.004, length, (sx, y, sz), 6, material=string, rot=rad(0, 90 - ang, 0)))
            fx, fz = (sx + nx) / 2, (sz + nz) / 2
            col = _m(colours[(i + rng.randrange(2)) % len(colours)], 0.7)
            outline = [(-0.12, 0.0), (0.12, 0.0), (0.12, -0.22)] + [
                (0.12 - 0.06 * k - 0.03, -0.22 - 0.025 * (k % 2 == 0)) for k in range(4)] + [(-0.12, -0.22)]
            p.add(b.prism("flag", outline, 0.004, (fx, y, fz), col, rad(0, 0, rng.uniform(-4, 4))))
            p.add(b.box("flag_cut", (0.08, 0.006, 0.07), (fx, y, fz - 0.14), 0.02, 2, _m("#2C2826", 0.9)))


def _paper_lantern(p, x, y, z, r=0.17, h=0.42, colour="#C8462F", glow=2.0):
    """A round paper lantern with ribs and black caps, glowing after sunset."""
    prof = [(0.0, 0.0), (r * 0.55, 0.0)] + [(r * math.sin(math.radians(a)), h * (1 - math.cos(math.radians(a))) / 2)
                                             for a in range(20, 161, 20)] + [(r * 0.55, h), (0.0, h)]
    p.add(b.lathe("lantern", prof, 24, (x, y, z), b.mat("t", rough=0.8, emit=glow * b._glow * 0.4,
                                                         hex_override=colour)))
    black = _m("#1E1A18", 0.5)
    p.add(b.cyl("lantern_cap", r * 0.6, 0.04, (x, y, z + h - 0.02), 20, edge=0.01, material=black))
    p.add(b.cyl("lantern_base", r * 0.6, 0.04, (x, y, z - 0.02), 20, edge=0.01, material=black))
    for k in range(1, 6):
        zz = z + h * k / 6
        rr = r * math.sin(math.acos(1 - 2 * k / 6)) if 0 < k < 6 else 0
        p.add(b.torus("lantern_rib", rr + 0.002, 0.004, (x, y, zz), 24, 4, _m("#7A2A1E", 0.6)))
    p.add(b.cyl("lantern_cord", 0.006, 0.25, (x, y, z + h), 6, material=black))


def _noren(p, cx, width, y, z_top, colour="#2E3F5C", emblem="#F4EFE4"):
    """A split fabric curtain on a wooden rod over the door, with a round emblem across the middle."""
    p.add(b.cyl("noren_rod", 0.02, width + 0.3, (cx - width / 2 - 0.15, y, z_top), 10,
                material=_m("#8A5A3B", 0.6), rot=rad(0, 90, 0)))
    panels = 3
    pw = width / panels
    cloth = _m(colour, 0.85)
    for i in range(panels):
        px = cx - width / 2 + pw * (i + 0.5)
        panel = b.box("noren", (pw - 0.025, 0.012, 0.85), (px, y - 0.02, z_top - 0.87), 0.006, 1, cloth,
                      rad(3 * (i - 1), 0, 0))
        p.add(panel)
    p.add(b.cyl("noren_emblem", 0.17, 0.006, (cx, y - 0.035, z_top - 0.45), 32, material=_m(emblem, 0.7),
                rot=rad(90)))
    p.add(b.cyl("noren_emblem_in", 0.12, 0.006, (cx, y - 0.04, z_top - 0.45), 32, material=cloth, rot=rad(90)))
    p.add(b.cyl("noren_dot", 0.03, 0.006, (cx, y - 0.045, z_top - 0.45), 16, material=_m("#F2B53B", 0.5),
                rot=rad(90)))


def _pine(p, x, y, seed=6):
    """A small shaped pine in a square dark planter."""
    rng = random.Random(seed)
    p.add(b.box("planter", (0.5, 0.5, 0.42), (x, y, 0), 0.03, 2, _m("#2C2826", 0.6)))
    bark = _m("#6B4A33", 0.8)
    p.add(b.capsule("trunk", 0.045, 0.9, (x, y, 0.4), 10, bark, rad(0, 12, 0)))
    p.add(b.capsule("trunk2", 0.035, 0.55, (x + 0.15, y, 1.15), 10, bark, rad(0, -35, 0)))
    for cx, cz, s in ((x - 0.15, 1.2, 0.26), (x + 0.32, 1.48, 0.22), (x + 0.05, 1.72, 0.2)):
        cloud = b.sphere("needles", s, (cx, y + rng.uniform(-0.05, 0.05), cz), 14, (1.3, 1.0, 0.55),
                         _m("#3F6B3A", 0.7))
        p.add(cloud)


# --- shop themes ----------------------------------------------------------------------------------------------------

def default_theme(palette_id="P1"):
    pal = palettes.PALETTES[palette_id]
    return {
        "name": "TOMORROW KITCHEN", "wall": pal["wall_out"], "trim": pal["main_dark"], "accent": pal["accent2"],
        "door": pal["main"], "sign": pal["main_dark"], "letters": "#F7F1E3", "stripes": pal["stripe"],
        "flowers": ["#E58436", "#F2D27A", "#F3E9D2"], "inside": "#F1E6CF", "inside_floor": "#C8A27A",
        "counter": pal["main"], "jars": ["#E3AE45", "#C9734F", "#8CC5AC", "#F3E9D2"], "number": "12",
        "props": lambda p, shape: _default_props(p, shape, pal),
    }


def _default_props(p, shape, pal):
    from .hero import menu_board
    from .kitchen import plant
    menu_board(p, (2.6, -1.1, 0.0), {})
    for px in (-2.85,):
        p.add(b.box("planter", (0.5, 0.5, 0.45), (px, -0.45, 0), 0.06, 3, _m(pal["main"], 0.45)))
        plant(p, (px, -0.45, 0.42), seed=21, scale=2.6)


def mex_theme():
    return {
        "name": "CASA NOPAL", "wall": "#EFC9A9", "trim": "#1F6763", "accent": "#C8553D", "door": "#2F8F8A",
        "sign": "#1F6763", "letters": "#F6EBD9", "stripes": ["#2F8F8A", "#F6EBD9", "#F2A93B", "#F6EBD9"],
        "flowers": ["#C8553D", "#F2A93B", "#E58AA6"], "inside": "#F3DEC8", "inside_floor": "#C8553D",
        "counter": "#2F8F8A", "jars": ["#C8553D", "#F2A93B", "#2E4A8C", "#F6EBD9"], "number": "7",
        "props": _mex_props,
    }


def _mex_props(p, shape):
    _talavera_band(p, -2.9, 2.9, 0.04, 2, -0.06 if shape.streamline else -0.05)
    edge_y, edge_z = (-1.18, 2.47) if shape.streamline else (-0.95, 2.21)
    _papel_picado(p, -2.7, 2.6, edge_y, edge_z, 0.06)
    top = _terracotta_pot(p, 2.65, -0.55, 0.24, 0.42)
    _column_cactus(p, 2.65, -0.55, top, 1.0, 3)
    top = _terracotta_pot(p, -2.85, -0.5, 0.26, 0.36, "#D9844F")
    _prickly_pear(p, -2.85, -0.5, top)


def jap_theme():
    return {
        "name": "YUZU NOODLES", "wall": "#EDE6D8", "trim": "#8A5A3B", "accent": "#2E3F5C", "door": "#C9A27A",
        "sign": "#2E3F5C", "letters": "#F4EFE4", "stripes": ["#2E3F5C", "#F4EFE4"],
        "flowers": ["#F4EFE4", "#E58AA6"], "inside": "#EFE9DD", "inside_floor": "#C9A27A", "counter": "#C9A27A",
        "jars": ["#2E3F5C", "#C8462F", "#F4EFE4", "#8A5A3B"], "number": "3",
        "props": _jap_props,
    }


def _jap_props(p, shape):
    # Wooden lattice in the window's transom, a noren over the door, paper lanterns, a vertical sign, a pine.
    wood = _m("#8A5A3B", 0.6)
    for i in range(36):
        x = -2.45 + 0.04 + i * (3.0 / 36)
        p.add(b.box("lattice", (0.025, 0.03, 0.4), (x, -0.07, 2.35 - 0.41), 0.0, 1, wood))
    _noren(p, 1.6, 1.1, -0.15, 2.3)
    for lx in (0.72, 2.48):
        _paper_lantern(p, lx, -0.4, 1.55)
        p.add(b.box("lantern_arm", (0.04, 0.4, 0.04), (lx, -0.2, 2.2), 0.01, 1, wood))
    board = b.box("v_sign", (0.34, 0.05, 1.5), (2.75, -0.12, 0.7), 0.02, 2, _m("#C9A27A", 0.6))
    p.add(board)
    for k, ch in enumerate("YUZU"):
        p.add(b.text("v_letter", ch, 0.22, (2.75, -0.15, 2.0 - k * 0.3), rad(90), _m("#2E3F5C", 0.4), 0.01))
    _pine(p, -2.85, -0.5)


THEMES = {"default": default_theme, "mex": mex_theme, "jap": jap_theme}
KITCHEN_PALETTE = {"default": "P1", "mex": "MEX", "jap": "JAP"}


def kitchen_props(p, theme_id, worktop_h):
    """Small signature props on the kitchen worktop."""
    if theme_id == "mex":
        top = _terracotta_pot_small(p, 1.25, 0.3, worktop_h)
        _prickly_pear_small(p, 1.25, 0.3, worktop_h + top)
        for k, col in enumerate(("#C8553D", "#F2A93B", "#2E4A8C")):
            p.add(b.lathe("bowl", [(0, 0), (0.05, 0), (0.08, 0.05), (0.085, 0.055), (0, 0.04)], 20,
                          (0.25 + k * 0.17, 0.42, worktop_h), _m(col, 0.35)))
    elif theme_id == "jap":
        for k in range(3):
            z = worktop_h + k * 0.075
            p.add(b.cyl("steamer", 0.13, 0.07, (1.2, 0.32, z), 24, edge=0.01, material=_m("#C9A27A", 0.7)))
            p.add(b.torus("steamer_band", 0.131, 0.006, (1.2, 0.32, z + 0.035), 24, 4, _m("#8A5A3B", 0.6)))
        p.add(b.cyl("steamer_lid", 0.13, 0.03, (1.2, 0.32, worktop_h + 0.225), 24, edge=0.015,
                    material=_m("#C9A27A", 0.7)))
        for k in range(3):
            p.add(b.lathe("bowl", [(0, 0), (0.035, 0), (0.07, 0.06), (0.073, 0.065), (0, 0.05)], 20,
                          (0.3 + k * 0.16, 0.42, worktop_h), _m(("#2E3F5C", "#F4EFE4", "#C8462F")[k], 0.3)))


def _terracotta_pot_small(p, x, y, z):
    p.add(b.lathe("pot", [(0, 0), (0.05, 0), (0.07, 0.1), (0.075, 0.11), (0, 0.1)], 20, (x, y, z), _m("#C8553D", 0.7)))
    return 0.1


def _prickly_pear_small(p, x, y, z):
    g = _m("#6E9A4A", 0.6)
    for px, pz, tilt in ((0, 0.05, 0), (-0.035, 0.12, -25), (0.04, 0.13, 30)):
        pad = b.sphere("pad", 0.04, (x + px, y, z + pz), 12, (1.0, 0.3, 1.3), g)
        pad.rotation_euler = rad(0, tilt, 0)
        p.add(pad)
