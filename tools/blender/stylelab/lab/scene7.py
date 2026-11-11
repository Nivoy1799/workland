"""Round 7 frames for the style lab (spec 046, plan in spec 051): the Canopy, the four seasons, three Wrecks.

A. the whole town in the rain from the air: dry under the Canopy, wet outside
B. Main Street under the Canopy in a thunderstorm: the bolt hits a rod on the glass, the café stays open
C-F. one promenade in spring, summer, autumn and winter, looking out past the Canopy's rim to Greenhouse Hill
G-I. the west edge, three tries: the Albatross as Ark Flight 9, the Pelican freight plane, the Stella starliner
"""

import math
import random

import bpy
from mathutils import Vector

from . import build as b
from . import canopy, character, climb, concept, future, hero, mess, scene, scene3, scene4, scene5, scene6, street
from . import themes, world, wrecks
from .build import rad
from .canopy import Canopy
from .future import _m
from .shapes import SOFT, STREAM

scene.TIMES["rain"] = {
    "sun": ("#DDE3EA", 1.1, 50, -30), "ambient": 0.95, "glow": 0.9, "haze": 0.004,
    "sky": ("#6F7A85", "#B3BAC0"), "sun_angle": 30.0,
}
scene.TIMES["autumn"] = {
    "sun": ("#FFC98A", 3.2, 20, -60), "ambient": 0.7, "glow": 0.9, "haze": 0.003,
    "sky": ("#8FB0C8", "#F2D3A8"),
}
scene.TIMES["winter"] = {
    "sun": ("#DCE6F2", 1.5, 14, -40), "ambient": 0.95, "glow": 1.4, "haze": 0.004,
    "sky": ("#A9B6C2", "#E4E6E6"), "sun_angle": 20.0,
}


def _shade(hex_, f):
    h = hex_.lstrip("#")
    r, g, bb = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return "#%02X%02X%02X" % (int(r * f), int(g * f), int(bb * f * 1.04 if f < 1 else bb * f))


# --- A. the town in the rain, from the air ----------------------------------------------------------------------------

TOWN_CANOPY = ((35.0, 25.0), 140.0, 95.0, 40.0, 58.0)


def frame_canopy(time_id):
    p = b.Parts()
    cv = Canopy(*TOWN_CANOPY, panel=11.0)
    dry = scene6.town_colour

    def wet(x, y, z, s):
        c = dry(x, y, z, s)
        return c if cv.inside(x, y) or z < -0.2 else _shade(c, 0.74)

    scene6.town_colour = wet
    try:
        scene6.town(p, 1.0, lots=False)
    finally:
        scene6.town_colour = dry
    cv.build(p, seed=7, rods=8)
    cv.columns(p, scene6.town_height, every=34.0, inner=[(40.0, -12.0), (-45.0, 45.0), (115.0, 48.0)])
    canopy.rain_around(cv, ((-340.0, 330.0), (-310.0, 260.0), (0.0, 120.0)), 24000, 7.0, 0.14, seed=71, alpha=0.22)
    world.clouds(p, (-40.0, 360.0, 150.0), 22, (420.0, 50.0), (30.0, 55.0), "#5B636D", seed=72)
    world.clouds(p, (-330.0, 0.0, 140.0), 10, (40.0, 260.0), (24.0, 40.0), "#646C76", seed=73)
    scene.add_sun(time_id, azimuth_offset=20)
    cam = scene.camera((350.0, -370.0, 255.0), (10.0, 35.0, 8.0), lens=30)
    scene5._wide_world(cam, 0.45)
    h = bpy.data.objects.get("haze")
    if h:
        h.scale = (7.0, 7.0, 5.0)
        h.location = (0.0, 0.0, -20.0)


# --- B. Main Street under the Canopy in a thunderstorm ------------------------------------------------------------------

def _hill_far(x, y):
    return 34.0 * scene6._ss(150.0, 330.0, y) + 2.0 * math.sin(x / 19.0) * scene6._ss(140.0, 200.0, y)


def frame_storm(time_id):
    p = b.Parts()
    world.terrain("land_hill", _hill_far, -260.0, 260.0, 30.0, 420.0, 9.0, seed=74,
                  colour=lambda x, y, z, s: world.GRASS[int(abs(x * 3 + y)) % 4] if s < 0.4 else world.ROCK[0])
    street.ground(p, -60, 60)
    hero.shop_front(STREAM, themes.default_theme("P1"), (0, 0, 0))
    for x, th in ((-6.6, themes.mex_theme()), (6.6, themes.jap_theme())):
        hero.shop_front(STREAM, th, (x, 0, 0))
    bakery = dict(themes.default_theme("P1"), name="GREEN BAKERY", sign="#C8553D", door="#E3AE45", props=None)
    flowers = dict(themes.default_theme("P1"), name="FLOWER SHOP", sign="#9A7AA8", door="#8CC5AC", props=None)
    hero.shop_front(SOFT, bakery, (-13.2, 0, 0))
    hero.shop_front(SOFT, flowers, (13.2, 0, 0))
    rng = random.Random(75)
    for k in range(10):
        future.garden_tree(p, (rng.uniform(-24, 24), rng.uniform(8, 20), 0.0), 60 + k, rng.uniform(4.0, 6.0))
    # The rest of the core behind the shops, then the rim, then the hill outside in the rain.
    for k in range(26):
        x, y = rng.uniform(-110.0, 110.0), rng.uniform(28.0, 125.0)
        if abs(x + 12.0) < 9 and y < 40:
            continue
        concept.building(p, (x, y, 0.0), rng.uniform(12, 18), rng.uniform(10, 14), rng.randint(2, 4),
                         rng.choice(future.WALLS), rng.choice(future.TRIMS), seed=7500 + k)
    concept.greenhouse_dome(p, (-30.0, 300.0, _hill_far(-30.0, 300.0) - 1.0), 26.0)
    for k in range(30):
        x, y = rng.uniform(-240.0, 240.0), rng.uniform(150.0, 400.0)
        future.garden_tree(p, (x, y, _hill_far(x, y) - 0.3), 7600 + k, rng.uniform(6, 10), "far_")
    cv = Canopy((0.0, 40.0), 125.0, 95.0, 30.0, 46.0, panel=7.0)
    cv.build(p, seed=3, rods=5)
    cv.columns(p, lambda x, y: 0.0, every=26.0, inner=[(-12.0, 14.0), (60.0, 30.0)])
    # The storm above the glass: clouds, rain on the vault and outside it, the bolt into a rod on the glass.
    world.clouds(p, (0.0, 260.0, 120.0), 26, (340.0, 80.0), (28.0, 48.0), "#2E343E", seed=91)
    world.clouds(p, (0.0, 120.0, 105.0), 14, (200.0, 40.0), (18.0, 28.0), "#3A414C", seed=92)
    canopy.rain_around(cv, ((-150.0, 150.0), (-40.0, 260.0), (0.0, 110.0)), 36000, 2.6, 0.035, seed=94, alpha=0.3)
    rx, ry = 6.0, 62.0
    rz = cv.z(rx, ry)
    p.add(b.cyl("strike_rod", 0.16, 5.0, (rx, ry, rz), 8, material=b.chrome()))
    world.lightning([(rx + 14.0, ry + 70.0, 125.0), (rx, ry, rz + 5.0)], seed=93, width=0.35, strength=200.0,
                    flash=1.2e6, branches=4)
    canopy.strike_glow(cv, (rx, ry), branches=6, strength=70.0, seed=5)
    # Under the glass it is dry: no umbrellas, the café tables stay out, a board says so.
    looks = scene3._looks()
    character.person(looks["cook"], (1.6, -0.6, 0.0), -15, "cook")
    character.person(looks["older_woman"], (2.6, -1.6, 0.0), -60, "woman")
    character.person(looks["younger"], (-3.0, -1.9, 0.0), 35, "younger")
    concept.crowd([(-6.5, -2.0, 120), (8.6, -2.3, -100), (-9.5, -6.0, 80)], seed=890)
    future.umbrella_table(p, (10.2, -1.4, 0.0), "#4F9A85", seed=893, guests=2)
    concept.robot_sweeper(p, (-1.2, -2.9, 0.0), 40)
    board = [b.box("aboard", (0.7, 0.06, 0.9), (0, 0, 0.0), 0.03, 1, _m("#2F3A35", 0.6), rad(-10, 0, 0))]
    for k, line in enumerate(("STORM TODAY", "canopy shut", "tables open")):
        board.append(b.text("aboard_txt", line, 0.09 if k else 0.11, (0, -0.1, 0.72 - k * 0.17), rad(80, 0, 0),
                            _m("#F3E9D2", 0.6), 0.003))
    p.add(b.group("aboard", board, (4.6, -3.0, 0.0), rad(0, 0, -15)))
    for k in range(3):  # drones are grounded in a storm: parked by the flower shop
        future.drone(p, (11.6 + k * 0.9, -0.9, 0.25), 90, package=False)
    street.street_lamp(STREAM, (-3.3, -2.9, 0.0))
    street.street_lamp(STREAM, (4.0, -2.9, 0.0))
    scene.add_sun(time_id, azimuth_offset=-10)
    scene._night_extras(time_id, lamps=[(-3.3, -2.9, 3.17), (4.0, -2.9, 3.17), (0.0, 1.0, 2.6), (-6.6, 1.0, 2.6),
                                        (6.6, 1.0, 2.6)])
    cam = scene.camera((6.0, -16.5, 1.6), (-1.0, 40.0, 21.0), lens=19)
    scene5._wide_world(cam, 0.35)


# --- C-F. the four seasons -----------------------------------------------------------------------------------------------

SEASON_TIME = {"spring": "morning", "summer": "clear", "autumn": "autumn", "winter": "winter"}
BANNERS = {"spring": ("BLOSSOM FAIR  SATURDAY", "#E58AA6"), "summer": ("LAKE NIGHTS  FRIDAYS", "#4F9A85"),
           "autumn": ("LANDING DAY  100 YEARS", "#C8553D"), "winter": ("WINTER LIGHTS  AT THE WRECK", "#2E3F5C")}
FLOWERS = {"spring": ("#E58AA6", "#F2D27A", "#F3E9D2", "#C8553D", "#9A7AA8"), "summer": future.FLOWERS,
           "autumn": ("#E3AE45", "#C8553D", "#9A7AA8", "#B5652E"), "winter": ("#3F5F4A",)}


def _hill(x, y):
    return 36.0 * scene6._ss(58.0, 240.0, y) + 2.5 * math.sin(x / 21.0) * scene6._ss(55.0, 110.0, y)


def _season(season):
    canopy.use_season(season)
    future.FLOWERS = FLOWERS[season]
    p = b.Parts()
    rng = random.Random(77)
    cv = Canopy((0.0, -45.0), 110.0, 92.0, 24.0, 40.0, panel=6.5)
    rim_y = -45.0 + 92.0  # where the rim crosses the promenade
    snow = season == "winter"

    def colour(x, y, z, s):
        if snow and y > rim_y - 1.0:
            return ("#F7F9FA", "#EEF2F5", "#E6ECF0")[int(abs(x * 3 + y)) % 3] if s < 0.5 else world.ROCK[1]
        if s > 0.5:
            return world.ROCK[int(abs(x + y)) % 3]
        if abs(x) < 3.0 and rim_y < y < rim_y + 45.0:
            return world.PATH
        return canopy.GROUND[season][int(abs(x * 3 + y * 5)) % 4]

    world.terrain("land_hill", _hill, -200.0, 200.0, 30.0, 380.0, 6.0, seed=78, colour=colour)
    pave = _m("#E2D5BC", 0.85)
    p.add(b.box("pave", (70.0, 90.0, 0.2), (0.0, rim_y - 46.0, -0.18), 0, 1, pave))
    p.add(b.box("walk", (5.2, 60.0, 0.2), (0.0, rim_y - 30.0, -0.16), 0, 1, _m("#C9A47E", 0.85)))
    for k in range(30):
        p.add(b.box("walk_tile", (5.2, 0.12, 0.2), (0.0, -12.0 + k * 2.0, -0.155), 0, 1, _m("#B8916A", 0.85)))
    grass = _m(canopy.GROUND[season if season != "winter" else "summer"][1], 0.9)
    for sx in (-1, 1):
        p.add(b.box("verge", (3.4, 44.0, 0.2), (sx * 7.5, 26.0, -0.14), 0.2, 1, grass))
    # The promenade: beds, trees, benches, houses either side.
    for k, y in enumerate(range(8, 44, 8)):
        for sx in (-1, 1):
            future.garden_bed(p, (sx * 3.6, y, 0.0), 1.4, 5.0, 0.5, seed=780 + k * 2 + (sx > 0),
                              tree=False)
            canopy.tree(p, (sx * 7.5, y + 3.0, 0.0), season, seed=790 + k * 2 + (sx > 0), h=rng.uniform(5.0, 6.5))
    for k, y in enumerate((-4.0, 12.0, 28.0)):
        future.bench(p, (-5.6, y, 0.0), 90)
    for i, y in enumerate((-2.0, 13.0, 28.0, 41.0)):
        for sx in (-1, 1):
            concept.building(p, (sx * 21.0, y, 0.0), 12.0, 13.0, 3 + (i + (sx > 0)) % 2,
                             future.WALLS[(i * 2 + (sx > 0)) % len(future.WALLS)],
                             future.TRIMS[(i + (sx > 0)) % len(future.TRIMS)], seed=800 + i * 2 + (sx > 0))
    # The food cart at the front with its first guests.
    climb.food_cart((4.6, 2.2, 0.0), 0, "#4F9A85", seed=3)
    looks = scene3._looks()
    character.person(looks["cook"], (4.8, 3.5, 0.0), 180, "cook")
    character.person(looks["older_woman"], (3.8, 0.6, 0.0), -10, "woman")
    concept.crowd([(5.6, 0.4, 20), (-1.5, 6.0, 170), (1.0, 18.0, 190), (-2.0, 30.0, 10)], seed=780)
    if season == "winter":
        concept.steam(p, (4.8, 2.2, 1.0), 14, seed=781, height=1.1)
    # The Canopy: closed in autumn and winter, panels slid open in spring and summer.
    open_frac = {"spring": 0.3, "summer": 0.6, "autumn": 0.0, "winter": 0.0}[season]
    cv.build(p, seed=79, open_frac=open_frac, snow_frac=0.85 if snow else 0.0, rods=4)
    cv.columns(p, _hill, every=22.0)
    banner_a, banner_c = (-9.0, 19.0), (9.0, 19.0)
    text, col = BANNERS[season]
    canopy.banner(p, banner_a, banner_c, 6.2, text, col)
    for x, y in (banner_a, banner_c):
        p.add(b.cyl("banner_pole", 0.08, 6.3, (x, y, 0.0), 8, material=_m("#2F6E5E", 0.5)))
    # Outside the rim: Greenhouse Hill in its season.
    for row in range(3):
        y = 95.0 + row * 28.0
        for k in range(5):
            x = -90.0 + k * 42.0 + row * 9.0
            if abs(x + 30.0) < 30 and row == 2:
                continue
            z = min(_hill(x + dx, y) for dx in (-13, 0, 13)) - 0.4
            p.add(b.box("glasshouse", (26.0, 9.0, 4.0), (x, y, z), 0.3, 2, hero.clear_glass("#CFE6E2", 0.5)))
            p.add(b.box("glasshouse_beds", (25.0, 8.0, 1.0), (x, y, z + 0.1), 0, 1,
                        _m("#5E8F4F" if season != "winter" else "#6E8F6A", 0.8)))
    concept.greenhouse_dome(p, (-30.0, 190.0, _hill(-30.0, 190.0) - 1.0), 26.0)
    p.add(b.box("compost_works", (30.0, 14.0, 8.0), (70.0, 120.0, _hill(70.0, 120.0) - 0.8), 0.6, 2,
                _m("#8FA77E", 0.8)))
    p.add(b.text("compost_sign", "COMPOST WORKS", 2.2, (70.0, 112.9, _hill(70.0, 120.0) + 5.4), rad(90, 0, 0),
                 _m("#F3E9D2", 0.6), 0.05))
    for k in range(70):
        x, y = rng.uniform(-190.0, 190.0), rng.uniform(rim_y + 6.0, 360.0)
        if abs(x) < 6 and y < rim_y + 40:
            continue
        if rng.random() < (0.55 if snow else 0.3):
            canopy.conifer(p, (x, y, _hill(x, y) - 0.3), 820 + k, rng.uniform(7, 11), snow, "far_" if y > 130 else "")
        else:
            canopy.tree(p, (x, y, _hill(x, y) - 0.2), season, 900 + k, rng.uniform(5.5, 8.0),
                        "far_" if y > 130 else "", snow)
    # The season's own things.
    if season == "spring":
        pts = [(rng.uniform(-9, 9), rng.uniform(-10, 45), 0.01) for _ in range(1400)]
        canopy.confetti("petals", pts, 0.05, canopy.PETALS, seed=11)
        for k in range(6):
            concept.bird(p, (rng.uniform(-6, 6), rng.uniform(20, 40), rng.uniform(8, 14)), "#4E6E8E")
    if season == "summer":
        for k, (x, y) in enumerate(((-2.0, -2.5), (9.5, 6.0), (-10.5, 16.0))):
            future.umbrella_table(p, (x, y, 0.0), ("#E58436", "#E3AE45", "#C8553D")[k], seed=850 + k)
        future.drone(p, (-3.0, 10.0, 11.0), 30)
        future.drone(p, (6.0, 30.0, 16.0), -120)
    if season == "autumn":
        pts = [(rng.uniform(-30, 30), rng.uniform(rim_y + 1, rim_y + 40), _hill(0, rim_y + 1) + 0.02)
               for _ in range(2600)]
        pts += [(rng.uniform(-10, 10), rng.uniform(-8, 46), 0.01) for _ in range(500)]
        pts = [(x, y, max(z, _hill(x, y) + 0.03)) for x, y, z in pts]
        canopy.confetti("leaves", pts, 0.09, canopy.LEAF_FALL, seed=12)
        glass_pts = []
        while len(glass_pts) < 4200:
            x, y = rng.uniform(-110, 110), rng.uniform(-60, 47)
            if 0.45 < cv.q(x, y) < 1.0:
                glass_pts.append((x, y, cv.z(x, y) + 0.12))
        canopy.confetti("leaves_on_glass", glass_pts, 0.35, canopy.LEAF_FALL, seed=13)
        # The leaf chute: the gutter sends the leaves down a glass pipe into the hopper, then up to the works.
        cx, cy = -9.5, cv.cy + cv.ry * math.sqrt(1 - (9.5 / cv.rx) ** 2) - 0.6
        p.add(b.cyl("far_chute", 0.75, cv.rim_z - 3.0, (cx, cy, 2.6), 16,
                    material=_m("#DCEDEE", 0.06, 0.2, alpha=0.3)))
        chute = [(cx + rng.uniform(-0.5, 0.5), cy - 0.2 + rng.uniform(-0.5, 0.5), rng.uniform(2.8, cv.rim_z - 0.6))
                 for _ in range(600)]
        canopy.confetti("chute_leaves", chute, 0.12, canopy.LEAF_FALL, seed=14, upright=True)
        p.add(b.box("hopper", (3.4, 2.4, 2.6), (cx, cy - 0.4, 0.0), 0.3, 2, _m("#4F9A85", 0.5)))
        p.add(b.text("hopper_txt", "LEAVES TO SOIL", 0.32, (cx, cy - 1.62, 1.8), rad(90, 0, 0),
                     _m("#F3E9D2", 0.6), 0.01))
        p.add(b.text("hopper_txt2", "compost works on the hill", 0.17, (cx, cy - 1.62, 1.35), rad(90, 0, 0),
                     _m("#F3E9D2", 0.6), 0.005))
        for k, (x, y) in enumerate(((-3.0, 40.0), (2.5, 34.0))):
            concept.robot_sweeper(p, (x, y, 0.0), 160 + 40 * k, seed=60 + k)
            for j in range(5):
                p.add(b.sphere("leaf_heap", 0.35, (x + rng.uniform(-0.6, 0.6), y + 0.9 + rng.uniform(-0.3, 0.3), 0.1),
                               8, (1.2, 1.0, 0.5), _m(rng.choice(canopy.LEAF_FALL), 0.8)))
        for k in range(4):
            mess.crate(p, (6.2 + (k % 2) * 0.6, 1.5 + (k // 2) * 0.45, 0.0), 10 * k, seed=70 + k)
            for j in range(3):
                p.add(b.sphere("squash", 0.12, (6.2 + (k % 2) * 0.6 + (j - 1) * 0.14, 1.5 + (k // 2) * 0.45, 0.36),
                               10, (1.1, 1.1, 0.8), _m(("#E58436", "#E3AE45", "#B5652E")[j], 0.6)))
    if season == "winter":
        canopy.rain_around(cv, ((-60.0, 60.0), (rim_y - 4.0, 160.0), (0.0, 40.0)), 9000, 0.0, 0.12, seed=15,
                           hex_="#FFFFFF", alpha=0.85, flakes=True, name="far_snow")
        canopy.snowman(p, (7.0, rim_y + 8.0, _hill(7.0, rim_y + 8.0)), -160)
        for k, y in enumerate((-2.0, 10.0, 22.0, 34.0)):
            concept.string_lights(p, (-7.5, y, 4.6), (7.5, y + 1.5, 4.6), 18, 0.5,
                                  ("#FFD9A0", "#FF9A7A", "#BFE8D8"), 3.0)
    lamps = []
    for y in (-2.0, 14.0, 30.0):
        street.street_lamp(STREAM, (-4.6, y, 0.0))
        lamps.append((-4.6, y, 3.17))
    return lamps


def _season_frame(season):
    def frame(time_id):
        lamps = _season(season)
        scene.add_sun(time_id, azimuth_offset=-20)
        scene._night_extras(time_id, lamps=lamps if season == "winter" else [])
        cam = scene.camera((2.4, -5.5, 1.7), (0.0, 60.0, 10.0), lens=24)
        scene5._wide_world(cam, 0.25 if season == "winter" else 0.5)
    return frame


# --- G-I. the west edge, three tries ---------------------------------------------------------------------------------

def _ark(*a, **k):
    return _ORIGINAL_WRECK(*a, ark=True, **k)


_ORIGINAL_WRECK = world.wreck


def _wreck_frame(fn, extras=None):
    def frame(time_id):
        world.wreck = fn
        try:
            scene6.frame_wreck(time_id)
        finally:
            world.wreck = _ORIGINAL_WRECK
        if extras:
            extras()
        # Further back than round 6, so the whole ship and its silhouette fit: that is what the three tries compare.
        cam = bpy.context.scene.camera
        cam.location = (112.0, -24.0, 7.0)
        cam.rotation_euler = (Vector((0.0, -6.0, 13.0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    return frame


def _ark_extras():
    p = b.Parts()
    stone = _m("#B9B3A8", 0.85)
    p.add(b.box("plaque_stone", (1.2, 0.5, 1.1), (27.0, -13.0, 0.0), 0.08, 2, stone, rad(0, 0, 90)))
    plate = _m("#9A6A3A", 0.35, 0.8)
    p.add(b.box("plaque", (0.9, 0.04, 0.6), (27.27, -13.0, 0.32), 0.02, 1, plate, rad(-12, 0, 90)))
    for k, line in enumerate(("ARK FLIGHT 9", "they walked out here", "14 September 2122")):
        p.add(b.text("plaque_txt", line, 0.09 if k else 0.11, (27.31, -13.0, 0.78 - k * 0.15), rad(78, 0, 90),
                     _m("#3D2A1A", 0.5), 0.003))
    for k in range(6):
        mess.crate(p, (22.0 + (k % 3) * 0.6, -17.0 + (k // 3) * 0.5, 0.0), 8 * k, seed=120 + k)
    p.add(b.text("seed_txt", "SEED LIBRARY", 0.16, (22.6, -17.3, 0.34), rad(90, 0, 90), _m("#2F6E5E", 0.6), 0.004))
    for k in range(3):
        concept.hen(p, (24.5 + k * 0.8, -11.0 + k * 0.6, 0.0), 120 + k * 50)


FRAMES = {
    "canopy": (frame_canopy, "rain", 0.6, 1.0),
    "storm": (frame_storm, "storm", 0.6, 1.0),
    "spring": (_season_frame("spring"), "morning", 0.4, 1.0),
    "summer": (_season_frame("summer"), "clear", 0.4, 1.0),
    "autumn": (_season_frame("autumn"), "autumn", 0.5, 1.0),
    "winter": (_season_frame("winter"), "winter", 0.4, 1.0),
    "wreck_ark": (_wreck_frame(_ark, _ark_extras), "sunset", 0.7, 1.0),
    "wreck_plane": (_wreck_frame(wrecks.wreck_plane), "sunset", 0.7, 1.0),
    "wreck_liner": (_wreck_frame(wrecks.wreck_liner), "sunset", 0.7, 1.0),
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
        scene4._no_ink_for(["haze", "far_", "land", "water", "pond", "petals", "leaves", "chute"])
        if name in ("canopy", "storm", "winter"):
            style = bpy.context.view_layer.freestyle_settings.linesets[0].linestyle
            fade = style.alpha_modifiers.new("fade", "DISTANCE_FROM_CAMERA")
            fade.range_min, fade.range_max = {"canopy": (300.0, 900.0), "storm": (30.0, 180.0),
                                              "winter": (25.0, 160.0)}[name]
            fade.invert = True
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
