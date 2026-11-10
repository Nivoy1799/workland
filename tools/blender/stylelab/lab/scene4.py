"""Round 4 vibe frames for the style lab (spec 046): how the world feels to play in. Concept look, not final art."""

import math
import random

import bpy
from mathutils import Vector

from . import build as b
from . import character, climb, concept, hero, kitchen, mess, palettes, scene, scene3, street, themes
from .shapes import SOFT, STREAM


def illustration_look(thickness=1.3):
    """Ink outlines (Freestyle) over the render, so the frames read as concept art."""
    sc = bpy.context.scene
    sc.render.use_freestyle = True
    sc.render.line_thickness_mode = "ABSOLUTE"
    sc.render.line_thickness = thickness
    fs = bpy.context.view_layer.freestyle_settings
    fs.crease_angle = math.radians(140)
    ls = fs.linesets[0] if len(fs.linesets) else fs.linesets.new("ink")
    ls.select_by_visibility = True
    ls.select_silhouette = True
    ls.select_border = True
    ls.select_crease = True
    if ls.linestyle is None:
        ls.linestyle = bpy.data.linestyles.new("ink")
    style = ls.linestyle
    style.color = (0.14, 0.1, 0.08)
    style.alpha = 0.75
    style.thickness = thickness


def _no_ink_for(prefixes):
    """Keep helper objects (the haze box) out of the outlines: move them to a collection the line set excludes."""
    col = bpy.data.collections.new("NoInk")
    bpy.context.scene.collection.children.link(col)
    for obj in list(bpy.context.scene.collection.objects):
        if any(obj.name.startswith(pre) for pre in prefixes):
            bpy.context.scene.collection.objects.unlink(obj)
            col.objects.link(obj)
    ls = bpy.context.view_layer.freestyle_settings.linesets[0]
    ls.select_by_collection = True
    ls.collection = col
    ls.collection_negation = "EXCLUSIVE"


def _lamps_area(points, size=(2.0, 1.0), hex_="#FFD9A8", watts=300):
    for loc in points:
        scene.area_light("room", loc, size, hex_, watts)


def _room(p, w, d, h, wall="#F1E6CF", floor_tiles=True, ceiling=True, x0=None):
    x0 = -w / 2 if x0 is None else x0
    p.add(b.box("back_wall", (w, 0.2, h), (x0 + w / 2, d, 0), 0, 1, b.mat("w", rough=0.85, hex_override=wall)))
    p.add(b.box("left_wall", (0.2, d + 4, h), (x0, d / 2 - 2, 0), 0, 1, b.mat("w", rough=0.85, hex_override=wall)))
    p.add(b.box("right_wall", (0.2, d + 4, h), (x0 + w, d / 2 - 2, 0), 0, 1, b.mat("w", rough=0.85, hex_override=wall)))
    if ceiling:
        c = b.box("ceiling", (w, d + 4, 0.2), (x0 + w / 2, d / 2 - 2, h), 0, 1, b.mat("w", rough=0.9, hex_override="#F3EFE4"))
        c.visible_shadow = True
        p.add(c)
    if floor_tiles == "checker":
        # Classic diner floor: cream and dark green squares.
        t = 0.45
        for i in range(int(w / t) + 1):
            for j in range(int((d + 2) / t) + 1):
                col = "#F1E6CF" if (i + j) % 2 == 0 else "#2F6E5E"
                p.add(b.box("check", (t - 0.004, t - 0.004, 0.01), (x0 + (i + 0.5) * t, -2.0 + (j + 0.5) * t, -0.01),
                            0.0, 1, b.mat("f", rough=0.35, hex_override=col)))
    elif floor_tiles:
        kitchen.floor_tiles(p, x0, x0 + w, -2.0, d, 0.4, seed=17)


# --- A. Morning clean-up ---------------------------------------------------------------------------------------------

def frame_morning(time_id):
    p = b.Parts()
    street.ground(p, -30, 30)
    for x, th in ((-6.6, themes.mex_theme()), (0.0, themes.default_theme("P1")), (6.6, themes.jap_theme())):
        hero.shop_front(STREAM, th, (x, 0, 0))
    scene3.street_mess(p, signs=False)
    concept.robot_sweeper(p, (0.6, -2.5, 0.0), 40, seed=1)
    concept.robot_sweeper(p, (3.9, -1.7, 0.0), -70, body="#E3AE45", band="#2F6E5E", seed=2)
    concept.robot_runner(p, (5.2, -0.65, 0.0), 180, tray=False)
    looks = scene3._looks()
    character.person(looks["older_man"], (-2.6, -1.9, 0.0), 70, "man")
    concept.dog(p, (-1.8, -2.2, 0.0), 80)
    concept.hen(p, (2.1, -2.75, 0.0), 120)
    concept.hen(p, (2.55, -2.4, 0.0), 160)
    character.person(looks["cook"], (1.6, -0.55, 0.0), -15, "cook")
    character.person(looks["helper"], (-0.9, -1.2, 0.0), 20, "helper")
    for k, (x, z) in enumerate(((-5.2, 2.75), (-4.9, 2.78), (7.8, 2.6))):
        concept.bird(p, (x, -0.95, z), "#4E6E8E" if k % 2 else "#8A5A3B")
    climb.delivery_van((-3.0, -6.0, -0.12), 180)
    street.street_lamp(STREAM, (-3.3, -2.8, 0.0))
    street.street_lamp(STREAM, (3.6, -2.8, 0.0))
    scene.add_sun(time_id)
    scene.camera((4.6, -7.4, 1.35), (0.0, -1.2, 1.15), lens=24)


# --- B. Automat hall -------------------------------------------------------------------------------------------------

def frame_automat(time_id):
    p = b.Parts()
    W, D, H = 12.0, 6.0, 3.6
    _room(p, W, D, H, wall="#F1E6CF")
    # The automat wall: 8 x 4 little lit windows with a dish in each.
    cols, rows = 9, 4
    x0, z0 = -3.4, 0.75
    rng = random.Random(21)
    green = b.mat("a", rough=0.4, hex_override="#4F9A85")
    p.add(b.box("automat", (cols * 0.55 + 0.3, 0.2, rows * 0.5 + 0.4), (x0 + cols * 0.55 / 2, D - 0.15, z0 - 0.2), 0.05, 2,
                green))
    inside = b.mat("cb", rough=0.6, hex_override="#6A4A34")
    for c in range(cols):
        for r in range(rows):
            cx = x0 + 0.275 + c * 0.55
            cz = z0 + r * 0.5
            # An open box: back, floor, roof and sides (green outside, warm wood inside), a lamp strip at the top.
            p.add(b.box("cubby_back", (0.46, 0.02, 0.4), (cx, D - 0.27, cz), 0.0, 1, inside))
            p.add(b.box("cubby_floor", (0.5, 0.3, 0.03), (cx, D - 0.42, cz - 0.03), 0.0, 1, inside))
            p.add(b.box("cubby_roof", (0.5, 0.3, 0.03), (cx, D - 0.42, cz + 0.4), 0.0, 1, green))
            for sx in (-0.24, 0.24):
                p.add(b.box("cubby_side", (0.03, 0.3, 0.46), (cx + sx, D - 0.42, cz - 0.03), 0.0, 1, green))
            p.add(b.box("cubby_lamp", (0.4, 0.02, 0.02), (cx, D - 0.45, cz + 0.36), 0.0, 1, b.lamp(3.0, "lamp_warm")))
            for fx, fz, fw, fh in ((0.0, -0.02, 0.5, 0.035), (0.0, 0.385, 0.5, 0.035), (-0.232, -0.02, 0.035, 0.44),
                                   (0.232, -0.02, 0.035, 0.44)):
                p.add(b.box("cubby_frame", (fw, 0.03, fh), (cx + fx, D - 0.56, cz + fz), 0.008, 1, b.chrome()))
            p.add(b.box("cubby_glass", (0.42, 0.01, 0.36), (cx, D - 0.58, cz + 0.02), 0.03, 2,
                        hero.clear_glass("#E8F2F4", 0.12)))
            if rng.random() < 0.8:
                concept.plated(p, (cx, D - 0.42, cz + 0.02), rng.choice(list(concept.DISHES)), scale=1.0,
                               seed=c * 7 + r)
            p.add(b.cyl("coin_slot", 0.02, 0.01, (cx + 0.18, D - 0.585, cz + 0.32), 10, material=b.chrome(),
                        rot=b.rad(90)))
    p.add(b.text("automat_sign", "TOMORROW AUTOMAT", 0.28, (x0 + cols * 0.55 / 2, D - 0.56, z0 + rows * 0.5 + 0.45),
                 b.rad(90), b.lamp(2.0, "neon"), 0.02))
    # Kitchen corner on the right: a hero stove with a robot arm stirring, a conveyor feeding the automat.
    k = kitchen.kitchen_corner(SOFT, SOFT, with_room=False, stove_fn=hero.stove, plant_on=False)
    k.place("Kitchen", (3.8, 3.6, 0.0), b.rad(0, 0, 0))
    kitchen.pot(SOFT, (3.8 - 0.9 + 0.75, 3.6 + 0.18, kitchen.WORKTOP_H + 0.03))
    concept.robot_arm(p, (3.8 - 0.9 + 1.25, 3.6 + 0.35, kitchen.WORKTOP_H), (3.8 - 0.9 + 0.75, 3.6 + 0.18,
                                                                             kitchen.WORKTOP_H + 0.14))
    concept.steam(p, (3.8 - 0.9 + 0.75, 3.6 + 0.18, kitchen.WORKTOP_H + 0.3), 10, 3, 0.6)
    p.add(b.box("conveyor", (4.6, 0.5, 0.08), (0.9, 4.9, 0.95), 0.03, 2, b.mat("c", rough=0.6, hex_override="#3A3532")))
    p.add(b.box("conveyor_frame", (4.7, 0.56, 0.95), (0.9, 4.9, 0.0), 0.04, 2, b.chrome()))
    for kx in range(6):
        concept.plated(p, (-1.0 + kx * 0.75, 4.9, 1.03), "bowl", seed=40 + kx)
    # People: a player supervising, a helper refilling, guests choosing.
    looks = scene3._looks()
    character.person(looks["cook"], (2.6, 2.6, 0.0), -40, "cook")
    character.person(looks["helper"], (-3.9, 4.2, 0.0), 150, "helper")
    concept.crowd([(-2.2, 4.3, 185), (-0.7, 4.5, 175), (0.6, 3.6, 200), (-1.5, 2.6, 160)], seed=60)
    concept.robot_sweeper(p, (1.6, 1.4, 0.0), -30, seed=3)
    concept.robot_runner(p, (-3.5, 1.8, 0.0), 60)
    for x in (-3.0, 0.0, 3.0):
        concept.pendant(p, (x, 2.8, H), "#E58436")
    scene.add_sun(time_id)
    _lamps_area([(-2.0, 2.5, H - 0.1), (2.0, 2.5, H - 0.1)], (3.0, 2.0), watts=500)
    scene.camera((0.4, -2.6, 1.7), (0.0, 4.0, 1.4), lens=20)


def clear_glass_light():
    return hero.clear_glass("#E8F2F4", 0.25)


# --- C. The guest's view ---------------------------------------------------------------------------------------------

def frame_guest(time_id):
    p = b.Parts()
    W, D, H = 10.0, 9.0, 3.4
    _room(p, W, D, H, wall="#F1E1C8", floor_tiles="checker")
    # Big windows on the left wall with a sunset street outside (painted backdrop).
    for k in range(3):
        wy = 0.5 + k * 2.6
        p.add(b.box("win_frame", (0.25, 2.1, 2.1), (-W / 2 + 0.05, wy, 0.75), 0.05, 2, b.mat("t", rough=0.4,
                                                                                            hex_override="#2F6E5E")))
        p.add(b.box("win_view", (0.05, 1.9, 1.9), (-W / 2 - 0.02, wy, 0.85), 0.0, 1,
                    b.mat("sky", emit=1.4, rough=0.5, hex_override="#F2A07B")))
        p.add(b.box("win_bldg", (0.04, 0.9, 0.9), (-W / 2 + 0.0, wy - 0.4, 0.85), 0.0, 1,
                    b.mat("sil", rough=0.8, hex_override="#8A5A6A")))
    # The pass at the back: counter, heat lamps, tickets, cooks working, the menu board above.
    p.add(b.box("pass", (5.0, 0.7, 1.1), (1.2, D - 1.2, 0.0), 0.06, 3, b.mat("t", rough=0.45, hex_override="#4F9A85")))
    p.add(b.box("pass_top", (5.1, 0.8, 0.05), (1.2, D - 1.2, 1.1), 0.02, 2, b.chrome()))
    for x in (-0.6, 0.6, 1.8, 3.0):
        p.add(b.lathe("heat_lamp", [(0.0, 0.15), (0.14, 0.0), (0.13, -0.01), (0.0, 0.02)], 20, (x, D - 1.2, 1.9),
                      b.mat("t", rough=0.4, hex_override="#C8553D")))
        p.add(b.sphere("heat_bulb", 0.05, (x, D - 1.2, 1.9), 10, material=b.lamp(3.0, "neon")))
        concept.plated(p, (x, D - 1.15, 1.15), "noodles" if x > 1 else "hash", seed=int(x * 10) + 70)
    concept.ticket_rail(p, (1.2, D - 1.6, 2.35), 9, 4.0)
    concept.menu_board_wall(p, (1.2, D - 0.15, 2.3), 3.2, 0.95)
    looks = scene3._looks()
    character.person(looks["cook"], (0.3, D - 0.6, 0.0), 180, "cook")
    character.person(looks["helper"], (2.4, D - 0.6, 0.0), 200, "helper")
    concept.steam(p, (1.0, D - 0.9, 1.3), 8, 9, 0.8)
    # Booths along the right wall (benches facing each other across a table), round tables with chairs.
    bx = W / 2 - 0.85
    for k in range(3):
        by = 1.2 + k * 2.3
        concept.booth(p, (bx, by + 0.68, 0.0), 0, "#C8553D", 1.4)
        concept.booth(p, (bx, by - 0.68, 0.0), 180, "#C8553D", 1.4)
        concept.table(p, (bx, by, 0.0), r=0.42)
        concept.plated(p, (bx - 0.12, by + 0.15, 0.76), "bowl", seed=80 + k)
        concept.plated(p, (bx + 0.12, by - 0.18, 0.76), "noodles", seed=85 + k)
        concept.drink(p, (bx + 0.25, by + 0.1, 0.76))
        spots = [(bx - 0.3, by + 0.62, 0), (bx + 0.32, by - 0.62, 180)]
        if k != 1:
            spots.append((bx + 0.3, by + 0.62, 0))
        concept.crowd(spots, seed=90 + k * 7, sit=True)
    for k, (tx, ty) in enumerate(((-1.7, 2.5), (0.7, 3.9), (-2.5, 5.4))):
        concept.table(p, (tx, ty, 0.0), r=0.5, seed=k)
        concept.plated(p, (tx - 0.2, ty, 0.76), "salad", seed=100 + k)
        concept.plated(p, (tx + 0.2, ty, 0.76), "hash", seed=103 + k)
        concept.drink(p, (tx, ty + 0.25, 0.76), "#E58436")
        concept.chair(p, (tx - 0.72, ty, 0.0), -90, "#2F6E5E")
        concept.chair(p, (tx + 0.72, ty, 0.0), 90, "#2F6E5E")
        concept.crowd([(tx - 0.62, ty, 90), (tx + 0.62, ty, -90)], seed=110 + k * 5, sit=True)
    # Something on the walls: framed town pictures and plants.
    for k, (x, title, cols) in enumerate(((-3.6, "TOWN FAIR", ["#F2D9B8", "#E58436", "#4F9A85", "#C9734F"]),
                                          (4.3, "GARDEN DOME", ["#DCE6E9", "#8FA77E", "#2F6E5E", "#E3AE45"]))):
        mess.poster(p, (x, D - 0.11, 1.4), 0.7, 0.95, cols, title, 0, 500 + k, faded=False)
    kitchen.plant(p, (-W / 2 + 0.5, D - 1.0, 0.0), seed=31, scale=3.2)
    kitchen.plant(p, (W / 2 - 0.5, 0.0, 0.0), seed=32, scale=3.0)
    concept.robot_runner(p, (-0.2, 1.4, 0.0), 160)
    for x, y in ((-2.0, 2.0), (0.5, 3.0), (3.0, 2.0), (-1.5, 5.5), (1.5, 6.0)):
        concept.pendant(p, (x, y, H), "#E3AE45")
    # Our own table in the foreground: potato hash, lemonade, cutlery, a napkin.
    concept.table(p, (0.2, -0.75, 0.0), top="#F3E9D2", r=0.55)
    concept.plated(p, (0.15, -0.8, 0.76), "hash", scale=1.15, seed=120)
    concept.drink(p, (0.55, -0.6, 0.76), "#F2D27A")
    p.add(b.box("fork", (0.012, 0.18, 0.006), (-0.15, -0.85, 0.76), 0.0, 1, b.chrome()))
    p.add(b.box("napkin", (0.16, 0.16, 0.008), (-0.25, -0.65, 0.76), 0.004, 1, b.mat("t", rough=0.9,
                                                                                    hex_override="#E58436")))
    scene.add_sun(time_id)
    _lamps_area([(-1.5, 2.5, H - 0.1), (2.0, 5.0, H - 0.1), (1.2, D - 1.6, H - 0.1)], (2.5, 1.5), "#FFC98A", 420)
    cam = scene.camera((0.25, -1.75, 1.18), (0.6, D - 1.0, 1.25), lens=24)
    cam.data.dof.use_dof = True
    cam.data.dof.focus_distance = 5.0
    cam.data.dof.aperture_fstop = 3.5


# --- D. Rush hour, co-op ---------------------------------------------------------------------------------------------

def frame_rush(time_id):
    p = b.Parts()
    W, D, H = 9.0, 5.0, 3.2
    _room(p, W, D, H, wall="#E2EADD")
    for kx in (-2.2, 2.0):
        k = kitchen.kitchen_corner(SOFT, SOFT, with_room=False, stove_fn=hero.stove, plant_on=False)
        k.place("Kitchen", (kx, D - 0.65, 0.0))
        kitchen.pot(SOFT, (kx - 0.15, D - 0.65 + 0.18, kitchen.WORKTOP_H + 0.03))
        kitchen.pan(SOFT, (kx + 0.15, D - 0.65 + 0.44, kitchen.WORKTOP_H + 0.03), yaw=-30)
        concept.steam(p, (kx - 0.15, D - 0.47, kitchen.WORKTOP_H + 0.25), 9, int(kx * 10) + 50, 0.7)
        concept.steam(p, (kx + 0.15, D - 0.2, kitchen.WORKTOP_H + 0.12), 6, int(kx * 10) + 51, 0.4)
    # Island prep table with boards, the pass with plates and tickets.
    p.add(b.box("island", (3.2, 1.0, 0.9), (0.0, 2.2, 0.0), 0.05, 3, b.mat("t", rough=0.5, hex_override="#4F9A85")))
    p.add(b.box("island_top", (3.3, 1.1, 0.04), (0.0, 2.2, 0.9), 0.01, 1, b.chrome()))
    for k, x in enumerate((-1.0, 0.0, 1.0)):
        from . import people
        people.food_board((x, 2.2, 0.94)).place(f"board{k}")
    concept.ticket_rail(p, (0.0, 0.85, 2.0), 12, 3.4, seed=7)
    p.add(b.box("pass", (3.6, 0.5, 1.05), (0.0, 0.7, 0.0), 0.04, 2, b.mat("t", rough=0.5, hex_override="#C8553D")))
    for k in range(5):
        concept.plated(p, (-1.4 + k * 0.7, 0.7, 1.06), ["hash", "bowl", "noodles", "salad", "hash"][k], seed=130 + k)
    p.add(b.cyl("bell", 0.05, 0.05, (1.6, 0.6, 1.05), 16, edge=0.02, material=b.chrome()))
    scene3.kitchen_rush_mess(p)
    looks = scene3._looks()
    cooks = [concept.random_look(200 + k, older=False, apron=c) for k, c in
             enumerate(("#E3AE45", "#F3E9D2", "#E58436", "#E3AE45", "#8CC5AC"))]
    spots = [(-2.4, 3.4, 180), (2.1, 3.5, 160), (-0.8, 1.55, 10), (0.9, 1.6, -15), (3.4, 1.8, 120)]
    for k, ((x, y, yaw), lk) in enumerate(zip(spots, cooks)):
        lk.expression = ["focused", "grin", "focused", "tired", "smile"][k]
        character.person(lk, (x, y, 0.0), yaw, f"player{k}")
    character.person(looks["cook"], (-3.6, 2.1, 0.0), 60, "cook")
    concept.robot_runner(p, (1.9, -0.1, 0.0), 200)
    for x in (-2.0, 0.0, 2.0):
        concept.pendant(p, (x, 2.2, H), "#E3AE45")
    scene.add_sun(time_id)
    _lamps_area([(-2.0, 2.4, H - 0.1), (2.0, 2.4, H - 0.1)], (3.0, 2.0), watts=520)
    scene.camera((0.6, -3.1, 3.0), (0.0, 2.6, 0.9), lens=20)


# --- E. Festival cook-off --------------------------------------------------------------------------------------------

def _booth_stall(p, x, name, body, accent, rival=False):
    p.add(b.box("stall_counter", (3.2, 0.9, 1.0), (x, 0.0, 0.0), 0.06, 3, b.mat("t", rough=0.4, hex_override=body)))
    p.add(b.box("stall_top", (3.3, 1.0, 0.05), (x, 0.0, 1.0), 0.02, 2, b.chrome()))
    for sx in (-1.55, 1.55):
        p.add(b.cyl("stall_post", 0.05, 2.6, (x + sx, 0.35, 0.0), 10, material=b.chrome()))
    p.add(b.box("stall_roof", (3.5, 1.4, 0.12), (x, 0.3, 2.6), 0.05, 3, b.mat("t", rough=0.4, hex_override=accent)))
    p.add(b.box("stall_sign", (3.0, 0.1, 0.5), (x, -0.32, 2.72), 0.05, 3, b.mat("t", rough=0.4, hex_override=body)))
    p.add(b.text("stall_name", name, 0.26, (x, -0.38, 2.97), b.rad(90), b.lamp(2.4, "cream"), 0.02))
    concept.string_lights(p, (x - 1.7, -0.4, 2.6), (x + 1.7, -0.4, 2.6), 10, 0.25)
    for k in range(3):
        concept.plated(p, (x - 0.8 + k * 0.8, -0.15, 1.05), ["hash", "bowl", "noodles"][k], seed=int(x) + k + 150)
    p.add(b.box("stall_stove", (1.2, 0.5, 0.06), (x, 0.25, 1.05), 0.01, 1, b.mat("t", rough=0.5, hex_override="#2C2826")))
    concept.steam(p, (x - 0.2, 0.25, 1.2), 8, int(x) + 160, 0.7)
    if rival:
        # The rival chain: everything matching and spotless, gold trim.
        p.add(b.box("stall_gold", (3.25, 0.05, 0.06), (x, -0.47, 0.9), 0.01, 1, b.mat("g", metal=1.0, rough=0.2,
                                                                                     hex_override="#D9B048")))


def frame_festival(time_id):
    p = b.Parts()
    p.add(b.box("plaza", (60, 60, 0.1), (0, 10, -0.1), 0, 1, b.mat("pl", rough=0.85, hex_override="#BBAA92")))
    _booth_stall(p, -2.6, "TOMORROW KITCHEN", "#4F9A85", "#E3AE45")
    _booth_stall(p, 2.6, "GOLDEN SPOON", "#8E2E2A", "#D9B048", rival=True)
    looks = scene3._looks()
    character.person(looks["cook"], (-3.2, 0.75, 0.0), 180, "cook")
    character.person(looks["helper"], (-2.0, 0.8, 0.0), 200, "helper")
    for k, x in enumerate((2.0, 3.2)):
        lk = concept.random_look(300 + k, older=False, apron="#8E2E2A")
        lk.top, lk.expression, lk.hat, lk.hat_colour = "#F3E9D2", "neutral", "diner", "#F7F1E3"
        character.person(lk, (x, 0.8, 0.0), 180, f"rival{k}")
    # Split-flap scoreboard between the stalls.
    p.add(b.box("score_frame", (1.6, 0.25, 1.4), (0.0, 0.6, 2.2), 0.05, 2, b.mat("t", rough=0.5, hex_override="#2C2826")))
    for c in range(2):
        for r, txt in enumerate(("87", "91")):
            pass
    for r, (name, val) in enumerate((("TK", "87"), ("GS", "91"))):
        for c, ch in enumerate(name + " " + val):
            if ch == " ":
                continue
            fx = -0.6 + c * 0.25
            fz = 2.95 - r * 0.55
            p.add(b.box("flap", (0.21, 0.04, 0.36), (fx, 0.46, fz - 0.18), 0.02, 2,
                        b.mat("t", rough=0.4, hex_override="#3A3532")))
            p.add(b.text("flap_ch", ch, 0.24, (fx, 0.43, fz), b.rad(90), b.mat("t", rough=0.4, hex_override="#F3E9D2"),
                         0.005))
    p.add(b.cyl("score_pole", 0.06, 2.2, (0.0, 0.7, 0.0), 10, material=b.chrome()))
    # Judges' table in front.
    p.add(b.box("judges", (3.0, 0.8, 0.75), (0.0, -2.0, 0.0), 0.04, 2, b.mat("t", rough=0.5, hex_override="#F3E9D2")))
    p.add(b.box("judges_cloth", (3.05, 0.02, 0.6), (0.0, -2.41, 0.12), 0.0, 1, b.mat("t", rough=0.8,
                                                                                    hex_override="#C8553D")))
    for x in (-0.9, 0.0, 0.9):
        concept.chair(p, (x, -1.35, 0.0), 180, "#2C2826")
    concept.crowd([(-0.9, -1.45, 180), (0.0, -1.4, 180), (0.9, -1.45, 180)], seed=320, sit=True)
    for x in (-0.9, 0.0, 0.9):
        p.add(b.box("score_card", (0.25, 0.01, 0.18), (x, -2.25, 0.76), 0.01, 1, b.mat("t", rough=0.8,
                                                                                    hex_override="#F7F2E6"), b.rad(-60, 0, 0)))
    # The crowd around, string lights criss-crossing, a banner, searchlights.
    rng = random.Random(330)
    spots = []
    for k in range(22):
        a = rng.uniform(math.radians(200), math.radians(340))
        r = rng.uniform(3.6, 7.0)
        # Everyone faces the stalls (+Y), give or take.
        spots.append((math.cos(a) * r * 1.3, -1.5 + math.sin(a) * r * 0.6, 180 + rng.uniform(-35, 35)))
    concept.crowd(spots, seed=340)
    for a, c in (((-8, -6, 4.2), (8, 3, 4.6)), ((8, -6, 4.2), (-8, 3, 4.6)), ((-8, -2, 4.0), (8, -2, 4.0))):
        concept.string_lights(p, a, c, 22, 0.8, ("#FFD9A0", "#FF9A7A", "#F2D27A"))
    p.add(b.box("banner", (6.0, 0.05, 0.8), (0.0, 3.0, 4.6), 0.02, 1, b.mat("t", rough=0.7, hex_override="#E3AE45")))
    p.add(b.text("banner_text", "TOWN COOK-OFF", 0.5, (0.0, 2.96, 5.0), b.rad(90), b.mat("t", rough=0.6,
                                                                                       hex_override="#2F3A35"), 0.02))
    for x in (-6.0, 6.0):
        p.add(b.cyl("banner_pole", 0.08, 5.6, (x * 0.5, 3.05, 0.0), 10, material=b.chrome()))
    scene.add_sun(time_id)
    scene._night_extras(time_id, lamps=[(-2.6, -0.5, 2.3), (2.6, -0.5, 2.3), (0.0, -2.0, 3.0)],
                        searchlights=[((-6.0, 8.0, 0.2), (-14, 40, 60)), ((6.0, 8.0, 0.2), (14, 40, 60))])
    scene.camera((1.5, -10.5, 2.6), (0.0, 0.0, 1.9), lens=26)


# --- F. The town from above ------------------------------------------------------------------------------------------

def frame_town(time_id):
    p = b.Parts()
    p.add(b.box("land", (220, 220, 0.1), (0, 40, -0.12), 0, 1, b.mat("ground", rough=0.95)))
    street.ground(p, -45, 45)
    # Main street: themed shops, upper floors behind them, a second row of blocks.
    rng = random.Random(400)
    for i, x in enumerate(range(-36, 37, 8)):
        th = [themes.default_theme("P1"), themes.mex_theme(), themes.jap_theme()][i % 3]
        th = dict(th, props=None)
        hero.shop_front(STREAM if i % 2 else SOFT, th, (x, 0, 0), show_interior=False)
        concept.building(p, (x, 8.0, 0.0), 7.5, 6.0, rng.randrange(3, 6),
                         rng.choice(["#F1E6CF", "#EFC9A9", "#E2EADD", "#EDE6D8"]),
                         rng.choice(["#2F6E5E", "#C8553D", "#2E3F5C", "#8A5A3B"]), seed=400 + i)
    for i, x in enumerate(range(-40, 41, 10)):
        # Leave the camera side open (the dome and the right side face the viewer).
        if x > -14:
            continue
        concept.building(p, (x, -22.0, 0.0), 8.5, 7.0, rng.randrange(2, 5), rng.choice(["#F1E6CF", "#E2EADD"]),
                         rng.choice(["#2F6E5E", "#E58436"]), seed=450 + i)
    # The garden dome across the street, a park around it, the monorail on the left.
    concept.greenhouse_dome(p, (0.0, -24.0, 0.0), 10.0)
    for k in range(14):
        a = 2 * math.pi * k / 14
        street.tree(SOFT, (math.cos(a) * 13.5, -24 + math.sin(a) * 9.0, 0.0), seed=k)
    concept.monorail(p, [(-30.0, -60.0), (-30.0, 30.0)], 7.0, 0.62)
    # Traffic and people along the street.
    climb.delivery_van((-8.0, -4.3, -0.12), 0)
    hero.car_streamline((6.0, -6.0, -0.12), 180)
    hero.car_soft((14.0, -4.3, -0.12), 0, colour_role="main")
    concept.crowd([(rng.uniform(-30, 30), rng.uniform(-2.8, -0.8), rng.uniform(0, 360)) for _ in range(16)], seed=500)
    for k in range(3):
        concept.robot_sweeper(p, (rng.uniform(-20, 20), rng.uniform(-2.6, -1.0), 0.0), rng.uniform(0, 360), seed=k)
    scene.add_sun(time_id, azimuth_offset=20)
    scene._night_extras(time_id, lamps=[], searchlights=[((-20, 20, 0.2), (-40, 80, 80)),
                                                          ((25, 25, 0.2), (40, 80, 80))])
    scene.camera((26.0, -46.0, 22.0), (-2.0, -6.0, 2.0), lens=32)


FRAMES = {
    "morning": (frame_morning, "morning", 0.8),
    "automat": (frame_automat, "interior", 0.6),
    "guest": (frame_guest, "interior", 0.6),
    "rush": (frame_rush, "interior", 1.0),
    "festival": (frame_festival, "night", 0.6),
    "town": (frame_town, "sunset", 0.6),
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
        illustration_look(1.2 if scale >= 1 else 0.8)
        _no_ink_for(["haze"])
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
