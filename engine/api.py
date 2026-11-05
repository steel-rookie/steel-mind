"""판정 카드 API. 표준 라이브러리만 쓴다.

POST /judge   본문: {"run": {"t": [...], "setpoint": [...], "torque": [...], "current": [...], "vib": [...], "temp": [...]},
                    "coupled": false, "checks": [{"channel": "vib", "value": 3.1, "record": "평균"}],
                    "event_id": "...", "equipment_id": "..."}
              응답: 판정 카드
POST /judge?view=evidence   같은 요청, 응답: 정비 가이드에 넘기는 근거 카드 (handoff.py)
GET  /health  엔진·모델 정보
"""
from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np

from . import __version__
from .handoff import evidence_card
from .judge import judge
from .model import NormalModel

REQUIRED = ("t", "setpoint", "torque", "current", "vib", "temp")


def make_handler(model: NormalModel, model_path: str):
    class Handler(BaseHTTPRequestHandler):
        def _send(self, code, payload):
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_OPTIONS(self):
            self._send(204, {})

        def do_GET(self):
            if self.path == "/health":
                self._send(200, {"ok": True, "engine_version": __version__, "model": model_path,
                                 "lag_T_s": model.lag_T, "trained_runs": model.n_runs})
            else:
                self._send(404, {"error": "없는 주소입니다. GET /health, POST /judge"})

        def do_POST(self):
            path, _, query = self.path.partition("?")
            if path != "/judge":
                self._send(404, {"error": "없는 주소입니다. POST /judge"})
                return
            evidence = "view=evidence" in query.split("&")
            try:
                length = int(self.headers.get("Content-Length", 0))
                req = json.loads(self.rfile.read(length) or b"{}")
                raw = req.get("run") or {}
                missing = [k for k in REQUIRED if k not in raw]
                if missing:
                    self._send(400, {"error": f"run에 열이 빠졌습니다: {', '.join(missing)}"})
                    return
                lengths = {len(raw[k]) for k in REQUIRED}
                if len(lengths) != 1:
                    self._send(400, {"error": "run의 열 길이가 서로 다릅니다"})
                    return
                run = {k: np.asarray(raw[k], dtype=float) for k in REQUIRED}
                card = judge(run, model, coupled=bool(req.get("coupled", False)), checks=req.get("checks"),
                             event_id=req.get("event_id"), equipment_id=req.get("equipment_id"))
                self._send(200, evidence_card(card) if evidence else card)
            except (ValueError, KeyError, TypeError) as exc:
                self._send(400, {"error": f"요청을 읽지 못했습니다: {exc}"})

        def log_message(self, fmt, *args):
            pass

    return Handler


def serve(model: NormalModel, model_path: str, host: str = "127.0.0.1", port: int = 8000) -> None:
    server = ThreadingHTTPServer((host, port), make_handler(model, model_path))
    print(f"판정 엔진 API: http://{host}:{port}  (POST /judge, GET /health)")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
