"""Round 6 frames for the style lab (spec 046, plan in spec 051): the whole town and its edges, weather and light.

A. the town model in the Town Hall (the plan in 3D, coordinates as in spec 051 and the plan map)
B. the west edge: the Wreck and its night market      C. the south edge: the Wildbelt gate and the free animals
D. Main Street in a thunderstorm                       E. morning fog on the lake: pier, lighthouse, lifeguard drone
"""

import math
import random

import bpy
from mathutils import Vector

from . import build as b
from . import character, climb, concept, future, hero, mess, scene, scene3, scene4, scene5, street, themes, world
from .build import rad
from .future import _m
from .shapes import SOFT, STREAM

scene.TIMES["storm"] = {
    "sun": ("#AEB8D0", 0.6, 25, 40), "ambient": 0.3, "glow": 1.5, "haze": 0.006,
    "sky": ("#1C212A", "#4A505B"),
}
scene.TIMES["fog"] = {
    "sun": ("#FFE2C0", 1.8, 12, -70), "ambient": 0.85, "glow": 1.3, "haze": 0.011,
    "sky": ("#C9CFCB", "#ECE4D4"),
}


def _ss(e0, e1, x):
    """Smoothstep from e0 to e1 (either order)."""
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


# --- the town plan (spec 051), metres, X east, Y north ------------------------------------------------------------

def shore(y):
    return 205.0 + 22.0 * math.sin(y / 70.0)


def town_height(x, y):
    h = 10.0 * _ss(200.0, -60.0, x)                                   # Main Street climbs west from the lake
    h += 6.0 * _ss(-150.0, -185.0, x) * (1 - _ss(-250.0, -275.0, x))  # Tower Rise plateau
    h += 35.0 * _ss(140.0, 235.0, y) * (1 - _ss(190.0, 250.0, x))     # Greenhouse Hill
    h += 45.0 * _ss(-222.0, -300.0, y)                                # hills behind the Wildbelt
    valley = _ss(48.0, 80.0, abs(y - 30.0))
    h += 55.0 * _ss(-245.0, -290.0, x) * valley                       # canyon walls around the west valley
    h += 30.0 * _ss(-315.0, -340.0, x)                                # landslide behind the Wreck
    core = math.hypot(x - 20.0, (y - 10.0) * 1.3) < 170.0
    h += (0.4 if core else 2.2) * math.sin(x / 23.0) * math.cos(y / 31.0) + (0 if core else 1.0) * math.sin((x + y) / 13.0)
    s = shore(y)
    if x > s - 18:
        h = h * (1 - _ss(s - 18, s, x)) - 4.0 * _ss(s - 4, s + 25, x)
    return h


def town_colour(x, y, z, slope):
    if z < -0.2:
        return "#7E8F86"
    if x > shore(y) - 22 and -95 < y < -25:
        return world.SAND
    if slope > 0.45:
        return world.ROCK[int(abs(x * 7 + y * 3)) % 3]
    if y < -222 or y > 232:
        return ("#9BAF7E", "#8DA472", "#A8B886")[int(abs(x + y)) % 3]
    return world.GRASS[int(abs(x * 3 + y * 5)) % 4]


DISTRICTS = [  # number, name, (x, y): the same numbers as the plan map and spec 051
    (1, "Station Square", (-90.0, 30.0)), (2, "Main Street", (-20.0, 20.0)), (3, "Fountain Cross", (40.0, 20.0)),
    (4, "Market Hall", (40.0, 55.0)), (5, "Garden Promenade", (40.0, -60.0)), (6, "Lakefront", (200.0, 20.0)),
    (7, "Homes", (110.0, -60.0)), (8, "Workshop Lane", (-115.0, -55.0)), (9, "Greenhouse Hill", (0.0, 200.0)),
    (10, "Festival Meadow and Arena", (95.0, -160.0)), (11, "Tower Rise", (-205.0, 0.0)),
]


def _on(x, y, dz=0.0):
    return (x, y, town_height(x, y) + dz)


def _shop_block(p, x, y, w, d, h, wall, sign, facing=-1, rng=None):
    """A small shop for the model: a body, an awning on the street side and a lit sign."""
    z = min(town_height(x + sx, y + sy) for sx in (-w / 2, w / 2) for sy in (-d / 2, d / 2)) - 0.6
    p.add(b.box("shop", (w, d, h + 0.6), (x, y, z), 0.4, 2, _m(wall, 0.85)))
    p.add(b.box("shop_cap", (w + 0.3, d + 0.3, 0.5), (x, y, z + h + 0.6), 0.2, 2, _m(sign, 0.5)))
    p.add(b.box("awning", (w * 0.9, 1.6, 0.4), (x, y + facing * (d / 2 + 0.8), z + 3.6), 0.1, 1, _m(sign, 0.6)))
    p.add(b.box("shop_win", (w * 0.8, 0.2, 2.0), (x, y + facing * (d / 2 + 0.05), z + 1.0), 0.05, 1, b.window()))


def town(p, detail=1.0, lots=True):
    """The whole bowl: terrain, lake, districts, edges. Returns nothing; everything lands in the scene."""
    rng = random.Random(51)
    world.terrain("land_town", town_height, -340.0, 330.0, -310.0, 260.0, 7.0, seed=51, colour=town_colour, skirt=-22.0)
    world.water(p, 150.0, 330.0, -310.0, 260.0, 0.0, "#6FA3B0", 0.9)
    pave = _m("#E2D5BC", 0.85)
    road = _m("#C9BBA2", 0.9)
    # Main Street from Station Square down to the lake, Promenade across it, squares.
    for k in range(24):
        x = -75.0 + k * 10.0
        p.add(b.box("main_street", (10.4, 12.0, 0.8), _on(x, 20.0, -0.5), 0, 1, pave))
    for k in range(26):
        y = -125.0 + k * 10.0
        if 12 < y < 28:
            continue
        p.add(b.box("promenade", (14.0, 10.4, 0.8), _on(40.0, y, -0.5), 0, 1, _m("#CFD9B8", 0.9)))
    p.add(b.cyl("fountain_cross", 16.0, 0.9, _on(40.0, 20.0, -0.5), 32, material=pave))
    future.fountain(p, _on(40.0, 20.0, 0.3), 6.0)
    p.add(b.box("station_square", (46.0, 40.0, 1.0), _on(-95.0, 30.0, -0.6), 2.0, 2, pave))
    # Shops along Main Street (both sides), the Fountain Cross corner restaurant.
    walls, signs = future.WALLS, ("#E58436", "#4F9A85", "#C8553D", "#E3AE45", "#9A7AA8", "#2F6E5E")
    for k, x in enumerate(range(-62, 175, 13)):
        if abs(x - 40) < 14:
            continue
        for side, yy in ((-1, 32.5), (1, 7.5)):
            if rng.random() < 0.9:
                _shop_block(p, x + rng.uniform(-1, 1), yy, 11.0, 11.0, rng.uniform(6, 10), rng.choice(walls),
                            rng.choice(signs), -side)
    for (x, y) in ((25.0, 38.0), (55.0, 2.0)):
        _shop_block(p, x, y, 18.0, 16.0, 11.0, "#F1E6CF", "#C8553D", -1 if y > 20 else 1)
    # Market Hall: a long barrel roof.
    mz = town_height(40.0, 55.0)
    p.add(b.box("market_body", (44.0, 22.0, 6.0), (40.0, 58.0, mz - 0.5), 0.5, 2, _m("#F3E9D2", 0.8)))
    roof = b.cyl("market_roof", 11.0, 44.0, (18.0, 58.0, mz + 5.5), 24, material=_m("#4F9A85", 0.5), rot=rad(0, 90, 0))
    roof.scale = (0.55, 1, 1)
    p.add(roof)
    # Station: a raised hall under the monorail; the Town Hall with a clock tower; the museum and statue.
    sz = town_height(-95.0, 30.0)
    p.add(b.box("station", (34.0, 14.0, 8.0), (-95.0, 46.0, sz), 1.5, 3, _m("#EFC9A9", 0.8)))
    p.add(b.box("townhall", (24.0, 16.0, 14.0), (-118.0, 18.0, sz - 0.5), 1.0, 3, _m("#F1E6CF", 0.8)))
    p.add(b.cyl("townhall_tower", 3.2, 14.0, (-118.0, 18.0, sz + 13.5), 16, material=_m("#2F6E5E", 0.5)))
    p.add(b.cyl("townhall_clock", 2.2, 0.4, (-118.0, 14.8, sz + 23.0), 20, material=_m("#F3E9D2", 0.4),
                rot=rad(90, 0, 0)))
    p.add(b.box("museum", (20.0, 14.0, 9.0), (-82.0, 4.0, sz - 0.5), 0.5, 2, _m("#E2D5BC", 0.8)))
    for k in range(5):
        p.add(b.cyl("museum_col", 0.7, 7.0, (-90.0 + k * 4.0, -3.4, sz), 10, material=_m("#F3E9D2", 0.7)))
    # Homes north and south of Main Street (gaps for the monorail and paths).
    for k in range(int(26 * detail)):
        while True:
            x, y = rng.uniform(-60.0, 170.0), rng.choice((rng.uniform(78.0, 130.0), rng.uniform(-110.0, -14.0)))
            if abs(x - 40.0) > 14 and not (abs(y - 75.0) < 7) and not (60 < x < 190 and -190 < y < -125) and \
                    abs(x - 190.0) > 8 and x < shore(y) - 28:
                break
        w, d = rng.uniform(12, 20), rng.uniform(10, 14)
        concept.building(p, _on(x, y, -1.0), w, d, rng.randint(2, 4), rng.choice(walls), rng.choice(future.TRIMS),
                         seed=600 + k)
    # Workshop Lane: sheds with saw-tooth roofs, the garage with cars outside.
    for k in range(4):
        x, y = -135.0 + k * 16.0, -55.0
        z = town_height(x, y) - 0.5
        p.add(b.box("shed", (14.0, 22.0, 6.0), (x, y, z), 0.3, 2, _m("#D9E3E6", 0.8)))
        for j in range(3):
            p.add(b.prism("shed_tooth", [(-7.0, 0.0), (7.0, 0.0), (7.0, 3.0)], 6.0, (x, y - 8.0 + j * 7.0, z + 6.0),
                          _m("#2E3F5C", 0.6), rad(90, 0, 0)))
    for k in range(5):
        future.hover_car(p, _on(-140.0 + k * 9.0, -35.0, 0.6), 90, future.CARS[k % len(future.CARS)])
    # Greenhouse Hill: terraces of long glasshouses, the big dome, the compost works, the funicular.
    for row in range(4):
        y = 160.0 + row * 18.0
        for k in range(6):
            x = -120.0 + k * 34.0 + row * 6
            if (x - (-10.0)) ** 2 + (y - 215.0) ** 2 < 40 ** 2:
                continue
            z = min(town_height(x + dx, y) for dx in (-13, 0, 13)) - 0.4
            p.add(b.box("glasshouse", (26.0, 9.0, 4.0), (x, y, z), 0.3, 2, hero.clear_glass("#CFE6E2", 0.5)))
            p.add(b.box("glasshouse_beds", (25.0, 8.0, 1.0), (x, y, z + 0.1), 0, 1, _m("#5E8F4F", 0.8)))
    concept.greenhouse_dome(p, _on(-10.0, 215.0, -1.0), 26.0)
    for k in range(3):
        p.add(b.cyl("silo", 5.0, 14.0, _on(130.0 + k * 11.0, 168.0, -1.0), 16, material=_m("#E3AE45", 0.6)))
    p.add(b.box("compost_hall", (36.0, 14.0, 7.0), _on(140.0, 150.0, -1.0), 0.5, 2, _m("#8FA77E", 0.8)))
    a, c = Vector(_on(40.0, 140.0, 1.0)), Vector(_on(20.0, 198.0, 1.0))
    d = c - a
    track = b.box("funicular", (2.0, d.length, 0.8), tuple((a + c) / 2 - Vector((0, 0, 0.4))), 0, 1, _m("#8E8174", 0.7))
    track.rotation_euler = d.to_track_quat("Y", "Z").to_euler()
    p.add(track)
    p.add(b.box("funicular_car", (3.2, 6.0, 3.4), tuple(a + d * 0.45), 0.6, 2, _m("#C8553D", 0.5)))
    # Promenade trees and beds.
    for k in range(int(40 * detail)):
        y = rng.uniform(-125.0, 135.0)
        if abs(y - 20.0) < 22:
            continue
        future.garden_tree(p, _on(40.0 + rng.choice((-1, 1)) * rng.uniform(4.0, 6.5), y), 300 + k, rng.uniform(5, 8))
    # Festival Meadow with tents and the arena.
    for k in range(7):
        x, y = 20.0 + k * 9.0, -150.0 + (k % 2) * 10.0
        p.add(b.lathe("tent", [(4.0, 0.0), (4.0, 2.0), (0.0, 6.0)], 8, _on(x, y, -0.3),
                      _m(("#E58436", "#F3E9D2", "#4F9A85", "#E3AE45")[k % 4], 0.6), smooth=False))
    az = town_height(130.0, -160.0)
    p.add(b.lathe("arena", [(30.0, 0.0), (33.0, 0.0), (36.0, 12.0), (33.0, 13.0), (24.0, 3.0), (24.0, 0.0)], 40,
                  (130.0, -160.0, az - 1.0), _m("#F1E6CF", 0.7), scale=(1.0, 0.75, 1.0)))
    p.add(b.cyl("arena_field", 24.0, 0.5, (130.0, -160.0, az - 0.6), 40, material=_m("#7E9B6A", 0.9)))
    # Lakefront: the pier, the lighthouse, the beach umbrellas, the ferry.
    world.pier(p, 196.0, 288.0, 0.0, 8.0, 1.2)
    world.lighthouse(p, (292.0, 0.0, 1.2), 22.0)
    for k in range(8):
        x, y = shore(-60.0) - 10.0 + rng.uniform(-6, 4), -85.0 + k * 7.5
        p.add(b.lathe("parasol", [(0.0, 0.8), (2.5, 0.0), (0.0, 0.6)], 8, _on(x, y, 2.2), _m(signs[k % 6], 0.6),
                      smooth=False))
    world.ferry(p, (262.0, 60.0, 0.4), 20)
    for k in range(10):
        world.buoy(p, (shore(-60.0) + 18.0, -95.0 + k * 7.0, 0.1))
    # Tower Rise: five towers on the plateau, the tallest with the Sky Deck.
    for k, (x, y, kind, h) in enumerate(((-215.0, 0.0, "needle", 120.0), (-200.0, -65.0, "ribbed", 85.0),
                                         (-190.0, 65.0, "round", 75.0), (-232.0, -120.0, "slab", 60.0),
                                         (-170.0, 115.0, "ribbed", 65.0))):
        future.tower(p, _on(x, y, -1.0), kind, h, 10.0 if kind != "needle" else 13.0, seed=6100 + k)
    # The town monorail loop and the line out through the hill.
    loop = [(-95.0, 46.0), (60.0, 75.0), (190.0, 75.0), (190.0, -140.0), (70.0, -195.0), (-95.0, -140.0),
            (-95.0, 46.0)]
    concept.monorail(p, loop, 26.0, 0.3)
    concept.monorail(p, [(-95.0, 46.0), (-150.0, 205.0)], 26.0, 0.8)
    portal_z = town_height(-150.0, 205.0)
    p.add(b.torus("tunnel_portal", 6.0, 1.2, (-150.0, 200.0, max(portal_z - 8.0, 26.0)), 20, 6, _m("#B9B3A8", 0.8),
                  rad(90, 0, 18), arc=180))
    concept.monorail(p, [(190.0, 75.0), (330.0, 130.0)], 26.0, 0.9)
    # The Wildbelt: hedge with its gate, free animals in the meadow beyond.
    world.hedge(p, (-250.0, -215.0), (205.0, -215.0), 5.0, 3.0, seed=52, gate_at=250.0, gate_w=6.0)
    world.gate(p, _on(0.0, -215.0, 0.0), 0, 5.0, 4.0)
    world.lookout(p, _on(18.0, -205.0, 0.0), 0)
    for k in range(int(30 * detail)):
        x, y = rng.uniform(-220.0, 180.0), rng.uniform(-300.0, -228.0)
        kind = rng.random()
        loc = _on(x, y)
        if kind < 0.45:
            world.cow(p, loc, rng.uniform(0, 360), k, rng.random() < 0.5)
        elif kind < 0.7:
            world.pig(p, loc, rng.uniform(0, 360), k)
        else:
            world.deer(p, loc, rng.uniform(0, 360), k)
    for k in range(int(60 * detail)):
        x, y = rng.uniform(-300.0, 200.0), rng.uniform(-305.0, -230.0)
        future.garden_tree(p, _on(x, y, -0.3), 400 + k, rng.uniform(6, 10))
    p.add(b.box("barn", (18.0, 12.0, 7.0), _on(-90.0, -265.0, -1.0), 0.5, 2, _m("#C8553D", 0.8)))
    p.add(b.prism("barn_roof", [(-9.5, 0.0), (9.5, 0.0), (0.0, 5.0)], 12.6, _on(-90.0, -258.7, 5.5),
                  _m("#5E554D", 0.7), rad(90, 0, 0)))
    # The west edge: the Wreck across the valley, the old highway ending in mid-air.
    world.wreck(p, (-298.0, -45.0, town_height(-298.0, 30.0) - 2.0), 90, 150.0, 18.0, lit=b._glow >= 1.0)
    world.overpass_stub(p, (-262.0, 125.0, town_height(-262.0, 125.0) - 2.0), -90, 44.0,
                        town_height(-262.0, 82.0) + 14.0 - town_height(-262.0, 125.0))
    # Trees on the hills and slopes; boulders at the cliff feet.
    for k in range(int(140 * detail)):
        x, y = rng.uniform(-330.0, 200.0), rng.uniform(-200.0, 255.0)
        if math.hypot(x - 20.0, (y - 10.0) * 1.3) < 150 or x > shore(y) - 25:
            continue
        if (x + 205) ** 2 + y ** 2 < 25 ** 2 or (-150 < x < 30 and 150 < y < 240):
            continue
        future.garden_tree(p, _on(x, y, -0.3), 800 + k, rng.uniform(6, 11))
    for k in range(int(40 * detail)):
        x, y = rng.uniform(-330.0, -240.0), rng.choice((rng.uniform(-80, -20), rng.uniform(80, 140)))
        world.rock(p, _on(x, y, -1.0), rng.uniform(4, 9), seed=900 + k)
    # Numbered district pins for the model (match the plan map).
    if lots:
        for n, name, (x, y) in DISTRICTS:
            z = town_height(x, y)
            p.add(b.cyl("pin_post", 0.9, 40.0, (x, y, z), 8, material=_m("#2F3A35", 0.6)))
            disc = b.cyl("pin_disc", 12.0, 2.0, (x, y, z + 52.0), 24, edge=0.4, material=_m("#F3E9D2", 0.5),
                         rot=rad(90, 0, 0))
            disc.rotation_euler = rad(90, 0, 40)
            p.add(disc)
            p.add(b.text("pin_num", str(n), 14.0, (x + 1.4, y - 1.6, z + 52.0), rad(90, 0, 40), _m("#C8553D", 0.5),
                         0.6))


# --- A. the town model in the Town Hall -----------------------------------------------------------------------------

def frame_model(time_id):
    p = b.Parts()
    before = set(bpy.data.objects)
    town(p, 1.0)
    root = bpy.data.objects.new("model_root", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in set(bpy.data.objects) - before:
        if obj is not root and obj.parent is None and obj.type in ("MESH", "EMPTY", "FONT"):
            obj.parent = root
    s = 0.01
    root.scale = (s, s, s)
    root.location = (0.0, 0.0, 1.12)
    # The table, the room, the people around the model.
    p.add(b.box("plinth", (7.0, 6.0, 0.9), (-0.05, -0.25, 0.0), 0.05, 2, _m("#8A5A3B", 0.6)))
    p.add(b.box("plinth_top", (7.2, 6.2, 0.04), (-0.05, -0.25, 0.9), 0.02, 1, _m("#2F6E5E", 0.5)))
    p.add(b.box("floor", (24.0, 20.0, 0.1), (0, 0, -0.1), 0, 1, _m("#E2D5BC", 0.7)))
    for k in range(60):
        rng = random.Random(k)
        p.add(b.cyl("terrazzo", rng.uniform(0.03, 0.08), 0.005, (rng.uniform(-10, 10), rng.uniform(-9, 9), 0.0), 6,
                    material=_m(rng.choice(("#C8553D", "#4F9A85", "#E3AE45", "#F3E9D2")), 0.5)))
    wall = _m("#EFE3CC", 0.85)
    p.add(b.box("hall_wall", (24.0, 0.3, 7.0), (0, 6.5, 0), 0, 1, wall))
    p.add(b.box("hall_wall", (0.3, 20.0, 7.0), (-8.5, 0, 0), 0, 1, wall))
    p.add(b.box("hall_band", (24.0, 0.32, 0.25), (0, 6.5, 2.6), 0, 1, _m("#2F6E5E", 0.5)))
    for k, x in enumerate((-5.0, -1.0, 3.0)):
        p.add(b.box("hall_window", (2.6, 0.32, 3.2), (x, 6.5, 2.9), 0.6, 3, b.mat("lamp_warm", emit=1.6, rough=0.3)))
    p.add(b.text("hall_title", "TOWN PLAN", 0.55, (0.0, 6.3, 6.3), rad(90, 0, 0), _m("#2F6E5E", 0.5), 0.03))
    p.add(b.text("hall_sub", "places to rent: ask at the desk", 0.25, (0.0, 6.3, 5.75), rad(90, 0, 0),
                 _m("#5B4A3A", 0.5), 0.02))
    looks = scene3._looks()
    character.person(looks["cook"], (3.9, -2.6, 0.0), 70, "cook")
    character.person(looks["older_woman"], (2.4, -3.7, 0.0), 20, "woman")
    concept.crowd([(-3.0, -3.8, -10), (-4.2, 1.5, -100), (0.8, 3.2, 175)], seed=860)
    concept.robot_runner(p, (4.6, 1.6, 0.0), 120, tray=False)
    scene.area_light("hall_key", (-1.5, 1.0, 5.6), (3.0, 3.0), "#FFF1DC", 420)
    scene.area_light("hall_fill", (6.0, -6.0, 3.5), (4.0, 3.0), "#FFE2C2", 160, rad(60, 0, 40))
    cam = scene.camera((4.6, -5.2, 4.3), (-0.5, 0.5, 1.0), lens=26)
    cam.data.clip_start = 0.05


# --- B. the west edge: the Wreck ---------------------------------------------------------------------------------------

def _valley_height(x, y):
    h = 42.0 * _ss(52.0, 92.0, abs(y)) * _ss(30.0, -20.0, x)
    h += 26.0 * _ss(-40.0, -90.0, x)
    h += 0.6 * math.sin(x / 9.0) * math.cos(y / 11.0)
    return h


def frame_wreck(time_id):
    p = b.Parts()
    world.terrain("land_valley", _valley_height, -220.0, 160.0, -170.0, 170.0, 4.0, seed=6,
                  colour=lambda x, y, z, s: world.ROCK[int(abs(x + y * 3)) % 3] if s > 0.35 else
                  world.GRASS[int(abs(x * 3 + y)) % 4])
    world.wreck(p, (-2.0, -76.0, -1.5), 90, 150.0, 17.0, seed=7, lit=True)
    world.overpass_stub(p, (-30.0, 112.0, 30.0), -90, 40.0, 12.0)
    path = _m("#D9C9AE", 0.9)
    p.add(b.box("path", (90.0, 7.0, 0.3), (60.0, -30.0, -0.2), 0, 1, path))
    # The market spilling out of the belly: stalls, string lights to poles, people walking in.
    ox, oy = 15.0, -76.0 + 150.0 * 0.52 * 0.55
    for k in range(5):
        y = oy - 10.0 + k * 5.0
        col = ("#E58436", "#4F9A85", "#E3AE45", "#C8553D", "#9A7AA8")[k]
        p.add(b.box("stall", (2.6, 2.0, 1.0), (ox + 5.0, y, 0.0), 0.08, 2, _m("#B98B5E", 0.8)))
        p.add(b.box("stall_roof", (3.0, 2.4, 0.15), (ox + 5.0, y, 2.4), 0.05, 1, _m(col, 0.6), rad(0, -8, 0)))
        for sy in (-1.0, 1.0):
            p.add(b.cyl("stall_post", 0.05, 2.4, (ox + 5.0 - 1.3, y + sy, 0.0), 6, material=_m("#7A5638", 0.8)))
            p.add(b.cyl("stall_post", 0.05, 2.4, (ox + 5.0 + 1.3, y + sy, 0.0), 6, material=_m("#7A5638", 0.8)))
        mess.crate(p, (ox + 6.6, y + 0.4, 0.0), 10 * k, seed=60 + k)
    for k in range(4):
        px, py = ox + 13.0, oy - 12.0 + k * 8.0
        p.add(b.cyl("light_pole", 0.08, 5.0, (px, py, 0.0), 8, material=_m("#2C2826", 0.6)))
        concept.string_lights(p, (px, py, 5.0), (ox - 1.0, oy - 12.0 + k * 8.0 + 2.0, 8.0), 16, 0.8,
                              ("#FFD9A0", "#FF9A7A", "#BFE8D8"), 4.0)
    p.add(b.box("market_sign", (6.0, 0.25, 1.4), (ox + 14.0, oy + 16.0, 2.4), 0.15, 2, _m("#2F6E5E", 0.5),
                rad(0, 0, 90)))
    p.add(b.text("market_text", "WRECK MARKET", 0.62, (ox + 14.2, oy + 16.0, 3.3), rad(90, 0, 90),
                 b.lamp(2.5, "lamp_warm"), 0.02))
    p.add(b.text("market_text2", "fridays after dark", 0.36, (ox + 14.2, oy + 16.0, 2.75), rad(90, 0, 90),
                 _m("#F3E9D2", 0.5), 0.02))
    for x in (ox + 14.0,):
        p.add(b.cyl("sign_post", 0.1, 2.4, (x, oy + 13.5, 0.0), 8, material=_m("#2C2826", 0.6)))
        p.add(b.cyl("sign_post", 0.1, 2.4, (x, oy + 18.5, 0.0), 8, material=_m("#2C2826", 0.6)))
    walkers = [(ox + 20.0, oy - 3.0, 95), (ox + 22.0, oy + 2.0, 80), (ox + 27.0, oy - 6.0, 100),
               (ox + 9.0, oy - 1.0, 60), (ox + 8.0, oy + 4.0, 120), (ox + 31.0, oy + 1.0, 85)]
    concept.crowd(walkers, seed=870)
    concept.dog(p, (ox + 24.0, oy - 1.0, 0.0), 95)
    # Rocks at the cliff feet, trees on the ledges, the last houses of town on the right.
    rng = random.Random(61)
    for k in range(40):
        y = rng.choice((rng.uniform(-110, -66), rng.uniform(68, 110)))
        world.rock(p, (rng.uniform(-60, 40), y, _valley_height(0, y) * 0.3), rng.uniform(2, 6), seed=700 + k)
    for k in range(30):
        x, y = rng.uniform(-80, 40), rng.choice((rng.uniform(-150, -85), rng.uniform(85, 150)))
        future.garden_tree(p, (x, y, _valley_height(x, y) - 0.3), 720 + k, rng.uniform(5, 8))
    for k in range(18):
        x, y = rng.uniform(-200, -90), rng.uniform(-90, 90)
        world.rock(p, (x, y, _valley_height(x, y) - 2.0), rng.uniform(5, 12), seed=760 + k)
    for i, (x, y) in enumerate(((70.0, -58.0), (88.0, -60.0), (72.0, 18.0))):
        concept.building(p, (x, y, 0.0), 14.0, 10.0, 3, future.WALLS[i], future.TRIMS[i], seed=880 + i)
    street.street_lamp(STREAM, (40.0, -26.0, 0.0))
    street.street_lamp(STREAM, (60.0, -26.0, 0.0))
    future.airship(p, (-120.0, 60.0, 95.0), 30, 50, "", "far_")
    future.drone(p, (30.0, -20.0, 9.0), 200, scale=1.4)
    scene.add_sun(time_id, azimuth_offset=95)
    scene._night_extras(time_id, lamps=[(40.0, -26.0, 3.2), (60.0, -26.0, 3.2)])
    scene.point_light("market_glow", (ox + 4.0, oy, 3.0), "#FFC98A", 2500, 2.0)
    for k, x in enumerate((30.0, 42.0)):
        p.add(b.cyl("path_pole", 0.07, 4.2, (x, -26.5, 0.0), 8, material=_m("#2C2826", 0.6)))
        p.add(b.cyl("path_pole", 0.07, 4.2, (x, -33.5, 0.0), 8, material=_m("#2C2826", 0.6)))
        concept.string_lights(p, (x, -26.5, 4.2), (x, -33.5, 4.2), 10, 0.5, ("#FFD9A0", "#FF9A7A", "#BFE8D8"), 4.0)
    concept.crowd([(40.0, -29.0, 95), (46.0, -31.5, 85), (51.0, -28.5, 100)], seed=875)
    cam = scene.camera((60.0, -31.0, 1.8), (0.0, -22.0, 12.0), lens=22)
    scene5._wide_world(cam, 0.5)
    h = bpy.data.objects.get("haze")
    if h:
        h.location = (-40.0, 0.0, -20.0)


# --- C. the south edge: the Wildbelt -------------------------------------------------------------------------------

def _belt_height(x, y):
    h = 0.0 if y < 12 else 2.0 * _ss(12.0, 60.0, y) + 30.0 * _ss(90.0, 220.0, y)
    return h + 0.5 * math.sin(x / 7.0) * math.cos(y / 9.0) * _ss(10.0, 25.0, y) + 6.0 * _ss(120, 220, y) * math.sin(x / 30.0)


def frame_wildbelt(time_id):
    p = b.Parts()
    world.terrain("land_belt", _belt_height, -160.0, 160.0, -30.0, 260.0, 3.0, seed=8,
                  colour=lambda x, y, z, s: (world.PATH if abs(x) < 2.2 and y < 10 else
                                             ("#A8B886", "#9BAF7E", "#B3C08C", "#8DA472")[int(abs(x * 3 + y)) % 4]))
    world.hedge(p, (-90.0, 10.0), (90.0, 10.0), 3.6, 2.6, seed=81, gate_at=90.0, gate_w=4.4)
    world.gate(p, (0.0, 10.0, 0.0), 0, 4.0, 3.4)
    world.ranger_bot(p, (-3.2, 8.2, 0.0), 20)
    world.lookout(p, (6.5, -1.5, 0.0), 0, 3.8)
    looks = scene3._looks()
    character.person(looks["older_woman"], (4.9, -1.0, 4.0), 25, "woman")
    concept.crowd([(-1.8, 5.0, 170)], seed=884)
    future.bench(p, (-6.0, 4.0, 0.0), 180)
    p.add(b.box("belt_sign", (1.6, 0.1, 1.1), (3.6, 7.0, 0.9), 0.06, 2, _m("#F3E9D2", 0.6)))
    p.add(b.cyl("belt_sign_post", 0.06, 1.0, (3.6, 7.05, 0.0), 6, material=_m("#7A5638", 0.8)))
    p.add(b.text("belt_sign_text", "please be quiet", 0.16, (3.6, 6.93, 1.62), rad(90, 0, 0), _m("#2F6E5E", 0.5),
                 0.005))
    p.add(b.text("belt_sign_text2", "they live here", 0.16, (3.6, 6.93, 1.36), rad(90, 0, 0), _m("#5B4A3A", 0.5),
                 0.005))
    for k in range(5):
        world.bush(p, (-12.0 + k * 1.3 if k < 3 else 10.0 + k, 6.5, 0.0), 0.7, 40 + k, 0.8)
    # Beyond the hedge: the meadow, a pond, the old barn now a shelter, trees, and the animals living there.
    world.water(p, 18.0, 52.0, 48.0, 70.0, _belt_height(35.0, 59.0) + 0.15, "#7FB0BC", 0.9, "pond")
    bx, by = -38.0, 92.0
    bz = _belt_height(bx, by) - 0.6
    p.add(b.box("barn", (16.0, 11.0, 6.0), (bx, by, bz), 0.3, 2, _m("#C8553D", 0.8)))
    p.add(b.prism("barn_roof", [(-8.6, 0.0), (8.6, 0.0), (0.0, 4.6)], 11.6, (bx, by + 5.8, bz + 6.0),
                  _m("#5E554D", 0.7), rad(90, 0, 0)))
    p.add(b.box("barn_door", (4.0, 0.2, 4.4), (bx, by - 5.6, bz), 0.1, 1, _m("#F3E9D2", 0.7)))
    for k in range(10):
        world.bush(p, (bx - 8 + k * 1.8, by - 6.2, bz + 0.4), 0.6, 60 + k, 1.0)
    rng = random.Random(82)
    herd = [("cow", -12.0, 24.0), ("cow", -6.0, 28.0, True), ("cow", -17.0, 33.0, True), ("cow", 6.0, 38.0),
            ("pig", 9.0, 21.0), ("pig", 11.5, 22.5), ("pig", 7.5, 24.5), ("deer", 30.0, 46.0), ("deer", 33.0, 49.0),
            ("cow", 22.0, 30.0, True), ("cow", -28.0, 44.0), ("deer", -40.0, 70.0), ("cow", 0.0, 58.0, True),
            ("pig", -24.0, 22.0), ("deer", 18.0, 75.0)]
    for k, a in enumerate(herd):
        kind, x, y = a[0], a[1], a[2]
        loc = (x, y, _belt_height(x, y))
        yaw = rng.uniform(0, 360)
        if kind == "cow":
            world.cow(p, loc, yaw, 90 + k, len(a) > 3)
        elif kind == "pig":
            world.pig(p, loc, yaw, 90 + k)
        else:
            world.deer(p, loc, yaw, 90 + k)
    for k in range(5):
        concept.hen(p, (rng.uniform(-6, 6), rng.uniform(18, 26), _belt_height(0, 22)), rng.uniform(0, 360))
    for k in range(70):
        x, y = rng.uniform(-150, 150), rng.uniform(70, 250)
        if abs(x - 35) < 22 and y < 90:
            continue
        world.bush(p, (x, y, _belt_height(x, y) - 1.0), rng.uniform(3.0, 6.0), 820 + k, 0.0)
    for k in range(10):
        x, y = rng.uniform(-100, 100), rng.uniform(25, 60)
        if abs(x) < 25:
            continue
        future.garden_tree(p, (x, y, _belt_height(x, y) - 0.3), 900 + k, rng.uniform(4, 7))
    for k, (x, z) in enumerate(((-6.0, 14.0), (-4.5, 15.2), (-3.0, 13.6), (14.0, 18.0), (15.4, 18.6))):
        concept.bird(p, (x, 40.0, z), "#4E6E8E" if k % 2 else "#2F3A35")
    # Town side: the path, a street lamp, a drone turning back at the hedge.
    street.street_lamp(SOFT, (-3.4, -4.0, 0.0))
    future.drone(p, (2.0, 6.0, 7.0), 160, scale=1.2)
    scene.add_sun(time_id, azimuth_offset=30)
    world.pig(p, (0.8, 11.6, 0.0), 175, 777)
    world.cow(p, (-2.2, 15.5, 0.0), 160, 778)
    cam = scene.camera((7.4, -3.3, 5.9), (0.0, 42.0, 0.0), lens=24)
    scene5._wide_world(cam, 0.6)


# --- D. Main Street in a thunderstorm --------------------------------------------------------------------------------

def _delete_tree(obj):
    for c in list(obj.children):
        _delete_tree(c)
    bpy.data.objects.remove(obj, do_unlink=True)


def frame_storm(time_id):
    scene5.frame_skyline(time_id)
    p = b.Parts()
    # Drones are grounded in a storm (spec 051): remove the flying ones, keep three parked on a roof.
    for obj in [o for o in bpy.data.objects if o.parent is None and o.name.split(".")[0] in ("drone", "far_drone")]:
        _delete_tree(obj)
    for k in range(3):
        future.drone(p, (-11.6 + k * 0.9, 4.2, 7.9), 90, package=False)
    # Wet streets: darker, glossy pavement and road; puddles that mirror the neon.
    for m in bpy.data.materials:
        if m.name.startswith(("pavement", "road", "kerb", "cream")) and m.node_tree:
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Roughness"].default_value = 0.12
                c = bsdf.inputs["Base Color"].default_value
                bsdf.inputs["Base Color"].default_value = (c[0] * 0.55, c[1] * 0.55, c[2] * 0.6, 1.0)
    for k, (x, y, r) in enumerate(((-1.0, -4.6, 0.9), (3.5, -6.5, 1.3), (-5.0, -8.0, 1.0), (1.5, -2.2, 0.5),
                                   (7.5, -3.6, 0.7))):
        mess.puddle(p, (x, y, 0.004), r, seed=100 + k)
    # Umbrellas, a courier robot on the ground instead of a drone, a newspaper over someone's head.
    world.umbrella(p, (2.6, -1.6, 0.0), "#E58436")
    world.umbrella(p, (-3.0, -1.9, 0.0), "#4F9A85", tilt=(10, -6))
    concept.crowd([(-6.5, -2.0, 120), (8.6, -2.3, -100)], seed=890)
    world.umbrella(p, (-6.5, -2.0, 0.0), "#E3AE45")
    world.umbrella(p, (8.6, -2.3, 0.0), "#C8553D", tilt=(-8, 4))
    concept.robot_runner(p, (5.4, -3.6, 0.0), -70, tray=False)
    mess.cardboard_box(p, (5.4, -3.6, 1.05), (0.4, 0.3, 0.3), 20, False, 910, "#58")
    # The storm: a cloud bank, a lightning bolt into the rod on a needle tower, rain.
    world.clouds(p, (0.0, 300.0, 105.0), 26, (340.0, 70.0), (28.0, 48.0), "#2E343E", seed=91)
    world.clouds(p, (0.0, 230.0, 88.0), 12, (260.0, 30.0), (16.0, 26.0), "#3A414C", seed=92)
    for obj in [o for o in bpy.data.objects if o.parent is None and o.name.split(".")[0] == "hovercar"
                and o.location.y < 10.0]:
        _delete_tree(obj)
    sx, sy = -14.0, 30.0
    top = future.tower(p, (sx, sy, 0.0), "needle", 24.0, 5.0, seed=4242)
    p.add(b.cyl("rod", 0.15, 6.0, (sx, sy, top), 8, material=b.chrome()))
    world.lightning([(sx - 22.0, sy + 34.0, 95.0), (sx, sy, top + 6.0)], seed=93, width=0.4, strength=200.0,
                    flash=1.5e6, branches=4)
    world.rain(((-14.0, 22.0), (-12.0, 30.0), (-0.5, 22.0)), 14000, 0.75, seed=94, alpha=0.32)
    world.rain(((-80.0, 80.0), (30.0, 140.0), (0.0, 80.0)), 9000, 3.0, seed=95, alpha=0.18)
    ee = bpy.context.scene.eevee
    if hasattr(ee, "use_raytracing"):
        ee.use_raytracing = True


# --- E. morning fog on the lake ----------------------------------------------------------------------------------------

def _lake_height(x, y):
    s = 18.0 + 4.0 * math.sin(y / 25.0)
    h = 2.2 * _ss(s, -30.0, x) + 0.3 * math.sin(x / 6.0) * math.cos(y / 8.0)
    return h * (1 - _ss(s - 6, s, x)) - 3.0 * _ss(s, s + 25, x)


def frame_fog(time_id):
    p = b.Parts()
    world.terrain("land_lake", _lake_height, -60.0, 400.0, -160.0, 200.0, 4.0, seed=9,
                  colour=lambda x, y, z, s: world.SAND if -1.0 < z < 1.0 else
                  ("#7E8F86" if z <= -1.0 else world.GRASS[int(abs(x + y * 3)) % 4]))
    world.water(p, 5.0, 400.0, -160.0, 200.0, 0.0, "#8FB0B4", 0.92)
    p.add(b.box("prom", (14.0, 220.0, 2.35), (0.0, 40.0, 0.0), 0, 1, _m("#E2D5BC", 0.85)))
    world.pier(p, 6.0, 78.0, 0.0, 6.0, 2.05)
    world.lighthouse(p, (82.0, 0.0, 2.0), 17.0, beam_target=(160.0, 80.0, 4.0))
    for k in range(16):
        world.buoy(p, (28.0 + k * 3.4, -9.5, 0.1), "#E58436" if k % 2 else "#F3E9D2")
    world.lifeguard_drone(p, (21.0, -6.5, 4.6), 30)
    world.ferry(p, (128.0, 30.0, 0.4), -25)
    # The cart on the pier: pitch C5, with the first guests of the day.
    Z = 2.35
    climb.food_cart((12.0, -1.0, Z), 0, "#4F9A85", seed=3)
    looks = scene3._looks()
    character.person(looks["cook"], (12.2, 0.3, Z), 180, "cook")
    character.person(looks["older_woman"], (11.4, -2.4, Z), -10, "woman")
    for o in concept.crowd([(13.2, -2.8, 20), (4.0, -2.2, 70), (9.0, 1.8, -120)], seed=900):
        o.location.z = Z
    concept.steam(p, (12.4, -1.0, Z + 1.0), 12, seed=901, height=0.9)
    for k, y in enumerate((-12.0, 12.0, 36.0, 60.0)):
        street.street_lamp(STREAM, (5.5, y, Z))
    for y in (-6.0, 20.0, 44.0):
        future.bench(p, (3.5, y, Z), -90)
    for k, x in enumerate(range(18, 78, 12)):
        street.street_lamp(SOFT, (x, 2.8, Z))
    # Behind the promenade: the lakefront houses, faint in the fog; the far shore as ghost towers.
    rng = random.Random(95)
    for i, y in enumerate(range(18, 150, 17)):
        concept.building(p, (-6.0 - (i % 2) * 3.0, y, Z), 12.0, 10.0, rng.randint(2, 4), rng.choice(future.WALLS),
                         rng.choice(future.TRIMS), seed=950 + i)
    for k in range(6):
        future.garden_tree(p, (-3.0 + (k % 2), 10.0 + k * 16.0, Z), 990 + k, 4.5)
    for k in range(9):
        future.tower(p, (rng.uniform(280.0, 380.0), rng.uniform(-80.0, 180.0), 0.0), rng.choice(("round", "ribbed")),
                     rng.uniform(50, 90), rng.uniform(7, 10), seed=960 + k, pre="far_")
    for k, (x, z) in enumerate(((24.0, 7.0), (25.4, 7.6), (27.0, 6.6))):
        concept.bird(p, (x, -8.0, z), "#5B6964")
    scene.add_sun(time_id, azimuth_offset=-5)
    scene._night_extras(time_id, lamps=[(5.5, -12.0, 5.7), (5.5, 12.0, 5.7), (18.0, 2.8, 5.6), (30.0, 2.8, 5.6)])
    cam = scene.camera((1.5, -8.5, 4.3), (40.0, 14.0, 3.6), lens=24)
    cam.data.clip_end = 1500.0
    sc = bpy.context.scene
    if hasattr(sc.eevee, "volumetric_end"):
        sc.eevee.volumetric_end = 400.0
    h = bpy.data.objects.get("haze")
    if h:
        h.scale = (4.0, 3.0, 1.0)
        h.location = (150.0, 20.0, -2.0)


FRAMES = {
    "model": (frame_model, "interior", 0.4, 0.75),
    "wreck": (frame_wreck, "sunset", 0.7, 1.0),
    "wildbelt": (frame_wildbelt, "morning", 0.5, 1.0),
    "storm": (frame_storm, "storm", 0.8, 1.0),
    "fog": (frame_fog, "fog", 0.6, 1.0),
}


def render(name, path, samples=64, scale=1.0, ink=True):
    fn, time_id, grit, ink_w = FRAMES[name]
    b.reset("P1")
    b.set_grit(grit)
    b.set_glow(scene.TIMES[time_id]["glow"])
    scene.setup_render(samples, scale)
    scene.setup_world(time_id)
    fn(time_id)
    if ink:
        scene4.illustration_look(ink_w * (1.1 if scale >= 1 else 0.7))
        scene4._no_ink_for(["haze", "far_", "land", "water", "pond"])
        if name in ("fog", "storm"):
            style = bpy.context.view_layer.freestyle_settings.linesets[0].linestyle
            fade = style.alpha_modifiers.new("fade", "DISTANCE_FROM_CAMERA")
            fade.range_min, fade.range_max = (12.0, 70.0) if name == "fog" else (25.0, 160.0)
            fade.invert = True
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
