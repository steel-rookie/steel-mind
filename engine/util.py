"""작은 시계열 도구. numpy만 쓴다."""
from __future__ import annotations

import numpy as np
from numpy.lib.stride_tricks import sliding_window_view


def _trailing(x: np.ndarray, w: int, fn) -> np.ndarray:
    """지금까지의 값만 쓰는(인과적) 이동 창 계산. 앞부분은 첫 값으로 채운다."""
    x = np.asarray(x, dtype=float)
    if w <= 1 or len(x) == 0:
        return x.copy()
    pad = np.concatenate([np.full(w - 1, x[0]), x])
    return fn(sliding_window_view(pad, w), axis=1)


def rolling_median(x, w: int) -> np.ndarray:
    return _trailing(x, w, np.median)


def rolling_std(x, w: int) -> np.ndarray:
    return _trailing(x, w, np.std)


def lowpass(u, tau: float, dt: float = 1.0) -> np.ndarray:
    """1차 지연 필터. 처음 값에서 정상 상태라고 보고 시작한다."""
    u = np.asarray(u, dtype=float)
    y = np.empty_like(u)
    if len(u) == 0:
        return y
    a = dt / (tau + dt)
    y[0] = u[0]
    for k in range(1, len(u)):
        y[k] = y[k - 1] + a * (u[k] - y[k - 1])
    return y


def transition_mask(t, setpoint, jump: float, hold: float) -> np.ndarray:
    """설정값이 바뀐 직후 hold초 동안을 True로 표시한다."""
    t = np.asarray(t, dtype=float)
    sp = np.asarray(setpoint, dtype=float)
    mask = np.zeros(len(t), dtype=bool)
    if len(t) < 2:
        return mask
    changed = np.flatnonzero(np.abs(np.diff(sp)) > jump) + 1
    for i in changed:
        mask |= (t >= t[i]) & (t < t[i] + hold)
    return mask


def first_persistent(mask, n: int):
    """True가 n번 이상 이어지는 첫 구간의 시작 위치. 없으면 None."""
    count = 0
    for i, v in enumerate(mask):
        count = count + 1 if v else 0
        if count >= n:
            return i - n + 1
    return None


def robust_sigma(x) -> float:
    x = np.asarray(x, dtype=float)
    x = x[np.isfinite(x)]
    if len(x) == 0:
        return 1.0
    mad = np.median(np.abs(x - np.median(x)))
    return float(max(1.4826 * mad, 1e-6))
