"""Palettes for the style lab (spec 046). Colours are sRGB hex; roles map a use to a colour.

P1 garden pastel: mint, sage, mustard, orange, cream (reference picture 3).
P2 warm streamline: rust, cherry, ochre, cream (reference picture 1).
Food keeps natural colours in every palette (art guide 5).
"""


def hex_to_linear(hex_str):
    """sRGB hex to linear RGBA, as Blender materials expect."""
    h = hex_str.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], 1.0)


# Natural food and skin colours, shared by every palette.
COMMON = {
    "potato_skin": "#A57A4C",
    "potato_flesh": "#EBD9A0",
    "tomato": "#C8402F",
    "tomato_flesh": "#E0664E",
    "tomato_seed": "#F0C477",
    "leaf_green": "#4F7A3A",
    "skin": "#D9A786",
    "hair": "#5A3A2A",
    "chrome": "#C9CDD0",
    "glass": "#7FA9B8",
    "hair_grey": "#C9C2B8",
    "mouth": "#8E4A3A",
    "blush": "#E39A84",
    "rubber": "#2C2826",
    "soil": "#5C4030",
    "plant": "#5E8F4F",
    "plant_dark": "#3F6B3A",
    "lamp_warm": "#FFD9A0",
    "neon": "#FF7A5C",
}

PALETTES = {
    "P1": {
        # Round 2: more lightness steps for older eyes. Wall about L*92, doors about L*74, cabinets about L*58.
        "name": "Garden pastel",
        "wall": "#E2EADD",
        "wall_out": "#F1E6CF",
        "main": "#4F9A85",
        "main_dark": "#2F6E5E",
        "accent": "#E3AE45",
        "accent2": "#E58436",
        "cream": "#F3E9D2",
        "trim": "#5E9C86",
        "wood": "#B98B5E",
        "dark": "#3A3532",
        "roof": "#C9734F",
        "road": "#6F6A66",
        "pavement": "#D8CDB8",
        "ground": "#9DB585",
        "car": "#E58436",
        "car_alt": "#8CC5AC",
        "apron": "#E3AE45",
        "shirt": "#F3E9D2",
        "trousers": "#4E6E8E",
        "tiles": ["#F3E9D2", "#E3AE45", "#C9734F", "#4E6E8E", "#8CC5AC", "#D9C7A6"],
        "stripe": ["#E3AE45", "#F3E9D2"],
        "sky_top": "#9CC3D5",
        "sky_bottom": "#F4E7CC",
    },
    "P2": {
        "name": "Warm streamline",
        "wall": "#E2CDB0",
        "wall_out": "#EFE2C8",
        "main": "#B5523B",
        "main_dark": "#7E3428",
        "accent": "#D49A3A",
        "accent2": "#B8362E",
        "cream": "#EFE2C8",
        "trim": "#7E3428",
        "wood": "#9A6542",
        "dark": "#3B2622",
        "roof": "#8E3E2E",
        "road": "#5E4E48",
        "pavement": "#CDB698",
        "ground": "#A98A5E",
        "car": "#B8362E",
        "car_alt": "#D49A3A",
        "apron": "#D49A3A",
        "shirt": "#EFE2C8",
        "trousers": "#5E2A26",
        "tiles": ["#EFE2C8", "#D49A3A", "#B5523B", "#7E3428", "#CDB698", "#E2CDB0"],
        "stripe": ["#B8362E", "#EFE2C8"],
        "sky_top": "#D98C66",
        "sky_bottom": "#F2D9B8",
    },
}


# Round 1 P1 as it was, kept to show the contrast change side by side.
PALETTES["P1r1"] = dict(PALETTES["P1"], wall="#C5D3C0", main="#8CC5AC", main_dark="#5E9C86", trim="#5E9C86",
                        name="Garden pastel (round 1)")
PALETTES["P1"]["trim"] = "#2F6E5E"


def colour(palette_id, role):
    """Hex colour of a role in a palette, falling back to the shared colours."""
    p = PALETTES[palette_id]
    if role in p:
        return p[role]
    return COMMON[role]
