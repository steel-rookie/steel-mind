"""정상 거동 모델 (가상센서).

정상 운전 회차에서 아래 관계를 배운다 (기획서 4-1, v0.6의 두 열 경로).
- 설정값 → 토크          : 2차 다항식
- 토크 → 전류            : 2차 다항식
- 설정값 → 진동          : 2차 다항식 (정상 데이터에서는 토크가 설정값에 묶여 있어 둘의 효과를 가를 수 없다.
                           토크를 넣으면 부하가 바뀔 때 엉뚱한 진동 기대값이 나온다)
- 토크 이력 → 온도(지연)  : 빠른·느린 두 열 경로의 1차 지연 합. 입력은 (토크/100)^2

predict() 인터페이스만 지키면 나중에 학습 모델(예: gradient boosting)로 바꿀 수 있다.
"""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field

import numpy as np

from .config import CHANNELS, CONFIG, Config
from .util import lowpass, robust_sigma, rolling_median, transition_mask

MODEL_VERSION = 1


def _arr(run, key):
    return np.asarray(run[key], dtype=float)


@dataclass
class NormalModel:
    tq_poly: list
    cur_poly: list
    vib_coef: list
    tau_f: float
    tau_s: float
    k_f: float
    k_s: float
    sigma: dict = field(default_factory=dict)       # 평활한 잔차의 σ (판정 문턱용)
    sigma_raw: dict = field(default_factory=dict)   # 평활 전 잔차의 σ (간헐 튐용)
    alpha: dict = field(default_factory=dict)       # 설비별 이득 차이: 운전점이 재보정 구간에서 멀어질수록 문턱을 넓힌다
    temp_noise: float = 0.1
    lag_T: float = 300.0                            # 학습한 온도 지연(초): 계단 응답 63% 도달 시간
    n_runs: int = 0
    version: int = MODEL_VERSION

    # ---- 예측 ----
    def predict(self, run) -> dict:
        sp = _arr(run, "setpoint")
        tq = _arr(run, "torque")
        u = np.clip(tq / 100.0, 0, None) ** 2
        return {
            "tq": np.polyval(self.tq_poly, sp),
            "cur": np.polyval(self.cur_poly, tq),
            "vib": np.polyval(self.vib_coef, sp),
            "temp": self.k_f * lowpass(u, self.tau_f) + self.k_s * lowpass(u, self.tau_s),
        }

    def current_from_setpoint(self, sp: float, tq_offset: float = 0.0, cur_offset: float = 0.0) -> float:
        """설정값만으로 본 정상 전류. 클램프미터 값과 비교해 실제 부하 증가를 본다."""
        tq = float(np.polyval(self.tq_poly, sp)) + tq_offset
        return float(np.polyval(self.cur_poly, tq)) + cur_offset

    # ---- 저장 ----
    def to_dict(self) -> dict:
        d = asdict(self)
        for k in ("tq_poly", "cur_poly", "vib_coef"):
            d[k] = [float(x) for x in d[k]]
        return d

    def save(self, path) -> None:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(self.to_dict(), fh, ensure_ascii=False, indent=2)

    @classmethod
    def load(cls, path) -> "NormalModel":
        with open(path, encoding="utf-8") as fh:
            return cls(**json.load(fh))


def _step_lag(k_f, tau_f, k_s, tau_s) -> float:
    t = np.arange(0, 6000.0)
    s = abs(k_f) * (1 - np.exp(-t / tau_f)) + abs(k_s) * (1 - np.exp(-t / tau_s))
    final = abs(k_f) + abs(k_s)
    if final <= 0:
        return 300.0
    idx = np.flatnonzero(s >= 0.632 * final)
    return float(t[idx[0]]) if len(idx) else 6000.0


def fit(runs: list, cfg: Config = CONFIG,
        taus_f=(10, 20, 30, 45, 60, 90), taus_s=(150, 200, 300, 400, 600, 900)) -> NormalModel:
    """정상 운전 회차 목록으로 모델을 배운다. 각 회차는 t, setpoint, torque, current, vib, temp 열을 가진다."""
    if not runs:
        raise ValueError("정상 운전 회차가 하나도 없습니다")

    sp_all, tq_all, cur_all, vib_all = [], [], [], []
    for run in runs:
        t = _arr(run, "t")
        m = (t >= cfg.calib[0]) & ~transition_mask(t, run["setpoint"], cfg.sp_jump, cfg.trans_hold)
        sp_all.append(_arr(run, "setpoint")[m])
        tq_all.append(_arr(run, "torque")[m])
        cur_all.append(_arr(run, "current")[m])
        vib_all.append(_arr(run, "vib")[m])
    sp, tq, cur, vib = (np.concatenate(x) for x in (sp_all, tq_all, cur_all, vib_all))

    tq_poly = np.polyfit(sp, tq, 2)
    cur_poly = np.polyfit(tq, cur, 2)
    vib_coef = np.polyfit(sp, vib, 2)

    # 열 경로: 회차마다 절편(주변 온도)을 따로 두고 두 이득을 함께 맞춘다.
    prepared = []
    for run in runs:
        t = _arr(run, "t")
        u = np.clip(_arr(run, "torque") / 100.0, 0, None) ** 2
        prepared.append((t >= cfg.calib[0], u, _arr(run, "temp")))
    cache = {}

    def lp(i, tau):
        key = (i, tau)
        if key not in cache:
            cache[key] = lowpass(prepared[i][1], tau)
        return cache[key]

    n = len(prepared)
    best = None
    for tf in taus_f:
        for ts in taus_s:
            if ts <= tf:
                continue
            rows, ys = [], []
            for i, (m, _u, temp) in enumerate(prepared):
                onehot = np.zeros((m.sum(), n))
                onehot[:, i] = 1.0
                rows.append(np.column_stack([onehot, lp(i, tf)[m], lp(i, ts)[m]]))
                ys.append(temp[m])
            X, y = np.vstack(rows), np.concatenate(ys)
            coef, *_ = np.linalg.lstsq(X, y, rcond=None)
            sse = float(np.sum((X @ coef - y) ** 2))
            if best is None or sse < best[0]:
                best = (sse, tf, ts, float(coef[-2]), float(coef[-1]))
    _, tau_f, tau_s, k_f, k_s = best

    model = NormalModel(tq_poly=list(tq_poly), cur_poly=list(cur_poly), vib_coef=list(vib_coef),
                        tau_f=float(tau_f), tau_s=float(tau_s), k_f=k_f, k_s=k_s, n_runs=n)

    # 잔차 크기: 재보정 뒤의 잔차로 잰다 (판정과 같은 방식).
    # 설비마다 이득이 조금씩 달라서, 운전점이 재보정 구간에서 멀어지면 잔차가 기울기를 가진다.
    # 회차마다 그 기울기를 재서 크기(alpha)를 배우고, σ는 기울기를 뺀 순수 잡음으로 잰다.
    smooth_res = {c: [] for c in CHANNELS}
    raw_res = {c: [] for c in CHANNELS}
    slopes = {c: [] for c in CHANNELS}
    noise = []
    for run in runs:
        t = _arr(run, "t")
        pred = model.predict(run)
        trans = transition_mask(t, run["setpoint"], cfg.sp_jump, cfg.trans_hold)
        calib = (t >= cfg.calib[0]) & (t <= cfg.calib[1])
        if calib.sum() < 10:
            calib = t <= t[0] + 0.25 * (t[-1] - t[0])
        active = (t >= cfg.detect_from) & ~trans
        meas = {"tq": _arr(run, "torque"), "cur": _arr(run, "current"),
                "vib": _arr(run, "vib"), "temp": _arr(run, "temp")}
        for c in CHANNELS:
            r = meas[c] - pred[c]
            r = r - np.median(r[calib])
            d = pred[c] - np.median(pred[c][calib])
            den = float(np.sum(d[active] ** 2))
            k = float(np.sum(r[active] * d[active]) / den) if den > 1e-9 else 0.0
            slopes[c].append(k)
            r = r - k * d
            raw_res[c].append(r[active])
            smooth_res[c].append(rolling_median(r, cfg.smooth[c])[active])
        noise.append(np.std(np.diff(meas["temp"])) / np.sqrt(2))

    model.sigma = {c: robust_sigma(np.concatenate(smooth_res[c])) for c in CHANNELS}
    model.sigma_raw = {c: robust_sigma(np.concatenate(raw_res[c])) for c in CHANNELS}
    model.alpha = {c: float(np.sqrt(np.mean(np.square(slopes[c])))) for c in CHANNELS}
    model.temp_noise = float(np.median(noise))
    model.lag_T = _step_lag(k_f, tau_f, k_s, tau_s)
    return model
