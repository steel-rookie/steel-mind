using System.Collections;
using System.Collections.Generic;
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
    /// 사건 → 에이전트 의견과 선택지 3개 → 결정 → Agent 회의 → (다음 사건) → 결과·AI 진단. 끄려면 Enabled = false.
    /// </summary>
    public class HotRollingScenario : MonoBehaviour
    {
        public static bool Enabled = true;   // 설정값(플레이 상태 아님)
        const string SceneName = "Rolling";
        const string DataPath = "Scenarios/hot_rolling";
        const float MeetingLineDelay = 0.7f, MeetingAutoClose = 4f;

        // 화면 색 (참고 화면: 어두운 남색 판, 얇은 테두리, 노란 강조)
        static readonly Color Bg = Hex("#0E1320", 0.97f), Panel = Hex("#151C2B"), Panel2 = Hex("#1B2436"), Border = Hex("#2C3850"),
            Body = Hex("#E6E9F0"), Dim = Hex("#8D97AD"), Yellow = Hex("#F0C040"), Cyan = Hex("#5FC3E4"),
            Accent = Hex("#6CC3B0"), LevelCol = Hex("#F0A04B"), Red = Hex("#FF6B6B");
        static readonly Dictionary<string, (string name, string color)> Agents = new Dictionary<string, (string, string)>
        {
            { "production", ("생산", "#7AA7FF") }, { "quality", ("품질", "#C99BFF") },
            { "equipment", ("설비", "#F0A04B") }, { "safety", ("안전", "#6FCF97") }, { "chief", ("공장장", "#F0C040") },
        };
        static readonly Dictionary<string, string> StanceColor = new Dictionary<string, string>
        {
            { "찬성", "#6FCF97" }, { "불만", "#FF8A5B" }, { "경고", "#F0C040" },
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
        bool deciding, meeting, meetingSkip, showingResult;
        Coroutine resume;

        RollingLine line;
        LineProcess process;
        PlayerMove player;
        Rigidbody2D body;
        readonly List<Behaviour> pausedDuringDecision = new List<Behaviour>();
        readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();

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
            resume = null;
            picks.Clear(); flags.Clear();
            current = -1; deciding = meeting = showingResult = false;
            ClearWindow();
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            if (process) process.speed = 1f;
            SetPlayerFrozen(false);
            StartCoroutine(Run());
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
                yield return Meeting(def.events[i], def.events[i].choices[picks[i]]);
                SetPlayerFrozen(false);
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
        }

        IEnumerator ResumeAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            if (line) line.SetRunning(true);
            if (process) process.speed = 1f;
            resume = null;
        }

        // 결정 뒤 Agent 회의: 말풍선이 하나씩 뜬다. Enter·Space로 넘기거나 잠시 뒤 자동으로 닫힌다.
        IEnumerator Meeting(ScenarioEvent ev, Choice c)
        {
            if (c.reactions == null || c.reactions.Length == 0) yield break;
            meeting = true; meetingSkip = false;
            var w = NewWindow();
            var head = Row(w, 10);
            Tag(head, "Agent 회의", Cyan);
            AddText(head, $"내 결정: {C(ColorHex(Yellow), c.label)}", 20, Body, FontStyle.Bold).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            AddText(w, "결정이 나자 Agent들이 각자의 입장으로 반응합니다.", 15, Dim);
            Relayout();
            foreach (var r in c.reactions)
            {
                var (name, col) = Agents.TryGetValue(r.agent, out var v) ? v : (r.agent, "#E6E9F0");
                var sc = StanceColor.TryGetValue(r.stance, out var s) ? s : "#E6E9F0";
                Speech(w, r.agent, $"{C(col, $"<b>{name}</b>")} · {C(sc, $"<b>{r.stance}</b>")}", r.line, null);
                Relayout();
                float t = 0;
                while (t < MeetingLineDelay && !meetingSkip) { t += Time.deltaTime; yield return null; }
            }
            AddText(w, "Enter·Space로 계속 · 잠시 뒤 자동으로 넘어갑니다", 14, Dim);
            Relayout();
            meetingSkip = false;
            float wait = 0;
            while (wait < MeetingAutoClose && !meetingSkip) { wait += Time.deltaTime; yield return null; }
            ClearWindow();
            meeting = false;
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
                if (pausedDuringDecision.Count > 0) return;
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
            else if (meeting)
            {
                if (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) meetingSkip = true;
            }
            else if (showingResult && k.rKey.wasPressedThisFrame) Restart();
        }

        // ── 화면 공통 ──────────────────────────────────────────
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
            var bar = new GameObject("Status", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(go.transform, false);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -12);
            Box(bar.gameObject, Bg, Border);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18, 18, 8, 8); h.childControlWidth = h.childControlHeight = true;
            var f = bar.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = AddText(bar, "", 17, Body);
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
            Box(rt.gameObject, Bg, Border);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(22, 22, 18, 18); v.spacing = 8;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window = rt;
            return rt;
        }

        static void Box(GameObject go, Color fill, Color border)
        {
            go.AddComponent<Image>().color = fill;
            var o = go.AddComponent<Outline>(); o.effectColor = border; o.effectDistance = new Vector2(2, -2);
        }

        RectTransform Panel_(Transform parent, Color fill, Color border, int pad = 12, float spacing = 4)
        {
            var rt = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Box(rt.gameObject, fill, border);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(pad, pad, pad - 2, pad - 2); v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
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

        RectTransform Row(Transform parent, float spacing = 12, bool expand = true)
        {
            var rt = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = expand; h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.UpperLeft;
            return rt;
        }

        void Tag(Transform parent, string s, Color c)
        {
            var p = Panel_(parent, Panel2, c, 6, 0);
            p.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 3, 3);
            var t = AddText(p, s, 15, c, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var le = p.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 0;
        }

        Sprite Portrait(string agent)
        {
            if (portraits.TryGetValue(agent, out var sp)) return sp;
            var bytes = Resources.Load<TextAsset>($"Portraits/{agent}");
            if (!bytes) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(bytes.bytes);
            tex.filterMode = FilterMode.Point;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            portraits[agent] = sp;
            return sp;
        }

        // 초상화 + 말풍선 한 줄
        void Speech(Transform parent, string agent, string title, string line1, string line2)
        {
            var row = Row(parent, 10, false);
            var pic = new GameObject("Portrait", typeof(RectTransform));
            pic.transform.SetParent(row, false);
            var img = pic.AddComponent<Image>(); img.sprite = Portrait(agent); img.preserveAspect = true;
            var frame = pic.AddComponent<Outline>(); frame.effectColor = Border; frame.effectDistance = new Vector2(2, -2);
            var le = pic.AddComponent<LayoutElement>(); le.preferredWidth = le.minWidth = 58; le.preferredHeight = le.minHeight = 48;
            var bubble = Panel_(row, Panel, Border, 10, 2);
            bubble.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            AddText(bubble, title, 15, Body);
            AddText(bubble, line1, 18, Body);
            if (!string.IsNullOrEmpty(line2)) AddText(bubble, line2, 15, Dim);
        }

        static string C(string hex, string s) => $"<color={hex}>{s}</color>";
        static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ── 사건 창 ────────────────────────────────────────────
        void BuildEventWindow(ScenarioEvent ev, int index)
        {
            var w = NewWindow();
            int n = def.events.Length;
            var head = Row(w, 10, false);
            Tag(head, ev.level, LevelCol);
            Tag(head, $"사건 {index + 1}/{n}", Dim);
            var title = AddText(head, $"경보: {ev.title}", 22, Red, FontStyle.Bold);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var demo = AddText(head, "모든 값은 데모용 가상 데이터", 13, Dim, FontStyle.Normal, TextAnchor.MiddleRight);
            demo.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddText(w, ev.desc, 18, Body);
            if (EfficiencyCalc.CarryApplies(ev, flags)) AddText(w, ev.carry.note, 16, Red);
            AddText(w, "AI 에이전트 의견", 14, Cyan, FontStyle.Bold);
            foreach (var a in ev.agents)
            {
                var (name, col) = Agents.TryGetValue(a.agent, out var v) ? v : (a.agent, "#E6E9F0");
                Speech(w, a.agent, C(col, $"<b>{name}</b>"), a.say, a.why);
            }
            Speech(w, "chief", C(ColorHex(Yellow), "<b>공장장 · 쟁점</b>"), ev.chief, null);
            AddText(w, "선택지", 14, Cyan, FontStyle.Bold);
            var row = Row(w);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            for (int k = 0; k < ev.choices.Length; k++)
            {
                var c = ev.choices[k];
                var recs = new List<string>();
                foreach (var a in ev.agents) if (a.rec == c.id && Agents.TryGetValue(a.agent, out var v)) recs.Add(v.name);
                var btn = Panel_(row, Panel, Border, 12, 3);
                var le = btn.gameObject.AddComponent<LayoutElement>(); le.minWidth = 0; le.preferredWidth = 10; le.flexibleWidth = 1;
                var b = btn.gameObject.AddComponent<Button>();
                b.targetGraphic = btn.GetComponent<Image>();
                var colors = b.colors; colors.highlightedColor = Hex("#2A3A55"); colors.pressedColor = Hex("#34507A"); b.colors = colors;
                AddText(btn, $"{C(ColorHex(Yellow), $"{k + 1}.")} {c.label}", 19, Body, FontStyle.Bold);
                AddText(btn, c.detail, 15, Dim);
                AddText(btn, recs.Count > 0 ? "추천: " + string.Join("·", recs) : "추천한 에이전트 없음", 14, Accent);
                int captured = k;
                b.onClick.AddListener(() => Choose(captured));
            }
            AddText(w, "버튼을 누르거나 키보드 1·2·3으로 고르세요.", 14, Dim);
            Relayout();
        }

        // ── 결과 창 ────────────────────────────────────────────
        void ShowResult()
        {
            var r = EfficiencyCalc.Compute(def, picks);
            var w = NewWindow();
            showingResult = true;
            var head = Row(w, 10, false);
            Tag(head, "결과", Cyan);
            AddText(head, $"교대 근무 종료 · {def.title} 결과", 20, Body, FontStyle.Bold).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var demo = AddText(head, "모든 값은 데모용 가상 데이터", 13, Dim, FontStyle.Normal, TextAnchor.MiddleRight);
            demo.horizontalOverflow = HorizontalWrapMode.Overflow;

            var cols = Row(w, 14);
            var left = Panel_(cols, Panel, Border, 14, 8);
            var right = Panel_(cols, Panel, Border, 14, 8);
            foreach (var p in new[] { left, right }) { var le = p.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 10; le.flexibleWidth = 1; }

            // 왼쪽: 효율, 게이지, 항목별 손실, AI 진단
            AddText(left, $"운영 효율  100%  →  {C(ColorHex(LossColor(r.drop)), r.finalEfficiency + "%")}", 32, Body, FontStyle.Bold);
            AddText(left, r.drop == 0 ? "모든 사건에서 최선의 선택을 해 효율을 100% 지켰습니다." : $"최선의 대응과 비교해 효율이 {r.drop}% 떨어졌습니다.", 16, Body);
            Gauge(left, r.finalEfficiency / 100f, Accent, 14);
            AddText(left, $"항목별 손실   생산 −{r.totals.production}   품질 −{r.totals.quality}   안전 −{r.totals.safety}   비용 −{r.totals.cost}", 15, Dim);
            MetricBar(left, "생산", r.totals.production); MetricBar(left, "품질", r.totals.quality);
            MetricBar(left, "안전", r.totals.safety); MetricBar(left, "비용", r.totals.cost);
            AddText(left, "AI 진단", 14, Cyan, FontStyle.Bold);
            Speech(left, "chief", C(ColorHex(Yellow), "<b>공장장 AI</b>") + "  " + C(ColorHex(Dim), "기본 진단 · LLM 연결 전"), Diagnose(r), null);

            // 오른쪽: 사건별 카드, 다시 하기
            AddText(right, "사건별 기록", 14, Cyan, FontStyle.Bold);
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                var card = Panel_(right, Panel2, Border, 12, 3);
                var ch = Row(card, 8);
                AddText(ch, $"{i + 1}. {e.ev.title}", 18, Body, FontStyle.Bold);
                AddText(ch, e.Total > 0 ? $"효율 −{e.Total}%" : "손실 없음", 18, LossColor(e.Total), FontStyle.Bold, TextAnchor.UpperRight);
                AddText(card, $"내 선택: {e.chosen.label}   {(e.chosen.best ? C(ColorHex(Accent), "최선의 선택") : C(ColorHex(Dim), $"최선: {e.best.label}"))}", 16, Body);
                AddText(card, e.chosen.outcome, 15, Dim);
                if (e.carried) AddText(card, $"{e.ev.carry.note} (효율 −{e.carryLoss}%)", 15, Red);
                var parts = new List<string>();
                if (e.loss.production != 0) parts.Add($"생산 −{e.loss.production}");
                if (e.loss.quality != 0) parts.Add($"품질 −{e.loss.quality}");
                if (e.loss.safety != 0) parts.Add($"안전 −{e.loss.safety}");
                if (e.loss.cost != 0) parts.Add($"비용 −{e.loss.cost}");
                if (parts.Count > 0) AddText(card, string.Join(" · ", parts), 15, LossColor(e.Total));
            }
            var btn = Panel_(right, Yellow, Yellow, 10, 0);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = btn.GetComponent<Image>();
            AddText(btn, "다시 하기 (R)", 19, Hex("#1A1408"), FontStyle.Bold, TextAnchor.MiddleCenter);
            b.onClick.AddListener(Restart);
            Relayout();
        }

        // 결과로 만든 기본 진단 문장 (나중에 LLM 진단 로그로 바꾼다)
        static string Diagnose(RunResult r)
        {
            if (r.drop == 0)
                return "두 사건 모두 감속으로 위험을 먼저 줄이면서 라인을 살렸습니다. 생산·품질·안전을 함께 지킨 판단입니다.";
            var cats = new[] { ("생산", r.totals.production), ("품질", r.totals.quality), ("안전", r.totals.safety), ("비용", r.totals.cost) };
            var worst = cats[0];
            foreach (var c in cats) if (c.Item2 > worst.Item2) worst = c;
            var sb = new System.Text.StringBuilder();
            sb.Append($"가장 크게 잃은 항목은 {worst.Item1}(−{worst.Item2})입니다. ");
            foreach (var e in r.events)
                if (!e.chosen.best) sb.Append($"'{e.ev.title}'에서는 '{e.chosen.label}' 대신 '{e.best.label}'이 손실이 가장 적었습니다. ");
            foreach (var e in r.events)
                if (e.carried) { sb.Append("앞 사건에서 무리한 선택이 다음 사건의 위험을 키웠습니다. "); break; }
            if (r.totals.safety > 0) sb.Append("사람이 있는 구역과 달아오른 설비는 생산보다 먼저 위험을 줄여야 합니다.");
            else if (r.totals.production > 0) sb.Append("무조건 멈추기보다 감속·점검으로 라인을 살리는 길도 있습니다.");
            return sb.ToString().Trim();
        }

        void MetricBar(Transform parent, string label, int loss)
        {
            var row = Row(parent, 10, false);
            var t = AddText(row, label, 15, Body);
            var tl = t.gameObject.AddComponent<LayoutElement>(); tl.preferredWidth = tl.minWidth = 44;
            var bar = Gauge(row, Mathf.Clamp01((100 - loss) / 100f), LossColor(loss), 10);
            var bl = bar.gameObject.GetComponent<LayoutElement>(); bl.flexibleWidth = 1; bl.preferredWidth = 10;
            var v = AddText(row, loss > 0 ? $"−{loss}" : "0", 15, LossColor(loss), FontStyle.Bold, TextAnchor.UpperRight);
            var vl = v.gameObject.AddComponent<LayoutElement>(); vl.preferredWidth = vl.minWidth = 44;
        }

        RectTransform Gauge(Transform parent, float ratio, Color color, float height)
        {
            var bg = new GameObject("Gauge", typeof(RectTransform)).GetComponent<RectTransform>();
            bg.SetParent(parent, false);
            bg.gameObject.AddComponent<Image>().color = Hex("#2A3346");
            var le = bg.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = le.minHeight = height;
            var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
            fill.SetParent(bg, false);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = color;
            return bg;
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
