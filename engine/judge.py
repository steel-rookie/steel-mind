"""판정: 징후 → 후보 → 확정 / 확인 대기 / 정보 부족 → 판정 카드.

후보는 단일 고장으로 설명되면 단일 후보만, 아니면 이중 고장 후보를 남긴다.
(두 고장이 동시에 생기는 경우보다 하나가 생기는 경우가 흔하다고 본다. 동시 이상 사례는 단일로 설명이 안 될 때 잡힌다.)
"""
from __future__ import annotations

from itertools import combinations

import numpy as np

from . import __version__
from .config import CHANNELS, CHECK_TOOLS, CONFIG, TOLERANCE, UNITS, Config
from .detect import Features, detect
from .model import NormalModel
from .signatures import EQUIP, SENSOR, SYMPTOM_LABELS, fault_table


# ---------- 후보 ----------
def _explains(faults, obs) -> bool:
    allowed = set()
    for f in faults:
        allowed |= f.allowed
    if not set(obs) <= allowed:
        return False
    return all(g & obs for f in faults for g in f.requires)


def _satisfies(H, table, constraints) -> bool:
    for channel, faulty in constraints:
        has = any(table[c].channel == channel for c in H)
        if has != faulty:
            return False
    return True


def explain(obs, table, constraints=()) -> list:
    obs = frozenset(obs)
    faults = list(table.values())
    singles = [frozenset({f.code}) for f in faults
               if _explains([f], obs) and _satisfies({f.code}, table, constraints)]
    if singles:
        return singles
    return [frozenset({a.code, b.code}) for a, b in combinations(faults, 2)
            if _explains([a, b], obs) and _satisfies({a.code, b.code}, table, constraints)]


def classify(cands, table) -> str:
    """설비 / 센서 / 동시 / 혼합 / 없음"""
    if not cands:
        return "없음"
    kinds = [frozenset(table[c].kind for c in H) for H in cands]
    if all(k == {EQUIP} for k in kinds):
        return "설비"
    if all(k == {SENSOR} for k in kinds):
        return "센서"
    if all(k == {EQUIP, SENSOR} for k in kinds):
        return "동시"
    return "혼합"


# ---------- 현장 확인 ----------
def _installed(feats: Features, channel: str, record: str) -> float:
    x = feats.measured[channel]
    if record == "최댓값":
        return float(np.max(x[-60:]))
    return float(np.median(x[-30:]))


def _tolerance(channel: str, value: float, record: str) -> float:
    abs_tol, rel_tol = TOLERANCE.get((channel, record), TOLERANCE[(channel, "평균")])
    return max(abs_tol, rel_tol * abs(value))


EXAMPLE_NOTE = ("측정 시간·기록 방식·허용오차는 예시값이다. 현업 절차와 실제 계측기 사양을 확인하기 전까지 "
                "현장 기준으로 쓰지 않는다.")


def _tol_text(channel: str, record: str) -> str:
    abs_tol, rel_tol = TOLERANCE.get((channel, record), TOLERANCE[(channel, "평균")])
    unit = UNITS[channel]
    if rel_tol:
        return f"허용오차(±{abs_tol:g} {unit} 또는 ±{100 * rel_tol:g}% 중 큰 값)"
    return f"허용오차(±{abs_tol:g} {unit})"


def _check_spec(channel: str, obs=()) -> dict:
    """현장 확인 지시 한 장. 시간·기록 방식·허용오차는 예시값으로 표시한다 (기획서 4.2 ③)."""
    spec = dict(CHECK_TOOLS[channel])
    if channel == "vib" and "vibspk" in obs:
        spec.update(seconds=60, record="최댓값")  # 간헐 튐은 한 순간 측정으로 못 잡는다 (E19)
    spec["channel"] = channel
    tol = _tol_text(channel, spec["record"])
    if spec["record"] == "최댓값":
        spec["read_rule"] = (f"{spec['seconds']}초 동안 최댓값을 적는다. 설치 센서의 같은 구간 최댓값과의 차이가 "
                             f"{tol} 이내면 센서 정상, 넘으면 센서 이상")
    else:
        spec["read_rule"] = (f"{spec['seconds']}초 평균을 적는다. 설치 센서 값과의 차이가 {tol} 이내면 센서 정상. "
                             "넘으면 설치 센서 값과 가상센서 값 중 휴대용 값에 가까운 쪽으로 본다")
    spec["example_values"] = ["seconds", "record", "read_rule"]
    spec["example_note"] = EXAMPLE_NOTE
    return spec


def choose_check(cands, table, used=(), obs=()) -> dict | None:
    """후보를 가장 고르게 나누는 계측기 하나를 고른다 (E12: 후보가 2개면 고정 규칙과 같다)."""
    best = None
    for ch in ("vib", "temp", "cur"):
        if ch in used:
            continue
        a = [H for H in cands if any(table[c].channel == ch for c in H)]
        b = [H for H in cands if H not in a]
        score = min(len(a), len(b))
        if score > 0 and (best is None or score > best[0]):
            best = (score, ch, a, b)
    if best is None:
        return None
    _, ch, a, b = best
    spec = _check_spec(ch, obs)
    spec["if_sensor_faulty"] = [_names(H, table) for H in a]
    spec["if_sensor_ok"] = [_names(H, table) for H in b]
    return spec


def _apply_checks(checks, feats: Features, model: NormalModel, run, table):
    constraints, extra, results = [], set(), []
    sp_now = float(np.median(np.asarray(run["setpoint"], dtype=float)[-30:]))
    for ck in checks:
        ch = ck["channel"]
        record = ck.get("record", "평균")
        value = float(ck["value"])
        installed = _installed(feats, ch, record)
        tol = _tolerance(ch, installed, record)
        d_inst = abs(value - installed)
        res = {"channel": ch, "record": record, "portable": value, "installed": round(installed, 3),
               "tolerance": round(tol, 3), "tolerance_is_example": True}
        if record == "최댓값":
            sensor_ok = d_inst <= tol
        else:
            virtual = float(np.median(feats.expected[ch][-30:]))
            sensor_ok = d_inst <= tol or d_inst <= abs(value - virtual)
            res["virtual"] = round(virtual, 3)
        res["sensor_ok"] = bool(sensor_ok)
        if ch == "cur" and not sensor_ok:
            # 클램프미터 값이 실제 전류다. 설정값만으로 본 정상 전류보다 크면 실제 부하도 늘었다.
            normal_cur = model.current_from_setpoint(sp_now, feats.offsets["tq"], feats.offsets["cur"])
            if value - normal_cur > _tolerance("cur", normal_cur, "평균"):
                extra.add("load+")
                res["note"] = "실제 전류가 설정값 대비 높아 부하 증가도 함께 있다고 봄"
        constraints.append((ch, not sensor_ok))
        results.append(res)
    return constraints, extra, results


# ---------- 확인 대기 ----------
def _wait(cands, obs, table, feats: Features, model: NormalModel, cfg: Config, constraints):
    window = min(cfg.wait_factor * model.lag_T, cfg.wait_cap)
    elapsed = float(feats.t[-1] - feats.onset)
    if elapsed >= window:
        return None
    late = set()
    for H in cands:
        for c in H:
            late |= table[c].late
    late -= set(obs)
    if elapsed >= model.lag_T:  # v0.5 추세 검정
        zt = feats.z["temp"][feats.t >= feats.onset]
        if "temp+" in late and (zt.max() if len(zt) else 0.0) < cfg.trend_sigma:
            late.discard("temp+")
        if "temp-" in late and (zt.min() if len(zt) else 0.0) > -cfg.trend_sigma:
            late.discard("temp-")
    for s in sorted(late):
        c2 = explain(set(obs) | {s}, table, constraints)
        if c2 and classify(c2, table) not in ("혼합", "없음"):
            return {"symptom": s, "until": float(feats.onset + window), "elapsed": elapsed}
    return None


def _e25_hold(feats: Features, cfg: Config) -> bool:
    recent = feats.z["tq"][-cfg.e25_window:]
    return bool(np.median(recent) >= cfg.e25_sigma)


# ---------- 카드 ----------
CHANNEL_SENSOR = {"vib": "진동센서", "temp": "온도센서", "cur": "전류센서"}


def _channel_of(code: str) -> str:
    return {"vibspk": "vib", "tstuck": "temp", "load+": "cur"}.get(code, code[:-1])


def _names(H, table):
    return [table[c].name for c in sorted(H)]


def _values(feats: Features, channels) -> dict:
    out = {}
    for c in channels:
        out[c] = {"installed": round(float(np.median(feats.measured[c][-30:])), 3),
                  "virtual": round(float(np.median(feats.expected[c][-30:])), 3),
                  "unit": UNITS[c]}
    return out


def _symptom_list(feats: Features):
    out = []
    for code, t0 in sorted(feats.symptoms.items(), key=lambda kv: kv[1]):
        ch = _channel_of(code)
        z = float(feats.z[ch][-1]) if ch in feats.z else None
        out.append({"code": code, "label": SYMPTOM_LABELS[code], "onset_s": t0,
                    "z_now": None if z is None else round(z, 2)})
    return out


def _finding(f, status: str) -> dict:
    return {"code": f.code, "name": f.name, "kind": f.kind, "target": f.target, "status": status}


def _set(card, measurement, equipment, steps, verdict, detail=None, handoff=False, basis=None, note=None):
    card.update(measurement_status=measurement, equipment_status=equipment, next_steps=list(steps),
                verdict=verdict, verdict_detail=detail, handoff_to_guide=handoff,
                equipment_basis=basis, handoff_note=note)


def judge(run, model: NormalModel, coupled: bool = False, checks=None,
          event_id: str | None = None, equipment_id: str | None = None,
          cfg: Config = CONFIG) -> dict:
    """한 회차를 판정해 판정 카드(JSON으로 바꿀 수 있는 dict)를 돌려준다.

    카드는 두 상태를 따로 적는다 (기획서 4.3, 센서 정상 ≠ 설비 고장 확정).
    - measurement_status: 센서 값을 믿어도 되는가. 확인됨 / 의심 / 일부 의심 / 확인 대기 / 정보 부족
    - equipment_status:   설비 쪽 이상이 있는가. 후보 있음 / 징후 없음 / 판단 보류. 설비 원인은 "확정"이라 쓰지 않는다.
    verdict·verdict_detail은 채점용 범주다. 화면과 정비 가이드는 두 상태를 쓴다.

    checks: 현장 확인 결과 목록. 예: [{"channel": "vib", "value": 3.1, "record": "평균"}]
    """
    table = fault_table(coupled)
    feats = detect(run, model, cfg)
    t_end = float(feats.t[-1])
    card = {
        "engine_version": __version__,
        "event_id": event_id,
        "equipment_id": equipment_id,
        "coupled_torque": bool(coupled),
        "decided_at_s": t_end,
        "onset_s": feats.onset,
        "symptoms": _symptom_list(feats),
        "measurement_status": "확인됨",
        "equipment_status": "징후 없음",
        "equipment_basis": None,
        "next_steps": [],
        "handoff_to_guide": False,
        "handoff_note": None,
        "verdict": "정상",
        "verdict_detail": None,
        "candidates": [],
        "findings": [],
        "suspect_sensors": [],
        "check": None,
        "wait_until_s": None,
        "checks_applied": [],
        "values": {},
        "reasons": [],
    }
    if not feats.symptoms:
        card["reasons"].append("모든 채널이 기대값 범위 안에 있음")
        return card

    obs = set(feats.symptoms)
    constraints, results = [], []
    if checks:
        constraints, extra, results = _apply_checks(checks, feats, model, run, table)
        if ("cur", True) in constraints and "load+" not in extra and not coupled and _e25_hold(feats, cfg):
            # 클램프미터로 전류센서 이상이 확인됐고, 전류와 독립인 토크 추정값이 3σ 넘게 올라 있다.
            # 작은 부하 증가가 함께 있다고 보고 설비 쪽도 남긴다 (위험 오분류를 피하는 쪽).
            extra.add("load+")
            for r in results:
                if r["channel"] == "cur":
                    r["note"] = "토크 추정값이 문턱 아래로 올라 있어 부하 증가도 함께 있다고 봄 (E25)"
        obs |= extra
    card["checks_applied"] = results
    excluded = [CHANNEL_SENSOR[r["channel"]] for r in results if r["sensor_ok"]]
    cands = explain(obs, table, constraints)
    cls = classify(cands, table)

    symptom_channels = []
    for code in feats.symptoms:
        ch = _channel_of(code)
        if ch not in symptom_channels:
            symptom_channels.append(ch)

    card["candidates"] = [[{"code": c, "name": table[c].name, "kind": table[c].kind} for c in sorted(H)]
                          for H in cands]
    certain = frozenset.intersection(*cands) if cands else frozenset()
    possible = frozenset().union(*cands) - certain if cands else frozenset()
    # 센서 이상은 현장 계측이나 물리적으로 불가능한 신호(고착)로 확정할 수 있다.
    # 설비 원인은 정비 점검으로만 확정되므로 엔진은 언제나 "후보"로 적는다.
    sure_sensors = {c for c in certain if table[c].kind == SENSOR}
    used = {ch for ch, _ in constraints}
    hold_current = False

    coupled_needs_clamp = (coupled and "cur" not in used and cls != "없음"
                           and any("S_CUR" in H for H in cands))
    if coupled_needs_clamp:
        # 결합 모드: 토크 추정값이 측정 전류에서 계산되므로 설치 센서만으로는 전류센서 이상과 실제 부하 변화를 가를 수 없다.
        # 전류센서가 후보에 있으면 엔진 혼자 확정하지 않고 클램프미터를 요구한다 (H4).
        hold_current = True
        _set(card, "정보 부족", "판단 보류", ["현장 확인"], "정보 부족")
        card["reasons"].append("결합 모드: 토크 추정값이 측정 전류에서 계산돼 전류센서 이상과 실제 부하 변화를 "
                               "설치 센서만으로 가를 수 없음. 클램프미터로 실제 전류를 잼")
        spec = _check_spec("cur")
        spec.update(if_sensor_faulty=[["전류센서 편향 (실제 전류로 부하 증가 여부를 함께 판단)"]],
                    if_sensor_ok=[_names(H, table) for H in cands if "S_CUR" not in H] or [["다시 판정"]])
        card["check"] = spec
    elif cls == "없음":
        _set(card, "정보 부족", "판단 보류", ["현업 판단"], "정보 부족")
        card["reasons"].append("알려진 고장 유형(단일·이중)으로 설명되지 않음" if not results
                               else "현장 확인 결과와 맞는 고장 유형이 없음")
    elif cls == "설비":
        basis = "센서 배제로 남은 후보" if excluded else "설비 쪽 징후"
        note = ("원인은 후보다. 현장 확인으로 " + ", ".join(excluded) + " 이상을 배제했을 뿐 설비 원인은 확정하지 않았다"
                if excluded else "원인은 후보다. 정비 점검으로 확정한다")
        _set(card, "확인됨", "후보 있음", ["정비 가이드로 넘김"], "확정", "설비", True, basis, note)
        card["reasons"].append("후보가 모두 설비 쪽이라 측정은 믿을 수 있음" if not excluded
                               else "현장 확인으로 " + ", ".join(excluded) + " 정상. 남은 설명은 설비 쪽뿐")
        card["reasons"].append("설비 원인은 후보로만 넘김. 확정은 정비 점검에서 함")
    elif cls == "동시":
        sensors = sorted({c for H in cands for c in H if table[c].kind == SENSOR})
        suspect = sorted({table[c].target for c in sensors})
        basis = "센서 배제로 남은 후보" if excluded else "설비 쪽 징후"
        _set(card, "일부 의심", "후보 있음", ["정비 가이드로 넘김", "계측 점검"], "확정", "동시", True, basis,
             "의심 센서(" + ", ".join(suspect) + ") 값은 빼고 넘긴다. 설비 원인은 후보다")
        card["suspect_sensors"] = suspect
        card["reasons"].append("설비 이상과 센서 이상이 함께 있음. 정비 가이드에는 의심 센서 값을 빼고 넘김")
    elif cls == "센서":
        if "S_CUR" in certain and "cur" not in used and not coupled and _e25_hold(feats, cfg):
            hold_current = True
            _set(card, "정보 부족", "판단 보류", ["현장 확인"], "정보 부족")
            card["reasons"].append("전류센서 편향으로 확정하기 전, 토크 추정값이 최근 30초 동안 3σ 이상 올라 있음. "
                                   "실제 부하 증가가 가려졌을 수 있음 (E25)")
            spec = _check_spec("cur")
            spec.update(if_sensor_faulty=[["전류센서 편향 (+ 부하 증가 여부를 함께 판단)"]],
                        if_sensor_ok=[["전류센서 정상 → 다시 판정"]])
            card["check"] = spec
        else:
            _set(card, "의심", "징후 없음", ["계측 점검"], "확정", "센서")
            card["reasons"].append("후보가 모두 센서 쪽. 설비 정비·정지를 권하지 않고 계측 점검으로 보냄")
    else:  # 혼합
        names = " / ".join(" + ".join(_names(H, table)) for H in cands)
        card["reasons"].append(f"지금 징후는 여러 원인으로 설명됨: {names}")
        w = None if results else _wait(cands, obs, table, feats, model, cfg, constraints)
        card["check"] = choose_check(cands, table, used, obs)
        if w is not None:
            steps = ["재판정 대기"] + (["현장 확인"] if card["check"] else [])
            _set(card, "확인 대기", "판단 보류", steps, "확인 대기")
            card["wait_until_s"] = w["until"]
            card["reasons"].append(f"'{SYMPTOM_LABELS[w['symptom']]}'이 나타나면 갈림. "
                                   f"약 {int(w['until'] - t_end)}초 더 지켜봄 (온도 지연의 {cfg.wait_factor:g}배까지)")
        else:
            _set(card, "정보 부족", "판단 보류", ["현장 확인"] if card["check"] else ["현업 판단"], "정보 부족")
            card["reasons"].append("지금 있는 측정으로는 끝까지 가를 수 없음. 현장에서 한 번 재면 갈림")
        if card["check"] is None and card["verdict"] == "정보 부족":
            card["reasons"].append("가를 수 있는 휴대용 계측기가 없음. 현업 판단 필요")

    if hold_current:
        sure_sensors.discard("S_CUR")  # 부하 증가가 가려졌을 수 있어 전류센서도 아직 확정하지 않는다
    card["findings"] = ([_finding(table[c], "확정") for c in sorted(sure_sensors)]
                        + [_finding(table[c], "후보") for c in sorted((certain | possible) - sure_sensors)])
    if not card["suspect_sensors"]:
        card["suspect_sensors"] = sorted({table[c].target for c in sure_sensors})

    shown = list(symptom_channels)
    if card["check"] and card["check"]["channel"] not in shown:
        shown.append(card["check"]["channel"])
    card["values"] = _values(feats, [c for c in CHANNELS if c in shown])
    return card
