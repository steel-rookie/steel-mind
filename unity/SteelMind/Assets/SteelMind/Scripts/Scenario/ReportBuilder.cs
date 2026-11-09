using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelMind.Scenario
{
    /// <summary>Agent 한 명의 회의 결과</summary>
    public class AgentVerdict
    {
        public string agent, name, mood, comment;
        public int followed, present;
    }

    /// <summary>결과 보고서에 들어가는 숫자와 기본 문장 (UnityEngine 의존 없음)</summary>
    public class Report
    {
        public RunResult run;
        public string pathCode, resultId, type, typeDesc, headline, suggestion;
        public int rank, totalPaths, topPercent, bestCount;
        public float[] radar;                 // 생산, 품질, 안전, 비용, 최선 선택 (0~1, 클수록 좋음)
        public List<string> moments = new List<string>();
        public List<AgentVerdict> agents = new List<AgentVerdict>();
        public bool accident;
    }

    /// <summary>
    /// 결과 보고서 계산. 틀과 숫자는 규칙으로 바로 계산하고, 문장은 기본 문장을 만든다(나중에 LLM 문장으로 바꿀 수 있게).
    /// </summary>
    public static class ReportBuilder
    {
        public static readonly string[] AgentOrder = { "production", "quality", "equipment", "safety" };
        public static readonly Dictionary<string, string> AgentName = new Dictionary<string, string>
            { { "production", "생산" }, { "quality", "품질" }, { "equipment", "설비" }, { "safety", "안전" } };
        public static readonly string[] Types = { "조율자", "납기 수호자", "원칙주의자", "장인", "정비반장" };
        static readonly Dictionary<string, string> TypeOf = new Dictionary<string, string>
            { { "production", "납기 수호자" }, { "safety", "원칙주의자" }, { "quality", "장인" }, { "equipment", "정비반장" } };
        static readonly Dictionary<string, string> TypeDesc = new Dictionary<string, string>
        {
            { "조율자", "모든 부서의 목소리를 듣고 손실 없이 균형을 맞췄습니다." },
            { "납기 수호자", "일정과 생산량을 가장 먼저 지켰습니다." },
            { "원칙주의자", "어떤 상황에서도 사람과 안전을 먼저 챙겼습니다." },
            { "장인", "제품 품질과 조건 안정을 먼저 지켰습니다." },
            { "정비반장", "설비가 상하지 않게 먼저 멈추고 살폈습니다." },
        };

        // 회의 반응: 시나리오에 적힌 반응이 있으면 그것을, 없으면 추천을 따랐는지로 만든다
        public static List<Reaction> ReactionsFor(ScenarioEvent ev, Choice c)
        {
            if (c.reactions != null && c.reactions.Length > 0) return c.reactions.ToList();
            var list = new List<Reaction>();
            foreach (var a in ev.agents)
            {
                bool agree = a.rec == c.id;
                string stance = agree ? "찬성" : a.agent == "safety" ? "경고" : "불만";
                list.Add(new Reaction { agent = a.agent, stance = stance, line = DefaultLine(a.agent, stance) });
            }
            return list;
        }

        static string DefaultLine(string agent, string stance)
        {
            switch (agent + ":" + stance)
            {
                case "production:찬성": return "좋습니다. 이대로면 출하 일정 맞춥니다.";
                case "production:불만": return "이러면 오늘 물량이 밀립니다. 일정도 봐 주세요.";
                case "quality:찬성": return "좋아요. 이러면 품질 편차가 잡힙니다.";
                case "quality:불만": return "이대로면 불량이 늘어요. 조건부터 잡아야 합니다.";
                case "equipment:찬성": return "잘하셨어요. 설비가 버텨 줍니다.";
                case "equipment:불만": return "설비에 무리가 갑니다. 나중에 더 오래 멈춰요.";
                case "safety:찬성": return "안전 확인했습니다. 좋은 판단입니다.";
                default: return "주변 인원 통제는 꼭 같이 해 주세요.";
            }
        }

        public static Report Build(ScenarioDef def, IList<int> picks)
        {
            int n = def.events.Length;
            var r = new Report { run = EfficiencyCalc.Compute(def, picks) };
            r.pathCode = string.Concat(picks.Select(p => (p + 1).ToString()));
            r.resultId = $"HR-{r.pathCode}-{r.run.finalEfficiency:000}";
            r.bestCount = r.run.events.Count(e => e.chosen.best);
            r.accident = r.run.events.Any(e => e.chosen.flag == "accident");

            // 모든 경로와 비교 (3^n)
            int total = 1; foreach (var ev in def.events) total *= ev.choices.Length;
            int better = 0; var maxLoss = new int[4];
            var pk = new int[n];
            for (int p = 0; p < total; p++)
            {
                int x = p;
                for (int i = n - 1; i >= 0; i--) { int m = def.events[i].choices.Length; pk[i] = x % m; x /= m; }
                var o = EfficiencyCalc.Compute(def, pk);
                if (o.finalEfficiency > r.run.finalEfficiency) better++;
                maxLoss[0] = Math.Max(maxLoss[0], o.totals.production); maxLoss[1] = Math.Max(maxLoss[1], o.totals.quality);
                maxLoss[2] = Math.Max(maxLoss[2], o.totals.safety); maxLoss[3] = Math.Max(maxLoss[3], o.totals.cost);
            }
            r.totalPaths = total; r.rank = better + 1;
            r.topPercent = Math.Max(1, (int)Math.Ceiling(100.0 * r.rank / total));
            var t = r.run.totals;
            float Score(int loss, int max) => max <= 0 ? 1f : 1f - (float)loss / max;
            r.radar = new[] { Score(t.production, maxLoss[0]), Score(t.quality, maxLoss[1]), Score(t.safety, maxLoss[2]), Score(t.cost, maxLoss[3]), (float)r.bestCount / n };

            // Agent별: 추천을 따른 횟수, 기분
            foreach (var ag in AgentOrder)
            {
                var v = new AgentVerdict { agent = ag, name = AgentName[ag] };
                int agree = 0, against = 0;
                foreach (var e in r.run.events)
                {
                    var line = e.ev.agents.FirstOrDefault(a => a.agent == ag);
                    if (line == null) continue;
                    v.present++;
                    if (line.rec == e.chosen.id) v.followed++;
                    foreach (var re in ReactionsFor(e.ev, e.chosen))
                        if (re.agent == ag) { if (re.stance == "찬성") agree++; else against++; }
                }
                v.mood = ag == "safety" && r.accident ? "불만" : agree > against ? "만족" : against > agree + 1 ? "불만" : "보통";
                v.comment = ag == "safety" && v.mood == "불만" && !r.accident ? "통제 구역과 과열 설비를 몇 번 놓쳤습니다. 다음엔 사람부터 챙겨 주세요." : Comment(ag, v.mood);
                r.agents.Add(v);
            }

            // 성향
            if (r.bestCount >= n - 1) r.type = "조율자";
            else
            {
                var top = r.agents.OrderByDescending(a => a.present == 0 ? 0 : (float)a.followed / a.present)
                                  .ThenBy(a => Array.IndexOf(new[] { "safety", "equipment", "quality", "production" }, a.agent)).First();
                r.type = TypeOf[top.agent];
            }
            r.typeDesc = TypeDesc[r.type];

            // 기본 문장
            int drop = r.run.drop;
            string body = drop == 0 ? "손실 없이 라인을 지켜 냈습니다."
                : r.accident ? "안전사고로 큰 손실을 입었습니다."
                : drop <= 15 ? "작은 손실로 라인을 지켜 냈습니다."
                : t.production <= Math.Min(t.quality, Math.Min(t.safety, t.cost)) ? "일정을 지켰으나 큰 손실을 입었습니다."
                : "위험은 막았지만 손실이 컸습니다.";
            r.headline = "책임자님, " + body;
            foreach (var e in r.run.events.Where(e => e.Total > 0).OrderByDescending(e => e.Total).Take(2))
                r.moments.Add($"{e.ev.title} · '{e.chosen.label}' → {e.chosen.outcome}");
            if (r.moments.Count == 0) r.moments.Add("모든 사건에서 손실이 가장 적은 선택을 했습니다.");
            var cats = new[] { ("안전", t.safety), ("생산", t.production), ("품질", t.quality), ("비용", t.cost) };
            var worst = cats.OrderByDescending(c => c.Item2).First();
            r.suggestion = drop == 0 ? "지금처럼 감속·점검으로 위험을 줄이면서 라인을 살리십시오."
                : r.accident || worst.Item1 == "안전" ? "다음에는 생산 압박이 있어도 사람이 있는 구역과 달아오른 설비부터 안전하게 만드십시오."
                : worst.Item1 == "생산" ? "다음에는 무조건 멈추기보다 감속·점검으로 라인을 살려 보십시오."
                : worst.Item1 == "품질" ? "다음에는 온도와 조건을 먼저 안정시켜 품질을 지켜 보십시오."
                : "다음에는 급정지 대신 감속과 예비 설비로 재가동 비용을 줄여 보십시오.";
            return r;
        }

        static string Lower(string s) => string.IsNullOrEmpty(s) ? s : s;

        static string Comment(string agent, string mood)
        {
            switch (agent + ":" + mood)
            {
                case "production:만족": return "멈추지 않고 달려서 납기를 지켰으니 다행입니다.";
                case "production:불만": return "자꾸 세우고 늦추니 출하 일정이 빠듯합니다. 다음엔 물량도 봐 주세요.";
                case "production:보통": return "일정은 겨우 맞췄습니다. 조금만 더 밀어붙였으면 좋았겠어요.";
                case "quality:만족": return "온도와 조건을 잡아 줘서 불량 없이 끝났습니다.";
                case "quality:불만": return "재질 편차와 두께 불균형이 너무 큽니다. 고객이 받을 제품을 생각해 주세요.";
                case "quality:보통": return "몇 번은 품질을 지켰지만 흔들린 코일도 있었습니다.";
                case "equipment:만족": return "설비를 아껴 줘서 롤과 펌프가 잘 버텼습니다.";
                case "equipment:불만": return "설비가 달아오르고 부담이 컸습니다. 정비와 점검에 더 신경 써 주세요.";
                case "equipment:보통": return "급한 불은 껐지만 점검이 필요한 곳이 남았습니다.";
                case "safety:만족": return "사람이 다칠 뻔한 순간을 모두 막았습니다.";
                case "safety:불만": return "안전사고가 났습니다. 사람이 먼저라는 원칙을 잊지 마세요.";
                default: return "사고는 막았지만 위험한 순간이 있었습니다.";
            }
        }
    }
}
