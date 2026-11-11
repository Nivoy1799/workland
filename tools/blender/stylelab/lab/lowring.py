"""Round 8 (spec 046, spec 051 section 5c): props for the Low Ring, the shaded ground floors of Haven Nine.

Soot black and rust, orange sodium lamps, signs with dead letters, broken robots from the 2130s, scrap, overflowing
bins, dream booths. All the vices are made up (betting on drone races, credit slots, dream booths).
"""

import math
import random

from . import build as b
from . import concept, mess
from .build import rad
from .future import _m

SOOT = ("#2B2826", "#34302C", "#3B3632", "#24221F")
RUST = ("#7A4A30", "#8A5A3B", "#6B3F2A")
SODIUM = "#FFA040"


def sodium_lamp(p, loc, h=4.6, yaw=0.0, lit=True, broken=False, glow=1.0):
    """A bent mast with an orange sodium head. broken=True leaves the head dark and cracked."""
    x, y, z = loc
    pole = _m("#3A3734", 0.6, 0.5)
    p.add(b.cyl("sodium_pole", 0.09, h, loc, 8, material=pole))
    arm = b.cyl("sodium_arm", 0.06, 1.2, (x, y, z + h - 0.1), 8, material=pole, rot=rad(0, 70, yaw))
    p.add(arm)
    hx = x + math.cos(math.radians(yaw)) * 1.1
    hy = y + math.sin(math.radians(yaw)) * 1.1
    head_m = _m(SODIUM, 0.4, emit=9.0 * glow) if lit and not broken else _m("#4A4540", 0.3, 0.3)
    p.add(b.sphere("sodium_head", 0.22, (hx, hy, z + h + 0.25), 12, (1.6, 1, 0.6), head_m))
    return (hx, hy, z + h + 0.1)


def neon(p, loc, text, colour, size=0.5, yaw=0.0, dead=(), glow=1.0, backing="#1C1A18"):
    """A neon sign on a dark backing. Letters at the indices in dead are off (a dim tube)."""
    x, y, z = loc
    w = len(text) * size * 0.62 + size
    p.add(b.box("neon_back", (w, 0.12, size * 1.7), (x, y, z - size * 0.85), 0.06, 1, _m(backing, 0.8),
                rad(0, 0, yaw)))
    lit = _m(colour, 0.4, emit=12.0 * glow)
    off = _m("#4A4440", 0.5)
    step = size * 0.62
    x0 = -(len(text) - 1) * step / 2
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for k, ch in enumerate(text):
        if ch == " ":
            continue
        dx = x0 + k * step
        t = b.text("neon_ch", ch, size, (x + c * dx + s * 0.08, y + s * dx - c * 0.08, z), rad(90, 0, yaw),
                   off if k in dead else lit, 0.02)
        t.visible_shadow = False
        p.add(t)
    return w


def screen(p, loc, w, h, colour="#5FD3C4", yaw=0.0, glow=1.0, bars=5, seed=1, text=""):
    """A betting screen: a glowing panel with race bars (odds) and a title."""
    rng = random.Random(seed)
    x, y, z = loc
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    p.add(b.box("screen", (w, 0.15, h), (x, y, z), 0.03, 1, _m("#0E1618", 0.3, emit=0.6 * glow), rad(0, 0, yaw)))
    for k in range(bars):
        L = w * rng.uniform(0.25, 0.8)
        bz = z + h * 0.15 + k * h * 0.7 / bars
        dx = -w / 2 + 0.1 + L / 2
        p.add(b.box("screen_bar", (L, 0.02, h * 0.08), (x + c * dx + s * 0.09, y + s * dx - c * 0.09, bz), 0, 1,
                    _m(rng.choice((colour, "#E3AE45", "#E58AA6")), 0.4, emit=8.0 * glow), rad(0, 0, yaw)))
    if text:
        t = b.text("screen_txt", text, h * 0.11, (x + s * 0.1, y - c * 0.1, z + h * 0.9), rad(90, 0, yaw),
                   _m("#F3E9D2", 0.4, emit=6.0 * glow), 0.005)
        t.visible_shadow = False
        p.add(t)


def broken_runner(p, loc, yaw=0.0, lean=24.0, seed=3, screen_text="WELCOME TO HAVEN NIN", glow=1.0):
    """An old serving robot slumped against a wall: one arm gone, a cracked dome, its screen still looping an ad."""
    rng = random.Random(seed)
    parts = []
    body = _m("#B9AE98", 0.7)
    parts.append(b.sphere("brunner_ball", 0.12, (0, 0, 0.12), 14, material=_m("#2C2826", 0.6)))
    parts.append(b.lathe("brunner_body", [(0.0, 0.18), (0.16, 0.2), (0.2, 0.5), (0.17, 0.9), (0.12, 1.0), (0.0, 1.02)],
                         20, (0, 0, 0), body))
    parts.append(b.torus("brunner_band", 0.195, 0.02, (0, 0, 0.55), 20, 6, _m(RUST[0], 0.8)))
    parts.append(b.sphere("brunner_head", 0.16, (0, 0, 1.15), 16, (1, 0.9, 0.85), _m("#7E8C8C", 0.2, alpha=0.6)))
    parts.append(b.box("brunner_screen", (0.2, 0.02, 0.1), (0, -0.13, 1.1), 0.01, 1, _m("#5FD3C4", 0.3, emit=5 * glow)))
    t = b.text("brunner_txt", screen_text, 0.014, (0, -0.142, 1.1), rad(90, 0, 0), _m("#0E1618", 0.4), 0.001)
    parts.append(t)
    for k in range(3):  # cracks across the glass dome
        parts.append(b.box("brunner_crack", (0.004, 0.004, 0.12), (-0.04 + k * 0.04, -0.15, 1.18), 0, 1,
                           _m("#1E1E1F", 0.5), rad(0, 30 - k * 25, 0)))
    parts.append(b.capsule("brunner_arm", 0.025, 0.35, (0.17, 0.0, 0.85), 8, b.chrome(), rad(-15, 0, 0)))
    parts.append(b.cyl("brunner_stump", 0.03, 0.05, (-0.19, 0, 0.85), 8, material=_m("#4A4540", 0.5), rot=rad(0, 90, 0)))
    for k in range(4):  # rust streaks and a loose wire
        parts.append(b.box("brunner_rust", (0.03, 0.01, rng.uniform(0.1, 0.25)),
                           (rng.uniform(-0.12, 0.12), -0.19, rng.uniform(0.3, 0.8)), 0, 1, _m(RUST[k % 3], 0.9)))
    parts.append(b.cyl("brunner_wire", 0.006, 0.3, (-0.2, 0, 0.85), 5, material=_m("#C8553D", 0.5), rot=rad(0, 140, 0)))
    g = b.group("brunner", parts, loc, rad(lean, 0, yaw))
    p.add(g)
    return g


def scrap_pile(p, loc, r=2.2, n=40, seed=5):
    """Robot parts, panels, pipes and a few heads in a heap."""
    rng = random.Random(seed)
    x, y, z = loc
    mats = [_m(c, 0.8) for c in SOOT + RUST] + [_m("#B9AE98", 0.7), b.chrome(), _m("#4F9A85", 0.7)]
    for k in range(n):
        a = rng.uniform(0, 6.28)
        d = r * math.sqrt(rng.random())
        h = (r - d) * 0.55
        px, py, pz = x + math.cos(a) * d, y + math.sin(a) * d, z + rng.uniform(0, h)
        kind = rng.random()
        rot = rad(rng.uniform(0, 360), rng.uniform(0, 360), rng.uniform(0, 360))
        if kind < 0.4:
            p.add(b.box("scrap_panel", (rng.uniform(0.3, 0.9), rng.uniform(0.2, 0.6), 0.04), (px, py, pz), 0.01, 1,
                        rng.choice(mats), rot))
        elif kind < 0.65:
            p.add(b.cyl("scrap_pipe", rng.uniform(0.03, 0.08), rng.uniform(0.4, 1.2), (px, py, pz), 8,
                        material=rng.choice(mats), rot=rot))
        elif kind < 0.82:
            p.add(b.sphere("scrap_head", 0.16, (px, py, pz + 0.1), 12, (1, 0.9, 0.85), _m("#7E8C8C", 0.3, alpha=0.7)))
        else:
            p.add(b.torus("scrap_wheel", 0.14, 0.04, (px, py, pz), 14, 5, _m("#2C2826", 0.7), rot))


def overflow_bin(p, loc, yaw=0.0, seed=7):
    """A kitchen scrap bin nobody emptied: lid up, peels and bags round it, a smell line of flies."""
    rng = random.Random(seed)
    x, y, z = loc
    p.add(b.box("bin", (0.8, 0.6, 1.0), loc, 0.06, 2, _m("#2F6E5E", 0.8), rad(0, 0, yaw)))
    p.add(b.box("bin_lid", (0.82, 0.62, 0.05), (x, y + 0.32, z + 1.25), 0.02, 1, _m("#2F6E5E", 0.8), rad(70, 0, yaw)))
    for k in range(10):
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0.4, 1.0)
        p.add(b.sphere("bin_waste", rng.uniform(0.06, 0.16), (x + math.cos(a) * d, y + math.sin(a) * d, z + 0.04), 8,
                       (1, 1, 0.5), _m(rng.choice(("#6B7A3A", "#8A6A3A", "#3A3632", "#B9A26A")), 0.9)))
    for k in range(3):
        p.add(b.sphere("bin_bag", 0.25, (x + rng.uniform(-0.6, 0.6), y - 0.55, z + 0.2), 10, (1, 1, 0.8),
                       _m("#1E1E1F", 0.3)))
    p.add(b.sphere("bin_heap", 0.38, (x, y, z + 1.0), 10, (1, 0.8, 0.5), _m("#6B7A3A", 0.9)))


def dream_booth(p, loc, yaw=0.0, glow=1.0, asleep=True, seed=9):
    """A coin-op pod with a glass lid and a blue glow. Somebody lies inside, dreaming."""
    parts = []
    parts.append(b.box("booth_base", (2.2, 0.9, 0.7), (0, 0, 0), 0.2, 2, _m("#3B3632", 0.6, 0.3)))
    parts.append(b.capsule("booth_lid", 0.45, 2.0, (-1.0, 0, 0.7), 16, _m("#9FC3CF", 0.1, alpha=0.4), rad(0, 90, 0)))
    parts.append(b.box("booth_glow", (2.0, 0.7, 0.05), (0, 0, 0.7), 0.1, 1, _m("#6E8BFF", 0.3, emit=5.0 * glow)))
    parts.append(b.box("booth_meter", (0.3, 0.1, 0.4), (1.2, -0.3, 0.5), 0.03, 1, _m("#E58AA6", 0.4, emit=6 * glow)))
    if asleep:
        parts.append(b.capsule("booth_body", 0.17, 1.4, (-0.75, 0, 0.82), 10, _m("#5B5048", 0.7), rad(0, 90, 0)))
        parts.append(b.sphere("booth_head", 0.13, (0.85, 0, 0.88), 12, material=_m("#C99878", 0.6)))
        parts.append(b.box("booth_visor", (0.1, 0.24, 0.07), (0.92, 0, 0.92), 0.02, 1, _m("#6E8BFF", 0.3,
                                                                                       emit=8 * glow)))
    g = b.group("booth", parts, loc, rad(0, 0, yaw))
    p.add(g)
    return g


def pipes(p, a, c, z, n=3, r=0.09, seed=11):
    """Rusty pipes along a wall from a to c at height z, with brackets and a drip stain."""
    rng = random.Random(seed)
    ax, ay = a
    cx, cy = c
    L = math.hypot(cx - ax, cy - ay)
    ang = math.degrees(math.atan2(cy - ay, cx - ax))
    for k in range(n):
        m = _m(RUST[k % 3] if k else "#5A5550", 0.8, 0.3)
        p.add(b.cyl("pipe", r * (1.4 if k == 0 else 1.0), L, (ax, ay, z + k * 0.26), 10, material=m,
                    rot=rad(0, 90, ang)))
    for k in range(int(L / 2.0)):
        t = (k + 0.5) / int(L / 2.0)
        p.add(b.box("pipe_bracket", (0.06, 0.3, 0.9), (ax + (cx - ax) * t, ay + (cy - ay) * t, z - 0.2), 0, 1,
                    _m("#3A3734", 0.6, 0.5), rad(0, 0, ang)))


def vent_steam(p, loc, h=3.0, seed=13):
    """Steam from a grate: big soft puffs rising and spreading."""
    rng = random.Random(seed)
    x, y, z = loc
    p.add(b.box("grate", (1.0, 0.6, 0.04), loc, 0, 1, _m("#3A3734", 0.5, 0.6)))
    for k in range(14):
        t = k / 14
        p.add(b.sphere("steam_puff", 0.25 + 0.7 * t, (x + rng.uniform(-0.3, 0.3) + t * 0.8, y + rng.uniform(-0.3, 0.3),
                                                      z + 0.2 + t * h), 10,
                       material=_m("#C9C2B8", 0.9, alpha=0.32 - 0.22 * t)))
