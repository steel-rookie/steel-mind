"""엔진 동작 테스트. 내부 모의 데이터(개발용)로 기획서의 판정 규칙을 하나씩 확인한다.

실행: python -m pytest engine/tests -q   (레포 루트에서)
"""
import dataclasses
import json
import threading
import urllib.error
import urllib.request

import numpy as np
import pytest

from engine.api import ThreadingHTTPServer, make_handler
from engine.config import CONFIG
from engine.evaluate import portable_reading
from engine.judge import classify, explain, judge
from engine.model import NormalModel, fit
from engine.signatures import fault_table
from engine.sim import make_equipment, simulate_run

SP = 65.0  # 판정 규칙을 깨끗하게 보려고 설정값을 고정한 회차를 쓴다


@pytest.fixture(scope="module")
def setup():
    rng = np.random.default_rng(7)
    fleet = [make_equipment(rng) for _ in range(4)]
    normal = [simulate_run(rng, (), eq=fleet[i % 4])[0] for i in range(12)]
    return fit(normal), fleet


def run_of(setup, faults, seed, onset=220.0, after=240, coupling=0.0, **mag):
    model, fleet = setup
    rng = np.random.default_rng(seed)
    run, truth, _ = simulate_run(rng, faults, eq=fleet[0], onset=onset, T=int(onset + after),
                                 coupling=coupling, mag=mag or None, setpoint=SP)
    return run, truth


def check_from_truth(card, truth, seed=0):
    ck = card["check"]
    return {"channel": ck["channel"], "record": ck["record"],
            "value": portable_reading(truth, ck, np.random.default_rng(seed))}


def codes(card, status):
    return {f["code"] for f in card["findings"] if f["status"] == status}


# ---------- 후보 규칙 ----------
def test_explain_temp_only_is_ambiguous():
    table = fault_table()
    cands = explain({"temp+"}, table)
    assert {frozenset({"E_LUB"}), frozenset({"S_TDR"})} == set(cands)
    assert classify(cands, table) == "혼합"


def test_explain_masked_load_needs_pair():
    table = fault_table()
    cands = explain({"tq+", "cur-"}, table)
    assert cands and all("S_CUR" in H and len(H) == 2 for H in cands)
    assert classify(cands, table) == "동시"


def test_explain_stuck_with_vibration_keeps_sensor_certain():
    table = fault_table()
    cands = explain({"vib+", "tstuck"}, table)
    assert all("S_TST" in H for H in cands)
    assert classify(cands, table) == "혼합"


# ---------- 판정 ----------
def test_normal_run_is_normal(setup):
    run, _ = run_of(setup, (), seed=1)
    assert judge(run, setup[0])["verdict"] == "정상"


def test_current_sensor_bias_confirmed_as_sensor(setup):
    run, _ = run_of(setup, ("S_CUR",), seed=2, cur_bias=30.0)
    card = judge(run, setup[0])
    assert (card["verdict"], card["verdict_detail"]) == ("확정", "센서")
    assert card["evidence_status"] == "측정 의심" and card["handoff_to_guide"] is False
    assert codes(card, "확정") == {"S_CUR"}


def test_load_increase_confirmed_as_equipment(setup):
    run, _ = run_of(setup, ("E_LOAD",), seed=3, load=0.10)
    card = judge(run, setup[0])
    assert (card["verdict"], card["verdict_detail"]) == ("확정", "설비")
    assert card["evidence_status"] == "측정 확인됨" and card["handoff_to_guide"] is True


def test_vibration_only_is_held_then_resolved_by_portable_check(setup):
    run, truth = run_of(setup, ("S_VIB",), seed=4, vib_bias=0.8)
    card = judge(run, setup[0])
    assert card["verdict"] in ("확인 대기", "정보 부족")
    assert card["check"]["channel"] == "vib"
    after = judge(run, setup[0], checks=[check_from_truth(card, truth)])
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "센서")
    assert codes(after, "확정") == {"S_VIB"}


def test_bearing_with_late_temperature_is_equipment(setup):
    run, _ = run_of(setup, ("E_BRG",), seed=5, after=420, brg_vib=1.0, brg_heat=0.06, brg_impulse=0.0)
    card = judge(run, setup[0])
    assert (card["verdict"], card["verdict_detail"]) == ("확정", "설비")


def test_bearing_resolved_by_portable_check_when_temperature_is_slow(setup):
    run, truth = run_of(setup, ("E_BRG",), seed=6, after=120, brg_vib=1.0, brg_heat=0.0, brg_impulse=0.0)
    card = judge(run, setup[0])
    assert card["verdict"] in ("확인 대기", "정보 부족")
    after = judge(run, setup[0], checks=[check_from_truth(card, truth)])
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "설비")


def test_stuck_temperature_with_bearing(setup):
    """기획서 6번: 온도센서 고착은 확정, 진동 상승 원인은 구분 불가 → 휴대용 진동계"""
    run, truth = run_of(setup, ("E_BRG", "S_TST"), seed=8, brg_vib=1.0, brg_heat=0.05, brg_impulse=0.0)
    card = judge(run, setup[0])
    assert "S_TST" in codes(card, "확정")
    assert card["verdict"] == "정보 부족" and card["check"]["channel"] == "vib"
    after = judge(run, setup[0], checks=[check_from_truth(card, truth)])
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "동시")
    assert "온도센서" in after["suspect_sensors"]


def test_coupled_mode_asks_clamp_meter_for_current_sensor(setup):
    """H4·E29: 토크 추정값이 측정 전류에서 오면, 결합 모드는 전류센서 이상을 혼자 확정하지 않고 클램프미터를 요구한다."""
    for after in (240, 500):
        run, truth = run_of(setup, ("S_CUR",), seed=9, after=after, coupling=1.0, cur_bias=30.0)
        card = judge(run, setup[0], coupled=True)
        assert card["verdict"] == "정보 부족" and card["check"]["channel"] == "cur", after
    after_check = judge(run, setup[0], coupled=True, checks=[check_from_truth(card, truth)])
    assert (after_check["verdict"], after_check["verdict_detail"]) == ("확정", "센서")
    assert codes(after_check, "확정") == {"S_CUR"}


def test_coupled_masked_load_is_not_blamed_on_sensor_alone(setup):
    """결합 모드에서 부하 증가가 전류센서 음의 편향에 가려진 경우: 클램프미터 뒤 동시 이상으로 본다."""
    run, truth = run_of(setup, ("E_LOAD", "S_CUR"), seed=11, coupling=1.0, load=0.08, cur_bias=-40.0)
    card = judge(run, setup[0], coupled=True)
    assert card["verdict_detail"] != "센서"
    assert card["check"]["channel"] == "cur"
    after = judge(run, setup[0], coupled=True, checks=[check_from_truth(card, truth)])
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "동시")


def test_masked_load_increase_is_held_by_e25(setup):
    """E25: 전류센서 편향으로 확정하기 전, 토크 추정값이 3σ 넘게 올라 있으면 보류하고 클램프미터.
    토크 징후가 문턱(4σ)을 넘지 않는 상황을 확실히 만들려고 토크 징후 찾기만 끈 설정으로 본다."""
    model, fleet = setup
    eq = fleet[0]
    tq_level = eq.L * (5 + 0.8 * SP + 0.003 * SP ** 2)
    load = 4.0 * model.sigma["tq"] / tq_level
    run, truth = run_of(setup, ("E_LOAD", "S_CUR"), seed=10, load=load, cur_bias=-40.0)
    no_tq = dataclasses.replace(CONFIG, persist={**CONFIG.persist, "tq": 10 ** 6})
    card = judge(run, model, cfg=no_tq)
    assert card["verdict"] == "정보 부족" and card["check"]["channel"] == "cur"
    after = judge(run, model, checks=[check_from_truth(card, truth)], cfg=no_tq)
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "동시")
    assert "S_CUR" in codes(after, "확정")
    # 기본 설정에서도 전류센서 단독으로 확정해 부하 증가를 놓치지는 않는다.
    assert judge(run, model)["verdict_detail"] != "센서"


def test_card_is_json_serializable(setup):
    run, _ = run_of(setup, ("S_VIB",), seed=4, vib_bias=0.8)
    text = json.dumps(judge(run, setup[0], event_id="x"), ensure_ascii=False)
    assert '"verdict"' in text


# ---------- 저장·API ----------
def test_model_roundtrip(setup, tmp_path):
    model = setup[0]
    path = tmp_path / "m.json"
    model.save(path)
    again = NormalModel.load(path)
    run, _ = run_of(setup, ("S_CUR",), seed=2, cur_bias=30.0)
    assert judge(run, again)["verdict"] == judge(run, model)["verdict"]


def test_api_judge_and_errors(setup):
    model = setup[0]
    server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(model, "test"))
    port = server.server_address[1]
    th = threading.Thread(target=server.serve_forever, daemon=True)
    th.start()
    try:
        run, _ = run_of(setup, ("S_CUR",), seed=2, cur_bias=30.0)
        body = json.dumps({"run": {k: list(map(float, v)) for k, v in run.items()}}).encode()
        req = urllib.request.Request(f"http://127.0.0.1:{port}/judge", data=body,
                                     headers={"Content-Type": "application/json"})
        card = json.loads(urllib.request.urlopen(req).read())
        assert card["verdict_detail"] == "센서"
        bad = urllib.request.Request(f"http://127.0.0.1:{port}/judge", data=b'{"run": {"t": [1]}}',
                                     headers={"Content-Type": "application/json"})
        with pytest.raises(urllib.error.HTTPError) as err:
            urllib.request.urlopen(bad)
        assert err.value.code == 400
    finally:
        server.shutdown()
        server.server_close()
