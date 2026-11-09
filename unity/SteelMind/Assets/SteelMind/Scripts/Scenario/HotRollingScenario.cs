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
    /// 사건이 터지면 오른쪽에 운영실 회의방(채팅)이 열려 Agent들이 차례로 말하고, 플레이어가 고르면
    /// Agent 회의가 이어진다. 사건이 다 끝나면 결과 화면(게임 결과 연출 + AI 진단). 끄려면 Enabled = false.
    /// </summary>
    public class HotRollingScenario : MonoBehaviour
    {
        public static bool Enabled = true;   // 설정값(플레이 상태 아님)
        const string SceneName = "Rolling";
        const string DataPath = "Scenarios/hot_rolling";
        const float TypingTime = 0.45f, AfterMessage = 0.25f, MeetingAutoClose = 4f;

        static readonly Color Room = Hex("#1E1E1E", 0.97f), BubbleBg = Hex("#303030"), Body = Hex("#F2F2F2"), Dim = Hex("#A8A8B0"),
            Yellow = Hex("#F0C040"), Cyan = Hex("#5FC3E4"), Accent = Hex("#6CC3B0"), LevelCol = Hex("#F0A04B"), Red = Hex("#FF6B6B");
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
        bool deciding, chatting, fastForward, showingResult;
        Coroutine resume;

        RollingLine line;
        LineProcess process;
        PlayerMove player;
        Rigidbody2D body;
        readonly List<Behaviour> pausedDuringDecision = new List<Behaviour>();
        readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();

        Font font;
        Sprite round;
        Canvas canvas;
        Text status;
        RectTransform chat, chatContent, chatFooter, overlay;
        ScrollRect chatScroll;
        Text chatTitle;

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
            round = MakeRoundSprite(64, 18);
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
            current = -1; deciding = chatting = showingResult = false;
            CloseChat();
            if (overlay) Destroy(overlay.gameObject);
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
                yield return EventChat(i);
                var chosen = def.events[i].choices[picks[i]];
                if (i + 1 < n) SetStatus($"조치 진행 중: {chosen.label} · 사건 {i + 2}/{n} 대기 중");
            }
            SetStatus("교대 근무를 마무리하는 중입니다");
            yield return new WaitForSeconds(def.resultDelay);
            yield return ResultScreen();
        }

        // 사건 하나: 경보 → Agent 의견 → 선택 → 내 메시지 → Agent 회의
        IEnumerator EventChat(int i)
        {
            current = i;
            chatting = true; fastForward = false;
            var ev = def.events[i];
            int n = def.events.Length;
            if (line && !string.IsNullOrEmpty(ev.warn)) line.SetWarning(ev.warn, true);
            SetPlayerFrozen(true);
            OpenChat($"운영실 회의방   {C(ColorHex(LevelCol), ev.level)} · 사건 {i + 1}/{n}");

            AlertCard(ev);
            yield return Pause(AfterMessage * 2);
            foreach (var a in ev.agents)
            {
                yield return Typing(a.agent);
                var (name, col) = Agents.TryGetValue(a.agent, out var v) ? v : (a.agent, "#F2F2F2");
                AgentMsg(a.agent, $"{C(col, $"<b>{name}</b>")} {C(ColorHex(Dim), "· 의견")}", a.say, a.why);
                yield return Pause(AfterMessage);
            }
            yield return Typing("chief");
            AgentMsg("chief", C(ColorHex(Yellow), "<b>공장장 · 쟁점</b>"), ev.chief, "어떻게 할까요? 아래에서 고르세요.");
            ShowChoices(ev);
            deciding = true;
            while (deciding) yield return null;

            var c = ev.choices[picks[i]];
            PlayerMsg($"{c.label} — 이렇게 진행합니다.", c.detail);
            yield return Pause(AfterMessage * 2);
            if (c.reactions != null && c.reactions.Length > 0)
            {
                Divider("Agent 회의");
                foreach (var r in c.reactions)
                {
                    yield return Typing(r.agent);
                    var (name, col) = Agents.TryGetValue(r.agent, out var v) ? v : (r.agent, "#F2F2F2");
                    var sc = StanceColor.TryGetValue(r.stance, out var s) ? s : "#F2F2F2";
                    AgentMsg(r.agent, $"{C(col, $"<b>{name}</b>")} · {C(sc, $"<b>{r.stance}</b>")}", r.line, null);
                    yield return Pause(AfterMessage);
                }
            }
            SetFooterHint("Enter·Space로 닫기 · 잠시 뒤 자동으로 닫힙니다");
            fastForward = false;
            float t = 0;
            while (t < MeetingAutoClose && !fastForward) { t += Time.deltaTime; yield return null; }
            CloseChat();
            chatting = false;
            SetPlayerFrozen(false);
        }

        IEnumerator Pause(float sec)
        {
            float t = 0;
            while (t < sec && !fastForward) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator Typing(string agent)
        {
            var row = AgentMsg(agent, null, "···", null);
            yield return Pause(TypingTime);
            Destroy(row.gameObject);
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
            fastForward = false;
            ClearFooter();
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

        void SetPlayerFrozen(bool frozen)
        {
            if (player)
            {
                player.enabled = !frozen;
                if (body) body.linearVelocity = Vector2.zero;
            }
            // 대화 중에는 숫자키 공정 이동·Tab 전체 보기를 막는다(1·2·3 선택과 겹침)
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
            else if (chatting)
            {
                if (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) fastForward = true;
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
            var bar = NewRect("Status", go.transform);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -12);
            Round(bar.gameObject, Room);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(20, 20, 8, 8); h.childControlWidth = h.childControlHeight = true;
            var f = bar.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = AddText(bar, "", 17, Body);
            status.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        void SetStatus(string s) { if (status) status.text = s; }

        static RectTransform NewRect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        Image Round(GameObject go, Color c)
        {
            var img = go.AddComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.4f; img.color = c;
            return img;
        }

        static Sprite MakeRoundSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
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

        static VerticalLayoutGroup VLayout(GameObject go, int padX, int padY, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padX, padX, padY, padY); v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        static HorizontalLayoutGroup HLayout(GameObject go, float spacing, TextAnchor align = TextAnchor.UpperLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childAlignment = align;
            return h;
        }

        static LayoutElement LE(GameObject go, float prefW = -1, float prefH = -1, float flexW = -1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (!le) le = go.AddComponent<LayoutElement>();
            if (prefW >= 0) { le.preferredWidth = prefW; le.minWidth = prefW; }
            if (prefH >= 0) { le.preferredHeight = prefH; le.minHeight = prefH; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        Sprite Portrait(string agent)
        {
            if (portraits.TryGetValue(agent, out var sp)) return sp;
            var bytes = Resources.Load<TextAsset>($"Portraits/{agent}");
            if (!bytes) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(bytes.bytes);
            tex.filterMode = FilterMode.Point;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            portraits[agent] = sp;
            return sp;
        }

        static string C(string hex, string s) => $"<color={hex}>{s}</color>";
        static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ── 운영실 회의방 (채팅) ───────────────────────────────
        void OpenChat(string title)
        {
            CloseChat();
            chat = NewRect("Chat", canvas.transform);
            chat.anchorMin = new Vector2(1, 0); chat.anchorMax = new Vector2(1, 1); chat.pivot = new Vector2(1, 0.5f);
            chat.offsetMin = new Vector2(-16 - 580, 16); chat.offsetMax = new Vector2(-16, -64);
            Round(chat.gameObject, Room);
            VLayout(chat.gameObject, 0, 0, 0).childForceExpandHeight = false;

            var head = NewRect("Header", chat);
            HLayout(head.gameObject, 8, TextAnchor.MiddleLeft).padding = new RectOffset(18, 18, 12, 10);
            LE(head.gameObject, -1, 46);
            chatTitle = AddText(head, title, 18, Body, FontStyle.Bold, TextAnchor.MiddleLeft);
            LE(chatTitle.gameObject, -1, -1, 1);
            var demo = AddText(head, "데모용 가상 데이터", 13, Dim, FontStyle.Normal, TextAnchor.MiddleRight);
            demo.horizontalOverflow = HorizontalWrapMode.Overflow;

            var view = NewRect("Viewport", chat);
            view.gameObject.AddComponent<RectMask2D>();
            var vle = LE(view.gameObject); vle.flexibleHeight = 1; vle.minHeight = 100;
            chatContent = NewRect("Content", view);
            chatContent.anchorMin = new Vector2(0, 1); chatContent.anchorMax = new Vector2(1, 1); chatContent.pivot = new Vector2(0.5f, 1);
            chatContent.offsetMin = chatContent.offsetMax = Vector2.zero;
            VLayout(chatContent.gameObject, 16, 10, 12);
            chatContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            chatScroll = view.gameObject.AddComponent<ScrollRect>();
            chatScroll.content = chatContent; chatScroll.viewport = view; chatScroll.horizontal = false;
            chatScroll.movementType = ScrollRect.MovementType.Clamped; chatScroll.scrollSensitivity = 30;

            chatFooter = NewRect("Footer", chat);
            VLayout(chatFooter.gameObject, 16, 10, 8);
        }

        void CloseChat()
        {
            if (chat) Destroy(chat.gameObject);
            chat = chatContent = chatFooter = null;
        }

        void ScrollToBottom()
        {
            if (!chatContent) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(chat);
            if (chatScroll) chatScroll.verticalNormalizedPosition = 0;
        }

        void AlertCard(ScenarioEvent ev)
        {
            var card = NewRect("Alert", chatContent);
            Round(card.gameObject, Hex("#3A2424"));
            VLayout(card.gameObject, 16, 12, 4);
            AddText(card, $"⚠ 경보 · {ev.title}", 20, Red, FontStyle.Bold);
            AddText(card, ev.desc, 16, Body);
            if (EfficiencyCalc.CarryApplies(ev, flags)) AddText(card, ev.carry.note, 15, Red);
            ScrollToBottom();
        }

        // 왼쪽: 캐릭터 + 말풍선. header가 null이면 말풍선만(입력 중 표시용)
        RectTransform AgentMsg(string agent, string header, string text, string sub)
        {
            var row = NewRect("Msg", chatContent);
            HLayout(row.gameObject, 12);
            var pic = NewRect("Portrait", row);
            var img = pic.gameObject.AddComponent<Image>(); img.sprite = Portrait(agent); img.preserveAspect = true;
            LE(pic.gameObject, 48, 64);
            var bubble = NewRect("Bubble", row);
            Round(bubble.gameObject, BubbleBg);
            VLayout(bubble.gameObject, 16, 10, 2);
            LE(bubble.gameObject, -1, -1, 1);
            if (header != null) AddText(bubble, header, 16, Body);
            AddText(bubble, text, 18, header == null ? Dim : Body, header == null ? FontStyle.Bold : FontStyle.Normal);
            if (!string.IsNullOrEmpty(sub)) AddText(bubble, sub, 14, Dim);
            ScrollToBottom();
            return row;
        }

        // 오른쪽: 내 메시지(노란 말풍선) + 내 캐릭터
        void PlayerMsg(string text, string sub)
        {
            var row = NewRect("Me", chatContent);
            HLayout(row.gameObject, 12, TextAnchor.UpperRight);
            var spacer = NewRect("Space", row); LE(spacer.gameObject, 60, -1, 0);
            var bubble = NewRect("Bubble", row);
            Round(bubble.gameObject, Yellow);
            VLayout(bubble.gameObject, 16, 10, 2);
            LE(bubble.gameObject, -1, -1, 1);
            AddText(bubble, "<b>나 · 운영 책임자</b>", 15, Hex("#4A3A10"));
            AddText(bubble, text, 18, Hex("#1A1408"), FontStyle.Bold);
            if (!string.IsNullOrEmpty(sub)) AddText(bubble, sub, 14, Hex("#4A3A10"));
            var pic = NewRect("Portrait", row);
            var img = pic.gameObject.AddComponent<Image>(); img.sprite = Portrait("player"); img.preserveAspect = true;
            LE(pic.gameObject, 48, 64);
            ScrollToBottom();
        }

        void Divider(string label)
        {
            var row = NewRect("Divider", chatContent);
            AddText(row, $"─────  {label}  ─────", 14, Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
            VLayout(row.gameObject, 0, 4, 0);
            ScrollToBottom();
        }

        void ShowChoices(ScenarioEvent ev)
        {
            ClearFooter();
            AddText(chatFooter, "선택지 · 버튼을 누르거나 키보드 1·2·3", 14, Cyan, FontStyle.Bold);
            for (int k = 0; k < ev.choices.Length; k++)
            {
                var c = ev.choices[k];
                var recs = new List<string>();
                foreach (var a in ev.agents) if (a.rec == c.id && Agents.TryGetValue(a.agent, out var v)) recs.Add(v.name);
                var btnRt = NewRect("Choice", chatFooter);
                var bg = Round(btnRt.gameObject, Hex("#2B2B2B"));
                HLayout(btnRt.gameObject, 12, TextAnchor.MiddleLeft).padding = new RectOffset(12, 14, 8, 8);
                var b = btnRt.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                var colors = b.colors; colors.highlightedColor = Hex("#4A4A4A"); colors.pressedColor = Hex("#5A5A5A"); b.colors = colors;
                var num = NewRect("Num", btnRt);
                Round(num.gameObject, Yellow);
                LE(num.gameObject, 34, 34);
                var nt = AddText(num, (k + 1).ToString(), 18, Hex("#1A1408"), FontStyle.Bold, TextAnchor.MiddleCenter);
                var ntr = nt.rectTransform; ntr.anchorMin = Vector2.zero; ntr.anchorMax = Vector2.one; ntr.offsetMin = ntr.offsetMax = Vector2.zero;
                var col = NewRect("Text", btnRt);
                VLayout(col.gameObject, 0, 0, 1);
                LE(col.gameObject, -1, -1, 1);
                AddText(col, $"<b>{c.label}</b>  {C(ColorHex(Dim), c.detail)}", 17, Body);
                AddText(col, recs.Count > 0 ? "추천: " + string.Join("·", recs) : "추천한 에이전트 없음", 13, Accent);
                int captured = k;
                b.onClick.AddListener(() => Choose(captured));
            }
            ScrollToBottom();
        }

        void SetFooterHint(string s)
        {
            ClearFooter();
            if (chatFooter) AddText(chatFooter, s, 14, Dim, FontStyle.Normal, TextAnchor.MiddleCenter);
            ScrollToBottom();
        }

        void ClearFooter()
        {
            if (!chatFooter) return;
            for (int i = chatFooter.childCount - 1; i >= 0; i--) Destroy(chatFooter.GetChild(i).gameObject);
        }

        // ── 결과 화면 (게임 결과 연출) ─────────────────────────
        IEnumerator ResultScreen()
        {
            var r = EfficiencyCalc.Compute(def, picks);
            showingResult = true;
            overlay = NewRect("Result", canvas.transform);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.78f);

            var card = NewRect("Card", overlay);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1040, 0);
            Round(card.gameObject, Room);
            VLayout(card.gameObject, 28, 22, 12);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var banner = AddText(card, "교대 근무 종료", 40, Yellow, FontStyle.Bold, TextAnchor.MiddleCenter);
            AddText(card, $"{def.title} 결과 · 모든 값은 데모용 가상 데이터", 15, Dim, FontStyle.Normal, TextAnchor.MiddleCenter);
            StartCoroutine(Pop(banner.transform, 1.6f));

            var cols = NewRect("Cols", card);
            var ch = HLayout(cols.gameObject, 20); ch.childForceExpandWidth = true;
            var left = NewRect("Left", cols); VLayout(left.gameObject, 0, 0, 8); LE(left.gameObject, 10, -1, 1);
            var right = NewRect("Right", cols); VLayout(right.gameObject, 0, 0, 10); LE(right.gameObject, 10, -1, 1);

            // 왼쪽: 효율 카운트다운, 게이지, 항목 막대
            AddText(left, "운영 효율", 16, Dim, FontStyle.Bold);
            var big = AddText(left, "100%", 72, LossColor(0), FontStyle.Bold);
            var arrow = AddText(left, "100%  →  …", 16, Dim);
            var gauge = Gauge(left, 1f, Accent, 16);
            var sentence = AddText(left, "", 16, Body);
            var bars = new List<(Image fill, Text val, int loss)>();
            foreach (var (label, loss) in new[] { ("생산", r.totals.production), ("품질", r.totals.quality), ("안전", r.totals.safety), ("비용", r.totals.cost) })
                bars.Add(MetricBar(left, label, loss));

            // 오른쪽: 사건 스탬프, AI 진단
            AddText(right, "사건 기록", 16, Dim, FontStyle.Bold);
            var stamps = new List<RectTransform>();
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                var sc = NewRect("Stamp", right);
                Round(sc.gameObject, BubbleBg);
                var h = HLayout(sc.gameObject, 12, TextAnchor.MiddleLeft); h.padding = new RectOffset(16, 16, 10, 10);
                var txt = NewRect("Txt", sc); VLayout(txt.gameObject, 0, 0, 2); LE(txt.gameObject, -1, -1, 1);
                AddText(txt, $"<b>{i + 1}. {e.ev.title}</b>", 17, Body);
                AddText(txt, $"내 선택: {e.chosen.label}" + (e.chosen.best ? "" : C(ColorHex(Dim), $"  ·  최선: {e.best.label}")), 15, Body);
                AddText(txt, e.chosen.outcome, 13, Dim);
                if (e.carried) AddText(txt, $"{e.ev.carry.note} (효율 −{e.carryLoss}%)", 13, Red);
                var stamp = NewRect("Mark", sc);
                Round(stamp.gameObject, e.chosen.best ? Hex("#24402F") : Hex("#402424"));
                LE(stamp.gameObject, 108, 56);
                var st = AddText(stamp, e.chosen.best ? "최선!" : $"−{e.Total}%", e.chosen.best ? 24 : 22, e.Total == 0 ? LossColor(0) : LossColor(e.Total), FontStyle.Bold, TextAnchor.MiddleCenter);
                var str = st.rectTransform; str.anchorMin = Vector2.zero; str.anchorMax = Vector2.one; str.offsetMin = str.offsetMax = Vector2.zero;
                sc.gameObject.SetActive(false);
                stamps.Add(sc);
            }
            AddText(right, "AI 진단", 16, Cyan, FontStyle.Bold);
            var diagRow = NewRect("Diag", right);
            HLayout(diagRow.gameObject, 12);
            var pic = NewRect("Portrait", diagRow);
            var pimg = pic.gameObject.AddComponent<Image>(); pimg.sprite = Portrait("chief"); pimg.preserveAspect = true;
            LE(pic.gameObject, 48, 64);
            var bubble = NewRect("Bubble", diagRow);
            Round(bubble.gameObject, BubbleBg);
            VLayout(bubble.gameObject, 16, 10, 2);
            LE(bubble.gameObject, -1, -1, 1);
            AddText(bubble, $"{C(ColorHex(Yellow), "<b>공장장 AI</b>")} {C(ColorHex(Dim), "· 기본 진단 (LLM 연결 전)")}", 15, Body);
            var diag = AddText(bubble, "", 16, Body);
            diagRow.gameObject.SetActive(false);

            var btnRt = NewRect("Restart", card);
            var bimg = Round(btnRt.gameObject, Yellow);
            LE(btnRt.gameObject, -1, 50);
            var b = btnRt.gameObject.AddComponent<Button>(); b.targetGraphic = bimg;
            var bt = AddText(btnRt, "다시 하기 (R)", 20, Hex("#1A1408"), FontStyle.Bold, TextAnchor.MiddleCenter);
            var btr = bt.rectTransform; btr.anchorMin = Vector2.zero; btr.anchorMax = Vector2.one; btr.offsetMin = btr.offsetMax = Vector2.zero;
            b.onClick.AddListener(Restart);
            btnRt.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);

            // 연출: 숫자가 100에서 최종값으로 내려가고 막대가 줄어든다
            yield return new WaitForSeconds(0.6f);
            float dur = 1.4f, t = 0;
            var gfill = gauge.GetChild(0).GetComponent<RectTransform>();
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, t / dur);
                int shown = Mathf.RoundToInt(Mathf.Lerp(100, r.finalEfficiency, k));
                big.text = shown + "%";
                big.color = LossColor(100 - shown);
                gfill.anchorMax = new Vector2(shown / 100f, 1);
                foreach (var (fill, val, loss) in bars)
                {
                    float rem = Mathf.Lerp(100, 100 - loss, k);
                    fill.rectTransform.anchorMax = new Vector2(rem / 100f, 1);
                    val.text = loss > 0 ? $"−{Mathf.RoundToInt(loss * k)}" : "0";
                }
                yield return null;
            }
            big.text = r.finalEfficiency + "%";
            arrow.text = $"운영 효율  100%  →  {r.finalEfficiency}%";
            sentence.text = r.drop == 0 ? "모든 사건에서 최선의 선택을 해 효율을 100% 지켰습니다." : $"최선의 대응과 비교해 효율이 {r.drop}% 떨어졌습니다.";
            StartCoroutine(Pop(big.transform, 1.25f));
            yield return new WaitForSeconds(0.4f);
            foreach (var s in stamps)
            {
                s.gameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                StartCoroutine(Pop(s.GetChild(1), 1.8f));
                yield return new WaitForSeconds(0.45f);
            }
            diagRow.gameObject.SetActive(true);
            var full = Diagnose(r);
            for (int i = 1; i <= full.Length; i++)
            {
                diag.text = full.Substring(0, i);
                if (i % 3 == 0) LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                yield return new WaitForSeconds(0.015f);
            }
            btnRt.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }

        IEnumerator Pop(Transform tr, float from)
        {
            float t = 0, dur = 0.25f;
            while (t < dur && tr)
            {
                t += Time.deltaTime;
                float s = Mathf.Lerp(from, 1f, Mathf.SmoothStep(0, 1, t / dur));
                tr.localScale = new Vector3(s, s, 1);
                yield return null;
            }
            if (tr) tr.localScale = Vector3.one;
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

        (Image, Text, int) MetricBar(Transform parent, string label, int loss)
        {
            var row = NewRect("Metric", parent);
            HLayout(row.gameObject, 10, TextAnchor.MiddleLeft);
            var t = AddText(row, label, 16, Body, FontStyle.Bold);
            LE(t.gameObject, 44);
            var bar = Gauge(row, 1f, LossColor(loss), 12);
            LE(bar.gameObject, 10, 12, 1);
            var v = AddText(row, "0", 16, LossColor(loss), FontStyle.Bold, TextAnchor.MiddleRight);
            LE(v.gameObject, 48);
            return (bar.GetChild(0).GetComponent<Image>(), v, loss);
        }

        RectTransform Gauge(Transform parent, float ratio, Color color, float height)
        {
            var bg = NewRect("Gauge", parent);
            Round(bg.gameObject, Hex("#3A3A3A")).pixelsPerUnitMultiplier = 4f;
            LE(bg.gameObject, -1, height);
            var fill = NewRect("Fill", bg);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Round(fill.gameObject, color).pixelsPerUnitMultiplier = 4f;
            return bg;
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
