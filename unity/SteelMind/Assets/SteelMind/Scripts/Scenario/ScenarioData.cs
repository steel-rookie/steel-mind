using System;

namespace SteelMind.Scenario
{
    // 시나리오 JSON 구조 (Resources/Scenarios/*.json). UnityEngine에 의존하지 않는다.
    [Serializable]
    public class Loss
    {
        public int production, quality, safety, cost;
        public int Total => production + quality + safety + cost;
        public void Add(Loss o)
        {
            if (o == null) return;
            production += o.production; quality += o.quality; safety += o.safety; cost += o.cost;
        }
    }

    [Serializable] public class AgentLine { public string agent, say, why, rec; }

    // 결정 뒤 Agent 회의 한 줄 (stance: 찬성·불만·경고)
    [Serializable] public class Reaction { public string agent, stance, line; }

    [Serializable]
    public class Choice
    {
        public string id, label, detail, outcome, flag;
        public bool best, keepWarning;
        public float lineSpeed, resumeAfter;
        public Loss loss = new Loss();
        public Reaction[] reactions;
    }

    [Serializable]
    public class Carry
    {
        public string ifFlag, note;
        public Loss loss = new Loss();
    }

    [Serializable]
    public class ScenarioEvent
    {
        public string id, level, warn, title, desc, chief;
        public float delay;
        public AgentLine[] agents;
        public Choice[] choices;
        public Carry carry;
    }

    [Serializable]
    public class ScenarioDef
    {
        public string id, title;
        public float resultDelay;
        public ScenarioEvent[] events;
    }
}
