"""K-뉴딜 아카데미 마스코트(공룡 후드) + 제철소 작업복. 24x32, 앞·뒤·왼쪽·오른쪽 × 걷기 4프레임."""
import numpy as np
import pixkit as pk
import character as ch

ch.C.update({k: pk.hx(v) for k, v in dict(
    t1='#7fdccb', t2='#4fbfae', t3='#35a091', t4='#257a6f', t5='#1a5c54',
    pk1='#f2b2ae', pk2='#d68585', fw='#f6f3ec', fo='#f0a03a', tf='#ec8a2c', tf2='#c46a1c',
    th='#ffffff', fk='#f8d6c0', fk2='#ebbca3', ck='#ec8a87', ey='#140f14', mo='#6e2228', tg='#e57d7d',
    wg='#26262e', wg2='#3a3a44').items()})
R, P = ch.R, ch.P

def hood_shape(img, top, x0=2, x1=21, cols=('t2', 't3', 't4')):
    rows = [(8, 15), (6, 17), (4, 19), (3, 20), (3, 20), (2, 21), (2, 21), (2, 21), (2, 21), (2, 21), (2, 21), (2, 21), (3, 20), (3, 20), (4, 19), (5, 18), (7, 16)]
    for i, (a, b) in enumerate(rows):
        y = top + i
        R(img, a, y, b, y, cols[0])
        R(img, b - 2, y, b, y, cols[1]); P(img, b, y, cols[2])
        if i < 6: R(img, a + 1, y, a + 2, y, 't1')
    return top + len(rows)

def spike(img, top, x=12):
    R(img, x, top, x + 1, top, 't3'); R(img, x - 1, top + 1, x + 2, top + 1, 't2'); R(img, x - 1, top + 2, x + 3, top + 2, 't2')
    P(img, x + 3, top + 2, 't3')

def ear(img, top, x=3):
    R(img, x + 1, top, x + 1, top, 'pk2'); R(img, x, top + 1, x + 2, top + 1, 'pk1'); R(img, x, top + 2, x + 3, top + 2, 'pk1')
    R(img, x, top + 3, x + 3, top + 3, 'pk2')

def daisy(img, top, x=18):
    P(img, x + 1, top, 'fw'); R(img, x, top + 1, x + 2, top + 1, 'fw'); P(img, x + 1, top + 1, 'fo'); P(img, x + 1, top + 2, 'fw')

def face_front(img, y):
    # 후드 구멍 안 얼굴 (y = 구멍 윗줄)
    rows = [(7, 16), (6, 17), (5, 18), (5, 18), (5, 18), (5, 18), (6, 17), (7, 16)]
    for i, (a, b) in enumerate(rows):
        R(img, a, y + i, b, y + i, 'fk')
    R(img, 7, y + 7, 16, y + 7, 'fk2')
    R(img, 10, y, 13, y, 'tf'); R(img, 10, y + 1, 13, y + 1, 'tf2')        # 주황 앞머리
    P(img, 7, y, 'th'); P(img, 16, y, 'th')                                 # 송곳니
    R(img, 7, y + 2, 8, y + 4, 'ey'); R(img, 15, y + 2, 16, y + 4, 'ey')    # 큰 눈
    P(img, 7, y + 2, 'th'); P(img, 15, y + 2, 'th')
    R(img, 5, y + 4, 6, y + 5, 'ck'); R(img, 17, y + 4, 18, y + 5, 'ck')    # 볼
    R(img, 10, y + 5, 13, y + 5, 'mo'); R(img, 11, y + 6, 12, y + 6, 'tg')  # 웃는 입
    P(img, 11, y + 3, 'ck')

def head_front(img, top):
    spike(img, top); ear(img, top + 1)
    end = hood_shape(img, top + 2)
    daisy(img, top + 4)
    P(img, 8, top + 6, 'ey'); P(img, 15, top + 6, 'ey'); P(img, 11, top + 7, 't5'); P(img, 12, top + 7, 't5')
    face_front(img, top + 9)
    R(img, 6, end - 2, 17, end - 2, 't3')
    return end

def head_back(img, top):
    spike(img, top); ear(img, top + 1, 16)
    end = hood_shape(img, top + 2)
    daisy(img, top + 4, 2)
    for k in range(3):
        R(img, 11, top + 5 + k * 4, 12, top + 6 + k * 4, 't3')
    return end

def head_side(img, top):
    spike(img, top, 9); ear(img, top + 1, 4)
    end = hood_shape(img, top + 2)
    daisy(img, top + 4, 17)
    P(img, 18, top + 6, 'ey')
    y = top + 9
    for i, (a, b) in enumerate([(13, 19), (12, 20), (11, 20), (11, 20), (11, 20), (11, 20), (12, 19), (13, 18)]):
        R(img, a, y + i, b, y + i, 'fk')
    R(img, 15, y, 18, y, 'tf'); R(img, 15, y + 1, 18, y + 1, 'tf2'); P(img, 19, y, 'th')
    R(img, 16, y + 2, 17, y + 4, 'ey'); P(img, 16, y + 2, 'th')
    R(img, 13, y + 4, 14, y + 5, 'ck'); R(img, 18, y + 5, 19, y + 5, 'mo'); P(img, 19, y + 6, 'tg')
    return end

def wings(img, y, both=False):
    R(img, 18, y, 20, y, 'wg2'); R(img, 18, y + 1, 21, y + 1, 'wg'); R(img, 19, y + 2, 20, y + 2, 'wg')
    if both:
        R(img, 3, y, 5, y, 'wg2'); R(img, 2, y + 1, 5, y + 1, 'wg'); R(img, 3, y + 2, 4, y + 2, 'wg')

def frame(direction, step):
    img = ch.canvas()
    bob = 1 if step in (1, 3) else 0
    top = bob
    swing = {0: 0, 1: 1, 2: 0, 3: -1}[step]
    if direction in ('down', 'up'):
        back = direction == 'up'
        body_y = 20 + bob
        wings(img, body_y, both=back)
        ch.arm(img, 5, body_y + 1, -swing if not back else swing)
        ch.arm(img, 17, body_y + 1, swing if not back else -swing)
        ch.body_front(img, body_y, back)
        ch.legs_front(img, body_y + 8 - bob, step)
        (head_back if back else head_front)(img, top)
    else:
        body_y = 20 + bob
        R(img, 4, body_y, 6, body_y, 'wg2'); R(img, 3, body_y + 1, 7, body_y + 1, 'wg')
        R(img, 8, body_y, 15, body_y + 7, 'n2')
        R(img, 8, body_y, 15, body_y + 3, 'o1'); R(img, 8, body_y + 4, 15, body_y + 4, 'rf'); R(img, 8, body_y + 5, 15, body_y + 7, 'n2')
        R(img, 14, body_y, 15, body_y + 2, 'n1')
        ch.legs_side(img, body_y + 8 - bob, step)
        head_side(img, top)
        ch.arm(img, 11 + {0: 0, 1: -2, 2: 0, 3: 2}[step], body_y + 1, 0)
    img = pk.outline(img, pk.hx('#151218'))
    if direction == 'left':
        img = img[:, ::-1]
    return img

def sheet():
    out = np.zeros((32 * 4, 24 * 4, 4), np.uint8)
    for r, d in enumerate(ch.DIRS):
        for c in range(4):
            out[r * 32:(r + 1) * 32, c * 24:(c + 1) * 24] = frame(d, c)
    return out
