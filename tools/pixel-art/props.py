"""소품 정의 (world 좌표). 지도 px로 생각하고 px_to_world로 바꾼다."""
import numpy as np
from equip import box, cyl_x, cyl_y, cyl_z, px_to_world, S

def W(px):  # px 길이 → world 길이
    return px / S

def pulpit(px, py_front):
    """운전실: 앞쪽 아래 모서리가 (px, py_front)인 상자 건물. 폭 180px, 깊이 52px, 높이 46px"""
    x0, z1 = px_to_world(px, py_front); x1 = x0 + W(180); z0 = z1 - W(52); h = W(46)
    P = [('Struct_Gray', box(x0, x1, 0, h, z0, z1)),
         ('Steel_Dark', box(x0 - W(3), x1 + W(3), h, h + W(4), z0 - W(3), z1 + W(3))),
         ('Steel', box(x0 + W(20), x0 + W(52), h + W(4), h + W(14), z0 + W(10), z0 + W(34))),
         ('Steel', box(x1 - W(48), x1 - W(20), h + W(4), h + W(12), z0 + W(12), z0 + W(32))),
         ('Grating', box(x0 + W(70), x0 + W(130), h + W(4), h + W(6), z0 + W(8), z0 + W(40))),
         ('Steel_Dark', box(x0 + W(140), x0 + W(166), 0, W(36), z1, z1 + W(1)))]   # 문
    for i in range(4):  # 창문 4개 (불 켜짐)
        wx = x0 + W(10 + i * 32)
        P.append(('Lit_Window', box(wx, wx + W(26), W(18), W(38), z1, z1 + W(0.6))))
        P.append(('Screen', box(wx + W(4), wx + W(12), W(18), W(25), z1 + W(0.6), z1 + W(1.2))))
        P.append(('Screen', box(wx + W(15), wx + W(23), W(18), W(24), z1 + W(0.6), z1 + W(1.2))))
    P.append(('Safety_Yellow', box(x0, x1, 0, W(3), z1, z1 + W(0.5))))
    return P

def coil_row(px, py_front, n=3):
    """권취 끝난 코일을 받침대 위에 n개"""
    x0, z1 = px_to_world(px, py_front)
    r = W(14); depth = W(24); gap = W(5)
    P = []
    for i in range(n):
        cx = x0 + r + i * (2 * r + gap)
        P.append(('Steel_Dark', box(cx - r, cx + r, 0, W(4), z1 - depth - W(2), z1)))
        P.append(('Coil_Cooled', cyl_z(cx, W(4) + r, z1 - depth, z1 - W(1), r, n=24, inner=W(5))))
        P.append(('Graphite', box(cx - W(4), cx + W(4), W(4) + r - W(4), W(4) + r + W(4), z1 - depth, z1 - depth + W(1))))
    return P

def slab_stack(px, py_front, layers=3, warm_top=False):
    x0, z1 = px_to_world(px, py_front)
    P = [('Steel_Dark', box(x0 + W(4), x0 + W(10), 0, W(4), z1 - W(30), z1 - W(2))),
         ('Steel_Dark', box(x0 + W(66), x0 + W(72), 0, W(4), z1 - W(30), z1 - W(2)))]
    for i in range(layers):
        y0 = W(4) + i * W(8)
        mat = 'Rust' if (layers - 1 - i) % 2 == 0 else 'Slab_Cold'
        if warm_top and i == layers - 1:
            mat = 'Hot_Slab'
        P.append((mat, box(x0, x0 + W(76), y0, y0 + W(6), z1 - W(30), z1 - W(1))))
        if i < layers - 1:
            for bx in (W(8), W(66)):
                P.append(('Steel_Dark', box(x0 + bx, x0 + bx + W(4), y0 + W(6), y0 + W(8), z1 - W(30), z1 - W(1))))
    return P

def barrels(px, py_front, n=3):
    x0, z1 = px_to_world(px, py_front)
    P = []
    for i in range(n):
        cx = x0 + W(8 + i * 17); cz = z1 - W(8 + (i % 2) * 6)
        P.append(('Paint_Red' if i == 1 else 'Blue_Steel', cyl_y(cx, cz, 0, W(20), W(7))))
        P.append(('Steel_Dark', cyl_y(cx, cz, W(20), W(21), W(5))))
    return P

def cabinet(px, py_front, w=40, h=34, color='Paint_Green'):
    x0, z1 = px_to_world(px, py_front)
    return [(color, box(x0, x0 + W(w), 0, W(h), z1 - W(14), z1)),
            ('Steel_Dark', box(x0 + W(w / 2 - 0.5), x0 + W(w / 2 + 0.5), W(4), W(h - 4), z1, z1 + W(0.6))),
            ('Screen', box(x0 + W(4), x0 + W(8), W(h - 8), W(h - 5), z1, z1 + W(0.6)))]

def cones(px, py_front, n=3, step=22):
    x0, z1 = px_to_world(px, py_front)
    P = []
    for i in range(n):
        cx = x0 + W(i * step)
        P.append(('Steel_Dark', box(cx - W(5), cx + W(5), 0, W(2), z1 - W(10), z1)))
        P.append(('Safety_Yellow', cyl_y(cx, z1 - W(5), W(2), W(14), W(3), n=10)))
    return P

PROPS = {
    # id: (만드는 함수, 충돌 여부)
    'pulpit':      (lambda: pulpit(700, 476), True),
    'coil_row_a':  (lambda: coil_row(1250, 470, 3), True),
    'coil_row_b':  (lambda: coil_row(1250, 530, 3), True),
    'coil_row_c':  (lambda: coil_row(1385, 530, 2), True),
    'slab_a':      (lambda: slab_stack(60, 462, 3), True),
    'slab_b':      (lambda: slab_stack(60, 538, 4), True),
    'slab_c':      (lambda: slab_stack(158, 506, 2), True),
    'barrels':     (lambda: barrels(560, 470), True),
    'cabinet_a':   (lambda: cabinet(16, 180, 40, 34), True),
    'cabinet_b':   (lambda: cabinet(1430, 180, 26, 30, 'Paint_Red'), True),
    'cones':       (lambda: cones(1000, 400, 4, 30), False),
}
