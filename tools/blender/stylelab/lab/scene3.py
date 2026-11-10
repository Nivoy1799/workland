"""Round 3 shots for the style lab (spec 046): the lived-in pass and the climb in pictures (docs/game-design.md)."""

import math
import random

import bpy

from . import build as b
from . import character, climb, hero, kitchen, mess, palettes, scene, scene2, street, themes
from .shapes import STREAM, shape_for


def _looks():
    return scene2._cast_looks()


# --- mess layouts ----------------------------------------------------------------------------------------------------

def kitchen_rush_mess(p):
    """Mid-rush: sauce on the tiles, stains and flour on the worktop, dishes, boxes on the floor, order tickets."""
    W = kitchen.WORKTOP_H
    tile_y = kitchen.DEPTH + 0.0085
    mess.splatter(p, (0.0, tile_y, 1.08), 0.28, 26, "#B8452F", "y", 31, 0.75)
    mess.splatter(p, (-0.15, tile_y, 1.0), 0.18, 14, "#8E5A3A", "y", 32, 0.6)
    mess.stain(p, (0.45, 0.2, W + 0.0012), 0.06, "#8E5A3A", "z", 33)
    mess.stain(p, (1.05, 0.4, W + 0.0012), 0.045, "#B8452F", "z", 34)
    mess.splatter(p, (0.85, 0.22, W + 0.0012), 0.12, 18, "#EDE6D3", "z", 35, 0.9)
    mess.dish_stack(p, (1.2, 0.33, W), 6, 36)
    mess.dish_stack(p, (1.38, 0.18, W), 3, 37)
    towel = b.sphere("towel", 0.09, (0.95, 0.12, W + 0.02), 12, (1.2, 0.8, 0.25), mess._m("#E58436", 0.85))
    towel.rotation_euler = b.rad(0, 0, 25)
    p.add(towel)
    mess.cardboard_box(p, (0.9, -0.6, 0.0), (0.45, 0.35, 0.3), 18, True, 38)
    mess.fruit_crate_full(p, (-1.45, -0.35, 0.0), -10, "#A57A4C", 39)
    mess.trash_bag(p, (-1.6, 0.25, 0.0), 40)
    for k, (x, y, r) in enumerate(((0.05, -0.45, 0.09), (-0.4, -0.9, 0.06), (0.6, -0.25, 0.05))):
        mess.stain(p, (x, y, 0.0015), r, "#6A5038", "z", 41 + k, 0.45)
    p.add(b.cyl("dropped_slice", 0.03, 0.006, (0.3, -0.7, 0.001), 14, material=mess._m("#E0664E", 0.4)))
    mess.paper_sign(p, (-1.2, tile_y - 0.002, 1.2), ["#12 hash x2", "no onion!"], 0.18, 0.14, 4, seed=43, size=0.03)
    mess.paper_sign(p, (-1.2, tile_y - 0.002, 1.02), ["#13 green", "sauce xtra"], 0.18, 0.14, -3, seed=44, size=0.03)


def street_mess(p, shop_xs=(-6.6, 0.0, 6.6), signs=True):
    """A lived-in pavement: cracks, weeds, gum, a puddle, bins and bags in the gaps, crates by a door, paper signs."""
    rng = random.Random(50)
    for k in range(9):
        mess.crack(p, (rng.uniform(-11, 11), rng.uniform(-2.9, -0.6), 0.001), rng.uniform(0.4, 0.9), "z", 51 + k,
                   "#9A9080", 0.008)
    for k in range(10):
        mess.weeds(p, (rng.uniform(-11, 11), -3.08, 0.0), rng.randrange(4, 9), 60 + k)
    for x in shop_xs:
        for k in range(3):
            mess.weeds(p, (x + rng.uniform(-2.8, 2.8), -0.12, 0.0), 5, 70 + k + int(x), 0.1)
    mess.gum(p, -10, 10, -2.9, -0.5, 0.001, 30, 80)
    mess.puddle(p, (2.1, -2.7, 0.001), 0.45, 81)
    mess.dented_bin(p, (3.3, -0.25, 0.0), 82)
    mess.trash_bag(p, (-3.25, -0.3, 0.0), 83)
    mess.trash_bag(p, (-3.45, -0.05, 0.0), 84, "#3A4A3E", 0.22)
    mess.crate(p, (2.75, -0.35, 0.0), 8, seed=85)
    mess.crate(p, (2.75, -0.35, 0.3), -6, seed=86)
    if signs:
        mess.paper_sign(p, (-1.6, -0.012, 1.45), ["WE'RE HIRING", "ask inside :)"], 0.32, 0.22, -3, seed=87, size=0.04)
        mess.paper_sign(p, (1.6, -0.085, 1.2), ["CARD READER", "BROKEN", "cash only, sorry"], 0.3, 0.25, 2, seed=88,
                        size=0.036)
    mess.crack(p, (-2.7, -0.003, 1.1), 0.5, "y", 89, "#8A7A66", 0.005)
    mess.crack(p, (4.3, -0.003, 0.7), 0.4, "y", 90, "#8A7A66", 0.005)


# --- shots -----------------------------------------------------------------------------------------------------------

def shot_kitchen_rush(variant, time_id):
    scene2._kitchen(variant, "default")
    p = b.Parts()
    kitchen_rush_mess(p)
    scene2._kitchen_sun(time_id)
    scene.camera((0.95, -2.9, 1.55), (0.25, 0.4, 1.1), lens=24)


def shot_faces_expr(variant, time_id):
    """One face, six expressions: smile, grin, neutral, focused, tired, yawn."""
    p = b.Parts()
    p.add(b.box("wall", (8.0, 0.1, 3.2), (0, 1.0, 0), 0, 1, b.mat("wall", rough=0.85)))
    look = _looks()["helper"]
    for k, ex in enumerate(("smile", "grin", "neutral", "focused", "tired", "yawn")):
        lk = character.Look(**{**look.__dict__, "expression": ex, "apron": "", "prop": ""})
        character.person(lk, (-1.0 + k * 0.4, 0.0, 1.70 - lk.height), 0, ex)
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((0.0, -3.0, 1.62), (0.0, 0.0, 1.57), lens=40)


def shot_cast_lived(variant, time_id):
    p = b.Parts()
    p.add(b.box("wall", (10.0, 0.1, 3.2), (0, 1.0, 0), 0, 1, b.mat("wall", rough=0.85)))
    kitchen.floor_tiles(p, -5, 5, -4, 1.0)
    looks = _looks()
    for x, key, yaw in zip((-1.4, -0.7, 0.0, 0.7, 1.4), ("older_woman", "cook", "helper", "younger", "older_man"),
                           (8, -5, 4, -10, -6)):
        character.person(looks[key], (x, 0.0, 0.0), yaw, key)
    mess.crate(p, (2.3, 0.4, 0.0), 10, seed=3)
    mess.cardboard_box(p, (2.3, 0.4, 0.3), (0.4, 0.3, 0.3), -8, True, 4)
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((0.3, -5.4, 1.2), (0.1, 0.0, 0.95), lens=36)


def _worn_wall(p, x0, x1, y=1.6, h=3.4):
    """A long plain wall with faded posters, cracks, a drain pipe and weeds at its foot."""
    p.add(b.box("worn_wall", (x1 - x0, 0.3, h), ((x0 + x1) / 2, y + 0.15, 0.0), 0, 1, b.mat("wall_out", rough=0.9)))
    p.add(b.box("worn_plinth", (x1 - x0, 0.32, 0.45), ((x0 + x1) / 2, y + 0.15, 0.0), 0, 1, b.mat("trim", rough=0.8)))
    mess.poster(p, (-2.4, y - 0.002, 1.25), 0.6, 0.85, ["#F2D9B8", "#E58436", "#4F9A85", "#C9734F"], "TOWN FAIR", 2, 91)
    mess.poster(p, (-1.65, y - 0.002, 1.4), 0.42, 0.6, ["#DCE6E9", "#E3AE45", "#4E6E8E", "#8CC5AC"], "SWAP MARKET",
                -4, 92)
    mess.paper_sign(p, (1.9, y - 0.003, 1.35), ["LOST: grey cat", "answers to Biscuit"], 0.28, 0.2, 5, seed=93,
                    size=0.03)
    mess.crack(p, (2.6, y - 0.003, 2.0), 0.7, "y", 94, "#8A7A66", 0.006)
    mess.crack(p, (-3.3, y - 0.003, 0.9), 0.5, "y", 95, "#8A7A66", 0.006)
    mess.drip_pipe(p, 3.2, y - 0.03, h)
    for k in range(8):
        mess.weeds(p, (x0 + 0.5 + k * (x1 - x0 - 1) / 8, y - 0.04, 0.0), 6, 96 + k, 0.16)


def shot_stage1(variant, time_id):
    """Stage 1, the food corner: a second-hand cart against a worn wall, two cooks and the first guests."""
    p = b.Parts()
    street.ground(p, -30, 30)
    _worn_wall(p, -8, 8)
    climb.food_cart((0.0, -0.4, 0.0), 0)
    looks = _looks()
    character.person(looks["cook"], (-0.35, 0.55, 0.0), 0, "cook")
    character.person(looks["helper"], (0.5, 0.62, 0.0), -10, "helper")
    character.person(looks["older_woman"], (-0.95, -1.45, 0.0), 165, "woman")
    character.person(looks["older_man"], (-1.8, -2.1, 0.0), 145, "man")
    character.person(looks["younger"], (2.5, -1.3, 0.0), 125, "younger")
    rng = random.Random(97)
    mess.gum(p, -6, 6, -2.9, 1.3, 0.001, 25, 98)
    for k in range(5):
        mess.crack(p, (rng.uniform(-5, 5), rng.uniform(-2.8, 1.0), 0.001), rng.uniform(0.4, 0.8), "z", 99 + k,
                   "#9A9080", 0.008)
    street.street_lamp(STREAM, (-3.4, -2.8, 0.0))
    scene.add_sun(time_id, azimuth_offset=-20)
    scene._night_extras(time_id, lamps=[(0.66, -0.12, 2.2), (-3.4, -2.8, 3.17)])
    scene.camera((3.4, -7.2, 1.6), (-0.2, 0.0, 1.05), lens=30)


def _laundry_theme():
    t = themes.default_theme("P1")
    t.update({"name": "LUCKY LAUNDRY", "sign": "#9AA39A", "letters": "#E6E2D6", "trim": "#7E8C84",
              "accent": "#B9A58C", "door": "#9AA8A0", "flowers": ["#8F8A84"], "props": None,
              "inside": "#D8D2C6", "counter": "#B9B2A4", "jars": ["#C9C2B4", "#B9B2A4"]})
    return t


def shot_stage2(variant, time_id):
    """Stage 2, the first rented place: an old laundry, worn, with a paper banner over the old sign."""
    p = b.Parts()
    street.ground(p, -30, 30)
    shape = shape_for(variant, "street")
    hero.shop_front(shape, _laundry_theme(), (0, 0, 0), show_interior=True)
    mess.paper_sign(p, (0.2, -0.2, 2.62), ["TOMORROW KITCHEN", "opening soon!"], 1.9, 0.5, -2, seed=101, size=0.13)
    mess.paper_sign(p, (-1.9, -0.012, 1.3), ["RENTED!", "new owners :)"], 0.36, 0.26, 4, seed=102, size=0.05)
    # Moving in: boxes, crates, a ladder, a paint tin, trash bags from clearing out.
    mess.cardboard_box(p, (2.7, -0.6, 0.0), (0.5, 0.4, 0.35), 10, False, 103, "KITCHEN")
    mess.cardboard_box(p, (2.75, -0.62, 0.35), (0.42, 0.34, 0.3), -6, True, 104)
    mess.cardboard_box(p, (-2.7, -0.7, 0.0), (0.55, 0.4, 0.4), -12, False, 105, "pots")
    mess.crate(p, (-2.0, -0.9, 0.0), 20, seed=106)
    for side in (-1, 1):
        rail = b.box("ladder_rail", (0.05, 0.05, 2.6), (-0.65 + side * 0.22, -0.55, 0.0), 0.01, 1, mess._m("#B98B5E", 0.8))
        rail.rotation_euler = b.rad(-14, 0, 0)
        p.add(rail)
    for k in range(7):
        rung = b.box("ladder_rung", (0.44, 0.04, 0.035), (-0.65, -0.55 + 0.085 * k, 0.3 + k * 0.33), 0.008, 1,
                     mess._m("#B98B5E", 0.8))
        p.add(rung)
    p.add(b.cyl("paint", 0.12, 0.2, (-0.25, -0.75, 0.0), 16, edge=0.01, material=mess._m("#E2EADD", 0.5)))
    mess.splatter(p, (-0.3, -0.85, 0.001), 0.25, 12, "#E2EADD", "z", 107, 0.95)
    mess.trash_bag(p, (3.4, -0.3, 0.0), 108)
    mess.trash_bag(p, (3.6, 0.0, 0.0), 109, "#3A4A3E", 0.22)
    rng = random.Random(110)
    for k in range(6):
        mess.crack(p, (rng.uniform(-5, 5), rng.uniform(-2.8, -0.5), 0.001), rng.uniform(0.4, 0.9), "z", 111 + k,
                   "#9A9080", 0.008)
        mess.weeds(p, (rng.uniform(-3, 3), -0.12, 0.0), 6, 120 + k, 0.14)
    mess.crack(p, (-2.6, -0.003, 1.9), 0.6, "y", 130, "#8A7A66", 0.006)
    mess.crack(p, (2.5, -0.003, 0.9), 0.5, "y", 131, "#8A7A66", 0.006)
    looks = _looks()
    character.person(looks["cook"], (1.5, -1.1, 0.0), -15, "cook")
    character.person(looks["helper"], (-1.2, -1.3, 0.0), 25, "helper")
    scene.add_sun(time_id, azimuth_offset=-25)
    scene.camera((3.6, -8.4, 1.65), (-0.2, 0.0, 1.6), lens=30)


def shot_stage3(variant, time_id):
    """A later stage: the same shop kept up, a delivery van at the kerb, a queue, still lived-in."""
    p = b.Parts()
    street.ground(p, -30, 30)
    shape = shape_for(variant, "street")
    hero.shop_front(shape, themes.default_theme("P1"), (0, 0, 0))
    climb.delivery_van((-1.6, -4.25, -0.12), 0)
    looks = _looks()
    character.person(looks["cook"], (1.55, -0.55, 0.0), -10, "cook")
    character.person(looks["older_woman"], (2.4, -1.5, 0.0), -60, "woman")
    character.person(looks["older_man"], (3.1, -1.9, 0.0), -80, "man")
    character.person(looks["younger"], (3.8, -2.3, 0.0), -95, "younger")
    character.person(looks["helper"], (-1.3, -3.0, 0.0), 160, "helper")
    mess.paper_sign(p, (-1.85, -0.012, 1.5), ["TOWN PAPER:", "best hash", "in town!"], 0.3, 0.26, -2, seed=140,
                    size=0.04)
    rng = random.Random(141)
    for k in range(4):
        mess.crack(p, (rng.uniform(-5, 5), rng.uniform(-2.8, -0.5), 0.001), rng.uniform(0.3, 0.7), "z", 142 + k,
                   "#9A9080", 0.008)
    mess.gum(p, -5, 5, -2.9, -0.5, 0.001, 15, 150)
    mess.dented_bin(p, (3.35, -0.25, 0.0), 151, lid_off=False)
    mess.crate(p, (-2.75, -0.45, 0.0), 6, seed=152)
    street.street_lamp(STREAM, (-3.6, -2.8, 0.0))
    scene.add_sun(time_id, azimuth_offset=-25)
    scene._night_extras(time_id, lamps=[(-3.6, -2.8, 3.17), (0, 1.0, 2.6)],
                        searchlights=[((-3.0, 6.0, 0.2), (-25, 60, 70))])
    scene.camera((4.2, -11.0, 1.7), (-0.6, -1.2, 1.4), lens=30)


def shot_street_lived(variant, time_id):
    scene2.shot_street(variant, time_id)
    p = b.Parts()
    street_mess(p)


def shot_cart(variant, time_id):
    p = b.Parts()
    street.ground(p, -30, 30)
    _worn_wall(p, -8, 8)
    climb.food_cart((0.0, -0.4, 0.0), 0)
    scene.add_sun(time_id, azimuth_offset=-30)
    scene._night_extras(time_id, lamps=[(0.66, -0.12, 2.2)])
    scene.camera((2.5, -4.6, 1.75), (-0.1, -0.3, 1.15), lens=30)


def shot_van(variant, time_id):
    p = b.Parts()
    street.ground(p, -30, 30)
    p.add(b.box("backwall", (30, 0.3, 4.0), (0, 1.5, 0), 0, 1, b.mat("wall_out", rough=0.9)))
    climb.delivery_van((0.0, -1.6, 0.0), 150)
    scene.add_sun(time_id, azimuth_offset=-20)
    scene.camera((4.6, -8.4, 1.6), (0.0, -1.5, 1.0), lens=38)


SHOTS = dict(scene2.SHOTS)
SHOTS.update({
    "kitchen-rush": shot_kitchen_rush,
    "faces-expr": shot_faces_expr,
    "cast-lived": shot_cast_lived,
    "stage1": shot_stage1,
    "stage2": shot_stage2,
    "stage3": shot_stage3,
    "street-lived": shot_street_lived,
    "cart": shot_cart,
    "van": shot_van,
})


def render(variant, palette_id, shot, time_id, path, grit=1.0, samples=64, scale=1.0):
    if shot in ("kitchen-mex", "kitchen-jap"):
        palette_id = themes.KITCHEN_PALETTE[shot.split("-")[1]]
    b.reset(palette_id)
    b.set_grit(grit)
    b.set_glow(scene.TIMES[time_id]["glow"])
    scene.setup_render(samples, scale)
    scene.setup_world(time_id)
    SHOTS[shot](variant, time_id)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
