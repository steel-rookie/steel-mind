"""채팅용 동그라미 프로필: Agent별 색 + 표정(기본·찬성·불만·경고)."""
import io, sys
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, 'tools')
import pixkit as pk, character as ch, mascot

LOOKS = {  # 후드, 얼굴, 윗옷, 바지 (마스코트 색 바꾸기)
 'production': (('#7fdccb','#4fbfae','#35a091','#257a6f','#1a5c54'), ('#f8d6c0','#ebbca3'), ('#ff9a3c','#ee7a22','#c45c18'), ('#3a4f86','#2a3a68','#1d284c')),
 'quality':    (('#a9c8ff','#7aa7ff','#5a86e0','#3f66b8','#2c4a8a'), ('#c8f0b0','#a8d890'), ('#b07ae8','#9058d0','#6c3cb0'), ('#3a6a3a','#2c522c','#1e3a1e')),
 'equipment':  (('#ffb8d0','#f28bb0','#d86a92','#b04f72','#843a56'), ('#a8dcf0','#88c0d8'), ('#5aa8e8','#3c88d0','#2a66a8'), ('#6a4a2a','#52381e','#3a2814')),
 'safety':     (('#b8e8a0','#8fd27a','#6cb35a','#4f8c42','#386830'), ('#f8c8e8','#e8a8d0'), ('#f088c8','#d868b0','#b04890'), ('#2a5a3a','#1e462c','#14321e')),
 'chief':      (('#ffe08a','#f0c040','#d0a030','#a07820','#785a18'), ('#f8d6c0','#ebbca3'), ('#e8e4dc','#c8c2b8','#9a948a'), ('#2a2a34','#1e1e26','#14141a')),
 'player':     (('#7fdccb','#4fbfae','#35a091','#257a6f','#1a5c54'), ('#f8d6c0','#ebbca3'), ('#ff9a3c','#ee7a22','#c45c18'), ('#3a4f86','#2a3a68','#1d284c')),
}
BG = {'base': None, 'agree': ('#2f5a40', '#6fcf97'), 'angry': ('#6a3324', '#ff8a5b'), 'warn': ('#6a5520', '#f0c040')}
TINT = {'production': '#2b3a5c', 'quality': '#3d2f57', 'equipment': '#4f3a24', 'safety': '#24443a', 'chief': '#4d4220', 'player': '#22443d'}
R, P = ch.R, ch.P

def face(img, y, mood):
    R(img, 5, y, 18, y + 6, 'fk'); R(img, 5, y + 6, 18, y + 6, 'fk2')
    R(img, 10, y, 13, y, 'tf'); R(img, 10, y + 1, 13, y + 1, 'tf2'); P(img, 7, y, 'th'); P(img, 16, y, 'th')
    R(img, 5, y + 4, 6, y + 5, 'ck'); R(img, 17, y + 4, 18, y + 5, 'ck')
    if mood == 'agree':      # ^^ 눈, 크게 웃는 입
        for x0 in (7, 15):
            P(img, x0, y + 3, 'ey'); P(img, x0 + 1, y + 2, 'ey'); P(img, x0 + 2, y + 3, 'ey') if x0 == 7 else P(img, x0 - 1, y + 3, 'ey')
        R(img, 10, y + 4, 13, y + 4, 'mo'); R(img, 10, y + 5, 13, y + 5, 'mo'); R(img, 11, y + 6, 12, y + 6, 'tg')
    elif mood == 'angry':    # 찌푸린 눈썹, 내려간 입, 화난 표시
        R(img, 7, y + 3, 8, y + 4, 'ey'); R(img, 15, y + 3, 16, y + 4, 'ey')
        P(img, 6, y + 1, 'ey'); P(img, 7, y + 1, 'ey'); P(img, 8, y + 2, 'ey')
        P(img, 17, y + 1, 'ey'); P(img, 16, y + 1, 'ey'); P(img, 15, y + 2, 'ey')
        P(img, 10, y + 6, 'mo'); P(img, 11, y + 5, 'mo'); P(img, 12, y + 5, 'mo'); P(img, 13, y + 6, 'mo')
        for (dx, dy) in ((19, y - 2), (21, y - 2), (20, y - 3), (20, y - 1)):
            img[dy, dx, :3] = pk.hx('#ff4a3a'); img[dy, dx, 3] = 255
    elif mood == 'warn':     # 동그란 눈, 작은 입, 땀방울
        R(img, 7, y + 2, 8, y + 4, 'ey'); R(img, 15, y + 2, 16, y + 4, 'ey'); P(img, 7, y + 2, 'th'); P(img, 15, y + 2, 'th')
        P(img, 6, y + 1, 'ey'); P(img, 7, y + 0, 'ey'); P(img, 17, y + 1, 'ey'); P(img, 16, y + 0, 'ey')
        R(img, 11, y + 5, 12, y + 6, 'mo')
        for dy in (y + 1, y + 2): img[dy, 19, :3] = pk.hx('#7fd0ff'); img[dy, 19, 3] = 255
        img[y + 3, 19, :3] = pk.hx('#4aa8e0'); img[y + 3, 19, 3] = 255
    else:                     # 기본
        R(img, 7, y + 2, 8, y + 4, 'ey'); R(img, 15, y + 2, 16, y + 4, 'ey'); P(img, 7, y + 2, 'th'); P(img, 15, y + 2, 'th')
        R(img, 10, y + 5, 13, y + 5, 'mo'); R(img, 11, y + 6, 12, y + 6, 'tg')

def head(agent, mood):
    orig = {k: v.copy() for k, v in ch.C.items()}
    hood, fc, top, pants = LOOKS[agent]
    for k, c in zip(('t1','t2','t3','t4','t5'), hood): ch.C[k] = pk.hx(c)
    ch.C['fk'], ch.C['fk2'] = pk.hx(fc[0]), pk.hx(fc[1])
    ch.C['o1'], ch.C['o2'], ch.C['o3'] = [pk.hx(c) for c in top]
    ch.C['n1'], ch.C['n2'], ch.C['n3'] = [pk.hx(c) for c in pants]
    img = ch.canvas()
    mascot.wings(img, 20)
    ch.body_front(img, 20, False)
    mascot.spike(img, 0); mascot.ear(img, 1)
    mascot.hood_shape(img, 2); mascot.daisy(img, 4)
    P(img, 8, 6, 'ey'); P(img, 15, 6, 'ey'); P(img, 11, 7, 't5'); P(img, 12, 7, 't5')
    face(img, 9, mood)
    R(img, 6, 17, 17, 17, 't3')
    img = pk.outline(img, pk.hx('#151218'))
    ch.C.clear(); ch.C.update(orig)
    return img[0:26]

def avatar(agent, mood, size=112):
    bg, ring = BG[mood] if BG[mood] else (TINT[agent], '#3a3d44')
    base = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    d.ellipse([0, 0, size - 1, size - 1], fill=tuple(int(v) for v in pk.hx(ring)) + (255,))
    d.ellipse([5, 5, size - 6, size - 6], fill=tuple(int(v) for v in pk.hx(bg)) + (255,))
    h = Image.fromarray(head(agent, mood)).resize((24 * 4, 26 * 4), Image.NEAREST)
    layer = Image.new('RGBA', (size, size), (0, 0, 0, 0)); layer.alpha_composite(h, ((size - h.width) // 2, 10))
    mask = Image.new('L', (size, size), 0); ImageDraw.Draw(mask).ellipse([5, 5, size - 6, size - 6], fill=255)
    m2 = Image.new('L', (size, size), 0); ImageDraw.Draw(m2).rectangle([0, 0, size, 60], fill=255)   # 위로 튀어나온 귀·지느러미는 살림
    from PIL import ImageChops
    mask = ImageChops.lighter(mask, m2)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'), mask))
    base.alpha_composite(layer)
    return base

if __name__ == '__main__':
    out = sys.argv[1]
    sheet = Image.new('RGBA', (6 * 120, 4 * 120), (30, 32, 37, 255))
    for r, mood in enumerate(('base', 'agree', 'angry', 'warn')):
        for c, ag in enumerate(LOOKS):
            a = avatar(ag, mood)
            b = io.BytesIO(); a.save(b, 'PNG'); open(f'{out}/{ag}_{mood}.bytes', 'wb').write(b.getvalue())
            sheet.alpha_composite(a, (c * 120 + 4, r * 120 + 4))
    sheet.save('avatars_sheet.png')
    print('ok')
