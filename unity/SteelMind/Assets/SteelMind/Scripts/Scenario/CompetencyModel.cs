using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelMind.Scenario
{
    // 역량별 추정치: 받은 점수의 합과 시험 횟수. UnityEngine에 의존하지 않는다.
    public class CompetencyState
    {
        public string id;
        public int sum, count;
        public float Mean => count == 0 ? 0f : (float)sum / count;
        // 아직 덜 시험했을수록 크다 (적응형 검사의 "정보가 부족한 곳")
        public float Uncertainty => 1f / (1f + count);
    }

    public class Pick
    {
        public ScenarioEvent ev;
        public string target;      // 노린 역량
        public float score;
        public string reason;      // 화면·보고서에 보여 줄 판단 이유 (기본 문장)
    }

    public class CompetencyModel
    {
        public static readonly string[] Ids = { "aware", "safety", "judge", "tech", "team" };
        public readonly Dictionary<string, CompetencyState> states = Ids.ToDictionary(i => i, i => new CompetencyState { id = i });
        public readonly List<Pick> picks = new List<Pick>();
        readonly Dictionary<string, string> names;

        public CompetencyModel(ScenarioDef def)
        {
            names = (def.competencies ?? new Competency[0]).ToDictionary(c => c.id, c => c.name);
        }

        public string Name(string id) => names.TryGetValue(id, out var n) ? n : id;

        // 선택 반영: 선택지 점수가 있는 역량 + 이 사건이 시험하는 역량(점수 0이어도 "확인함"으로 센다)
        public Dictionary<string, int> Apply(ScenarioEvent ev, Choice c)
        {
            var delta = new Dictionary<string, int>();
            foreach (var id in Ids)
            {
                int v = c.skills != null ? c.skills.Get(id) : 0;
                bool probed = ev.probes != null && Array.IndexOf(ev.probes, id) >= 0;
                if (v == 0 && !probed) continue;
                states[id].sum += v; states[id].count++;
                delta[id] = v;
            }
            return delta;
        }

        // 약점일수록, 덜 시험했을수록 높은 점수. 순수 적응형 검사는 불확실성만 보지만
        // 교육 목적상 약점도 함께 노린다 (설계 선택).
        public float Need(string id)
        {
            var s = states[id];
            float weakness = Math.Max(0f, 1.5f - s.Mean) / 3.5f;   // 평균 +1.5 이상이면 0, -2면 1
            return weakness + s.Uncertainty;
        }

        public Pick PickNext(IEnumerable<ScenarioEvent> pool)
        {
            Pick best = null;
            foreach (var ev in pool)
            {
                if (ev.probes == null || ev.probes.Length == 0) continue;
                float score = ev.probes.Sum(Need);
                if (best == null || score > best.score + 1e-4f)
                {
                    string target = ev.probes.OrderByDescending(Need).First();
                    best = new Pick { ev = ev, score = score, target = target };
                }
            }
            if (best != null)
            {
                var s = states[best.target];
                string why = s.count == 0 ? "아직 한 번도 확인하지 못했습니다"
                    : s.Mean < 0 ? $"지금까지 점수가 낮습니다 (평균 {s.Mean:+0.0;-0.0;0})"
                    : $"확인한 횟수가 적어 판단이 이릅니다 ({s.count}회)";
                best.reason = $"{Name(best.target)}: {why}";
                picks.Add(best);
            }
            return best;
        }

        public List<CompetencyState> Strengths() =>
            Ids.Select(i => states[i]).Where(s => s.count > 0 && s.Mean >= 1f).OrderByDescending(s => s.Mean).ToList();

        public List<CompetencyState> Weaknesses() =>
            Ids.Select(i => states[i]).Where(s => s.count > 0 && s.Mean < 0.5f).OrderBy(s => s.Mean).ToList();

        // 레이더용 0~1 (-2 → 0, +2 → 1)
        public float Radar(string id) => states[id].count == 0 ? 0.5f : Math.Max(0f, Math.Min(1f, (states[id].Mean + 2f) / 4f));
    }
}
