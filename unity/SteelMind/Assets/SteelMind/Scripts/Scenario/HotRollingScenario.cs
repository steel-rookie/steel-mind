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
    /// 사건 발생 → 설비 위 표시와 화면 가장자리 화살표로 길 안내 → 설비에 다가가면 회의 채팅방(왼쪽 대화, 오른쪽 선택)
    /// → 결정 → Agent 반응(표정·감정 색) → 다음 사건 → 결과 화면. 끄려면 Enabled = false.
    /// </summary>
    public class HotRollingScenario : MonoBehaviour
    {
        public static bool Enabled = true;   // 설정값(플레이 상태 아님)
        const string SceneName = "Rolling";
        const string DataPath = "Scenarios/hot_rolling";
        const float CharDelay = 0.045f, TypingDots = 0.8f, NextSpeaker = 1.0f, ArriveRange = 3f;
        static readonly string[] Abc = { "A", "B", "C" };

        static readonly Color Room = Hex("#1D1F24", 0.97f), Bubble = Hex("#2A2C32"), Body = Hex("#EEF0F3"), Dim = Hex("#A3A8B2"),
            Teal = Hex("#6CC3B0"), Orange = Hex("#F0A04B"), Red = Hex("#FF6B6B"), Yellow = Hex("#F0C040"), Card = Hex("#24262B");
        static readonly Dictionary<string, (string name, string color, string persona)> Agents = new Dictionary<string, (string, string, string)>
        {
            { "production", ("생산", "#7AA7FF", "납기 사수") }, { "quality", ("품질", "#C99BFF", "0.01mm 집착") },
            { "equipment", ("설비", "#F0A04B", "롤 장인") }, { "safety", ("안전", "#6FCF97", "원칙주의자") },
            { "chief", ("공장장", "#F0C040", "공장장") }, { "player", ("나", "#6CC3B0", "운영 책임자") },
        };
        static readonly Dictionary<string, (string mood, string color)> Stance = new Dictionary<string, (string, string)>
        {
            { "찬성", ("agree", "#6FCF97") }, { "불만", ("angry", "#FF8A5B") }, { "경고", ("warn", "#F0C040") },
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
        bool deciding, chatting, skip, showingResult, guiding;
        Coroutine resume;

        RollingLine line;
        LineProcess process;
        PlayerMove player;
        Rigidbody2D body;
        readonly List<Behaviour> paused = new List<Behaviour>();
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        Font font;
        Sprite round, triangle;
        Canvas canvas;
        RectTransform canvasRect;
        Text status;
        RectTransform chat, msgs, side, opts, overlay, guideMarker, guideArrow;
        ScrollRect scroll;
        Text progressText, guideLabel, guideArrowLabel, helpText;
        RectTransform progressFill;
        Bounds guideTarget;
        string guideName;

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
            triangle = MakeTriangleSprite(48);
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
            current = -1; deciding = chatting = showingResult = guiding = false;
            CloseChat(); HideGuide();
            if (overlay) Destroy(overlay.gameObject);
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            if (process) process.speed = 1f;
            SetFrozen(false);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            int n = def.events.Length;
            SetStatus($"{def.title} 테스트 · 사건 1/{n} 대기 중 · WASD로 이동");
            for (int i = 0; i < n; i++)
            {
                yield return new WaitForSeconds(def.events[i].delay);
                yield return GoToSite(i);
                yield return EventChat(i);
                var chosen = def.events[i].choices[picks[i]];
                if (i + 1 < n) SetStatus($"조치 진행 중: {chosen.label} · 사건 {i + 2}/{n} 대기 중");
            }
            SetStatus("교대 근무를 마무리하는 중입니다");
            yield return new WaitForSeconds(def.resultDelay);
            yield return ResultScreen();
        }

        // 사건 발생: 경보를 켜고 설비까지 길을 안내한다. 가까이 가면 회의가 열린다.
        IEnumerator GoToSite(int i)
        {
            var ev = def.events[i];
            if (line && !string.IsNullOrEmpty(ev.warn)) line.SetWarning(ev.warn, true);
            var eq = line ? line.equipment.Find(e => e.id == ev.warn) : null;
            if (eq == null || !eq.renderer || !player) yield break;
            guideTarget = eq.renderer.bounds;
            guideName = eq.label;
            SetStatus($"⚠ {ev.title} · {eq.label}(으)로 가세요");
            ShowGuide();
            while (true)
            {
                var b = guideTarget; b.Expand(new Vector3(ArriveRange * 2, ArriveRange * 2, 10));
                if (b.Contains(new Vector3(player.transform.position.x, player.transform.position.y, b.center.z))) break;
                yield return null;
            }
            HideGuide();
        }

        // 회의 채팅방: 한 명씩 입력 중 → 한 글자씩 → 다음 사람. 다 들으면 오른쪽에서 고른다.
        IEnumerator EventChat(int i)
        {
            current = i; chatting = true; skip = false;
            var ev = def.events[i];
            int n = def.events.Length;
            SetFrozen(true);
            SetStatus($"⚠ {ev.title} · 회의 중");
            OpenChat(ev, i, n);
            int total = ev.agents.Length + 1, heard = 0;
            yield return Wait(0.5f);
            foreach (var a in ev.agents)
            {
                yield return Say(a.agent, null, a.say, a.why, false);
                SetProgress(++heard, total);
            }
            yield return Say("chief", null, ev.chief, null, false);
            SetProgress(++heard, total);
            UnlockChoices();
            deciding = true;
            while (deciding) yield return null;

            var c = ev.choices[picks[i]];
            yield return Say("player", null, $"{Abc[picks[i]]}. {c.label}", c.detail, true);
            if (c.reactions != null)
                foreach (var r in c.reactions) yield return Say(r.agent, r.stance, r.line, null, false);
            yield return WaitNext(i + 1 < n ? "다음으로 (Space)" : "결과 보기 (Space)");
            CloseChat();
            chatting = false;
            SetFrozen(false);
        }

        void Choose(int k)
        {
            if (!deciding || current < 0) return;
            var ev = def.events[current];
            if (k < 0 || k >= ev.choices.Length) return;
            var c = ev.choices[k];
            picks.Add(k);
            if (!string.IsNullOrEmpty(c.flag)) flags.Add(c.flag);
            deciding = false; skip = false;
            MarkChosen(k);
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

        IEnumerator Wait(float sec)
        {
            float t = 0;
            while (t < sec && !skip) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator WaitNext(string label)
        {
            if (helpText) helpText.text = "";
            var go = Button_(side, label, Teal, Hex("#0B221E"), 48);
            bool clicked = false;
            go.onClick.AddListener(() => clicked = true);
            skip = false;
            while (!clicked && !skip) yield return null;
            skip = false;
        }

        void SetFrozen(bool frozen)
        {
            if (player)
            {
                player.enabled = !frozen;
                if (body) body.linearVelocity = Vector2.zero;
            }
            // 회의 중에는 숫자키 공정 이동·Tab 전체 보기를 막는다(1·2·3 선택과 겹침)
            if (frozen)
            {
                if (paused.Count > 0) return;
                foreach (var b in FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None)) if (b.enabled) { b.enabled = false; paused.Add(b); }
                foreach (var b in FindObjectsByType<OverviewCamera>(FindObjectsSortMode.None)) if (b.enabled) { b.enabled = false; paused.Add(b); }
            }
            else
            {
                foreach (var b in paused) if (b) b.enabled = true;
                paused.Clear();
            }
        }

        void Update()
        {
            var k = Keyboard.current;
            if (guiding) UpdateGuide();
            if (k == null) return;
            if (deciding)
            {
                if (k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame || k.aKey.wasPressedThisFrame) Choose(0);
                else if (k.digit2Key.wasPressedThisFrame || k.numpad2Key.wasPressedThisFrame || k.bKey.wasPressedThisFrame) Choose(1);
                else if (k.digit3Key.wasPressedThisFrame || k.numpad3Key.wasPressedThisFrame || k.cKey.wasPressedThisFrame) Choose(2);
            }
            else if (chatting)
            {
                if (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) skip = true;
            }
            else if (showingResult && k.rKey.wasPressedThisFrame) Restart();
        }

        // ── 길 안내 ────────────────────────────────────────────
        void ShowGuide()
        {
            HideGuide();
            guiding = true;
            guideMarker = NewRect("GuideMarker", canvas.transform);
            guideMarker.sizeDelta = new Vector2(10, 10);
            var tag = NewRect("Tag", guideMarker);
            tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0); tag.pivot = new Vector2(0.5f, 0);
            Round(tag.gameObject, Red);
            var h = HLayout(tag.gameObject, 6, TextAnchor.MiddleCenter); h.padding = new RectOffset(14, 14, 6, 6);
            var fit = tag.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            guideLabel = AddText(tag, $"!  {guideName}", 18, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            guideLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tip = NewRect("Tip", guideMarker);
            tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0); tip.pivot = new Vector2(0.5f, 1);
            tip.sizeDelta = new Vector2(18, 12); tip.localRotation = Quaternion.Euler(0, 0, 180);
            tip.gameObject.AddComponent<Image>().sprite = triangle; tip.GetComponent<Image>().color = Red;

            guideArrow = NewRect("GuideArrow", canvas.transform);
            guideArrow.sizeDelta = new Vector2(64, 64);
            var arrowImg = NewRect("Arrow", guideArrow); arrowImg.sizeDelta = new Vector2(46, 46);
            arrowImg.gameObject.AddComponent<Image>().sprite = triangle; arrowImg.GetComponent<Image>().color = Red;
            var lab = NewRect("Label", canvas.transform);
            guideArrowLabel = AddText(lab, "", 16, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            guideArrowLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var sh = guideArrowLabel.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, .8f); sh.effectDistance = new Vector2(2, -2);
            lab.SetParent(guideArrow, false); lab.sizeDelta = new Vector2(220, 30);
            UpdateGuide();
        }

        void HideGuide()
        {
            guiding = false;
            if (guideMarker) Destroy(guideMarker.gameObject);
            if (guideArrow) Destroy(guideArrow.gameObject);
            guideMarker = guideArrow = null;
        }

        void UpdateGuide()
        {
            var cam = Camera.main;
            if (!cam || !guideMarker || !player) return;
            var top = new Vector3(guideTarget.center.x, guideTarget.max.y, 0);
            Vector3 sp = cam.WorldToScreenPoint(top);
            bool onScreen = sp.x > 40 && sp.x < Screen.width - 40 && sp.y > 40 && sp.y < Screen.height - 40;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var local);
            float bob = Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 10f;
            guideMarker.anchoredPosition = local + new Vector2(0, 14 + bob);
            guideMarker.gameObject.SetActive(onScreen);
            // 화면 밖이면 가장자리 화살표
            guideArrow.gameObject.SetActive(!onScreen);
            if (!onScreen)
            {
                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                var dir = ((Vector2)sp - center);
                if (sp.z < 0) dir = -dir;
                dir.Normalize();
                float m = 70;
                var edge = center + dir * Mathf.Min((Screen.width / 2f - m) / Mathf.Max(Mathf.Abs(dir.x), 1e-3f), (Screen.height / 2f - m) / Mathf.Max(Mathf.Abs(dir.y), 1e-3f));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, edge, null, out var el);
                guideArrow.anchoredPosition = el;
                guideArrow.GetChild(0).localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90);
                float dist = Vector2.Distance(player.transform.position, guideTarget.ClosestPoint(player.transform.position));
                guideArrowLabel.text = $"{guideName} · {Mathf.RoundToInt(dist)}m";
                guideArrowLabel.rectTransform.anchoredPosition = -dir * 46;
                guideArrow.GetChild(0).localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Time.time * 6f));
            }
        }

        // ── 화면 공통 ──────────────────────────────────────────
        void BuildCanvas()
        {
            var go = new GameObject("ScenarioUI");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;   // 글자를 화면 픽셀에 맞춰 또렷하게
            canvasRect = go.GetComponent<RectTransform>();
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
            // 위쪽 상태 글줄은 두지 않는다. 사건 위치는 길 안내(설비 위 표시·가장자리 화살표)로 알려 준다.
        }

        void SetStatus(string s) { if (status) status.text = s; }

        static RectTransform NewRect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        Image Round(GameObject go, Color c, float mult = 1.4f)
        {
            var img = go.AddComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = mult; img.color = c;
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
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeTriangleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float half = (size - y) * 0.5f;           // 위로 뾰족한 삼각형
                    float a = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - size / 2f) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        Text AddText(Transform parent, string s, int size, Color c, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = anchor;
            t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.15f;
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

        Sprite LoadSprite(string path)
        {
            if (sprites.TryGetValue(path, out var sp)) return sp;
            var bytes = Resources.Load<TextAsset>(path);
            if (!bytes) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(bytes.bytes);
            tex.filterMode = FilterMode.Point;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            sprites[path] = sp;
            return sp;
        }

        Sprite Avatar(string agent, string stance)
        {
            var mood = stance != null && Stance.TryGetValue(stance, out var s) ? s.mood : "base";
            return LoadSprite($"Avatars/{agent}_{mood}") ?? LoadSprite($"Portraits/{agent}");
        }

        static string C(string hex, string s) => $"<color={hex}>{s}</color>";
        static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        Button Button_(Transform parent, string label, Color bg, Color fg, float height)
        {
            var rt = NewRect("Button", parent);
            var img = Round(rt.gameObject, bg);
            LE(rt.gameObject, -1, height);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var t = AddText(rt, label, 18, fg, FontStyle.Bold, TextAnchor.MiddleCenter);
            var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            Relayout();
            return b;
        }

        // ── 회의 채팅방 ────────────────────────────────────────
        void OpenChat(ScenarioEvent ev, int index, int n)
        {
            CloseChat();
            // 가운데 채팅방 + 오른쪽에 붙은 선택 패널 (둘을 합친 폭을 화면 가운데에)
            const float chatW = 760, sideW = 330, gap = 12, top = 36, bottom = 36;
            float left = -(chatW + gap + sideW) / 2f;
            chat = NewRect("Chat", canvas.transform);
            chat.anchorMin = new Vector2(0.5f, 0); chat.anchorMax = new Vector2(0.5f, 1); chat.pivot = new Vector2(0, 0.5f);
            chat.offsetMin = new Vector2(left, bottom); chat.offsetMax = new Vector2(left + chatW, -top);
            Round(chat.gameObject, Hex("#262626", 0.98f));
            VLayout(chat.gameObject, 0, 0, 0);

            // 머리: 공장장 + 방 이름 + 경보 표시
            var head = NewRect("Header", chat);
            var hh = HLayout(head.gameObject, 12, TextAnchor.MiddleLeft); hh.padding = new RectOffset(22, 22, 14, 14);
            var hle = LE(head.gameObject); hle.flexibleHeight = 0;
            var hav = NewRect("Avatar", head); var hai = hav.gameObject.AddComponent<Image>(); hai.sprite = LoadSprite("Portraits/chief"); hai.preserveAspect = true; LE(hav.gameObject, 34, 44);
            var ht = AddText(head, $"<b>열연 2라인 긴급 회의 ({ev.agents.Length + 2})</b>", 20, Body, FontStyle.Normal, TextAnchor.MiddleLeft);
            LE(ht.gameObject, -1, -1, 1);
            var pill = NewRect("Pill", head);
            Round(pill.gameObject, Hex("#3A2224"));
            var ph = HLayout(pill.gameObject, 0, TextAnchor.MiddleCenter); ph.padding = new RectOffset(14, 14, 6, 6);
            var po = pill.gameObject.AddComponent<Outline>(); po.effectColor = Red; po.effectDistance = new Vector2(1.5f, -1.5f);
            var pt = AddText(pill, $"⚠ {ev.level} · 사건 {index + 1}/{n}", 15, Hex("#FFD6D6"), FontStyle.Bold, TextAnchor.MiddleCenter);
            pt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var line1 = NewRect("Line", chat); line1.gameObject.AddComponent<Image>().color = Hex("#333333"); LE(line1.gameObject, -1, 1).flexibleHeight = 0;

            // 고정된 사건 카드 (작게, 내용 높이만큼)
            var pinWrap = NewRect("PinWrap", chat);
            VLayout(pinWrap.gameObject, 22, 12, 0); LE(pinWrap.gameObject).flexibleHeight = 0;
            var pin = NewRect("Pinned", pinWrap);
            Round(pin.gameObject, Hex("#4A2426"));
            var pino = pin.gameObject.AddComponent<Outline>(); pino.effectColor = Hex("#C8504E"); pino.effectDistance = new Vector2(1.5f, -1.5f);
            VLayout(pin.gameObject, 18, 10, 2);
            AddText(pin, $"<b>⚠ {ev.title}</b>", 19, Color.white, FontStyle.Normal, TextAnchor.UpperCenter);
            AddText(pin, ev.desc, 15, Hex("#F3DEDE"), FontStyle.Normal, TextAnchor.UpperCenter);
            if (EfficiencyCalc.CarryApplies(ev, flags)) AddText(pin, ev.carry.note, 14, Hex("#FF9A9A"), FontStyle.Normal, TextAnchor.UpperCenter);

            var view = NewRect("Viewport", chat);
            view.gameObject.AddComponent<RectMask2D>();
            var vle = LE(view.gameObject); vle.flexibleHeight = 1; vle.minHeight = 120;
            msgs = NewRect("Messages", view);
            msgs.anchorMin = new Vector2(0, 1); msgs.anchorMax = new Vector2(1, 1); msgs.pivot = new Vector2(0.5f, 1);
            msgs.offsetMin = msgs.offsetMax = Vector2.zero;
            VLayout(msgs.gameObject, 22, 6, 18);
            msgs.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = msgs; scroll.viewport = view; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;

            // 오른쪽: 내 답장(선택지)
            side = NewRect("Reply", canvas.transform);
            side.anchorMin = new Vector2(0.5f, 0); side.anchorMax = new Vector2(0.5f, 1); side.pivot = new Vector2(0, 0.5f);
            side.offsetMin = new Vector2(left + chatW + gap, bottom); side.offsetMax = new Vector2(left + chatW + gap + sideW, -top);
            Round(side.gameObject, Hex("#262626", 0.98f));
            VLayout(side.gameObject, 18, 18, 12);
            AddText(side, "내 답장", 18, Body, FontStyle.Bold);
            var prog = NewRect("Progress", side); HLayout(prog.gameObject, 10, TextAnchor.MiddleLeft);
            progressText = AddText(prog, $"의견 0/{ev.agents.Length + 1}", 13, Dim);
            LE(progressText.gameObject, 70);
            var bar = NewRect("Bar", prog); Round(bar.gameObject, Hex("#3A3A3A"), 6f); LE(bar.gameObject, 10, 6, 1);
            progressFill = NewRect("Fill", bar); progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0, 1); progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
            Round(progressFill.gameObject, Teal, 6f);
            opts = NewRect("Options", side); VLayout(opts.gameObject, 0, 0, 10);
            for (int k = 0; k < ev.choices.Length; k++) OptionCard(ev, k);
            opts.gameObject.AddComponent<CanvasGroup>();
            SetLocked(true);
            var spacer = NewRect("Space", side); LE(spacer.gameObject).flexibleHeight = 1;
            helpText = AddText(side, "의견을 다 들으면 고를 수 있어요 · Space로 대사 넘기기", 13, Dim);
            Relayout();
        }

        void OptionCard(ScenarioEvent ev, int k)
        {
            var c = ev.choices[k];
            var rt = NewRect("Option", opts);
            var bg = Round(rt.gameObject, Hex("#2F3B39"));
            var bo = rt.gameObject.AddComponent<Outline>(); bo.effectColor = Hex("#3F5551"); bo.effectDistance = new Vector2(1.5f, -1.5f);
            var h = HLayout(rt.gameObject, 14, TextAnchor.MiddleLeft); h.padding = new RectOffset(16, 16, 14, 14);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
            var cs = b.colors; cs.highlightedColor = Hex("#E0FFF8"); cs.pressedColor = Hex("#C8EDE5"); cs.disabledColor = Color.white; b.colors = cs;
            var badge = NewRect("Badge", rt); Round(badge.gameObject, Teal, 0.6f); LE(badge.gameObject, 36, 36);
            var bt = AddText(badge, Abc[k], 19, Hex("#0B221E"), FontStyle.Bold, TextAnchor.MiddleCenter);
            var btr = bt.rectTransform; btr.anchorMin = Vector2.zero; btr.anchorMax = Vector2.one; btr.offsetMin = btr.offsetMax = Vector2.zero;
            var col = NewRect("Col", rt); VLayout(col.gameObject, 0, 0, 3); LE(col.gameObject, -1, -1, 1);
            AddText(col, c.label, 19, Color.white, FontStyle.Bold);
            var recs = new List<string>();
            foreach (var a in ev.agents)
                if (a.rec == c.id && Agents.TryGetValue(a.agent, out var v)) recs.Add(v.name);
            AddText(col, c.detail + (recs.Count > 0 ? $"  ·  {C(ColorHex(Teal), string.Join("·", recs) + " 추천")}" : ""), 14, Dim);
            int captured = k;
            b.onClick.AddListener(() => Choose(captured));
        }

        void SetLocked(bool locked)
        {
            if (!opts) return;
            var g = opts.GetComponent<CanvasGroup>();
            g.alpha = locked ? 0.35f : 1f; g.interactable = !locked; g.blocksRaycasts = !locked;
        }

        void UnlockChoices()
        {
            SetLocked(false);
            if (helpText) helpText.text = "클릭하거나 A·B·C 키로 고르세요";
        }

        void MarkChosen(int k)
        {
            if (!opts) return;
            var g = opts.GetComponent<CanvasGroup>(); g.interactable = false; g.blocksRaycasts = false; g.alpha = 1f;
            for (int i = 0; i < opts.childCount; i++)
            {
                var img = opts.GetChild(i).GetComponent<Image>();
                img.color = i == k ? Hex("#1F3330") : Hex("#24262B", 0.4f);
                foreach (var t in opts.GetChild(i).GetComponentsInChildren<Text>())
                    if (i != k) t.color = new Color(t.color.r, t.color.g, t.color.b, 0.35f);
            }
            if (helpText) helpText.text = "Agent 회의 중…";
        }

        void SetProgress(int heard, int total)
        {
            if (progressText) progressText.text = $"의견 {heard}/{total}";
            if (progressFill) progressFill.anchorMax = new Vector2(heard / (float)total, 1);
        }

        void CloseChat()
        {
            if (chat) Destroy(chat.gameObject);
            if (side) Destroy(side.gameObject);
            chat = msgs = side = opts = null;
        }

        void ScrollToBottom()
        {
            if (!msgs) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(msgs);
            if (scroll) scroll.verticalNormalizedPosition = 0;
        }

        void Relayout()
        {
            Canvas.ForceUpdateCanvases();
            if (chat) LayoutRebuilder.ForceRebuildLayoutImmediate(chat);
            if (side) LayoutRebuilder.ForceRebuildLayoutImmediate(side);
        }

        // 말풍선 하나: 입력 중 점 → 한 글자씩. stance가 있으면 표정·감정 색이 바뀐다.
        IEnumerator Say(string agent, string stance, string text, string sub, bool me)
        {
            if (!msgs) yield break;
            var (name, color, persona) = Agents.TryGetValue(agent, out var v) ? v : (agent, "#EEF0F3", "");
            var row = NewRect("Msg", msgs);
            var rh = HLayout(row.gameObject, 14, me ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
            rh.reverseArrangement = me;
            var ava = NewRect("Avatar", row);
            var ai = ava.gameObject.AddComponent<Image>(); ai.sprite = LoadSprite($"Portraits/{agent}"); ai.preserveAspect = true;
            LE(ava.gameObject, 58, 77);
            var bub = NewRect("Bubble", row);
            Round(bub.gameObject, me ? Teal : Hex("#333333"), 1.0f);
            VLayout(bub.gameObject, 20, 14, 5);
            LE(bub.gameObject, -1, -1, 1);
            // 이름 · 페르소나 칩 · 감정 배지(색 동그라미 안에 흰 글씨)
            var hr = NewRect("Head", bub); HLayout(hr.gameObject, 8, TextAnchor.MiddleLeft);
            var nm = AddText(hr, $"<b>{name}</b>", 16, me ? Hex("#0B221E") : Hex(color), FontStyle.Normal, TextAnchor.MiddleLeft);
            nm.horizontalOverflow = HorizontalWrapMode.Overflow;
            Chip(hr, persona, me ? Hex("#0B221E", 0.12f) : Hex("#FFFFFF", 0.08f), me ? Hex("#1D4A42") : Dim, false);
            if (stance != null && StanceBadge.TryGetValue(stance, out var badge)) Chip(hr, stance, badge, Color.white, true);
            var t = AddText(bub, "", 19, me ? Hex("#0B221E") : Body, me ? FontStyle.Bold : FontStyle.Normal);
            ScrollToBottom();

            float w = 0; skip = false;
            while (w < (me ? 0.25f : TypingDots) && !skip)
            {
                int d = Mathf.FloorToInt(w / 0.2f) % 3 + 1;
                t.text = C(me ? "#0B221E" : "#8A8F99", new string('●', d));
                w += Time.deltaTime; yield return null;
            }
            for (int i = 1; i <= text.Length && !skip; i++)
            {
                t.text = text.Substring(0, i) + C(me ? "#0B221E" : ColorHex(Teal), "▍");
                if (i % 4 == 0) ScrollToBottom();
                float c = 0; while (c < CharDelay && !skip) { c += Time.deltaTime; yield return null; }
            }
            t.text = text;
            if (!string.IsNullOrEmpty(sub)) AddText(bub, sub, 15, me ? Hex("#1D4A42") : Dim);
            ScrollToBottom();
            skip = false;
            yield return Wait(NextSpeaker);
            skip = false;
        }

        static readonly Dictionary<string, Color> StanceBadge = new Dictionary<string, Color>
        {
            { "불만", Hex("#E5533D") }, { "찬성", Hex("#3BAA6E") }, { "경고", Hex("#E0A020") },
        };

        void Chip(Transform parent, string label, Color bg, Color fg, bool bold)
        {
            var c = NewRect("Chip", parent);
            Round(c.gameObject, bg, 0.6f);
            var h = HLayout(c.gameObject, 0, TextAnchor.MiddleCenter); h.padding = new RectOffset(10, 10, 3, 3);
            var t = AddText(c, label, 13, fg, bold ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
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

            AddText(left, "운영 효율", 16, Dim, FontStyle.Bold);
            var big = AddText(left, "100%", 72, LossColor(0), FontStyle.Bold);
            var arrow = AddText(left, "100%  →  …", 16, Dim);
            var gauge = Gauge(left, 1f, Teal, 16);
            var sentence = AddText(left, "", 16, Body);
            var bars = new List<(Image fill, Text val, int loss)>();
            foreach (var (label, loss) in new[] { ("생산", r.totals.production), ("품질", r.totals.quality), ("안전", r.totals.safety), ("비용", r.totals.cost) })
                bars.Add(MetricBar(left, label, loss));

            AddText(right, "사건 기록", 16, Dim, FontStyle.Bold);
            var stamps = new List<RectTransform>();
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                var sc = NewRect("Stamp", right);
                Round(sc.gameObject, Bubble);
                var h = HLayout(sc.gameObject, 12, TextAnchor.MiddleLeft); h.padding = new RectOffset(16, 16, 10, 10);
                var txt = NewRect("Txt", sc); VLayout(txt.gameObject, 0, 0, 2); LE(txt.gameObject, -1, -1, 1);
                AddText(txt, $"<b>{i + 1}. {e.ev.title}</b>", 17, Body);
                AddText(txt, $"내 선택: {e.chosen.label}" + (e.chosen.best ? "" : C(ColorHex(Dim), $"  ·  최선: {e.best.label}")), 15, Body);
                AddText(txt, e.chosen.outcome, 13, Dim);
                if (e.carried) AddText(txt, $"{e.ev.carry.note} (효율 −{e.carryLoss}%)", 13, Red);
                var stamp = NewRect("Mark", sc);
                Round(stamp.gameObject, e.chosen.best ? Hex("#24402F") : Hex("#402424"));
                LE(stamp.gameObject, 108, 56);
                var stt = AddText(stamp, e.chosen.best ? "최선!" : $"−{e.Total}%", e.chosen.best ? 24 : 22, LossColor(e.Total), FontStyle.Bold, TextAnchor.MiddleCenter);
                var str = stt.rectTransform; str.anchorMin = Vector2.zero; str.anchorMax = Vector2.one; str.offsetMin = str.offsetMax = Vector2.zero;
                sc.gameObject.SetActive(false);
                stamps.Add(sc);
            }
            AddText(right, "AI 진단", 16, Teal, FontStyle.Bold);
            var diagRow = NewRect("Diag", right);
            HLayout(diagRow.gameObject, 12);
            var pic = NewRect("Avatar", diagRow);
            var pimg = pic.gameObject.AddComponent<Image>(); pimg.sprite = Avatar("chief", null); pimg.preserveAspect = true;
            LE(pic.gameObject, 52, 52);
            var bubble = NewRect("Bubble", diagRow);
            Round(bubble.gameObject, Bubble);
            VLayout(bubble.gameObject, 16, 10, 2);
            LE(bubble.gameObject, -1, -1, 1);
            AddText(bubble, $"{C(ColorHex(Yellow), "<b>공장장 AI</b>")} {C(ColorHex(Dim), "· 기본 진단 (LLM 연결 전)")}", 15, Body);
            var diag = AddText(bubble, "", 16, Body);
            diagRow.gameObject.SetActive(false);

            var restart = Button_(card, "다시 하기 (R)", Yellow, Hex("#1A1408"), 50);
            restart.onClick.AddListener(Restart);
            restart.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);

            yield return new WaitForSeconds(0.6f);
            float dur = 1.4f, tt = 0;
            var gfill = gauge.GetChild(0).GetComponent<RectTransform>();
            while (tt < dur)
            {
                tt += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, tt / dur);
                int shown = Mathf.RoundToInt(Mathf.Lerp(100, r.finalEfficiency, k));
                big.text = shown + "%";
                big.color = LossColor(100 - shown);
                gfill.anchorMax = new Vector2(shown / 100f, 1);
                foreach (var (fill, val, loss) in bars)
                {
                    fill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(100, 100 - loss, k) / 100f, 1);
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
            restart.gameObject.SetActive(true);
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
            Round(bg.gameObject, Hex("#3A3A3A"), 4f);
            LE(bg.gameObject, -1, height);
            var fill = NewRect("Fill", bg);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Round(fill.gameObject, color, 4f);
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
