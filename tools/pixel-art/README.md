# 도트 그림·배경음 생성기

압연 맵 그림은 ST-ROOKIE의 열간압연 3D 모델(`st-rookie/dev` 브랜치의 `frontend/3d-demo/models/rolling.glb`)을 탑다운 시점으로 렌더링해 도트로 바꾼 것이다. 그림을 고치려면 여기 스크립트를 고치고 다시 뽑는다.

```bash
pip install numpy pillow
git show origin/st-rookie/dev:frontend/3d-demo/models/rolling.glb > /tmp/rolling.glb
python3 tools/pixel-art/make_rolling_map.py /tmp/rolling.glb /tmp/out   # 배경·설비·소품·layout.json
python3 tools/pixel-art/fx.py /tmp/out                                  # 공정 연출 그림
python3 tools/pixel-art/bgm.py /tmp/out/bgm_rolling_mill.wav            # 배경음 (직접 합성)
```

뽑은 PNG와 `layout.json`을 `unity/SteelMind/Assets/SteelMind/Art/Rolling/`에 덮어쓰면 Unity가 장면을 자동으로 다시 만든다(메뉴: STEEL MIND > 압연 맵 만들기).
캐릭터 그림은 `character.py`의 `frame()`으로 뽑는다(`player_{down,left,right,up}_{0..3}.png`).

- `make_rolling_map.py` 전체 실행 / `equip.py` 3D → 도트 / `ground.py` 벽·바닥·조명 / `props.py` 소품 / `fx.py` 공정 연출 / `character.py` 작업자 / `bgm.py` 배경음
- 한글 간판은 macOS 기본 글꼴(Apple SD Gothic Neo)로 그린다.
