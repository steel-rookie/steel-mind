"""판정에 쓰는 값. 기획서 v0.4~v0.7에 적힌 값을 그대로 옮겼고, 출처를 옆에 적었다."""
from __future__ import annotations

from dataclasses import dataclass, field

CHANNELS = ("tq", "cur", "vib", "temp")
COLUMNS = {"tq": "torque", "cur": "current", "vib": "vib", "temp": "temp"}
UNITS = {"tq": "%", "cur": "A", "vib": "mm/s", "temp": "°C"}


@dataclass(frozen=True)
class Config:
    S: float = 4.0                 # 켜짐 문턱 4σ (E4: 3·5·6σ보다 균형)
    trans_mult: float = 1.75       # 설정값 변경 직후 문턱 배수 (v0.4)
    trans_hold: float = 40.0       # 그 구간 길이(초) (v0.4)
    sp_jump: float = 0.5           # 1초에 이만큼(%) 넘게 바뀌면 설정값 변경으로 본다
    calib: tuple = (30.0, 150.0)   # 설비별 재보정 구간(초): 잔차 중앙값을 0으로 (기획서 4-2)
    detect_from: float = 150.0     # 재보정 구간이 끝난 뒤부터 징후를 찾는다
    smooth: dict = field(default_factory=lambda: {"tq": 5, "cur": 5, "vib": 10, "temp": 30})
    persist: dict = field(default_factory=lambda: {"tq": 10, "cur": 10, "vib": 10, "temp": 20})
    spk_min_edges: int = 3         # 진동 간헐 튐: 문턱을 넘었다 돌아온 횟수
    stuck_window: int = 60         # 온도 고착: 이 창에서 흔들림이 거의 없으면
    stuck_ratio: float = 0.2       # 정상 잡음의 이 비율 아래
    stuck_persist: int = 30
    e25_sigma: float = 3.0         # E25: 전류센서 확정 전 토크 추정값 최근 30초 중앙값이 3σ 이상이면 보류
    e25_window: int = 30
    wait_factor: float = 2.0       # 확인 대기: 학습한 온도 지연의 2배까지 (기획서 4-5)
    wait_cap: float = 900.0
    trend_sigma: float = 1.0       # v0.5 추세 검정: 지연만큼 지나도 온도가 1σ도 안 움직이면 대기 종료
    alpha_k: float = 2.0           # 설비별 이득 차이 여유: 정상 회차에서 잰 기울기 RMS의 몇 배까지 봐줄지 (재작성에서 추가)


CONFIG = Config()

# 현장 확인 허용오차 = 휴대용 계측기 정확도 (절대, 상대) 중 큰 값. 실제 계측기 사양으로 바꿔야 한다.
# 평균 기록은 허용오차를 넘으면 "설치 센서 값과 가상센서 값 중 어느 쪽에 가까운가"로 가른다 (E30).
# 최댓값 기록(간헐 튐)은 가상센서가 평균만 알려 주므로 허용오차만 쓴다. (E20c: ±15%는 간헐 튐을 못 잡았다)
TOLERANCE = {
    ("temp", "평균"): (0.3, 0.0),
    ("vib", "평균"): (0.05, 0.05),
    ("vib", "최댓값"): (0.3, 0.25),
    ("cur", "평균"): (2.0, 0.02),
}

CHECK_TOOLS = {
    "vib": {"tool": "휴대용 진동계", "where": "구동측 베어링 하우징 (설치 센서 바로 옆)", "seconds": 30, "record": "평균"},
    "temp": {"tool": "휴대용 접촉식 온도계", "where": "모터 고정자 표면 (설치 센서 바로 옆)", "seconds": 30, "record": "평균"},
    "cur": {"tool": "클램프미터", "where": "모터 전원 케이블 한 상", "seconds": 30, "record": "평균"},
}
