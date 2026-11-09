using System;
using System.Collections.Generic;

namespace SteelMind.Scenario
{
    /// <summary>사건 하나의 결과</summary>
    public class EventResult
    {
        public ScenarioEvent ev;
        public Choice chosen, best;
        public Loss loss = new Loss();      // 선택 손실 + 번진 손실
        public bool carried;                // 앞선 선택이 번졌는가
        public int carryLoss;
        public int Total => loss.Total;
    }

    /// <summary>한 판의 결과</summary>
    public class RunResult
    {
        public List<EventResult> events = new List<EventResult>();
        public Loss totals = new Loss();
        public int finalEfficiency, drop;
    }

    /// <summary>
    /// 효율 계산 (UnityEngine 의존 없음).
    /// 효율은 100에서 시작하고, 사건마다 고른 선택지의 항목별 손실(최선과의 차이)을 빼며,
    /// 앞선 선택의 플래그가 다음 사건의 carry 조건과 맞으면 그 손실을 더한다. 최종 = max(0, 100 - 합).
    /// </summary>
    public static class EfficiencyCalc
    {
        public const int Start = 100;

        public static bool CarryApplies(ScenarioEvent ev, ICollection<string> flags) =>
            ev.carry != null && !string.IsNullOrEmpty(ev.carry.ifFlag) && flags.Contains(ev.carry.ifFlag);

        public static Choice BestOf(ScenarioEvent ev)
        {
            foreach (var c in ev.choices) if (c.best) return c;
            return ev.choices[0];
        }

        /// <param name="picks">사건 순서대로 고른 선택지 번호(0부터)</param>
        public static RunResult Compute(ScenarioDef def, IList<int> picks)
        {
            if (def == null || def.events == null) throw new ArgumentException("시나리오가 비어 있습니다.");
            var r = new RunResult();
            var flags = new HashSet<string>();
            for (int i = 0; i < def.events.Length && i < picks.Count; i++)
            {
                var ev = def.events[i];
                var c = ev.choices[picks[i]];
                var er = new EventResult { ev = ev, chosen = c, best = BestOf(ev) };
                er.loss.Add(c.loss);
                if (CarryApplies(ev, flags))
                {
                    er.carried = true;
                    er.carryLoss = ev.carry.loss.Total;
                    er.loss.Add(ev.carry.loss);
                }
                if (!string.IsNullOrEmpty(c.flag)) flags.Add(c.flag);
                r.events.Add(er);
                r.totals.Add(er.loss);
            }
            r.finalEfficiency = Math.Max(0, Start - r.totals.Total);
            r.drop = Start - r.finalEfficiency;
            return r;
        }
    }
}
