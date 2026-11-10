"""Shape languages for the style lab (spec 046).

S1 soft: rounded, pill-like, few details (reference picture 3).
S2 streamline: teardrops, fins, ribs, chrome bands (reference picture 1).
S3 mix: S1 for interiors and small things, S2 for cars, buildings and big stations.
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class Shape:
    name: str
    streamline: bool      # fins, ribs, chrome bands, teardrop forms
    radius: float         # corner radius of furniture-sized boxes, metres
    small_radius: float   # corner radius of hand-sized things, metres
    segments: int         # bevel segments
    round_verts: int      # vertices around a cylinder or sphere


SOFT = Shape("soft", streamline=False, radius=0.09, small_radius=0.012, segments=4, round_verts=24)
STREAM = Shape("streamline", streamline=True, radius=0.03, small_radius=0.006, segments=3, round_verts=24)

# Which shape each object group uses, per variant.
VARIANTS = {
    "S1": {"interior": SOFT, "small": SOFT, "street": SOFT, "label": "Soft"},
    "S2": {"interior": STREAM, "small": STREAM, "street": STREAM, "label": "Streamline"},
    "S3": {"interior": SOFT, "small": SOFT, "street": STREAM, "label": "Mix"},
}


def shape_for(variant_id, group):
    return VARIANTS[variant_id][group]
