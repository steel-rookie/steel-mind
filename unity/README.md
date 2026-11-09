# Unity 프로젝트 (STEEL MIND)

- Unity **6000.3.27f1** (팀 모두 같은 버전), 템플릿 Universal 2D
- Unity Hub → 추가 → 디스크에서 프로젝트 추가 → `unity/SteelMind` 폴더 선택
- `Assets/SteelMind/Scenes/Rolling.unity`를 열고 ▶ Play

| 조작 | 키 |
|---|---|
| 이동 | WASD / 방향키 |
| 전체 공정 보기 | Tab 또는 왼쪽 위 버튼 |
| 미니맵 켜기·끄기 | M |

게임 엔진에서 라인 화면을 바꿀 때는 `RollingLine`을 쓴다.
- `SetWarning("finishing_mill", true)` 그 설비를 빨갛게 깜빡임
- `SetRunning(false)` 라인 정지(공정 연출 멈춤)

설비 id: `reheating_furnace` `descaler` `roughing_mill` `finishing_mill` `runout_table` `coiler`

## 같이 작업할 때

- `Rolling.unity`는 한 사람만 고친다. 다른 사람은 프리팹이나 자기 테스트 장면에서 작업한다.
- `.meta` 파일은 항상 같이 커밋한다. `Library/`는 올리지 않는다(.gitignore에 있음).
- 그림을 다시 뽑는 법은 `tools/pixel-art/README.md`.
