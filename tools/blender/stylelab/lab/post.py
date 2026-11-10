"""Image checks for the style lab (spec 046): greyscale, colour-blind simulation, pixel compare.

Colour-blind matrices: Machado, Oliveira and Fernandes (2009), severity 1.0, applied in linear RGB.
"""

import bpy
import numpy as np

PROTANOPIA = np.array([[0.152286, 1.052583, -0.204868],
                       [0.114503, 0.786281, 0.099216],
                       [-0.003882, -0.048116, 1.051998]])
DEUTERANOPIA = np.array([[0.367322, 0.860646, -0.227968],
                         [0.280085, 0.672501, 0.047413],
                         [-0.011820, 0.042940, 0.968881]])


def _load(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4), w, h


def _save(arr, w, h, path):
    img = bpy.data.images.new("out", w, h, alpha=False)
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "JPEG"
    bpy.context.scene.render.image_settings.quality = 92
    img.save()
    bpy.data.images.remove(img)


def _to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def greyscale(src, dst):
    px, w, h = _load(src)
    lin = _to_linear(px[..., :3])
    y = lin @ np.array([0.2126, 0.7152, 0.0722])
    out = px.copy()
    out[..., :3] = _to_srgb(y)[..., None]
    _save(out, w, h, dst)


def colour_blind(src, dst, kind):
    px, w, h = _load(src)
    m = PROTANOPIA if kind == "protanopia" else DEUTERANOPIA
    lin = _to_linear(px[..., :3])
    out = px.copy()
    out[..., :3] = _to_srgb(lin @ m.T)
    _save(out, w, h, dst)


def difference(a, b):
    """(largest, mean, share of pixels off by more than 2) per-channel difference between two images, 0..255."""
    pa, wa, ha = _load(a)
    pb, wb, hb = _load(b)
    if (wa, ha) != (wb, hb):
        return 255.0, 255.0, 1.0
    d = np.abs(pa[..., :3] - pb[..., :3]) * 255.0
    return float(d.max()), float(d.mean()), float((d.max(axis=2) > 2).mean())
