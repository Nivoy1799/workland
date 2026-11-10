"""Round 4 concept kit for the style lab (spec 046): quick pieces for vibe frames, not final models.

Robots, an automat wall, dining furniture, plated food, a crowd, animals and town pieces. Axes as everywhere:
X right, Y away from the camera, Z up, metres.
"""

import math
import random

from mathutils import Vector

from . import build as b
from . import character, mess
from .build import Parts, rad
from .hero import clear_glass


def _m(hex_, rough=0.55, metal=0.0, emit=0.0, alpha=1.0):
    return b.mat("concept", metal=metal, rough=rough, emit=emit, alpha=alpha, hex_override=hex_)


# --- robots ----------------------------------------------------------------------------------------------------------

def robot_sweeper(p, loc, yaw=0.0, body="#F3E9D2", band="#4F9A85", seed=1):
    """'Tidy': a low dome on spinning brushes, two lamp eyes, an antenna with a ball, a little bin on its back."""
    rng = random.Random(seed)
    x, y, z = loc
    parts = []
    parts.append(b.lathe("tidy_body", [(0, 0.06), (0.3, 0.06), (0.32, 0.12), (0.3, 0.3), (0.2, 0.42), (0, 0.45)], 28,
                         (0, 0, 0), _m(body, 0.4)))
    parts.append(b.torus("tidy_band", 0.315, 0.025, (0, 0, 0.15), 28, 6, _m(band, 0.4)))
    for sx in (-1, 1):
        parts.append(b.sphere("tidy_eye", 0.045, (sx * 0.1, -0.24, 0.3), 14, (1, 0.6, 1), b.lamp(2.5, "lamp_warm")))
        parts.append(b.torus("tidy_eye_ring", 0.047, 0.01, (sx * 0.1, -0.245, 0.3), 14, 4, b.chrome(), rad(90)))
    parts.append(b.cyl("tidy_antenna", 0.008, 0.22, (0.08, 0.05, 0.43), 6, material=b.chrome()))
    parts.append(b.sphere("tidy_ball", 0.025, (0.08, 0.05, 0.66), 10, material=b.lamp(3.0, "neon")))
    parts.append(b.box("tidy_bin", (0.22, 0.14, 0.12), (0, 0.24, 0.3), 0.03, 2, _m(band, 0.5)))
    for sx in (-1, 1):
        brush = b.cyl("tidy_brush", 0.13, 0.03, (sx * 0.24, -0.2, 0.0), 16, material=_m("#C9A548", 0.9))
        parts.append(brush)
        for k in range(10):
            a = 2 * math.pi * k / 10 + rng.uniform(0, 0.3)
            bristle = b.box("bristle", (0.01, 0.07, 0.008), (sx * 0.24 + math.cos(a) * 0.15, -0.2 + math.sin(a) * 0.15,
                                                             0.005), 0.0, 1, _m("#C9A548", 0.9))
            bristle.rotation_euler = rad(0, 0, math.degrees(a) + 90)
            parts.append(bristle)
    for k in range(3):
        parts.append(b.cyl("tidy_wheel", 0.05, 0.03, (0.18 * math.cos(k * 2.1), 0.18 * math.sin(k * 2.1), 0.05), 12,
                           material=_m("#2C2826", 0.7), rot=rad(0, 90, 0)))
    g = b.group("tidy", parts, loc, rad(0, 0, yaw))
    p.add(g)
    # A little dust swirl where it has been.
    for k in range(14):
        a = rng.uniform(0, 6.28)
        r = rng.uniform(0.35, 0.8)
        p.add(b.sphere("dust", rng.uniform(0.01, 0.025), (x + math.cos(a) * r * 0.6, y + 0.35 + abs(math.sin(a)) * r,
                                                           z + rng.uniform(0.01, 0.12)), 6,
                       material=_m("#D8CDB8", 0.9, alpha=0.6)))
    return g


def robot_runner(p, loc, yaw=0.0, body="#F3E9D2", accent="#E58436", tray=True, seed=2):
    """'Tray': a slim runner on a ball, a dome head with a screen face, arms holding a tray of plates."""
    parts = []
    parts.append(b.sphere("runner_ball", 0.12, (0, 0, 0.12), 16, material=_m("#2C2826", 0.6)))
    parts.append(b.lathe("runner_body", [(0.0, 0.18), (0.16, 0.2), (0.2, 0.5), (0.17, 0.9), (0.12, 1.0), (0.0, 1.02)],
                         24, (0, 0, 0), _m(body, 0.4)))
    parts.append(b.torus("runner_band", 0.195, 0.02, (0, 0, 0.55), 24, 6, _m(accent, 0.4)))
    parts.append(b.sphere("runner_head", 0.16, (0, 0, 1.15), 20, (1, 0.9, 0.85), clear_glass("#BFD9E0", 0.45)))
    parts.append(b.box("runner_screen", (0.18, 0.02, 0.1), (0, -0.12, 1.1), 0.02, 2, _m("#2C3A38", 0.3)))
    for sx in (-1, 1):
        parts.append(b.sphere("runner_eye", 0.016, (sx * 0.04, -0.135, 1.15), 8, material=b.lamp(3.0, "lamp_warm")))
    parts.append(b.torus("runner_smile", 0.03, 0.006, (0, -0.135, 1.13), 12, 4, b.lamp(3.0, "lamp_warm"), rad(90),
                         arc=150))
    parts[-1].rotation_euler = rad(90, 195, 0)
    for sx in (-1, 1):
        arm = b.capsule("runner_arm", 0.025, 0.35, (sx * 0.17, 0.0, 0.85), 8, b.chrome(), rad(-75, 0, 0))
        parts.append(arm)
    if tray:
        parts.append(b.cyl("tray", 0.24, 0.02, (0, -0.34, 0.92), 24, edge=0.008, material=b.chrome()))
        for k, col in enumerate(("#E3AE45", "#C8402F")):
            parts.append(b.lathe("plate", [(0, 0), (0.07, 0), (0.11, 0.02), (0, 0.012)], 20,
                                 (-0.08 + k * 0.16, -0.34, 0.94), _m("#F3EFE6", 0.35)))
            parts.append(b.sphere("dish", 0.06, (-0.08 + k * 0.16, -0.34, 0.965), 12, (1, 1, 0.45), _m(col, 0.5)))
    g = b.group("runner", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def robot_arm(p, base, target, colour="#F3E9D2", joint="#4F9A85"):
    """A rounded robot arm from a base on the counter to a target point (it ends in a spoon)."""
    base = Vector(base)
    target = Vector(target)
    p.add(b.cyl("arm_base", 0.12, 0.1, tuple(base), 24, edge=0.02, material=_m(joint, 0.4)))
    shoulder = base + Vector((0, 0, 0.18))
    p.add(b.sphere("arm_shoulder", 0.085, tuple(shoulder), 16, material=b.chrome()))
    mid = (shoulder + target) / 2 + Vector((0, 0.15, 0.35))
    for a, c, r in ((shoulder, mid, 0.06), (mid, target + Vector((0, 0, 0.25)), 0.05)):
        d = c - a
        seg = b.capsule("arm_seg", r, d.length, tuple(a), 14, _m(colour, 0.35))
        seg.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p.add(seg)
    p.add(b.sphere("arm_elbow", 0.07, tuple(mid), 16, material=b.chrome()))
    wrist = target + Vector((0, 0, 0.25))
    p.add(b.sphere("arm_wrist", 0.05, tuple(wrist), 14, material=_m(joint, 0.4)))
    p.add(b.cyl("arm_spoon", 0.01, 0.28, tuple(target + Vector((0, 0, -0.02))), 8, material=b.chrome()))
    p.add(b.sphere("arm_lamp", 0.02, tuple(mid + Vector((0, -0.07, 0.02))), 8, material=b.lamp(3.0, "neon")))


# --- food and dining -------------------------------------------------------------------------------------------------

DISHES = {
    "hash": ("#C9934E", "#5E8F4F"),       # potato hash with green sauce
    "bowl": ("#E0664E", "#F3E9D2"),       # tomato stew
    "noodles": ("#E8CF8A", "#4F7A3A"),    # noodles with greens
    "salad": ("#6E9A4A", "#E0664E"),
}


def plated(p, loc, kind="hash", plate="#F3EFE6", scale=1.0, seed=3):
    """A plate or bowl of food: a heap, a sauce, a few pieces on top."""
    rng = random.Random(seed)
    x, y, z = loc
    base, top = DISHES[kind]
    s = scale
    if kind in ("bowl", "noodles"):
        p.add(b.lathe("bowl", [(0, 0), (0.05 * s, 0), (0.09 * s, 0.06 * s), (0.095 * s, 0.065 * s), (0, 0.045 * s)], 24,
                      loc, _m(plate, 0.3)))
        p.add(b.cyl("soup", 0.085 * s, 0.004, (x, y, z + 0.05 * s), 24, material=_m(base, 0.25)))
    else:
        p.add(b.lathe("plate", [(0, 0), (0.08 * s, 0), (0.13 * s, 0.018 * s), (0.135 * s, 0.022 * s), (0, 0.012 * s)],
                      24, loc, _m(plate, 0.35)))
        p.add(b.sphere("heap", 0.075 * s, (x, y, z + 0.02 * s), 14, (1, 1, 0.4), _m(base, 0.6)))
    for k in range(5):
        a = rng.uniform(0, 6.28)
        r = rng.uniform(0, 0.04) * s
        p.add(b.sphere("topping", rng.uniform(0.008, 0.014) * s,
                       (x + math.cos(a) * r, y + math.sin(a) * r, z + 0.05 * s), 8, material=_m(top, 0.5)))


def drink(p, loc, colour="#F2D27A"):
    x, y, z = loc
    p.add(b.lathe("glass", [(0, 0), (0.035, 0), (0.04, 0.14), (0.041, 0.142), (0, 0.13)], 16, loc,
                  clear_glass("#E8F2F4", 0.35)))
    p.add(b.cyl("drink", 0.034, 0.1, (x, y, z + 0.01), 16, material=_m(colour, 0.2, alpha=0.85)))
    p.add(b.cyl("straw", 0.004, 0.2, (x + 0.015, y, z + 0.03), 6, material=_m("#E58436", 0.5), rot=rad(0, 10, 0)))


def table(p, loc, top="#F3E9D2", r=0.45, seed=4):
    x, y, z = loc
    p.add(b.cyl("table_top", r, 0.04, (x, y, z + 0.72), 32, edge=0.012, material=_m(top, 0.35)))
    p.add(b.torus("table_edge", r, 0.012, (x, y, z + 0.74), 32, 6, b.chrome()))
    p.add(b.cyl("table_leg", 0.04, 0.72, (x, y, z), 12, material=b.chrome()))
    p.add(b.cyl("table_foot", 0.22, 0.03, (x, y, z), 24, edge=0.01, material=b.chrome()))


def booth(p, loc, yaw=0.0, colour="#C8553D", width=1.4):
    """A diner booth bench with a tall rounded back."""
    parts = [b.box("booth_seat", (width, 0.55, 0.45), (0, 0, 0), 0.06, 3, _m(colour, 0.5)),
             b.box("booth_back", (width, 0.18, 0.75), (0, 0.22, 0.4), 0.08, 4, _m(colour, 0.5)),
             b.box("booth_trim", (width + 0.02, 0.2, 0.05), (0, 0.22, 1.13), 0.02, 2, b.chrome())]
    for k in range(int(width / 0.25)):
        parts.append(b.box("booth_tuft", (0.012, 0.01, 0.6), (-width / 2 + 0.2 + k * 0.25, 0.12, 0.5), 0.0, 1,
                           _m("#7A2E22", 0.6)))
    g = b.group("booth", parts, loc, rad(0, 0, yaw))
    p.add(g)


def menu_board_wall(p, loc, w=2.2, h=0.9, items=("POTATO HASH 4.50", "TOMATO STEW 5.00", "GREEN NOODLES 6.00",
                                                  "LEMONADE 2.00")):
    x, y, z = loc
    p.add(b.box("menu_back", (w, 0.04, h), (x, y, z), 0.03, 2, _m("#2F3A35", 0.8)))
    p.add(b.box("menu_frame", (w + 0.08, 0.03, h + 0.08), (x, y + 0.01, z - 0.04), 0.03, 2, _m("#B98B5E", 0.6)))
    for k, line in enumerate(items):
        t = b.text("menu_line", line, 0.075, (x, y - 0.025, z + h - 0.16 - k * 0.17), rad(90), _m("#F3E9D2", 0.8),
                   0.002)
        f = mess._hand_font()
        if f:
            t.data.font = f
        p.add(t)


def pendant(p, loc, colour="#E58436", glow=3.5):
    x, y, z = loc
    p.add(b.cyl("cord", 0.005, 1.0, (x, y, z), 6, material=_m("#2C2826", 0.6)))
    p.add(b.lathe("shade", [(0.0, 0.2), (0.2, 0.0), (0.19, -0.01), (0.0, 0.03)], 24, (x, y, z - 0.18),
                  _m(colour, 0.4)))
    p.add(b.sphere("bulb", 0.055, (x, y, z - 0.18), 12, material=b.lamp(glow)))


def steam(p, loc, n=10, seed=5, height=0.5):
    rng = random.Random(seed)
    x, y, z = loc
    for k in range(n):
        t = k / n
        p.add(b.sphere("steam", 0.03 + 0.05 * t, (x + rng.uniform(-0.05, 0.05) + t * 0.08, y + rng.uniform(-0.04, 0.04),
                                                  z + t * height), 10, material=_m("#FFFFFF", 0.9, alpha=0.35 - 0.25 * t)))


def ticket_rail(p, loc, n=8, w=1.6, seed=6):
    rng = random.Random(seed)
    x, y, z = loc
    p.add(b.box("rail", (w, 0.03, 0.03), (x, y, z), 0.01, 1, b.chrome()))
    for k in range(n):
        tx = x - w / 2 + 0.1 + k * (w - 0.2) / max(1, n - 1)
        tk = b.box("ticket", (0.09, 0.003, 0.16), (tx, y - 0.02, z - 0.16), 0.0, 1, _m("#F7F2E6", 0.85))
        tk.rotation_euler = rad(rng.uniform(-6, 6), 0, rng.uniform(-8, 8))
        p.add(tk)
        for line in range(3):
            p.add(b.box("ticket_line", (0.06 - 0.015 * (line % 2), 0.002, 0.006), (tx, y - 0.023, z - 0.05 - line * 0.03),
                        0.0, 1, _m("#2A2624", 0.8)))


def string_lights(p, a, c, n=14, sag=0.4, colours=("#FFD9A0",), glow=3.0):
    a, c = Vector(a), Vector(c)
    wire = _m("#2C2826", 0.6)
    prev = a
    for k in range(1, n + 1):
        t = k / n
        pt = a + (c - a) * t - Vector((0, 0, sag * 4 * t * (1 - t)))
        d = pt - prev
        seg = b.cyl("wire", 0.004, d.length, tuple(prev), 4, material=wire)
        seg.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p.add(seg)
        if k < n:
            col = colours[k % len(colours)]
            p.add(b.sphere("bulb", 0.035, tuple(pt - Vector((0, 0, 0.04))), 8,
                           material=b.mat("bulb", emit=glow * b._glow * 0.5, rough=0.4, hex_override=col)))
        prev = pt


# --- crowd and animals -----------------------------------------------------------------------------------------------

SKINS = ["#F1C9A5", "#E4B597", "#D9A786", "#C98E6B", "#A86E4C", "#8E5B3E", "#6B4430"]
HAIRS = ["#2A1E18", "#5A3A2A", "#8A5A3B", "#C9A06A", "#D8D2CA", "#1E1714", "#A0482E"]
TOPS = ["#E58436", "#4F9A85", "#E3AE45", "#F3E9D2", "#4E6E8E", "#C8553D", "#8CC5AC", "#9A7AA8", "#D9C7A6"]
BOTTOMS = ["#4E6E8E", "#5E554D", "#3F5F78", "#2F3A35", "#8A5A3B", "#C8553D"]


def random_look(seed, older=None, apron=""):
    rng = random.Random(seed)
    old = rng.random() < 0.4 if older is None else older
    skirt = rng.random() < 0.3
    lk = character.Look(
        height=rng.uniform(1.6, 1.82) - (0.04 if old else 0.0), build=rng.uniform(0.92, 1.18),
        shoulders=rng.uniform(0.92, 1.05), lean=rng.uniform(0.2, 0.45) if old else 0.0,
        skin=rng.choice(SKINS), hair="#D8D2CA" if old and rng.random() < 0.7 else rng.choice(HAIRS),
        hair_style=rng.choice(["bun", "balding", "short"] if old else ["short", "bob", "quiff", "curly", "bun"]),
        top=rng.choice(TOPS), top_style=rng.choice(["shirt", "tee", "blouse"]),
        layer=rng.choice(TOPS) if rng.random() < 0.35 else "", layer_style=rng.choice(["cardigan", "vest"]),
        bottom=rng.choice(BOTTOMS), bottom_style="skirt" if skirt else "trousers",
        glasses=old and rng.random() < 0.7, eyelids=old,
        hat=rng.choice(["", "", "", "flat"]) if old else "", hat_colour=rng.choice(["#7E6A55", "#4E6E8E"]),
        expression=rng.choice(["smile", "smile", "neutral", "grin", "focused", "tired"]), seed=seed,
        apron=apron, apron_stains=bool(apron) and rng.random() < 0.7, messy=rng.random() < 0.3,
        prop=rng.choice(["", "", "bag", "tote"]) if not apron else "", prop_colour=rng.choice(TOPS),
        shoes=rng.choice(["#6B4A33", "#2C2826", "#F3E9D2", "#3A2A22"]))
    return lk


def crowd(spots, seed=10, apron="", sit=False):
    """People at given (x, y, yaw) spots; seated ones sit on a 0.45 m seat with hands forward on a table."""
    out = []
    for k, (x, y, yaw) in enumerate(spots):
        lk = random_look(seed + k * 13, apron=apron)
        if sit:
            lk.sit, lk.bottom_style, lk.prop, lk.lean = True, "trousers", "", 0.0
        out.append(character.person(lk, (x, y, 0.0), yaw, f"crowd{seed}_{k}"))
    return out


def chair(p, loc, yaw=0.0, seat="#C8553D"):
    """A diner chair: chrome legs, a padded seat and a rounded back."""
    parts = [b.box("chair_seat", (0.44, 0.42, 0.08), (0, 0, 0.42), 0.035, 3, _m(seat, 0.5)),
             b.box("chair_back", (0.42, 0.07, 0.38), (0, 0.2, 0.55), 0.04, 3, _m(seat, 0.5))]
    for sx in (-0.18, 0.18):
        for sy in (-0.17, 0.17):
            parts.append(b.cyl("chair_leg", 0.015, 0.43, (sx, sy, 0.0), 8, material=b.chrome()))
    g = b.group("chair", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def dog(p, loc, yaw=0.0, colour="#C9A06A"):
    parts = []
    fur = _m(colour, 0.8)
    parts.append(b.capsule("dog_body", 0.12, 0.55, (0, 0.27, 0.38), 14, fur, rad(90, 0, 0)))
    parts.append(b.sphere("dog_head", 0.11, (0, -0.32, 0.6), 14, (0.9, 1.1, 0.95), fur))
    parts.append(b.sphere("dog_snout", 0.06, (0, -0.43, 0.56), 12, (0.8, 1.2, 0.75), fur))
    parts.append(b.sphere("dog_nose", 0.02, (0, -0.5, 0.58), 8, material=_m("#2C2826", 0.4)))
    for sx in (-1, 1):
        ear = b.sphere("dog_ear", 0.06, (sx * 0.08, -0.3, 0.66), 10, (0.4, 0.8, 1.3), _m("#8A5A3B", 0.8))
        ear.rotation_euler = rad(0, sx * 25, 0)
        parts.append(ear)
        parts.append(b.sphere("dog_eye", 0.014, (sx * 0.045, -0.41, 0.64), 8, material=_m("#2C2826", 0.3)))
        for ly in (-0.2, 0.22):
            parts.append(b.capsule("dog_leg", 0.035, 0.36, (sx * 0.07, ly, 0.0), 10, fur))
    parts.append(b.capsule("dog_tail", 0.025, 0.25, (0, 0.27, 0.45), 8, fur, rad(55, 0, 0)))
    parts.append(b.torus("dog_collar", 0.07, 0.012, (0, -0.24, 0.52), 14, 4, _m("#C8402F", 0.5), rad(70, 0, 0)))
    g = b.group("dog", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def hen(p, loc, yaw=0.0):
    parts = []
    white = _m("#F4EFE4", 0.8)
    parts.append(b.sphere("hen_body", 0.14, (0, 0.03, 0.22), 14, (0.85, 1.15, 0.9), white))
    parts.append(b.sphere("hen_head", 0.065, (0, -0.13, 0.36), 12, material=white))
    parts.append(b.lathe("hen_beak", [(0, 0), (0.015, 0.0), (0, 0.04)], 8, (0, -0.19, 0.35), _m("#E3AE45", 0.5),
                         rad(-90, 0, 0)))
    parts.append(b.sphere("hen_comb", 0.03, (0, -0.13, 0.42), 8, (0.4, 1.2, 0.9), _m("#C8402F", 0.5)))
    parts.append(b.sphere("hen_wattle", 0.018, (0, -0.18, 0.32), 8, (0.6, 0.6, 1.2), _m("#C8402F", 0.5)))
    parts.append(b.sphere("hen_tail", 0.08, (0, 0.18, 0.32), 10, (0.6, 0.9, 1.2), white))
    for sx in (-1, 1):
        parts.append(b.sphere("hen_eye", 0.01, (sx * 0.04, -0.16, 0.38), 6, material=_m("#2C2826", 0.3)))
        parts.append(b.cyl("hen_leg", 0.008, 0.12, (sx * 0.04, 0.03, 0.0), 6, material=_m("#E3AE45", 0.5)))
    g = b.group("hen", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def bird(p, loc, colour="#4E6E8E"):
    x, y, z = loc
    p.add(b.sphere("bird", 0.05, loc, 10, (0.8, 1.2, 0.8), _m(colour, 0.7)))
    p.add(b.sphere("bird_head", 0.03, (x, y - 0.05, z + 0.03), 8, material=_m(colour, 0.7)))


# --- town ------------------------------------------------------------------------------------------------------------

def building(p, loc, w, d, floors, wall="#F1E6CF", trim="#2F6E5E", seed=11, roof_garden=True, rounded=True):
    """A simple town block: storeys with window rows, a few balconies with plants, a roof garden."""
    rng = random.Random(seed)
    x, y, z = loc
    fh = 3.0
    h = floors * fh
    parts = [b.box("bldg", (w, d, h), (0, 0, 0), 0.6 if rounded else 0.1, 4, _m(wall, 0.85)),
             b.box("bldg_cap", (w + 0.2, d + 0.2, 0.3), (0, 0, h), 0.15, 3, _m(trim, 0.5))]
    for f in range(1, floors):
        parts.append(b.box("bldg_band", (w + 0.04, d + 0.04, 0.12), (0, 0, f * fh), 0.05, 2, _m(trim, 0.5)))
        nx = int(w / 1.6)
        for i in range(nx):
            wx = -w / 2 + (i + 0.5) * (w / nx)
            lit = rng.random() < 0.35
            win = b.box("bldg_win", (0.8, 0.05, 1.2), (wx, -d / 2 - 0.01, f * fh + 0.7), 0.06, 2,
                        b.lamp(0.6, "lamp_warm") if lit and b._glow >= 1.0 else _m("#5F7F8C", 0.15, 0.3))
            parts.append(win)
            if rng.random() < 0.25:
                parts.append(b.box("balcony", (1.1, 0.5, 0.08), (wx, -d / 2 - 0.25, f * fh + 0.45), 0.03, 2,
                                   _m(trim, 0.5)))
                for k in range(3):
                    parts.append(b.sphere("balcony_plant", 0.13, (wx - 0.35 + k * 0.35, -d / 2 - 0.3, f * fh + 0.62),
                                          10, material=_m("#5E8F4F" if k % 2 else "#3F6B3A", 0.7)))
    if roof_garden:
        for k in range(int(w * d / 6)):
            parts.append(b.sphere("roof_bush", rng.uniform(0.3, 0.6), (rng.uniform(-w / 2 + 0.6, w / 2 - 0.6),
                                                                       rng.uniform(-d / 2 + 0.6, d / 2 - 0.6), h + 0.3),
                                  10, (1, 1, 0.7), _m(rng.choice(["#5E8F4F", "#3F6B3A", "#8FA77E"]), 0.7)))
        parts.append(b.box("solar", (min(2.4, w * 0.4), 1.2, 0.06), (w * 0.2, d * 0.2, h + 0.6), 0.01, 1,
                           _m("#2E3F5C", 0.15, 0.4), rad(25, 0, 0)))
    g = b.group("building", parts, loc)
    p.add(g)
    return g


def greenhouse_dome(p, loc, r=9.0):
    """A big glass dome with ribs: the town's garden, where food grows."""
    x, y, z = loc
    p.add(b.sphere("dome_glass", r, (x, y, z), 40, (1, 1, 0.7), clear_glass("#CFE6E2", 0.28)))
    for k in range(16):
        rib = b.torus("dome_rib", r, 0.08, (x, y, z), 48, 6, _m("#F3E9D2", 0.4), rad(90, 0, 360 / 16 * k), arc=180)
        rib.scale = (1, 1, 1)
        rib.rotation_euler = rad(90, 0, 360 / 32 * k)
        p.add(rib)
    p.add(b.torus("dome_ring", r, 0.15, (x, y, z + 0.05), 48, 6, _m("#2F6E5E", 0.4)))
    rng = random.Random(12)
    for k in range(24):
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0, r * 0.75)
        p.add(b.sphere("dome_tree", rng.uniform(0.8, 1.6), (x + math.cos(a) * d, y + math.sin(a) * d, z + 1.5),
                       10, (1, 1, 1.2), _m(rng.choice(["#5E8F4F", "#3F6B3A", "#8FA77E", "#6E9A4A"]), 0.7)))


def monorail(p, points, height=6.5, train_at=0.5):
    """A beam on pylons along a path, and a three-car capsule train."""
    beam = _m("#F3E9D2", 0.45)
    pts = [Vector((q[0], q[1], 0.0)) for q in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        # The box is centred in X and Y, so placing it at the midpoint and turning it about Z lays it along d.
        seg = b.box("beam", (0.6, d.length, 0.5), ((a.x + c.x) / 2, (a.y + c.y) / 2, height), 0.1, 2, beam)
        seg.rotation_euler = rad(0, 0, -math.degrees(math.atan2(d.x, d.y)))
        p.add(seg)
        n = max(1, int(d.length / 12))
        for k in range(n + 1):
            q = a + d * (k / n)
            p.add(b.cyl("pylon", 0.3, height, (q.x, q.y, 0), 12, material=beam))
    # The train sits on the first segment.
    a, c = pts[0], pts[1]
    d = (c - a).normalized()
    ang = -math.degrees(math.atan2(d.x, d.y))
    perp = Vector((d.y, -d.x, 0))
    for k in range(3):
        q = a + (c - a) * train_at + d * (k * 4.3)
        car = b.capsule("train", 1.1, 4.0, (q.x, q.y, height + 1.6), 20, _m("#E58436", 0.35))
        car.rotation_euler = rad(-90, 0, ang)
        p.add(car)
        for side in (1, -1):
            mid = q + d * 2.0 + perp * (side * 1.08)
            win = b.box("train_win", (0.06, 2.6, 0.42), (mid.x, mid.y, height + 1.62), 0.12, 3,
                        b.lamp(0.8, "lamp_warm") if b._glow >= 1.0 else _m("#3F5F78", 0.15, 0.3))
            win.rotation_euler = rad(0, 0, ang)
            p.add(win)
