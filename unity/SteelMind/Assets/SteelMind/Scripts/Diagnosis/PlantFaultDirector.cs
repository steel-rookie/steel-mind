using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SteelMind.Scenario;

namespace SteelMind.Diagnosis
{
    /// <summary>
    /// 전체 공정 돌발 문제. 열간압연 시나리오의 보고서를 닫으면 시작해, 네 공정의 설비에서 무작위로 문제를 낸다.
    /// 설비에 느낌표가 뜨고, 다가가거나 느낌표를 누르면 측정값·원인 분석·해결 순서를 보여 준다.
    /// 설명은 GeminiClient가 쓰고, 쓸 수 없거나 근거를 벗어나면 문제 카드로 만든 기본 분석을 보여 준다.
    /// 유니티 에디터에서는 F9로 시나리오 없이 바로 시작할 수 있다. 문제 데이터는 Resources/Scenarios/plant_faults.json에 있다.
    /// </summary>
    public class PlantFaultDirector : MonoBehaviour
    {
        /// <summary>이 거리(타일) 안에 오면 도착으로 본다. 앞을 다른 설비가 막아 4.6칸까지만 다가갈 수 있는 설비가 있다.</summary>
        public const float ArriveTiles = 5f;
        static readonly bool Enabled = true;
        const string DataPath = "Scenarios/plant_faults";
        const float MarkerLiftTiles = 3f, MinAlertSeconds = 1.5f, AskSeconds = 15f;
        // 느낌표가 위쪽 공정 버튼과 오른쪽 위 미니맵을 가리지 않게 하는 간격
        const float MarkerTopGap = 110f, MinimapZoneWidth = 400f, MinimapZoneGap = 300f;

        static readonly Color Panel = Hex("#1D1F24", 0.97f), Card = Hex("#2A2C32"), Body = Hex("#EEF0F3"), Dim = Hex("#A3A8B2"),
            Teal = Hex("#6CC3B0"), Orange = Hex("#F0A04B"), Red = Hex("#FF6B6B"), Yellow = Hex("#E8C25A"), Ink = Hex("#1A1410");

        FaultSet data;
        FaultPicker picker;
        SessionState session;
        RollingLine line;
        PlayerMove player;
        Rigidbody2D body;
        SceneSwitcher switcher;
        OverviewCamera overview;
        Font font;
        Canvas canvas;
        Text status, markerName;
        RectTransform marker, statusBar;
        GameObject window;
        Vector3 markerWorld;
        Bounds alertBounds;
        float canArriveAt;
        bool armed, openRequested, closeRequested, forced;

        public Fault ActiveFault { get; private set; }
        public bool AlertOpen { get; private set; }
        public bool Armed => armed;
        public bool WindowOpen { get; private set; }
        public bool Waiting { get; private set; }
        public Analysis Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Enabled || FindFirstObjectByType<PlantFaultDirector>()) return;
            var go = new GameObject(nameof(PlantFaultDirector));
            DontDestroyOnLoad(go);
            go.AddComponent<PlantFaultDirector>();
        }

        void Start()
        {
            var json = Resources.Load<TextAsset>(DataPath);
            if (!json)
            {
                Debug.LogError($"[Diagnosis] 문제 데이터가 없습니다: Resources/{DataPath}.json");
                enabled = false;
                return;
            }
            data = JsonUtility.FromJson<FaultSet>(json.text);
            picker = new FaultPicker(data.faults, n => UnityEngine.Random.Range(0, n));
            font = Resources.Load<Font>("Fonts/Galmuri11") ?? Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 18);
            BuildCanvas();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Bind();
            StartCoroutine(Run());
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Bind();

        /// <summary>장면이 바뀌면 그 장면의 설비와 캐릭터를 다시 찾는다.</summary>
        void Bind()
        {
            line = FindAnyObjectByType<RollingLine>(FindObjectsInactive.Include);
            player = FindAnyObjectByType<PlayerMove>(FindObjectsInactive.Include);
            body = player ? player.GetComponent<Rigidbody2D>() : null;
            switcher = FindAnyObjectByType<SceneSwitcher>(FindObjectsInactive.Include);
            overview = FindAnyObjectByType<OverviewCamera>(FindObjectsInactive.Include);
            armed = false;
            if (marker) marker.gameObject.SetActive(false);
            EnsureEventSystem();
        }

        /// <summary>열간압연 시나리오를 끝낸 뒤에만 돌아간다.</summary>
        bool Running
        {
            get
            {
                if (forced) return true;
                if (!session) session = FindFirstObjectByType<SessionState>();
                return session && session.hotRollingDone;
            }
        }

        IEnumerator Run()
        {
            float delay = data.firstDelay;
            while (true)
            {
                statusBar.gameObject.SetActive(false);
                while (!Running) yield return null;
                yield return new WaitForSeconds(delay);
                delay = UnityEngine.Random.Range(data.nextDelayMin, data.nextDelayMax);
                if (!Running) { delay = data.firstDelay; continue; }

                var fault = picker.Next();
                ActiveFault = fault;
                yield return Alert(fault);
                if (ActiveFault == null) { delay = data.firstDelay; continue; }

                Freeze(true);
                statusBar.gameObject.SetActive(false);
                WindowOpen = true;
                Waiting = true;
                closeRequested = false;
                Current = null;
                ShowWindow(fault, null);
                yield return Ask(fault);
                Waiting = false;
                ShowWindow(fault, Current);
                while (!closeRequested) yield return null;

                CloseWindow();
                WindowOpen = false;
                Freeze(false);
                if (line) line.SetWarning(fault.warn, false);
                ActiveFault = null;
            }
        }

        /// <summary>플레이어가 사건 창을 열 때까지 기다린다. 다른 공정에 있으면 어디로 가야 하는지 알려 준다.</summary>
        IEnumerator Alert(Fault fault)
        {
            openRequested = false;
            armed = false;
            AlertOpen = true;
            while (!openRequested)
            {
                if (!Running)
                {
                    marker.gameObject.SetActive(false);
                    AlertOpen = false;
                    ActiveFault = null;
                    yield break;
                }

                if (SceneManager.GetActiveScene().name == fault.scene)
                {
                    if (!armed) Arm(fault);
                    if (armed)
                    {
                        ShowStatus($"문제 발생: {Process(fault.scene)} · {fault.location} · 다가가거나 느낌표를 누르세요");
                        if (Time.time >= canArriveAt && PlayerArrived()) RequestOpen();
                    }
                }
                else
                {
                    marker.gameObject.SetActive(false);
                    armed = false;
                    int index = Array.IndexOf(SceneSwitcher.Scenes, fault.scene);
                    ShowStatus($"문제 발생: {Process(fault.scene)} · {fault.location} · 숫자키 {index + 1}로 이동하세요");
                }
                yield return null;
            }
            AlertOpen = false;
            marker.gameObject.SetActive(false);
        }

        void Arm(Fault fault)
        {
            var equipment = line ? line.equipment.Find(e => e.id == fault.warn) : null;
            if (equipment == null || !equipment.renderer) return;
            line.SetWarning(fault.warn, true);

            // 충돌 영역이 그림보다 앞으로 나온 설비가 있어서, 부딪히는 곳까지 포함해 거리를 잰다.
            alertBounds = equipment.renderer.bounds;
            var solid = equipment.renderer.GetComponent<Collider2D>();
            if (solid) alertBounds.Encapsulate(new Bounds(new Vector3(solid.bounds.center.x, solid.bounds.center.y, alertBounds.center.z), solid.bounds.size));

            markerWorld = new Vector3(alertBounds.center.x, alertBounds.min.y + MarkerLiftTiles, 0f);
            markerName.text = fault.location;
            marker.gameObject.SetActive(true);
            PlaceMarker();
            canArriveAt = Time.time + MinAlertSeconds;   // 이미 근처에 있어도 느낌표를 볼 틈은 준다
            armed = true;
        }

        /// <summary>느낌표 클릭, 또는 설비에 다가가면 불린다.</summary>
        public void RequestOpen()
        {
            if (AlertOpen && armed) openRequested = true;
        }

        /// <summary>「조치 완료」 버튼과 Enter 키가 부른다.</summary>
        public void RequestClose()
        {
            if (WindowOpen && !Waiting) closeRequested = true;
        }

        /// <summary>문제 카드를 Gemini에게 보낸다. 혼잡으로 실패하면 한 번 더 시도하고, 안 되면 기본 분석을 쓴다.</summary>
        IEnumerator Ask(Fault fault)
        {
            if (!GeminiClient.Available)
            {
                Current = Analysis.Basic(fault, "no_key");
                yield break;
            }

            string card = JsonUtility.ToJson(fault);
            string prompt = DiagnosisPrompt.Build(card);
            string reply = null;
            yield return GeminiClient.Ask(prompt, AskSeconds, t => reply = t);
            if (reply == null)
            {
                yield return new WaitForSecondsRealtime(1f);
                yield return GeminiClient.Ask(prompt, AskSeconds, t => reply = t);
            }
            if (reply == null)
            {
                Current = Analysis.Basic(fault, "llm_error");
                yield break;
            }

            Analysis answer = null;
            try { answer = JsonUtility.FromJson<Analysis>(reply); }
            catch (ArgumentException) { }

            if (DiagnosisPrompt.IsValid(answer, fault, card))
            {
                answer.source = "gemini";
                Current = answer;
            }
            else
            {
                Debug.LogWarning($"[Diagnosis] AI 답변이 근거와 달라 기본 분석을 씁니다: {fault.id}");
                Current = Analysis.Basic(fault, "invalid");
            }
        }

        static string Process(string scene)
        {
            int index = Array.IndexOf(SceneSwitcher.Scenes, scene);
            return index >= 0 ? SceneSwitcher.Labels[index].Substring(2) : scene;
        }

        bool PlayerArrived()
        {
            if (!player) return false;
            Vector2 p = player.transform.position;
            Vector2 closest = alertBounds.ClosestPoint(new Vector3(p.x, p.y, alertBounds.center.z));
            return Vector2.Distance(p, closest) <= ArriveTiles;
        }

        void Freeze(bool on)
        {
            if (player) player.enabled = !on;
            if (on && body) body.linearVelocity = Vector2.zero;
            if (switcher) switcher.enabled = !on;
            if (overview) overview.enabled = !on;
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (WindowOpen && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) RequestClose();
            if (Application.isEditor && k.f9Key.wasPressedThisFrame) forced = true;
        }

        void LateUpdate()
        {
            if (marker && marker.gameObject.activeSelf) PlaceMarker();
        }

        void PlaceMarker()
        {
            var cam = Camera.main;
            if (!cam) return;
            var root = (RectTransform)canvas.transform;
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, cam.WorldToScreenPoint(markerWorld), uiCamera, out var p);
            var area = root.rect;
            p.x = Mathf.Clamp(p.x, area.xMin + 60f, area.xMax - 60f);
            float topGap = p.x > area.xMax - MinimapZoneWidth ? MinimapZoneGap : MarkerTopGap;
            p.y = Mathf.Clamp(p.y, area.yMin + 90f, area.yMax - topGap) + Mathf.Sin(Time.unscaledTime * 5f) * 6f;
            marker.anchoredPosition = p;
        }

        // ── 화면 ────────────────────────────────────────────────

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include)) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        void BuildCanvas()
        {
            var go = new GameObject("DiagnosisCanvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var bar = Ui("Status", go.transform);
            statusBar = (RectTransform)bar.transform;
            bar.AddComponent<Image>().color = Hex("#000000", 0.6f);
            statusBar.anchorMin = statusBar.anchorMax = new Vector2(0.5f, 0f);
            statusBar.pivot = new Vector2(0.5f, 0f);
            statusBar.anchoredPosition = new Vector2(0, 12);
            Stack(bar, 0, new RectOffset(18, 18, 6, 6));
            var fit = bar.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = Label(bar.transform, "", 18, Body);
            status.horizontalOverflow = HorizontalWrapMode.Overflow;
            bar.SetActive(false);

            var m = Ui("Marker", go.transform);
            marker = (RectTransform)m.transform;
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(44, 44);
            var image = m.AddComponent<Image>();
            image.color = Yellow;
            var outline = m.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(3, -3);
            m.AddComponent<Button>().onClick.AddListener(RequestOpen);
            var bang = Label(m.transform, "!", 32, Ink, FontStyle.Bold);
            bang.alignment = TextAnchor.MiddleCenter;
            Fill(bang.rectTransform);
            markerName = Label(m.transform, "", 16, Body, FontStyle.Bold);
            markerName.alignment = TextAnchor.UpperCenter;
            markerName.horizontalOverflow = HorizontalWrapMode.Overflow;
            markerName.gameObject.AddComponent<Outline>().effectColor = Color.black;
            var nameRect = markerName.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0, -6);
            nameRect.sizeDelta = new Vector2(200, 24);
            m.SetActive(false);
        }

        void ShowStatus(string text)
        {
            status.text = text;
            statusBar.gameObject.SetActive(true);
        }

        void CloseWindow()
        {
            if (window) Destroy(window);
            window = null;
        }

        /// <summary>문제 창을 그린다. analysis가 null이면 분석 중으로 표시한다.</summary>
        void ShowWindow(Fault f, Analysis analysis)
        {
            CloseWindow();
            window = Ui("Window", canvas.transform);
            window.AddComponent<Image>().color = Hex("#000000", 0.6f);
            Fill((RectTransform)window.transform);

            var box = Ui("Panel", window.transform);
            box.AddComponent<Image>().color = Panel;
            var rect = (RectTransform)box.transform;
            Fill(rect);
            rect.offsetMin = new Vector2(60, 24);
            rect.offsetMax = new Vector2(-60, -24);
            Stack(box, 8, new RectOffset(24, 24, 16, 16));

            Label(box.transform, $"위치: {Process(f.scene)} · {f.location}", 16, Orange, FontStyle.Bold);
            Label(box.transform, f.title, 30, Body, FontStyle.Bold);
            Label(box.transform, f.symptom, 18, Dim);

            var columns = Ui("Columns", box.transform);
            columns.AddComponent<LayoutElement>().flexibleHeight = 1;
            var layout = columns.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            var left = Column(columns.transform, 4);
            Label(left, "측정값", 17, Teal, FontStyle.Bold);
            foreach (var r in f.readings)
                Label(left, $"{r.name}  <color=#{(r.abnormal ? "FF6B6B" : "EEF0F3")}>{r.value}</color>\n<size=14><color=#A3A8B2>기준 {r.normal}</color></size>", 16, Body);

            var right = Column(columns.transform, 6);
            if (analysis == null)
            {
                Label(right, "AI가 원인을 분석하는 중입니다...", 20, Teal, FontStyle.Bold);
            }
            else
            {
                Label(right, analysis.SourceLabel(GeminiClient.Model), 14, analysis.FromAi ? Teal : Yellow, FontStyle.Bold);
                Label(right, $"유력 원인: {analysis.root_cause}", 20, Red, FontStyle.Bold);
                Label(right, string.Join("\n", analysis.analysis), 16, Body);
                Label(right, "해결 순서", 17, Teal, FontStyle.Bold);
                var steps = new StringBuilder();
                for (int i = 0; i < analysis.steps.Length; i++) steps.AppendLine($"{i + 1}. {analysis.steps[i]}");
                Label(right, steps.ToString().TrimEnd(), 16, Body);
                Label(right, $"<color=#6CC3B0><b>재발 방지</b></color>  {analysis.prevention}", 16, Body);

                var button = Ui("Done", box.transform);
                button.AddComponent<Image>().color = Teal;
                button.AddComponent<Button>().onClick.AddListener(RequestClose);
                button.AddComponent<LayoutElement>().minHeight = 42;
                Stack(button, 0, new RectOffset(16, 16, 9, 9));
                var text = Label(button.transform, "조치 완료 (Enter)", 19, Hex("#062520"), FontStyle.Bold);
                text.alignment = TextAnchor.MiddleCenter;
            }
        }

        Transform Column(Transform parent, float weight)
        {
            var go = Ui("Column", parent);
            go.AddComponent<Image>().color = Card;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = weight;
            element.flexibleWidth = weight;
            Stack(go, 6, new RectOffset(14, 14, 10, 10));
            return go.transform;
        }

        Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var label = Ui("Text", parent).AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        static GameObject Ui(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stack(GameObject go, float spacing, RectOffset padding)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static Color Hex(string hex, float alpha = 1f)
        {
            int rgb = Convert.ToInt32(hex.Substring(1), 16);
            return new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, alpha);
        }
    }
}
