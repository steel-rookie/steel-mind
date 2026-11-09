using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SteelMind.Scenario
{
    /// <summary>
    /// 돌발상황 대처 진단에서 AI가 맡는 일. 점수(손실·효율)는 계속 EfficiencyCalc가 규칙으로 계산한다.
    ///   F6 선택 직후 공장장 회의(LLM) · F7 지금까지의 선택에서 약점 판단 · F8 약점을 노리는 다음 사건과 맞춤 상황 문장
    ///   F9 진단 보고서에 쓸 규칙 진단(강점·약점, 다른 걸 골랐다면, 추천 교육·사수)
    /// 어느 호출이 실패해도 규칙 결과(대본 반응, 규칙 1순위 사건, 기본 문장)로 게임은 계속된다.
    /// </summary>
    public static class AiDirector
    {
        public static readonly string[] Metrics = { "production", "quality", "safety", "cost" };
        public static readonly Dictionary<string, string> MetricName = new Dictionary<string, string>
            { { "production", "생산" }, { "quality", "품질" }, { "safety", "안전" }, { "cost", "비용·설비" } };
        static readonly string[] Tie = { "safety", "quality", "cost", "production" };
        static readonly Dictionary<string, string> Persona = new Dictionary<string, string>
        {
            { "production", "생산 공장장. 급하고 실적을 먼저 따진다. 멈춤과 지연에 예민하다." },
            { "quality", "품질 공장장. 꼼꼼하고 차분하다. 고객이 받을 제품인지를 기준으로 말한다." },
            { "equipment", "설비 공장장. 현실적이고 담백하다. 설비가 상했는지, 정비가 필요한지를 말한다." },
            { "safety", "안전 공장장. 단호하다. 사람이 다칠 뻔했는지를 가장 먼저 본다." },
        };
        const string Common = "너는 제철소 운영 시뮬레이션 'STEEL MIND'의 AI다. 이 시뮬레이션은 신입 운영자의 돌발상황 대처 역량을 진단하고 약점에 맞는 교육으로 잇는다.\n" +
            "주어진 기록에 있는 사실만 써라. 기록에 없는 사건, 설비, 수치를 지어내지 마라. 숫자(퍼센트, 점수, 횟수)는 한 글자도 쓰지 마라.\n" +
            "플레이어는 운영 책임자다. '책임자님'이라고 부르고 존댓말로, 짧고 구체적으로 쓴다. JSON 하나만 출력하라.";

        public static bool On => ReportLlm.HasKey;

        // ── 추천 교육·사수 (시연용 예시 데이터. 실제 매뉴얼·인사 정보가 아님) ──
        public class Training { public string id, metric, title, note; }
        public class Mentor { public string id, name, career, note; public string[] strengths; }
        public static readonly Training[] Trainings =
        {
            new Training { id = "T-SAF-1", metric = "safety", title = "통제 구역 출입 확인과 설비 잠금 절차", note = "사람이 들어간 구역은 감속·정지 후 확인하고 재가동한다." },
            new Training { id = "T-SAF-2", metric = "safety", title = "과열 설비 주변 인원 통제 원칙", note = "생산 압박이 있어도 달아오른 설비 근처 인원부터 뺀다." },
            new Training { id = "T-PRD-1", metric = "production", title = "감속 운전으로 라인 살리기", note = "무조건 정지 대신 감속·점검으로 위험을 줄이며 생산을 잇는다." },
            new Training { id = "T-PRD-2", metric = "production", title = "정지 판단 기준과 재가동 비용", note = "정지가 꼭 필요한 신호와 감속으로 충분한 신호를 구분한다." },
            new Training { id = "T-QUA-1", metric = "quality", title = "가열·압연 온도 조건 관리", note = "온도가 모자라거나 흔들리면 재질과 두께가 고르지 않다." },
            new Training { id = "T-QUA-2", metric = "quality", title = "표면 결함(스케일) 원인과 예방", note = "수압이 낮은 채로 보내면 스케일이 강판에 눌려 붙는다." },
            new Training { id = "T-EQP-1", metric = "cost", title = "예비 설비 전환 절차", note = "예비 펌프·가이드 조정처럼 라인을 세우지 않고 넘기는 방법." },
            new Training { id = "T-EQP-2", metric = "cost", title = "롤·가이드 마모 점검과 보정", note = "쏠림·장력 이상은 다음 패스 전에 보정해 설비 손상을 막는다." },
        };
        public static readonly Mentor[] Mentors =
        {
            new Mentor { id = "M-1", name = "예시 선배 A", career = "열연 운전 · 안전 담당", strengths = new[] { "safety", "production" }, note = "위험 신호에서 감속으로 사람과 라인을 같이 지키는 판단에 강함" },
            new Mentor { id = "M-2", name = "예시 선배 B", career = "품질 보증", strengths = new[] { "quality" }, note = "온도·표면 조건을 먼저 잡아 불량을 막는 판단에 강함" },
            new Mentor { id = "M-3", name = "예시 선배 C", career = "설비 정비", strengths = new[] { "cost", "quality" }, note = "라인을 세우지 않고 예비 설비·보정으로 넘기는 판단에 강함" },
            new Mentor { id = "M-4", name = "예시 선배 D", career = "생산 계획", strengths = new[] { "production", "cost" }, note = "정지가 꼭 필요한 순간과 아닌 순간을 가르는 판단에 강함" },
        };

        // ── 규칙: 손실과 희생 비율 ───────────────────────────────────────
        static int L(Loss l, string k) => l == null ? 0 : k == "production" ? l.production : k == "quality" ? l.quality : k == "safety" ? l.safety : l.cost;
        static int MaxLoss(ScenarioEvent ev, string k) => ev.choices.Max(c => L(c.loss, k));
        static string Level(int n) => n == 0 ? "손실 없음" : n <= 5 ? "작은 손실" : n <= 10 ? "큰 손실" : "매우 큰 손실";

        public class Sac { public int stake, lost; public float? rate; }

        /// <summary>[F7 규칙] 지표별 희생 비율 = 내준 손실 / 그 사건들에서 내줄 수 있었던 최대 손실. done개 사건까지 본다.</summary>
        public static Dictionary<string, Sac> Sacrifice(ScenarioDef def, IList<int> picks, int done)
        {
            var o = Metrics.ToDictionary(k => k, k => new Sac());
            for (int i = 0; i < done && i < picks.Count; i++)
            {
                var ev = def.events[i]; var c = ev.choices[picks[i]];
                foreach (var k in Metrics) { o[k].stake += MaxLoss(ev, k); o[k].lost += L(c.loss, k); }
            }
            foreach (var k in Metrics) o[k].rate = o[k].stake > 0 ? (float)o[k].lost / o[k].stake : (float?)null;
            return o;
        }

        public static string WeakOf(Dictionary<string, Sac> s)
        {
            var c = Metrics.Where(k => s[k].rate > 0).OrderByDescending(k => s[k].rate).ThenBy(k => Array.IndexOf(Tie, k)).ToList();
            return c.Count > 0 ? c[0] : null;
        }

        static string[] Tests(ScenarioEvent ev) => Metrics.Where(k => MaxLoss(ev, k) >= 5).ToArray();
        static string Words(Sac s) => s.rate == null ? "아직 안 겪음" : s.rate == 0 ? "내준 적 없음" : s.rate < 0.4f ? "가끔 내줌" : "자주 내줌";

        /// <summary>[F8 규칙] 남은 사건(from번째부터) 중 약점과 아직 덜 본 지표를 크게 가르는 사건 순으로 후보를 고른다.</summary>
        static List<int> Candidates(ScenarioDef def, Dictionary<string, Sac> s, int from)
        {
            float Score(ScenarioEvent ev) => Metrics.Sum(k => MaxLoss(ev, k) * ((s[k].rate ?? 0f) + 0.5f / (1f + s[k].stake / 10f)));
            return Enumerable.Range(from, def.events.Length - from).OrderByDescending(i => Score(def.events[i])).Take(2).ToList();
        }

        [Serializable] class HistItem { public string @event, picked, gave_up; public bool was_best; }
        static HistItem[] History(ScenarioDef def, IList<int> picks, int done) => Enumerable.Range(0, Math.Min(done, picks.Count)).Select(i =>
        {
            var ev = def.events[i]; var c = ev.choices[picks[i]];
            return new HistItem { @event = ev.title, picked = c.label, was_best = c.best,
                gave_up = string.Join(", ", Metrics.Where(k => L(c.loss, k) > 0).Select(k => $"{MetricName[k]} {Level(L(c.loss, k))}")) };
        }).ToArray();

        // ── F7·F8 다음 사건 ──────────────────────────────────────────────
        public class NextPick { public int index; public string situation, reason, pattern; public bool ai; public string note; }

        [Serializable] class CandItem { public string id, title, desc, tests; }
        [Serializable] class NextReq { public HistItem[] history; public string sacrifice; public CandItem[] candidates; }
        [Serializable] class NextAns { public string pattern, weak, next_event, reason, situation; }

        public static NextPick RulePick(ScenarioDef def, IList<int> picks, int slot)
        {
            var s = Sacrifice(def, picks, slot); var w = WeakOf(s);
            int idx = Candidates(def, s, slot)[0]; var ev = def.events[idx];
            return new NextPick { index = idx, ai = false, situation = ev.desc,
                reason = $"{string.Join("·", Tests(ev).Select(k => MetricName[k]))} 쪽이 선택에 따라 크게 갈리는 사건입니다.",
                pattern = w == null ? "아직 뚜렷한 약점이 없습니다." : $"{MetricName[w]} 쪽 손실이 가장 많습니다." };
        }

        /// <summary>slot번째에 낼 사건을 고른다. 규칙이 후보 두 개로 좁히고, AI가 고르며 상황 문장을 이 사람에 맞춘다.</summary>
        public static IEnumerator PickNext(ScenarioDef def, IList<int> picks, int slot, Action<NextPick> done)
        {
            var rule = RulePick(def, picks, slot);
            if (!On || slot >= def.events.Length - 1) { done(rule); yield break; } // 마지막 하나는 고를 것이 없다
            var s = Sacrifice(def, picks, slot);
            var cands = Candidates(def, s, slot);
            var req = new NextReq
            {
                history = History(def, picks, slot),
                sacrifice = string.Join(", ", Metrics.Select(k => $"{MetricName[k]}: {Words(s[k])}")),
                candidates = cands.Select(i => def.events[i]).Select(e => new CandItem { id = e.id, title = e.title, desc = e.desc, tests = string.Join("·", Tests(e).Select(k => MetricName[k])) }).ToArray(),
            };
            var prompt = $@"{Common}

[지금까지의 선택과 지표별 희생 정도, 다음 사건 후보] (tests: 그 사건에서 선택에 따라 크게 갈리는 지표)
{JsonUtility.ToJson(req, true)}

너는 이 사람의 대처 역량을 가장 적은 사건으로 정확히 진단하려 한다.
1) pattern: 지금까지의 선택에서 보이는 약점을 한 문장으로(50자 이내). 선택이 없거나 모두 최선이면 '아직 뚜렷한 약점이 없습니다'류로.
2) next_event: 후보 중 그 약점을 가장 잘 확인할 사건 하나의 id. 약점이 없으면 아직 덜 본 지표를 시험하는 사건.
3) reason: 왜 이 사건을 골랐는지(60자 이내).
4) situation: 고른 사건의 desc를 바탕으로, 이 사람의 약점을 시험하도록 압박 조건을 하나 더한 상황 문장(120자 이내). 설비와 징후는 바꾸지 마라.
   situation은 상황만 묘사한다. 어떤 행동이 옳은지, 무엇을 해야 하는지(멈춰야 한다, 구해야 한다 등)는 절대 암시하지 마라. 판단은 플레이어가 한다.
{{""pattern"": ""…"", ""weak"": ""production|quality|safety|cost|none"", ""next_event"": ""후보 id"", ""reason"": ""…"", ""situation"": ""…""}}";
            NextAns ans = null; string err = null;
            yield return ReportLlm.CallJson(prompt, j => { try { ans = JsonUtility.FromJson<NextAns>(j); } catch (Exception e) { err = e.Message; } }, e => err = e);
            int idx = ans == null ? -1 : cands.FindIndex(i => def.events[i].id == ans.next_event);
            if (idx >= 0) idx = cands[idx];
            if (idx < 0) { rule.note = err ?? $"후보 밖 사건: {ans?.next_event}"; Debug.LogWarning("[AiDirector] 다음 사건을 규칙으로 고름: " + rule.note); done(rule); yield break; }
            var ev = def.events[idx];
            done(new NextPick
            {
                index = idx, ai = true,
                situation = ReportLlm.Ok(ans.situation, 170) ? ans.situation.Trim() : ev.desc,
                reason = ReportLlm.Ok(ans.reason, 90) ? ans.reason.Trim() : rule.reason,
                pattern = ReportLlm.Ok(ans.pattern, 70) ? ans.pattern.Trim() : rule.pattern,
            });
        }

        // ── F6 AI 공장장 회의 ────────────────────────────────────────────
        [Serializable] class View { public string agent, say, why, rec; }
        [Serializable] class MeetReq { public string @event, situation, picked, outcome, loss; public View[] agent_views; public HistItem[] history; }
        [Serializable] class MeetAns { public Reaction[] reactions; }

        /// <summary>선택 직후 회의. 공장장마다 AI 대사를 받고, 빠졌거나 검사를 통과하지 못한 줄은 대본 반응으로 채운다. done(줄 목록, AI 줄 수).</summary>
        public static IEnumerator Meeting(ScenarioDef def, IList<int> picks, int slot, string situation, Action<List<Reaction>, int> done)
        {
            var ev = def.events[slot]; var c = ev.choices[picks[slot]];
            var script = ReportBuilder.ReactionsFor(ev, c);
            if (!On) { done(script, 0); yield break; }
            var req = new MeetReq
            {
                @event = ev.title, situation = situation, picked = c.label, outcome = c.outcome,
                loss = string.Join(", ", Metrics.Select(k => $"{MetricName[k]} {Level(L(c.loss, k))}")),
                agent_views = ev.agents.Select(a => new View { agent = a.agent, say = a.say, why = a.why, rec = ev.choices.FirstOrDefault(x => x.id == a.rec)?.label }).ToArray(),
                history = History(def, picks, slot),
            };
            var keys = ev.agents.Select(a => a.agent).ToList();
            var prompt = $@"{Common}

[이번 사건과 결정]
{JsonUtility.ToJson(req, true)}

[공장장 말투]
{string.Join("\n", keys.Select(k => $"- {k}: {Persona[k]}"))}

결정 직후 열리는 공장장 회의다. 공장장마다 한 줄씩, 자기 부서 입장에서 이번 결정에 반응하라.
- 자기가 추천한 행동(agent_views의 rec)을 골랐으면 대체로 찬성, 아니면 불만이나 경고. 단 결과(outcome)와 손실을 보고 판단하라.
- history에 같은 실수가 반복됐으면 그 점을 짚어라. line은 60자 이내.
{{""reactions"": [{{""agent"": ""{keys[0]}"", ""stance"": ""찬성|불만|경고 중 하나"", ""line"": ""…""}}]}}
reactions에는 다음 공장장만, 이 순서로 넣어라: {string.Join(", ", keys)}";
            MeetAns ans = null; string err = null;
            yield return ReportLlm.CallJson(prompt, j => { try { ans = JsonUtility.FromJson<MeetAns>(j); } catch (Exception e) { err = e.Message; } }, e => err = e);
            var outList = new List<Reaction>(); int aiCount = 0;
            foreach (var s in script)
            {
                var a = ans?.reactions?.FirstOrDefault(r => r.agent == s.agent);
                if (a != null && (a.stance == "찬성" || a.stance == "불만" || a.stance == "경고") && ReportLlm.Ok(a.line, 80)) { outList.Add(new Reaction { agent = a.agent, stance = a.stance, line = a.line.Trim() }); aiCount++; }
                else outList.Add(s);
            }
            if (aiCount == 0) Debug.LogWarning("[AiDirector] 회의를 대본으로 진행: " + (err ?? "쓸 만한 줄 없음"));
            done(outList, aiCount);
        }

        // ── F9 규칙 진단(보고서 기본 문장) ───────────────────────────────
        public class Diagnosis
        {
            public string weakKey, strength, weakness, whatIf, trainingReason, mentorReason;
            public List<string> training = new List<string>();
            public string mentor;
            public Dictionary<string, Sac> sac;
        }

        public static Diagnosis RuleDiagnosis(ScenarioDef def, IList<int> picks, Report rep)
        {
            var s = Sacrifice(def, picks, picks.Count); var w = WeakOf(s);
            var d = new Diagnosis { weakKey = w, sac = s };
            var strong = Metrics.Where(k => s[k].stake > 0 && s[k].rate == 0).Select(k => MetricName[k]).ToList();
            d.strength = strong.Count > 0 ? $"{string.Join("·", strong)} 쪽은 한 번도 내주지 않았습니다." : "모든 지표에서 한 번 이상 손실이 있었습니다.";
            var worstW = w == null ? null : rep.run.events.Where(e => L(e.chosen.loss, w) > 0).OrderByDescending(e => L(e.chosen.loss, w)).FirstOrDefault();
            d.weakness = worstW == null ? "뚜렷한 약점이 없습니다." : $"{MetricName[w]} 쪽 손실이 가장 컸습니다. 「{worstW.ev.title}」에서 '{worstW.chosen.label}'을 골랐습니다.";
            var worst = rep.run.events.Where(e => e.Total > 0).OrderByDescending(e => e.Total).FirstOrDefault();
            d.whatIf = worst == null ? "모든 사건에서 최선을 골랐습니다." : $"「{worst.ev.title}」에서 '{worst.best.label}'을 골랐다면 효율 {worst.Total}%를 지켰습니다.";
            var key = w ?? "safety";
            d.training = Trainings.Where(t => t.metric == key).Take(2).Select(t => t.id).ToList();
            d.trainingReason = $"{MetricName[key]} 쪽 판단을 보강하는 교육입니다.";
            d.mentor = (Mentors.FirstOrDefault(m => m.strengths.Contains(key)) ?? Mentors[0]).id;
            d.mentorReason = $"{MetricName[key]} 쪽을 강점으로 가진 선배입니다.";
            return d;
        }

        public static string SacrificeWords(Dictionary<string, Sac> s) => string.Join(", ", Metrics.Select(k => $"{MetricName[k]}: {Words(s[k])}"));
    }
}
