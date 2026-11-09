"""제철소 전체 지도 그림 (지도 종이 위 바닷가 제철소, 640x400 도트, 오리지널)."""
import sys, os, json
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk, equip, ground
from equip import box, cyl_y

W, H, S = 640, 400, 22
equip.setup(S, 0.0, 0.0, 0, W, H)
U = lambda px: px / S
rng = np.random.default_rng(11)
TH = pk.bayer(H, W)
yy, xx = np.mgrid[0:H, 0:W]

# 바다 + 지도 종이 격자
sea = np.array([pk.hx(c) for c in ['#1c3346', '#21394f', '#264259', '#2c4b63']])
lvl = 1.4 + (pk.fbm(H, W, 3, cells=(60, 20, 8)) - 0.5) * 1.6
img = np.zeros((H, W, 4), np.uint8)
ground.ramp_fill(img, np.ones((H, W), bool), sea, lvl)
img[(yy % 6 == 0) & (xx % 3 != 0), :3] = pk.hx('#2f5068')          # 물결 줄
img[(xx % 40 == 0) | (yy % 40 == 0), :3] = pk.hx('#365a74')          # 지도 격자

# 땅 (해안선)
cx, cy = 320, 210
ang = np.arctan2(yy - cy, (xx - cx) * 0.62)
r = 175 + 18 * np.sin(ang * 3 + 0.4) + 10 * np.sin(ang * 7) + (pk.fbm(H, W, 9, cells=(40, 14)) - 0.5) * 22
d = np.hypot((xx - cx) * 0.62, yy - cy)
land = d < r * 0.9
shore = (d < r * 0.9 + 5) & ~land
img[shore, :3] = pk.hx('#8a7a5a')
grass = np.array([pk.hx(c) for c in ['#2b3a2c', '#324433', '#3a4e3a', '#435842']])
ground.ramp_fill(img, land, grass, 1.6 + (pk.fbm(H, W, 5) - 0.5) * 1.8)
# 땅 아래 절벽(앞면)
for x in range(W):
    col = np.nonzero(land[:, x])[0]
    if len(col):
        yb = col.max()
        for k in range(1, 10):
            if yb + k < H and not land[yb + k, x]:
                img[yb + k, x, :3] = pk.hx('#4a3a2c') if k < 7 else pk.hx('#2e241c')

# 공장 부지 (콘크리트 패드)
def pad(x0, y0, x1, y1):
    m = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1) & land
    ground.ramp_fill(img, m, ground.FLOORR, 4.0 + (pk.fbm(H, W, 7) - 0.5))
ZONES = {  # 지도 위 공장 자리 (건물 앞쪽 가운데 기준)
    'Ironmaking':  dict(x0=130, x1=232, front=178, depth=34, h=36, extra='furnace', label='① 제선 공장'),
    'Steelmaking': dict(x0=292, x1=388, front=150, depth=32, h=40, extra='stack', label='② 제강 공장'),
    'Casting':     dict(x0=436, x1=526, front=246, depth=32, h=34, extra='stack2', label='③ 연주 공장'),
    'Rolling':     dict(x0=210, x1=404, front=322, depth=32, h=30, extra='long', label='④ 압연 공장'),
}
for z in ZONES.values():
    pad(z['x0'] - 12, z['front'] - z['depth'] - 14, z['x1'] + 12, z['front'] + 14)

# 길 (공정 순서대로)
dr = ImageDraw.Draw(Image.new('L', (W, H)))
road = Image.new('L', (W, H), 0); rd = ImageDraw.Draw(road)
pts = [(181, 192), (250, 186), (340, 166), (405, 186), (480, 258), (462, 290), (404, 334), (300, 336)]
rd.line(pts, fill=255, width=9, joint='curve')
rd.line([(120, 236), (150, 206), (181, 192)], fill=255, width=7, joint='curve')   # 원료 야드에서
rd.line([(404, 334), (530, 346)], fill=255, width=7)                              # 부두로
rm = (np.asarray(road) > 0) & land
img[rm, :3] = pk.hx('#b9a47a')
edge = rm & ~np.asarray(Image.fromarray((rm * 255).astype(np.uint8)).filter(__import__('PIL.ImageFilter', fromlist=['MinFilter']).MinFilter(3))).astype(bool)
img[edge, :3] = pk.hx('#8e7a56')
img[(np.asarray(road) > 0) & ~land & ~shore, :3] = pk.hx('#6d6252')            # 다리 위

# 원료 야드 (광석·석탄 더미)
for (mx, my, c) in ((104, 240, '#8a5a3a'), (130, 250, '#2a2a30'), (100, 262, '#a8a4a0'), (128, 272, '#7a4a30')):
    for k in range(14):
        img[my - k // 2: my, mx - 14 + k: mx + 14 - k, :3] = pk.hx(c)
        img[my - k // 2, mx - 14 + k: mx + 14 - k, :3] = np.minimum(pk.hx(c).astype(int) + 30, 255)

# 나무 몇 그루
for _ in range(60):
    tx, ty = rng.integers(0, W), rng.integers(0, H)
    if land[ty, tx] and not rm[ty, tx] and all(not (z['x0'] - 20 < tx < z['x1'] + 20 and z['front'] - z['depth'] - 30 < ty < z['front'] + 20) for z in ZONES.values()):
        img[ty - 4:ty, tx - 2:tx + 3, :3] = pk.hx('#2f5a34'); img[ty - 5, tx - 1:tx + 2, :3] = pk.hx('#4a7a4a'); img[ty:ty + 2, tx, :3] = pk.hx('#4a3a2a')

def building(b):
    x0, x1 = U(b['x0']), U(b['x1']); z1 = U(b['front']); z0 = z1 - U(b['depth']); h = U(b['h'])
    P = [('Cladding', box(x0, x1, 0, h, z0, z1)), ('Steel_Dark', box(x0 - U(2), x1 + U(2), h, h + U(3), z0 - U(2), z1 + U(2))),
         ('Safety_Yellow', box(x0, x1, h - U(3), h - U(2), z1, z1 + U(0.4))), ('Blue_Steel', box(x0, x1, 0, U(10), z1, z1 + U(0.3)))]
    for i in range(int((b['x1'] - b['x0']) // 18)):
        wx = x0 + U(6 + i * 18)
        P.append(('Lit_Window', box(wx, wx + U(8), U(16), U(24), z1, z1 + U(0.4))))
    if b['extra'] == 'furnace':
        bx, bz = x1 - U(14), z0 + U(10)
        P += [('Rust', cyl_y(bx, bz, 0, U(80), U(10))), ('Steel_Dark', cyl_y(bx, bz, U(80), U(86), U(6)))]
        for k in range(3): P.append(('Struct_Gray', cyl_y(x0 + U(16 + k * 15), z0 + U(9), h, h + U(26), U(6))))
    if b['extra'] in ('stack', 'stack2'):
        sx, sz = x0 + U(18), z0 + U(10); top = U(96 if b['extra'] == 'stack' else 76)
        P += [('Steel', cyl_y(sx, sz, 0, top, U(5))), ('Paint_Red', cyl_y(sx, sz, top - U(8), top - U(4), U(5.5)))]
    if b['extra'] == 'long':
        for k in range(2): P.append(('Steel', cyl_y(x0 + U(30 + k * 24), z0 + U(10), 0, U(72), U(4))))
    return P

layout = {'size': [W, H], 'zones': {}}
for scene, z in ZONES.items():
    spr = equip.render_prop(building(z), None, 70)
    x, y, w, h = spr['rect']
    rgba = Image.fromarray(spr['rgba'])
    base = Image.fromarray(img); base.alpha_composite(rgba, (x, y)); img = np.asarray(base).copy()
    layout['zones'][scene] = dict(label=z['label'], rect=[int(x), int(y), int(w), int(h)], anchor=[int((z['x0'] + z['x1']) / 2), int(y)])

# 부두와 화물선
img[340:350, 520:600, :3] = pk.hx('#6d6252'); img[350:352, 520:600, :3] = pk.hx('#3a3228')
hull = (xx >= 548) & (xx < 616) & (yy >= 356) & (yy < 368) & ((xx - 548) > (yy - 356) * 0.5)
img[hull, :3] = pk.hx('#7a2a2a'); img[356:358, 548:616, :3] = pk.hx('#b9b0b6')
img[346:356, 596:610, :3] = pk.hx('#d8d4cc'); img[348:351, 598:608, :3] = pk.hx('#4a6a8a')
for i, c in enumerate(('#3c88d0', '#d86a2a', '#6cb35a', '#e0a020')):
    img[350:356, 556 + i * 9:563 + i * 9, :3] = pk.hx(c)
# 구름 (가장자리)
for (cx2, cy2, n) in ((60, 40, 6), (590, 60, 5), (40, 360, 5), (610, 180, 4), (300, 20, 4), (150, 380, 3)):
    for k in range(n):
        ox, oy, rr = cx2 + rng.integers(-24, 24), cy2 + rng.integers(-8, 8), rng.integers(8, 14)
        m = (xx - ox) ** 2 + ((yy - oy) * 1.4) ** 2 < rr * rr
        img[m, :3] = pk.hx('#e8ecf0'); img[m & (yy > oy + 2), :3] = pk.hx('#b9c3cc')
img[..., 3] = 255
out = sys.argv[1]
Image.fromarray(img).save(f'{out}/world_map.png')
Image.fromarray(img).resize((W * 2, H * 2), Image.NEAREST).save(f'{out}/world_map_x2.png')
json.dump(layout, open(f'{out}/world_map_layout.json', 'w'), ensure_ascii=False)
print(layout)
