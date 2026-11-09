using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SteelMind.Scenario
{
    /// <summary>
    /// 열간압연 테스트 시나리오. Rolling 장면이 열리면 스스로 생긴다(장면 파일은 건드리지 않음).
    /// 사건 → 에이전트 의견 → 선택지 3개 → 라인 반응 → 마지막에 결과 화면. 끄려면 Enabled = false.
    /// </summary>
    public class HotRollingScenario : MonoBehaviour
    {
        public static bool Enabled = true;   // 설정값(플레이 상태 아님)
        const string SceneName = "Rolling";
        const string DataPath = "Scenarios/hot_rolling";

        static readonly Color WindowBg = Hex("#221F1D", 0.96f), Body = Hex("#F2F2F2"), Dim = Hex("#B9B9BE"),
            Accent = Hex("#6CC3B0"), LevelCol = Hex("#F0A04B"), Red = Hex("#FF6B6B");
        static readonly Dictionary<string, (string name, string color)> Agents = new Dictionary<string, (string, string)>
        {
            { "production", ("생산", "#7AA7FF") }, { "quality", ("품질", "#C99BFF") },
            { "equipment", ("설비", "#F0A04B") }, { "safety", ("안전", "#6FCF97") },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn(s);

        static void TrySpawn(Scene s)
        {
            if (!Enabled || s.name != SceneName || FindFirstObjectByType<HotRollingScenario>()) return;
            new GameObject("HotRollingScenario").AddComponent<HotRollingScenario>();
        }

        // ── 상태 (인스턴스에만 둔다) ───────────────────────────
        ScenarioDef def;
        readonly List<int> picks = new List<int>();
        readonly HashSet<string> flags = new HashSet<string>();
        int current = -1;
        bool deciding, showingResult;
        Coroutine timeline, resume;

        RollingLine line;
        LineProcess process;
        PlayerMove player;
        Rigidbody2D body;
        readonly List<Behaviour> pausedDuringDecision = new List<Behaviour>();

        Font font;
        Canvas canvas;
        Text status;
        RectTransform window;

        void Start()
        {
            var json = Resources.Load<TextAsset>(DataPath);
            if (!json) { Debug.LogError($"[Scenario] Resources/{DataPath}.json 이 없습니다."); enabled = false; return; }
            def = JsonUtility.FromJson<ScenarioDef>(json.text);
            line = FindFirstObjectByType<RollingLine>();
            process = FindFirstObjectByType<LineProcess>(FindObjectsInactive.Include);
            player = FindFirstObjectByType<PlayerMove>();
            if (player) body = player.GetComponent<Rigidbody2D>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "AppleGothic", "Noto Sans CJK KR", "Arial" }, 20);
            BuildCanvas();
            Restart();
        }

        void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }

        // ── 진행 ───────────────────────────────────────────────
        void Restart()
        {
            StopAllCoroutines();
            timeline = resume = null;
            picks.Clear(); flags.Clear();
            current = -1; deciding = showingResult = false;
            ClearWindow();
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            if (process) process.speed = 1f;
            SetPlayerFrozen(false);
            timeline = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            int n = def.events.Length;
            SetStatus($"{def.title} 테스트 · 사건 1/{n} 대기 중 · WASD로 이동");
            for (int i = 0; i < n; i++)
            {
                yield return new WaitForSeconds(def.events[i].delay);
                ShowEvent(i);
                while (deciding) yield return null;
                var chosen = def.events[i].choices[picks[i]];
                if (i + 1 < n) SetStatus($"조치 진행 중: {chosen.label} · 사건 {i + 2}/{n} 대기 중");
            }
            SetStatus("교대 근무를 마무리하는 중입니다");
            yield return new WaitForSeconds(def.resultDelay);
            ShowResult();
        }

        void ShowEvent(int i)
        {
            current = i; deciding = true;
            var ev = def.events[i];
            if (line && !string.IsNullOrEmpty(ev.warn)) line.SetWarning(ev.warn, true);
            SetPlayerFrozen(true);
            BuildEventWindow(ev, i);
        }

        void Choose(int k)
        {
            if (!deciding || current < 0) return;
            var ev = def.events[current];
            if (k < 0 || k >= ev.choices.Length) return;
            var c = ev.choices[k];
            picks.Add(k);
            if (!string.IsNullOrEmpty(c.flag)) flags.Add(c.flag);
            deciding = false;
            ClearWindow();
            if (line && !c.keepWarning && !string.IsNullOrEmpty(ev.warn)) line.SetWarning(ev.warn, false);
            if (resume != null) { StopCoroutine(resume); resume = null; }
            if (line)
            {
                if (c.lineSpeed <= 0f) line.SetRunning(false);
                else { line.SetRunning(true); if (process) process.speed = c.lineSpeed; }
            }
            if (c.resumeAfter > 0f) resume = StartCoroutine(ResumeAfter(c.resumeAfter));
            SetPlayerFrozen(false);
        }

        IEnumerator ResumeAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            if (line) line.SetRunning(true);
            if (process) process.speed = 1f;
            resume = null;
        }

        void SetPlayerFrozen(bool frozen)
        {
            if (player)
            {
                player.enabled = !frozen;
                if (body) body.linearVelocity = Vector2.zero;
            }
            // 결정 중에는 숫자키 공정 이동·Tab 전체 보기를 막는다(1·2·3 선택과 겹침)
            if (frozen)
            {
                pausedDuringDecision.Clear();
                foreach (var b in FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None)) if (b.enabled) { b.enabled = false; pausedDuringDecision.Add(b); }
                foreach (var b in FindObjectsByType<OverviewCamera>(FindObjectsSortMode.None)) if (b.enabled) { b.enabled = false; pausedDuringDecision.Add(b); }
            }
            else
            {
                foreach (var b in pausedDuringDecision) if (b) b.enabled = true;
                pausedDuringDecision.Clear();
            }
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (deciding)
            {
                if (k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame) Choose(0);
                else if (k.digit2Key.wasPressedThisFrame || k.numpad2Key.wasPressedThisFrame) Choose(1);
                else if (k.digit3Key.wasPressedThisFrame || k.numpad3Key.wasPressedThisFrame) Choose(2);
            }
            else if (showingResult && k.rKey.wasPressedThisFrame) Restart();
        }

        // ── 화면 ───────────────────────────────────────────────
        void BuildCanvas()
        {
            var go = new GameObject("ScenarioUI");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (!FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }
            // 상단 상태 줄
            var bar = new GameObject("Status", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(go.transform, false);
            bar.anchorMin = new Vector2(0.5f, 1); bar.anchorMax = new Vector2(0.5f, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -12);
            bar.gameObject.AddComponent<Image>().color = Hex("#221F1D", 0.85f);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18, 18, 8, 8); h.childControlWidth = h.childControlHeight = true;
            var f = bar.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = AddText(bar, "", 18, Body);
            status.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        void SetStatus(string s) { if (status) status.text = s; }

        void ClearWindow()
        {
            if (window) Destroy(window.gameObject);
            window = null;
            showingResult = false;
        }

        RectTransform NewWindow()
        {
            ClearWindow();
            var rt = new GameObject("Window", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(80, 0); rt.offsetMax = new Vector2(-80, 0);
            rt.gameObject.AddComponent<Image>().color = WindowBg;
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 22, 22); v.spacing = 8;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window = rt;
            return rt;
        }

        Text AddText(Transform parent, string s, int size, Color c, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = anchor;
            t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            t.text = s;
            return t;
        }

        RectTransform Row(Transform parent, float spacing = 12)
        {
            var rt = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return rt;
        }

        static string C(string hex, string s) => $"<color={hex}>{s}</color>";
        static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        void BuildEventWindow(ScenarioEvent ev, int index)
        {
            var w = NewWindow();
            int n = def.events.Length;
            AddText(w, $"{C(ColorHex(LevelCol), ev.level)} · 사건 {index + 1}/{n}", 16, Dim, FontStyle.Bold);
            AddText(w, $"경보: {ev.title}", 26, Red, FontStyle.Bold);
            AddText(w, ev.desc, 18, Body);
            if (EfficiencyCalc.CarryApplies(ev, flags)) AddText(w, ev.carry.note, 16, Red);
            Spacer(w, 4);
            foreach (var a in ev.agents)
            {
                var (name, col) = Agents.TryGetValue(a.agent, out var v) ? v : (a.agent, "#F2F2F2");
                AddText(w, $"{C(col, $"<b>{name}</b>")}  {a.say}  {C(ColorHex(Dim), a.why)}", 17, Body);
            }
            AddText(w, $"{C(ColorHex(Accent), "<b>공장장</b>")}  {ev.chief}", 17, Body);
            Spacer(w, 6);
            var row = Row(w);
            for (int k = 0; k < ev.choices.Length; k++)
            {
                var c = ev.choices[k];
                var recs = new List<string>();
                foreach (var a in ev.agents) if (a.rec == c.id && Agents.TryGetValue(a.agent, out var v)) recs.Add(v.name);
                var btn = new GameObject("Choice", typeof(RectTransform)).GetComponent<RectTransform>();
                btn.SetParent(row, false);
                btn.gameObject.AddComponent<Image>().color = Hex("#2E2A27");
                var b = btn.gameObject.AddComponent<Button>();
                var colors = b.colors; colors.highlightedColor = Hex("#3A4A46"); colors.pressedColor = Hex("#45605A"); b.colors = colors;
                var v2 = btn.gameObject.AddComponent<VerticalLayoutGroup>();
                v2.padding = new RectOffset(16, 16, 12, 12); v2.spacing = 4;
                v2.childControlWidth = v2.childControlHeight = true; v2.childForceExpandWidth = true; v2.childForceExpandHeight = false;
                var le = btn.gameObject.AddComponent<LayoutElement>(); le.minWidth = 0; le.preferredWidth = 10; le.flexibleWidth = 1;
                AddText(btn, $"{k + 1}. {c.label}", 20, Body, FontStyle.Bold);
                AddText(btn, c.detail, 16, Dim);
                AddText(btn, recs.Count > 0 ? "추천: " + string.Join("·", recs) : "추천한 에이전트 없음", 15, Accent);
                int captured = k;
                b.onClick.AddListener(() => Choose(captured));
            }
            AddText(w, "버튼을 누르거나 키보드 1·2·3으로 고르세요.", 15, Dim);
            Relayout();
        }

        void ShowResult()
        {
            var r = EfficiencyCalc.Compute(def, picks);
            var w = NewWindow();
            showingResult = true;
            AddText(w, $"교대 근무 종료 · {def.title} 결과", 18, Dim, FontStyle.Bold);
            AddText(w, $"운영 효율  100%  →  {C(ColorHex(LossColor(r.drop)), r.finalEfficiency + "%")}", 34, Body, FontStyle.Bold);
            AddText(w, r.drop == 0 ? "모든 사건에서 최선의 선택을 해 효율을 100% 지켰습니다." : $"최선의 대응과 비교해 효율이 {r.drop}% 떨어졌습니다.", 18, Body);
            Gauge(w, r.finalEfficiency / 100f);
            AddText(w, $"항목별 손실   생산 −{r.totals.production}   품질 −{r.totals.quality}   안전 −{r.totals.safety}   비용 −{r.totals.cost}", 17, Dim);
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
                card.SetParent(w, false);
                card.gameObject.AddComponent<Image>().color = Hex("#2B2725");
                var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(16, 16, 10, 10); v.spacing = 4;
                v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                var head = Row(card, 8);
                AddText(head, $"{i + 1}. {e.ev.title}", 19, Body, FontStyle.Bold);
                AddText(head, e.Total > 0 ? $"효율 −{e.Total}%" : "손실 없음", 19, LossColor(e.Total), FontStyle.Bold, TextAnchor.UpperRight);
                AddText(card, $"내 선택: {e.chosen.label}   {(e.chosen.best ? C(ColorHex(Accent), "최선의 선택") : C(ColorHex(Dim), $"최선: {e.best.label}"))}", 17, Body);
                AddText(card, e.chosen.outcome, 16, Dim);
                if (e.carried) AddText(card, $"{e.ev.carry.note} (효율 −{e.carryLoss}%)", 16, Red);
                var parts = new List<string>();
                if (e.loss.production != 0) parts.Add($"생산 −{e.loss.production}");
                if (e.loss.quality != 0) parts.Add($"품질 −{e.loss.quality}");
                if (e.loss.safety != 0) parts.Add($"안전 −{e.loss.safety}");
                if (e.loss.cost != 0) parts.Add($"비용 −{e.loss.cost}");
                if (parts.Count > 0) AddText(card, string.Join(" · ", parts), 16, LossColor(e.Total));
            }
            var row = Row(w);
            var btn = new GameObject("Restart", typeof(RectTransform)).GetComponent<RectTransform>();
            btn.SetParent(row, false);
            btn.gameObject.AddComponent<Image>().color = Accent;
            var b = btn.gameObject.AddComponent<Button>();
            var v3 = btn.gameObject.AddComponent<VerticalLayoutGroup>();
            v3.padding = new RectOffset(12, 12, 10, 10); v3.childControlWidth = v3.childControlHeight = true;
            AddText(btn, "다시 하기 (R)", 20, Hex("#062520"), FontStyle.Bold, TextAnchor.MiddleCenter);
            b.onClick.AddListener(Restart);
            Relayout();
        }

        void Gauge(Transform parent, float ratio)
        {
            var bg = new GameObject("Gauge", typeof(RectTransform)).GetComponent<RectTransform>();
            bg.SetParent(parent, false);
            bg.gameObject.AddComponent<Image>().color = Hex("#3A3532");
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
            fill.SetParent(bg, false);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = Accent;
        }

        void Spacer(Transform parent, float h)
        {
            var s = new GameObject("Space", typeof(RectTransform));
            s.transform.SetParent(parent, false);
            s.AddComponent<LayoutElement>().preferredHeight = h;
        }

        void Relayout()
        {
            Canvas.ForceUpdateCanvases();
            if (window) LayoutRebuilder.ForceRebuildLayoutImmediate(window);
        }

        static Color LossColor(int loss) => loss == 0 ? Hex("#6FCF97") : loss <= 15 ? Hex("#E8C25A") : Hex("#FF6B6B");

        static Color Hex(string hex, float a = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = a;
            return c;
        }
    }
}
