"""메인 화면 배경: 밤의 제철소 전경 (480x270 도트, 오리지널)."""
import sys, os
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk

W, H = 480, 270
rng = np.random.default_rng(4)
img = np.zeros((H, W, 3), np.uint8)
hx = pk.hx
TH = pk.bayer(H, W)

# 하늘: 위는 짙은 남색, 지평선 쪽은 보라·주황 (디더링)
sky = [hx(c) for c in ['#070a18', '#0b1026', '#131535', '#1d1a40', '#2a1d44', '#3d2244', '#5a2c40', '#7a3a34', '#a0512c']]
yy, xx = np.mgrid[0:H, 0:W]
t = (yy / 190.0) ** 1.6 * (len(sky) - 1)
li = np.clip(np.floor(t + TH * 0.999), 0, len(sky) - 1).astype(int)
img[:] = np.array(sky)[li]
# 별
for _ in range(90):
    x, y = rng.integers(0, W), rng.integers(0, 110)
    img[y, x] = hx('#c9cfe8') if rng.random() < 0.7 else hx('#ffe9b0')

def rect(x0, y0, x1, y1, c):
    img[max(y0, 0):min(y1, H), max(x0, 0):min(x1, W)] = hx(c)

def circle_top(cx, top, r, c):
    for y in range(top, top + r):
        dx = int(np.sqrt(max(r * r - (top + r - y) ** 2, 0)))
        rect(cx - dx, y, cx + dx + 1, y + 1, c)

# 먼 산 / 바다 수평선
ridge = 196 + (np.sin(np.arange(W) / 37.0) * 6 + np.sin(np.arange(W) / 13.0) * 2).astype(int)
for x in range(W):
    rect(x, ridge[x], x + 1, H, '#191428')

# 공장 실루엣(가운데 층)
MID, MID2, LIT = '#221b2e', '#2b2238', '#ffcf6a'
GROUND = 214
# 고로 (왼쪽 가운데)
rect(150, 92, 178, GROUND, MID); circle_top(164, 84, 14, MID)
rect(160, 58, 168, 92, MID); rect(146, 120, 182, 124, MID2); rect(146, 150, 182, 154, MID2)
rect(178, 70, 214, 74, MID); rect(210, 70, 214, 130, MID)   # 하강관
# 열풍로 3기
for cx in (96, 116, 136):
    rect(cx - 7, 120, cx + 7, GROUND, MID); circle_top(cx, 112, 7, MID)
# 굴뚝들 (빨강·흰 띠 꼭대기)
STACKS = [(240, 40), (262, 56), (360, 34), (430, 62), (60, 70)]
for sx, top in STACKS:
    rect(sx - 3, top, sx + 3, GROUND, MID)
    rect(sx - 3, top, sx + 3, top + 3, '#8a2a2a'); rect(sx - 3, top + 3, sx + 3, top + 5, '#b9b0b6'); rect(sx - 3, top + 5, sx + 3, top + 8, '#8a2a2a')
# 가스 홀더
rect(300, 120, 340, GROUND, MID2); circle_top(320, 112, 20, MID2)
for y in range(126, GROUND, 10): rect(300, y, 340, y + 1, MID)
# 긴 공장 건물들 (불 켜진 창)
for x0, x1, top in ((180, 300, 150), (330, 470, 140), (0, 90, 160), (205, 255, 128)):
    rect(x0, top, x1, GROUND, MID2)
    rect(x0, top - 3, x1, top, MID)
    for x in range(x0 + 4, x1 - 4, 7):
        for y in range(top + 6, GROUND - 6, 9):
            if rng.random() < 0.55: rect(x, y, x + 3, y + 2, LIT if rng.random() < 0.8 else '#ff9a4a')
# 컨베이어 다리 (대각선)
for i in range(70):
    x, y = 214 + i, 130 + int(i * 0.55)
    rect(x, y, x + 1, y + 3, MID)
for i in range(0, 70, 10):
    x, y = 214 + i, 130 + int(i * 0.55)
    rect(x, y, x + 1, GROUND, MID)
# 크레인
rect(372, 70, 376, GROUND, MID); rect(352, 70, 420, 74, MID); rect(412, 74, 414, 104, MID); rect(408, 104, 418, 110, MID)

# 고로 아래 주황 빛 (디더링 원)
def glow(cx, cy, rx, ry, col, strength):
    f = np.clip(1 - ((xx - cx) / rx) ** 2 - ((yy - cy) / ry) ** 2, 0, 1) * strength
    m = TH < f
    c = np.array(hx(col), float)
    img[m] = (img[m] * 0.45 + c * 0.55).astype(np.uint8)
glow(164, 212, 90, 34, '#ff7a2a', 0.85)
glow(320, 214, 140, 22, '#ff9a4a', 0.45)
glow(250, 200, 240, 30, '#c0502a', 0.25)

# 앞쪽 층: 파이프랙·울타리·도로
NEAR = '#120f18'
rect(0, GROUND, W, H, NEAR)
for x in range(0, W, 26):
    rect(x, GROUND - 26, x + 2, GROUND, NEAR)
rect(0, GROUND - 26, W, GROUND - 23, NEAR); rect(0, GROUND - 14, W, GROUND - 12, NEAR)
for x in range(0, W, 4): rect(x, GROUND - 6, x + 1, GROUND, '#1b1622')
# 도로 가로등
for x in range(30, W, 90):
    rect(x, 222, x + 1, 246, '#2a2433'); rect(x, 222, x + 6, 223, '#2a2433'); rect(x + 5, 223, x + 7, 225, '#ffd88a')
    glow(x + 6, 252, 22, 10, '#ffd88a', 0.35)
# 아래로 갈수록 어둡게
dark = np.clip((yy - 240) / 30.0, 0, 1)[..., None]
img[:] = (img * (1 - dark * 0.6)).astype(np.uint8)

out = sys.argv[1]
Image.fromarray(img).save(out + '/title_bg.png')
Image.fromarray(img).resize((W * 3, H * 3), Image.NEAREST).save(out + '/title_bg_x3.png')
# 굴뚝 꼭대기 위치 (연기·빨간 불 연출용, 480x270 기준)
import json
json.dump({'stacks': [[sx, top] for sx, top in STACKS], 'size': [W, H]}, open(out + '/title_layout.json', 'w'))
print('ok')
