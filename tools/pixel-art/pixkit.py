"""Palette, ramps and pixel-art helpers for the STEEL MIND rolling map (original palette)."""
import numpy as np

def hx(h):
    h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.uint8)

# 어둡고 채도 낮은 공장 톤 + 따뜻한 조명 포인트
RAMPS = {
    'steel':  ['#141318', '#211f27', '#302d36', '#433f49', '#58535d', '#706a73', '#8b848b'],
    'bright': ['#1a1920', '#2b2930', '#403c45', '#58535c', '#736d75', '#938c92', '#b6aeb1'],
    'dark':   ['#0e0d12', '#16151b', '#1f1d24', '#2a2830', '#36333c', '#45414a'],
    'slate':  ['#12161d', '#1a2029', '#232b36', '#2f3946', '#3e4a59', '#52606f', '#6a7887'],
    'warm':   ['#191614', '#25201d', '#332c27', '#433a33', '#564b42', '#6b5e53', '#847566'],
    'ochre':  ['#2a2112', '#3e3119', '#5a4621', '#7a5f2a', '#9b7a34', '#bb9842'],
    'hot':    ['#5a1f12', '#8d2f14', '#c4501c', '#ee7a2a', '#ffb04a', '#ffe08a'],
    'water':  ['#3e4a59', '#6a7887', '#9fb0bb'],
    'lit':    ['#5a2a14', '#8d4a1c', '#c47a2c', '#eea84a', '#ffd88a'],
    'screen': ['#16222a', '#1f3440', '#2c4d5c', '#3f6f7f', '#6aa3ad'],
    'red':    ['#2a1112', '#451a1a', '#6a2522', '#8d332b', '#b04a3a'],
    'green':  ['#132019', '#1c2e24', '#27402f', '#34553d'],
    'rust':   ['#1c1411', '#2b1d17', '#3d291e', '#553826', '#6e4a30', '#8a5f3c'],
}
RAMPS = {k: np.array([hx(c) for c in v]) for k, v in RAMPS.items()}
MAT_RAMP = {
    'Struct_Gray': 'steel', 'Roll_Steel': 'bright', 'Steel': 'bright', 'Chrome_Rod': 'bright',
    'Steel_Dark': 'dark', 'Grating': 'dark', 'Graphite': 'dark', 'Coil_Cooled': 'steel',
    'Blue_Steel': 'slate', 'Cladding': 'warm', 'Concrete': 'warm', 'Slab_Cold': 'steel',
    'Safety_Yellow': 'ochre', 'Hot_Slab': 'hot', 'Water_Spray': 'water',
    'Lit_Window': 'lit', 'Screen': 'screen', 'Paint_Red': 'red', 'Paint_Green': 'green', 'Rust': 'rust',
    'Copper': 'rust', 'Oven_Brick': 'rust', 'Scrap': 'steel', 'Belt_Rubber': 'dark', 'Coke': 'dark', 'Glass': 'slate',
    'Iron_Ore': 'rust', 'Sinter': 'rust', 'Refractory': 'warm', 'Molten_Steel': 'hot', 'Slag_Hot': 'hot', 'Water': 'water',
    'Slag_Granulated': 'warm', 'Limestone': 'bright', 'Coal': 'dark', 'Slab_Cooling': 'hot',
}
HOT_MATS = {'Hot_Slab', 'Molten_Steel', 'Slag_Hot', 'Slab_Cooling'}
EMISSIVE = {'hot', 'lit', 'screen'}
OUTLINE = hx('#0b0a0e')
WARN = hx('#ff5a44'); WARN_DARK = hx('#7a1d18')

BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 + 1 / 32

def bayer(h, w, ox=0, oy=0):
    ys = (np.arange(h) + oy) % 4; xs = (np.arange(w) + ox) % 4
    return BAYER4[ys[:, None], xs[None, :]]

def value_noise(h, w, cell, seed):
    from PIL import Image
    rng = np.random.default_rng(seed)
    g = rng.random((h // cell + 2, w // cell + 2)).astype(np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8)).resize(((w // cell + 2) * cell, (h // cell + 2) * cell), Image.BILINEAR)
    return np.asarray(im, np.float32)[:h, :w] / 255.0

def fbm(h, w, seed, cells=(48, 16, 6)):
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for i, c in enumerate(cells):
        out += amp * value_noise(h, w, c, seed + i); tot += amp; amp *= 0.5
    return out / tot

def outline(rgba, color=OUTLINE, diag=False):
    a = rgba[..., 3] > 0
    n = np.zeros_like(a)
    n[1:] |= a[:-1]; n[:-1] |= a[1:]; n[:, 1:] |= a[:, :-1]; n[:, :-1] |= a[:, 1:]
    if diag:
        n[1:, 1:] |= a[:-1, :-1]; n[1:, :-1] |= a[:-1, 1:]; n[:-1, 1:] |= a[1:, :-1]; n[:-1, :-1] |= a[1:, 1:]
    edge = n & ~a
    out = rgba.copy(); out[edge, :3] = color; out[edge, 3] = 255
    return out
