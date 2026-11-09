"""귀여운 2등신 작업자 (24x32, 앞·뒤·왼쪽·오른쪽 × 걷기 4프레임). 흰 안전모, 주황·남색 작업복, 반사띠."""
import numpy as np
import pixkit as pk

FW, FH = 24, 32
C = {k: pk.hx(v) for k, v in dict(
    hw='#ffd447', hm='#e8ad22', hd='#b07a14', hb='#7a520e', fs='#231d2a', fs2='#342b3c', ss='#6e4a44', ss2='#9a6a5a',          # 안전모
    sk='#f3c9a6', sd='#d9a283', bl='#f09a8a', ey='#1c1418', hr='#2a1f1c', hr2='#3d2e28',
    o1='#ff9a3c', o2='#ee7a22', o3='#c45c18',                         # 주황
    n1='#3a4f86', n2='#2a3a68', n3='#1d284c',                         # 남색
    rf='#e6ecef', rf2='#aab4bb', bt='#1a1a20', bt2='#33333c').items()}

def canvas():
    return np.zeros((FH, FW, 4), np.uint8)

def R(img, x0, y0, x1, y1, c):
    """[x0,x1] x [y0,y1] 포함 범위 칠하기"""
    y0, y1 = max(y0, 0), min(y1, FH - 1); x0, x1 = max(x0, 0), min(x1, FW - 1)
    if x0 > x1 or y0 > y1: return
    img[y0:y1 + 1, x0:x1 + 1, :3] = C[c]; img[y0:y1 + 1, x0:x1 + 1, 3] = 255

def P(img, x, y, c):
    if 0 <= x < FW and 0 <= y < FH:
        img[y, x, :3] = C[c]; img[y, x, 3] = 255

def helmet(img, by, side=0, back=False):
    # 둥근 안전모: by = 맨 윗줄
    rows = [(8, 15), (6, 17), (5, 18), (4, 19), (4, 19), (4, 19)]
    for i, (a, b) in enumerate(rows):
        R(img, a, by + i, b, by + i, 'hw')
    for i, (a, b) in enumerate(rows):
        R(img, b - 2, by + i, b, by + i, 'hm'); P(img, b, by + i, 'hd')
    P(img, 7, by + 2, 'rf'); R(img, 6, by + 3, 7, by + 3, 'rf')
    if not back:
        R(img, 11, by, 12, by + 5, 'hm')   # 가운데 골
    P(img, 11, by, 'hw'); P(img, 12, by, 'hw')
    # 챙
    if side == 0:
        R(img, 3, by + 6, 20, by + 6, 'hm'); R(img, 3, by + 7, 20, by + 7, 'hd')
    elif side > 0:
        R(img, 4, by + 6, 21, by + 6, 'hm'); R(img, 6, by + 7, 22, by + 7, 'hd')
    if not back and side == 0:
        R(img, 10, by + 3, 13, by + 4, 'n2')     # 앞 엠블럼 자리(글자 없음)
    if side > 0 and not back:
        R(img, 14, by + 3, 16, by + 4, 'n2')

def face_front(img, y):
    # 얼굴은 보이고, 눈 높이까지는 안전모 그림자
    R(img, 5, y, 18, y + 6, 'sk'); R(img, 5, y + 6, 18, y + 6, 'sd')
    R(img, 4, y, 4, y + 4, 'hr'); R(img, 19, y, 19, y + 4, 'hr')
    R(img, 5, y, 18, y + 1, 'fs'); R(img, 5, y + 2, 18, y + 2, 'ss'); R(img, 5, y + 3, 18, y + 3, 'ss2')
    R(img, 6, y + 4, 7, y + 4, 'bl'); R(img, 16, y + 4, 17, y + 4, 'bl')
    R(img, 11, y + 5, 12, y + 5, 'sd')
    return
    R(img, 5, y, 18, y + 6, 'sk'); R(img, 5, y + 6, 18, y + 6, 'sd')
    R(img, 4, y, 4, y + 4, 'hr'); R(img, 19, y, 19, y + 4, 'hr')
    R(img, 5, y, 18, y, 'hr2'); R(img, 6, y + 1, 8, y + 1, 'hr2'); R(img, 15, y + 1, 17, y + 1, 'hr2')
    R(img, 8, y + 2, 8, y + 4, 'ey'); R(img, 15, y + 2, 15, y + 4, 'ey')
    P(img, 8, y + 2, 'rf'); P(img, 15, y + 2, 'rf')
    R(img, 6, y + 5, 7, y + 5, 'bl'); R(img, 16, y + 5, 17, y + 5, 'bl')
    R(img, 11, y + 5, 12, y + 5, 'sd')

def face_back(img, y):
    R(img, 4, y, 19, y + 5, 'hr'); R(img, 5, y + 1, 18, y + 3, 'hr2'); R(img, 6, y + 6, 17, y + 6, 'sd')

def face_side(img, y):
    R(img, 5, y, 18, y + 6, 'hr'); R(img, 6, y + 1, 9, y + 4, 'hr2')
    R(img, 10, y, 19, y + 6, 'sk'); R(img, 10, y + 6, 19, y + 6, 'sd')
    R(img, 10, y, 19, y + 1, 'fs'); R(img, 10, y + 2, 19, y + 2, 'ss'); R(img, 10, y + 3, 19, y + 3, 'ss2')
    R(img, 15, y + 4, 16, y + 4, 'bl'); R(img, 10, y + 2, 11, y + 4, 'sd')
    return
    R(img, 5, y, 18, y + 6, 'hr'); R(img, 6, y + 1, 9, y + 4, 'hr2')
    R(img, 10, y, 19, y + 6, 'sk'); R(img, 10, y + 6, 19, y + 6, 'sd'); R(img, 10, y, 19, y, 'hr2')
    R(img, 16, y + 2, 16, y + 4, 'ey'); P(img, 16, y + 2, 'rf'); R(img, 15, y + 5, 16, y + 5, 'bl')
    R(img, 10, y + 2, 11, y + 4, 'sd')

def body_front(img, y, back=False):
    # y = 몸통 윗줄. 주황 위, 남색 아래, 반사띠
    R(img, 7, y, 16, y + 8, 'n2')
    R(img, 7, y, 16, y + 4, 'o1'); R(img, 15, y, 16, y + 4, 'o2')
    R(img, 7, y + 5, 16, y + 5, 'rf'); R(img, 15, y + 5, 16, y + 5, 'rf2')
    R(img, 7, y + 6, 16, y + 8, 'n2'); R(img, 15, y + 6, 16, y + 8, 'n3')
    if not back:
        R(img, 9, y, 14, y, 'n1'); R(img, 10, y + 1, 13, y + 1, 'n1')     # 남색 깃
        R(img, 11, y + 1, 12, y + 8, 'n3')                               # 지퍼
        R(img, 8, y + 3, 9, y + 4, 'n2'); R(img, 14, y + 3, 15, y + 4, 'n2')  # 가슴 주머니
    else:
        R(img, 9, y, 14, y, 'n2')

def arm(img, x, y, swing, front=True):
    yo = y + swing
    R(img, x, yo, x + 1, yo + 2, 'o2' if front else 'o3')
    R(img, x, yo + 3, x + 1, yo + 3, 'rf')
    R(img, x, yo + 4, x + 1, yo + 5, 'n2')
    R(img, x, yo + 6, x + 1, yo + 6, 'sk')

def legs_front(img, y, step):
    # step: 0 서기, 1 왼발 앞, 2 서기, 3 오른발 앞 (앞모습은 발 높이로 표현)
    ly = 0 if step != 3 else -1
    ry = 0 if step != 1 else -1
    R(img, 8, y, 10, y + 2 + ly, 'n3'); R(img, 8, y + 3 + ly, 10, y + 4 + ly, 'bt')
    R(img, 13, y, 15, y + 2 + ry, 'n3'); R(img, 13, y + 3 + ry, 15, y + 4 + ry, 'bt')
    R(img, 8, y + 3 + ly, 8, y + 3 + ly, 'bt2'); R(img, 13, y + 3 + ry, 13, y + 3 + ry, 'bt2')

def legs_side(img, y, step):
    off = {0: (0, 0), 1: (2, -2), 2: (0, 0), 3: (-2, 2)}[step]
    for dx, col in ((off[1], 'n3'), (off[0], 'n2')):
        x = 10 + dx
        R(img, x, y, x + 2, y + 2, col); R(img, x, y + 3, x + 3, y + 4, 'bt')

def frame(direction, step):
    img = canvas()
    bob = 1 if step in (1, 3) else 0
    top = 1 + bob          # 머리 윗줄
    face_y = top + 8
    body_y = face_y + 7
    leg_y = body_y + 9
    swing = {0: 0, 1: 1, 2: 0, 3: -1}[step]
    if direction in ('down', 'up'):
        back = direction == 'up'
        arm(img, 5, body_y + 1, -swing if not back else swing)
        arm(img, 17, body_y + 1, swing if not back else -swing)
        body_front(img, body_y, back)
        legs_front(img, leg_y - bob, step)
        (face_back if back else face_front)(img, face_y)
        helmet(img, top, 0, back)
    else:
        R(img, 8, body_y, 15, body_y + 8, 'n2')
        R(img, 8, body_y, 15, body_y + 4, 'o1'); R(img, 8, body_y + 5, 15, body_y + 5, 'rf'); R(img, 8, body_y + 6, 15, body_y + 8, 'n2')
        R(img, 14, body_y, 15, body_y + 2, 'n1')
        legs_side(img, leg_y - bob, step)
        face_side(img, face_y)
        helmet(img, top, 1)
        arm(img, 11 + {0: 0, 1: -2, 2: 0, 3: 2}[step], body_y + 1, 0)
    img = pk.outline(img, pk.hx('#151218'))
    if direction == 'left':
        img = img[:, ::-1]
    return img

DIRS = ['down', 'left', 'right', 'up']

def sheet():
    out = np.zeros((FH * 4, FW * 4, 4), np.uint8)
    for r, d in enumerate(DIRS):
        for c in range(4):
            out[r * FH:(r + 1) * FH, c * FW:(c + 1) * FW] = frame(d, c)
    return out
