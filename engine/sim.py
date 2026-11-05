"""내부 모의 데이터 생성기 (개발용).

판별기 작성자가 만든 생성기라 징후표와 같은 가정을 공유한다. 이 데이터의 성능은 "내부 모의(개발용)"로만 보고한다.
블라인드 평가 데이터는 판별기 작성자가 아닌 팀원(데이터·검증 담당)이 따로 만든다.

1초 간격. 엔진이 보는 열: t, setpoint(%), torque(% 정격, 인버터 추정값), current(A), vib(mm/s), temp(°C)
정답(truth) 열: 실제 물리값. 휴대용 계측기가 재는 값이다.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .signatures import EQUIP, SENSOR, fault_table
from .util import lowpass

FAULT_CODES = ("E_BRG", "E_LUB", "E_LOAD", "S_CUR", "S_VIB", "S_VSPK", "S_TDR", "S_TST")
PAIRS = (("E_BRG", "S_TST"), ("E_LOAD", "S_CUR"), ("E_LUB", "S_CUR"))


@dataclass
class Equipment:
    L: float
    c0: float
    cg: float
    v0: float
    v1: float
    v2: float
    k_f: float
    k_s: float
    tau_f: float
    tau_s: float
    amb0: float
    drift: float  # 회차 전체에 걸친 주변 온도 변화(°C)


def make_equipment(rng) -> Equipment:
    return Equipment(L=rng.uniform(0.97, 1.03), c0=rng.uniform(45, 55), cg=rng.uniform(3.9, 4.1),
                     v0=rng.uniform(0.95, 1.05), v1=rng.uniform(0.019, 0.021), v2=rng.uniform(0.0095, 0.0105),
                     k_f=rng.uniform(7, 9), k_s=rng.uniform(23, 27), tau_f=rng.uniform(35, 45),
                     tau_s=rng.uniform(270, 330), amb0=rng.uniform(25, 31), drift=rng.uniform(-0.15, 0.15))


def setpoint_profile(rng, T: int, constant: float | None = None) -> np.ndarray:
    if constant is not None:
        return np.full(T, float(constant))
    sp = np.empty(T)
    cur = rng.uniform(50, 75)
    t = 0
    while t < T:
        hold = int(rng.uniform(60, 150))
        nxt = float(np.clip(rng.uniform(40, 90), 40, 90))
        ramp = np.linspace(cur, nxt, 6)[1:]
        end = min(t + len(ramp), T)
        sp[t:end] = ramp[: end - t]
        t = end
        end = min(t + hold, T)
        sp[t:end] = nxt
        t = end
        cur = nxt
    return sp


def _ramp(t, onset, tau):
    x = np.clip(t - onset, 0, None)
    return np.where(t >= onset, 1 - np.exp(-x / tau), 0.0)


def default_magnitudes(rng) -> dict:
    sgn = rng.choice([-1, 1])
    return {
        "brg_vib": rng.uniform(0.5, 1.5), "brg_heat": rng.uniform(0.02, 0.06), "brg_impulse": rng.uniform(0, 0.03),
        "lub_heat": rng.uniform(0.04, 0.12), "lub_tq": rng.uniform(0.0, 3.0),
        "load": rng.uniform(0.04, 0.12),
        "cur_bias": sgn * rng.uniform(15, 40),
        "vib_bias": rng.uniform(0.4, 1.2),
        "spk_rate": rng.uniform(0.03, 0.08), "spk_size": rng.uniform(1.5, 3.0),
        "tdr_rate": rng.choice([-1, 1]) * rng.uniform(0.004, 0.01),
    }


def simulate_run(rng, faults=(), eq: Equipment | None = None, T: int | None = None, onset: float | None = None,
                 coupling: float = 0.0, mag: dict | None = None, setpoint: float | None = None):
    """한 회차를 만든다. 돌려주는 값: (엔진 입력 dict, 정답 dict, 라벨 dict)"""
    faults = tuple(faults)
    eq = eq or make_equipment(rng)
    if onset is None:
        onset = float(rng.uniform(200, 300)) if faults else None
    if T is None:
        T = int(onset + 240) if faults else int(rng.uniform(480, 540))
    m = default_magnitudes(rng)
    if mag:
        m.update(mag)
    t = np.arange(T, dtype=float)
    on = (t >= onset) if faults else np.zeros(T, dtype=bool)
    sp = setpoint_profile(rng, T, setpoint)

    tq_ideal = lowpass(eq.L * (5 + 0.8 * sp + 0.003 * sp ** 2), 2.0)
    tq_true = tq_ideal.copy()
    heat = np.zeros(T)
    vib_extra = np.zeros(T)
    if "E_LOAD" in faults:
        tq_true = tq_true * (1 + m["load"] * _ramp(t, onset, 10))
    if "E_LUB" in faults:
        tq_true = tq_true + m["lub_tq"] * _ramp(t, onset, 60)
        heat += m["lub_heat"] * _ramp(t, onset, 60)
    if "E_BRG" in faults:
        vib_extra += m["brg_vib"] * _ramp(t, onset, 40)
        heat += m["brg_heat"] * _ramp(t, onset, 60)
        hits = on & (rng.random(T) < m["brg_impulse"])
        vib_extra += hits * rng.uniform(0.5, 1.5, T)

    cur_true = eq.c0 + eq.cg * tq_true
    vib_true = eq.v0 + eq.v1 * sp + eq.v2 * tq_true + vib_extra
    u = (tq_true / 100.0) ** 2 + heat
    amb = eq.amb0 + eq.drift * t / max(T - 1, 1)
    temp_true = amb + eq.k_f * lowpass(u, eq.tau_f) + eq.k_s * lowpass(u, eq.tau_s)

    cur = cur_true + rng.normal(0, 3.0, T)
    if "S_CUR" in faults:
        cur = cur + m["cur_bias"] * on
    tq_est = tq_true + rng.normal(0, 0.8, T)
    if coupling > 0:
        tq_est = (1 - coupling) * tq_est + coupling * (cur - eq.c0) / eq.cg
    vib = vib_true + rng.normal(0, 0.08, T)
    if "S_VIB" in faults:
        vib = vib + m["vib_bias"] * _ramp(t, onset, 5)
    if "S_VSPK" in faults:
        spikes = on & (rng.random(T) < m["spk_rate"])
        vib = vib + spikes * m["spk_size"] * rng.uniform(0.8, 1.2, T)
    temp = temp_true + rng.normal(0, 0.15, T)
    if "S_TDR" in faults:
        temp = temp + m["tdr_rate"] * np.clip(t - onset, 0, None)
    if "S_TST" in faults:
        k = int(onset)
        temp[k:] = round(float(temp[k]), 1)

    run = {"t": t, "setpoint": sp, "torque": tq_est, "current": cur, "vib": vib, "temp": temp}
    truth = {"t": t, "torque": tq_true, "current": cur_true, "vib": vib_true, "temp": temp_true}
    table = fault_table(False)
    kinds = {table[f].kind for f in faults}
    category = "정상" if not faults else ("동시" if kinds == {EQUIP, SENSOR} else next(iter(kinds)))
    label = {"faults": list(faults), "onset_s": onset, "coupling": coupling, "category": category}
    return run, truth, label


def scenario_mix(rng, n: int):
    """정상 20%, 단일 고장 60%, 동시 이상 20%"""
    out = []
    for _ in range(n):
        r = rng.random()
        if r < 0.2:
            out.append(())
        elif r < 0.8:
            out.append((FAULT_CODES[rng.integers(len(FAULT_CODES))],))
        else:
            out.append(PAIRS[rng.integers(len(PAIRS))])
    return out
