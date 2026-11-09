"""메인 화면 배경: 해 질 녘 바닷가 제철소 전경 (480x270 도트, 오리지널).

왼쪽에서 비치는 노을빛, 멀수록 푸르게 흐려지는 공기, 고로·열풍로·빨강흰 굴뚝, 녹슨 공장 건물,
불 켜진 창·가로등·출선구 불빛. 굴뚝 꼭대기 좌표는 title_layout.json 으로 내보내 연기 연출에 쓴다.
"""
import sys, os, json
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk

W, H = 480, 270
hx = pk.hx
TH = pk.bayer(H, W)
yy, xx = np.mgrid[0:H, 0:W]
img = np.zeros((H, W, 3), float)
rng = np.random.default_rng(7)

HAZE = np.array(hx('#9db0c6'), float)
haze = 0.0

def R(*cs):
    return np.array([hx(c) for c in cs], float)

# 램프 (어두운 → 밝은)
RUST = R('#2a1712', '#45231a', '#64321f', '#874426', '#a85a30', '#c8743c', '#e4934e', '#f6b672')
BRICK = R('#2c1814', '#4a2620', '#6a3426', '#8c452e', '#ad5a38', '#cf7848')
STEEL = R('#262a35', '#3b404d', '#555b68', '#737885', '#9497a0', '#b8b6b4', '#ddd3c4')
DARK = R('#14121a', '#1f1c25', '#2c2832', '#3b3540', '#4c4450')
RED = R('#5a1a16', '#7e241c', '#a43526', '#c84a32', '#e46a44')
WHITE = R('#6f7480', '#9a9ca4', '#c3c0bb', '#e6ddcf', '#fbf3e4')
CLOUD = R('#6c7c99', '#8996b0', '#a9b2c4', '#cdd0d8', '#eae6e2', '#fff7ea')
LIT = R('#ffb04a', '#ffd27a', '#fff0b8')

def col(c):
    c = np.asarray(c, float)
    return c * (1 - haze) + HAZE * haze

def rect_mask(x0, y0, x1, y1):
    return (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)

def paint(mask, ramp, level):
    """level(스칼라 또는 배열)을 디더링해서 ramp 색으로 칠한다."""
    if not mask.any():
        return
    lv = level[mask] if np.ndim(level) else np.full(mask.sum(), float(level))
    i = np.clip(np.floor(lv + TH[mask]), 0, len(ramp) - 1).astype(int)
    img[mask] = col(ramp[i])

def solid(mask, c):
    img[mask] = col(hx(c) if isinstance(c, str) else c)

# ── 하늘 ─────────────────────────────────────────────
SKY = R('#16305e', '#1e3d72', '#2a4d84', '#3a5f94', '#5276a5', '#6e8db4', '#90a6c0', '#b4b9c2', '#d6bea4', '#ebc192')
t = np.clip(yy / 168.0, 0, 1) ** 1.35 * (len(SKY) - 1.2)
t = t + np.clip((xx - 200) / 280.0, 0, 1) * np.clip((yy - 110) / 60.0, 0, 1) * 0.8   # 오른쪽 지평선이 조금 더 따뜻
img[:] = SKY[np.clip(np.floor(t + TH), 0, len(SKY) - 1).astype(int)]

# 구름: 둥근 덩어리 × 노이즈, 왼쪽 위에서 빛
def cloud_field(blobs, seed):
    d = np.zeros((H, W))
    for cx, cy, rx, ry, s in blobs:
        d += s * np.exp(-(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2) * 2.2)
    n = pk.fbm(H, W, seed)
    return d * (0.55 + 0.9 * n)

def clouds(blobs, seed, cut=0.55, dim=0.0):
    d = cloud_field(blobs, seed)
    m = d > cut
    sh = np.roll(np.roll(d, 3, 0), 3, 1)          # 빛 방향(왼쪽 위) 쪽 밀도
    light = np.clip((d - sh) * 3.0 + 0.5, 0, 1)
    under = np.clip((yy - (np.array([b[1] for b in blobs]).mean())) / 14.0, 0, 1)
    lv = 1.0 + light * 4.2 - under * 1.6 - dim
    paint(m, CLOUD, lv)

clouds([(40, 22, 70, 16, 1.2), (110, 36, 60, 18, 1.0), (10, 50, 40, 14, 0.9), (170, 18, 40, 10, 0.7)], 3)
clouds([(400, 30, 50, 12, 1.0), (455, 46, 40, 12, 0.9), (330, 58, 36, 9, 0.7)], 5)
clouds([(300, 96, 80, 8, 0.8), (430, 110, 60, 9, 0.9), (60, 104, 70, 7, 0.7)], 9, cut=0.6, dim=0.6)

# ── 바다 ─────────────────────────────────────────────
SEA_Y = 158
sea = rect_mask(0, SEA_Y, W, 185)
SEA = R('#2f4a6a', '#3e5d7f', '#55789a', '#7e9db6', '#c9c2b0')
lv = 1.4 + np.clip((yy - SEA_Y) / 30.0, 0, 1) * -0.6 + (pk.fbm(H, W, 11) - 0.5) * 1.4
paint(sea, SEA, lv)
for _ in range(60):   # 반짝이는 물결
    x, y = rng.integers(0, W), rng.integers(SEA_Y + 2, 180)
    solid(rect_mask(x, y, x + rng.integers(2, 6), y + 1), '#d8d2c2')

# ── 그리기 도구 ──────────────────────────────────────
def box(x0, y0, x1, y1, ramp, base, roof=3, windows=None, lit_ratio=0.0, seed=0):
    """정면 박스 + 지붕 띠. 왼쪽이 밝고 오른쪽으로 갈수록 어둡다."""
    m = rect_mask(x0, y0, x1, y1)
    grad = base + 1.0 - (xx - x0) / max(x1 - x0, 1) * 1.6
    paint(m, ramp, grad)
    paint(rect_mask(x0, y0 - roof, x1, y0), ramp, base + 2.2)          # 지붕 윗면(빛 받음)
    paint(rect_mask(x0, y0, x1, y0 + 1), ramp, base - 1.5)             # 처마 그림자
    paint(rect_mask(x0, y0, x0 + 1, y1), ramp, base + 2.6)             # 왼쪽 모서리 빛
    if windows:
        r = np.random.default_rng(seed)
        wx, wy, ww, wh = windows
        for y in range(y0 + 4, y1 - 3, wy):
            for x in range(x0 + 3, x1 - 3 - ww, wx):
                if r.random() < lit_ratio:
                    solid(rect_mask(x, y, x + ww, y + wh), LIT[r.integers(0, 3)])
                    lights.append((x + ww / 2, y + wh / 2))
                else:
                    paint(rect_mask(x, y, x + ww, y + wh), ramp, base - 1.8)

def cyl(cx, top, bottom, r, ramp, base, dome=True):
    """세로 원통. 왼쪽에서 빛."""
    u = (xx - cx) / max(r, 1)
    m = (np.abs(u) <= 1) & (yy >= top) & (yy < bottom)
    shade = base + 1.4 - (u + 0.35) ** 2 * 2.2
    paint(m, ramp, shade)
    if dome:
        dm = ((xx - cx) ** 2 + ((yy - top) * 1.0) ** 2 <= r * r) & (yy < top)
        paint(dm, ramp, base + 1.8 - (u + 0.4) ** 2 * 2.0 + (top - yy) / r * 0.3)

def stack(cx, top, bottom, w=3, base=2.2):
    """빨강·흰 띠 굴뚝"""
    u = (xx - cx) / w
    body = (np.abs(u) <= 1) & (yy >= top) & (yy < bottom)
    band = ((yy - top) // 7) % 2 == 0
    shade = base + 1.2 - (u + 0.4) ** 2 * 1.8
    paint(body & band, RED, shade)
    paint(body & ~band, WHITE, shade + 0.4)
    paint(rect_mask(cx - w - 1, top, cx + w + 2, top + 2), DARK, 2)
    tops.append((cx, top))

def line(x0, y0, x1, y1, c, wdt=1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for i in range(n):
        x = int(round(x0 + (x1 - x0) * i / max(n - 1, 1))); y = int(round(y0 + (y1 - y0) * i / max(n - 1, 1)))
        if 0 <= x < W and 0 <= y < H:
            img[y:y + wdt, x:x + wdt] = col(c)

def lattice(x0, y0, x1, y1, ramp, base):
    """고로 꼭대기 철골 탑"""
    cl, cd = ramp[min(int(base + 1), len(ramp) - 1)], ramp[max(int(base - 1), 0)]
    line(x0, y1, x0 + 2, y0, cl, 2); line(x1, y1, x1 - 2, y0, cd, 2)
    for y in range(y0, y1, 8):
        line(x0, y, x1, y, cd); line(x0, y, x1, min(y + 8, y1), cd)
    line(x0 - 2, y0, x1 + 2, y0, cl, 2)

def steam(cx, cy, size, seed, up=1.0):
    """뭉게뭉게 수증기: 둥근 덩어리를 겹겹이 쌓고 각 덩어리 왼쪽 위를 밝게"""
    r = np.random.default_rng(seed)
    m = np.zeros((H, W), bool); lv = np.zeros((H, W))
    puffs = []
    for k in range(14):
        f = k / 13
        px = cx + f * size * 2.2 + r.normal(0, size * 0.35)
        py = cy - f * size * 2.6 * up + r.normal(0, size * 0.2)
        pr = size * (0.35 + 0.35 * f) * r.uniform(0.75, 1.2)
        puffs.append((px, py, pr))
    for px, py, pr in sorted(puffs, key=lambda q: -q[1]):     # 아래 덩어리부터 그려서 위가 덮게
        dx, dy = xx - px, yy - py
        c = dx * dx + dy * dy <= pr * pr
        sh = np.clip(1 - np.sqrt((dx + pr * 0.4) ** 2 + (dy + pr * 0.45) ** 2) / (pr * 1.25), 0, 1)
        lv = np.where(c, 1.6 + sh * 4.0, lv); m |= c
    paint(m, CLOUD, lv)

lights, tops = [], []

# ── 먼 공장 (푸르게 흐림) ────────────────────────────
haze = 0.62
for x0, x1, top in ((300, 360, 136), (352, 420, 128), (410, 480, 140), (240, 300, 146), (0, 50, 140)):
    box(x0, top, x1, 170, RUST, 3)
for sx, top in ((322, 70), (372, 64), (404, 82), (458, 72), (290, 96)):
    stack(sx, top, 170, 2)
for cx in (440, 452, 464):
    cyl(cx, 128, 170, 5, STEEL, 3)
haze = 0.42
lattice(398, 96, 420, 132, STEEL, 2); cyl(409, 132, 172, 9, RUST, 3)
for x0, x1, top in ((260, 330, 150), (430, 480, 146)):
    box(x0, top, x1, 178, RUST, 3.5, windows=(8, 8, 3, 2), lit_ratio=0.2, seed=x0)
steam(470, 112, 14, 21); steam(338, 118, 10, 22)

# ── 가운데: 고로 3기 · 열풍로 · 굴뚝 ─────────────────
haze = 0.18
GROUND = 196
def furnace(cx, top, s=1.0, seed=0):
    lattice(int(cx - 12 * s), top, int(cx + 12 * s), top + int(42 * s), STEEL, 3)
    cyl(cx, top + int(42 * s), GROUND, int(12 * s), RUST, 3.5, dome=False)
    paint(rect_mask(int(cx - 15 * s), top + int(58 * s), int(cx + 15 * s), top + int(62 * s)), DARK, 3)   # 작업 발판
    # 하강관: 꼭대기에서 오른쪽 아래 집진기로 휘어 내려감
    for i in range(int(40 * s)):
        x = cx + 4 + i; y = top + 4 + int((i / (40 * s)) ** 2 * 50 * s)
        img[y:y + 3, x:x + 3] = col(RUST[5 if i < 6 else 4])
        img[y + 3, x:x + 3] = col(RUST[2])
    cyl(int(cx + 44 * s), top + int(54 * s), GROUND, int(6 * s), RUST, 3.2)
    for k in range(3):   # 열풍로 (돔 원통)
        cyl(int(cx - 26 * s - k * 13 * s), top + int(52 * s), GROUND, int(6 * s), STEEL, 3.6)
    box(int(cx - 20 * s), GROUND - 18, int(cx + 22 * s), GROUND, BRICK, 3, roof=2)  # 주상 건물

furnace(110, 72, 1.0, 1)
furnace(262, 62, 1.1, 2)
furnace(392, 84, 0.9, 3)
for sx, top in ((198, 46), (334, 40), (452, 50), (36, 64), (176, 74)):
    stack(sx, top, GROUND, 3)
# 컨베이어 다리
for i in range(90):
    x, y = 150 + i, 150 + int(i * 0.42)
    img[y:y + 3, x] = col(STEEL[3]); img[y + 3, x] = col(STEEL[1])
    if i % 12 == 0:
        paint(rect_mask(x, y, x + 1, GROUND), STEEL, 1.5)
steam(70, 150, 12, 31); steam(300, 160, 11, 32); steam(232, 168, 9, 33)

# 가운데 줄 공장 건물
haze = 0.08
for x0, x1, top, ramp in ((0, 70, 168, RUST), (60, 150, 176, BRICK), (196, 250, 170, RUST), (310, 380, 172, RUST),
                          (372, 440, 178, BRICK), (430, 480, 168, RUST)):
    box(x0, top, x1, 206, ramp, 3.4, roof=4, windows=(7, 7, 3, 2), lit_ratio=0.35, seed=x0 + 1)

# ── 앞쪽: 큰 공장 지붕 · 파이프 · 철길 ────────────────
haze = 0.0
ground = rect_mask(0, 206, W, H)
paint(ground, R('#1e1a1c', '#2a2426', '#383032', '#4a3e3c', '#5e4e48'), 2.2 + (pk.fbm(H, W, 41) - 0.5) * 1.5)
# 파이프 랙 (길게 가로지름)
for y, c in ((204, 4), (208, 3)):
    paint(rect_mask(0, y, W, y + 2), STEEL, c)
for x in range(6, W, 22):
    paint(rect_mask(x, 200, x + 2, 214), STEEL, 2.2)
# 철길 (왼쪽 아래에서 사선)
for k in range(4):
    for i in range(0, 260):
        x = i; y = 226 + k * 9 + int(i * 0.16)
        if y < H:
            img[y, x] = col(hx('#8a7f78')); img[min(y + 3, H - 1), x] = col(hx('#6a605a'))
            if i % 4 == 0: img[y:y + 4, x] = col(hx('#3a2e2a'))
# 쇳물 운반 열차
for k in range(6):
    x = 18 + k * 20; y = 222 + int(x * 0.16)
    cyl(x + 7, y - 7, y + 1, 6, STEEL, 3.4)
    paint(rect_mask(x, y + 1, x + 15, y + 3), DARK, 2)
paint(rect_mask(130, 232, 146, 241), RED, 3)
# 큰 앞 건물들
for x0, x1, top, ramp, win in ((150, 260, 222, RUST, 0.45), (262, 330, 230, BRICK, 0.3), (336, 420, 226, RUST, 0.4),
                                (40, 120, 248, BRICK, 0.4), (420, 480, 238, STEEL, 0.25)):
    box(x0, top, x1, H, ramp, 3.8, roof=6, windows=(9, 9, 4, 3), lit_ratio=win, seed=x0 + 7)
stack(140, 186, 236, 3)
# 가로등
for x in (30, 136, 268, 334, 450):
    paint(rect_mask(x, 212, x + 1, 232), DARK, 3)
    solid(rect_mask(x, 212, x + 5, 213), '#3b3540'); solid(rect_mask(x + 4, 213, x + 6, 215), '#fff0b8')
    lights.append((x + 5, 214))

# ── 조명 ─────────────────────────────────────────────
# 1) 노을: 왼쪽 위에서 비추는 따뜻한 빛 (전체 색조)
sun = np.clip(1.0 - xx / 520.0, 0, 1) * np.clip((yy - 40) / 200.0, 0, 1)
warm = np.array(hx('#ffb070'), float)
img = img * (1 - 0.18 * sun[..., None]) + warm * (0.18 * sun[..., None])

# 2) 불빛 주변 번짐 (디더링된 둥근 빛)
def glow(cx, cy, rx, ry, c, s):
    global img
    f = np.clip(1 - ((xx - cx) / rx) ** 2 - ((yy - cy) / ry) ** 2, 0, 1) ** 1.3 * s
    m = TH < f
    c = np.array(hx(c), float)
    img[m] = img[m] * 0.55 + c * 0.45

for (lx, ly) in lights:
    glow(lx, ly, 7, 5, '#ffcf7a', 0.35)
glow(262, 196, 60, 14, '#ff7a2a', 0.7)    # 출선구 불빛
glow(110, 196, 46, 12, '#ff8a3a', 0.6)
glow(392, 196, 40, 10, '#ff8a3a', 0.5)

# 3) 아래쪽 비네트
dark = np.clip((yy - 236) / 40.0, 0, 1)[..., None]
img = img * (1 - dark * 0.35)

out = sys.argv[1]
im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
im.save(out + '/title_bg.png')
im.resize((W * 3, H * 3), Image.NEAREST).save(out + '/title_bg_x3.png')
json.dump({'stacks': [[int(x), int(y)] for x, y in tops if y < 120], 'size': [W, H]}, open(out + '/title_layout.json', 'w'))
print('ok', len(tops), 'stacks')
