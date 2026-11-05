"""잔차 계산과 징후 찾기."""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .config import CHANNELS, COLUMNS, CONFIG, Config
from .model import NormalModel
from .util import first_persistent, rolling_median, rolling_std, transition_mask


@dataclass
class Features:
    t: np.ndarray
    measured: dict      # 채널별 측정값
    expected: dict      # 채널별 기대값(가상센서, 재보정 포함)
    offsets: dict       # 설비별 재보정 오프셋
    z: dict             # 평활한 표준화 잔차
    zraw: dict          # 평활 전 표준화 잔차
    symptoms: dict      # 징후 -> 시작 시각(초)
    onset: float | None


def detect(run, model: NormalModel, cfg: Config = CONFIG) -> Features:
    t = np.asarray(run["t"], dtype=float)
    measured = {c: np.asarray(run[COLUMNS[c]], dtype=float) for c in CHANNELS}
    pred = model.predict(run)

    calib = (t >= cfg.calib[0]) & (t <= cfg.calib[1])
    if calib.sum() < 10:
        calib = t <= t[0] + 0.25 * (t[-1] - t[0])
    offsets = {c: float(np.median((measured[c] - pred[c])[calib])) for c in CHANNELS}

    trans = transition_mask(t, run["setpoint"], cfg.sp_jump, cfg.trans_hold)
    mult = np.where(trans, cfg.trans_mult, 1.0)
    active = t >= cfg.detect_from

    z, zraw, expected = {}, {}, {}
    for c in CHANNELS:
        expected[c] = pred[c] + offsets[c]
        r = measured[c] - expected[c]
        # 운전점이 재보정 구간에서 멀어질수록 설비별 이득 차이만큼 문턱을 넓힌다.
        d = pred[c] - float(np.median(pred[c][calib]))
        spread = (cfg.alpha_k * model.alpha.get(c, 0.0) * d) ** 2
        zraw[c] = r / np.sqrt(model.sigma_raw[c] ** 2 + spread)
        z[c] = rolling_median(r, cfg.smooth[c]) / np.sqrt(model.sigma[c] ** 2 + spread)

    symptoms: dict[str, float] = {}
    for c in CHANNELS:
        thr = cfg.S * mult
        for sign, mask in (("+", z[c] > thr), ("-", z[c] < -thr)):
            i = first_persistent(mask & active, cfg.persist[c])
            if i is not None:
                symptoms[f"{c}{sign}"] = float(t[i])

    # 진동 간헐 튐: 평활 진동은 정상인데 순간값만 문턱을 넘었다 돌아오기를 반복한다.
    if "vib+" not in symptoms:
        spk = (zraw["vib"] > cfg.S) & (z["vib"] < cfg.S) & active
        edges = np.flatnonzero(spk & ~np.concatenate([[False], spk[:-1]]))
        if len(edges) >= cfg.spk_min_edges:
            symptoms["vibspk"] = float(t[edges[0]])

    # 온도 고착: 정상 잡음보다 훨씬 작게만 흔들린다.
    sd = rolling_std(measured["temp"], cfg.stuck_window)
    stuck = (sd < cfg.stuck_ratio * model.temp_noise) & active
    i = first_persistent(stuck, cfg.stuck_persist)
    if i is not None:
        symptoms["tstuck"] = float(max(t[i] - (cfg.stuck_window - 1), t[0]))

    onset = min(symptoms.values()) if symptoms else None
    return Features(t=t, measured=measured, expected=expected, offsets=offsets,
                    z=z, zraw=zraw, symptoms=symptoms, onset=onset)
