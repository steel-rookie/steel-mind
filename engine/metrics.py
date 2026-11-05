"""평가 지표. 정답 라벨은 데이터·검증 담당이 가진다. 판별기 작성자는 블라인드 데이터에 이 코드를 돌리지 않는다.

지표 정의 (기획서 5장, results.html)
- 정답률        : 전체 회차 중 판정 범주가 맞은 비율 (보류는 정답 아님)
- 위험 오분류   : 설비 이상이 있는 회차를 "센서"로 확정한 비율 (고장 회차 기준)
- 놓침          : 고장 회차를 "정상"으로 본 비율
- 오분류        : 그 밖에 틀린 확정 (센서 고장을 설비로 확정 등)
- 보류율        : 고장 회차 중 확인 대기·정보 부족 비율
- 확정 정확도   : 고장 회차에서 확정한 판정 중 맞은 비율
- 정상 오경보   : 정상 회차에서 알람을 낸 비율
- 기대비용      : 위험 50, 놓침 10, 오분류 3, 보류 1, 현장 확인 1 (E3)
"""
from __future__ import annotations

COSTS = {"risky": 50.0, "miss": 10.0, "misclass": 3.0, "hold": 1.0, "check": 1.0}


def predicted_category(card: dict) -> str:
    if card["verdict"] == "정상":
        return "정상"
    if card["verdict"] == "확정":
        return card["verdict_detail"]
    return "보류"


def outcome(truth_cat: str, faults, pred_cat: str) -> str:
    has_equip = any(f.startswith("E_") for f in faults)
    if pred_cat == truth_cat:
        return "correct"
    if truth_cat == "정상":
        return "false_alarm"
    if pred_cat == "정상":
        return "miss"
    if pred_cat == "보류":
        return "hold"
    if has_equip and pred_cat == "센서":
        return "risky"
    return "misclass"


def summarize(rows, n_checks: int = 0) -> dict:
    """rows: [{"truth": 범주, "faults": [...], "pred": 범주}]"""
    n = len(rows)
    fault_rows = [r for r in rows if r["truth"] != "정상"]
    normal_rows = [r for r in rows if r["truth"] == "정상"]
    outs = [outcome(r["truth"], r["faults"], r["pred"]) for r in rows]
    fouts = [outcome(r["truth"], r["faults"], r["pred"]) for r in fault_rows]
    nf = max(len(fault_rows), 1)
    confirmed = [o for o, r in zip(fouts, fault_rows) if r["pred"] in ("센서", "설비", "동시")]
    cost = (COSTS["risky"] * fouts.count("risky") + COSTS["miss"] * fouts.count("miss")
            + COSTS["misclass"] * fouts.count("misclass") + COSTS["hold"] * fouts.count("hold")
            + COSTS["check"] * n_checks + COSTS["misclass"] * outs.count("false_alarm"))
    return {
        "n": n, "n_fault": len(fault_rows), "n_normal": len(normal_rows),
        "accuracy": outs.count("correct") / max(n, 1),
        "risky": fouts.count("risky") / nf,
        "miss": fouts.count("miss") / nf,
        "misclass": fouts.count("misclass") / nf,
        "hold": fouts.count("hold") / nf,
        "confirm_accuracy": (confirmed.count("correct") / len(confirmed)) if confirmed else None,
        "false_alarm": (sum(1 for r in normal_rows if r["pred"] != "정상") / len(normal_rows)) if normal_rows else None,
        "expected_cost": cost / max(n, 1),
        "checks": n_checks,
    }


LABELS = [("accuracy", "정답률"), ("risky", "위험 오분류"), ("miss", "놓침"), ("misclass", "오분류"),
          ("hold", "보류율"), ("confirm_accuracy", "확정 정확도"), ("false_alarm", "정상 오경보"),
          ("expected_cost", "기대비용/회차")]


def format_table(named: dict) -> str:
    """{"이름": summary} → 사람이 읽는 표"""
    names = list(named)
    lines = ["| 지표 | " + " | ".join(names) + " |", "|---|" + "---|" * len(names)]
    for key, label in LABELS:
        cells = []
        for nm in names:
            v = named[nm].get(key)
            if v is None:
                cells.append("—")
            elif key == "expected_cost":
                cells.append(f"{v:.2f}")
            else:
                cells.append(f"{100 * v:.1f}%")
        lines.append(f"| {label} | " + " | ".join(cells) + " |")
    first = named[names[0]]
    lines.append(f"\n회차 {first['n']} (고장 {first['n_fault']}, 정상 {first['n_normal']})")
    return "\n".join(lines)
