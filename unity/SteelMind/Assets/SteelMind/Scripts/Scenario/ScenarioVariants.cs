using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SteelMind.Scenario
{
    // Resources/Scenarios/hot_rolling_variants.json 구조. UnityEngine에 의존하지 않는다.
    [Serializable]
    public class ChoicePatch
    {
        public string choice, outcome;
        public bool dropFlag;                // 이 상황에서는 뒤 사건으로 번지지 않는다
        public Loss loss;                    // 비어 있으면 원래 손실을 쓴다
    }

    [Serializable]
    public class EventVariant
    {
        public string id, desc, bestChoice;  // bestChoice가 있으면 이 상황의 최선이 바뀐다
        public ChoicePatch[] patches;
    }

    [Serializable] public class EventVariants { public string eventId; public EventVariant[] variants; }
    [Serializable] public class VariantSet { public EventVariants[] events; }

    /// <summary>
    /// 같은 사건이라도 판마다 현장 상황을 바꾼다. 원래 상황과 변형 중 하나를 무작위로 고르고,
    /// 바로 전 판과 같은 상황은 고르지 않는다. 결과는 여전히 규칙표로 계산하므로
    /// 같은 상황에서 같은 선택을 하면 같은 결과가 나온다.
    /// </summary>
    public class ScenarioVariants
    {
        readonly Dictionary<string, EventVariant[]> byEvent;

        public ScenarioVariants(VariantSet set)
        {
            byEvent = (set?.events ?? new EventVariants[0]).ToDictionary(e => e.eventId, e => e.variants);
        }

        /// <param name="last">사건별로 전 판에 나온 상황 번호. 장면이 바뀌어도 유지되는 곳에 둔다.</param>
        /// <param name="random">0 이상 n 미만의 정수를 돌려주는 함수</param>
        public ScenarioEvent Apply(ScenarioEvent ev, IDictionary<string, int> last, Func<int, int> random)
        {
            if (!byEvent.TryGetValue(ev.id, out var variants) || variants.Length == 0) return ev;
            var options = Enumerable.Range(0, variants.Length + 1).ToList();    // 0은 원래 상황
            if (last.TryGetValue(ev.id, out int previous)) options.Remove(previous);
            int picked = options[random(options.Count)];
            last[ev.id] = picked;
            return picked == 0 ? ev : Make(ev, variants[picked - 1]);
        }

        /// <summary>원본을 건드리지 않고 상황이 반영된 사건을 만든다.</summary>
        public static ScenarioEvent Make(ScenarioEvent ev, EventVariant variant)
        {
            var copy = Copy(ev);
            copy.desc = variant.desc;
            copy.choices = ev.choices.Select(Copy).ToArray();

            if (!string.IsNullOrEmpty(variant.bestChoice))
            {
                var oldBest = copy.choices.First(c => c.best);
                var newBest = copy.choices.First(c => c.id == variant.bestChoice);
                if (oldBest != newBest)
                {
                    // 이 상황의 올바른 판단에 역량 점수가 따라가게 한다.
                    (oldBest.skills, newBest.skills) = (newBest.skills, oldBest.skills);
                    oldBest.best = false;
                    newBest.best = true;
                }
            }

            foreach (var patch in variant.patches ?? new ChoicePatch[0])
            {
                var choice = copy.choices.First(c => c.id == patch.choice);
                if (!string.IsNullOrEmpty(patch.outcome)) choice.outcome = patch.outcome;
                if (patch.loss != null && patch.loss.Total > 0) choice.loss = patch.loss;
                if (patch.dropFlag) choice.flag = null;
            }
            return copy;
        }

        static T Copy<T>(T source) where T : new()
        {
            var copy = new T();
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(copy, field.GetValue(source));
            return copy;
        }
    }
}
