using System.Collections.Generic;
using UnityEngine;

namespace SteelMind
{
    /// <summary>
    /// 압연 라인 화면 상태. 게임 엔진은 이 두 가지만 부르면 된다.
    ///   SetRunning(false)  → 열연 강판과 그 빛을 끈다(라인 정지)
    ///   SetWarning("finishing_mill", true) → 그 설비를 빨갛게 깜빡인다
    /// 설비 id: reheating_furnace, descaler, roughing_mill, finishing_mill, runout_table, coiler
    /// </summary>
    public class RollingLine : MonoBehaviour
    {
        [System.Serializable]
        public class Equipment
        {
            public string id;
            public string label;
            public SpriteRenderer renderer;
            public Sprite normal;
            public Sprite warn;
            [HideInInspector] public bool warning;
        }

        public List<Equipment> equipment = new List<Equipment>();
        [Tooltip("라인이 돌 때만 보이는 것(열연 강판, 강판 빛)")]
        public List<GameObject> runningOnly = new List<GameObject>();
        public float blinkSeconds = 0.4f;
        public bool running = true;

        public void SetRunning(bool on)
        {
            running = on;
            foreach (var go in runningOnly)
                if (go) go.SetActive(on);
        }

        public void SetWarning(string id, bool on)
        {
            var e = equipment.Find(x => x.id == id);
            if (e == null) { Debug.LogWarning($"[RollingLine] 없는 설비 id: {id}"); return; }
            e.warning = on;
            if (!on && e.renderer) e.renderer.sprite = e.normal;
        }

        public void ClearWarnings()
        {
            foreach (var e in equipment) SetWarning(e.id, false);
        }

        void Start() => SetRunning(running);

        void Update()
        {
            bool phase = Mathf.FloorToInt(Time.time / blinkSeconds) % 2 == 0;
            foreach (var e in equipment)
                if (e.warning && e.renderer) e.renderer.sprite = phase ? e.warn : e.normal;
        }
    }
}
