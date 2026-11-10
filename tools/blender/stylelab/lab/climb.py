"""Round 3 models for the climb (docs/game-design.md, spec 046): the stage 1 food cart and a delivery van.

Axes: X right, Y away from the camera, Z up. The cart's serving side faces -Y; the van's front is -X.
"""

import math
import random

from . import build as b
from . import character, kitchen, mess, people
from .build import Parts, rad
from .hero import _whitewall_wheel, clear_glass


def _m(hex_, rough=0.6, metal=0.0, emit=0.0, alpha=1.0):
    return b.mat("climb", metal=metal, rough=rough, emit=emit, alpha=alpha, hex_override=hex_)


def food_cart(loc, yaw=0.0, colour="#4F9A85", seed=1):
    """Stage 1: a second-hand cart. One replacement panel in another colour, tape on a corner, a two-burner hob,
    a parasol, a gas bottle, a cooler, crates, a taped handwritten menu, a cash tin and a tip jar."""
    rng = random.Random(seed)
    p = Parts()
    W, D, H = 1.6, 0.75, 0.92
    body = _m(colour, 0.65)
    p.add(b.box("cart_body", (W, D, H - 0.3), (0, 0, 0.3), 0.04, 3, body))
    # Replacement front panel, a different, sun-faded colour, slightly proud of the body.
    p.add(b.box("cart_patch", (0.5, 0.012, 0.42), (-0.42, -D / 2 - 0.004, 0.38), 0.01, 1, _m("#D9C7A6", 0.8)))
    for sx in (-0.65, -0.19):
        p.add(b.cyl("rivet", 0.008, 0.008, (sx, -D / 2 - 0.012, 0.76), 8, material=b.chrome(), rot=rad(90)))
    mess.tape(p, (W / 2 - 0.05, -D / 2 - 0.003, 0.5), 0.14, 60)
    mess.tape(p, (W / 2 - 0.08, -D / 2 - 0.003, 0.56), 0.12, 70)
    # Wheels on one end, two legs on the other.
    for sy in (-1, 1):
        p.add(b.torus("cart_tyre", 0.21, 0.035, (W / 2 - 0.25, sy * (D / 2 + 0.04), 0.245), 24, 8,
                      _m("#2C2826", 0.8), rad(90, 0, 0)))
        for k in range(6):
            p.add(b.cyl("spoke", 0.006, 0.2, (W / 2 - 0.25, sy * (D / 2 + 0.04), 0.245), 6, material=b.chrome(),
                        rot=rad(0, 60 * k, 0)))
        p.add(b.cyl("cart_leg", 0.02, 0.3, (-W / 2 + 0.1, sy * (D / 2 - 0.08), 0.0), 8, material=_m("#7F8A84", 0.5, 0.6)))
    p.add(b.capsule("cart_handle", 0.018, D + 0.2, (W / 2 + 0.12, -(D + 0.2) / 2, 0.85), 10, b.chrome(),
                    rad(-90, 0, 0)))
    # Worktop with a two-burner hob, a pot, a board with food, squeeze bottles.
    top_z = H
    p.add(b.box("cart_top", (W + 0.04, D + 0.04, 0.03), (0, 0, top_z - 0.03), 0.008, 2, _m("#9AA0A3", 0.45, 0.8)))
    p.add(b.box("cart_hob", (0.5, 0.36, 0.06), (-0.4, 0.05, top_z), 0.01, 2, _m("#2C2826", 0.5)))
    for hx in (-0.52, -0.28):
        p.add(b.torus("hob_ring", 0.06, 0.008, (hx, 0.05, top_z + 0.065), 16, 4, _m("#3A3532", 0.6, 0.3)))
    p.add(kitchen.pot(kitchen_shape(), (-0.52, 0.05, top_z + 0.07)))
    p.add(b.box("cart_board", (0.34, 0.22, 0.02), (0.22, 0.05, top_z), 0.008, 2, _m("#B98B5E", 0.6)))
    p.add(people.potato((0.16, 0.08, top_z + 0.02), False, 5))
    p.add(people.potato((0.27, 0.02, top_z + 0.02), True, 6))
    for k, col in enumerate(("#C8402F", "#E3AE45")):
        p.add(b.lathe("squeeze", [(0, 0), (0.03, 0), (0.032, 0.15), (0.012, 0.19), (0.004, 0.22), (0, 0.22)], 12,
                      (0.5 + k * 0.08, 0.22, top_z), _m(col, 0.4)))
    # Cash tin and tip jar.
    p.add(b.box("cash_tin", (0.2, 0.14, 0.07), (0.62, -0.18, top_z), 0.01, 2, _m("#7F8A84", 0.45, 0.6)))
    p.add(b.box("cash_tin_lid", (0.2, 0.012, 0.13), (0.62, -0.11, top_z + 0.07), 0.005, 1, _m("#7F8A84", 0.45, 0.6),
                rad(-20, 0, 0)))
    p.add(b.lathe("tip_jar", [(0, 0), (0.05, 0), (0.05, 0.13), (0.04, 0.14), (0, 0.14)], 16, (0.4, -0.22, top_z),
                  clear_glass("#D9E8EC", 0.4)))
    for k in range(3):
        p.add(b.cyl("coin", 0.012, 0.003, (0.4 + rng.uniform(-0.02, 0.02), -0.22 + rng.uniform(-0.02, 0.02),
                                           top_z + 0.005 + k * 0.004), 10, material=_m("#C9A548", 0.3, 0.8)))
    # Handwritten menu taped on the front.
    mess.paper_sign(p, (0.35, -D / 2 - 0.006, 0.5), ["POTATO HASH  4.50", "+ green sauce  0.50", "CASH ONLY"], 0.5,
                    0.28, -2.5, seed=seed + 3, size=0.045)
    # Parasol, slightly leaning, with a string of bulbs.
    pole_x, pole_y = 0.72, 0.28
    pole = b.cyl("parasol_pole", 0.02, 1.55, (pole_x, pole_y, top_z), 8, material=_m("#F3E9D2", 0.5))
    pole.rotation_euler = rad(3, -4, 0)
    p.add(pole)
    # Parasol: a shallow cone of 12 panels in two alternating colours.
    shade = b.lathe("parasol", [(0.0, 0.3), (1.1, 0.0)], 12, (pole_x - 0.06, pole_y, top_z + 1.45),
                    _m("#E3AE45", 0.75), smooth=False)
    shade.data.materials.append(_m("#F3E9D2", 0.75))
    k = 0
    for poly in shade.data.polygons:
        if len(poly.vertices) == 3:
            poly.material_index = k % 2
            k += 1
    shade.rotation_euler = rad(3, -4, 0)
    p.add(shade)
    p.add(b.sphere("parasol_tip", 0.03, (pole_x - 0.06, pole_y, top_z + 1.76), 10, material=_m("#F3E9D2", 0.5)))
    for k in range(9):
        a = 2 * math.pi * k / 9
        bulb_z = top_z + 1.36 - 0.05 * math.sin(k * 1.3)
        p.add(b.sphere("bulb", 0.022, (pole_x - 0.06 + math.cos(a) * 1.0, pole_y + math.sin(a) * 1.0, bulb_z), 8,
                       material=b.lamp(2.0)))
    # Gas bottle with hose, cooler box, crates, a cardboard OPEN sign, stools.
    p.add(b.capsule("gas", 0.13, 0.55, (-W / 2 - 0.25, 0.15, 0.0), 16, _m("#C8402F", 0.5)))
    p.add(b.cyl("gas_valve", 0.03, 0.06, (-W / 2 - 0.25, 0.15, 0.55), 10, material=b.chrome()))
    mess.cable(p, [(-W / 2 - 0.25, 0.15, 0.6), (-W / 2 - 0.12, 0.1, 0.75), (-W / 2 + 0.05, 0.1, top_z - 0.05)],
               "#2C2826")
    p.add(b.box("cooler", (0.55, 0.36, 0.36), (-W / 2 - 0.1, -0.55, 0.0), 0.04, 3, _m("#4E6E8E", 0.5)))
    p.add(b.box("cooler_lid", (0.57, 0.38, 0.06), (-W / 2 - 0.1, -0.55, 0.36), 0.03, 3, _m("#F3E9D2", 0.5)))
    mess.fruit_crate_full(p, (W / 2 + 0.45, 0.25, 0.0), 12, "#A57A4C", seed + 4)
    mess.crate(p, (W / 2 + 0.45, 0.25, 0.3), -8, seed=seed + 5)
    sign = mess.cardboard_box(p, (-0.2, -D / 2 - 0.75, 0.0), (0.5, 0.04, 0.42), 8, False, seed + 6, "OPEN :)")
    sign.rotation_euler = rad(-12, 0, 8)
    mess.stool(p, (-0.45, -1.25, 0.0), "round", "#C8553D", seed + 7)
    mess.stool(p, (0.25, -1.35, 0.0), "square", "#9A7452", seed + 8)
    return p.place("FoodCart", loc, rad(0, 0, yaw))


def kitchen_shape():
    from .shapes import SOFT
    return SOFT


def delivery_van(loc, yaw=0.0, body_hex="#4F9A85", roof_hex="#F3E9D2", name="TOMORROW KITCHEN", seed=2):
    """A rounded retro delivery van, about 4.2 m: low rounded nose, tall rounded box, a roof sign, the name on the
    side, white-wall tyres, chrome bumpers. Front is -X."""
    p = Parts()
    body = _m(body_hex, 0.35)
    cream = _m(roof_hex, 0.4)
    # Box body and a lower rounded nose.
    p.add(b.box("van_box", (2.9, 1.78, 1.75), (2.55, 0, 0.42), 0.34, 6, body))
    p.add(b.box("van_nose", (1.35, 1.72, 1.0), (0.85, 0, 0.42), 0.38, 6, body))
    p.add(b.box("van_cab", (0.9, 1.7, 0.9), (1.25, 0, 1.1), 0.3, 6, body))
    # Two-tone band and the roof.
    p.add(b.box("van_band", (3.95, 1.8, 0.16), (2.05, 0, 0.78), 0.06, 3, cream))
    p.add(b.box("van_roof", (2.95, 1.7, 0.08), (2.55, 0, 2.15), 0.04, 3, cream))
    # Windscreen and side windows.
    ws = b.box("windscreen", (0.06, 1.4, 0.5), (0.86, 0, 1.38), 0.12, 4, clear_glass("#2F4048", 0.75))
    ws.rotation_euler = rad(0, -22, 0)
    p.add(ws)
    for sy in (-1, 1):
        p.add(b.box("side_window", (0.55, 0.02, 0.42), (1.3, sy * 0.86, 1.42), 0.1, 4, clear_glass("#2F4048", 0.75)))
        p.add(b.box("door_seam", (0.012, 0.01, 1.1), (1.62, sy * 0.893, 0.55), 0.0, 1, _m("#2C2826", 0.6)))
        # The name and an emblem on the side.
        side_y = sy * 0.9
        t = b.text("van_name", name, 0.2, (2.6, side_y + sy * 0.003, 1.45), rad(90, 0, 0 if sy < 0 else 180),
                   _m("#F3E9D2", 0.4), 0.01)
        p.add(t)
        p.add(b.cyl("van_emblem", 0.28, 0.01, (2.6, side_y, 1.05), 32, material=cream, rot=rad(90 * sy, 0, 0)))
        p.add(b.cyl("van_emblem_in", 0.2, 0.012, (2.6, side_y + sy * 0.001, 1.05), 32, material=_m("#E3AE45", 0.5),
                    rot=rad(90 * sy, 0, 0)))
        p.add(b.box("handle", (0.12, 0.03, 0.025), (1.45, sy * 0.9, 1.05), 0.01, 1, b.chrome()))
        hx = 0.22
        p.add(b.torus("bezel", 0.09, 0.02, (hx, sy * 0.58, 0.95), 20, 8, b.chrome(), rad(0, 90, 0)))
        p.add(b.sphere("headlight", 0.085, (hx - 0.01, sy * 0.58, 0.95), 16, (0.5, 1, 1), b.lamp(1.6)))
        p.add(b.box("taillight", (0.03, 0.1, 0.18), (4.0, sy * 0.72, 0.85), 0.01, 1, b.lamp(1.5, "neon")))
    # Grille, bumpers, roof sign.
    for k in range(5):
        p.add(b.box("van_grille", (0.03, 0.7, 0.03), (0.19, 0, 0.62 + k * 0.06), 0.01, 1, b.chrome()))
    for sx in (0.16, 3.98):
        p.add(b.box("bumper", (0.12, 1.8, 0.12), (sx, 0, 0.32), 0.05, 4, b.chrome()))
    p.add(b.box("roof_sign", (1.6, 0.12, 0.42), (2.6, 0, 2.23), 0.06, 3, _m("#2F6E5E", 0.4)))
    for sy in (-1, 1):
        p.add(b.text("roof_text", "DELIVERY", 0.16, (2.6, sy * 0.063, 2.44), rad(90, 0, 0 if sy < 0 else 180),
                     b.lamp(1.2, "cream"), 0.005))
    # A little wear: a dent sticker and a scuff on the bumper corner.
    mess.tape(p, (3.98, -0.85, 0.36), 0.12, 0, "y", "#8F8F8A")
    for wx, wy in ((0.85, 0.82), (0.85, -0.82), (3.3, 0.82), (3.3, -0.82)):
        _whitewall_wheel(p, wx, wy, 0.34)
    for o in p.items:
        o.location.x -= 2.1
    return p.place("Van", loc, rad(0, 0, yaw))
