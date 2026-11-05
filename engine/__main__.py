"""명령줄: python -m engine <명령>

  sim       내부 모의 데이터 만들기 (개발용)
  train     정상 운전 회차로 모델 배우기
  judge     한 회차 판정 → 판정 카드 출력
  batch     폴더 전체 판정 → 판정 카드 JSONL (정답 없이)
  score     판정 카드 JSONL + 라벨로 지표 계산 (데이터·검증 담당용)
  evaluate  sim 형식 폴더를 판정·현장 확인까지 평가 (정답이 든 폴더에만)
  serve     판정 카드 API 띄우기
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

from .evaluate import evaluate_dir
from .files import read_jsonl, read_run, run_files, write_jsonl, write_run
from .judge import judge
from .metrics import format_table, predicted_category, summarize
from .model import NormalModel, fit
from .sim import make_equipment, scenario_mix, simulate_run

DEFAULT_MODEL = Path(__file__).parent / "models" / "dev-model.json"


def _parse_check(text: str) -> dict:
    # 예: vib=3.1  또는  vib=4.8:최댓값
    ch, _, rest = text.partition("=")
    value, _, record = rest.partition(":")
    return {"channel": ch.strip(), "value": float(value), "record": record.strip() or "평균"}


def cmd_sim(a):
    out = Path(a.out)
    for sub in ("runs", "truth", "normal"):
        (out / sub).mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(a.seed)
    fleet = [make_equipment(rng) for _ in range(a.equipment)]
    for i in range(a.normal):
        run, _, _ = simulate_run(rng, (), eq=fleet[i % len(fleet)], coupling=a.coupling)
        write_run(out / "normal" / f"normal-{i:03d}.csv", run)
    labels = []
    for i, faults in enumerate(scenario_mix(rng, a.n)):
        eq_idx = int(rng.integers(len(fleet)))
        run, truth, label = simulate_run(rng, faults, eq=fleet[eq_idx], coupling=a.coupling)
        rid = f"run-{i:03d}"
        write_run(out / "runs" / f"{rid}.csv", run)
        write_run(out / "truth" / f"{rid}.csv", truth)
        labels.append({"run_id": rid, "equipment_id": f"motor-{eq_idx}", **label})
    write_jsonl(out / "labels.jsonl", labels)
    print(f"{out}: 정상 학습용 {a.normal}회, 평가용 {a.n}회 (seed {a.seed}, 결합 {a.coupling})")


def cmd_train(a):
    runs = [read_run(p) for p in run_files(a.normal)]
    model = fit(runs)
    model.save(a.out)
    print(json.dumps({"saved": a.out, "runs": len(runs), "lag_T_s": model.lag_T,
                      "tau_f": model.tau_f, "tau_s": model.tau_s,
                      "sigma": {k: round(v, 4) for k, v in model.sigma.items()}}, ensure_ascii=False, indent=2))


def cmd_judge(a):
    model = NormalModel.load(a.model)
    checks = [_parse_check(c) for c in (a.check or [])]
    card = judge(read_run(a.run), model, coupled=a.coupled, checks=checks or None,
                 event_id=Path(a.run).stem, equipment_id=a.equipment_id)
    print(json.dumps(card, ensure_ascii=False, indent=2))


def cmd_batch(a):
    model = NormalModel.load(a.model)
    cards = [judge(read_run(p), model, coupled=a.coupled, event_id=p.stem) for p in run_files(a.runs)]
    write_jsonl(a.out, cards)
    print(f"{len(cards)}회 판정 → {a.out}")


def cmd_score(a):
    cards = {c["event_id"]: c for c in read_jsonl(a.pred)}
    rows = []
    for lab in read_jsonl(a.labels):
        card = cards.get(lab["run_id"])
        if card is None:
            print(f"판정 카드가 없습니다: {lab['run_id']}", file=sys.stderr)
            continue
        rows.append({"truth": lab["category"], "faults": lab["faults"], "pred": predicted_category(card)})
    print(format_table({"판정": summarize(rows)}))


def cmd_evaluate(a):
    model = NormalModel.load(a.model)
    before, after, details = evaluate_dir(model, a.data, coupled=a.coupled, field_check=not a.no_check, seed=a.seed)
    print(format_table({"도구만": before, "현장 확인 1회 후": after}))
    if a.details:
        write_jsonl(a.details, details)
        print(f"회차별 결과 → {a.details}")


def cmd_serve(a):
    from .api import serve
    serve(NormalModel.load(a.model), str(a.model), host=a.host, port=a.port)


def main(argv=None):
    p = argparse.ArgumentParser(prog="python -m engine", description="측정 신뢰성 판별 엔진")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("sim", help="내부 모의 데이터 만들기 (개발용)")
    s.add_argument("--out", required=True)
    s.add_argument("--n", type=int, default=40)
    s.add_argument("--normal", type=int, default=12)
    s.add_argument("--equipment", type=int, default=4)
    s.add_argument("--seed", type=int, default=101)
    s.add_argument("--coupling", type=float, default=0.0, help="토크 추정값이 측정 전류에서 오는 비율(0~1)")
    s.set_defaults(fn=cmd_sim)

    s = sub.add_parser("train", help="정상 운전 회차로 모델 배우기")
    s.add_argument("--normal", required=True, help="정상 운전 CSV 폴더")
    s.add_argument("--out", default=str(DEFAULT_MODEL))
    s.set_defaults(fn=cmd_train)

    s = sub.add_parser("judge", help="한 회차 판정")
    s.add_argument("--run", required=True)
    s.add_argument("--model", default=str(DEFAULT_MODEL))
    s.add_argument("--coupled", action="store_true", help="토크 추정값이 측정 전류에서 계산됨 (결합 모드)")
    s.add_argument("--check", action="append", help="현장 확인 결과. 예: vib=3.1 또는 vib=4.8:최댓값")
    s.add_argument("--equipment-id")
    s.set_defaults(fn=cmd_judge)

    s = sub.add_parser("batch", help="폴더 전체 판정 (정답 없이)")
    s.add_argument("--runs", required=True)
    s.add_argument("--out", required=True)
    s.add_argument("--model", default=str(DEFAULT_MODEL))
    s.add_argument("--coupled", action="store_true")
    s.set_defaults(fn=cmd_batch)

    s = sub.add_parser("score", help="판정 카드 + 라벨로 지표 계산")
    s.add_argument("--pred", required=True)
    s.add_argument("--labels", required=True)
    s.set_defaults(fn=cmd_score)

    s = sub.add_parser("evaluate", help="정답이 든 폴더 평가 (현장 확인 1회 포함)")
    s.add_argument("--data", required=True)
    s.add_argument("--model", default=str(DEFAULT_MODEL))
    s.add_argument("--coupled", action="store_true")
    s.add_argument("--no-check", action="store_true", help="현장 확인 흉내를 끔")
    s.add_argument("--seed", type=int, default=0)
    s.add_argument("--details", help="회차별 결과 JSONL 저장 경로")
    s.set_defaults(fn=cmd_evaluate)

    s = sub.add_parser("serve", help="판정 카드 API")
    s.add_argument("--model", default=str(DEFAULT_MODEL))
    s.add_argument("--host", default="127.0.0.1")
    s.add_argument("--port", type=int, default=8000)
    s.set_defaults(fn=cmd_serve)

    a = p.parse_args(argv)
    a.fn(a)


if __name__ == "__main__":
    main()
