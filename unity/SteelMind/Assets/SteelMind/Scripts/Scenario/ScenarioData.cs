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

    // 대처 역량 점수 (선택지마다, -3~+2). aware 상황 인식 · safety 안전 우선 · judge 위기 판단 · tech 설비·공정 이해 · team 소통·조율
    [Serializable]
    public class Skills
    {
        public int aware, safety, judge, tech, team;
        public int Get(string id)
        {
            switch (id) { case "aware": return aware; case "safety": return safety; case "judge": return judge; case "tech": return tech; case "team": return team; default: return 0; }
        }
    }

    // 역량 정의와 결과 보고서 문장 (근거: NCS 능력단위, 비기술적 역량 연구)
    [Serializable]
    public class Competency
    {
        public string id, name, desc, strong, weak, training, mentor, role, ncs;
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
        public Skills skills = new Skills();
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
        public bool pressure;            // 생산·시간 재촉이 있는 사건 (기수 보고서의 재촉 분석용 표시. 점수에는 안 씀)
        public string[] probes;          // 이 사건이 주로 시험하는 역량
        public AgentLine[] agents;
        public Choice[] choices;
        public Carry carry;
    }

    [Serializable]
    public class ScenarioDef
    {
        public string id, title;
        public float resultDelay;
        public int baseCount = 3, totalCount = 6;   // 기본 사건 수, 한 판의 전체 사건 수
        public Competency[] competencies;
        public ScenarioEvent[] events;               // 기본 사건 (앞의 baseCount개를 먼저 낸다)
        public ScenarioEvent[] surprises;            // AI가 약점을 보고 골라 내는 돌발상황
        public ScenarioEvent[] retrains;             // 맞춤 재훈련 전용 (역량마다 2개, 조건이 다른 처음 보는 상황)
    }
}
