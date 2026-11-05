"""비교 기준: 현장 첫 판단 규칙 (기획서 H1).

이전 비교군(v0.2)의 코드가 남아 있지 않아 H1의 비교 기준을 새로 만든다.
현장에서 알람을 처음 볼 때 흔히 쓰는 어림 규칙을 그대로 옮겼다.

  - 아무 채널도 기대값을 벗어나지 않으면 정상
  - 센서 채널(전류·진동·온도) 하나만 벗어나고 토크(부하)는 그대로면 → 그 센서 고장
  - 토크가 함께 벗어나거나, 두 채널 이상이 함께 벗어나면 → 설비 고장

토크는 인버터가 계산한 값이라 센서 고장 대상으로 보지 않는다.
징후 찾기는 판정 엔진과 똑같이 쓴다. 그래서 두 결과의 차이는 "판정 논리"의 차이만 보여 준다.
이 규칙은 보류하지 않고, 현장 확인도 요구하지 않는다.
"""
from __future__ import annotations

from .config import CONFIG, Config
from .detect import detect
from .judge import CHANNEL_SENSOR, _channel_of
from .model import NormalModel

RULE_NAME = "현장 첫 판단 규칙"


def baseline_judge(run, model: NormalModel, event_id: str | None = None, cfg: Config = CONFIG) -> dict:
    feats = detect(run, model, cfg)
    channels = sorted({_channel_of(code) for code in feats.symptoms})
    card = {"rule": RULE_NAME, "event_id": event_id, "channels": channels,
            "verdict": "정상", "verdict_detail": None, "suspect_sensors": [], "reasons": []}
    if not channels:
        card["reasons"].append("벗어난 채널 없음")
        return card
    sensor_channels = [c for c in channels if c in CHANNEL_SENSOR]
    if "tq" not in channels and len(sensor_channels) == 1:
        sensor = CHANNEL_SENSOR[sensor_channels[0]]
        card.update(verdict="확정", verdict_detail="센서", suspect_sensors=[sensor])
        card["reasons"].append(f"{sensor} 하나만 벗어나고 토크는 그대로라 센서 고장으로 봄")
    else:
        card.update(verdict="확정", verdict_detail="설비")
        card["reasons"].append("토크가 함께 벗어나거나 여러 채널이 함께 벗어나 설비 고장으로 봄")
    return card
