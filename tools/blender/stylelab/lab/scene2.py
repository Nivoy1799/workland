"""Round 2 shots for the style lab (spec 046): people, hero models, themes. Uses the round 1 light and render setup."""

import math

import bpy

from . import build as b
from . import character, kitchen, palettes, people, scene, street, themes
from .shapes import SOFT, STREAM, shape_for


def _room(p, wall_role="wall", floor=True, width=10.0):
    p.add(b.box("wall", (width, 0.1, 3.2), (0, 1.0, 0), 0, 1, b.mat(wall_role, rough=0.85)))
    if floor:
        kitchen.floor_tiles(p, -width / 2, width / 2, -4.0, 1.0)


def _cast_looks():
    pal = palettes.PALETTES[b.palette_id()]
    # Round 3 (grit above 0): the same people on a normal working day.
    return character.cast_lived(pal) if b.grit() > 0 else character.cast(pal)


def shot_cast(variant, time_id):
    """The four people side by side, front view."""
    p = b.Parts()
    _room(p)
    looks = _cast_looks()
    for x, key in zip((-1.05, -0.35, 0.35, 1.05), ("older_woman", "cook", "younger", "older_man")):
        character.person(looks[key], (x, 0.0, 0.0), 0, key)
    kitchen.plant(p, (2.0, 0.6, 0.0), seed=9, scale=3.0)
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((0.0, -4.6, 1.15), (0.0, 0.0, 0.92), lens=38)


def shot_turn(variant, time_id, who="cook"):
    """One person from the front, three-quarter, side and back."""
    p = b.Parts()
    _room(p)
    look = _cast_looks()[who]
    for x, yaw in zip((-1.2, -0.4, 0.4, 1.2), (0, 40, 90, 180)):
        character.person(look, (x, 0.0, 0.0), yaw, f"{who}_{yaw}")
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((0.0, -4.8, 1.05), (0.0, 0.0, 0.92), lens=38)


def shot_faces(variant, time_id):
    """Head and shoulders of the four people."""
    p = b.Parts()
    _room(p, floor=False)
    looks = _cast_looks()
    for x, key in zip((-0.6, -0.2, 0.2, 0.6), ("older_woman", "cook", "younger", "older_man")):
        lk = looks[key]
        # Line the faces up: shift taller people down a little so all heads sit near the same height.
        character.person(lk, (x, 0.0, 1.70 - lk.height), 0, key)
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((0.0, -2.4, 1.6), (0.0, 0.0, 1.55), lens=45)


def _kitchen(variant, theme_id="default", person=True):
    """The kitchen corner with the hero stove, pot, pan, food and (optionally) the cook."""
    from . import hero
    interior, small = shape_for(variant, "interior"), shape_for(variant, "small")
    k = kitchen.kitchen_corner(interior, small, with_room=True, stove_fn=hero.stove, plant_on=theme_id == "default")
    k.place("Kitchen")
    kitchen.pot(small, (-0.9 + 0.75, 0.18, kitchen.WORKTOP_H + 0.03))
    kitchen.pan(small, (-0.9 + 1.05, 0.44, kitchen.WORKTOP_H + 0.03), yaw=-30)
    people.food_board((0.6, 0.25, kitchen.WORKTOP_H)).place("Food")
    if theme_id != "default":
        p = b.Parts()
        themes.kitchen_props(p, theme_id, kitchen.WORKTOP_H)
    kitchen.window_occluder(-2.2, -3.0, 0.8)
    for x0, w in ((-0.9, 0.58), (0.3, 1.16)):
        b.box("strip", (w - 0.04, 0.02, 0.01), (x0 + w / 2, 0.45, 1.49), 0, 1, b.lamp(2.0))
    if person:
        character.person(_cast_looks()["cook"], (1.95, 0.05, 0.0), -30, "cook")


def _kitchen_sun(time_id):
    sun = scene.add_sun(time_id)
    el = math.radians(30)
    from mathutils import Vector
    src = Vector((-math.cos(el) * 0.85, -math.cos(el) * 0.5, math.sin(el)))
    sun.rotation_euler = (-src).to_track_quat("-Z", "Y").to_euler()
    if time_id != "noon":
        scene.area_light("ceiling", (0.3, -1.2, 2.9), (1.6, 0.8), "#FFD3A0", 110 if time_id == "night" else 50)


def shot_stove(variant, time_id):
    """Close three-quarter view of the hero stove in its kitchen corner."""
    _kitchen(variant, person=False)
    _kitchen_sun(time_id)
    scene.camera((0.55, -1.45, 1.35), (-0.55, 0.3, 0.72), lens=35)


def shot_kitchen(variant, time_id, theme_id="default"):
    _kitchen(variant, theme_id)
    _kitchen_sun(time_id)
    scene.camera((0.75, -3.0, 1.62), (0.35, 0.4, 1.15), lens=24)


def _street_ground(p):
    street.ground(p, -30, 30)


def shot_car(variant, time_id):
    """A car three-quarter from the front, parked at the kerb in front of a plain wall."""
    from . import hero
    p = b.Parts()
    _street_ground(p)
    p.add(b.box("backwall", (30, 0.3, 4.0), (0, 1.5, 0), 0, 1, b.mat("wall_out", rough=0.9)))
    if variant == "S1":
        hero.car_soft((0.0, -1.6, 0.0), yaw=150)
    else:
        hero.car_streamline((0.0, -1.6, 0.0), yaw=150)
    scene.add_sun(time_id, azimuth_offset=-20)
    scene.camera((3.4, -7.0, 1.5), (-0.1, -1.5, 0.7), lens=40)


def shot_shop(variant, time_id, theme_id="default", pal="P1"):
    """One shop front, three-quarter at eye height, with the cook in the door and a customer."""
    from . import hero
    p = b.Parts()
    _street_ground(p)
    theme = themes.THEMES[theme_id]() if theme_id != "default" else themes.default_theme(pal)
    hero.shop_front(shape_for(variant, "street"), theme, (0, 0, 0))
    looks = _cast_looks()
    character.person(looks["older_woman"], (-1.0, -1.4, 0.0), -20, "woman")
    scene.add_sun(time_id, azimuth_offset=-25)
    scene._night_extras(time_id, lamps=[(-1.0, 1.0, 2.6)], searchlights=[((-3.0, 6.0, 0.2), (-25, 60, 70))])
    scene.camera((3.8, -8.2, 1.65), (-0.2, 0.0, 1.75), lens=30)


def shot_themes(variant, time_id):
    """Three shops in a row: the default, Casa Nopal (Mexican-inspired) and Yuzu Noodles (Japanese-inspired)."""
    from . import hero
    p = b.Parts()
    _street_ground(p)
    shape = shape_for(variant, "street")
    for x, theme in ((-6.6, themes.default_theme("P1")), (0.0, themes.mex_theme()), (6.6, themes.jap_theme())):
        hero.shop_front(shape, theme, (x, 0, 0))
    scene.add_sun(time_id, azimuth_offset=-15)
    scene._night_extras(time_id, lamps=[(-6.6, 1.0, 2.6), (0, 1.0, 2.6), (6.6, 1.0, 2.6)],
                        searchlights=[((-3.0, 6.0, 0.2), (-25, 60, 70)), ((3.0, 7.0, 0.2), (18, 60, 70))])
    scene.camera((0.0, -17.5, 2.2), (0.0, 0.0, 1.9), lens=30)


def shot_street(variant, time_id):
    """The full street: three themed shops, both cars, the cast on the pavement, lamps and trees."""
    from . import hero
    p = b.Parts()
    _street_ground(p)
    shape = shape_for(variant, "street")
    for x, theme in ((-6.6, themes.mex_theme()), (0.0, themes.default_theme("P1")), (6.6, themes.jap_theme())):
        hero.shop_front(shape, theme, (x, 0, 0))
    hero.car_streamline((1.2, -4.3, -0.12), yaw=160)
    hero.car_soft((-6.0, -4.3, -0.12), yaw=20, colour_role="main")
    looks = _cast_looks()
    character.person(looks["cook"], (1.6, -0.6, 0.0), -10, "cook")
    character.person(looks["older_woman"], (2.6, -1.6, 0.0), -60, "woman")
    character.person(looks["older_man"], (3.3, -1.3, 0.0), -110, "man")
    character.person(looks["younger"], (-3.4, -1.8, 0.0), 30, "younger")
    street.street_lamp(STREAM if variant != "S1" else SOFT, (-3.3, -2.8, 0.0))
    street.street_lamp(STREAM if variant != "S1" else SOFT, (3.6, -2.8, 0.0))
    street.tree(STREAM if variant != "S1" else SOFT, (9.9, -2.2, 0.0))
    scene.add_sun(time_id, azimuth_offset=-10)
    lamp_z = 3.42 if variant != "S1" else 3.62
    scene._night_extras(time_id, lamps=[(-3.3, -2.8, lamp_z - 0.25), (3.6, -2.8, lamp_z - 0.25), (0, 1.0, 2.6),
                                        (-6.6, 1.0, 2.6), (6.6, 1.0, 2.6)],
                        searchlights=[((-3.0, 6.0, 0.2), (-25, 60, 70)), ((3.0, 7.0, 0.2), (18, 60, 70))])
    scene.camera((6.5, -15.0, 1.9), (-0.6, 0.0, 1.7), lens=30)


SHOTS = {
    "cast": shot_cast,
    "turn-cook": lambda v, t: shot_turn(v, t, "cook"),
    "turn-woman": lambda v, t: shot_turn(v, t, "older_woman"),
    "turn-man": lambda v, t: shot_turn(v, t, "older_man"),
    "faces": shot_faces,
    "stove": shot_stove,
    "kitchen": shot_kitchen,
    "kitchen-mex": lambda v, t: shot_kitchen(v, t, "mex"),
    "kitchen-jap": lambda v, t: shot_kitchen(v, t, "jap"),
    "car": shot_car,
    "shop": shot_shop,
    "shop-mex": lambda v, t: shot_shop(v, t, "mex"),
    "shop-jap": lambda v, t: shot_shop(v, t, "jap"),
    "themes": shot_themes,
    "street": shot_street,
}


def render(variant, palette_id, shot, time_id, path, samples=64, scale=1.0, theme=None):
    if shot in ("kitchen-mex", "kitchen-jap"):
        palette_id = themes.KITCHEN_PALETTE[shot.split("-")[1]]
    b.reset(palette_id)
    b.set_glow(scene.TIMES[time_id]["glow"])
    scene.setup_render(samples, scale)
    scene.setup_world(time_id)
    SHOTS[shot](variant, time_id)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
