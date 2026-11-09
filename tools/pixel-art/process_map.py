"""제선·제강·연주 공정 맵 생성기 (압연은 make_rolling_map.py).

    python3 process_map.py <ironmaking|steelmaking|casting> <모델.glb> <출력 폴더>
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixkit as pk, equip, ground, fx as fxmod

CONFIGS = {
    'ironmaking': dict(
        title='제선 공장  IRONMAKING', scene='Ironmaking', S=30, front=2.6,
        groups={'sinter_plant': ['EQ_sinter_plant'], 'coke_oven': ['EQ_coke_oven'], 'blast_furnace': ['EQ_blast_furnace'],
                'hot_stove': ['EQ_hot_stove'], 'taphole_casthouse': ['EQ_taphole_casthouse'], 'torpedo_car': ['EQ_torpedo_car'],
                'gas_cleaning': ['I7_Gas_Cleaning'], 'raw_yard': ['I1_Raw_Material_Yard', 'Raw_Yard'],
                'conveyor': ['I4_Stockhouse_Charging_Conveyor'], 'rails': ['Rails_Torpedo']},
        separate=['torpedo_car'], flat=['rails'],
        labels={'sinter_plant': '소결기', 'coke_oven': '코크스 오븐', 'blast_furnace': '고로', 'hot_stove': '열풍로',
                'taphole_casthouse': '출선구·주상', 'torpedo_car': '토페도카', 'gas_cleaning': '가스 청정', 'raw_yard': '원료 야드',
                'conveyor': '장입 컨베이어', 'rails': '토페도 레일'},
        equipment=['sinter_plant', 'coke_oven', 'blast_furnace', 'hot_stove', 'taphole_casthouse', 'torpedo_car']),
    'steelmaking': dict(
        title='제강 공장  STEELMAKING', scene='Steelmaking', S=34, front=2.4,
        groups={'hot_metal_pretreatment': ['EQ_hot_metal_pretreatment'], 'bof_converter': ['EQ_bof_converter'],
                'oxygen_lance_offgas': ['EQ_oxygen_lance_offgas'], 'tapping_ladle_crane': ['EQ_tapping_ladle_crane'],
                'secondary_refining': ['EQ_secondary_refining'], 'ladle_transfer': ['EQ_ladle_transfer'],
                'caster': ['S6_Caster_Tower', 'S6_Strands_Segments', 'S6_Turret_Ladle_Tundish'],
                'rails': ['Rails_Main'], 'pit': ['Floor_Pit_Grating']},
        separate=['ladle_transfer'], flat=['rails', 'pit'],
        labels={'hot_metal_pretreatment': '용선 예비처리', 'bof_converter': '전로', 'oxygen_lance_offgas': '산소 랜스·배가스',
                'tapping_ladle_crane': '출강 래들·크레인', 'secondary_refining': '2차 정련', 'ladle_transfer': '래들 이송',
                'caster': '연주 설비(→ 연주 공장)', 'rails': '래들 레일', 'pit': '피트'},
        equipment=['hot_metal_pretreatment', 'bof_converter', 'oxygen_lance_offgas', 'tapping_ladle_crane', 'secondary_refining', 'ladle_transfer']),
    'casting': dict(
        title='연주 공장  CONTINUOUS CASTING', scene='Casting', S=32, front=2.4, title_x=640,
        groups={'ladle_turret': ['EQ_ladle_turret'], 'tundish': ['EQ_tundish'], 'cc_mold': ['EQ_cc_mold'],
                'secondary_cooling': ['EQ_secondary_cooling'], 'withdrawal_straightener': ['EQ_withdrawal_straightener'],
                'torch_cutter': ['EQ_torch_cutter'], 'turret_structure': ['C1_Turret_Structure'], 'casting_floor': ['C2_Casting_Floor'],
                'strands': ['C4_Strands_Hot'], 'ladle_crane': ['C1_Ladle_Crane']},
        separate=['ladle_crane'], flat=[], overhead=['ladle_crane'],
        labels={'ladle_turret': '래들 터릿', 'tundish': '턴디시', 'cc_mold': '주형', 'secondary_cooling': '2차 냉각',
                'withdrawal_straightener': '인발·교정 롤', 'torch_cutter': '절단기', 'turret_structure': '터릿 구조물',
                'casting_floor': '주조 작업대', 'strands': '주편', 'ladle_crane': '래들 크레인'},
        equipment=['ladle_turret', 'tundish', 'cc_mold', 'secondary_cooling', 'withdrawal_straightener', 'torch_cutter']),
}

def plan(cfg, parts):
    """모델 크기로 축척·지도 크기를 정한다"""
    S = cfg['S']
    allv = np.concatenate([t.reshape(-1, 3) for tag, _, m, t in parts])
    floor = [t for tag, _, m, t in parts if tag == 'Floor_Slab']
    fv = np.concatenate([t.reshape(-1, 3) for t in floor]) if floor else allv
    xmin, xmax = min(fv[:, 0].min(), allv[:, 0].min()), max(fv[:, 0].max(), allv[:, 0].max())
    zmin, zmax = fv[:, 2].min(), max(fv[:, 2].max(), allv[:, 2].max())
    z_back = zmin
    need = ((allv[:, 1] - (allv[:, 2] - z_back)) * S).max()     # 높은 설비가 지도 위로 넘지 않게
    wall_h = int(np.ceil(max(170, need + 24) / 16) * 16)
    x0 = xmin - 1.2
    map_w = int(np.ceil(((xmax - xmin) + 2.4) * S / 16) * 16)
    map_h = int(np.ceil((wall_h + (zmax - z_back + cfg['front']) * S) / 16) * 16)
    return S, x0, z_back, wall_h, map_w, map_h

def draw_wall(img, wall_h, W, title, seed, title_x=40):
    ground.MAP_W = W; ground.WALL_H = 160
    ground.COLUMNS = [c for c in (96 + 192 * k for k in range(W // 192 + 1)) if c + 200 < W]
    upper = wall_h - 160
    if upper > 0:
        H = upper
        yy, xx = np.mgrid[0:H, 0:W]
        lvl = 2.6 + np.array([1.0, 0.5, 0.0, -0.4, -0.9, -0.3])[xx % 6] - 1.8 * (1 - yy / max(H, 1)) ** 1.5
        lvl += (pk.fbm(H, W, seed + 30) - 0.5) * 1.2
        lvl += np.where(yy % 40 == 0, -2.0, np.where(yy % 40 == 1, 0.8, 0))
        ground.ramp_fill(img[:H], np.ones((H, W), bool), ground.WALLR, lvl)
        img[0:6, :, :3] = ground.WALLR[0]
        D = pk.RAMPS['dark']; B = pk.RAMPS['bright']; St = pk.RAMPS['steel']
        for c in ground.COLUMNS:
            if c + 18 >= W:
                continue
            img[6:H, c - 1, :3] = D[0]; img[6:H, c + 18, :3] = D[0]
            img[6:H, c:c + 1, :3] = B[5]; img[6:H, c + 1:c + 4, :3] = B[3]
            img[6:H, c + 4:c + 14, :3] = St[1]; img[6:H, c + 14:c + 18, :3] = St[3]
        # 높은 창 줄
        for y0 in range(24, H - 60, 110):
            for c in ground.COLUMNS[:-1]:
                mid = c + 105
                for wx in (mid - 66, mid + 6):
                    if wx + 62 >= W:
                        continue
                    img[y0 - 1:y0 + 49, wx - 1:wx + 61, :3] = D[0]
                    sub = img[y0:y0 + 48, wx:wx + 60]
                    py_, px_ = np.mgrid[y0:y0 + 48, wx:wx + 60]
                    g = 2.4 + ((px_ + py_ * 2) % 23 < 6) * 1.0 - (py_ - y0) / 40
                    ground.ramp_fill(sub, np.ones((48, 60), bool), pk.RAMPS['slate'], g, wx, y0)
                    sub[:, ::20, :3] = St[2]; sub[::24, :, :3] = St[2]
    ground.wall(img[upper:upper + 160], seed)
    # 간판
    boxes = []
    def sign(x, y, txt, color, panel, glow=None, w=None):
        m = ground.text_mask(txt, 13)
        w = w or m.shape[1] + 20
        img[y - 1:y + 23, x - 1:x + w + 1, :3] = pk.RAMPS['dark'][0]
        img[y:y + 22, x:x + w, :3] = pk.RAMPS['steel'][3]
        img[y + 1:y + 21, x + 1:x + w - 1, :3] = pk.hx(panel)
        tx = x + (w - m.shape[1]) // 2
        sub = img[y + 2:y + 2 + m.shape[0], tx:tx + m.shape[1]]
        sub[m[:sub.shape[0], :sub.shape[1]], :3] = pk.hx(color)
        if glow:
            boxes.append((x, y, w, 22, glow))
    by = upper + 110
    sign(title_x, by, title, '#c9c2c4', '#232b36')
    sign(W // 2 - 56 if title_x < W // 3 else 40 + 220, by, '안전 제일', '#ffb04a', '#0b0a0e', '#ff9a3a', 112)
    sign(W - 200, by, '무재해 365일', '#ff7a5a', '#0b0a0e', '#ff6a4a', 128)
    return boxes

def draw_floor(img, wall_h, W, H, objmask, foot, labels, lamps, hot_spots, seed):
    yy, xx = np.mgrid[0:H, 0:W]
    Hf = H - wall_h
    light = np.zeros((Hf, W))
    for cx, cy, rx, ry, s in lamps:
        light += 2.4 * s * ground.ellipse_field(H, W, cx, cy, rx, ry)[wall_h:] ** 0.7
    for cx, cy in hot_spots:
        light += 1.4 * ground.ellipse_field(H, W, cx, cy, 90, 40)[wall_h:]
    fy = yy[wall_h:]; fx = xx[wall_h:]
    light -= 1.8 * np.clip(1 - (fy - wall_h) / 22, 0, 1)
    light -= 1.0 * np.clip(1 - fx / 60, 0, 1) + 1.0 * np.clip(1 - (W - fx) / 60, 0, 1) + 0.8 * np.clip((fy - (H - 60)) / 60, 0, 1)
    om = objmask[wall_h:].astype(np.float32)
    sh = np.zeros_like(om); sh[6:, 7:] = om[:-6, :-7]
    sh = np.asarray(Image.fromarray((sh * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(3)), np.float32) / 255
    light -= 2.2 * sh
    lvl = 3.4 + light + (pk.fbm(Hf, W, seed + 2) - 0.5) * 1.6
    lvl -= ((fx % 96 == 0) | ((fy - wall_h) % 96 == 0)) * 1.3
    lvl -= np.clip(pk.value_noise(Hf, W, 10, seed + 5) - 0.78, 0, 1) * 9
    sub = img[wall_h:]
    ground.ramp_fill(sub, np.ones((Hf, W), bool), ground.FLOORR, lvl, 0, wall_h)
    # 설비 둘레 위험 띠
    zone = np.zeros((H, W), bool)
    for x, y, w, h in foot:
        zone[max(y - 6, wall_h + 6):y + h + 6, max(x - 6, 0):x + w + 6] = True
    inner = np.asarray(Image.fromarray((zone * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(9))) > 0
    border = zone & ~inner
    stripe = ((xx + yy) // 4) % 2 == 0
    paint = [(pk.RAMPS['ochre'], 3.0, border & stripe), (pk.RAMPS['dark'], 1.2, border & ~stripe)]
    lane = (yy >= H - 52) & (yy < H - 28) & (xx >= 24) & (xx < W - 24) & ~zone
    paint.append((pk.RAMPS['green'], 1.6, lane))
    paint.append((pk.RAMPS['ochre'], 3.2, lane & ((yy == H - 52) | (yy == H - 51) | (yy == H - 30) | (yy == H - 29))))
    txt = np.zeros((H, W), bool)
    for t, cx, y in labels:
        m = ground.text_mask(t, 11)
        x = int(cx - m.shape[1] // 2); y = int(min(y, H - 70))
        x = max(4, min(x, W - m.shape[1] - 4))
        txt[y:y + m.shape[0], x:x + m.shape[1]] |= m
    paint.append((pk.RAMPS['ochre'], 2.8, txt))
    front = (yy >= H - 8)
    paint.append((ground.WALLR, 2.0, front & (yy == H - 8))); paint.append((ground.WALLR, 0.6, front & (yy > H - 8)))
    for ramp, base, mask in paint:
        ground.ramp_fill(sub, mask[wall_h:], ramp, base + light * 0.8, 0, wall_h)

def hot_center(s):
    if 'hot' not in s or not s['hot'].any():
        return None
    ys, xs = np.nonzero(s['hot'])
    return float(xs.mean() + s['rect'][0]), float(ys.mean() + s['rect'][1])

def glow_sprite(s):
    hm = s['hot']
    m = Image.fromarray((hm * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.BoxBlur(4))
    m = np.asarray(m, np.float32) / 255
    th = pk.bayer(*m.shape)
    a = np.zeros(hm.shape + (4,), np.uint8)
    lv = np.floor(m * 3 + th * 0.9) / 3
    a[..., :3] = pk.hx('#ffb04a'); a[..., 3] = (lv * 150).astype(np.uint8)
    return a

def sparks_frames(n=4, size=20, seed=0):
    out = []
    for f in range(n):
        rng = np.random.default_rng(seed + f)
        a = np.zeros((size, size, 4), np.uint8)
        for _ in range(14):
            ang = rng.uniform(-np.pi, 0); r = rng.uniform(2, size / 2 - 1)
            x = int(size / 2 + np.cos(ang) * r); y = int(size / 2 + np.sin(ang) * r * 0.9 + f)
            if 0 <= x < size and 0 <= y < size:
                a[y, x, :3] = pk.hx(rng.choice(['#ffe08a', '#ffb04a', '#ff7a2a'])); a[y, x, 3] = 255
        out.append(a)
    return out

def bucket():
    a = np.zeros((8, 10, 4), np.uint8)
    a[1:7, 1:9, :3] = pk.RAMPS['rust'][4]; a[1:3, 1:9, :3] = pk.RAMPS['rust'][5]; a[3:7, 1:9, :3] = pk.RAMPS['rust'][3]
    return pk.outline(a)

def main(kind, glb_path, out):
    cfg = CONFIGS[kind]
    os.makedirs(out, exist_ok=True)
    import glb
    _, parts = glb.triangles(glb_path)
    S, x0, z_back, wall_h, W, H = plan(cfg, parts)
    equip.setup(S, x0, z_back, wall_h, W, H)
    print(kind, 'S', S, 'map', W, H, 'wall', wall_h)
    sprites, j, parts = equip.build_groups(glb_path, cfg['groups'], cfg['separate'])
    objmask = np.zeros((H, W), bool)
    for k, s in sprites.items():
        x, y, w, h = s['rect']
        if k not in cfg.get('overhead', []):
            objmask[y:y + h, x:x + w] |= s['rgba'][..., 3] > 0
    bg = np.zeros((H, W, 4), np.uint8)
    boxes = draw_wall(bg, wall_h, W, cfg['title'], 11, cfg.get('title_x', 40))
    foot = [sprites[k]['footprint'] for k in cfg['equipment'] if k in sprites]
    labels = []
    for k in cfg['equipment']:
        if k in sprites:
            fx_, fy_, fw, fh = sprites[k]['footprint']
            labels.append((cfg['labels'][k], fx_ + fw / 2, fy_ + fh + 8))
    lamps = [(cx, cy, 70, 34, 0.9) for cx in range(160, W, 220) for cy in range(wall_h + 120, H - 40, 200)]
    hots = [hot_center(s) for s in sprites.values()]
    hots = [h for h in hots if h]
    draw_floor(bg, wall_h, W, H, objmask, foot, labels, lamps, hots, 13)
    bg[..., 3] = 255
    Image.fromarray(bg).save(f'{out}/ground.png')
    floor_lights = [(cx, cy, rx * 1.15, ry * 1.15, '#ffcf8a', 30 * s) for cx, cy, rx, ry, s in lamps]
    floor_lights += [(cx, cy, 110, 46, '#ff8a3a', 40) for cx, cy in hots]
    Image.fromarray(ground.glow(floor_lights, [])).save(f'{out}/glow_floor.png')
    air = ground.glow([], boxes)
    Image.fromarray(air).save(f'{out}/glow_air.png')

    layout = dict(map=dict(width=W, height=H, tile=16, pixelsPerUnit=16, wallHeight=wall_h, title=cfg['title']),
                  layers=[dict(id='ground', file='ground.png', order=-1000), dict(id='glow_floor', file='glow_floor.png', order=-900),
                          dict(id='glow_air', file='glow_air.png', order=9500)],
                  objects=[], effects=[], spawn=[W // 2, H - 40],
                  colliders=[dict(id='back_wall', rect=[0, 0, W, wall_h + 6]), dict(id='front_wall', rect=[0, H - 8, W, 8]),
                             dict(id='left_edge', rect=[-8, 0, 8, H]), dict(id='right_edge', rect=[W, 0, 8, H])])
    for k, s in sprites.items():
        Image.fromarray(s['rgba']).save(f'{out}/eq_{k}.png')
        fp = s['footprint']
        o = dict(id=k, label=cfg['labels'].get(k, k), file=f'eq_{k}.png', rect=s['rect'], sortY=fp[1] + fp[3])
        if k in cfg.get('flat', []):
            o['sortY'] = -800
        elif k in cfg.get('overhead', []):
            o['sortY'] = 9000
        else:
            o['collider'] = fp
        if k in cfg['equipment']:
            from make_rolling_map import equip_warn
            Image.fromarray(equip_warn(s['rgba'])).save(f'{out}/eq_{k}_warn.png')
            o['warnFile'] = f'eq_{k}_warn.png'; o['equipment'] = True
        layout['objects'].append(o)
        if s['hot'].any() and k not in cfg.get('flat', []):
            Image.fromarray(glow_sprite(s)).save(f'{out}/fx_glow_{k}.png')
            layout['effects'].append(dict(type='pulse', file=f'fx_glow_{k}.png', x=s['rect'][0], y=s['rect'][1], order=o['sortY'] + 1, speed=1.6 + 0.4 * len(layout['effects'])))
    # 공용 연출 그림
    for i, a in enumerate(sparks_frames()): Image.fromarray(a).save(f'{out}/fx_sparks_{i}.png')
    for i, a in enumerate(fxmod.steam_frames()): Image.fromarray(a).save(f'{out}/fx_steam_{i}.png')
    for i, a in enumerate(fxmod.spray(60, 30, 10)): Image.fromarray(a).save(f'{out}/fx_spray_{i}.png')
    E = layout['effects']
    def top_of(k, frac=0.5):
        x, y, w, h = sprites[k]['rect']; a = sprites[k]['rgba'][..., 3] > 0
        cx = int(w * frac); col = np.nonzero(a[:, cx])[0]
        return x + cx, y + (col.min() if len(col) else 0)
    sparks = [f'fx_sparks_{i}.png' for i in range(4)]; steam = [f'fx_steam_{i}.png' for i in range(4)]
    spray = [f'fx_spray_{i}.png' for i in range(3)]
    if kind == 'ironmaking':
        to = equip.to_px
        # 컨베이어 벨트 위로 원료 버킷
        belt = [t for tag, _, m, t in parts if tag in cfg['groups']['conveyor'] and j['materials'][m]['name'] == 'Belt_Rubber']
        v = np.concatenate([t.reshape(-1, 3) for t in belt]) if belt else None
        if v is not None:
            xs = np.linspace(v[:, 0].min() + 0.2, v[:, 0].max() - 0.2, 8)
            path = []
            for xv in xs:
                sel = np.abs(v[:, 0] - xv) < 0.6
                if not sel.any():
                    continue
                yv = v[sel, 1].max(); zv = v[sel, 2].mean()
                px, py = to(xv, yv, zv); path += [round(float(px)) - 5, round(float(py)) - 7]
            Image.fromarray(bucket()).save(f'{out}/fx_bucket.png')
            E.append(dict(type='spawner', file='fx_bucket.png', path=path, speed=40, interval=1.1, order=sprites['conveyor']['footprint'][1] + sprites['conveyor']['footprint'][3] + 1))
        for k in ('coke_oven', 'gas_cleaning', 'sinter_plant'):
            if k in sprites:
                tx, ty = top_of(k, 0.5)
                E.append(dict(type='steam', x0=tx - 30, x1=tx + 30, y=ty - 4, rate=0.35, order=9100))
        c = hot_center(sprites['taphole_casthouse']) if 'taphole_casthouse' in sprites else None
        if c:
            E.append(dict(type='frames', files=sparks, x=c[0] - 10, y=c[1] - 14, fps=10, order=9050))
        if 'torpedo_car' in sprites:
            rx = sprites['rails']['rect'] if 'rails' in sprites else None
            x, y, w, h = sprites['torpedo_car']['rect']
            far = (rx[0] + rx[2] - w - 10) if rx else x + 150
            E.append(dict(type='mover', target='torpedo_car', path=[x, y, far, y], speed=30, pause=2.5, pingpong=True))
    elif kind == 'steelmaking':
        if 'ladle_transfer' in sprites:
            x, y, w, h = sprites['ladle_transfer']['rect']
            rx = sprites['rails']['rect'] if 'rails' in sprites else [x - 400, y, 400, 10]
            E.append(dict(type='mover', target='ladle_transfer', path=[x, y, rx[0] + 20, y], speed=45, pause=2.0, pingpong=True))
        for k in ('bof_converter', 'secondary_refining'):
            c = hot_center(sprites[k]) if k in sprites else None
            if c:
                E.append(dict(type='frames', files=sparks, x=c[0] - 10, y=c[1] - 16, fps=12, order=9050))
        if 'oxygen_lance_offgas' in sprites:
            tx, ty = top_of('oxygen_lance_offgas', 0.85)
            E.append(dict(type='steam', x0=tx - 20, x1=tx + 20, y=ty - 2, rate=0.3, order=9100))
        if 'tapping_ladle_crane' in sprites:
            E.append(dict(type='shake', target='tapping_ladle_crane', period=0.12))
    elif kind == 'casting':
        if 'torch_cutter' in sprites and sprites['torch_cutter']['hot'].any():
            s = sprites['torch_cutter']; ys, xs = np.nonzero(s['hot'])
            yrow = int(np.median(ys)) + s['rect'][1]
            x0p, x1p = xs.min() + s['rect'][0], xs.max() + s['rect'][0]
            slab = fxmod.block(40, 7, 6, 3)
            Image.fromarray(slab).save(f'{out}/fx_cut_slab.png')
            E.append(dict(type='spawner', file='fx_cut_slab.png', path=[int(x0p), yrow - 9, int(x1p) - 40, yrow - 9], speed=28, interval=3.2,
                          order=s['footprint'][1] + s['footprint'][3] + 1))
            E.append(dict(type='frames', files=sparks, x=int(x0p) + 8, y=yrow - 22, fps=14, order=9050))
        if 'secondary_cooling' in sprites:
            x, y, w, h = sprites['secondary_cooling']['rect']
            E.append(dict(type='frames', files=spray, x=x + w // 2 - 30, y=y + h // 3, fps=12, order=9040))
        if 'ladle_crane' in sprites:
            x, y, w, h = sprites['ladle_crane']['rect']
            E.append(dict(type='mover', target='ladle_crane', path=[x, y, x + 120, y], speed=12, pause=3, pingpong=True))
        if 'tundish' in sprites:
            tx, ty = top_of('tundish', 0.5)
            E.append(dict(type='steam', x0=tx - 25, x1=tx + 25, y=ty - 2, rate=0.4, order=9100))
    # 미니맵
    prev = Image.fromarray(bg).convert('RGBA'); prev.alpha_composite(Image.open(f'{out}/glow_floor.png'))
    for o in sorted(layout['objects'], key=lambda o: o['sortY']):
        prev.alpha_composite(Image.open(f"{out}/{o['file']}"), tuple(o['rect'][:2]))
    prev.alpha_composite(Image.open(f'{out}/glow_air.png'))
    prev.save(f'{out}/preview.png')
    mm = prev.resize((W // 4, H // 4), Image.BOX)
    b = Image.new('RGBA', (mm.width + 4, mm.height + 4), (11, 10, 14, 255))
    from PIL import ImageDraw
    ImageDraw.Draw(b).rectangle([0, 0, b.width - 1, b.height - 1], outline=(232, 173, 34, 255)); b.alpha_composite(mm, (2, 2))
    b.save(f'{out}/ui_minimap.png')
    layout['scene'] = cfg['scene']
    json.dump(layout, open(f'{out}/layout.json', 'w'), ensure_ascii=False, indent=1, default=int)
    return layout

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3])
