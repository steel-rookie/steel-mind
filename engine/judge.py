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
    spec = dict(CHECK_TOOLS[ch])
    if ch == "vib" and "vibspk" in obs:
        spec.update(seconds=60, record="최댓값")  # 간헐 튐은 한 순간 측정으로 못 잡는다 (E19)
    spec["channel"] = ch
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
               "tolerance": round(tol, 3)}
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


def judge(run, model: NormalModel, coupled: bool = False, checks=None,
          event_id: str | None = None, equipment_id: str | None = None,
          cfg: Config = CONFIG) -> dict:
    """한 회차를 판정해 판정 카드(JSON으로 바꿀 수 있는 dict)를 돌려준다.

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
        "verdict": "정상",
        "verdict_detail": None,
        "evidence_status": None,
        "handoff_to_guide": False,
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
    card["findings"] = ([{"code": c, "name": table[c].name, "kind": table[c].kind,
                          "target": table[c].target, "status": "확정"} for c in sorted(certain)]
                        + [{"code": c, "name": table[c].name, "kind": table[c].kind,
                            "target": table[c].target, "status": "후보"} for c in sorted(possible)])
    used = {ch for ch, _ in constraints}

    coupled_needs_clamp = (coupled and "cur" not in used and cls != "없음"
                           and any("S_CUR" in H for H in cands))
    if coupled_needs_clamp:
        # 결합 모드: 토크 추정값이 측정 전류에서 계산되므로 설치 센서만으로는 전류센서 이상과 실제 부하 변화를 가를 수 없다.
        # 전류센서가 후보에 있으면 엔진 혼자 확정하지 않고 클램프미터를 요구한다 (H4).
        card.update(verdict="정보 부족", evidence_status="정보 부족")
        card["reasons"].append("결합 모드: 토크 추정값이 측정 전류에서 계산돼 전류센서 이상과 실제 부하 변화를 "
                               "설치 센서만으로 가를 수 없음. 클램프미터로 실제 전류를 잼")
        spec = dict(CHECK_TOOLS["cur"])
        spec.update(channel="cur",
                    if_sensor_faulty=[["전류센서 편향 (실제 전류로 부하 증가 여부를 함께 판단)"]],
                    if_sensor_ok=[_names(H, table) for H in cands if "S_CUR" not in H] or [["다시 판정"]])
        card["check"] = spec
    elif cls == "없음":
        card["verdict"] = "정보 부족"
        card["evidence_status"] = "정보 부족"
        card["reasons"].append("알려진 고장 유형(단일·이중)으로 설명되지 않음" if not results
                               else "현장 확인 결과와 맞는 고장 유형이 없음")
        card["check"] = None
    elif cls == "설비":
        card.update(verdict="확정", verdict_detail="설비", evidence_status="측정 확인됨", handoff_to_guide=True)
        card["reasons"].append("후보가 모두 설비 쪽이라 측정은 믿을 수 있음")
    elif cls == "동시":
        sensors = sorted({c for H in cands for c in H if table[c].kind == SENSOR})
        card.update(verdict="확정", verdict_detail="동시", evidence_status="측정 확인됨", handoff_to_guide=True)
        card["suspect_sensors"] = [table[c].target for c in sensors]
        card["reasons"].append("설비 이상과 센서 이상이 함께 있음. 정비 가이드에는 의심 센서 값을 빼고 넘김")
    elif cls == "센서":
        if "S_CUR" in certain and "cur" not in used and not coupled and _e25_hold(feats, cfg):
            card.update(verdict="정보 부족", evidence_status="정보 부족")
            card["reasons"].append("전류센서 편향으로 확정하기 전, 토크 추정값이 최근 30초 동안 3σ 이상 올라 있음. "
                                   "실제 부하 증가가 가려졌을 수 있음 (E25)")
            spec = dict(CHECK_TOOLS["cur"])
            spec.update(channel="cur", if_sensor_faulty=[["전류센서 편향 (+ 부하 증가 여부를 함께 판단)"]],
                        if_sensor_ok=[["전류센서 정상 → 다시 판정"]])
            card["check"] = spec
        else:
            card.update(verdict="확정", verdict_detail="센서", evidence_status="측정 의심")
            card["suspect_sensors"] = sorted({table[c].target for c in certain})
            card["reasons"].append("후보가 모두 센서 쪽. 설비 정비·정지를 권하지 않고 계측 점검으로 보냄")
    else:  # 혼합
        names = " / ".join(" + ".join(_names(H, table)) for H in cands)
        card["reasons"].append(f"지금 징후는 여러 원인으로 설명됨: {names}")
        w = None if results else _wait(cands, obs, table, feats, model, cfg, constraints)
        if w is not None:
            card.update(verdict="확인 대기", evidence_status="확인 대기", wait_until_s=w["until"])
            card["reasons"].append(f"'{SYMPTOM_LABELS[w['symptom']]}'이 나타나면 갈림. "
                                   f"약 {int(w['until'] - t_end)}초 더 지켜봄 (온도 지연의 {cfg.wait_factor:g}배까지)")
        else:
            card.update(verdict="정보 부족", evidence_status="정보 부족")
            card["reasons"].append("지금 있는 측정으로는 끝까지 가를 수 없음. 현장에서 한 번 재면 갈림")
        card["check"] = choose_check(cands, table, used, obs)
        if card["check"] is None and card["verdict"] == "정보 부족":
            card["reasons"].append("가를 수 있는 휴대용 계측기가 없음. 현업 판단 필요")

    shown = list(symptom_channels)
    if card["check"] and card["check"]["channel"] not in shown:
        shown.append(card["check"]["channel"])
    card["values"] = _values(feats, [c for c in CHANNELS if c in shown])
    return card
