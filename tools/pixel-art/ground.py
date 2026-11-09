"""뒷벽 + 바닥 + 바닥 표시 + 그림자/조명 (한 장으로 굽는 배경)"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import pixkit as pk
from equip import MAP_W, MAP_H, WALL_H

FONT = '/System/Library/Fonts/AppleSDGothicNeo.ttc'
WALLR = pk.RAMPS['warm'] * 0  # placeholder replaced below
WALLR = np.array([pk.hx(c) for c in ['#0d0c0f', '#141216', '#1b181c', '#221e22', '#2a2528', '#332d30', '#3e3639', '#4b4245']])
FLOORR = np.array([pk.hx(c) for c in ['#121011', '#181516', '#1e1b1b', '#252120', '#2c2826', '#34302d', '#3d3834', '#48413c', '#554c45']])
COLUMNS = [96 + 192 * k for k in range(8)]
LAMPS = [(192 * k, 352, 70, 34, 1.0) for k in range(1, 8)] + [(192 * k + 96, 480, 64, 30, 0.85) for k in range(0, 8)] + \
        [(330, 186, 40, 14, 0.6), (1010, 186, 40, 14, 0.6)]

def text_mask(txt, size, bold=True):
    f = ImageFont.truetype(FONT, size, index=6 if bold else 2)
    l, t, r, b = f.getbbox(txt)
    im = Image.new('L', (r - l + 2, b - t + 2), 0)
    d = ImageDraw.Draw(im); d.fontmode = '1'
    d.text((1 - l, 1 - t), txt, font=f, fill=255)
    return np.asarray(im) > 0

def ramp_fill(img, mask, ramp, level, ox=0, oy=0):
    H, W = mask.shape
    th = pk.bayer(H, W, ox, oy)
    li = np.clip(np.floor(level + th), 0, len(ramp) - 1).astype(int)
    img[mask, :3] = ramp[li[mask]]
    img[mask, 3] = 255

def ellipse_field(H, W, cx, cy, rx, ry):
    yy, xx = np.mgrid[0:H, 0:W]
    return np.clip(1 - ((xx - cx) / rx) ** 2 - ((yy - cy) / ry) ** 2, 0, 1)

def wall(img, seed):
    H, W = WALL_H, MAP_W
    yy, xx = np.mgrid[0:H, 0:W]
    lvl = np.full((H, W), 3.4)
    rib = np.array([1.0, 0.5, 0.0, -0.4, -0.9, -0.3])
    lvl += rib[xx % 6]
    seam = yy % 40
    lvl += np.where(seam == 0, -2.0, np.where(seam == 1, 0.8, 0))
    lvl -= 2.6 * (1 - yy / H) ** 1.6
    lvl += (pk.fbm(H, W, seed) - 0.5) * 1.4
    streak = pk.value_noise(H, W, 3, seed + 9)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8)).resize((W, H)), np.float32) / 255
    vn = np.asarray(Image.fromarray((pk.value_noise(8, W // 3, 1, seed + 3) * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR), np.float32) / 255
    lvl -= np.clip(vn - 0.6, 0, 1) * 3.0 * (yy > 100)
    ramp_fill(img, np.ones((H, W), bool), WALLR, lvl)
    B = pk.RAMPS['bright']; D = pk.RAMPS['dark']; St = pk.RAMPS['steel']
    # 천장 어둠 + 크레인 주행 보
    img[0:6, :, :3] = WALLR[0]
    rows = {6: B[4], 7: B[3], 8: St[3], 9: D[2], 10: D[2], 11: D[2], 12: D[2], 13: D[2], 14: St[2], 15: St[3], 16: D[0]}
    for y, c in rows.items():
        img[y, :, :3] = c
    img[11, ::16, :3] = B[4]
    # 창문
    rng = np.random.default_rng(seed)
    for cxol in COLUMNS[:-1]:
        mid = cxol + 9 + 96
        for wx in (mid - 66, mid + 6):
            x0, x1, y0, y1 = wx, wx + 60, 26, 74
            img[y0 - 1:y1 + 1, x0 - 1:x1 + 1, :3] = D[0]
            img[y0:y1, x0:x1, :3] = St[2]
            for c in range(3):
                for r in range(2):
                    px0, py0 = x0 + 2 + c * 19, y0 + 2 + r * 23
                    pw, ph = 17, 21
                    py_, px_ = np.mgrid[py0:py0 + ph, px0:px0 + pw]
                    g = 2.6 + ((px_ + py_ * 2) % 23 < 6) * 1.1 - (py_ - y0) / 40
                    if rng.random() < 0.18:
                        g = g - 2.2
                    sl = (slice(py0, py0 + ph), slice(px0, px0 + pw))
                    sub = img[sl]
                    ramp_fill(sub, np.ones((ph, pw), bool), pk.RAMPS['slate'], g, px0, py0)
            img[y1:y1 + 2, x0 - 2:x1 + 2, :3] = St[4]
            img[y1 + 2, x0 - 2:x1 + 2, :3] = D[0]
    # 기둥 (H빔)
    for c in COLUMNS:
        img[17:160, c - 1, :3] = D[0]; img[17:160, c + 18, :3] = D[0]
        img[17:160, c:c + 1, :3] = B[5]; img[17:160, c + 1:c + 4, :3] = B[3]
        img[17:160, c + 4:c + 14, :3] = St[1]; img[17:160, c + 14:c + 18, :3] = St[3]
        for y in range(40, 160, 48):
            img[y:y + 3, c + 4:c + 14, :3] = St[3]; img[y + 3, c + 4:c + 14, :3] = D[0]
        img[150:160, c - 3:c + 21, :3] = St[3]; img[150, c - 3:c + 21, :3] = B[4]
        img[153:158:3, c - 1:c + 20:4, :3] = B[5]
    # 케이블 트레이와 배관
    img[80:85, :, :3] = D[1]; img[80, :, :3] = St[3]; img[82, ::5, :3] = St[2]
    pipe = [1, 3, 4, 5, 5, 4, 3, 3, 2, 1]
    Ru = pk.RAMPS['slate']
    for i, l in enumerate(pipe):
        img[88 + i, :, :3] = Ru[min(l, len(Ru) - 1)]
    for fx in range(40, W, 96):
        img[86:100, fx:fx + 4, :3] = Ru[4]; img[86:100, fx + 4, :3] = D[0]; img[86, fx:fx + 4, :3] = Ru[6]
    Oc = pk.RAMPS['ochre']
    for i, l in enumerate([1, 3, 5, 3, 1]):
        img[101 + i, :, :3] = Oc[l]
    for c in COLUMNS:
        img[98:108, c + 20:c + 23, :3] = St[3]
    # 벽 아래 위험 표시 띠
    k = ((xx[150:159] + yy[150:159]) // 5) % 2 == 0
    band = img[150:159]
    band[k, :3] = Oc[4]; band[~k, :3] = D[1]
    img[159, :, :3] = D[0]
    for c in COLUMNS:
        img[150:160, c - 3:c + 21, :3] = St[3]
    return img

def wall_details(img):
    D = pk.RAMPS['dark']; St = pk.RAMPS['steel']; B = pk.RAMPS['bright']
    def board(x0, y0, w, h, panel, frame):
        img[y0 - 1:y0 + h + 1, x0 - 1:x0 + w + 1, :3] = D[0]
        img[y0:y0 + h, x0:x0 + w, :3] = frame
        img[y0 + 1:y0 + h - 1, x0 + 1:x0 + w - 1, :3] = panel
    def put_text(txt, size, x, y, color, w=None):
        m = text_mask(txt, size)
        if w is not None:
            x = x + (w - m.shape[1]) // 2
        sub = img[y:y + m.shape[0], x:x + m.shape[1]]
        sub[m[:sub.shape[0], :sub.shape[1]], :3] = color
        return x, m.shape
    glow_boxes = []
    # LED: 안전 제일
    board(564, 110, 112, 22, pk.hx('#0b0a0e'), St[3])
    put_text('안전 제일', 13, 564, 112, pk.hx('#ffb04a'), 104)
    glow_boxes.append((564, 110, 112, 22, '#ff9a3a'))
    # 라인 이름판
    board(1072, 110, 152, 22, pk.RAMPS['slate'][2], St[3])
    put_text('열연 2라인  HSM-2', 13, 1072, 112, pk.hx('#c9c2c4'), 140)
    # 무재해 전광판
    board(884, 110, 128, 22, pk.hx('#0b0a0e'), St[3])
    put_text('무재해 365일', 13, 884, 112, pk.hx('#ff7a5a'), 116)
    glow_boxes.append((884, 110, 128, 22, '#ff6a4a'))
    # 출입문 + 비상구 표시
    dx = 776
    img[117:159, dx - 1:dx + 31, :3] = D[0]; img[118:159, dx:dx + 30, :3] = St[2]
    img[118:159, dx + 14:dx + 16, :3] = D[1]; img[136:139, dx + 10:dx + 13, :3] = B[5]; img[136:139, dx + 17:dx + 20, :3] = B[5]
    img[122:130, dx + 4:dx + 12, :3] = pk.RAMPS['slate'][4]; img[122:130, dx + 18:dx + 26, :3] = pk.RAMPS['slate'][4]
    img[108:114, dx + 7:dx + 23, :3] = pk.hx('#3f8f5a'); img[110:112, dx + 9:dx + 21, :3] = pk.hx('#9fe0b0')
    glow_boxes.append((dx + 7, 108, 16, 6, '#5fd08a'))
    # 게시물
    for i, (px, py) in enumerate([(818, 120), (832, 124), (846, 119), (720, 122), (734, 126)]):
        img[py:py + 14, px:px + 11, :3] = pk.hx('#8f877c') if i % 2 else pk.hx('#a59c8e')
        img[py + 14, px:px + 11, :3] = D[0]
        for ly in range(py + 3, py + 12, 2):
            img[ly, px + 2:px + 9 - (ly % 3), :3] = pk.hx('#5e574f')
    # 기둥마다 소화기함
    for c in COLUMNS:
        x = c + 22
        img[134:150, x:x + 9, :3] = pk.RAMPS['red'][3]; img[134, x:x + 9, :3] = pk.RAMPS['red'][4]
        img[137:147, x + 3:x + 6, :3] = pk.RAMPS['red'][1]; img[150, x:x + 9, :3] = D[0]
    # 오른쪽 끝 셔터(출하구)
    x0, x1 = 1462, 1532
    img[38:160, x0 - 2:x1 + 2, :3] = St[3]; img[38:160, x0 - 3, :3] = D[0]
    for y in range(40, 152):
        img[y, x0:x1, :3] = St[2] if y % 3 else St[1]
    img[40:44, x0:x1, :3] = D[1]
    k = ((np.arange(x0, x1)[None, :] + np.arange(150, 158)[:, None]) // 5) % 2 == 0
    img[150:158, x0:x1, :3] = np.where(k[..., None], pk.RAMPS['ochre'][4], D[1])
    board(x0 + 13, 26, 44, 9, pk.RAMPS['slate'][2], St[3])
    put_text('출하', 10, x0 + 13, 25, pk.hx('#c9c2c4'), 44)
    return glow_boxes

def floor(img, objects_mask, seed, paint):
    H, W = MAP_H - WALL_H, MAP_W
    yy, xx = np.mgrid[WALL_H:MAP_H, 0:W]
    light = np.zeros((H, W))
    for cx, cy, rx, ry, s in LAMPS:
        light += 2.4 * s * ellipse_field(MAP_H, W, cx, cy, rx, ry)[WALL_H:] ** 0.7
    light += 1.4 * ellipse_field(MAP_H, W, 265, 352, 210, 46)[WALL_H:]
    light -= 1.8 * np.clip(1 - (yy - WALL_H) / 22, 0, 1)
    light -= 1.0 * np.clip(1 - xx / 60, 0, 1) + 1.0 * np.clip(1 - (W - xx) / 60, 0, 1) + 0.8 * np.clip((yy - 500) / 60, 0, 1)
    om = objects_mask[WALL_H:]
    sh = np.zeros_like(om, dtype=np.float32)
    sh[6:, 7:] = om[:-6, :-7]
    sh = np.asarray(Image.fromarray((sh * 255).astype(np.uint8)).filter(__import__('PIL.ImageFilter', fromlist=['BoxBlur']).BoxBlur(3)), np.float32) / 255
    light -= 2.2 * sh
    lvl = 3.4 + light + (pk.fbm(H, W, seed + 2) - 0.5) * 1.6
    jx = (xx % 96 == 0); jy = (yy - WALL_H) % 96 == 0
    lvl -= (jx | jy) * 1.3
    lvl += (((xx - 1) % 96 == 0) | ((yy - WALL_H - 1) % 96 == 0)) * 0.5
    # 얼룩
    stains = pk.value_noise(H, W, 10, seed + 5)
    lvl -= np.clip(stains - 0.78, 0, 1) * 9
    sub = img[WALL_H:]
    ramp_fill(sub, np.ones((H, W), bool), FLOORR, lvl, 0, WALL_H)
    # 칠한 표시 (조명 같이 받음)
    for ramp, base, mask in paint:
        m = mask[WALL_H:]
        ramp_fill(sub, m, ramp, base + light * 0.8, 0, WALL_H)
    return light

def paint_masks():
    """바닥 칠: (ramp, 기본 단계, mask) 목록과 바닥 글자"""
    H, W = MAP_H, MAP_W
    yy, xx = np.mgrid[0:H, 0:W]
    P = []
    Oc = pk.RAMPS['ochre']; D = pk.RAMPS['dark']; G = pk.RAMPS['green']; B = pk.RAMPS['bright']
    # 설비 위험 구역 테두리 (노랑/검정 빗금)
    zone = ((xx >= 66) & (xx < 1318) & (yy >= 180) & (yy < 320)) | ((xx >= 1318) & (xx < 1434) & (yy >= 180) & (yy < 400))
    inner = ((xx >= 70) & (xx < 1318) & (yy >= 184) & (yy < 316)) | ((xx >= 1314) & (xx < 1430) & (yy >= 184) & (yy < 396))
    border = zone & ~inner
    stripe = ((xx + yy) // 4) % 2 == 0
    P.append((Oc, 3.0, border & stripe)); P.append((D, 1.2, border & ~stripe))
    # 보행자 통로
    lane = (yy >= 352) & (yy < 378) & (xx >= 24) & (xx < 1300)
    P.append((G, 1.6, lane))
    edge = lane & ((yy == 352) | (yy == 353) | (yy == 376) | (yy == 377))
    P.append((Oc, 3.2, edge))
    chev = np.zeros((H, W), bool)
    for cx in range(120, 1300, 192):
        for i in range(6):
            chev[359 + i, cx + i:cx + i + 2] = True; chev[370 - i, cx + i:cx + i + 2] = True
            chev[359 + i, cx + 8 + i:cx + 10 + i] = True; chev[370 - i, cx + 8 + i:cx + 10 + i] = True
    P.append((G, 3.0, chev & lane))
    # 야적장 박스
    def rect_border(x0, y0, x1, y1, t=2):
        r = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
        i = (xx >= x0 + t) & (xx < x1 - t) & (yy >= y0 + t) & (yy < y1 - t)
        return r & ~i
    P.append((Oc, 2.6, rect_border(1236, 418, 1462, 546)))
    P.append((Oc, 2.6, rect_border(46, 418, 250, 546)))
    # 운전실 앞 발판, 입구 발판
    grate = (xx >= 838) & (xx < 870) & (yy >= 478) & (yy < 490)
    grate2 = (xx >= 600) & (xx < 680) & (yy >= 534) & (yy < 552)
    P.append((D, 2.0, (grate | grate2) & ((xx + yy) % 3 != 0)))
    P.append((D, 0.6, (grate | grate2) & ((xx + yy) % 3 == 0)))
    # 배수구
    drains = np.zeros((H, W), bool)
    for cx in range(160, 1300, 224):
        drains[326:332, cx:cx + 14] = True
    P.append((D, 1.4, drains & (xx % 2 == 0))); P.append((D, 0.4, drains & (xx % 2 == 1)))
    labels = [('가열로', 265, 382), ('조압연', 660, 334), ('사상압연', 874, 334), ('런아웃 테이블', 1150, 334),
              ('코일 적치장', 1349, 402), ('슬라브 야드', 204, 404), ('보행자 통로', 470, 359),
              ('보행자 통로', 1046, 359), ('입구', 640, 520)]
    txt = np.zeros((H, W), bool)
    for t, cx, y in labels:
        m = text_mask(t, 11)
        x = cx - m.shape[1] // 2
        txt[y:y + m.shape[0], x:x + m.shape[1]] |= m
    on_lane = txt & lane
    P.append((Oc, 2.8, txt & ~lane)); P.append((G, 3.2, on_lane))
    # 앞쪽 벽 (지도 아래 끝) + 입구
    front = (yy >= 552) & ~((xx >= 600) & (xx < 680))
    P.append((WALLR, 2.0, front & (yy == 552))); P.append((WALLR, 0.6, front & (yy > 552)))
    return P

def glow(lights, boxes, H=MAP_H, W=MAP_W):
    """lights: [(cx, cy, rx, ry, color, max_alpha)] → 단계진 RGBA 빛 레이어"""
    out = np.zeros((H, W, 4), np.float32)
    th = pk.bayer(H, W)
    for cx, cy, rx, ry, col, amax in lights:
        f = ellipse_field(H, W, cx, cy, rx, ry)
        steps = np.floor(f * 3 + th * 0.9) / 3
        a = steps * amax
        c = pk.hx(col).astype(np.float32)
        sel = a > out[..., 3]
        out[sel, :3] = c; out[sel, 3] = a[sel]
    for x, y, w, h, col in boxes:
        lights2 = ellipse_field(H, W, x + w / 2, y + h / 2, w / 2 + 14, h / 2 + 10)
        a = np.floor(lights2 * 3 + th * 0.9) / 3 * 70
        c = pk.hx(col).astype(np.float32)
        sel = a > out[..., 3]
        out[sel, :3] = c; out[sel, 3] = a[sel]
    return out.astype(np.uint8)

def window_shafts(H=MAP_H, W=MAP_W):
    out = np.zeros((H, W, 4), np.uint8)
    th = pk.bayer(H, W)
    yy, xx = np.mgrid[0:H, 0:W]
    for cxol in COLUMNS[:-1]:
        mid = cxol + 9 + 96
        for wx in (mid - 66, mid + 6):
            t = np.clip((yy - 74) / 190, 0, 1)
            x0 = wx + 30 * t; x1 = wx + 60 + 50 * t
            inside = (yy >= 74) & (yy < 264) & (xx >= x0) & (xx < x1)
            a = (1 - t) * 26 * ((th + (1 - t) * 0.6) > 0.55)
            sel = inside & (a > 0)
            out[sel, :3] = pk.hx('#9fb6c4'); out[sel, 3] = np.maximum(out[sel, 3], a[sel].astype(np.uint8))
    return out
