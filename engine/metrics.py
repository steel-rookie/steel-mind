"""평가 지표. 정답 라벨은 데이터·검증 담당이 가진다. 판별기 작성자는 블라인드 데이터에 이 코드를 돌리지 않는다.

지표마다 분모가 다르다. 분모가 0이면 비율을 내지 않고 "평가 불가"로 적는다 (기획서 H3).
모든 지표는 건수(k/n)와 비율을 함께 낸다.

| 지표 | 분자 | 분모 |
|---|---|---|
| 정답률 | 판정 범주가 맞은 회차 (보류는 정답 아님) | 전체 회차 |
| 위험 오분류 | 설비 이상이 있는데 "센서"로 판정 | 설비 이상이 든 회차 (설비 단독 + 동시) |
| 놓침 | "정상"으로 판정 | 고장 회차 |
| 오분류 | 그 밖에 틀린 판정 (센서 고장을 설비로 등) | 고장 회차 |
| 센서 고장을 설비로 봄 | "설비" 또는 "동시"로 판정 | 센서 고장만 든 회차 |
| 보류율 | 확인 대기·정보 부족 | 고장 회차 |
| 확정 정확도 | 맞은 판정 | 고장 회차 중 판정한 회차 |
| 정상 오경보 | 정상이 아니라고 판정 | 정상 회차 |
| 기대비용 | 위험 50, 놓침 10, 오분류 3, 보류 1, 현장 확인 1 (E3), 정상 오경보 3 | 전체 회차 |

보류 해소 (H2): 처음 보류된 회차만 따로 본다. 전체 정답률과 섞지 않는다.
  올바른 해소 = 확인 뒤 정답 범주 / 잘못된 해소 = 확인 뒤 틀린 판정 / 남은 보류 = 확인 뒤에도 보류
"""
from __future__ import annotations

COSTS = {"risky": 50.0, "miss": 10.0, "misclass": 3.0, "hold": 1.0, "check": 1.0, "false_alarm": 3.0}
DECIDED = ("센서", "설비", "동시")


def predicted_category(card: dict) -> str:
    if card["verdict"] == "정상":
        return "정상"
    if card["verdict"] == "확정":
        return card["verdict_detail"]
    return "보류"


def has_equipment(faults) -> bool:
    return any(f.startswith("E_") for f in faults)


def outcome(truth_cat: str, faults, pred_cat: str) -> str:
    if pred_cat == truth_cat:
        return "correct"
    if truth_cat == "정상":
        return "false_alarm"
    if pred_cat == "정상":
        return "miss"
    if pred_cat == "보류":
        return "hold"
    if has_equipment(faults) and pred_cat == "센서":
        return "risky"
    return "misclass"


def ratio(k: int, n: int) -> dict:
    return {"k": k, "n": n, "rate": (k / n) if n else None}


def summarize(rows, n_checks: int = 0) -> dict:
    """rows: [{"truth": 범주, "faults": [...], "pred": 범주}]"""
    fault = [r for r in rows if r["truth"] != "정상"]
    normal = [r for r in rows if r["truth"] == "정상"]
    equip = [r for r in fault if has_equipment(r["faults"])]
    sensor_only = [r for r in fault if not has_equipment(r["faults"])]
    out = [outcome(r["truth"], r["faults"], r["pred"]) for r in rows]
    fout = [outcome(r["truth"], r["faults"], r["pred"]) for r in fault]
    decided = [r for r in fault if r["pred"] in DECIDED]
    risky = sum(1 for r in equip if r["pred"] == "센서")
    cost = (COSTS["risky"] * risky + COSTS["miss"] * fout.count("miss")
            + COSTS["misclass"] * fout.count("misclass") + COSTS["hold"] * fout.count("hold")
            + COSTS["check"] * n_checks + COSTS["false_alarm"] * out.count("false_alarm"))
    return {
        "n": len(rows), "n_fault": len(fault), "n_normal": len(normal),
        "n_equip": len(equip), "n_sensor_only": len(sensor_only),
        "accuracy": ratio(out.count("correct"), len(rows)),
        "risky": ratio(risky, len(equip)),
        "miss": ratio(fout.count("miss"), len(fault)),
        "misclass": ratio(fout.count("misclass"), len(fault)),
        "sensor_as_equip": ratio(sum(1 for r in sensor_only if r["pred"] in ("설비", "동시")), len(sensor_only)),
        "hold": ratio(fout.count("hold"), len(fault)),
        "confirm_accuracy": ratio(sum(1 for r in decided if r["pred"] == r["truth"]), len(decided)),
        "false_alarm": ratio(sum(1 for r in normal if r["pred"] != "정상"), len(normal)),
        "expected_cost": (cost / len(rows)) if rows else None,
        "checks": n_checks,
    }


def hold_resolution(rows) -> dict:
    """rows: [{"truth", "faults", "pred", "pred_after"}]. 처음 보류된 회차만 센다."""
    held = [r for r in rows if r["pred"] == "보류"]
    correct = [r for r in held if r["pred_after"] == r["truth"]]
    remaining = [r for r in held if r["pred_after"] == "보류"]
    wrong = [r for r in held if r not in correct and r not in remaining]
    wrong_risky = [r for r in wrong if has_equipment(r["faults"]) and r["pred_after"] == "센서"]
    return {
        "held": len(held),
        "held_normal": sum(1 for r in held if r["truth"] == "정상"),
        "correct": ratio(len(correct), len(held)),
        "wrong": ratio(len(wrong), len(held)),
        "wrong_risky": len(wrong_risky),
        "remaining": ratio(len(remaining), len(held)),
        "check_time": None,  # 모의 데이터에는 확인 시간이 없다. 실측에서 잰다.
    }


LABELS = [("accuracy", "정답률"), ("risky", "위험 오분류 (설비 포함 회차)"), ("miss", "놓침 (고장 회차)"),
          ("misclass", "오분류 (고장 회차)"), ("sensor_as_equip", "센서 고장을 설비로 봄 (센서 단독 회차)"),
          ("hold", "보류율 (고장 회차)"), ("confirm_accuracy", "확정 정확도 (판정한 고장 회차)"),
          ("false_alarm", "정상 오경보 (정상 회차)"), ("expected_cost", "기대비용/회차")]


def cell(v) -> str:
    if v is None:
        return "평가 불가"
    if isinstance(v, dict):
        if not v["n"]:
            return "평가 불가 (분모 0)"
        return f"{v['k']}/{v['n']} ({100 * v['rate']:.1f}%)"
    return f"{v:.2f}"


def format_table(named: dict) -> str:
    """{"이름": summary} → 사람이 읽는 표"""
    names = list(named)
    lines = ["| 지표 | " + " | ".join(names) + " |", "|---|" + "---|" * len(names)]
    for key, label in LABELS:
        lines.append(f"| {label} | " + " | ".join(cell(named[nm].get(key)) for nm in names) + " |")
    first = named[names[0]]
    lines.append(f"\n회차 {first['n']} (정상 {first['n_normal']}, 고장 {first['n_fault']}: "
                 f"설비 포함 {first['n_equip']}, 센서 단독 {first['n_sensor_only']})")
    return "\n".join(lines)


def format_resolution(res: dict) -> str:
    if not res["held"]:
        return "보류 해소: 평가 불가 (처음 보류된 회차 0)"
    return "\n".join([
        f"보류 해소 (처음 보류 {res['held']}회, 그중 정상 회차 {res['held_normal']}회)",
        f"- 올바른 해소: {cell(res['correct'])}",
        f"- 잘못된 해소: {cell(res['wrong'])}, 그중 위험 오분류 {res['wrong_risky']}건",
        f"- 남은 보류: {cell(res['remaining'])}",
        "- 확인 시간: " + ("평가 불가 (모의 데이터)" if res["check_time"] is None else f"{res['check_time']}"),
    ])
