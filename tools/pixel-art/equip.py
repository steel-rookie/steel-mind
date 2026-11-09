"""Render the rolling line (rolling.glb) and 3D-built props into stylized pixel-art sprites (top-down oblique)."""
import numpy as np
import glb, oblique, pixkit as pk

S = 48            # px per world unit
WALL_H = 160      # 뒷벽 높이(px). 바닥은 이 아래부터
Z_BACK = -2.6     # 뒷벽이 서 있는 world z
X0 = -16.1        # map px 0 에 해당하는 world x
MAP_W, MAP_H = 1536, 560

def to_px(x, y, z):
    return (x - X0) * S, WALL_H + (z - Z_BACK) * S - y * S

def px_to_world(px, py):
    """바닥(y=0) 위 지도 좌표 → world x, z"""
    return px / S + X0, (py - WALL_H) / S + Z_BACK

LINE_GROUPS = {
    'reheating_furnace': 'EQ_reheating_furnace', 'descaler': 'EQ_descaler', 'roughing_mill': 'EQ_roughing_mill',
    'finishing_mill': 'EQ_finishing_mill', 'runout_table': 'EQ_runout_table', 'coiler': 'EQ_coiler',
    'roller_tables': 'Roller_Tables', 'walkway': 'Operator_Walkways', 'hot_strip': 'Hot_Material.001',
}

def stylize(r, mat_names, seed):
    H, W = r['mat'].shape
    rgba = np.zeros((H, W, 4), np.uint8)
    m = r['mat']; has = m >= 0
    noise = pk.fbm(H, W, seed, cells=(24, 8, 3))
    dth = pk.bayer(H, W)
    d = r['depth']
    farther = np.zeros((H, W), bool)
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        nb = np.roll(np.roll(d, dy, 0), dx, 1)
        farther |= has & (nb - d > 0.06)
    for i, name in enumerate(mat_names):
        sel = m == i
        if not sel.any():
            continue
        key = pk.MAT_RAMP.get(name, 'steel'); ramp = pk.RAMPS[key]; n = len(ramp)
        if key == 'hot':
            lvl = np.full(sel.sum(), 3.0) + (r['up'][sel] > 0.5) * 0.4 + (noise[sel] - 0.5) * 2.2
        elif key == 'lit':
            lvl = np.full(sel.sum(), n - 2.2) + (noise[sel] - 0.5) * 2.0
        elif key == 'screen':
            lvl = np.full(sel.sum(), 2.6) + (noise[sel] - 0.5) * 3.0
        else:
            lvl = 0.8 + r['lam'][sel] * (n - 2.4)
            lvl += (r['up'][sel] > 0.8) * 0.9
            lvl += (noise[sel] - 0.5) * 1.1
        lvl -= farther[sel] * 1.6
        li = np.clip(np.floor(lvl + dth[sel]), 0, n - 1).astype(int)
        rgba[sel, :3] = ramp[li]
        rgba[sel, 3] = 255
        if key == 'water':
            yy, xx = np.nonzero(sel)
            off = ((yy + xx) % 2 == 1)
            rgba[yy[off], xx[off], 3] = 0
    # 윗면 앞 모서리 하이라이트
    top = has & (r['up'] > 0.8)
    below = np.zeros((H, W), bool); below[:-1] = has[1:] & (r['up'][1:] <= 0.8) & (np.abs(d[1:] - d[:-1]) < 0.5)
    hl = top & below
    rgba[hl, :3] = np.minimum(rgba[hl, :3].astype(int) + 20, 255)
    return rgba

def outline_by_group(rgba, gid):
    """조합 렌더의 바깥 실루엣에만 외곽선을 넣고, 그 외곽선 픽셀은 이웃 설비 그룹에 붙인다."""
    a = rgba[..., 3] > 0
    gid = gid.copy()
    out = rgba.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        na = np.roll(np.roll(a, dy, 0), dx, 1)
        ng = np.roll(np.roll(gid, dy, 0), dx, 1)
        edge = ~a & na & (out[..., 3] == 0)
        out[edge, :3] = pk.OUTLINE; out[edge, 3] = 255
        gid[edge] = ng[edge]
    return out, gid

def crop(rgba, pad=3):
    ys, xs = np.nonzero(rgba[..., 3])
    x0, x1 = max(xs.min() - pad, 0), min(xs.max() + 1 + pad, rgba.shape[1])
    y0, y1 = max(ys.min() - pad, 0), min(ys.max() + 1 + pad, rgba.shape[0])
    return rgba[y0:y1, x0:x1], [int(x0), int(y0), int(x1 - x0), int(y1 - y0)]

def footprint(tris):
    v = tris.reshape(-1, 3)
    fx0, fy0 = to_px(v[:, 0].min(), 0, v[:, 2].min())
    fx1, fy1 = to_px(v[:, 0].max(), 0, v[:, 2].max())
    return [int(round(fx0)), int(round(fy0)), int(round(fx1 - fx0)), int(round(fy1 - fy0))]

def build_line(glb_path, seed=7):
    j, parts = glb.triangles(glb_path)
    names = [m['name'] for m in j['materials']]
    keys = list(LINE_GROUPS)
    tl = []
    geo = {k: [] for k in keys}
    for tag, _, m, t in parts:
        for gi, k in enumerate(keys):
            if tag == LINE_GROUPS[k]:
                tl.append((m, t, gi)); geo[k].append(t)
    r = oblique.render(tl, MAP_W, MAP_H, to_px)
    rgba = stylize(r, names, seed)
    rgba, gid = outline_by_group(rgba, r['gid'])
    sprites = {}
    for gi, k in enumerate(keys):
        layer = np.zeros_like(rgba)
        sel = (gid == gi) & (rgba[..., 3] > 0)
        layer[sel] = rgba[sel]
        img, rect = crop(layer)
        sprites[k] = dict(rgba=img, rect=rect, footprint=footprint(np.concatenate(geo[k])), mask_full=sel)
    return sprites

# ── 소품: 박스·원통으로 3D 모양을 만들고 같은 방식으로 도트화 ──────────────
def box(x0, x1, y0, y1, z0, z1):
    v = np.array([[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]], float)
    f = [(0, 1, 2), (0, 2, 3), (4, 6, 5), (4, 7, 6), (0, 4, 5), (0, 5, 1), (3, 2, 6), (3, 6, 7), (0, 3, 7), (0, 7, 4), (1, 5, 6), (1, 6, 2)]
    return v[np.array(f)]

def cyl_x(x0, x1, cy, cz, r, n=20, inner=0.0):
    """x축 방향으로 누운 원통(코일). inner>0이면 앞뒤 면에 구멍."""
    a = np.linspace(0, 2 * np.pi, n + 1)
    ring = np.stack([np.cos(a), np.sin(a)], 1)
    tris = []
    for i in range(n):
        (c0, s0), (c1, s1) = ring[i], ring[i + 1]
        p = lambda x, c, s, rr: [x, cy + s * rr, cz + c * rr]
        tris += [[p(x0, c0, s0, r), p(x1, c0, s0, r), p(x1, c1, s1, r)], [p(x0, c0, s0, r), p(x1, c1, s1, r), p(x0, c1, s1, r)]]
        for x in (x0, x1):
            if inner > 0:
                tris += [[p(x, c0, s0, r), p(x, c1, s1, r), p(x, c1, s1, inner)], [p(x, c0, s0, r), p(x, c1, s1, inner), p(x, c0, s0, inner)]]
            else:
                tris += [[[x, cy, cz], p(x, c0, s0, r), p(x, c1, s1, r)]]
    return np.array(tris, float)

def cyl_z(cx, cy, z0, z1, r, n=22, inner=0.0):
    """z축(앞뒤) 방향 원통: 앞면 원이 보인다. inner>0이면 가운데 구멍"""
    a = np.linspace(0, 2 * np.pi, n + 1)
    tris = []
    for i in range(n):
        c0, s0, c1, s1 = np.cos(a[i]), np.sin(a[i]), np.cos(a[i + 1]), np.sin(a[i + 1])
        P = lambda z, c, s, rr: [cx + c * rr, cy + s * rr, z]
        tris += [[P(z0, c0, s0, r), P(z1, c0, s0, r), P(z1, c1, s1, r)], [P(z0, c0, s0, r), P(z1, c1, s1, r), P(z0, c1, s1, r)]]
        if inner > 0:
            tris += [[P(z1, c0, s0, r), P(z1, c1, s1, r), P(z1, c1, s1, inner)], [P(z1, c0, s0, r), P(z1, c1, s1, inner), P(z1, c0, s0, inner)]]
            tris += [[P(z0, c0, s0, inner), P(z1, c0, s0, inner), P(z1, c1, s1, inner)], [P(z0, c0, s0, inner), P(z1, c1, s1, inner), P(z0, c1, s1, inner)]]
        else:
            tris += [[[cx, cy, z1], P(z1, c0, s0, r), P(z1, c1, s1, r)]]
    return np.array(tris, float)

def cyl_y(cx, cz, y0, y1, r, n=16):
    a = np.linspace(0, 2 * np.pi, n + 1)
    tris = []
    for i in range(n):
        c0, s0, c1, s1 = np.cos(a[i]), np.sin(a[i]), np.cos(a[i + 1]), np.sin(a[i + 1])
        P = lambda y, c, s: [cx + c * r, y, cz + s * r]
        tris += [[P(y0, c0, s0), P(y1, c0, s0), P(y1, c1, s1)], [P(y0, c0, s0), P(y1, c1, s1), P(y0, c1, s1)]]
        tris += [[[cx, y1, cz], P(y1, c0, s0), P(y1, c1, s1)]]
    return np.array(tris, float)

def render_prop(parts, mat_names, seed):
    """parts: [(material_name, tris)]"""
    names = sorted({p[0] for p in parts})
    tl = [(names.index(mn), t) for mn, t in parts]
    r = oblique.render(tl, MAP_W, MAP_H, to_px)
    rgba = pk.outline(stylize(r, names, seed))
    img, rect = crop(rgba)
    return dict(rgba=img, rect=rect, footprint=footprint(np.concatenate([t for _, t in parts])))
