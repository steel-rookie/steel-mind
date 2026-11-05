"""판정 카드 → 정비 가이드(LLM)에 넘기는 근거 카드.

판정 카드에서 가이드가 읽을 것만 남긴다 (기획서 4.2 ⑥, 4.3).
- 측정 상태와 설비 진단 상태를 그대로 넘긴다. 설비 원인은 "후보"로만 넘긴다.
- 의심 센서의 값은 빼고, 뺐다는 사실만 적는다.
- 현장 확인 지시는 예시값 표시와 함께 넘긴다.
- 내부 계산값(z, 시각 등)은 넘기지 않는다.

이 카드는 판정을 바꾸지 않는다. 판정 카드와 어긋나는 내용이 생기면 판정 카드가 맞다.
"""
from __future__ import annotations

from .judge import CHANNEL_SENSOR


def _group(H) -> str:
    return " + ".join(f"{c['name']}({c['kind']})" for c in H)


def evidence_card(card: dict) -> dict:
    suspect = set(card["suspect_sensors"])
    values = {}
    for ch, v in card["values"].items():
        if CHANNEL_SENSOR.get(ch) in suspect:
            values[ch] = {"excluded": True, "reason": f"{CHANNEL_SENSOR[ch]} 값은 믿을 수 없어 뺌", "unit": v["unit"]}
        else:
            values[ch] = {"installed": v["installed"], "expected": v["virtual"], "unit": v["unit"]}
    check = None
    if card["check"]:
        ck = card["check"]
        check = {k: ck[k] for k in ("tool", "where", "seconds", "record", "read_rule", "example_note") if k in ck}
        check["if_sensor_faulty"] = [" + ".join(g) for g in ck.get("if_sensor_faulty", [])]
        check["if_sensor_ok"] = [" + ".join(g) for g in ck.get("if_sensor_ok", [])]
    wait = None
    if card["wait_until_s"] is not None:
        wait = max(1, round((card["wait_until_s"] - card["decided_at_s"]) / 60))
    return {
        "engine_version": card["engine_version"],
        "event_id": card["event_id"],
        "equipment_id": card["equipment_id"],
        "measurement_status": card["measurement_status"],
        "equipment_status": card["equipment_status"],
        "equipment_basis": card["equipment_basis"],
        "handoff_to_guide": card["handoff_to_guide"],
        "handoff_note": card["handoff_note"],
        "observed": [s["label"] for s in card["symptoms"]],
        "confirmed_sensor_faults": [f["name"] for f in card["findings"] if f["status"] == "확정"],
        "candidates": [_group(H) for H in card["candidates"]],
        "suspect_sensors": card["suspect_sensors"],
        "next_steps": card["next_steps"],
        "check": check,
        "wait_minutes": wait,
        "values": values,
        "reasons": card["reasons"],
    }
