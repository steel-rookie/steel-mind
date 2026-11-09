"""STEEL MIND 결과 보고서 프로토타입 서버.

화면(index.html)은 규칙으로 계산한 보고서를 바로 보여 주고, 문장 칸(총평·결정적 순간·Agent 한마디·다음 판 제안)만
이 서버에 '작업'으로 맡긴다. 서버는 작업을 백그라운드 스레드에서 LLM으로 처리하고, 화면은 작업 상태를 주기적으로 묻는다.

- POST /api/report/jobs        판 기록(facts)을 받아 작업을 만들고 바로 job_id를 돌려준다(202).
- GET  /api/report/jobs/<id>   작업 상태: running / done(report 포함) / failed(error 포함)
- GET  /api/health             지금 쓰는 LLM 제공자
- 그 밖의 GET                  이 폴더의 파일(index.html, scenario.js)

LLM은 Gemini만 쓴다. 키는 루트 .env의 GEMINI_API_KEY(모델은 GEMINI_MODEL). 키가 없으면 작업은 바로 실패하고 화면은 기본 문장으로 남는다.
API 키는 서버에만 있고 브라우저로 나가지 않는다. 같은 경로(시나리오 ID + 결과 ID)의 결과는 .cache/에 저장해 다시 부르지 않는다.
표준 라이브러리만 쓴다. 실행: python llm/result-report/server.py  →  http://localhost:8791
"""
import json
import os
import re
import sys
import threading
import time
import urllib.request
import uuid
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CACHE = HERE / ".cache"
PORT = int(os.environ.get("REPORT_PORT", "8791"))


def load_env():
    env = {}
    path = ROOT / ".env"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                env[k.strip()] = v.strip()
    for k in ("GEMINI_API_KEY", "GEMINI_MODEL"):
        if os.environ.get(k):
            env[k] = os.environ[k]
    return env


ENV = load_env()


MODEL = ENV.get("GEMINI_MODEL") or "gemini-2.5-flash"


def provider_name():
    return f"gemini:{MODEL}" + ("" if ENV.get("GEMINI_API_KEY") else " (키 없음)")


PERSONAS = {
    "production": "생산 공장장. 급하고 실적을 먼저 따진다. 멈춤과 지연에 예민하다.",
    "quality": "품질 공장장. 꼼꼼하고 차분하다. 고객이 받을 제품인지를 기준으로 말한다.",
    "equipment": "설비 공장장. 현실적이고 담백하다. 설비가 상했는지, 정비가 필요한지를 말한다.",
    "safety": "안전 공장장. 단호하다. 사람이 다칠 뻔했는지를 가장 먼저 본다.",
}


def build_prompt(facts):
    record = {k: facts.get(k) for k in ("scenario_title", "type", "type_desc", "accident", "events", "worst_events", "agents")}
    personas = "\n".join(f"- {k}: {v}" for k, v in PERSONAS.items())
    return f"""너는 제철소 운영 시뮬레이션 게임 'STEEL MIND'의 결과 보고서를 쓰는 작가다.
아래 [판 기록]에 있는 사실만 써라. 기록에 없는 사건, 설비, 수치를 지어내지 마라.
숫자(퍼센트, 점수, 횟수)는 한 글자도 쓰지 마라. 숫자는 화면의 다른 칸에 이미 나온다.
플레이어는 이 공장장들을 이끄는 운영 책임자다. 플레이어를 '책임자님'이라고 부르고 존댓말로, 짧고 구체적으로 쓴다.

[판 기록]
{json.dumps(record, ensure_ascii=False, indent=1)}

[공장장 말투]
{personas}

아래 JSON 하나만 출력하라. 다른 글은 쓰지 마라.
{{"headline": "이번 판을 한 문장으로(40자 이내)",
 "turning_points": ["worst_events 첫 사건에서 무엇을 골랐고 무엇을 잃었는지(90자 이내)", "worst_events 둘째 사건(있을 때만, 90자 이내)"],
 "agent_comments": {{"production": "생산 공장장이 이번 판을 두고 하는 한마디(60자 이내)", "quality": "…", "equipment": "…", "safety": "…"}},
 "next_try": "다음 판에 바꿔 볼 선택 하나(60자 이내)"}}
worst_events가 비어 있으면 turning_points에는 가장 잘한 결정 하나를 짚어라.
agent_comments는 agents의 followed(그 공장장 추천을 따른 정도)와 말투에 맞춰 만족하거나 불만을 말한다."""


def post_json(url, body, headers, timeout):
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json", **headers}, method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as res:
        return json.loads(res.read().decode("utf-8"))


def call_llm(prompt):
    key = ENV.get("GEMINI_API_KEY")
    if not key:
        raise RuntimeError("GEMINI_API_KEY가 없음(.env 확인)")
    data = post_json(
        f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
        {"contents": [{"parts": [{"text": prompt}]}],
         "generationConfig": {"responseMimeType": "application/json", "temperature": 0.4}},
        {"x-goog-api-key": key}, timeout=40)  # 키는 URL이 아니라 헤더로 보낸다
    return data["candidates"][0]["content"]["parts"][0]["text"]


def check_shape(text):
    """모양만 확인한다(글자 수·숫자 검사는 화면에서 칸마다 한다)."""
    d = json.loads(re.sub(r"^```(?:json)?|```$", "", text.strip()).strip())
    out = {
        "headline": str(d.get("headline", "")).strip(),
        "turning_points": [str(x).strip() for x in (d.get("turning_points") or []) if str(x).strip()][:2],
        "agent_comments": {k: str((d.get("agent_comments") or {}).get(k, "")).strip() for k in PERSONAS},
        "next_try": str(d.get("next_try", "")).strip(),
    }
    if not out["headline"] and not out["turning_points"]:
        raise ValueError("LLM 응답에 쓸 만한 칸이 없음")
    return out


JOBS = {}
LOCK = threading.Lock()


def run_job(job_id, facts, key):
    started = time.time()
    try:
        report = check_shape(call_llm(build_prompt(facts)))
        CACHE.mkdir(exist_ok=True)
        (CACHE / f"{key}.json").write_text(json.dumps({"provider": provider_name(), "report": report}, ensure_ascii=False, indent=1), encoding="utf-8")
        update = {"status": "done", "provider": provider_name(), "report": report}
    except Exception as e:  # noqa: BLE001 - 어떤 실패든 화면은 기본 문장으로 넘어간다
        update = {"status": "failed", "error": f"{type(e).__name__}: {e}"[:300]}
    update["seconds"] = round(time.time() - started, 1)
    with LOCK:
        JOBS[job_id].update(update)
    print(f"[job {job_id}] {update['status']} {update['seconds']}s {update.get('error', '')}", flush=True)


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(HERE), **kwargs)

    def send_json(self, code, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("Access-Control-Allow-Origin", "*")  # 파일·Live Server로 연 화면도 부를 수 있게
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_POST(self):
        if self.path != "/api/report/jobs":
            return self.send_json(404, {"error": "not found"})
        try:
            facts = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
        except ValueError:
            return self.send_json(400, {"error": "JSON이 아님"})
        key = re.sub(r"[^A-Za-z0-9_-]", "", f"{facts.get('scenario_id', 'x')}_{facts.get('path_id', 'x')}")
        job_id = uuid.uuid4().hex[:12]
        cached = CACHE / f"{key}.json"
        if cached.exists() and not facts.get("no_cache"):
            data = json.loads(cached.read_text(encoding="utf-8"))
            job = {"status": "done", "provider": data["provider"] + " · 캐시", "report": data["report"], "seconds": 0}
            with LOCK:
                JOBS[job_id] = job
        else:
            job = {"status": "running"}
            with LOCK:
                JOBS[job_id] = dict(job)
            threading.Thread(target=run_job, args=(job_id, facts, key), daemon=True).start()
        return self.send_json(202, {"job_id": job_id, **job})

    def do_GET(self):
        if self.path.startswith("/api/report/jobs/"):
            with LOCK:
                job = JOBS.get(self.path.rsplit("/", 1)[-1])
            return self.send_json(200 if job else 404, job or {"status": "missing"})
        if self.path == "/api/health":
            return self.send_json(200, {"provider": provider_name()})
        return super().do_GET()

    def log_message(self, fmt, *args):
        if "/api/report/jobs/" not in str(args[0] if args else ""):
            sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))


if __name__ == "__main__":
    print(f"결과 보고서 서버: http://localhost:{PORT}  (LLM: {provider_name()})", flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
