using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SteelMind.Scenario
{
    /// <summary>LLM이 쓴 보고서 문장 칸. 검사를 통과하지 못한 칸은 null(기본 문장 유지).</summary>
    public class LlmText
    {
        public string headline, nextTry;
        public List<string> moments;
        public Dictionary<string, string> agents = new Dictionary<string, string>();
        public string provider;
        public float seconds;
    }

    /// <summary>
    /// 결과 보고서 문장 칸을 Gemini로 채운다(발표 시연용, 내 컴퓨터에서만 실행).
    /// - 키는 코드·빌드에 넣지 않는다. 환경 변수 GEMINI_API_KEY 또는 레포 루트의 .env(Git 제외)에서 읽는다.
    /// - 키는 URL이 아니라 x-goog-api-key 헤더로 보낸다.
    /// - 숫자는 규칙 칸에만 나오므로 LLM 문장에 숫자가 있거나, 비었거나, 너무 길면 그 칸은 기본 문장으로 둔다.
    /// 프롬프트와 검사 규칙은 llm/result-report(server.py, index.html)와 같다.
    /// </summary>
    public static class ReportLlm
    {
        const int MaxHeadline = 60, MaxMoment = 120, MaxAgent = 80, MaxNext = 80;

        static Dictionary<string, string> env;

        /// <summary>환경 변수 → 위로 올라가며 찾은 .env 순서로 값을 읽는다.</summary>
        static string Env(string key)
        {
            var v = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(v)) return v;
            if (env == null)
            {
                env = new Dictionary<string, string>();
                var dir = new DirectoryInfo(Application.dataPath);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    var f = Path.Combine(dir.FullName, ".env");
                    if (!File.Exists(f)) continue;
                    foreach (var raw in File.ReadAllLines(f))
                    {
                        var line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (line.Length == 0 || line.StartsWith("#") || eq < 0) continue;
                        env[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                    break;
                }
            }
            return env.TryGetValue(key, out var e) ? e : null;
        }

        public static bool HasKey => !string.IsNullOrEmpty(Env("GEMINI_API_KEY"));
        static string Model => string.IsNullOrEmpty(Env("GEMINI_MODEL")) ? "gemini-2.5-flash" : Env("GEMINI_MODEL");

        static readonly (string key, string persona)[] Personas =
        {
            ("production", "생산 공장장. 급하고 실적을 먼저 따진다. 멈춤과 지연에 예민하다."),
            ("quality", "품질 공장장. 꼼꼼하고 차분하다. 고객이 받을 제품인지를 기준으로 말한다."),
            ("equipment", "설비 공장장. 현실적이고 담백하다. 설비가 상했는지, 정비가 필요한지를 말한다."),
            ("safety", "안전 공장장. 단호하다. 사람이 다칠 뻔했는지를 가장 먼저 본다."),
        };

        // ── LLM에 보내는 판 기록(손실은 숫자 대신 말로) ──────────────────
        [Serializable] class EventFact { public string title, picked, best, outcome, loss; public bool was_best; }
        [Serializable] class AgentFact { public string agent, name, followed; }
        [Serializable] class Facts
        {
            public string scenario_title, type, type_desc; public bool accident;
            public EventFact[] events; public string[] worst_events; public AgentFact[] agents;
        }

        static string Level(int n) => n == 0 ? "손실 없음" : n <= 5 ? "작은 손실" : n <= 10 ? "큰 손실" : "매우 큰 손실";

        static string Prompt(ScenarioDef def, Report rep)
        {
            var facts = new Facts
            {
                scenario_title = def.title, type = rep.type, type_desc = rep.typeDesc, accident = rep.accident,
                events = rep.run.events.Select(e => new EventFact
                {
                    title = e.ev.title, picked = e.chosen.label, best = e.best.label, was_best = e.chosen.best,
                    outcome = e.chosen.outcome, loss = Level(e.Total),
                }).ToArray(),
                worst_events = rep.run.events.Where(e => e.Total > 0).OrderByDescending(e => e.Total).Take(2).Select(e => e.ev.title).ToArray(),
                agents = rep.agents.Select(a => new AgentFact
                {
                    agent = a.agent, name = a.name,
                    followed = a.present == 0 ? "참여 안 함" : a.followed * 2 >= a.present ? "자주 따름" : a.followed > 0 ? "가끔 따름" : "따르지 않음",
                }).ToArray(),
            };
            var personas = string.Join("\n", Personas.Select(p => $"- {p.key}: {p.persona}"));
            return $@"너는 제철소 운영 시뮬레이션 게임 'STEEL MIND'의 결과 보고서를 쓰는 작가다.
아래 [판 기록]에 있는 사실만 써라. 기록에 없는 사건, 설비, 수치를 지어내지 마라.
숫자(퍼센트, 점수, 횟수)는 한 글자도 쓰지 마라. 숫자는 화면의 다른 칸에 이미 나온다.
플레이어는 이 공장장들을 이끄는 운영 책임자다. 플레이어를 '책임자님'이라고 부르고 존댓말로, 짧고 구체적으로 쓴다.

[판 기록]
{JsonUtility.ToJson(facts, true)}

[공장장 말투]
{personas}

아래 JSON 하나만 출력하라. 다른 글은 쓰지 마라.
{{""headline"": ""이번 판을 한 문장으로(40자 이내)"",
 ""turning_points"": [""worst_events 첫 사건에서 무엇을 골랐고 무엇을 잃었는지(90자 이내)"", ""worst_events 둘째 사건(있을 때만, 90자 이내)""],
 ""agent_comments"": {{""production"": ""생산 공장장이 이번 판을 두고 하는 한마디(60자 이내)"", ""quality"": ""…"", ""equipment"": ""…"", ""safety"": ""…""}},
 ""next_try"": ""다음 판에 바꿔 볼 선택 하나(60자 이내)""}}
worst_events가 비어 있으면 turning_points에는 가장 잘한 결정 하나를 짚어라.
agent_comments는 agents의 followed(그 공장장 추천을 따른 정도)와 말투에 맞춰 만족하거나 불만을 말한다.";
        }

        // ── Gemini 요청·응답 모양 ───────────────────────────────────────
        [Serializable] class Part { public string text; }
        [Serializable] class Content { public Part[] parts; }
        [Serializable] class GenConfig { public string responseMimeType = "application/json"; public float temperature = 0.4f; }
        [Serializable] class Request { public Content[] contents; public GenConfig generationConfig = new GenConfig(); }
        [Serializable] class Candidate { public Content content; }
        [Serializable] class Response { public Candidate[] candidates; }
        [Serializable] class Comments { public string production, quality, equipment, safety; }
        [Serializable] class Answer { public string headline, next_try; public string[] turning_points; public Comments agent_comments; }

        static bool Ok(string t, int max) => !string.IsNullOrWhiteSpace(t) && t.Trim().Length >= 4 && t.Trim().Length <= max && !t.Any(char.IsDigit);

        /// <summary>Gemini를 불러 문장 칸을 만든다. 실패하면 onFail(이유), 성공하면 onDone(칸별 검사 결과).</summary>
        public static IEnumerator Fetch(ScenarioDef def, Report rep, Action<LlmText> onDone, Action<string> onFail)
        {
            var key = Env("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(key)) { onFail("GEMINI_API_KEY 없음(.env 확인)"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            var body = JsonUtility.ToJson(new Request { contents = new[] { new Content { parts = new[] { new Part { text = Prompt(def, rep) } } } } });
            using (var req = new UnityWebRequest($"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-goog-api-key", key); // 키는 URL이 아니라 헤더로
                req.timeout = 40;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { onFail($"{req.responseCode} {req.error}"); yield break; }

                LlmText outText;
                try
                {
                    var res = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    var raw = res.candidates[0].content.parts[0].text;
                    int s = raw.IndexOf('{'), e2 = raw.LastIndexOf('}');   // ```json 울타리가 붙어 와도 본문만
                    var a = JsonUtility.FromJson<Answer>(raw.Substring(s, e2 - s + 1));
                    outText = new LlmText { provider = "gemini:" + Model, seconds = Time.realtimeSinceStartup - t0 };
                    if (Ok(a.headline, MaxHeadline)) outText.headline = a.headline.Trim();
                    if (Ok(a.next_try, MaxNext)) outText.nextTry = a.next_try.Trim();
                    var tp = (a.turning_points ?? new string[0]).Where(x => Ok(x, MaxMoment)).Select(x => x.Trim()).Take(2).ToList();
                    if (tp.Count > 0) outText.moments = tp;
                    var c = a.agent_comments ?? new Comments();
                    foreach (var (k, v) in new[] { ("production", c.production), ("quality", c.quality), ("equipment", c.equipment), ("safety", c.safety) })
                        if (Ok(v, MaxAgent)) outText.agents[k] = v.Trim();
                }
                catch (Exception e) { onFail("응답을 읽지 못함: " + e.Message); yield break; }
                onDone(outText);
            }
        }
    }
}
