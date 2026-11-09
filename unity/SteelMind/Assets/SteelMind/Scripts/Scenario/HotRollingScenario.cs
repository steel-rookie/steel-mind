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
    /// 사건 발생 → 설비 위 표시와 화면 가장자리 화살표로 길 안내 → 설비에 다가가면 미연시 회의(캐릭터가 한 명씩 말하고 가운데 선택지 A·B·C)
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
        bool deciding, chatting, skip, showingResult, guiding, finished;
        Coroutine resume, fade;
        AudioSource normalBgm, alarmBgm;
        float normalVolume = 0.5f;

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
            font = Resources.Load<Font>("Fonts/Galmuri11") ?? Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "AppleGothic", "Arial" }, 20);   // 도트 글꼴 갈무리(OFL)
            round = MakeRoundSprite(64, 18);
            triangle = MakeTriangleSprite(48);
            BuildCanvas();
            SetupMusic();
            session = FindFirstObjectByType<SessionState>();
            if (!session) { session = new GameObject("SteelMindSession").AddComponent<SessionState>(); DontDestroyOnLoad(session.gameObject); }
            if (session.titleShown) Restart();
            else StartCoroutine(TitleThenIntro());
        }

        // ── 메인 화면과 인트로 (플레이할 때 한 번만) ─────────────
        SessionState session;
        RectTransform title;

        // 메인 화면은 Title 장면이 맡는다. 여기서는 공장에 처음 들어왔을 때 인트로만 한 번 보여 준다.
        IEnumerator TitleThenIntro()
        {
            SetFrozen(true);
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            yield return new WaitForSeconds(0.6f);
            yield return Intro();
            session.titleShown = true;
            Restart();
        }

        IEnumerator Intro()
        {
            chatting = true; skip = false;
            OpenVN("열연 2라인 · 야간 교대 시작", false);
            yield return Say(null, null, "열연 2라인, 야간 교대.\n오늘 당신은 처음으로 이 라인의 운영 책임자를 맡았습니다.", null, false);
            yield return Say(null, null, "이 공장에는 부서마다 AI Agent가 있습니다.\n모두 맞는 말을 하지만, 서로 다른 것을 지키려 합니다.", null, false);
            yield return Say("production", null, "생산 담당 AI입니다. 납기와 물량은 제가 챙깁니다. 라인은 멈추면 안 돼요.", null, false);
            yield return Say("quality", null, "품질 담당이에요. 0.01mm라도 어긋나면 불량입니다. 조건부터 잡아 주세요.", null, false);
            yield return Say("equipment", null, "설비 담당입니다. 롤 소리만 들어도 상태를 알죠. 무리하면 결국 더 오래 멈춥니다.", null, false);
            yield return Say("safety", null, "안전 담당입니다. 사람이 먼저입니다. 그건 타협 못 합니다.", null, false);
            yield return Say(null, null, "사건이 터지면 화살표를 따라 그 설비로 가세요.\nAI들의 의견을 듣고, 최종 결정은 당신이 내립니다.", null, false);
            yield return Say(null, null, "같은 선택은 언제나 같은 결과를 냅니다.\n교대가 끝나면 운영 결과 보고서를 받게 됩니다.", null, false);
            CloseChat();
            chatting = false;
        }

        void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }

        // ── 진행 ───────────────────────────────────────────────
        void Restart()
        {
            StopAllCoroutines();
            resume = null;
            picks.Clear(); flags.Clear();
            current = -1; deciding = chatting = showingResult = guiding = finished = false;
            CloseChat(); HideGuide();
            if (overlay) Destroy(overlay.gameObject);
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            if (process) process.speed = 1f;
            SetFrozen(false);
            if (normalBgm) { normalBgm.volume = normalVolume; if (!normalBgm.isPlaying) normalBgm.Play(); }
            if (alarmBgm) { alarmBgm.volume = 0; alarmBgm.Stop(); }
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
            Music(true);
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

        // 미연시 회의: 경보 → Agent가 한 명씩 나와 말함(클릭·Space로 넘김) → 선택 → 내 말 → 반응
        IEnumerator EventChat(int i)
        {
            current = i; chatting = true; skip = false;
            var ev = def.events[i];
            int n = def.events.Length;
            SetFrozen(true);
            OpenChat(ev, i, n);
            var alarm = $"⚠ {ev.title}\n{ev.desc}";
            yield return Say(null, null, alarm, EfficiencyCalc.CarryApplies(ev, flags) ? ev.carry.note : null, false);
            foreach (var a in ev.agents) yield return Say(a.agent, null, a.say, a.why, false);
            yield return Say(null, null, $"쟁점 · {ev.chief}\n어떻게 하시겠습니까?", null, false);
            UnlockChoices();
            deciding = true;
            while (deciding) yield return null;

            var c = ev.choices[picks[i]];
            yield return Say("player", null, $"{c.label}.", c.detail, true);
            foreach (var r in ReportBuilder.ReactionsFor(ev, c)) yield return Say(r.agent, r.stance, r.line, null, false);
            CloseChat();
            chatting = false;
            SetFrozen(false);
            Music(false);
        }

        // ── 음악: 평소 공장 음악 ↔ 사건 경보 음악 (서로 서서히 바뀜) ──
        void SetupMusic()
        {
            var cam = Camera.main;
            if (cam) normalBgm = cam.GetComponent<AudioSource>();
            if (normalBgm) normalVolume = normalBgm.volume;
            var clip = Resources.Load<AudioClip>("Audio/bgm_alarm");
            if (!clip) return;
            alarmBgm = gameObject.AddComponent<AudioSource>();
            alarmBgm.clip = clip; alarmBgm.loop = true; alarmBgm.playOnAwake = false; alarmBgm.volume = 0;
        }

        void Music(bool alarm)
        {
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(Crossfade(alarm, alarm ? 0.6f : 1.5f));
        }

        IEnumerator Crossfade(bool alarm, float dur)
        {
            if (alarm && alarmBgm && !alarmBgm.isPlaying) alarmBgm.Play();
            if (!alarm && normalBgm && !normalBgm.isPlaying) normalBgm.Play();
            float n0 = normalBgm ? normalBgm.volume : 0, a0 = alarmBgm ? alarmBgm.volume : 0, t = 0;
            while (t < dur)
            {
                t += Time.deltaTime; float k = t / dur;
                if (normalBgm) normalBgm.volume = Mathf.Lerp(n0, alarm ? 0 : normalVolume, k);
                if (alarmBgm) alarmBgm.volume = Mathf.Lerp(a0, alarm ? 0.55f : 0, k);
                yield return null;
            }
            if (!alarm && alarmBgm) alarmBgm.Stop();
            fade = null;
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
            else if ((showingResult || finished) && k.rKey.wasPressedThisFrame) Restart();
            else if (showingResult && k.escapeKey.wasPressedThisFrame) { if (runLog) { Destroy(runLog.gameObject); runLog = null; } else CloseReport(); }
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
            return b;
        }

        // ── 미연시(비주얼 노벨) 회의 ──────────────────────────
        RectTransform vn, vnChoices, vnLog, vnPlate, vnNext;
        Image actorL, actorR, vnBadgeBg;
        string whoL, whoR;
        int nextSide;
        Text vnName, vnPersona, vnBadge, vnLine, vnWhy, vnLogText;
        bool fastAll;
        readonly List<string> vnHistory = new List<string>();

        void OpenChat(ScenarioEvent ev, int index, int n) => OpenVN($"⚠ {ev.level} · 사건 {index + 1}/{n} · {ev.title}", true);

        void OpenVN(string pillText, bool alarm)
        {
            CloseChat();
            fastAll = false; whoL = whoR = null; nextSide = 0;
            vn = NewRect("VisualNovel", canvas.transform);
            vn.anchorMin = Vector2.zero; vn.anchorMax = Vector2.one; vn.offsetMin = vn.offsetMax = Vector2.zero;
            // 아래쪽을 살짝 어둡게
            var shade = NewRect("Shade", vn); shade.anchorMin = Vector2.zero; shade.anchorMax = new Vector2(1, 0.55f); shade.offsetMin = shade.offsetMax = Vector2.zero;
            shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.28f);

            // 위: 경보 알약
            var pill = NewRect("Pill", vn); pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 1); pill.anchoredPosition = new Vector2(0, -16);
            Round(pill.gameObject, alarm ? Hex("#3C1616", 0.92f) : Hex("#1F2A28", 0.92f), 1.3f);
            var po = pill.gameObject.AddComponent<Outline>(); po.effectColor = alarm ? Red : Teal; po.effectDistance = new Vector2(1, -1);
            var ph = HLayout(pill.gameObject, 0, TextAnchor.MiddleCenter); ph.padding = new RectOffset(18, 18, 7, 7);
            var pf = pill.gameObject.AddComponent<ContentSizeFitter>(); pf.horizontalFit = pf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var pt = AddText(pill, $"<b>{pillText}</b>", 17, alarm ? Hex("#FFE2E2") : Hex("#DFF6F0"), FontStyle.Normal, TextAnchor.MiddleCenter);
            pt.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 캐릭터 자리 (왼쪽·오른쪽)
            actorL = Actor("ActorL", new Vector2(0.2f, 0)); actorR = Actor("ActorR", new Vector2(0.8f, 0));

            // 대화창
            var box = NewRect("Dialog", vn); box.anchorMin = new Vector2(0, 0); box.anchorMax = new Vector2(1, 0); box.pivot = new Vector2(0.5f, 0);
            box.sizeDelta = new Vector2(0, 220); box.anchoredPosition = Vector2.zero;
            box.gameObject.AddComponent<Image>().color = Hex("#111216", 0.94f);
            var top = NewRect("TopLine", box); top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1); top.sizeDelta = new Vector2(0, 2);
            top.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.18f);
            var click = box.gameObject.AddComponent<Button>(); click.transition = Selectable.Transition.None; click.onClick.AddListener(() => skip = true);

            vnPlate = NewRect("NamePlate", box); vnPlate.anchorMin = vnPlate.anchorMax = new Vector2(0, 1); vnPlate.pivot = new Vector2(0, 0.5f);
            vnPlate.anchoredPosition = new Vector2(150, 0);
            Round(vnPlate.gameObject, Hex("#EEF0F3"), 2.4f);
            var nh = HLayout(vnPlate.gameObject, 10, TextAnchor.MiddleCenter); nh.padding = new RectOffset(28, 28, 6, 6);
            var nf = vnPlate.gameObject.AddComponent<ContentSizeFitter>(); nf.horizontalFit = nf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            vnName = AddText(vnPlate, "", 24, Hex("#24272D"), FontStyle.Bold, TextAnchor.MiddleCenter); vnName.horizontalOverflow = HorizontalWrapMode.Overflow;
            vnPersona = AddText(vnPlate, "", 16, Hex("#6B7280"), FontStyle.Bold, TextAnchor.MiddleCenter); vnPersona.horizontalOverflow = HorizontalWrapMode.Overflow;
            var badge = NewRect("Badge", vnPlate); vnBadgeBg = Round(badge.gameObject, Red, 1.8f);
            var bh = HLayout(badge.gameObject, 0, TextAnchor.MiddleCenter); bh.padding = new RectOffset(10, 10, 2, 2);
            vnBadge = AddText(badge, "", 16, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter); vnBadge.horizontalOverflow = HorizontalWrapMode.Overflow;

            var body = NewRect("Body", box); body.anchorMin = new Vector2(0.14f, 0); body.anchorMax = new Vector2(0.86f, 1); body.offsetMin = new Vector2(0, 20); body.offsetMax = new Vector2(0, -44);
            VLayout(body.gameObject, 0, 0, 6);
            vnLine = AddText(body, "", 30, Hex("#F3F4F6"));
            vnWhy = AddText(body, "", 21, Hex("#A6ABB6"));
            vnNext = NewRect("Next", box); vnNext.anchorMin = vnNext.anchorMax = new Vector2(1, 0); vnNext.anchoredPosition = new Vector2(-80, 30); vnNext.sizeDelta = new Vector2(18, 14);
            var ni = vnNext.gameObject.AddComponent<Image>(); ni.sprite = triangle; ni.color = Color.white; vnNext.localRotation = Quaternion.Euler(0, 0, 180);

            // 로그·스킵
            // 게임 중 대화 기록(로그) 버튼은 두지 않는다
            CircleButton("▶▶\n스킵", new Vector2(1, 1), new Vector2(-46, -46), () => { fastAll = true; skip = true; });
            vnHistory.Clear();
        }

        Image Actor(string name, Vector2 anchor)
        {
            var rt = NewRect(name, vn); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 150); rt.sizeDelta = new Vector2(300, 400);
            var img = rt.gameObject.AddComponent<Image>(); img.preserveAspect = true; img.color = new Color(1, 1, 1, 0);
            return img;
        }

        void CircleButton(string label, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var rt = NewRect("Circle", vn); rt.anchorMin = rt.anchorMax = anchor; rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(60, 60);
            var img = Round(rt.gameObject, Hex("#E9EBEF"), 0.6f);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(onClick);
            var t = AddText(rt, $"<b>{label}</b>", 12, Hex("#2A2D33"), FontStyle.Normal, TextAnchor.MiddleCenter);
            var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        }

        void ToggleLog()
        {
            if (!vnLog) return;
            bool on = !vnLog.gameObject.activeSelf;
            vnLog.gameObject.SetActive(on);
            if (on) vnLogText.text = string.Join("\n\n", vnHistory);
        }

        // 말하는 캐릭터를 왼쪽·오른쪽에 번갈아 세우고, 말하는 쪽만 밝게
        IEnumerator Place(string who)
        {
            if (who == null) { Tint(actorL, whoL != null, false); Tint(actorR, whoR != null, false); yield break; }
            Image slot = whoL == who ? actorL : whoR == who ? actorR : null;
            if (!slot)
            {
                slot = nextSide++ % 2 == 0 ? actorL : actorR;
                if (slot == actorL) whoL = who; else whoR = who;
                slot.sprite = LoadSprite($"Portraits/{who}");
                var rt = slot.rectTransform; var home = new Vector2(0, 150); var from = home + new Vector2(slot == actorL ? -120 : 120, 0);
                float t = 0;
                while (t < 0.3f && !fastAll) { t += Time.deltaTime; float k = Mathf.SmoothStep(0, 1, t / 0.3f); rt.anchoredPosition = Vector2.Lerp(from, home, k); slot.color = new Color(1, 1, 1, k); yield return null; }
                rt.anchoredPosition = home;
            }
            Tint(actorL, whoL != null, slot == actorL);
            Tint(actorR, whoR != null, slot == actorR);
        }

        static void Tint(Image img, bool visible, bool talking)
        {
            if (!img) return;
            float v = talking ? 1f : 0.45f;
            img.color = visible ? new Color(v, v, v, 1) : new Color(1, 1, 1, 0);
            img.rectTransform.localScale = talking ? new Vector3(1.04f, 1.04f, 1) : Vector3.one;
        }

        // 대사 한 줄: 이름표 → 한 글자씩 → ▼ → 클릭·Space로 다음
        IEnumerator Say(string agent, string stance, string text, string sub, bool me)
        {
            if (!vn) yield break;
            bool narr = agent == null;
            yield return Place(narr ? null : agent);
            vnPlate.gameObject.SetActive(!narr);
            if (!narr)
            {
                // 이름표를 말하는 캐릭터 바로 아래에
                float ax = whoR == agent ? 0.8f : 0.2f;
                vnPlate.anchorMin = vnPlate.anchorMax = new Vector2(ax, 1); vnPlate.pivot = new Vector2(0.5f, 0.5f); vnPlate.anchoredPosition = Vector2.zero;
                var (name, color, persona) = Agents.TryGetValue(agent, out var v) ? v : (agent, "#EEF0F3", "");
                vnName.text = name; vnPersona.text = persona;
                bool hasBadge = stance != null && StanceBadge.TryGetValue(stance, out var bc);
                vnBadgeBg.gameObject.SetActive(hasBadge);
                if (hasBadge) { vnBadgeBg.color = StanceBadge[stance]; vnBadge.text = stance; }
                LayoutRebuilder.ForceRebuildLayoutImmediate(vnPlate);
            }
            vnLine.alignment = narr ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            vnWhy.alignment = vnLine.alignment;
            vnLine.color = narr ? Hex("#FFD7D7") : Hex("#F3F4F6");
            vnWhy.text = "";
            vnNext.gameObject.SetActive(false);
            var who = narr ? "<color=#FF9A9A><b>경보</b></color>" : $"<b>{(Agents.TryGetValue(agent, out var a2) ? a2.name : agent)}</b>{(stance != null ? $" ({stance})" : "")}";
            vnHistory.Add($"{who}  {text}");

            skip = fastAll;
            for (int i = 1; i <= text.Length && !skip; i++)
            {
                vnLine.text = text.Substring(0, i);
                float c = 0; while (c < CharDelay && !skip) { c += Time.deltaTime; yield return null; }
            }
            vnLine.text = text;
            vnWhy.text = sub ?? "";
            vnNext.gameObject.SetActive(true);
            skip = false;
            if (fastAll) { yield return null; yield break; }
            float bob = 0;
            while (!skip)
            {
                bob += Time.deltaTime;
                vnNext.anchoredPosition = new Vector2(-80, 30 - Mathf.Abs(Mathf.Sin(bob * 5f)) * 6f);
                yield return null;
            }
            skip = false;
        }

        // 선택지: 화면을 어둡게 하고 가운데 큰 판 3개
        void UnlockChoices()
        {
            var ev = def.events[current];
            fastAll = false;
            vnChoices = NewRect("Choices", vn); vnChoices.anchorMin = Vector2.zero; vnChoices.anchorMax = Vector2.one; vnChoices.offsetMin = vnChoices.offsetMax = Vector2.zero;
            vnChoices.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.4f);
            var col = NewRect("Col", vnChoices); col.anchorMin = col.anchorMax = new Vector2(0.5f, 0.62f); col.pivot = new Vector2(0.5f, 0.5f); col.sizeDelta = new Vector2(820, 0);
            VLayout(col.gameObject, 0, 0, 14);
            col.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            for (int k = 0; k < ev.choices.Length; k++)
            {
                var c = ev.choices[k];
                var recs = new List<string>();
                foreach (var a in ev.agents) if (a.rec == c.id && Agents.TryGetValue(a.agent, out var v)) recs.Add(v.name);
                var rt = NewRect("Choice", col);
                var bg = Round(rt.gameObject, Hex("#F4F5F7"), 2.2f);
                var h = HLayout(rt.gameObject, 16, TextAnchor.MiddleLeft); h.padding = new RectOffset(20, 40, 12, 12);
                var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
                var cs = b.colors; cs.highlightedColor = Hex("#FFF4CC"); cs.pressedColor = Hex("#F2DE9A"); b.colors = cs;
                var badge = NewRect("Key", rt); Round(badge.gameObject, Hex("#2A2D33"), 1.2f); LE(badge.gameObject, 40, 40);
                var kt = AddText(badge, $"<b>{Abc[k]}</b>", 19, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
                var kr = kt.rectTransform; kr.anchorMin = Vector2.zero; kr.anchorMax = Vector2.one; kr.offsetMin = kr.offsetMax = Vector2.zero;
                var tc = NewRect("Text", rt); VLayout(tc.gameObject, 0, 0, 2); LE(tc.gameObject, -1, -1, 1);
                AddText(tc, $"<b>{c.label}</b>", 26, Hex("#262A31"), FontStyle.Normal, TextAnchor.MiddleCenter);
                AddText(tc, c.detail + (recs.Count > 0 ? $"  ·  <color=#0F7A66><b>{string.Join("·", recs)} 추천</b></color>" : ""), 17, Hex("#5D636E"), FontStyle.Normal, TextAnchor.MiddleCenter);
                int captured = k;
                b.onClick.AddListener(() => Choose(captured));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(col);
        }

        void MarkChosen(int k)
        {
            if (vnChoices) Destroy(vnChoices.gameObject);
            vnChoices = null;
        }

        void CloseChat()
        {
            if (vn) Destroy(vn.gameObject);
            vn = vnChoices = vnLog = null;
        }

        static readonly Dictionary<string, Color> StanceBadge = new Dictionary<string, Color>
        {
            { "불만", Hex("#E5533D") }, { "찬성", Hex("#3BAA6E") }, { "경고", Hex("#E0A020") },
        };

        // ── 결과 보고서 ────────────────────────────────────────
        const string HistoryKey = "steelmind.history";
        const string RunsKey = "steelmind.runs";

        [System.Serializable] class RunRecord { public int no; public string when, path, type, title; public int efficiency, bestCount, events; public bool accident; public string[] choices; public string[] events_; public int[] losses; public bool[] best; public string[] bestLabels; public string[] carryNotes; }
        [System.Serializable] class RunList { public List<RunRecord> runs = new List<RunRecord>(); }

        static RunList LoadRuns()
        {
            var json = PlayerPrefs.GetString(RunsKey, "");
            if (string.IsNullOrEmpty(json)) return new RunList();
            try { return JsonUtility.FromJson<RunList>(json) ?? new RunList(); } catch { return new RunList(); }
        }

        static void SaveRuns(RunList list) { PlayerPrefs.SetString(RunsKey, JsonUtility.ToJson(list)); PlayerPrefs.Save(); }

        RunRecord SaveRun(Report rep)
        {
            var list = LoadRuns();
            var rec = new RunRecord
            {
                no = list.runs.Count == 0 ? 1 : list.runs[list.runs.Count - 1].no + 1,
                when = System.DateTime.Now.ToString("MM/dd HH:mm"), path = rep.pathCode, type = rep.type, title = def.title,
                efficiency = rep.run.finalEfficiency, bestCount = rep.bestCount, events = def.events.Length, accident = rep.accident,
                choices = rep.run.events.ConvertAll(e => e.chosen.label).ToArray(), events_ = rep.run.events.ConvertAll(e => e.ev.title).ToArray(),
                losses = rep.run.events.ConvertAll(e => e.Total).ToArray(), best = rep.run.events.ConvertAll(e => e.chosen.best).ToArray(),
                bestLabels = rep.run.events.ConvertAll(e => e.best.label).ToArray(), carryNotes = rep.run.events.ConvertAll(e => e.carried ? e.ev.carry.note : "").ToArray(),
            };
            list.runs.Add(rec); SaveRuns(list);
            return rec;
        }
        static readonly Color CardBg = Hex("#221F1D"), CardLine = Hex("#3A3634"), Tile = Hex("#2C2826");

        RectTransform ReportCard(Transform parent, int pad = 22, float spacing = 10)
        {
            var c = NewRect("Card", parent);
            Round(c.gameObject, CardBg, 1.2f);
            var o = c.gameObject.AddComponent<Outline>(); o.effectColor = CardLine; o.effectDistance = new Vector2(1, -1);
            VLayout(c.gameObject, pad, pad - 4, spacing);
            return c;
        }

        void AiTag(Transform parent, string label)
        {
            var row = NewRect("Label", parent); HLayout(row.gameObject, 8, TextAnchor.MiddleLeft);
            AddText(row, label, 15, Dim, FontStyle.Bold, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
            var chip = NewRect("Chip", row); Round(chip.gameObject, Hex("#1F3A33"), 1.8f);
            var h = HLayout(chip.gameObject, 0, TextAnchor.MiddleCenter); h.padding = new RectOffset(8, 8, 2, 2);
            AddText(chip, "기본 문장", 12, Teal, FontStyle.Bold, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        IEnumerator ResultScreen()
        {
            var rep = ReportBuilder.Build(def, picks);
            var r = rep.run;
            var record = SaveRun(rep);
            showingResult = true; finished = true;
            overlay = NewRect("Report", canvas.transform);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = Hex("#141312", 0.97f);
            var view = NewRect("Viewport", overlay); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one; view.offsetMin = view.offsetMax = Vector2.zero;
            view.gameObject.AddComponent<RectMask2D>();
            var page = NewRect("Page", view); page.anchorMin = page.anchorMax = new Vector2(0.5f, 1); page.pivot = new Vector2(0.5f, 1); page.sizeDelta = new Vector2(1040, 0);
            var pv = VLayout(page.gameObject, 0, 28, 16); pv.padding = new RectOffset(0, 0, 28, 110);
            page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = page; sr.viewport = view; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;

            // 머리
            AddText(page, $"<b>STEEL MIND · 결과 보고서 · {record.no}회차</b>", 14, Orange);
            AddText(page, "<b>운영 결과 보고서</b>", 34, Color.white);
            AddText(page, "틀과 숫자는 규칙으로 바로 계산하고, 문장 칸은 기본 문장으로 먼저 보여 줍니다. 모든 값은 데모용 가상 데이터입니다.", 15, Dim);

            // 1줄: 운영 성향 | 운영 효율
            var row1 = NewRect("Row1", page); var h1 = HLayout(row1.gameObject, 16); h1.childForceExpandWidth = true; h1.childForceExpandHeight = true;
            var typeCard = ReportCard(row1); LE(typeCard.gameObject, 10, -1, 1);
            AddText(typeCard, "운영 성향", 15, Dim, FontStyle.Bold);
            AddText(typeCard, $"<b>{rep.type}</b>", 44, Color.white);
            AddText(typeCard, rep.typeDesc, 17, Body);
            AddText(typeCard, $"결과 ID  <b>{rep.resultId}</b>", 14, Dim);
            var effCard = ReportCard(row1); LE(effCard.gameObject, 10, -1, 1);
            AddText(effCard, "운영 효율", 15, Dim, FontStyle.Bold);
            var big = AddText(effCard, "100%", 52, LossColor(0), FontStyle.Bold);
            var gauge = Gauge(effCard, 1f, LossColor(r.drop), 10);
            AddText(effCard, $"{rep.totalPaths}가지 경로 중 {rep.rank}위 · 상위 {rep.topPercent}% · 최선의 선택 {rep.bestCount}/{def.events.Length}", 14, Dim);
            var tiles = NewRect("Tiles", effCard); var th = HLayout(tiles.gameObject, 8); th.childForceExpandWidth = true;
            foreach (var (label, loss) in new[] { ("생산 손실", r.totals.production), ("품질 손실", r.totals.quality), ("안전 손실", r.totals.safety), ("비용 손실", r.totals.cost) })
            {
                var t = NewRect("Tile", tiles); Round(t.gameObject, Tile, 1.4f); VLayout(t.gameObject, 8, 8, 2); LE(t.gameObject, 10, -1, 1);
                AddText(t, label, 13, Dim, FontStyle.Normal, TextAnchor.UpperCenter);
                AddText(t, $"<b>{(loss > 0 ? "−" + loss : "0")}</b>", 20, LossColor(loss), FontStyle.Normal, TextAnchor.UpperCenter);
            }

            // 2줄: 레이더 + 한 줄 총평 + 결정적 순간
            var c2 = ReportCard(page); var h2 = c2.gameObject.GetComponent<VerticalLayoutGroup>();
            var row2 = NewRect("Row2", c2); var r2 = HLayout(row2.gameObject, 24, TextAnchor.MiddleLeft);
            var radarBox = NewRect("Radar", row2); LE(radarBox.gameObject, 260, 240);
            var chart = NewRect("Chart", radarBox); chart.anchorMin = chart.anchorMax = new Vector2(0.5f, 0.5f); chart.sizeDelta = new Vector2(150, 150);
            var rc = chart.gameObject.AddComponent<RadarChart>(); rc.values = rep.radar;
            string[] axes = { "생산", "품질", "안전", "비용", "최선 선택" };
            for (int i = 0; i < axes.Length; i++)
            {
                float a = Mathf.PI / 2f - i * 2f * Mathf.PI / axes.Length;
                var lab = NewRect("Axis", radarBox); lab.anchorMin = lab.anchorMax = new Vector2(0.5f, 0.5f); lab.sizeDelta = new Vector2(90, 22);
                lab.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 100f;
                var lt = AddText(lab, axes[i], 14, Dim, FontStyle.Normal, TextAnchor.MiddleCenter);
                var ltr = lt.rectTransform; ltr.anchorMin = Vector2.zero; ltr.anchorMax = Vector2.one; ltr.offsetMin = ltr.offsetMax = Vector2.zero;
            }
            var col2 = NewRect("Col", row2); VLayout(col2.gameObject, 0, 0, 8); LE(col2.gameObject, -1, -1, 1);
            AiTag(col2, "한 줄 총평");
            AddText(col2, $"<b>{rep.headline}</b>", 23, Color.white);
            AiTag(col2, "결정적 순간");
            foreach (var m in rep.moments) AddText(col2, $"{C(ColorHex(Orange), "●")}  {m}", 15, Body);

            // 3줄: Agent 회의
            var c3 = ReportCard(page);
            AiTag(c3, "Agent 회의");
            var row3 = NewRect("Agents", c3); var h3 = HLayout(row3.gameObject, 12); h3.childForceExpandWidth = true; h3.childForceExpandHeight = true;
            foreach (var v in rep.agents)
            {
                var (name, color, persona) = Agents[v.agent];
                var card = NewRect("Agent", row3); Round(card.gameObject, Tile, 1.4f); LE(card.gameObject, 10, -1, 1);
                var cv = VLayout(card.gameObject, 14, 0, 6); cv.padding = new RectOffset(14, 14, 0, 14);
                var bar = NewRect("Top", card); bar.gameObject.AddComponent<Image>().color = Hex(color); LE(bar.gameObject, -1, 3);
                var hd = NewRect("Head", card); HLayout(hd.gameObject, 6, TextAnchor.MiddleLeft);
                var nt = AddText(hd, $"<b>{name} Agent</b>", 16, Hex(color), FontStyle.Normal, TextAnchor.MiddleLeft); LE(nt.gameObject, -1, -1, 1);
                AddText(hd, $"추천을 따른 사건 {v.followed}/{v.present}", 12, Dim, FontStyle.Normal, TextAnchor.MiddleRight).horizontalOverflow = HorizontalWrapMode.Overflow;
                var moodColor = v.mood == "만족" ? Hex("#6FCF97") : v.mood == "불만" ? Hex("#FF6B6B") : Dim;
                AddText(card, $"<b>{v.mood}</b>", 16, moodColor);
                AddText(card, v.comment, 15, Body);
            }

            // 4줄: 사건별 기록
            var c4 = ReportCard(page, 22, 0);
            AddText(c4, "<b>사건별 기록</b>", 17, Body);
            Spacer(c4, 8);
            TableRow(c4, "사건", "고른 행동", "최선의 행동", "효율 손실", Dim, Dim, Dim, Dim, true);
            foreach (var e in r.events)
            {
                TableRow(c4, e.ev.title, e.chosen.label, e.chosen.best ? "같음" : e.best.label, e.Total > 0 ? $"−{e.Total}%" : "0%",
                    Body, Body, e.chosen.best ? Hex("#6FCF97") : Body, LossColor(e.Total), false);
                if (e.carried) AddText(c4, $"   ↳ {e.ev.carry.note} (−{e.carryLoss}%)", 14, Red);
            }

            // 5줄: 다음 판 제안 | 회차별 성향 분포
            var row5 = NewRect("Row5", page); var h5 = HLayout(row5.gameObject, 16); h5.childForceExpandWidth = true; h5.childForceExpandHeight = true;
            var sug = ReportCard(row5); LE(sug.gameObject, 10, -1, 1);
            AiTag(sug, "다음 판 제안");
            AddText(sug, $"<b>{rep.suggestion}</b>", 21, Color.white);
            var hist = ReportCard(row5); LE(hist.gameObject, 10, -1, 1);
            AddText(hist, "회차별 성향 분포", 15, Dim, FontStyle.Bold);
            var bars = NewRect("Bars", hist); VLayout(bars.gameObject, 0, 4, 8);
            var note = AddText(hist, "", 13, Dim);

            // 회차 기록 (모든 판의 선택과 결과)
            var runsCard = ReportCard(page, 22, 0);
            var rh = NewRect("Head", runsCard); HLayout(rh.gameObject, 8, TextAnchor.MiddleLeft);
            var rt = AddText(rh, "<b>회차 기록</b>  <color=#A3A8B2>끝낸 판은 자동으로 저장됩니다 (최근 순)</color>", 17, Body, FontStyle.Normal, TextAnchor.MiddleLeft); LE(rt.gameObject, -1, -1, 1);
            var clearBtn = SmallButton(rh, "기록 지우기");
            var runsBox = NewRect("Runs", runsCard); VLayout(runsBox.gameObject, 0, 8, 0);
            System.Action draw = () => { DrawHistory(bars, note, rep.type); DrawRuns(runsBox, record.no); };
            clearBtn.onClick.AddListener(() => { SaveRuns(new RunList()); draw(); });
            draw();

            // 아래 고정 버튼 막대: 닫기 · 다시 플레이
            var barRt = NewRect("Actions", overlay); barRt.anchorMin = new Vector2(0, 0); barRt.anchorMax = new Vector2(1, 0); barRt.pivot = new Vector2(0.5f, 0);
            barRt.sizeDelta = new Vector2(0, 84);
            barRt.gameObject.AddComponent<Image>().color = Hex("#141312", 0.96f);
            var line2 = NewRect("Line", barRt); line2.anchorMin = new Vector2(0, 1); line2.anchorMax = new Vector2(1, 1); line2.pivot = new Vector2(0.5f, 1); line2.sizeDelta = new Vector2(0, 1);
            line2.gameObject.AddComponent<Image>().color = CardLine;
            var btns = NewRect("Buttons", barRt); btns.anchorMin = btns.anchorMax = new Vector2(0.5f, 0.5f); btns.sizeDelta = new Vector2(1040, 52);
            var bh2 = HLayout(btns.gameObject, 12, TextAnchor.MiddleRight); bh2.childForceExpandHeight = true;
            var info = AddText(btns, $"{record.no}회차 · {r.finalEfficiency}% · {rep.type}  <color=#A3A8B2>저장됨</color>", 16, Body, FontStyle.Normal, TextAnchor.MiddleLeft); LE(info.gameObject, -1, -1, 1);
            var logBtn = Button_(btns, "이전 기록", Tile, Body, 52); LE(logBtn.gameObject, 160, 52);
            logBtn.onClick.AddListener(() => OpenRunLog(record.no));
            var close = Button_(btns, "닫기 (Esc)", Tile, Body, 52); LE(close.gameObject, 170, 52);
            close.onClick.AddListener(CloseReport);
            var restart = Button_(btns, "다시 플레이 (R)", Yellow, Hex("#1A1408"), 52); LE(restart.gameObject, 210, 52);
            restart.onClick.AddListener(Restart);
            LayoutRebuilder.ForceRebuildLayoutImmediate(page);

            // 효율 숫자 내려가기
            float dur = 1.2f, tt = 0;
            var gfill = gauge.GetChild(0).GetComponent<RectTransform>();
            while (tt < dur)
            {
                tt += Time.deltaTime;
                int shown = Mathf.RoundToInt(Mathf.Lerp(100, r.finalEfficiency, Mathf.SmoothStep(0, 1, tt / dur)));
                big.text = shown + "%"; big.color = LossColor(100 - shown);
                gfill.anchorMax = new Vector2(shown / 100f, 1);
                yield return null;
            }
            big.text = r.finalEfficiency + "%";
        }

        void TableRow(Transform parent, string a, string b, string c, string d, Color ca, Color cb, Color cc, Color cd, bool head)
        {
            var row = NewRect("Row", parent); var h = HLayout(row.gameObject, 10, TextAnchor.MiddleLeft); h.padding = new RectOffset(4, 4, 9, 9);
            var t1 = AddText(row, a, head ? 13 : 16, ca); LE(t1.gameObject, 380);
            var t2 = AddText(row, b, head ? 13 : 16, cb); LE(t2.gameObject, 210);
            var t3 = AddText(row, c, head ? 13 : 16, cc); LE(t3.gameObject, 210);
            var t4 = AddText(row, head ? d : $"<b>{d}</b>", head ? 13 : 16, cd, FontStyle.Normal, TextAnchor.MiddleRight); LE(t4.gameObject, -1, -1, 1);
            var line = NewRect("Line", parent); line.gameObject.AddComponent<Image>().color = CardLine; LE(line.gameObject, -1, 1);
        }

        Button SmallButton(Transform parent, string label)
        {
            var rt = NewRect("Btn", parent);
            var img = Round(rt.gameObject, Tile, 1.6f);
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = CardLine; o.effectDistance = new Vector2(1, -1);
            var h = HLayout(rt.gameObject, 0, TextAnchor.MiddleCenter); h.padding = new RectOffset(12, 12, 6, 6);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            AddText(rt, $"<b>{label}</b>", 13, Body, FontStyle.Normal, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            return b;
        }

        static List<string> LoadHistory()
        {
            var s = PlayerPrefs.GetString(HistoryKey, "");
            return string.IsNullOrEmpty(s) ? new List<string>() : new List<string>(s.Split('|'));
        }

        static void SaveHistory(List<string> list) { PlayerPrefs.SetString(HistoryKey, string.Join("|", list)); PlayerPrefs.Save(); }

        void DrawHistory(RectTransform bars, Text note, string current)
        {
            for (int i = bars.childCount - 1; i >= 0; i--) Destroy(bars.GetChild(i).gameObject);
            var runs = LoadRuns().runs;
            foreach (var type in ReportBuilder.Types)
            {
                int cnt = 0; foreach (var x in runs) if (x.type == type) cnt++;
                float pct = runs.Count == 0 ? 0 : (float)cnt / runs.Count;
                var row = NewRect("Row", bars); HLayout(row.gameObject, 10, TextAnchor.MiddleLeft);
                var tl = AddText(row, type == current ? $"<b>{type}</b>" : type, 15, type == current ? Teal : Body, FontStyle.Normal, TextAnchor.MiddleLeft); LE(tl.gameObject, 100);
                var g = Gauge(row, pct, Teal, 8); LE(g.gameObject, 10, 8, 1);
                var pv = AddText(row, $"{cnt}회", 14, Dim, FontStyle.Normal, TextAnchor.MiddleRight); LE(pv.gameObject, 44);
            }
            note.text = runs.Count == 0 ? "저장된 회차가 없습니다." : $"지금까지 {runs.Count}판";
        }

        void DrawRuns(RectTransform box, int currentNo)
        {
            for (int i = box.childCount - 1; i >= 0; i--) Destroy(box.GetChild(i).gameObject);
            var runs = LoadRuns().runs;
            if (runs.Count == 0) { AddText(box, "저장된 회차가 없습니다.", 14, Dim); return; }
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                var x = runs[i];
                var item = NewRect("Run", box);
                Round(item.gameObject, x.no == currentNo ? Hex("#1F2C2A") : Tile, 1.4f);
                if (x.no == currentNo) { var o = item.gameObject.AddComponent<Outline>(); o.effectColor = Teal; o.effectDistance = new Vector2(1, -1); }
                VLayout(item.gameObject, 16, 10, 4);
                var head = NewRect("Head", item); HLayout(head.gameObject, 12, TextAnchor.MiddleLeft);
                AddText(head, $"<b>{x.no}회차</b>", 17, Body, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(head, $"<b>{x.efficiency}%</b>", 17, LossColor(100 - x.efficiency), FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(head, x.type, 15, Teal, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(head, $"최선 {x.bestCount}/{x.events}" + (x.accident ? "  <color=#FF6B6B>안전사고</color>" : ""), 14, Dim, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                var when = AddText(head, $"{x.when}{(x.no == currentNo ? "  · 이번 판" : "")}", 13, Dim, FontStyle.Normal, TextAnchor.MiddleRight); LE(when.gameObject, -1, -1, 1);
                var parts = new List<string>();
                if (x.choices != null)
                    for (int k = 0; k < x.choices.Length; k++)
                        parts.Add($"{(x.events_ != null && k < x.events_.Length ? x.events_[k] : $"사건 {k + 1}")} <color=#EEF0F3>{x.choices[k]}</color>");
                AddText(item, string.Join("   ·   ", parts), 13, Dim);
                var gap = NewRect("Gap", box); LE(gap.gameObject, -1, 8);
            }
        }

        RectTransform runLog;

        // 이전 기록 창: 모든 회차를 최근 순으로, 사건마다 고른 행동·최선 여부·손실까지
        void OpenRunLog(int currentNo)
        {
            if (runLog) { Destroy(runLog.gameObject); runLog = null; return; }
            runLog = NewRect("RunLog", overlay);
            runLog.anchorMin = Vector2.zero; runLog.anchorMax = Vector2.one; runLog.offsetMin = runLog.offsetMax = Vector2.zero;
            runLog.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            var win = NewRect("Window", runLog); win.anchorMin = new Vector2(0.5f, 0.5f); win.anchorMax = new Vector2(0.5f, 0.5f); win.sizeDelta = new Vector2(900, 620);
            Round(win.gameObject, CardBg, 1.0f);
            var wo = win.gameObject.AddComponent<Outline>(); wo.effectColor = CardLine; wo.effectDistance = new Vector2(1, -1);
            VLayout(win.gameObject, 0, 0, 0);
            var head = NewRect("Head", win); var hh = HLayout(head.gameObject, 10, TextAnchor.MiddleLeft); hh.padding = new RectOffset(22, 16, 14, 14); LE(head.gameObject).flexibleHeight = 0;
            var runs = LoadRuns().runs;
            var title = AddText(head, $"<b>이전 기록</b>  <color=#A3A8B2>{runs.Count}판 · 최근 순</color>", 20, Body, FontStyle.Normal, TextAnchor.MiddleLeft); LE(title.gameObject, -1, -1, 1);
            var x = SmallButton(head, "닫기"); x.onClick.AddListener(() => { if (runLog) Destroy(runLog.gameObject); runLog = null; });
            var view = NewRect("Viewport", win); view.gameObject.AddComponent<RectMask2D>(); var vle = LE(view.gameObject); vle.flexibleHeight = 1;
            var list = NewRect("List", view); list.anchorMin = new Vector2(0, 1); list.anchorMax = new Vector2(1, 1); list.pivot = new Vector2(0.5f, 1); list.offsetMin = list.offsetMax = Vector2.zero;
            VLayout(list.gameObject, 22, 8, 12);
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = list; sr.viewport = view; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            if (runs.Count == 0) AddText(list, "저장된 기록이 없습니다.", 15, Dim);
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                var r = runs[i];
                var card = NewRect("Run", list); Round(card.gameObject, r.no == currentNo ? Hex("#1F2C2A") : Tile, 1.4f);
                if (r.no == currentNo) { var o = card.gameObject.AddComponent<Outline>(); o.effectColor = Teal; o.effectDistance = new Vector2(1, -1); }
                VLayout(card.gameObject, 18, 12, 6);
                var top = NewRect("Top", card); HLayout(top.gameObject, 14, TextAnchor.MiddleLeft);
                AddText(top, $"<b>{r.no}회차</b>", 19, Body, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(top, $"<b>{r.efficiency}%</b>", 19, LossColor(100 - r.efficiency), FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(top, r.type, 16, Teal, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                AddText(top, $"최선 {r.bestCount}/{r.events}" + (r.accident ? "  <color=#FF6B6B>안전사고</color>" : ""), 14, Dim, FontStyle.Normal, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
                var when = AddText(top, r.when + (r.no == currentNo ? "  · 이번 판" : ""), 13, Dim, FontStyle.Normal, TextAnchor.MiddleRight); LE(when.gameObject, -1, -1, 1);
                if (r.choices == null) continue;
                for (int k = 0; k < r.choices.Length; k++)
                {
                    var row = NewRect("Event", card); var rh = HLayout(row.gameObject, 10, TextAnchor.MiddleLeft); rh.padding = new RectOffset(0, 0, 2, 2);
                    var ev = AddText(row, $"{k + 1}. {(r.events_ != null && k < r.events_.Length ? r.events_[k] : "")}", 14, Dim); LE(ev.gameObject, 250);
                    bool isBest = r.best != null && k < r.best.Length && r.best[k];
                    var ch = AddText(row, r.choices[k], 14, Body); LE(ch.gameObject, 200);
                    var bl = AddText(row, isBest ? "최선" : (r.bestLabels != null && k < r.bestLabels.Length ? "최선: " + r.bestLabels[k] : ""), 13, isBest ? Hex("#6FCF97") : Dim); LE(bl.gameObject, 230);
                    int loss = r.losses != null && k < r.losses.Length ? r.losses[k] : 0;
                    var lt = AddText(row, r.losses == null ? "" : (loss > 0 ? $"<b>−{loss}%</b>" : "<b>0%</b>"), 14, LossColor(loss), FontStyle.Normal, TextAnchor.MiddleRight); LE(lt.gameObject, -1, -1, 1);
                    if (r.carryNotes != null && k < r.carryNotes.Length && !string.IsNullOrEmpty(r.carryNotes[k])) AddText(card, $"     ↳ {r.carryNotes[k]}", 13, Red);
                }
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
        }

        void CloseReport()
        {
            if (overlay) Destroy(overlay.gameObject);
            overlay = null; showingResult = false;
            if (line) { line.ClearWarnings(); line.SetRunning(true); }
            if (process) process.speed = 1f;
            SetFrozen(false);
        }

        void Spacer(Transform parent, float h)
        {
            var s = NewRect("Space", parent); LE(s.gameObject, -1, h);
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
