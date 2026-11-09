"""공정 가동 연출용 작은 그림들 (fx_*.png) + layout.json의 fx 항목"""
import json, os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk

HOT = pk.RAMPS['hot']; OL = pk.hx('#2a0f08')

def save(a, path): Image.fromarray(a.astype(np.uint8)).save(path)

def block(w, top, front, seed):
    """뜨거운 강재 덩어리: 윗면 top px + 앞면 front px, 외곽선 포함"""
    h = top + front
    a = np.zeros((h + 2, w + 2, 4), np.uint8)
    n = pk.fbm(h, w, seed, cells=(8, 4, 2)); th = pk.bayer(h, w)
    lvl = np.where(np.arange(h)[:, None] < top, 3.6, 2.4) + (n - 0.5) * 1.6
    lvl[0] += 0.8
    li = np.clip(np.floor(lvl + th), 0, 5).astype(int)
    a[1:-1, 1:-1, :3] = HOT[li]; a[1:-1, 1:-1, 3] = 255
    a[0, 1:-1, :3] = OL; a[-1, 1:-1, :3] = OL; a[1:-1, 0, :3] = OL; a[1:-1, -1, :3] = OL
    a[0, 1:-1, 3] = a[-1, 1:-1, 3] = a[1:-1, 0, 3] = a[1:-1, -1, 3] = 255
    return a

def spray(w, h, seed, frames=3):
    out = []
    for f in range(frames):
        rng = np.random.default_rng(seed + f)
        a = np.zeros((h, w, 4), np.uint8)
        for x in range(1, w - 1, 3):
            off = rng.integers(0, 6)
            for y in range(off, h, rng.integers(4, 7)):
                L = rng.integers(1, 3)
                a[y:y + L, x, :3] = pk.hx('#d8eef5') if rng.random() < 0.5 else pk.hx('#8fc3d6')
                a[y:y + L, x, 3] = 200
            if rng.random() < 0.5:
                a[h - 2:h, x - 1:x + 2, :3] = pk.hx('#e8f6fa'); a[h - 2:h, x - 1:x + 2, 3] = 170
        a[0:2, :, :3] = pk.hx('#4c6a7a'); a[0:2, :, 3] = 230
        out.append(a)
    return out

def coil_frames(r, frames=4):
    out = []
    S = 2 * r + 3
    yy, xx = np.mgrid[0:S, 0:S] - (r + 1)
    d = np.hypot(xx, yy); ang = np.arctan2(yy, xx)
    for f in range(frames):
        a = np.zeros((S, S, 4), np.uint8)
        body = d <= r
        rings = (np.floor(d) % 3 == 0)
        lvl = 3.2 - (d / r) * 1.2 + rings * -0.8
        spoke = (np.abs(((ang + f * np.pi / 8) % (np.pi / 2)) - np.pi / 4) < 0.12) & (d > r * 0.35)
        lvl = lvl - spoke * 1.2
        li = np.clip(np.round(lvl), 0, 5).astype(int)
        a[body, :3] = HOT[li[body]]; a[body, 3] = 255
        eye = d <= r * 0.3
        a[eye, :3] = pk.hx('#1a1214')
        edge = (d > r) & (d <= r + 1.2)
        a[edge, :3] = OL; a[edge, 3] = 255
        out.append(a)
    return out

def steam_frames(frames=4):
    out = []
    for f in range(frames):
        S = 14; a = np.zeros((S, S, 4), np.uint8)
        yy, xx = np.mgrid[0:S, 0:S]
        r = 2.5 + f * 1.4
        d = np.hypot(xx - 7, yy - 8 + f)
        m = d <= r
        th = pk.bayer(S, S)
        keep = m & (th < (0.85 - f * 0.18))
        a[keep, :3] = pk.hx('#b9b3b0'); a[keep, 3] = 150 - f * 25
        out.append(a)
    return out

def furnace_glow(furnace_png, rect):
    im = np.asarray(Image.open(furnace_png)).astype(int)
    hot = (im[..., 0] > 180) & (im[..., 1] < 200) & (im[..., 2] < 120) & (im[..., 3] > 0)
    from PIL import ImageFilter
    m = Image.fromarray((hot * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.BoxBlur(3))
    m = np.asarray(m, np.float32) / 255
    th = pk.bayer(*m.shape)
    a = np.zeros(im.shape, np.uint8)
    lv = np.floor(m * 3 + th * 0.9) / 3
    a[..., :3] = pk.hx('#ffb04a'); a[..., 3] = (lv * 150).astype(np.uint8)
    return a

def main(out):
    L = json.load(open(f'{out}/layout.json'))
    obj = {o['id']: o for o in L['objects']}
    hs = np.asarray(Image.open(f"{out}/{obj['hot_strip']['file']}"))
    c = int(np.argmax((hs[..., 3] > 0).sum(0)))
    ys = np.nonzero(hs[:, c, 3])[0]
    col = hs[ys.min():ys.max() + 1, c:c + 1]
    save(col, f'{out}/fx_strip_0.png')
    col2 = col.copy().astype(int); col2[..., :3] = np.minimum(col2[..., :3] + [18, 14, 6], 255); save(col2, f'{out}/fx_strip_1.png')
    strip_top = obj['hot_strip']['rect'][1] + ys.min()
    save(block(34, 8, 7, 1), f'{out}/fx_slab.png')
    save(block(60, 8, 5, 2), f'{out}/fx_bar.png')
    for i, a in enumerate(spray(70, 30, 10)): save(a, f'{out}/fx_spray_short_{i}.png')
    for i, a in enumerate(spray(360, 34, 20)): save(a, f'{out}/fx_spray_long_{i}.png')
    for i, a in enumerate(coil_frames(13)): save(a, f'{out}/fx_coil_{i}.png')
    for i, a in enumerate(steam_frames()): save(a, f'{out}/fx_steam_{i}.png')
    fr = obj['reheating_furnace']['rect']
    save(furnace_glow(f"{out}/{obj['reheating_furnace']['file']}", fr), f'{out}/fx_furnace_glow.png')
    dr, rr, fm, ro = (obj[k]['collider'] for k in ('descaler', 'roughing_mill', 'finishing_mill', 'runout_table'))
    L['fx'] = dict(
        note='공정 연출 위치(px). LineProcess.cs가 읽는다',
        stripTop=int(strip_top), stripHeight=int(len(col)),
        furnaceExit=fr[0] + fr[2] - 20, furnaceGlow=[fr[0], fr[1]],
        descaler=[dr[0], dr[0] + dr[2]], roughing=[rr[0], rr[0] + rr[2]], finishing=[fm[0], fm[0] + fm[2]],
        runout=[ro[0], ro[0] + ro[2]], coil=[1388, 260], coilRadius=13,
        sprayShort=[dr[0] - 1, strip_top - 30], sprayLong=[ro[0] + 3, strip_top - 34])
    json.dump(L, open(f'{out}/layout.json', 'w'), ensure_ascii=False, indent=1, default=int)
    print(L['fx'])

if __name__ == '__main__':
    main(sys.argv[1])
