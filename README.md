# STEEL MIND

팀 철든신입 · POSCO K-뉴딜 아카데미 내부 해커톤 (2026-11-09 ~ 11-10)

AI 에이전트들의 엇갈린 제안을 듣고, 제철소 운영 책임자로서 최종 결정을 내리는 시뮬레이션 게임이다. 생산·품질·설비·안전 에이전트가 같은 사건을 보고 서로 다른 행동을 제안하고, 플레이어가 그중 하나를 고른다. 결과는 게임 엔진이 규칙표로 계산한다.

- 기획안: [`docs/steel-mind-game-plan.md`](docs/steel-mind-game-plan.md)
- 시안: [`docs/steel-mind-prototype.html`](docs/steel-mind-prototype.html). 브라우저로 열면 압연 미션을 처음부터 결과까지 해 볼 수 있다.
- 가상의 교육·체험 시뮬레이션이다. 수치는 게임 밸런스용이고 실제 제철소 기준이 아니다.

## 흐름

```
공장 현황 → 사건 발생 → 에이전트 회의 → 플레이어 결정 → 엔진이 지표 계산
  → 미션이 남았으면 다음 사건, 끝났으면 최종 평가·등급
```

## LLM과 게임 엔진의 경계

| LLM(에이전트) | 게임 엔진 |
|---|---|
| 상황 요약, 원인 설명, 행동 제안, 제안끼리 차이 설명 | 상태값, 선택지 검증, 점수 계산, 다음 사건, 승패 판정 |

LLM은 행동을 제안할 뿐 점수를 직접 바꾸지 못한다. 같은 선택을 하면 언제나 같은 결과가 나온다.

## 역할

| 맡을 일 | 담당 | 폴더 |
|---|---|---|
| 게임 시스템·에이전트 구조·통합 | | `engine/` |
| 화면과 제철소 배경 | | `frontend/` |
| 시나리오와 점수 규칙 | | `scenarios/` |
| LLM 에이전트 응답과 결과 화면 | | `llm/` `frontend/` |

담당자는 팀에서 정해 채운다.

## 폴더

- `engine/` 게임 엔진: 상태값, 선택지 검증, 점수 계산, 다음 사건, 승패 판정
- `scenarios/` 공정별 사건, 에이전트 입장, 선택지와 지표 효과 규칙표
- `llm/` 에이전트 응답 생성과 공장장 쟁점 정리
- `frontend/` 화면 4개: 공장 대시보드, 에이전트 회의실, 의사결정, 결과 보고서
- `schemas/` 서로 주고받는 JSON 형식
- `docs/` 기획서, 시안, 발표 자료

## 주고받는 형식

에이전트 응답은 에이전트마다 `{ say, why, rec }`이다. 예시는 [`schemas/agent-proposal.example.json`](schemas/agent-proposal.example.json)에 있다. `rec`는 그 사건의 선택지 id 중 하나여야 하고, 엔진이 이를 확인한다.

## 이전 프로젝트(ST-ROOKIE) 가져다 쓰기

ST-ROOKIE `dev` 브랜치의 전체 이력을 이 레포의 `st-rookie/dev` 브랜치로 옮겨 두었다. `main`과는 이력이 따로라서 통째로 합치지 않고, 필요한 파일만 꺼내 쓴다.

```bash
git fetch origin

# 파일 목록 보기
git ls-tree -r --name-only origin/st-rookie/dev | grep frontend/

# 같은 경로로 내 작업 브랜치에 가져오기
git checkout origin/st-rookie/dev -- frontend/login_ui/exam.css

# 다른 경로에 두기
git show origin/st-rookie/dev:frontend/3d-demo/data_v2.js > frontend/data_v2.js
```

자주 쓸 파일

| 무엇 | ST-ROOKIE 경로 |
|---|---|
| 공정 4개·설비 24개의 id와 이름 | `frontend/3d-demo/data_v2.js` (`PROCESSES[].equipment[]`) |
| 설비 id 표와 규칙 | `docs/equipment-ids.md` |
| 화면 디자인 토큰 (시안이 쓰는 색·글꼴) | `frontend/login_ui/exam.css` |
| 열간압연 3D 모델 | `frontend/3d-demo/models/rolling.glb` |

다른 브랜치까지 보려면 원래 레포 [`steel-rookie/ST-ROOKIE`](https://github.com/steel-rookie/ST-ROOKIE)를 본다.

## 설정

```
cp .env.example .env   # GEMINI_API_KEY 등을 채운다
```

## 작업 규칙

- `main`에 직접 올리지 않는다. 브랜치를 만들어 작업하고 Pull Request로 합친다.
- **Pull Request는 다른 팀원 1명 이상이 승인해야 합친다.** 무료 조직 플랜의 비공개 레포라 GitHub가 이 규칙을 강제하지 못한다. 팀 약속으로 지키고, 승인 없이 합치지 않는다.
- 승인한 뒤에 새로 커밋했으면 다시 승인을 받는다.
- 브랜치 이름: `engine/…`, `scenarios/…`, `llm/…`, `frontend/…`, `docs/…`처럼 폴더 이름으로 시작한다.
- `.env`는 Pull Request에도 넣지 않는다.

## 커밋 메시지

이전 프로젝트(ST-ROOKIE)와 같은 형식을 쓴다.

```
type(scope): 영어로 짧게 핵심 요약

한글로 "왜/무엇을" 설명했습니다.

- 실제 변경 1
- 실제 변경 2
- 실제 변경 3
```
