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
from engine.baseline import baseline_judge
from engine.config import CONFIG
from engine.evaluate import portable_reading
from engine.handoff import evidence_card
from engine.judge import classify, explain, judge
from engine.metrics import format_resolution, format_table, hold_resolution, summarize
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


def states(card):
    return card["measurement_status"], card["equipment_status"]


def no_equipment_confirmed(card):
    """설비 원인은 엔진이 "확정"이라 쓰지 않는다 (기획서 4.3)."""
    return all(f["status"] == "후보" for f in card["findings"] if f["kind"] == "설비")


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
    card = judge(run, setup[0])
    assert card["verdict"] == "정상"
    assert states(card) == ("확인됨", "징후 없음") and card["next_steps"] == []


def test_current_sensor_bias_confirmed_as_sensor(setup):
    run, _ = run_of(setup, ("S_CUR",), seed=2, cur_bias=30.0)
    card = judge(run, setup[0])
    assert (card["verdict"], card["verdict_detail"]) == ("확정", "센서")
    assert states(card) == ("의심", "징후 없음")
    assert card["handoff_to_guide"] is False and card["next_steps"] == ["계측 점검"]
    assert codes(card, "확정") == {"S_CUR"} and card["suspect_sensors"] == ["전류센서"]


def test_load_increase_confirmed_as_equipment(setup):
    run, _ = run_of(setup, ("E_LOAD",), seed=3, load=0.10)
    card = judge(run, setup[0])
    assert (card["verdict"], card["verdict_detail"]) == ("확정", "설비")
    assert states(card) == ("확인됨", "후보 있음") and card["handoff_to_guide"] is True
    # 설비 쪽 징후로 넘기지만 원인은 후보다
    assert card["equipment_basis"] == "설비 쪽 징후" and no_equipment_confirmed(card)
    # 토크 상승만으로는 부하 증가와 윤활 부족을 가르지 않는다. 둘 다 후보로 넘긴다.
    assert "E_LOAD" in codes(card, "후보") and codes(card, "확정") == set()


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
    # 센서 정상 ≠ 설비 고장 확정: 센서를 배제했을 뿐이라고 적는다
    assert states(after) == ("확인됨", "후보 있음")
    assert after["equipment_basis"] == "센서 배제로 남은 후보" and no_equipment_confirmed(after)
    assert "확정하지 않았다" in after["handoff_note"]


def test_stuck_temperature_with_bearing(setup):
    """기획서 6번: 온도센서 고착은 확정, 진동 상승 원인은 구분 불가 → 휴대용 진동계"""
    run, truth = run_of(setup, ("E_BRG", "S_TST"), seed=8, brg_vib=1.0, brg_heat=0.05, brg_impulse=0.0)
    card = judge(run, setup[0])
    assert "S_TST" in codes(card, "확정")
    assert card["verdict"] == "정보 부족" and card["check"]["channel"] == "vib"
    assert states(card) == ("정보 부족", "판단 보류") and card["next_steps"] == ["현장 확인"]
    assert card["suspect_sensors"] == ["온도센서"] and card["handoff_to_guide"] is False
    after = judge(run, setup[0], checks=[check_from_truth(card, truth)])
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "동시")
    assert "온도센서" in after["suspect_sensors"]
    assert states(after) == ("일부 의심", "후보 있음") and no_equipment_confirmed(after)
    assert after["next_steps"] == ["정비 가이드로 넘김", "계측 점검"]


def test_coupled_mode_asks_clamp_meter_for_current_sensor(setup):
    """H4·E29: 토크 추정값이 측정 전류에서 오면, 결합 모드는 전류센서 이상을 혼자 확정하지 않고 클램프미터를 요구한다."""
    for after in (240, 500):
        run, truth = run_of(setup, ("S_CUR",), seed=9, after=after, coupling=1.0, cur_bias=30.0)
        card = judge(run, setup[0], coupled=True)
        assert card["verdict"] == "정보 부족" and card["check"]["channel"] == "cur", after
        # 부하 증가가 가려졌을 수 있으니 전류센서도 아직 확정이라 쓰지 않는다
        assert "S_CUR" not in codes(card, "확정") and states(card) == ("정보 부족", "판단 보류")
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
    assert "S_CUR" not in codes(card, "확정")
    after = judge(run, model, checks=[check_from_truth(card, truth)], cfg=no_tq)
    assert (after["verdict"], after["verdict_detail"]) == ("확정", "동시")
    assert "S_CUR" in codes(after, "확정")
    # 기본 설정에서도 전류센서 단독으로 확정해 부하 증가를 놓치지는 않는다.
    assert judge(run, model)["verdict_detail"] != "센서"


def test_check_card_marks_example_values(setup):
    """현장 확인 시간·기록 방식·허용오차는 현업 확인 전까지 예시값이다 (기획서 4.2 ③)."""
    run, truth = run_of(setup, ("S_VIB",), seed=4, vib_bias=0.8)
    card = judge(run, setup[0])
    ck = card["check"]
    assert set(ck["example_values"]) == {"seconds", "record", "read_rule"} and "예시값" in ck["example_note"]
    assert "mm/s" in ck["read_rule"]
    after = judge(run, setup[0], checks=[check_from_truth(card, truth)])
    assert all(r["tolerance_is_example"] for r in after["checks_applied"])


def test_evidence_card_drops_suspect_sensor_values(setup):
    """정비 가이드에는 의심 센서 값을 빼고 넘긴다. 상태 두 가지와 예시값 표시는 그대로 넘긴다."""
    run, truth = run_of(setup, ("E_BRG", "S_TST"), seed=8, brg_vib=1.0, brg_heat=0.05, brg_impulse=0.0)
    card = judge(run, setup[0])
    ev = evidence_card(card)
    assert ev["values"]["temp"]["excluded"] is True and "installed" not in ev["values"]["temp"]
    assert ev["values"]["vib"]["installed"] == card["values"]["vib"]["installed"]
    assert (ev["measurement_status"], ev["equipment_status"]) == states(card)
    assert ev["confirmed_sensor_faults"] == ["온도센서 고착"] and "예시값" in ev["check"]["example_note"]
    assert "z_now" not in json.dumps(ev, ensure_ascii=False)


def test_card_is_json_serializable(setup):
    run, _ = run_of(setup, ("S_VIB",), seed=4, vib_bias=0.8)
    text = json.dumps(judge(run, setup[0], event_id="x"), ensure_ascii=False)
    assert '"verdict"' in text


# ---------- 비교 기준 (H1) ----------
def test_baseline_rule(setup):
    model = setup[0]
    run, _ = run_of(setup, (), seed=1)
    assert baseline_judge(run, model)["verdict"] == "정상"
    run, _ = run_of(setup, ("S_CUR",), seed=2, cur_bias=30.0)
    card = baseline_judge(run, model)
    assert card["verdict_detail"] == "센서" and card["suspect_sensors"] == ["전류센서"]
    run, _ = run_of(setup, ("E_LOAD",), seed=3, load=0.10)
    assert baseline_judge(run, model)["verdict_detail"] == "설비"
    # 진동만 먼저 오르는 베어링 열화를 진동센서 탓으로 본다. 판정 엔진은 보류한다. 이 차이를 H1에서 잰다.
    run, _ = run_of(setup, ("E_BRG",), seed=6, after=120, brg_vib=1.0, brg_heat=0.0, brg_impulse=0.0)
    assert baseline_judge(run, model)["verdict_detail"] == "센서"
    assert judge(run, model)["verdict"] in ("확인 대기", "정보 부족")


# ---------- 지표 ----------
def test_risky_uses_equipment_runs_and_reports_missing_denominator():
    rows = [{"truth": "센서", "faults": ["S_VIB"], "pred": "센서"},
            {"truth": "센서", "faults": ["S_CUR"], "pred": "설비"},
            {"truth": "정상", "faults": [], "pred": "정상"}]
    m = summarize(rows)
    assert m["risky"] == {"k": 0, "n": 0, "rate": None}
    assert m["sensor_as_equip"]["k"] == 1 and m["sensor_as_equip"]["n"] == 2
    text = format_table({"x": m})
    assert "평가 불가 (분모 0)" in text and "1/2 (50.0%)" in text
    rows.append({"truth": "동시", "faults": ["E_LOAD", "S_CUR"], "pred": "센서"})
    rows.append({"truth": "설비", "faults": ["E_BRG"], "pred": "보류"})
    m = summarize(rows)
    assert (m["risky"]["k"], m["risky"]["n"]) == (1, 2)
    assert (m["hold"]["k"], m["hold"]["n"]) == (1, 4)


def test_hold_resolution_counts_only_held_runs():
    rows = [{"truth": "센서", "faults": ["S_VIB"], "pred": "보류", "pred_after": "센서"},
            {"truth": "설비", "faults": ["E_BRG"], "pred": "보류", "pred_after": "센서"},
            {"truth": "동시", "faults": ["E_BRG", "S_TST"], "pred": "보류", "pred_after": "보류"},
            {"truth": "설비", "faults": ["E_LOAD"], "pred": "설비", "pred_after": "설비"}]
    r = hold_resolution(rows)
    assert r["held"] == 3
    assert (r["correct"]["k"], r["wrong"]["k"], r["remaining"]["k"]) == (1, 1, 1)
    assert r["wrong_risky"] == 1
    assert "확인 시간: 평가 불가" in format_resolution(r)
    assert "평가 불가" in format_resolution(hold_resolution(rows[3:]))


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
        req = urllib.request.Request(f"http://127.0.0.1:{port}/judge?view=evidence", data=body,
                                     headers={"Content-Type": "application/json"})
        ev = json.loads(urllib.request.urlopen(req).read())
        assert ev["measurement_status"] == "의심" and ev["values"]["cur"]["excluded"] is True
        bad = urllib.request.Request(f"http://127.0.0.1:{port}/judge", data=b'{"run": {"t": [1]}}',
                                     headers={"Content-Type": "application/json"})
        with pytest.raises(urllib.error.HTTPError) as err:
            urllib.request.urlopen(bad)
        assert err.value.code == 400
    finally:
        server.shutdown()
        server.server_close()
