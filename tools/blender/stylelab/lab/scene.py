"""Shots, light moods and render settings for the style lab (spec 046).

Every shot is built from an empty scene, so renders do not depend on each other. Light stays close to what URP can
show: one sun, soft ambient, emissive lit things, a little haze at sunset and night. No path tracing.
"""

import math

import bpy
from mathutils import Vector

from . import build as b
from . import kitchen, palettes, people, street
from .shapes import shape_for

WIDTH, HEIGHT = 1600, 1000

# Light moods. Sun elevation and azimuth in degrees; azimuth 0 means the light comes from -Y (from the camera side).
TIMES = {
    "noon": {
        "sun": ("#FFF1DC", 4.2, 48, -40), "ambient": 0.9, "glow": 0.4, "haze": 0.0,
        "sky": None,  # palette sky
    },
    "sunset": {
        "sun": ("#FF9A5C", 3.0, 9, -65), "ambient": 0.55, "glow": 1.6, "haze": 0.006,
        "sky": ("#6E5490", "#F4A27E"),
    },
    "night": {
        "sun": ("#8FA6FF", 0.5, 35, 30), "ambient": 0.22, "glow": 4.0, "haze": 0.012,
        "sky": ("#1E0B10", "#7A2A22"),
    },
    # Round 3: a grey, soft day. Big soft sun, strong sky light, a little haze.
    "overcast": {
        "sun": ("#EEF2F4", 1.6, 55, -30), "ambient": 1.0, "glow": 0.8, "haze": 0.0012,
        "sky": ("#9DA9B0", "#D3D8D6"), "sun_angle": 40.0,
    },
}


def _engine():
    scene = bpy.context.scene
    for name in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = name
            return
        except TypeError:
            continue


def setup_render(samples, scale):
    scene = bpy.context.scene
    _engine()
    scene.render.resolution_x = int(WIDTH * scale)
    scene.render.resolution_y = int(HEIGHT * scale)
    scene.render.resolution_percentage = 100
    # JPEG at high quality: about a tenth of the PNG size, so every round can be committed.
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.quality = 92
    scene.render.film_transparent = False
    ee = scene.eevee
    ee.taa_render_samples = samples
    for attr, value in (("use_raytracing", False), ("use_shadows", True), ("shadow_ray_count", 2),
                        ("shadow_step_count", 8), ("volumetric_tile_size", "4"), ("volumetric_samples", 64),
                        ("volumetric_end", 120.0), ("use_volumetric_shadows", True)):
        if hasattr(ee, attr):
            try:
                setattr(ee, attr, value)
            except (TypeError, ValueError):
                pass
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.render.use_compositing = False
    scene.render.use_sequencer = False


def setup_world(time_id):
    t = TIMES[time_id]
    world = bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    if t["sky"]:
        top, bottom = t["sky"]
    else:
        pal = palettes.PALETTES[b.palette_id()]
        top, bottom = pal["sky_top"], pal["sky_bottom"]
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.25
    ramp.color_ramp.elements[0].color = palettes.hex_to_linear(bottom)
    ramp.color_ramp.elements[1].position = 0.95
    ramp.color_ramp.elements[1].color = palettes.hex_to_linear(top)
    nt.links.new(coord.outputs["Window"], sep.inputs[0])
    nt.links.new(sep.outputs["Y"], ramp.inputs["Fac"])
    bg_cam = nt.nodes.new("ShaderNodeBackground")
    nt.links.new(ramp.outputs["Color"], bg_cam.inputs["Color"])
    bg_cam.inputs["Strength"].default_value = 1.0
    # Ambient light: a blend of the two sky colours.
    amb = nt.nodes.new("ShaderNodeBackground")
    ta, tb = palettes.hex_to_linear(top), palettes.hex_to_linear(bottom)
    amb.inputs["Color"].default_value = tuple((ta[i] + tb[i] * 2) / 3 for i in range(3)) + (1.0,)
    amb.inputs["Strength"].default_value = t["ambient"]
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs["Fac"])
    nt.links.new(amb.outputs["Background"], mix.inputs[1])
    nt.links.new(bg_cam.outputs["Background"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    if t["haze"] > 0:
        # A fog box around the scene (a world volume in EEVEE would swallow the sky light).
        fog = bpy.data.materials.new("Haze")
        fog.use_nodes = True
        fnt = fog.node_tree
        fnt.nodes.clear()
        fout = fnt.nodes.new("ShaderNodeOutputMaterial")
        vol = fnt.nodes.new("ShaderNodeVolumeScatter")
        vol.inputs["Density"].default_value = t["haze"]
        vol.inputs["Color"].default_value = palettes.hex_to_linear(bottom)
        fnt.links.new(vol.outputs["Volume"], fout.inputs["Volume"])
        b.box("haze", (140, 140, 50), (0, 20, -1), 0, 1, fog)


def add_sun(time_id, azimuth_offset=0.0):
    hex_, strength, elev, azim = TIMES[time_id]["sun"]
    data = bpy.data.lights.new("Sun", "SUN")
    data.color = palettes.hex_to_linear(hex_)[:3]
    data.energy = strength
    data.angle = math.radians(TIMES[time_id].get("sun_angle", 4.0))
    obj = bpy.data.objects.new("Sun", data)
    bpy.context.scene.collection.objects.link(obj)
    # Direction the light travels: from (azimuth, elevation) towards the scene.
    az = math.radians(azim + azimuth_offset)
    el = math.radians(elev)
    src = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    obj.rotation_euler = (-src).to_track_quat("-Z", "Y").to_euler()
    return obj


def point_light(name, loc, hex_, watts, radius=0.1):
    data = bpy.data.lights.new(name, "POINT")
    data.color = palettes.hex_to_linear(hex_)[:3]
    data.energy = watts
    data.shadow_soft_size = radius
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    return obj


def area_light(name, loc, size, hex_, watts, rot=(0, 0, 0)):
    data = bpy.data.lights.new(name, "AREA")
    data.color = palettes.hex_to_linear(hex_)[:3]
    data.energy = watts
    data.shape = "RECTANGLE"
    data.size, data.size_y = size
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = rot
    return obj


def searchlight(name, loc, target, watts=150000):
    data = bpy.data.lights.new(name, "SPOT")
    data.color = palettes.hex_to_linear("#FFE2C2")[:3]
    data.energy = watts
    data.spot_size = math.radians(5)
    data.spot_blend = 0.3
    data.shadow_soft_size = 0.05
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return obj


def camera(loc, target, lens=35.0, ortho_scale=None):
    data = bpy.data.cameras.new("Camera")
    data.lens = lens
    data.sensor_width = 36
    data.clip_end = 400
    if ortho_scale:
        data.type = "ORTHO"
        data.ortho_scale = ortho_scale
    obj = bpy.data.objects.new("Camera", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = obj
    return obj


def _night_extras(time_id, lamps=(), searchlights=()):
    if time_id == "noon":
        return
    warm = "#FFC98A"
    for loc in lamps:
        point_light("lamp", loc, warm, 220 if time_id == "night" else 90, 0.15)
    if time_id == "night":
        for src, dst in searchlights:
            searchlight("search", src, dst)


# --- shots ------------------------------------------------------------------------------------------------------------

def shot_lineup(variant, time_id):
    """All test objects in a row next to the 1.75 m person, on a neutral floor with a curved backdrop."""
    interior, small, outside = (shape_for(variant, g) for g in ("interior", "small", "street"))
    b.box("floor", (60, 40, 0.1), (0, 8, -0.1), 0, 1, b.mat("pavement", rough=0.9))
    b.box("backdrop", (80, 0.2, 30), (0, 22, -0.1), 0, 1, b.mat("wall", rough=0.95))
    hx, cx, px, ox, kx = -5.0, 0.9, 3.6, 4.4, 6.9
    street.house(outside, (hx, 1.5, 0.0))
    street.car(outside, (cx, 0.0, 0.0), yaw=160)
    people.person(small, (px, 0.0, 0.0), yaw=0)
    people.person(small, (ox, 0.2, 0.0), yaw=-15, older=True, apron_on=False)
    k = kitchen.kitchen_corner(interior, small, with_room=False)
    k.place("Kitchen", (kx, 0.0, 0.0))
    kitchen.pot(small, (kx - 0.9 + 0.75, 0.17, kitchen.WORKTOP_H))
    kitchen.pan(small, (kx - 0.9 + 1.05, 0.43, kitchen.WORKTOP_H), yaw=-30)
    people.food_board((kx + 0.75, 0.25, kitchen.WORKTOP_H)).place("Food")
    add_sun(time_id, azimuth_offset=0)
    _night_extras(time_id, lamps=[(hx - 0.9, 2.6, 1.5), (kx, -0.6, 2.4), (cx, -2.5, 2.5)],
                  searchlights=[((-8.0, 6.0, 0.2), (-14, 40, 60)), ((-3.0, 6.0, 0.2), (8, 40, 60))])
    camera((0.2, -21.0, 1.5), (0.2, 0.0, 2.7), lens=40)


def shot_kitchen(variant, time_id):
    """The kitchen corner at cook's eye height: stove, pot and pan, food board, wall units, hood, plant."""
    interior, small = shape_for(variant, "interior"), shape_for(variant, "small")
    k = kitchen.kitchen_corner(interior, small, with_room=True)
    k.place("Kitchen")
    kitchen.pot(small, (-0.9 + 0.75, 0.17, kitchen.WORKTOP_H))
    kitchen.pan(small, (-0.9 + 1.05, 0.43, kitchen.WORKTOP_H), yaw=-30)
    people.food_board((0.75, 0.25, kitchen.WORKTOP_H)).place("Food")
    people.person(small, (2.2, 0.2, 0.0), yaw=-35)
    kitchen.window_occluder(-2.2, -3.0, 0.8)
    # Under-cabinet light strips (reference picture 3).
    for x0, w in ((-0.9, 0.58), (0.3, 1.16)):
        b.box("strip", (w - 0.04, 0.02, 0.01), (x0 + w / 2, 0.45, 1.49), 0, 1, b.lamp(2.0))
    sun = add_sun(time_id, azimuth_offset=0)
    # Sun comes in through the window on the left.
    hex_, strength, elev, _ = TIMES[time_id]["sun"]
    el = math.radians(max(elev, 18) if time_id != "night" else 30)
    src = Vector((-math.cos(el) * 0.85, -math.cos(el) * 0.5, math.sin(el)))
    sun.rotation_euler = (-src).to_track_quat("-Z", "Y").to_euler()
    if time_id != "noon":
        area_light("ceiling", (0.3, -1.2, 2.9), (1.6, 0.8), "#FFD3A0", 110 if time_id == "night" else 50, (0, 0, 0))
    camera((0.75, -3.0, 1.62), (0.35, 0.4, 1.15), lens=24)


def shot_street(variant, time_id):
    """The shop-house with the car parked in front, the person on the pavement, a lamp and a tree."""
    outside, small = shape_for(variant, "street"), shape_for(variant, "small")
    p = b.Parts()
    street.ground(p, -30, 30)
    street.house(outside, (0, 0, 0))
    street.car(outside, (0.6, -4.3, -0.12), yaw=160)
    people.person(small, (3.0, -1.4, 0.0), yaw=-30)
    people.person(small, (3.7, -1.1, 0.0), yaw=-60, older=True, apron_on=False)
    street.street_lamp(outside, (-4.0, -2.7, 0.0))
    street.tree(outside, (5.6, -2.0, 0.0))
    street.house(outside, (-9.5, 0.5, 0.0))
    street.tree(outside, (-6.5, 1.0, 0.0), seed=4)
    add_sun(time_id, azimuth_offset=0)
    lamp_z = 3.62 if not outside.streamline else 3.42
    _night_extras(time_id, lamps=[(-4.0, -2.7, lamp_z - 0.25), (-0.9, 1.5, 1.4), (-10.4, 2.0, 1.4)],
                  searchlights=[((-3.0, 6.0, 0.2), (-25, 60, 70)), ((3.0, 7.0, 0.2), (18, 60, 70))])
    camera((5.6, -15.5, 2.1), (-0.4, 0.0, 1.9), lens=32)


def shot_food(variant, time_id):
    """Close-up of the board: whole and cut potato and tomato, next to the pot on the stove."""
    interior, small = shape_for(variant, "interior"), shape_for(variant, "small")
    k = kitchen.kitchen_corner(interior, small, with_room=True)
    k.place("Kitchen")
    kitchen.pot(small, (-0.9 + 0.75, 0.17, kitchen.WORKTOP_H))
    kitchen.pan(small, (-0.9 + 1.05, 0.43, kitchen.WORKTOP_H), yaw=-30)
    people.food_board((0.75, 0.25, kitchen.WORKTOP_H)).place("Food")
    sun = add_sun(time_id, azimuth_offset=-30)
    if time_id != "noon":
        area_light("ceiling", (0.6, -0.6, 2.4), (1.0, 0.6), "#FFD3A0", 60 if time_id == "night" else 30, (0, 0, 0))
    camera((0.62, -0.32, 1.3), (0.7, 0.25, 0.93), lens=45)


def shot_people(variant, time_id):
    """The cook and an older customer, close enough to judge faces and proportions."""
    small = shape_for(variant, "small")
    p = b.Parts()
    p.add(b.box("wall", (8.0, 0.1, 3.0), (0, 1.2, 0), 0, 1, b.mat("wall", rough=0.85)))
    kitchen.floor_tiles(p, -3.0, 3.0, -3.0, 1.2)
    people.person(small, (-0.45, 0.0, 0.0), yaw=12)
    people.person(small, (0.45, 0.1, 0.0), yaw=-12, older=True, apron_on=False)
    kitchen.plant(p, (1.5, 0.7, 0.0), seed=9, scale=3.0)
    add_sun(time_id, azimuth_offset=-20)
    if time_id != "noon":
        area_light("ceiling", (0.0, -1.2, 2.9), (1.2, 0.6), "#FFD3A0", 160 if time_id == "night" else 60, (0, 0, 0))
    camera((0.0, -3.6, 1.3), (0.0, 0.0, 1.0), lens=40)


def shot_knobs(variant, time_id):
    small = shape_for(variant, "small")
    kitchen.knob_sheet(small)
    sun = add_sun("noon", azimuth_offset=-10)
    sun.data.energy = 2.2
    camera((0.0, -6.0, 0.4), (0.0, 0.0, 0.4), ortho_scale=2.3)


SHOTS = {"lineup": shot_lineup, "kitchen": shot_kitchen, "street": shot_street, "people": shot_people,
         "food": shot_food, "knobs": shot_knobs}


def rad_(x, y, z):
    return b.rad(x, y, z)


def render(variant, palette_id, shot, time_id, path, samples=64, scale=1.0):
    b.reset(palette_id)
    b.set_glow(TIMES[time_id]["glow"])
    setup_render(samples, scale)
    setup_world(time_id)
    SHOTS[shot](variant, time_id)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
