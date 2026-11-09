"""STEEL MIND 로고 (도트, 오리지널 글자꼴).

가늘고 각진 산업용 글자 + 크롬 금속 그라데이션 + 어두운 테두리 + 옅은 푸른 빛.
결과는 투명 PNG (게임에서 Point 필터로 확대).
"""
import sys
import numpy as np
from PIL import Image

H, S = 18, 3          # 글자 높이, 획 두께
MID = H // 2 - 1      # 가운데 가로획 시작 행


def blank(w):
    return np.zeros((H, w), bool)


def chamfer(m, c=2, corners='tl tr bl br'):
    h, w = m.shape
    for y in range(c):
        for x in range(c - y):
            if 'tl' in corners: m[y, x] = False
            if 'tr' in corners: m[y, w - 1 - x] = False
            if 'bl' in corners: m[h - 1 - y, x] = False
            if 'br' in corners: m[h - 1 - y, w - 1 - x] = False
    return m


def S_():
    w = 13; m = blank(w)
    m[0:S, :] = True; m[MID:MID + S, :] = True; m[H - S:, :] = True
    m[:MID + S, 0:S] = True; m[MID:, w - S:] = True
    m[S:S + 2, w - S:] = True          # 위 오른쪽 짧은 꼬리
    m[H - S - 2:H - S, 0:S] = True     # 아래 왼쪽 짧은 꼬리
    return chamfer(m, 3)


def T_():
    w = 13; m = blank(w)
    m[0:S, :] = True; m[:, w // 2 - 1:w // 2 + 2] = True
    return m


def E_():
    w = 11; m = blank(w)
    m[:, 0:S] = True; m[0:S, :] = True; m[MID:MID + S, :w - 2] = True; m[H - S:, :] = True
    return chamfer(m, 2, 'tl bl')


def L_():
    w = 11; m = blank(w)
    m[:, 0:S] = True; m[H - S:, :] = True
    return chamfer(m, 2, 'bl')


def M_():
    w = 16; m = blank(w)
    m[:, 0:S] = True; m[:, w - S:] = True
    for y in range(0, 10):                     # 두 사선이 가운데에서 만남
        x = int(round(y * (w / 2 - S) / 9))
        m[y, S - 1 + x:S + 2 + x] = True
        m[y, w - S - 2 - x:w - S + 1 - x] = True
    return chamfer(m, 2, 'tl tr')


def I_():
    m = blank(S); m[:, :] = True
    return m


def N_():
    w = 14; m = blank(w)
    m[:, 0:S] = True; m[:, w - S:] = True
    for y in range(H):
        x = int(round(y * (w - 2 * S) / (H - 1)))
        m[y, S - 1 + x:S + 2 + x] = True
    return chamfer(m, 2, 'tl br')


def D_():
    """왼쪽은 반듯하게, 오른쪽만 크게 깎아서 O와 구분되게"""
    w, c = 13, 5
    m = blank(w)
    m[:, 0:S] = True; m[0:S, :] = True; m[H - S:, :] = True; m[:, w - S:] = True
    for y in range(c):                       # 오른쪽 위·아래를 사선으로 깎기
        for x in range(c - y):
            m[y, w - 1 - x] = False; m[H - 1 - y, w - 1 - x] = False
    for k in range(c):                       # 깎인 사선에도 획 두께를 채움
        m[k, w - c + k - S + 1:w - c + k + 1] = True
        m[H - 1 - k, w - c + k - S + 1:w - c + k + 1] = True
    return m


GLYPH = {'S': S_, 'T': T_, 'E': E_, 'L': L_, 'M': M_, 'I': I_, 'N': N_, 'D': D_}


def word(text, gap=3, space=9):
    parts = []
    for ch in text:
        if ch == ' ':
            parts.append(blank(space))
        else:
            parts.append(GLYPH[ch]()); parts.append(blank(gap))
    return np.concatenate(parts[:-1], axis=1)


def hx(c):
    return np.array([int(c[i:i + 2], 16) for i in (1, 3, 5)], float)


def main(out):
    m = word('STEEL MIND')
    pad = 5
    h, w = m.shape
    M = np.zeros((h + pad * 2, w + pad * 2), bool); M[pad:pad + h, pad:pad + w] = m
    HH, WW = M.shape
    img = np.zeros((HH, WW, 4), float)

    # 옅은 푸른 빛 (테두리 바깥 몇 칸)
    from scipy.ndimage import distance_transform_edt as edt
    d = edt(~M)
    bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0
    th = bayer[np.arange(HH)[:, None] % 4, np.arange(WW)[None, :] % 4]
    for k, a in ((2, 70), (3, 40), (4, 18)):   # 바깥으로 갈수록 옅어지는 단계형 빛
        ring = (d > k - 1) & (d <= k)
        img[ring] = [*hx('#3d8cff'), a]

    # 아래로 깔리는 그림자(깊이감) + 어두운 테두리
    drop = np.zeros_like(M); drop[2:, :] = M[:-2, :]
    outline = (d > 0) & (d <= 1.5)
    img[(drop | outline) & ~M] = [*hx('#0a1020'), 255]
    img[drop & ~M & ~outline] = [*hx('#1c2a48'), 255]

    # 크롬 금속: 위는 밝고, 가운데에서 확 어두워졌다가 아래로 다시 밝아짐
    rows = np.arange(HH) - pad
    ramp = []
    for r in rows:
        t = r / (H - 1)
        if t < 0.08: c = '#ffffff'
        elif t < 0.30: c = '#eef3f8'
        elif t < 0.47: c = '#d3dce7'
        elif t < 0.53: c = '#7d8ea6'
        elif t < 0.70: c = '#9fb0c4'
        elif t < 0.88: c = '#bfcbd8'
        else: c = '#dfe7ef'
        ramp.append(hx(c))
    ramp = np.array(ramp)
    for y in range(HH):
        sel = M[y]
        img[y, sel, :3] = ramp[y]; img[y, sel, 3] = 255
    # 획 왼쪽 가장자리에 하이라이트 한 줄
    edge = M & ~np.roll(M, 1, axis=1)
    img[edge & (np.arange(HH)[:, None] - pad < H * 0.47), :3] = hx('#ffffff')

    Image.fromarray(img.clip(0, 255).astype(np.uint8), 'RGBA').save(out + '/title_logo.png')
    Image.fromarray(img.clip(0, 255).astype(np.uint8), 'RGBA').resize((WW * 6, HH * 6), Image.NEAREST).save(out + '/title_logo_x6.png')
    print('ok', WW, HH)


if __name__ == '__main__':
    main(sys.argv[1])
