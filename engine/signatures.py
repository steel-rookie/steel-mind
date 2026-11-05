"""고장 징후표.

고장 8종(설비 3, 센서 5)이 어떤 잔차 징후를 만드는지 적는다.
징후 이름: tq+/tq- (설정값 대비 토크), cur+/cur- (토크 대비 전류), vib+/vib- (운전 조건 대비 진동),
vibspk (진동 간헐 튐), temp+/temp- (토크 이력 대비 온도), tstuck (온도 값 고착),
load+ (클램프미터로 잰 실제 전류가 설정값 대비 높음 = 실제 부하 증가. 현장 확인에서만 생긴다).

- requires: 묶음마다 하나 이상은 보여야 그 고장으로 설명된다.
- may:      함께 보여도 되는 징후.
- late:     열 지연 뒤에 나타날 수 있는 징후. 기다리면 후보가 갈릴 수 있다.

안전 원칙: 늦은 징후가 "안 나타났다"는 이유로 설비 고장을 지우지 않는다.
나타난 징후만 후보를 지운다. 설비 이상을 센서 탓으로 넘기는 오류(위험 오분류)가 가장 비싸기 때문이다.
"""
from __future__ import annotations

from dataclasses import dataclass

SENSOR = "센서"
EQUIP = "설비"

SYMPTOM_LABELS = {
    "tq+": "토크가 설정값 대비 높음",
    "tq-": "토크가 설정값 대비 낮음",
    "cur+": "전류가 토크 대비 높음",
    "cur-": "전류가 토크 대비 낮음",
    "vib+": "진동이 운전 조건 대비 높음",
    "vib-": "진동이 운전 조건 대비 낮음",
    "vibspk": "진동이 간헐적으로 튐",
    "temp+": "온도가 토크 이력 대비 높음",
    "temp-": "온도가 토크 이력 대비 낮음",
    "tstuck": "온도 값이 변하지 않음",
    "load+": "실제 전류(클램프미터)가 설정값 대비 높음",
}


@dataclass(frozen=True)
class Fault:
    code: str
    name: str
    kind: str
    target: str
    requires: tuple
    may: frozenset = frozenset()
    late: frozenset = frozenset()
    channel: str | None = None  # 센서 고장이면 현장 계측기로 확인할 채널

    @property
    def allowed(self) -> frozenset:
        out = set(self.may) | set(self.late)
        for g in self.requires:
            out |= set(g)
        return frozenset(out)


def _f(*names):
    return frozenset(names)


def fault_table(coupled: bool = False) -> dict[str, Fault]:
    """coupled=True면 인버터 토크 추정값이 측정 전류에서 계산된다고 본다(결합 모드)."""
    faults = [
        Fault("E_BRG", "베어링 열화", EQUIP, "베어링",
              requires=(_f("vib+", "vibspk"),), may=_f("tq+", "vib+", "vibspk"), late=_f("temp+")),
        Fault("E_LUB", "윤활 부족(마찰 증가)", EQUIP, "베어링·윤활",
              requires=(_f("tq+", "temp+", "load+"),), may=_f("tq+", "vib+", "load+"), late=_f("temp+")),
        Fault("E_LOAD", "부하 증가", EQUIP, "부하",
              requires=(_f("tq+", "load+"),), may=_f("vib+", "load+")),
        Fault("S_VIB", "진동센서 편향", SENSOR, "진동센서",
              requires=(_f("vib+", "vib-"),), channel="vib"),
        Fault("S_VSPK", "진동센서 간헐 튐", SENSOR, "진동센서",
              requires=(_f("vibspk"),), channel="vib"),
        Fault("S_TDR", "온도센서 드리프트", SENSOR, "온도센서",
              requires=(_f("temp+", "temp-"),), channel="temp"),
        Fault("S_TST", "온도센서 고착", SENSOR, "온도센서",
              requires=(_f("tstuck"),), may=_f("temp+", "temp-"), channel="temp"),
    ]
    if coupled:
        # 측정 전류가 틀리면 토크 추정값도 같이 틀리고, 토크로 예측한 온도가 실제와 어긋난다.
        faults.append(Fault("S_CUR", "전류센서 편향", SENSOR, "전류센서",
                            requires=(_f("tq+", "tq-", "cur+", "cur-"),),
                            may=_f("tq+", "tq-", "cur+", "cur-"), late=_f("temp+", "temp-"),
                            channel="cur"))
    else:
        faults.append(Fault("S_CUR", "전류센서 편향", SENSOR, "전류센서",
                            requires=(_f("cur+", "cur-"),), channel="cur"))
    return {f.code: f for f in faults}
