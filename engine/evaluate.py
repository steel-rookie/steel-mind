"""데이터 폴더 하나를 판정하고 지표를 낸다. 현장 확인 1회도 흉내 낸다.

폴더 구조 (sim 명령이 만드는 것과 같다)
  runs/<id>.csv    엔진 입력
  truth/<id>.csv   실제 물리값 (현장 확인 흉내에 씀)
  labels.jsonl     {"run_id", "faults", "category", ...}
"""
from __future__ import annotations

from pathlib import Path

import numpy as np

from .files import read_jsonl, read_run
from .judge import judge
from .metrics import predicted_category, summarize

# 휴대용 계측기 잡음 (표준편차). 실제 계측기 사양으로 바꿔야 한다.
PORTABLE_NOISE = {"temp": ("abs", 0.15), "vib": ("rel", 0.03), "cur": ("rel", 0.01)}
TRUTH_COLUMNS = {"temp": "temp", "vib": "vib", "cur": "current"}


def portable_reading(truth: dict, check: dict, rng) -> float:
    x = np.asarray(truth[TRUTH_COLUMNS[check["channel"]]], dtype=float)
    value = float(np.max(x[-60:])) if check.get("record") == "최댓값" else float(np.median(x[-30:]))
    kind, s = PORTABLE_NOISE[check["channel"]]
    return value + rng.normal(0, s if kind == "abs" else s * abs(value))


def evaluate_dir(model, data_dir, coupled: bool = False, field_check: bool = True, seed: int = 0):
    data_dir = Path(data_dir)
    labels = read_jsonl(data_dir / "labels.jsonl")
    rng = np.random.default_rng(seed)
    before, after, details = [], [], []
    n_checks = 0
    for lab in labels:
        run = read_run(data_dir / "runs" / f"{lab['run_id']}.csv")
        card = judge(run, model, coupled=coupled, event_id=lab["run_id"])
        final = card
        if field_check and card["check"] is not None:
            truth = read_run(data_dir / "truth" / f"{lab['run_id']}.csv")
            ck = {"channel": card["check"]["channel"], "record": card["check"]["record"],
                  "value": portable_reading(truth, card["check"], rng)}
            final = judge(run, model, coupled=coupled, checks=[ck], event_id=lab["run_id"])
            n_checks += 1
        b = {"truth": lab["category"], "faults": lab["faults"], "pred": predicted_category(card)}
        a = {"truth": lab["category"], "faults": lab["faults"], "pred": predicted_category(final)}
        before.append(b)
        after.append(a)
        details.append({"run_id": lab["run_id"], "faults": lab["faults"], "truth": lab["category"],
                        "pred": b["pred"], "pred_after_check": a["pred"],
                        "verdict": card["verdict"], "symptoms": [s["code"] for s in card["symptoms"]],
                        "check": card["check"]["channel"] if card["check"] else None})
    return summarize(before), summarize(after, n_checks), details
