"""Round 8 frames for the style lab (spec 046, plan in spec 051 sections 5b and 5c): Haven Nine under its Dome.

A. dome_air:   the whole town under the high organic Dome, from outside; the Storm Ring on the horizon
B. dome_night: inside at night from Greenhouse Hill, the lit ribs overhead
C. wreck_wall: the Albatross through the Dome wall: tail and market inside, nose outside in the storm
D. lake_wall:  the wall running down into the lake, the water gate, the storm outside
E. train_gate: the round pressure door of the tunnel to Haven Eight, the cargo train, "Transit closed"
F. low_ring:   the Low Ring at night: betting halls, dream booths, broken robots, one warm soup stall
"""

import math
import random

import bpy
from mathutils import Euler, Vector

from . import build as b
from . import character, climb, concept, dome, future, lowring, mess, scene, scene3, scene4, scene5, scene6, street
from . import world
from .build import rad
from .dome import Dome, Wall
from .future import _m

scene.TIMES["calm"] = {  # the calm eye over the town: clear above, a little haze
    "sun": ("#FFF1DC", 3.8, 34, -35), "ambient": 0.85, "glow": 0.5, "haze": 0.0012,
    "sky": ("#7FA6C4", "#E9DCC6"),
}
scene.TIMES["lake"] = {
    "sun": ("#FFE2C0", 3.2, 22, -60), "ambient": 0.8, "glow": 0.8, "haze": 0.002,
    "sky": ("#7E9DB8", "#D9D6CC"),
}
scene.TIMES["dome_night"] = {
    "sun": ("#8FA6FF", 0.4, 35, 30), "ambient": 0.2, "glow": 4.0, "haze": 0.01,
    "sky": ("#0B1020", "#2C2645"),
}
scene.TIMES["dusk_storm"] = {
    "sun": ("#FFB27A", 1.4, 8, -60), "ambient": 0.4, "glow": 2.2, "haze": 0.005,
    "sky": ("#1E2430", "#5E5458"),
}

HAVEN = dict(centre=(-5.0, -25.0), rx=385.0, ry=322.0, top=250.0)
WRECK_AT = (-410.0, 20.0)


def _haven_wreck(p, loc, yaw=0.0, length=150.0, r=17.0, seed=7, lit=True, **kw):
    """The town's Wreck turned so its nose points west out through the Dome wall (spec 051 section 5b)."""
    x, y = WRECK_AT
    z = scene6.town_height(x, y) - 5.0
    return _orig_wreck(p, (x, y, z), 0.0, length, r, seed, lit, pitch=10.0, roll=4.0, ark=True)


_orig_wreck = world.wreck


def _town_under_dome(p, lit_ribs=False):
    world.wreck = _haven_wreck
    try:
        scene6.town(p, 1.0, lots=False)
    finally:
        world.wreck = _orig_wreck
    dm = Dome(HAVEN["centre"], HAVEN["rx"], HAVEN["ry"], HAVEN["top"], seed=9)
    dm.build(ribs_lit=lit_ribs, glass_alpha=0.12 if not lit_ribs else 0.08)
    # Land and water outside: the town's height field carried on, grey-brown and bare; the lake dark and rough.
    oh = dome.outside_height(scene6.town_height)

    def colour(x, y, z, s):
        if dm.inside(x, y):
            return scene6.town_colour(x, y, z, s)
        if z < -0.2:
            return "#4E5A5C"
        return dome.OUTSIDE[int(abs(x * 3 + y * 5)) % 4] if s < 0.5 else world.ROCK[2]

    world.terrain("land_out", oh, -2900.0, 2900.0, -2900.0, 2900.0, 30.0, seed=81, colour=colour)
    p.add(b.box("water_out", (2750.0, 5800.0, 0.05), (1525.0, 0.0, -0.1), 0, 1, _m("#2F3B3F", 0.6)))
    inner = b.cyl("water_in", 1.0, 0.04, (HAVEN["centre"][0], HAVEN["centre"][1], -0.02), 96,
                  material=_m("#6FA3B0", 0.15, alpha=0.9))
    inner.scale = (HAVEN["rx"] * 0.995, HAVEN["ry"] * 0.995, 1.0)
    # The seal where the wall meets the Wreck's hull (where the hull crosses the shell).
    hx = HAVEN["centre"][0] - HAVEN["rx"] * math.sqrt(1 - ((WRECK_AT[1] - HAVEN["centre"][1]) / HAVEN["ry"]) ** 2)
    wz = scene6.town_height(*WRECK_AT) - 5.0
    lx = hx - WRECK_AT[0]
    m = Euler(rad(4.0, 10.0, 0.0)).to_matrix()
    c = m @ Vector((lx, 0.0, 17.0 * 0.7)) + Vector((WRECK_AT[0], WRECK_AT[1], wz))
    rr = 17.0 * math.sin(math.pi * lx / 150.0) ** 0.55
    dome.seal_collar(p, tuple(c), rr, rad(0, 90 + 10, 0))
    # Dead trees and boulders outside the glass.
    rng = random.Random(82)
    for k in range(260):
        a, d = rng.uniform(0, 6.28), rng.uniform(1.03, 3.6)
        x = HAVEN["centre"][0] + HAVEN["rx"] * d * math.cos(a)
        y = HAVEN["centre"][1] + HAVEN["ry"] * d * math.sin(a)
        z = oh(x, y)
        if z < 0.3:
            continue
        if k % 3:
            dome.dead_tree(p, (x, y, z), 8200 + k, rng.uniform(6, 12))
        else:
            world.rock(p, (x, y, z), rng.uniform(3, 8), 8300 + k, pre="far_")
    return dm


def _far_world(cam, clip=9000.0, haze=0.12, scale=(30.0, 30.0, 12.0)):
    sc = bpy.context.scene
    if hasattr(sc.eevee, "volumetric_end"):
        sc.eevee.volumetric_end = clip
    cam.data.clip_end = clip
    h = bpy.data.objects.get("haze")
    if h:
        h.scale = scale
        h.location = (0.0, 0.0, -60.0)
        node = h.data.materials[0].node_tree.nodes.get("Volume Scatter")
        node.inputs["Density"].default_value *= haze


# --- A. the Dome from outside --------------------------------------------------------------------------------------

def frame_dome_air(time_id):
    p = b.Parts()
    _town_under_dome(p)
    dome.storm_ring(p, HAVEN["centre"], 2500.0, depth=380.0, height=950.0, seed=83, bolts=12)
    scene.add_sun(time_id, azimuth_offset=15)
    cam = scene.camera((1000.0, -1050.0, 330.0), (-40.0, 0.0, 115.0), lens=50)
    _far_world(cam, haze=0.015, scale=(50.0, 50.0, 16.0))


# --- B. inside at night ---------------------------------------------------------------------------------------------

def frame_dome_night(time_id):
    p = b.Parts()
    _town_under_dome(p, lit_ribs=True)
    dome.storm_ring(p, HAVEN["centre"], 2500.0, depth=380.0, height=950.0, seed=85, bolts=10)
    scene.add_sun(time_id)
    cx, cy = -40.0, 212.0
    cz = scene6.town_height(cx, cy) + 2.0
    cam = scene.camera((cx, cy, cz), (-150.0, -120.0, 150.0), lens=15)
    _far_world(cam, haze=0.03, scale=(12.0, 12.0, 8.0))


# --- C. the Wreck through the wall ----------------------------------------------------------------------------------

def _valley_out(x, y):
    """Flat valley floor inside (x > 25), the old landslide rising outside (x < 0)."""
    h = 34.0 * scene6._ss(-20.0, -140.0, x) + 22.0 * scene6._ss(60.0, 140.0, abs(y)) * scene6._ss(40.0, -40.0, x)
    return h + 0.5 * math.sin(x / 7.0) * math.cos(y / 9.0)


def frame_wreck_wall(time_id):
    p = b.Parts()
    wall = Wall(24.0, lean=0.2, bend=1.0 / 1400.0, y_range=(-150.0, 150.0), z_top=95.0)

    def colour(x, y, z, s):
        if x < wall.x(y, 0.0):
            return dome.OUTSIDE[int(abs(x * 3 + y)) % 4] if s < 0.45 else world.ROCK[2]
        return world.GRASS[int(abs(x * 3 + y * 5)) % 4] if s < 0.45 else world.ROCK[0]

    world.terrain("land_wreck", _valley_out, -400.0, 300.0, -300.0, 300.0, 4.0, seed=86, colour=colour)
    loc, pitch, roll = (0.0, 0.0, -1.5), -3.0, 5.0
    world.wreck(p, loc, 0.0, 150.0, 17.0, seed=7, lit=True, pitch=pitch, roll=roll, ark=True)
    # Where the hull axis crosses the wall (a few fixed-point steps), and its radius there.
    m = Euler(rad(roll, pitch, 0.0)).to_matrix()
    lx = 24.0
    for _ in range(6):
        c = m @ Vector((lx, 0.0, 17.0 * 0.7)) + Vector(loc)
        lx += wall.x(c.y, c.z) - c.x
    c = m @ Vector((lx, 0.0, 17.0 * 0.7)) + Vector(loc)
    rr = 17.0 * math.sin(math.pi * lx / 150.0) ** 0.55
    tilt = math.degrees(math.atan(wall.lean + 0.008 * c.z))

    def hole(y, z):
        return math.hypot(y - c.y, z - c.z) < rr + 3.0

    wall.build(p, panel=(4.5, 3.5), hole=hole, glass_alpha=0.22)
    rot = rad(0, 90 + tilt, 0)
    dome.seal_collar(p, tuple(c), rr, rot)
    plate = b.lathe("seal_plate", [(rr + 0.3, -0.25), (rr + 6.2, -0.25), (rr + 6.2, 0.25), (rr + 0.3, 0.25)], 48,
                    tuple(c), _m("#5A5650", 0.55, 0.5), rot, smooth=False)
    p.add(plate)
    for k in range(36):
        a = 2 * math.pi * k / 36
        q = Vector((0.0, math.cos(a) * (rr + 4.6), math.sin(a) * (rr + 4.6)))
        q = Euler(rad(0, tilt, 0)).to_matrix() @ q + c
        p.add(b.sphere("seal_rivet", 0.22, tuple(q - Vector((0.3, 0, 0))), 8, material=_m("#3A3734", 0.5, 0.6)))
    p.add(b.text("seal_txt", "SEALED 2161", 1.4, tuple(c + Vector((3.5, -rr - 3.2, -rr * 0.55))), rad(90, 0, -8),
                 _m("#E3AE45", 0.6), 0.03))
    # Outside: the storm on the nose, rain, the black ring of cloud, a bolt into the landslide.
    world.rain(((-260.0, 22.0), (-150.0, 150.0), (0.0, 110.0)), 26000, 3.5, seed=87, hex_="#AAB6C2", alpha=0.32,
               slant=(0.35, 0.0))
    world.clouds(p, (-250.0, 30.0, 130.0), 30, (200.0, 220.0), (40.0, 70.0), "#262B33", seed=88)
    world.clouds(p, (-520.0, 0.0, 70.0), 26, (60.0, 400.0), (60.0, 110.0), "#1D2128", seed=89)
    world.lightning([(-150.0, 70.0, 140.0), (-110.0, 40.0, _valley_out(-110.0, 40.0))], seed=90, width=0.8,
                    strength=160.0, flash=6.0e5, branches=4)
    for k in range(40):
        rng = random.Random(900 + k)
        x, y = rng.uniform(-250.0, -10.0), rng.uniform(-140.0, 160.0)
        if abs(y) < 22 and x > -60:
            continue
        dome.dead_tree(p, (x, y, _valley_out(x, y)), 910 + k, rng.uniform(5, 9))
    # Inside: the market spilling out of the hull: stalls, string lights, lamps, people.
    rng = random.Random(91)
    awn = ("#C8553D", "#E3AE45", "#4F9A85", "#9A7AA8", "#E58436")
    for k in range(6):
        x = 38.0 + k * 6.5
        y = -20.5 - (k % 2) * 1.5
        p.add(b.box("stall", (4.2, 2.6, 1.0), (x, y, 0.0), 0.1, 1, _m("#8A5A3B", 0.8)))
        p.add(b.box("stall_awning", (4.8, 3.2, 0.25), (x, y, 2.6), 0.05, 1, _m(awn[k % 5], 0.6), rad(-10, 0, 0)))
        for sx in (-2.0, 2.0):
            p.add(b.cyl("stall_post", 0.06, 2.6, (x + sx, y - 1.2, 0.0), 6, material=_m("#3A3734", 0.6)))
        p.add(b.box("stall_glow", (3.8, 2.2, 0.1), (x, y, 1.02), 0.05, 1, b.lamp(0.2, "lamp_warm")))
        scene.point_light("stall_lamp", (x, y - 1.0, 2.3), "#FFC98A", 140, 0.3)
    concept.string_lights(p, (34.0, -24.0, 5.5), (80.0, -24.0, 5.5), 22, 0.8)
    concept.string_lights(p, (34.0, -28.5, 4.8), (80.0, -18.0, 7.5), 22, 0.6, colours=("#FFD9A0", "#E58AA6"))
    for sx in (34.0, 74.0):
        p.add(b.cyl("arch_post", 0.25, 6.5, (sx, -33.0, 0.0), 10, material=_m("#3A3734", 0.6, 0.4)))
    p.add(b.box("arch_beam", (41.0, 0.5, 0.5), (54.0, -33.0, 6.4), 0.1, 1, _m("#3A3734", 0.6, 0.4)))
    lowring.neon(p, (54.0, -33.3, 8.2), "WRECK MARKET", "#FFB070", size=1.8, glow=0.8)
    spots = [(rng.uniform(36.0, 78.0), rng.uniform(-30.0, -24.0), rng.uniform(0, 360)) for _ in range(14)]
    concept.crowd(spots, seed=920)
    for k in range(4):
        lowring.sodium_lamp(p, (34.0 + k * 16.0, -34.0, 0.0), 6.0, 90, glow=0.7)
        scene.point_light("sodium", (34.0 + k * 16.0, -32.9, 6.2), lowring.SODIUM, 260, 0.2)
    scene.add_sun(time_id, azimuth_offset=40)
    scene.area_light("market_wash", (55.0, -40.0, 18.0), (40.0, 12.0), "#FFC98A", 9000, rad(60, 0, 0))
    scene.area_light("storm_cold", (-50.0, -50.0, 45.0), (40.0, 40.0), "#9DB8FF", 60000,
                     (Vector((10.0, 0.0, 10.0)) - Vector((-50.0, -50.0, 45.0))).to_track_quat("-Z", "Y").to_euler())
    cam = scene.camera((74.0, -50.0, 4.5), (18.0, 2.0, 15.0), lens=21)
    _far_world(cam, clip=2000.0, haze=0.4, scale=(6.0, 6.0, 4.0))


# --- D. the wall in the lake -------------------------------------------------------------------------------------

def _waves(x, y):
    return 0.45 * math.sin(x / 3.1 + y / 5.0) + 0.3 * math.sin(x / 1.7 - y / 2.3) + 0.2 * math.cos(y / 1.3)


def frame_lake_wall(time_id):
    p = b.Parts()
    wall = Wall(0.0, lean=0.24, bend=1.0 / 1100.0, y_range=(-180.0, 180.0), z_top=110.0, z_bottom=-8.0)

    def gate(y, z):
        return abs(y) < 9.0 and z < 11.0

    wall.build(p, panel=(6.0, 5.0), hole=gate, glass_alpha=0.18)
    # Inside: a calm, light lake; the pier we stand on; buoys; a fisher.
    world.water(p, 0.0, 400.0, -300.0, 300.0, 0.0, "#6FA3B0", 0.9, name="water_in")
    world.pier(p, 18.0, 95.0, -16.0, 5.0, 1.0)
    for k in range(12):
        world.buoy(p, (14.0 + (k % 2) * 1.5, -60.0 + k * 10.0, 0.1))
    # Outside: dark choppy water, white caps, the storm wall, rain, a bolt.
    world.terrain("water_out", _waves, -700.0, -1.0, -300.0, 300.0, 2.5, seed=92,
                  colour=lambda x, y, z, s: "#C7CED0" if z > 0.62 else ("#3E4B50", "#46555A", "#384448")[
                      int(abs(x * 3 + y)) % 3])
    world.rain(((-400.0, -2.0), (-200.0, 200.0), (0.0, 120.0)), 26000, 4.0, seed=93, hex_="#AAB6C2", alpha=0.3,
               slant=(0.4, 0.0))
    world.clouds(p, (-380.0, 0.0, 140.0), 34, (200.0, 330.0), (50.0, 90.0), "#262B33", seed=94)
    world.clouds(p, (-760.0, 0.0, 90.0), 30, (60.0, 520.0), (80.0, 140.0), "#1D2128", seed=95)
    world.clouds(p, (-300.0, 0.0, 30.0), 40, (60.0, 420.0), (40.0, 70.0), "#1A1D23", seed=951)
    world.lightning([(-260.0, 60.0, 150.0), (-210.0, 30.0, 0.0)], seed=96, width=0.9, strength=170.0, flash=8.0e5,
                    branches=5)
    # The water gate: a concrete arch in the wall, a steel sluice half up, a cleaning boat waiting at it.
    concrete = _m("#B9B3A8", 0.85)
    gx = wall.x(0.0, 0.0)
    p.add(b.torus("gate_arch", 9.6, 1.4, (gx + 0.3, 0.0, 0.0), 32, 8, concrete, rad(90, 0, 90), arc=180))
    for sy in (-1, 1):
        p.add(b.box("gate_pier", (3.6, 3.0, 14.0), (gx + 0.3, sy * 10.4, -4.0), 0.3, 2, concrete))
    sluice = b.box("gate_sluice", (0.8, 18.0, 6.0), (gx + 0.6, 0.0, 4.8), 0.1, 1, _m("#5A5650", 0.5, 0.6))
    p.add(sluice)
    for k in range(9):
        p.add(b.box("gate_rib", (0.9, 0.3, 6.0), (gx + 0.7, -8.0 + k * 2.0, 4.8), 0, 1, _m("#3A3734", 0.5, 0.6)))
    for k in range(9):
        p.add(b.box("gate_warn", (0.92, 2.0, 0.5), (gx + 0.7, -8.0 + k * 2.0, 4.8), 0, 1,
                    _m("#E3AE45" if k % 2 else "#2C2A28", 0.6)))
    p.add(b.text("gate_txt", "WATER GATE 3   CLEANING CRAFT ONLY", 0.9, (gx + 1.4, 0.0, 12.3), rad(90, 0, 90),
                 _m("#2F3A35", 0.6), 0.02))
    # The cleaning boat: a flat hull with brushes and a robot dome.
    boat = [b.box("boat_hull", (3.0, 6.5, 1.0), (0, 0, -0.4), 0.4, 2, _m("#F3E9D2", 0.5)),
            b.box("boat_band", (3.05, 6.55, 0.25), (0, 0, 0.2), 0.1, 1, _m("#4F9A85", 0.5))]
    for sy in (-2.4, 2.4):
        boat.append(b.cyl("boat_brush", 0.5, 3.4, (-1.7, sy, 0.0), 12, material=_m("#C9A548", 0.9),
                          rot=rad(0, 90, 0)))
    boat.append(b.sphere("boat_dome", 1.0, (0.2, 0, 0.6), 16, (1, 1, 0.7), _m("#F3E9D2", 0.4)))
    for sy in (-0.3, 0.3):
        boat.append(b.sphere("boat_eye", 0.12, (-0.75, sy, 0.95), 10, material=b.lamp(2.5, "lamp_warm")))
    p.add(b.group("cleaner", boat, (gx + 12.0, 3.0, 0.0), rad(0, 0, 0)))
    character.person(concept.random_look(940), (58.0, -15.0, 1.2), 100, "fisher")
    p.add(b.cyl("rod", 0.015, 3.2, (57.6, -15.4, 2.0), 6, material=_m("#3A3734", 0.5), rot=rad(0, -60, 10)))
    for k in range(6):
        future.drone(p, (40.0 + k * 9.0, 30.0 + k * 6.0, 18.0 + k * 3.0), 180, package=k % 2 == 0)
    scene.add_sun(time_id, azimuth_offset=170)
    cam = scene.camera((64.0, -18.5, 3.2), (-10.0, 6.0, 19.0), lens=22)
    _far_world(cam, clip=2500.0, haze=0.04, scale=(8.0, 8.0, 4.0))


# --- E. the tunnel door to Haven Eight ---------------------------------------------------------------------------

def _ridge(x, y):
    h = 24.0 * scene6._ss(-24.0, -32.0, x) + 30.0 * scene6._ss(-60.0, -160.0, x)
    return h + 1.2 * math.sin(y / 5.0) * scene6._ss(-24.0, -40.0, x) + 0.3 * math.sin(x / 4.0)


def frame_train_gate(time_id):
    p = b.Parts()
    world.terrain("land_ridge", _ridge, -260.0, 160.0, -160.0, 160.0, 2.5, seed=97,
                  colour=lambda x, y, z, s: world.ROCK[int(abs(x + y * 3)) % 3] if s > 0.5 else
                  (world.GRASS[int(abs(x * 3 + y)) % 4] if x > -30 else dome.OUTSIDE[int(abs(x + y)) % 4]))
    wall = Wall(-36.0, lean=0.18, bend=1.0 / 1500.0, y_range=(-150.0, 150.0), z_top=120.0, z_bottom=22.0)
    wall.build(p, panel=(6.0, 5.0), glass_alpha=0.18)
    concrete = _m("#B9B3A8", 0.85)
    # The portal: a concrete face in the cliff, the round door in it, warning lights.
    p.add(b.box("portal_face", (3.0, 34.0, 24.0), (-25.5, 0.0, -0.5), 0.4, 2, concrete))
    p.add(b.torus("portal_ring", 8.6, 1.3, (-23.8, 0.0, 8.6), 48, 8, _m("#9A958C", 0.5, 0.5), rad(90, 0, 90)))
    dome.vault_door(p, (-24.0, 0.0, 8.6), r=7.6, yaw=90.0)
    for sy in (-11.0, 11.0):
        p.add(b.sphere("portal_beacon", 0.5, (-23.8, sy, 18.0), 12, material=_m("#E58436", 0.4, emit=10.0)))
        scene.point_light("beacon", (-22.8, sy, 18.0), "#E58436", 220, 0.3)
    p.add(b.text("portal_txt", "HAVEN EIGHT  132 KM", 1.6, (-23.9, 0.0, 20.6), rad(90, 0, 90), _m("#2F3A35", 0.6),
                 0.03))
    # The track and the cargo train standing at the door.
    beam = _m("#F3E9D2", 0.45)
    p.add(b.box("beam", (110.0, 1.2, 1.0), (31.0, 0.0, 0.2), 0.1, 2, beam))
    grey, stripe = _m("#8A8F94", 0.5, 0.3), _m("#E58436", 0.5)
    for k in range(4):
        x0 = -14.0 + k * 13.0
        p.add(b.box("cargo_car", (12.0, 4.0, 4.2), (x0 + 6.0, 0.0, 1.2), 0.9, 3, grey))
        p.add(b.box("cargo_stripe", (12.05, 4.05, 0.4), (x0 + 6.0, 0.0, 2.4), 0.2, 1, stripe))
        p.add(b.text("cargo_txt", "H8 CARGO", 0.8, (x0 + 6.0, -2.06, 4.0), rad(90, 0, 0), _m("#2F3A35", 0.6), 0.01))
        for j in range(3):
            p.add(b.box("cargo_rib", (0.25, 4.1, 4.0), (x0 + 1.5 + j * 4.5, 0.0, 1.3), 0, 1, _m("#6E7378", 0.5)))
    p.add(b.capsule("cargo_nose", 2.1, 6.0, (38.0, 0.0, 3.3), 20, grey, rad(0, 90, 0)))
    # The platform: crates unloaded by robots, a guard robot of the Keepers, people waiting, the board.
    p.add(b.box("platform", (70.0, 10.0, 1.3), (20.0, -8.0, 0.0), 0.2, 2, concrete))
    for k in range(34):
        p.add(b.box("platform_edge", (1.0, 0.5, 0.05), (-13.0 + k * 2.0, -3.2, 1.3), 0, 1,
                    _m("#E3AE45" if k % 2 else "#2C2A28", 0.6)))
    rng = random.Random(98)
    for k in range(14):
        mess.crate(p, (rng.uniform(-4.0, 26.0), rng.uniform(-11.5, -6.0), 1.3 + (0.36 if k % 5 == 0 else 0.0)),
                   rng.uniform(-10, 10), seed=980 + k, size=(1.1, 0.8, 0.7))
    for k, (x, y, yaw) in enumerate(((4.0, -5.0, 90), (12.0, -6.5, -80), (19.0, -5.2, 100))):
        concept.robot_runner(p, (x, y, 1.3), yaw, tray=False, seed=990 + k)
    concept.robot_runner(p, (-8.0, -6.0, 1.3), 60, body="#2E3F5C", accent="#E3AE45", tray=False, seed=995)
    lowring.broken_runner(p, (-11.0, -10.6, 1.3), 30, lean=-14.0, seed=996, screen_text="OPEN  OPEN  OPEN")
    looks = scene3._looks()
    character.person(looks["older_woman"], (24.0, -9.5, 1.3), 160, "waiting_woman")
    concept.crowd([(28.0, -10.0, 150), (31.0, -8.6, 175), (8.0, -10.8, 200)], seed=960)
    board = [b.box("board", (0.2, 7.0, 3.0), (0, 0, 0), 0.05, 1, _m("#1C1A18", 0.6))]
    for k, (line, col) in enumerate((("HAVEN EIGHT   CARGO ONLY", "#E3AE45"), ("TRANSIT CLOSED", "#E58436"),
                                     ("NEXT SERVICE: AUTUMN", "#E3AE45"))):
        t = b.text("board_txt", line, 0.42, (0.12, 0, 2.4 - k * 0.8), rad(90, 0, 90),
                   _m(col, 0.4, emit=7.0), 0.005)
        t.visible_shadow = False
        board.append(t)
    for sy in (-3.0, 3.0):
        board.append(b.cyl("board_post", 0.1, 3.4, (0, sy, -3.4), 8, material=_m("#3A3734", 0.6)))
    p.add(b.group("board", board, (6.0, -12.0, 4.7), rad(0, 0, -90 + 20)))
    world.clouds(p, (-170.0, 0.0, 75.0), 34, (40.0, 260.0), (40.0, 70.0), "#262B33", seed=99)
    world.clouds(p, (-260.0, 60.0, 130.0), 20, (60.0, 200.0), (50.0, 80.0), "#1D2128", seed=100)
    world.rain(((-200.0, -40.0), (-150.0, 150.0), (20.0, 110.0)), 14000, 4.0, seed=101, hex_="#AAB6C2", alpha=0.3,
               slant=(0.3, 0.0))
    scene.add_sun(time_id, azimuth_offset=-20)
    cam = scene.camera((44.0, -19.0, 3.0), (-22.0, 2.0, 11.0), lens=21)
    _far_world(cam, clip=1500.0, haze=0.15, scale=(5.0, 5.0, 4.0))


# --- F. the Low Ring at night --------------------------------------------------------------------------------------

def _facade(p, x, y0, y1, h, side, rng, wall=None):
    """A dark street front along Y at x, facing the street (side -1 faces -X, +1 faces +X)."""
    wall = wall or rng.choice(lowring.SOOT)
    p.add(b.box("lr_front", (1.0, y1 - y0, h), (x, (y0 + y1) / 2, 0.0), 0.05, 1, _m(wall, 0.9)))
    for f in range(1, int(h / 3.2)):
        for k in range(int((y1 - y0) / 2.2)):
            y = y0 + 1.1 + k * 2.2
            lit = rng.random() < 0.22
            p.add(b.box("lr_win", (0.06, 1.0, 1.3), (x + side * 0.52, y, f * 3.2 + 0.6), 0.02, 1,
                        _m("#FFB070", 0.4, emit=2.5) if lit else _m("#1E2124", 0.2, 0.3)))
    for k in range(int((y1 - y0) / 4)):
        if rng.random() < 0.4:
            p.add(b.box("lr_streak", (0.04, 0.5, rng.uniform(2, 6)), (x + side * 0.51, y0 + rng.uniform(0, y1 - y0),
                                                                     rng.uniform(2, 8)), 0, 1, _m("#1A1816", 0.9)))


def frame_low_ring(time_id):
    p = b.Parts()
    rng = random.Random(101)
    glow = b._glow / 4.0
    # Ground: wet dark asphalt, a gutter, puddles.
    p.add(b.box("lr_street", (40.0, 120.0, 0.2), (0.0, 40.0, -0.2), 0, 1, _m("#26282A", 0.35, 0.2)))
    for sx in (-1, 1):
        p.add(b.box("lr_kerb", (2.6, 120.0, 0.18), (sx * 5.7, 40.0, 0.0), 0.03, 1, _m("#3A3734", 0.8)))
    for k in range(14):
        mess.puddle(p, (rng.uniform(-4.0, 4.0), rng.uniform(-2.0, 50.0), 0.005), rng.uniform(0.5, 1.6), seed=1010 + k)
    # Street fronts on both sides, the tower feet above them, a deck overhead with a single gap.
    for sx, x in ((-1, -7.0), (1, 7.0)):
        y = -6.0
        while y < 80.0:
            w = rng.uniform(7.0, 11.0)
            _facade(p, x, y, y + w, rng.uniform(12.0, 18.0), -sx, rng)
            y += w + 0.2
    for k, (x, y) in enumerate(((-18.0, 20.0), (18.0, 36.0), (-18.0, 62.0), (18.0, 84.0))):
        p.add(b.cyl("lr_tower", 9.0, 80.0, (x, y, 0.0), 32, material=_m("#2F2C29", 0.85)))
        for j in range(6):
            p.add(b.torus("lr_tower_ring", 9.05, 0.2, (x, y, 14.0 + j * 10.0), 32, 4, _m("#4A4540", 0.6, 0.4)))
    for k in range(3):
        y = 4.0 + k * 26.0
        p.add(b.box("lr_deck", (40.0, 20.0, 2.2), (0.0, y, 26.0), 0.2, 1, _m("#2B2826", 0.85)))
        for j in range(6):
            p.add(b.box("lr_deck_beam", (40.0, 0.6, 1.0), (0.0, y - 9.0 + j * 3.6, 25.2), 0, 1, _m("#3A3734", 0.7)))
    # A far bright gap: the Upper Town up there, warm windows and a lit dome rib.
    p.add(b.box("far_upper", (60.0, 2.0, 40.0), (0.0, 112.0, 20.0), 0, 1, _m("#7F97A8", 0.6, emit=1.2)))
    for k, (x, w, h) in enumerate(((-12.0, 7.0, 34.0), (-3.0, 5.0, 26.0), (7.0, 8.0, 40.0), (15.0, 5.0, 22.0))):
        p.add(b.box("far_tower", (w, 2.0, h), (x, 108.0, 14.0), 0.4, 2, _m("#14171A", 0.8)))
        for j in range(int(h / 3)):
            if (j + k) % 3:
                p.add(b.box("far_tower_win", (w * 0.7, 0.1, 0.4), (x, 106.9, 15.5 + j * 3.0), 0, 1,
                            _m("#FFD9A0", 0.4, emit=5.0)))
    p.add(b.box("far_bridge", (40.0, 2.0, 1.4), (0.0, 107.5, 30.0), 0.2, 1, _m("#F3E9D2", 0.5, emit=1.5)))
    for k in range(40):
        p.add(b.box("far_upper_win", (0.9, 0.1, 1.2), (rng.uniform(-26, 26), 111.0, rng.uniform(24, 56)), 0, 1,
                    _m("#FFD9A0", 0.4, emit=6.0)))
    # Left side: the betting hall (ODDS), then the dream booths.
    lowring.neon(p, (-6.3, 6.0, 7.5), "ODDS", "#E58AA6", size=1.4, yaw=90, glow=glow)
    lowring.neon(p, (-6.3, 6.0, 5.4), "DRONE RACES TONIGHT", "#5FD3C4", size=0.5, yaw=90, glow=glow)
    for k in range(3):
        lowring.screen(p, (-6.35, 3.0 + k * 3.0, 1.2), 2.4, 1.6, yaw=90, glow=glow, seed=1020 + k,
                       text=("RACE 7", "LANE 3  4:1", "LAST CALL")[k])
    scene.point_light("odds_glow", (-4.5, 6.0, 4.0), "#E58AA6", 160, 0.5)
    scene.point_light("screen_glow", (-4.8, 6.0, 1.8), "#5FD3C4", 90, 0.5)
    concept.crowd([(-4.2, 3.5, -90), (-4.0, 6.2, -100), (-4.6, 8.8, -80)], seed=1030)
    lowring.neon(p, (-6.3, 19.0, 6.0), "DREAM BOOTHS  3 CR / HR", "#8FA6FF", size=0.55, yaw=90, glow=glow)
    lowring.dream_booth(p, (-4.6, 17.0, 0.0), 90, glow=glow)
    lowring.dream_booth(p, (-4.6, 20.5, 0.0), 90, glow=glow, asleep=False)
    scene.point_light("booth_glow", (-3.8, 18.8, 1.6), "#6E8BFF", 70, 0.6)
    lowring.neon(p, (-6.3, 33.0, 9.5), "HA EN NINE", "#E3AE45", size=1.0, yaw=90, dead=(2, 8), glow=glow)
    mess.poster(p, (-6.4, 27.0, 1.4), 1.3, 1.8, ("#F3E9D2", "#E3AE45", "#2F6E5E", "#C8553D"),
                "NO ONE WILL EVER GO WITHOUT", rot=90)
    lowring.pipes(p, (-6.4, -4.0), (-6.4, 46.0), 3.2, n=3, seed=1040)
    # Right side: the soup stall (the one warm spot), a queue; broken robots and the scrap pile.
    climb.food_cart((4.4, 10.0, 0.0), 90, "#C8553D", seed=5)
    p.add(b.text("soup_txt", "HOT SOUP - MADE BY HAND", 0.34, (5.8, 10.0, 3.4), rad(90, 0, -90), _m("#F3E9D2", 0.5),
                 0.01))
    scene.point_light("soup_lamp", (3.6, 10.0, 2.8), "#FFC98A", 260, 0.4)
    looks = scene3._looks()
    character.person(looks["cook"], (5.2, 10.6, 0.0), -90, "cook")
    concept.crowd([(2.8, 9.6, 90), (2.4, 7.8, 70), (2.6, 6.0, 80), (2.2, 4.3, 90), (2.9, 2.6, 100)], seed=1050)
    lowring.broken_runner(p, (5.6, 17.5, 0.0), -100, lean=-22.0, seed=1060, glow=glow)
    scene.point_light("runner_screen", (4.9, 17.4, 1.3), "#5FD3C4", 40, 0.2)
    scene.point_light("scrap_lamp", (3.6, 26.0, 3.0), "#FFA040", 120, 0.3)
    sweep = concept.robot_sweeper(p, (3.4, 20.0, 0.45), 30, body="#B9AE98", band="#7A4A30", seed=1061)
    sweep.rotation_euler = rad(170, 10, 30)
    lowring.scrap_pile(p, (5.0, 24.5, 0.0), 2.6, 50, seed=1062)
    lowring.overflow_bin(p, (5.4, 29.0, 0.0), 0, seed=1063)
    lowring.overflow_bin(p, (5.4, 30.4, 0.0), 0, seed=1064)
    lowring.neon(p, (6.3, 36.0, 7.0), "CREDIT SLOTS", "#E58436", size=0.8, yaw=-90, dead=(7,), glow=glow)
    lowring.pipes(p, (6.4, -4.0), (6.4, 50.0), 4.4, n=2, seed=1065)
    future.bench(p, (5.4, 1.0, 0.0), -90, "#4A4540")
    sleeper = concept.random_look(1070)
    sleeper.sit = True
    character.person(sleeper, (5.2, 1.0, 0.0), -90, "sleeper")
    for k, y in enumerate((14.0, 38.0, 54.0)):
        lowring.vent_steam(p, (rng.uniform(-2.0, 2.0), y, 0.0), 3.5, seed=1080 + k)
    # Sodium lamps (one dead), and the one shaft of light through the deck gap far down the street.
    for k, y in enumerate((-2.0, 12.0, 26.0, 40.0, 54.0)):
        sx = -1 if k % 2 else 1
        head = lowring.sodium_lamp(p, (sx * 4.9, y, 0.0), 5.0, 180 if sx > 0 else 0, broken=k == 2, glow=glow)
        if k != 2:
            scene.point_light("sodium", head, lowring.SODIUM, 320, 0.2)
    shaft = scene.searchlight("shaft", (1.0, 43.0, 70.0), (0.0, 43.0, 0.0), 400000)
    shaft.data.spot_size = math.radians(9)
    shaft.data.color = (0.75, 0.85, 1.0)
    for k in range(4):
        future.drone(p, (rng.uniform(-3, 3), 20.0 + k * 9.0, rng.uniform(9, 14)), rng.uniform(0, 360), package=False)
    scene.add_sun(time_id)
    cam = scene.camera((-1.2, -9.0, 1.65), (1.2, 30.0, 4.4), lens=20)
    cam.data.clip_end = 400.0


FRAMES = {  # name: (builder, light mood, grit, ink width, ink fade (near, far) or None)
    "dome_air": (frame_dome_air, "calm", 0.4, 0.8, (1200.0, 2600.0)),
    "dome_night": (frame_dome_night, "dome_night", 0.5, None, None),
    "wreck_wall": (frame_wreck_wall, "dusk_storm", 0.7, 1.0, (60.0, 260.0)),
    "lake_wall": (frame_lake_wall, "lake", 0.5, 1.0, (60.0, 300.0)),
    "train_gate": (frame_train_gate, "morning", 0.6, 1.0, (40.0, 200.0)),
    "low_ring": (frame_low_ring, "night", 0.95, None, None),
}


def render(name, path, samples=64, scale=1.0, ink=True):
    fn, time_id, grit, ink_w, fade_range = FRAMES[name]
    b.reset("P1")
    b.set_grit(grit)
    b.set_glow(scene.TIMES[time_id]["glow"])
    scene.setup_render(samples, scale)
    scene.setup_world(time_id)
    fn(time_id)
    if ink and ink_w:  # the two night frames read better without outlines
        scene4.illustration_look(ink_w * (1.1 if scale >= 1 else 0.7))
        scene4._no_ink_for(["haze", "far_", "land", "water", "pond", "steam", "lr_win", "neon_ch"])
        if fade_range:
            style = bpy.context.view_layer.freestyle_settings.linesets[0].linestyle
            fade = style.alpha_modifiers.new("fade", "DISTANCE_FROM_CAMERA")
            fade.range_min, fade.range_max = fade_range
            fade.invert = True
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
