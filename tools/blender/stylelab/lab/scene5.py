"""Round 5 vibe frames for the style lab (spec 046): Worktown in 2222. Gardens, a skyline, drones, things flying."""

import math
import random

import bpy

from . import build as b
from . import concept, future, hero, mess, scene, scene3, scene4, street, themes
from .shapes import SOFT, STREAM

# A clear day with a light warm haze (noon has none), for frames that look far.
scene.TIMES["clear"] = dict(scene.TIMES["noon"], haze=0.0015)


def _land(p, size=900.0, y=150.0, z=-0.25):
    p.add(b.box("land", (size, size, 0.1), (0, y, z), 0, 1, b.mat("ground", rough=0.95)))


def _wide_world(cam, haze=0.35):
    """Let the camera and the haze reach a skyline several hundred metres away."""
    sc = bpy.context.scene
    if hasattr(sc.eevee, "volumetric_end"):
        sc.eevee.volumetric_end = 700.0
    cam.data.clip_end = 1500.0
    h = bpy.data.objects.get("haze")
    if h:
        h.scale = (6.0, 6.0, 4.0)
        h.location = (0.0, 150.0, -20.0)
        node = h.data.materials[0].node_tree.nodes.get("Volume Scatter")
        node.inputs["Density"].default_value *= haze


def city(p, rows, seed=1, xs=(-130.0, 130.0), step=26.0, keep=0.55, z0=0.0, near_y=70.0, hmin=35.0, hmax=80.0,
         avoid=None, growth=0.25):
    """Towers in rows behind the town. Near ones get ink outlines, far ones ('far_') do not."""
    rng = random.Random(seed)
    tops = []
    for row, y in enumerate(rows):
        x = xs[0] + rng.uniform(0, step)
        while x < xs[1]:
            tx, ty = x + rng.uniform(-4, 4), y + rng.uniform(-5, 5)
            if rng.random() < keep and not (avoid and avoid(tx, ty)):
                kind = rng.choice(("round", "ribbed", "slab", "slab", "ribbed", "needle"))
                h = rng.uniform(hmin, hmax) * (1 + growth * row)
                r = rng.uniform(5, 9) if kind != "needle" else rng.uniform(6, 8)
                pre = "" if ty < near_y else "far_"
                top = future.tower(p, (tx, ty, z0), kind, h, r, seed=rng.randint(1, 10 ** 6), pre=pre)
                tops.append((tx, ty, top, kind, pre))
            x += step * rng.uniform(0.8, 1.3)
    return tops


def bridges(p, tops, z0=0.0, n=3, seed=2):
    """Sky bridges between neighbouring towers of the same row."""
    rng = random.Random(seed)
    made = 0
    for (x1, y1, h1, k1, pre1), (x2, y2, h2, k2, pre2) in zip(tops, tops[1:]):
        if made >= n:
            break
        if abs(y1 - y2) < 12 and 12 < abs(x2 - x1) < 40 and "needle" not in (k1, k2):
            future.sky_bridge(p, (x1, y1), (x2, y2), z0 + min(h1, h2) * rng.uniform(0.35, 0.6), seed + made, pre1)
            made += 1


def traffic(p, lanes, seed=3):
    """Hover cars in lanes: (y, z, x0, x1, direction, count, prefix). Direction -1 flies towards -X."""
    rng = random.Random(seed)
    trail = b._glow >= 3.0
    for y, z, x0, x1, direction, n, pre in lanes:
        for k in range(n):
            future.hover_car(p, (rng.uniform(x0, x1), y + rng.uniform(-1.5, 1.5), z + rng.uniform(-1, 1)),
                             0 if direction < 0 else 180, rng.choice(future.CARS), pre, trail)


def swarm(p, n, box, seed=4, pre="far_", scale=1.0):
    rng = random.Random(seed)
    (x0, x1), (y0, y1), (z0, z1) = box
    for k in range(n):
        future.drone(p, (rng.uniform(x0, x1), rng.uniform(y0, y1), rng.uniform(z0, z1)), rng.uniform(0, 360),
                     package=rng.random() < 0.7, pre=pre, scale=scale)


def _shop_row(xs, shape=STREAM):
    named = [themes.mex_theme(), themes.default_theme("P1"), themes.jap_theme(),
             dict(themes.default_theme("P1"), name="GREEN BAKERY", sign="#C8553D", door="#E3AE45", props=None),
             dict(themes.default_theme("P1"), name="FLOWER SHOP", sign="#9A7AA8", door="#8CC5AC", props=None)]
    for i, x in enumerate(xs):
        hero.shop_front(shape if i % 2 == 0 else SOFT, named[i % len(named)], (x, 0, 0))


# --- A. The street with the skyline ----------------------------------------------------------------------------------

def frame_skyline(time_id):
    p = b.Parts()
    _land(p)
    street.ground(p, -60, 60)
    hero.shop_front(STREAM, themes.default_theme("P1"), (0, 0, 0))
    for x, th in ((-6.6, themes.mex_theme()), (6.6, themes.jap_theme())):
        hero.shop_front(STREAM, th, (x, 0, 0))
    bakery = dict(themes.default_theme("P1"), name="GREEN BAKERY", sign="#C8553D", door="#E3AE45", props=None)
    flowers = dict(themes.default_theme("P1"), name="FLOWER SHOP", sign="#9A7AA8", door="#8CC5AC", props=None)
    hero.shop_front(SOFT, bakery, (-13.2, 0, 0))
    hero.shop_front(SOFT, flowers, (13.2, 0, 0))
    # Behind the shops: a garden courtyard instead of more blocks, so the towers rise straight behind the town.
    rng = random.Random(11)
    for k in range(12):
        future.garden_tree(p, (rng.uniform(-24, 24), rng.uniform(8, 20), 0.0), 60 + k, rng.uniform(4.0, 6.0))
    tops = city(p, [34.0, 58.0, 88.0, 128.0, 178.0, 240.0], seed=5, near_y=62.0)
    bridges(p, tops, n=4)
    concept.monorail(p, [(-220.0, 17.0), (220.0, 17.0)], 17.0, 0.49)
    for k, x in enumerate((-10.0, -3.3, 3.3, 10.0)):
        future.garden_bed(p, (x, -2.4, 0.0), 2.2, 0.8, 0.5, seed=20 + k, tree=k % 2 == 0)
    traffic(p, [(-6.0, 8.5, -14.0, 20.0, -1, 2, ""), (24.0, 24.0, -70.0, 70.0, -1, 6, ""),
                (26.0, 27.0, -70.0, 70.0, 1, 5, ""), (46.0, 36.0, -110.0, 110.0, -1, 8, ""),
                (75.0, 50.0, -150.0, 150.0, 1, 10, "far_"), (115.0, 62.0, -180.0, 180.0, -1, 10, "far_")])
    future.drone(p, (1.6, -3.0, 5.2), 30, scale=1.2)
    future.drone(p, (-4.5, -1.5, 7.5), -60)
    future.drone(p, (9.0, -6.0, 10.5), 120)
    swarm(p, 28, ((-110, 110), (22, 160), (14, 60)))
    future.airship(p, (-60.0, 160.0, 90.0), 12, 60, "TOWN FAIR SUNDAY", "far_")
    for k, (x, z) in enumerate(((-3.0, 13.0), (-2.2, 13.6), (-1.5, 12.8))):
        concept.bird(p, (x, -5.0, z), "#4E6E8E" if k % 2 else "#2F3A35")
    looks = scene3._looks()
    from . import character
    character.person(looks["cook"], (1.6, -0.6, 0.0), -15, "cook")
    character.person(looks["older_woman"], (2.6, -1.6, 0.0), -60, "woman")
    character.person(looks["younger"], (-3.0, -1.9, 0.0), 35, "younger")
    concept.robot_sweeper(p, (-1.2, -2.9, 0.0), 40)
    street.street_lamp(STREAM, (-3.3, -2.9, 0.0))
    street.street_lamp(STREAM, (4.0, -2.9, 0.0))
    scene.add_sun(time_id, azimuth_offset=-10)
    scene._night_extras(time_id, lamps=[(-3.3, -2.9, 3.17), (4.0, -2.9, 3.17), (0.0, 1.0, 2.6), (-6.6, 1.0, 2.6),
                                        (6.6, 1.0, 2.6)],
                        searchlights=[((-30.0, 30.0, 0.2), (-80, 200, 160)), ((40.0, 40.0, 0.2), (60, 200, 160))])
    cam = scene.camera((6.0, -16.5, 1.6), (-1.0, 40.0, 15.0), lens=22)
    _wide_world(cam, 0.3)


# --- B. The garden promenade -----------------------------------------------------------------------------------------

def frame_garden(time_id):
    p = b.Parts()
    _land(p)
    p.add(b.box("promenade", (17.0, 220.0, 0.12), (0, 90.0, -0.12), 0, 1, b.mat("pavement", rough=0.85)))
    named = [themes.default_theme("P1"), themes.mex_theme(), themes.jap_theme(),
             dict(themes.default_theme("P1"), name="GREEN BAKERY", sign="#C8553D", door="#E3AE45", props=None)]
    for i in range(4):
        y = 3.5 + i * 6.8
        left = hero.shop_front(STREAM if i % 2 == 0 else SOFT, named[i % 4], (-8.5, y, 0))
        left.rotation_euler = b.rad(0, 0, 90)
        right = hero.shop_front(SOFT if i % 2 == 0 else STREAM, named[(i + 2) % 4], (8.5, y, 0))
        right.rotation_euler = b.rad(0, 0, -90)
    city(p, [14.0, 40.0, 70.0, 110.0, 160.0, 220.0], seed=8, xs=(-150.0, 150.0), step=24.0, keep=0.6, near_y=60.0,
         avoid=lambda x, y: abs(x) < 22 and y < 52)
    for k, y in enumerate((2.0, 9.0, 27.0, 34.0)):
        future.garden_bed(p, (0.0, y, 0.0), 1.6, 4.0, 0.55, seed=30 + k, tree=True)
    future.fountain(p, (0.0, 18.0, 0.0), 2.2)
    for k, y in enumerate((5.5, 12.5, 23.5, 30.5)):
        future.bench(p, (-1.6, y, 0.0), -90 if k % 2 else 90)
    for k, (x, y, col) in enumerate(((-5.0, 6.5, "#E58436"), (5.0, 3.0, "#4F9A85"), (5.0, 13.5, "#E3AE45"),
                                     (-5.0, 20.0, "#C8553D"))):
        future.umbrella_table(p, (x, y, 0.0), col, seed=700 + k * 9)
    walkers = [(-2.6, 4.0, 160), (2.4, 7.5, 20), (3.2, 16.0, 200), (-3.0, 24.0, 10), (1.8, 26.0, 180),
               (-2.0, 11.0, 70)]
    concept.crowd(walkers, seed=740)
    concept.dog(p, (-1.6, 10.2, 0.0), 80)
    concept.hen(p, (1.4, 1.4, 0.0), 130)
    concept.robot_sweeper(p, (2.8, 0.2, 0.0), -40)
    concept.monorail(p, [(-220.0, 46.0), (220.0, 46.0)], 11.0, 0.5)
    for k, (x, y, z) in enumerate(((1.0, 6.0, 6.0), (-2.5, 14.0, 8.5), (3.0, 22.0, 10.0), (-1.0, 32.0, 12.5))):
        future.drone(p, (x, y, z), 180 + k * 25, scale=1.1)
    swarm(p, 24, ((-80, 80), (40, 200), (18, 60)))
    traffic(p, [(62.0, 30.0, -90.0, 90.0, -1, 6, ""), (64.0, 33.0, -90.0, 90.0, 1, 5, ""),
                (100.0, 45.0, -140.0, 140.0, -1, 8, "far_")])
    future.airship(p, (50.0, 190.0, 80.0), -15, 55, "", "far_")
    scene.add_sun(time_id, azimuth_offset=40)
    cam = scene.camera((1.2, -5.5, 1.65), (0.0, 40.0, 7.0), lens=22)
    _wide_world(cam, 0.5)


# --- C. The drone pad on our roof ------------------------------------------------------------------------------------

def frame_roof(time_id):
    p = b.Parts()
    _land(p, z=-14.25)
    wall = b.mat("wall_out", rough=0.85)
    trim = b.mat("trim", rough=0.5)
    p.add(b.box("roof_building", (18.0, 15.0, 14.0), (0, 3.5, -14.3), 0, 1, wall))
    p.add(b.box("roof", (18.0, 15.0, 0.3), (0, 3.5, -0.3), 0.05, 1, b.mat("pavement", rough=0.85)))
    for x, y, w, d in ((0, -4.0, 18.0, 0.3), (0, 11.0, 18.0, 0.3), (-9.0, 3.5, 0.3, 15.0), (9.0, 3.5, 0.3, 15.0)):
        p.add(b.box("parapet", (w, d, 0.9), (x, y, 0.0), 0.08, 2, trim))
        p.add(b.box("rail", (w if w > 1 else 0.06, d if d > 1 else 0.06, 0.06), (x, y, 1.15), 0.02, 1, b.chrome()))
    # The pad: dark disc, warm ring, our initials.
    px, py = -1.2, 3.0
    p.add(b.cyl("pad", 1.9, 0.08, (px, py, 0.0), 40, edge=0.02, material=future._m("#3A3532", 0.6)))
    p.add(b.torus("pad_ring", 1.65, 0.06, (px, py, 0.09), 40, 4, b.lamp(2.5, "lamp_warm")))
    p.add(b.text("pad_text", "TK", 1.0, (px, py, 0.09), None, future._m("#F3E9D2", 0.5), 0.005))
    future.drone(p, (px, py, 1.7), 20, scale=1.7)
    future.drone(p, (3.8, 7.0, 5.0), 60, scale=1.5)
    future.drone(p, (-7.0, 9.0, 9.0), -30, scale=1.4)
    # Orders waiting, the cook bringing one more.
    for k in range(3):
        mess.cardboard_box(p, (1.6, 0.6 + k * 0.05, k * 0.3), (0.42, 0.34, 0.3), k * 7, False, 800 + k, f"#{12 + k}")
    from . import character
    character.person(scene3._looks()["cook"], (2.6, 1.7, 0.0), 120, "cook")
    # Herb garden, a little greenhouse, solar panels, a bench.
    for k in range(3):
        future.garden_bed(p, (-7.0, 6.5 + k * 1.6, 0.0), 3.4, 1.0, 0.5, seed=40 + k)
    gx, gy = 6.0, 7.5
    p.add(b.box("gh_glass", (3.2, 2.6, 2.2), (gx, gy, 0.0), 0.05, 1, hero.clear_glass("#CFE6E2", 0.3)))
    for sx in (-1.6, 0.0, 1.6):
        p.add(b.box("gh_frame", (0.06, 2.62, 2.25), (gx + sx, gy, 0.0), 0.0, 1, b.mat("cream", rough=0.4)))
    p.add(b.box("gh_roof", (3.3, 2.7, 0.08), (gx, gy, 2.2), 0.02, 1, b.mat("cream", rough=0.4)))
    for k in range(6):
        p.add(b.sphere("gh_plant", 0.3, (gx - 1.2 + k * 0.48, gy, 0.5), 10, material=future._m(future.LEAVES[k % 4], 0.7)))
    for k in range(4):
        p.add(b.box("solar", (1.6, 1.0, 0.05), (-4.0 + k * 1.8, 10.0, 0.6), 0.01, 1, future._m("#2E3F5C", 0.15, 0.4),
                    b.rad(25, 0, 0)))
    future.bench(p, (-6.5, 1.0, 0.0), -90)
    future.garden_tree(p, (-7.8, -2.5, 0.0), 7)
    # Neighbouring roofs below and the towers beyond.
    rng = random.Random(14)
    for i, (x, y) in enumerate(((-30.0, 6.0), (30.0, 0.0), (-8.0, 30.0), (16.0, 32.0), (-36.0, 30.0), (40.0, 28.0))):
        concept.building(p, (x, y, -14.0), 16.0, 12.0, rng.randint(3, 4), rng.choice(future.WALLS),
                         rng.choice(future.TRIMS), seed=900 + i)
    tops = city(p, [52.0, 80.0, 115.0, 165.0, 225.0], seed=9, z0=-14.0, near_y=90.0,
                avoid=lambda x, y: abs(x) < 16 and y < 60)
    bridges(p, tops, z0=-14.0, n=3)
    traffic(p, [(20.0, 6.0, -60.0, 60.0, -1, 3, ""), (36.0, 12.0, -80.0, 80.0, 1, 5, ""),
                (60.0, 22.0, -120.0, 120.0, -1, 8, ""), (100.0, 34.0, -160.0, 160.0, 1, 10, "far_")])
    swarm(p, 26, ((-100, 100), (25, 170), (4, 50)))
    future.airship(p, (-50.0, 170.0, 70.0), 20, 55, "TOWN FAIR SUNDAY", "far_")
    scene.add_sun(time_id, azimuth_offset=-20)
    cam = scene.camera((4.2, -3.2, 1.9), (-1.6, 5.0, 2.6), lens=24)
    _wide_world(cam, 0.6)


# --- D. The town from above ------------------------------------------------------------------------------------------

def frame_aerial(time_id):
    p = b.Parts()
    _land(p, 1400.0, 120.0)
    road = b.mat("road", rough=0.9)
    for y in (-8.0, 62.0, 132.0, 210.0):
        p.add(b.box("road", (700.0, 8.0, 0.05), (0, y, -0.17), 0, 1, road))
    for x in (-130.0, -48.0, 48.0, 130.0):
        p.add(b.box("road", (8.0, 600.0, 0.05), (x, 120.0, -0.16), 0, 1, road))
    street.ground(p, -45, 45)
    for i, x in enumerate((-26.4, -19.8, -13.2, -6.6, 0.0, 6.6, 13.2, 19.8, 26.4)):
        th = [themes.default_theme("P1"), themes.mex_theme(), themes.jap_theme()][i % 3]
        hero.shop_front(STREAM if i % 2 else SOFT, dict(th, props=None), (x, 0, 0), show_interior=False)
    rng = random.Random(15)
    for i, x in enumerate(range(-36, 37, 9)):
        concept.building(p, (x, 11.0, 0.0), 8.0, 6.0, rng.randint(3, 5), rng.choice(future.WALLS),
                         rng.choice(future.TRIMS), seed=950 + i)
    concept.greenhouse_dome(p, (-20.0, -42.0, 0.0), 16.0)
    for k in range(16):
        a = 2 * math.pi * k / 16
        future.garden_tree(p, (-20.0 + math.cos(a) * 21.0, -42.0 + math.sin(a) * 15.0, 0.0), k, 4.5)
    tops = city(p, [-70.0, -35.0, 30.0, 70.0, 110.0, 160.0, 220.0, 290.0], seed=12, xs=(-260.0, 260.0), step=34.0,
                keep=0.45, near_y=95.0, growth=0.1,
                avoid=lambda x, y: (abs(x) < 60 and -70 < y < 40) or abs(y - 62) < 10 or abs(y + 8) < 12)
    bridges(p, tops, n=6)
    # Parks fill the ground between roads: the town is a garden with towers in it.
    for k in range(150):
        tx, ty = rng.uniform(-120, 120), rng.uniform(-90, 55)
        if (abs(tx) < 48 and -14 < ty < 18) or abs(ty - 62) < 7 or abs(ty + 8) < 7 or \
                min(abs(tx - rx) for rx in (-130.0, -48.0, 48.0, 130.0)) < 6 or \
                (tx + 20) ** 2 + ((ty + 42) * 1.3) ** 2 < 21 ** 2:
            continue
        future.garden_tree(p, (tx, ty, 0.0), 1000 + k, rng.uniform(4.0, 7.0))
    for k in range(6):
        future.garden_bed(p, (rng.uniform(-40, 40), rng.uniform(-30, -16), 0.0), 6.0, 3.0, 0.6, seed=1200 + k)
    concept.monorail(p, [(-150.0, 30.0), (150.0, 30.0), (150.0, 160.0), (-150.0, 160.0), (-150.0, 30.0)], 24.0, 0.45)
    traffic(p, [(-20.0, 30.0, -160.0, 160.0, -1, 8, ""), (40.0, 42.0, -200.0, 200.0, 1, 12, ""),
                (100.0, 55.0, -220.0, 220.0, -1, 12, "far_"), (180.0, 70.0, -240.0, 240.0, 1, 12, "far_")])
    swarm(p, 40, ((-150, 150), (-60, 220), (10, 70)), scale=1.6)
    future.airship(p, (-70.0, 60.0, 95.0), 25, 60, "TOWN FAIR SUNDAY", "")
    future.airship(p, (120.0, 260.0, 110.0), -40, 50, "", "far_")
    concept.crowd([(rng.uniform(-30, 30), rng.uniform(-2.8, -0.8), rng.uniform(0, 360)) for _ in range(14)], seed=980)
    scene.add_sun(time_id, azimuth_offset=20)
    scene._night_extras(time_id, lamps=[], searchlights=[((-60.0, 40.0, 0.2), (-120, 240, 200)),
                                                          ((70.0, 60.0, 0.2), (120, 260, 200))])
    cam = scene.camera((55.0, -100.0, 52.0), (-8.0, 0.0, 6.0), lens=30)
    _wide_world(cam, 0.3)


FRAMES = {
    "skyline-sunset": (frame_skyline, "sunset", 0.6),
    "skyline-night": (frame_skyline, "night", 0.6),
    "garden": (frame_garden, "morning", 0.6),
    "roof": (frame_roof, "clear", 0.7),
    "aerial": (frame_aerial, "sunset", 0.5),
}


def render(name, path, samples=64, scale=1.0, ink=True):
    fn, time_id, grit = FRAMES[name]
    b.reset("P1")
    b.set_grit(grit)
    b.set_glow(scene.TIMES[time_id]["glow"])
    scene.setup_render(samples, scale)
    scene.setup_world(time_id)
    fn(time_id)
    if ink:
        scene4.illustration_look(1.1 if scale >= 1 else 0.7)
        scene4._no_ink_for(["haze", "far_", "land"])
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
