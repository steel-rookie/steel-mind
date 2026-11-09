"""STEEL MIND 압연 맵 생성기.

rolling.glb(ST-ROOKIE 열간압연 모델)를 탑다운 시점으로 렌더링해 도트 스프라이트로 바꾸고,
뒷벽·바닥·조명 레이어와 Unity 배치용 layout.json을 만든다.

    python3 make_rolling_map.py <rolling.glb> <출력 폴더>
"""
import json, os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk, equip, props, ground
from equip import MAP_W, MAP_H, WALL_H

LABELS = {'reheating_furnace': '가열로', 'descaler': '스케일 제거기', 'roughing_mill': '조압연기', 'finishing_mill': '사상압연기',
          'runout_table': '런아웃 테이블', 'coiler': '권취기', 'roller_tables': '롤러 테이블', 'walkway': '작업자 난간',
          'hot_strip': '열연 강판', 'pulpit': '운전실'}
EQUIPMENT = ['reheating_furnace', 'descaler', 'roughing_mill', 'finishing_mill', 'runout_table', 'coiler']

def player_sprite():
    """자리표시용 작업자(16x32, 정면). 크기 기준용"""
    P = {'.': None, 'o': '#0b0a0e', 'h': '#bb9842', 'H': '#e0c070', 'k': '#c99a7a', 'K': '#a87c62', 'e': '#1a1214',
         'n': '#2a3646', 'N': '#3a4a5e', 's': '#c9d2d6', 'b': '#1a1a20', 'g': '#5a4a3a'}
    rows = [
        '................', '................', '.....oooooo.....', '....ohhHHhho....', '...ohhHHHhhho...', '...ohhhhhhhho...',
        '..oooooooooooo..', '....okkkkkko....', '....okekkeko....', '....okkkkkko....', '.....oKkkKo.....', '....oooooooo....',
        '...onnnnnnnno...', '..onNnnnnnnNno..', '..onNnnnnnnNno..', '..onnnnnnnnnno..', '..ossssssssssso.', '..onnnnnnnnnno..',
        '..okonnnnnnokko.', '..okonnnnnnoko..', '...oonnnnnnoo...', '....onnnnnno....', '....onnnoonnno..', '....onnno.onno..',
        '....onnno.onno..', '....onnno.onno..', '....ossso.osso..', '....obbbo.obbo..', '...obbbbo.obbbo.', '...ooooo..oooo..',
        '................', '................']
    img = np.zeros((32, 16, 4), np.uint8)
    for y, r in enumerate(rows):
        for x, ch in enumerate(r[:16]):
            if P.get(ch):
                img[y, x, :3] = pk.hx(P[ch]); img[y, x, 3] = 255
    return img

def main(glb_path, out):
    os.makedirs(out, exist_ok=True)
    print('라인 렌더링...')
    line = equip.build_line(glb_path)
    print('소품 렌더링...')
    prop_sprites = {}
    for i, (pid, (fn, solid)) in enumerate(props.PROPS.items()):
        s = equip.render_prop(fn(), None, 40 + i)
        s['solid'] = solid
        prop_sprites[pid] = s
    # 그림자용 물체 마스크
    objmask = np.zeros((MAP_H, MAP_W), bool)
    for k, s in list(line.items()) + list(prop_sprites.items()):
        if k == 'hot_strip':
            continue
        x, y, w, h = s['rect']
        objmask[y:y + h, x:x + w] |= s['rgba'][..., 3] > 0
    print('배경 그리기...')
    bg = np.zeros((MAP_H, MAP_W, 4), np.uint8)
    ground.wall(bg[:WALL_H], 11)
    boxes = ground.wall_details(bg)
    ground.floor(bg, objmask, 13, ground.paint_masks())
    bg[..., 3] = 255
    Image.fromarray(bg).save(f'{out}/ground.png')
    # 빛
    floor_lights = [(cx, cy, rx * 1.15, ry * 1.15, '#ffcf8a', 34 * s) for cx, cy, rx, ry, s in ground.LAMPS]
    floor_lights += [(265, 350, 220, 50, '#ff8a3a', 46), (790, 492, 110, 22, '#ffb860', 40)]
    Image.fromarray(ground.glow(floor_lights, [])).save(f'{out}/glow_floor.png')
    air = ground.glow([], boxes)
    shafts = ground.window_shafts()
    sel = shafts[..., 3] > air[..., 3]
    air[sel] = shafts[sel]
    Image.fromarray(air).save(f'{out}/glow_air.png')
    sx, sy, sw, sh = line['hot_strip']['rect']
    strip_glow = ground.glow([(sx + sw / 2, sy + sh / 2 + 4, sw / 2 + 30, 26, '#ff8a3a', 40)], [])
    Image.fromarray(strip_glow).save(f'{out}/glow_strip.png')
    # 스프라이트 저장 + layout
    layout = dict(map=dict(width=MAP_W, height=MAP_H, tile=16, pixelsPerUnit=16, wallHeight=WALL_H,
                           note='좌표는 지도 왼쪽 위가 (0,0)인 px. rect=[x,y,w,h], collider는 바닥 발자국, sortY가 클수록 앞에 그린다.'),
                  layers=[dict(id='ground', file='ground.png', order=-1000), dict(id='glow_floor', file='glow_floor.png', order=-900),
                          dict(id='glow_strip', file='glow_strip.png', order=9000, toggle='line_running'),
                          dict(id='glow_air', file='glow_air.png', order=9500)],
                  objects=[], spawn=[640, 536],
                  colliders=[dict(id='back_wall', rect=[0, 0, MAP_W, WALL_H + 6]), dict(id='front_wall_left', rect=[0, 552, 600, 8]),
                             dict(id='front_wall_right', rect=[680, 552, MAP_W - 680, 8]), dict(id='left_edge', rect=[-8, 0, 8, MAP_H]),
                             dict(id='right_edge', rect=[MAP_W, 0, 8, MAP_H])])
    for k, s in line.items():
        Image.fromarray(s['rgba']).save(f'{out}/line_{k}.png')
        o = dict(id=k, label=LABELS[k], file=f'line_{k}.png', rect=s['rect'], sortY=s['footprint'][1] + s['footprint'][3])
        if k in EQUIPMENT:
            Image.fromarray(equip_warn(s['rgba'])).save(f'{out}/line_{k}_warn.png')
            o['warnFile'] = f'line_{k}_warn.png'; o['equipment'] = True
        if k != 'hot_strip':
            o['collider'] = s['footprint']
        else:
            o['toggle'] = 'line_running'
        layout['objects'].append(o)
    for k, s in prop_sprites.items():
        Image.fromarray(s['rgba']).save(f'{out}/prop_{k}.png')
        o = dict(id=k, label=LABELS.get(k, k), file=f'prop_{k}.png', rect=s['rect'], sortY=s['footprint'][1] + s['footprint'][3])
        if s['solid']:
            o['collider'] = s['footprint']
        layout['objects'].append(o)
    Image.fromarray(player_sprite()).save(f'{out}/player_placeholder.png')
    json.dump(layout, open(f'{out}/layout.json', 'w'), ensure_ascii=False, indent=1)
    # 미리보기 합성
    prev = Image.fromarray(bg).convert('RGBA')
    prev.alpha_composite(Image.open(f'{out}/glow_floor.png'))
    objs = sorted(layout['objects'], key=lambda o: o['sortY'])
    pl = Image.fromarray(player_sprite())
    player_at = (632, 392)
    drawn_player = False
    for o in objs:
        if not drawn_player and o['sortY'] > player_at[1] + 32:
            prev.alpha_composite(pl, player_at); drawn_player = True
        prev.alpha_composite(Image.open(f"{out}/{o['file']}"), tuple(o['rect'][:2]))
    if not drawn_player:
        prev.alpha_composite(pl, player_at)
    prev.alpha_composite(Image.open(f'{out}/glow_strip.png'))
    prev.alpha_composite(Image.open(f'{out}/glow_air.png'))
    prev.save(f'{out}/preview.png')
    return layout

def equip_warn(rgba):
    out = rgba.astype(np.float32).copy()
    a = rgba[..., 3] > 0
    out[a, :3] = out[a, :3] * 0.7 + pk.WARN * 0.3
    out = out.astype(np.uint8)
    pad = np.zeros((out.shape[0], out.shape[1], 4), np.uint8); pad[:] = out
    o1 = pk.outline(pad, pk.WARN)
    edge = (o1[..., 3] > 0) & ~a
    res = out.copy(); res[edge] = o1[edge]
    return res

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
