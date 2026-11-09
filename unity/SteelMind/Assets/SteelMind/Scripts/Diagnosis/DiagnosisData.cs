using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SteelMind.Diagnosis
{
    // Resources/Scenarios/plant_faults.json 구조. UnityEngine에 의존하지 않는다.
    [Serializable] public class Reading { public string name, value, normal; public bool abnormal; }
    [Serializable] public class Cause { public string name, verdict, evidence; }

    [Serializable]
    public class Fault
    {
        public string id, scene, warn, location, title, symptom, prevention;
        public Reading[] readings;
        public Cause[] causes;
        public string[] steps;

        public Cause Likely => causes.FirstOrDefault(c => c.verdict == "유력");
    }

    [Serializable]
    public class FaultSet
    {
        public float firstDelay, nextDelayMin, nextDelayMax;
        public Fault[] faults;
    }

    /// <summary>
    /// 다음 문제를 고른다. 공정 전체의 설비 중 하나를 무작위로 고르므로 같은 설비가 연달아 나올 수 있다.
    /// 다만 그 설비에서 바로 전에 나온 문제는 다시 고르지 않아 같은 내용이 반복되지 않는다.
    /// </summary>
    public class FaultPicker
    {
        readonly Fault[] faults;
        readonly string[] places;
        readonly Func<int, int> random;
        readonly Dictionary<string, string> lastAt = new Dictionary<string, string>();

        /// <param name="random">0 이상 n 미만의 정수를 돌려주는 함수</param>
        public FaultPicker(Fault[] faults, Func<int, int> random)
        {
            this.faults = faults;
            this.random = random;
            places = faults.Select(f => f.warn).Distinct().ToArray();
        }

        public Fault Next()
        {
            string place = places[random(places.Length)];
            lastAt.TryGetValue(place, out var last);
            var options = faults.Where(f => f.warn == place && f.id != last).ToArray();
            if (options.Length == 0) options = faults.Where(f => f.warn == place).ToArray();
            var chosen = options[random(options.Length)];
            lastAt[place] = chosen.id;
            return chosen;
        }
    }

    /// <summary>화면에 보여 줄 분석. AI 답이거나, AI 없이 문제 카드로 만든 기본 분석이다.</summary>
    [Serializable]
    public class Analysis
    {
        public string source, reason, summary, root_cause, prevention;
        public string[] analysis, steps;

        public bool FromAi => source == "gemini";

        public string SourceLabel(string model)
        {
            if (FromAi) return $"AI 분석 · {model}";
            switch (reason)
            {
                case "no_key": return "기본 분석 · AI 키 없음";
                case "invalid": return "기본 분석 · AI 답변이 근거와 달라 제외";
                default: return "기본 분석 · AI 응답 실패";
            }
        }

        public static Analysis Basic(Fault f, string reason)
        {
            var likely = f.Likely;
            var lines = new List<string>();
            if (likely != null) lines.Add($"가장 유력한 원인은 {likely.name}입니다. {likely.evidence}");
            lines.AddRange(f.causes.Where(c => c != likely).Select(c => $"{c.name}({c.verdict}): {c.evidence}"));
            return new Analysis
            {
                source = "fallback",
                reason = reason,
                summary = f.symptom,
                root_cause = likely?.name,
                analysis = lines.ToArray(),
                steps = f.steps,
                prevention = f.prevention,
            };
        }
    }

    /// <summary>AI에게 줄 지시문과 AI 답 검사. 원인과 수치는 문제 카드를 벗어나면 안 된다.</summary>
    public static class DiagnosisPrompt
    {
        const string Rules = @"당신은 제철소 공정의 진단 설명 AI입니다. 아래 JSON 문제 카드를 보고 답합니다.
규칙:
- 카드에 있는 사실만 사용합니다. 카드에 없는 수치, 원인, 절차를 만들지 않습니다.
- root_cause는 verdict가 ""유력""인 원인의 name을 글자 그대로 씁니다.
- analysis는 2~4문장의 배열입니다. 어떤 측정값 때문에 유력 원인으로 좁혀졌는지, 나머지 후보는 왜 가능성이 낮거나 더 확인해야 하는지 설명합니다.
- steps는 카드의 steps와 같은 개수, 같은 순서로 쓰고 각 단계에 왜 필요한지 짧게 덧붙입니다.
- 공장 신입도 이해할 쉬운 한국어로, 존댓말로 씁니다.
- 다음 키만 있는 JSON 하나로 답합니다: summary, root_cause, analysis, steps, prevention

문제 카드:
";

        public static string Build(string cardJson) => Rules + cardJson;

        static readonly Regex Number = new Regex(@"\d[\d,]*(?:\.\d+)?");

        static HashSet<string> NumbersIn(string text) =>
            new HashSet<string>(Number.Matches(text).Cast<Match>().Select(m => m.Value.Replace(",", "")));

        /// <summary>AI 답이 카드를 지켰는지 본다. 원인이 다르거나, 단계 수가 다르거나, 없는 수치를 쓰면 false.</summary>
        public static bool IsValid(Analysis a, Fault f, string cardJson)
        {
            if (a == null || a.root_cause != f.Likely?.name) return false;
            if (string.IsNullOrWhiteSpace(a.summary) || string.IsNullOrWhiteSpace(a.prevention)) return false;
            if (a.analysis == null || a.analysis.Length == 0 || a.analysis.Any(string.IsNullOrWhiteSpace)) return false;
            if (a.steps == null || a.steps.Length != f.steps.Length || a.steps.Any(string.IsNullOrWhiteSpace)) return false;

            var allowed = NumbersIn(cardJson);
            string written = string.Join(" ", new[] { a.summary, a.prevention }.Concat(a.analysis).Concat(a.steps));
            return NumbersIn(written).All(n => allowed.Contains(n) || (int.TryParse(n, out int small) && small < 10));
        }
    }
}
