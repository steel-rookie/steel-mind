using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            { "production", ("한도윤", "#7AA7FF", "생산") }, { "quality", ("서지안", "#C99BFF", "품질") },
            { "equipment", ("강태호", "#F0A04B", "설비") }, { "safety", ("윤하늘", "#6FCF97", "안전") },
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
            baseEvents = def.events ?? new ScenarioEvent[0];
            surprisePool = def.surprises ?? new ScenarioEvent[0];
            line = FindFirstObjectByType<RollingLine>();
            process = FindFirstObjectByType<LineProcess>(FindObjectsInactive.Include);
            player = FindFirstObjectByType<PlayerMove>();
            if (player) body = player.GetComponent<Rigidbody2D>();
            font = Resources.Load<Font>("Fonts/Galmuri11") ?? Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "AppleGothic", "Arial" }, 20);   // 도트 글꼴 갈무리(OFL)
            reportFont = Resources.Load<Font>("Fonts/Pretendard-Regular") ?? font;   // 보고서는 읽기 쉬운 글꼴 (Pretendard, OFL)
            reportBold = Resources.Load<Font>("Fonts/Pretendard-Bold") ?? reportFont;
            round = MakeRoundSprite(64, 18);
            triangle = MakeTriangleSprite(48);
            BuildCanvas();
            SetupMusic();
            SetupVoice();
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
            yield return CameraIntro();
            yield return Intro();
            session.titleShown = true;
            Restart();
        }

        // 공장 전체를 멀리서 보여 주다가 플레이어 쪽으로 천천히 다가가는 카메라 연출
        IEnumerator CameraIntro()
        {
            var cam = Camera.main;
            var follow = cam ? cam.GetComponent<CameraFollow>() : null;
            if (!cam || !follow || !follow.target) { yield return new WaitForSeconds(0.6f); yield break; }
            Behaviour ppc = null;
            foreach (var b in cam.GetComponents<Behaviour>()) if (b && b.GetType().Name == "PixelPerfectCamera" && b.enabled) ppc = b;
            if (ppc) ppc.enabled = false;
            follow.enabled = false;

            const float ppu = YSort.PixelsPerUnit;
            float mapW = follow.mapSizePx.x / ppu, mapH = follow.mapSizePx.y / ppu;
            float near = 270f / 2f / ppu;
            float far = Mathf.Max(mapH / 2f, mapW / 2f / cam.aspect);
            Vector3 wide = new Vector3(mapW / 2f, -mapH / 2f, -10f);
            // 도착 위치는 CameraFollow가 잡을 자리와 똑같이 맞춘다
            float halfH = near, halfW = near * cam.aspect;
            Vector3 tp = follow.target.position;
            Vector3 close = new Vector3(mapW <= halfW * 2 ? mapW / 2 : Mathf.Clamp(tp.x, halfW, mapW - halfW),
                                        mapH <= halfH * 2 ? -mapH / 2 : Mathf.Clamp(tp.y, -mapH + halfH, -halfH), -10f);

            cam.orthographicSize = far; cam.transform.position = wide;
            var card = AddText(canvas.transform, "열연 2라인 · 야간 교대", 44, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            card.font = Resources.Load<Font>("Fonts/Galmuri11-Bold") ?? font;
            var crt = card.rectTransform; crt.anchorMin = new Vector2(0, 0.62f); crt.anchorMax = new Vector2(1, 0.78f); crt.offsetMin = crt.offsetMax = Vector2.zero;
            var csh = card.gameObject.AddComponent<Outline>(); csh.effectColor = new Color(0, 0, 0, 0.8f); csh.effectDistance = new Vector2(3, -3);
            for (float t = 0; t < 1f; t += Time.deltaTime) { card.color = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, t / 0.7f)); yield return null; }
            yield return new WaitForSeconds(0.9f);
            const float dur = 2.2f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / dur);
                cam.orthographicSize = Mathf.Lerp(far, near, k);
                cam.transform.position = Vector3.Lerp(wide, close, k);
                card.color = new Color(1, 1, 1, 1f - Mathf.Clamp01(t / 0.8f));
                yield return null;
            }
            Destroy(card.gameObject);
            cam.orthographicSize = near; cam.transform.position = close;
            follow.enabled = true;
            if (ppc) ppc.enabled = true;
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Intro()
        {
            chatting = true; skip = false;
            OpenVN("열연 2라인 · 야간 교대 시작", false);
            yield return Say(null, null, "열연 2라인, 야간 교대.\n오늘 당신은 처음으로 이 라인의 운영 책임자를 맡았습니다.", null, false);
            yield return Say(null, null, "오늘 함께 일할 AI 동료 네 명이 있습니다.\n모두 맞는 말을 하지만, 서로 다른 것을 지키려 합니다.", null, false);
            yield return Say("production", null, "한도윤입니다. 오늘 밤 같이 라인을 지킵니다.", null, false);
            yield return Say("quality", null, "서지안이에요. 궁금한 건 언제든 물어보세요.", null, false);
            yield return Say("equipment", null, "강태호입니다. 이 라인에서 오래 일했습니다.", null, false);
            yield return Say("safety", null, "윤하늘입니다. 잘 부탁드립니다.", null, false);
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
            played.Clear(); def.events = new ScenarioEvent[0]; eventDeltas.Clear(); pickAt.Clear();
            retrainTarget = null; retrainBefore = null; retrainBase = null; retrainEvent = null; retraining = false;
            if (canvas) foreach (Transform ch in canvas.transform) if (ch.name == "AiToast") Destroy(ch.gameObject);
            model = new CompetencyModel(def);
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

        // ── 적응형 진행: 기본 사건 몇 개로 기본 측정 → AI가 약점·불확실한 역량을 노린 사건을 고른다 ──
        ScenarioEvent[] baseEvents, surprisePool;
        readonly List<ScenarioEvent> played = new List<ScenarioEvent>();
        CompetencyModel model;
        int planned;
        readonly List<Dictionary<string, int>> eventDeltas = new List<Dictionary<string, int>>();
        readonly Dictionary<int, Pick> pickAt = new Dictionary<int, Pick>();   // 사건 번호 → AI가 고른 이유
        public CompetencyModel Model => model;

        IEnumerator Run()
        {
            int baseN = Mathf.Clamp(def.baseCount > 0 ? def.baseCount : baseEvents.Length, 0, baseEvents.Length);
            planned = def.totalCount > 0 ? def.totalCount : baseEvents.Length;
            var pool = new List<ScenarioEvent>(surprisePool);
            for (int k = baseN; k < baseEvents.Length; k++) pool.Add(baseEvents[k]);
            planned = Mathf.Min(planned, baseN + pool.Count);
            for (int i = 0; i < planned; i++)
            {
                ScenarioEvent ev;
                if (i < baseN) ev = baseEvents[i];
                else
                {
                    var cands = model.TopCandidates(pool, 3);
                    var pk = model.PickNext(pool);
                    if (pk == null) break;
                    if (GeminiClient.Available) yield return AskPick(pk, cands);
                    ev = pk.ev; pool.Remove(ev); pickAt[i] = pk;
                    yield return new WaitForSeconds(1.2f);
                    yield return AiToast("AI 판단 · 다음 사건 선택", $"노리는 역량  <b>{model.Name(pk.target)}</b>\n{pk.reason}\n→ 돌발상황을 준비합니다", 3.6f, true);
                }
                played.Add(ev); def.events = played.ToArray();
                yield return new WaitForSeconds(Mathf.Max(1.5f, ev.delay - (i < baseN ? 0 : 3f)));
                yield return GoToSite(i);
                yield return EventChat(i);
                var chosen = ev.choices[picks[i]];
                if (i + 1 < planned) SetStatus($"조치 진행 중: {chosen.label} · 사건 {i + 2}/{planned} 대기 중");
            }
            SetStatus("교대 근무를 마무리하는 중입니다");
            yield return new WaitForSeconds(def.resultDelay);
            yield return ResultScreen();
        }

        [System.Serializable] class PickReply { public string id, reason; }
        [System.Serializable] class DiagReply { public string title, body; public string[] coaching; }

        // Gemini가 후보 3개 중 다음 돌발상황을 고르고 이유를 쓴다. 실패하면 규칙 기반 선택 그대로.
        IEnumerator AskPick(Pick pk, List<ScenarioEvent> cands)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("너는 제철소 신입 운영자의 돌발상황 대처 역량을 진단하는 평가 AI다. 적응형 검사처럼, 지금까지의 답변으로 가장 약하거나 아직 덜 확인된 역량을 정확히 측정할 다음 사건 하나를 고른다.\n");
            sb.Append("역량 현황(평균 -2~+2): ").Append(model.Summary()).Append("\n지금까지의 선택:\n");
            for (int i = 0; i < played.Count && i < picks.Count; i++) sb.Append($"- {played[i].title}: {played[i].choices[picks[i]].label}\n");
            sb.Append("후보 사건:\n");
            foreach (var c in cands) sb.Append($"- id={c.id}, 제목={c.title}, 시험 역량={string.Join("/", c.probes)}\n");
            sb.Append("JSON으로만 답하라: {\"id\": 후보 id, \"reason\": 한국어 한 문장, 40자 이내, 어떤 역량을 왜 확인하려는지}");
            string reply = null;
            yield return GeminiClient.Ask(sb.ToString(), 6f, t => reply = t);
            if (string.IsNullOrEmpty(reply)) yield break;
            try
            {
                var r = JsonUtility.FromJson<PickReply>(reply);
                var ev = cands.Find(c => c.id == r.id);
                if (ev == null) yield break;
                pk.ev = ev;
                pk.target = ev.probes.OrderByDescending(model.Need).First();
                if (!string.IsNullOrEmpty(r.reason)) pk.reason = r.reason;
                pk.byGemini = true;
            }
            catch { }
        }

        // 보고서 진단 문장을 Gemini로 다시 쓴다
        IEnumerator AskDiagnosis(RunResult r, Text title, Text body, Text chip, RectTransform coachBox)
        {
            if (!GeminiClient.Available) yield break;
            chip.text = "Gemini 작성 중…";
            var sb = new System.Text.StringBuilder();
            sb.Append("너는 제철소 신입 운영자의 돌발상황 대처 역량을 진단하고 교육 담당자에게 보고하는 AI다. 아래 기록만 근거로 판단하고, 숫자를 지어내지 마라.\n");
            sb.Append("역량 정의:\n");
            foreach (var c in def.competencies) sb.Append($"- {c.name}: {c.desc}\n");
            sb.Append("역량 결과(평균 -2~+2, 확인 횟수): ").Append(model.Summary()).Append("\n사건 기록:\n");
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                sb.Append($"- {e.ev.title} → '{e.chosen.label}' ({e.chosen.outcome})");
                if (pickAt.TryGetValue(i, out var pk)) sb.Append($" [AI가 '{model.Name(pk.target)}' 확인을 위해 낸 돌발상황]");
                sb.Append("\n");
            }
            sb.Append("JSON으로만 답하라: {\"title\": 진단 한 줄 20자 이내, \"body\": 운영자에게 존댓말 2~3문장(강점과 보완점, 근거가 된 사건 이름 포함), \"coaching\": [다음 교육에서 할 구체적 훈련 2개, 각 30자 이내]}");
            string reply = null;
            yield return GeminiClient.Ask(sb.ToString(), 15f, t => reply = t);
            DiagReply d = null;
            try { if (!string.IsNullOrEmpty(reply)) d = JsonUtility.FromJson<DiagReply>(reply); } catch { }
            if (d == null || string.IsNullOrEmpty(d.title)) { chip.text = "기본 문장"; yield break; }
            if (title) title.text = $"<b>{d.title}</b>";
            if (body && !string.IsNullOrEmpty(d.body)) body.text = d.body;
            chip.text = $"Gemini · {GeminiClient.Model}";
            if (coachBox && d.coaching != null && d.coaching.Length > 0)
            {
                AddText(coachBox, "<b>AI 코칭</b>", 15, Teal);
                foreach (var c in d.coaching) AddText(coachBox, "• " + c, 15, Body);
                ApplyReportFont(coachBox);
                LayoutRebuilder.ForceRebuildLayoutImmediate(coachBox);
                if (overlay) LayoutRebuilder.ForceRebuildLayoutImmediate(overlay);
            }
        }

        // AI 분석·판단 알림 (오른쪽 위 카드). wait=true면 사라질 때까지 기다린다
        IEnumerator AiToast(string head, string body, float secs, bool wait)
        {
            var co = StartCoroutine(Toast(head, body, secs));
            if (wait) yield return co;
        }

        IEnumerator Toast(string head, string body, float secs)
        {
            var rt = NewRect("AiToast", canvas.transform);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(24, -24);
            var bg = Round(rt.gameObject, Hex("#0F1F1C", 0.95f), 1.4f);
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = Teal; o.effectDistance = new Vector2(2, -2);
            var v = VLayout(rt.gameObject, 16, 14, 6);
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cg = rt.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0;
            var h = AddText(rt, $"<b>◆ {head}</b>", 16, Teal); h.horizontalOverflow = HorizontalWrapMode.Overflow;
            var t = AddText(rt, body, 18, Color.white); t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float a = 0;
            while (a < 0.25f) { a += Time.deltaTime; cg.alpha = a / 0.25f; rt.anchoredPosition = new Vector2(24 - 30 * (1 - a / 0.25f), -24); yield return null; }
            cg.alpha = 1; rt.anchoredPosition = new Vector2(24, -24);
            yield return new WaitForSeconds(secs);
            a = 0;
            while (a < 0.3f && rt) { a += Time.deltaTime; cg.alpha = 1 - a / 0.3f; yield return null; }
            if (rt) Destroy(rt.gameObject);
        }

        string DeltaText(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var id in CompetencyModel.Ids)
            {
                if (!d.TryGetValue(id, out var v)) continue;
                string col = v > 0 ? "#6FCF97" : v < 0 ? "#FF6B6B" : "#A3A8B2";
                string arrow = v > 0 ? "▲" : v < 0 ? "▼" : "–";
                parts.Add($"{model.Name(id)} <color={col}><b>{arrow}{(v != 0 ? Mathf.Abs(v).ToString() : "")}</b></color>");
            }
            return parts.Count == 0 ? "평가 항목 없음" : string.Join("   ", parts);
        }

        // 사건 발생: 경보 + 붉은 깜빡임 → 화살표 따라 설비까지 걸어감 → 회의
        IEnumerator GoToSite(int i)
        {
            var ev = def.events[i];
            Music(true);
            if (line && !string.IsNullOrEmpty(ev.warn)) line.SetWarning(ev.warn, true);
            SetFrozen(true);
            var go = new GameObject("AlarmFlash", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>(); img.raycastTarget = false;
            const int blinks = 3; const float period = 0.5f;
            for (float t = 0; t < blinks * period; t += Time.deltaTime)
            {
                float k = Mathf.Sin(t / period * Mathf.PI); // 0→1→0 한 번 깜빡임마다
                img.color = new Color(0.9f, 0.1f, 0.08f, 0.38f * k * k);
                yield return null;
            }
            Destroy(go);
            SetFrozen(false);
            // 문제가 난 설비까지 화살표를 따라 걸어간다. 가까이 가면 회의가 열린다.
            var eq = line ? line.equipment.Find(e => e.id == ev.warn) : null;
            if (eq == null || !eq.renderer || !player) yield break;
            guideTarget = eq.renderer.bounds;
            guideName = eq.label;
            SetStatus($"⚠ {ev.title} · {eq.label}(으)로 가세요");
            ShowGuide();
            while (true)
            {
                var bb = guideTarget; bb.Expand(new Vector3(ArriveRange * 2, ArriveRange * 2, 10));
                if (bb.Contains(new Vector3(player.transform.position.x, player.transform.position.y, bb.center.z))) break;
                yield return null;
            }
            HideGuide();
        }

        // 미연시 회의: 경보 → Agent가 한 명씩 나와 말함(클릭·Space로 넘김) → 선택 → 내 말 → 반응
        IEnumerator EventChat(int i)
        {
            current = i; chatting = true; skip = false;
            var ev = def.events[i];
            int n = Mathf.Max(planned, def.events.Length);
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
            // 선택 뒤 동료들의 찬성·불만 반응은 보여 주지 않는다 (평가가 드러나지 않게). 결과는 보고서에서만.
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
            if (model != null)
            {
                var d = model.Apply(ev, c);
                eventDeltas.Add(d);
                StartCoroutine(AiToast("AI 분석 · 방금 선택", DeltaText(d), 3.2f, false));
            }
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
            else if (showingResult && !runLog && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame)) ShowPage(reportPage + 1, 1);
            else if (showingResult && !runLog && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame)) ShowPage(reportPage - 1, -1);
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
            vnNext = NewRect("Next", box); vnNext.anchorMin = vnNext.anchorMax = new Vector2(1, 0); vnNext.anchoredPosition = new Vector2(-100, 30); vnNext.sizeDelta = new Vector2(18, 14);
            var ni = vnNext.gameObject.AddComponent<Image>(); ni.sprite = triangle; ni.color = Color.white; vnNext.localRotation = Quaternion.Euler(0, 0, 180);

            // 로그·스킵
            // 게임 중 대화 기록(로그) 버튼은 두지 않는다
            SkipChevrons(box);
            vnHistory.Clear();
        }

        Image Actor(string name, Vector2 anchor)
        {
            var rt = NewRect(name, vn); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 150); rt.sizeDelta = new Vector2(300, 400);
            var img = rt.gameObject.AddComponent<Image>(); img.preserveAspect = true; img.color = new Color(1, 1, 1, 0);
            return img;
        }

        // 대화창 오른쪽 아래 노란 » 스킵 표시
        void SkipChevrons(RectTransform box)
        {
            var rt = NewRect("Skip", box); rt.anchorMin = rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-28, 18); rt.sizeDelta = new Vector2(52, 44);
            var hit = rt.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            var b = rt.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { fastAll = true; skip = true; });
            var yellow = Hex("#F2C94C");
            for (int c = 0; c < 2; c++)
                for (int k = 0; k < 2; k++)
                {
                    var bar = NewRect("Bar", rt); bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                    bar.sizeDelta = new Vector2(5, 17);
                    bar.anchoredPosition = new Vector2(-8 + c * 14, k == 0 ? 5.5f : -5.5f);
                    bar.localRotation = Quaternion.Euler(0, 0, k == 0 ? 45 : -45);
                    var im = bar.gameObject.AddComponent<Image>(); im.color = yellow; im.raycastTarget = false;
                }
        }

        // ── 동물의 숲 같은 웅얼웅얼 목소리 ──────────────────
        AudioSource voice;
        AudioClip[] blips;
        static readonly Dictionary<string, float> VoicePitch = new Dictionary<string, float>
        { { "production", 0.85f }, { "quality", 1.35f }, { "equipment", 0.72f }, { "safety", 1.05f }, { "player", 1.18f } };
        // 한글 중성(ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ) → 아/에/이/오/우 소리
        static readonly int[] JungToVowel = { 0, 1, 0, 1, 3, 1, 3, 1, 3, 0, 1, 1, 3, 4, 3, 1, 2, 4, 4, 2, 2 };

        void SetupVoice()
        {
            voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.volume = 0.45f;
            float[,] F = { { 800, 1250 }, { 520, 1850 }, { 300, 2300 }, { 520, 900 }, { 340, 760 } };
            blips = new AudioClip[5];
            for (int v = 0; v < 5; v++) blips[v] = MakeBlip(F[v, 0], F[v, 1], v);
        }

        static AudioClip MakeBlip(float f1, float f2, int v)
        {
            const int sr = 22050; const float f0 = 210f, dur = 0.075f;
            int n = (int)(sr * dur);
            var d = new float[n]; float peak = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, x = 0;
                float glide = f0 * (1f + 0.12f * (1 - t / dur));   // 살짝 내려가는 음
                for (int h = 1; h <= 14; h++)
                {
                    float hf = h * glide;
                    float a = Mathf.Exp(-Mathf.Pow((hf - f1) / 260f, 2)) + 0.7f * Mathf.Exp(-Mathf.Pow((hf - f2) / 380f, 2)) + 0.12f / h;
                    x += a * Mathf.Sin(2 * Mathf.PI * hf * t);
                }
                float env = Mathf.Min(1, t / 0.006f) * Mathf.Exp(-t * 26f);
                d[i] = x * env; peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.7f;
            var clip = AudioClip.Create($"blip{v}", n, 1, sr, false); clip.SetData(d, 0);
            return clip;
        }

        void Blip(string agent, char ch)
        {
            if (!voice || blips == null || agent == null) return;
            int v;
            if (ch >= 0xAC00 && ch <= 0xD7A3) v = JungToVowel[((ch - 0xAC00) / 28) % 21];
            else if (char.IsLetterOrDigit(ch)) v = ch % 5;
            else return;
            float p = VoicePitch.TryGetValue(agent, out var vp) ? vp : 1f;
            voice.pitch = p * Random.Range(0.94f, 1.06f);
            voice.PlayOneShot(blips[v]);
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
        // 문장이 끝나면(. ? !) 줄을 바꿔 한 줄에 한 문장씩 보이게 한다. "0.01mm"처럼 뒤에 공백이 없으면 그대로.
        static string SplitSentences(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, @"([.?!…])[ \t]+(?=\S)", "$1\n");
        }

        IEnumerator Say(string agent, string stance, string text, string sub, bool me)
        {
            if (!vn) yield break;
            text = SplitSentences(text);
            bool narr = agent == null;
            yield return Place(narr ? null : agent);
            vnPlate.gameObject.SetActive(!narr);
            if (!narr)
            {
                // 이름표를 말하는 캐릭터 바로 아래에
                float ax = whoR == agent ? 0.8f : 0.2f;
                vnPlate.anchorMin = vnPlate.anchorMax = new Vector2(ax, 1); vnPlate.pivot = new Vector2(0.5f, 0.5f); vnPlate.anchoredPosition = Vector2.zero;
                var (name, color, persona) = Agents.TryGetValue(agent, out var v) ? v : (agent, "#EEF0F3", "");
                vnName.text = name; vnPersona.text = ""; vnPersona.gameObject.SetActive(false);
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
                if (!narr && i % 2 == 1) Blip(agent, text[i - 1]);
                float c = 0; while (c < CharDelay && !skip) { c += Time.deltaTime; yield return null; }
            }
            vnLine.text = text;
            vnWhy.text = sub ?? "";
            vnNext.gameObject.SetActive(false);   // ▼ 표시는 쓰지 않는다 (» 스킵만)
            skip = false;
            if (fastAll) { yield return null; yield break; }
            float bob = 0;
            while (!skip)
            {
                bob += Time.deltaTime;
                vnNext.anchoredPosition = new Vector2(-100, 30 - Mathf.Abs(Mathf.Sin(bob * 5f)) * 6f);
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
                AddText(tc, c.detail, 17, Hex("#5D636E"), FontStyle.Normal, TextAnchor.MiddleCenter);
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

        Text AiTag(Transform parent, string label)
        {
            var row = NewRect("Label", parent); HLayout(row.gameObject, 8, TextAnchor.MiddleLeft);
            AddText(row, label, 15, Dim, FontStyle.Bold, TextAnchor.MiddleLeft).horizontalOverflow = HorizontalWrapMode.Overflow;
            var chip = NewRect("Chip", row); Round(chip.gameObject, Hex("#1F3A33"), 1.8f);
            var h = HLayout(chip.gameObject, 0, TextAnchor.MiddleCenter); h.padding = new RectOffset(8, 8, 2, 2);
            var ct = AddText(chip, "기본 문장", 12, Teal, FontStyle.Bold, TextAnchor.MiddleCenter); ct.horizontalOverflow = HorizontalWrapMode.Overflow;
            return ct;
        }

        // 진단 요약 (기본 문장. Gemini가 있으면 덮어쓴다)
        (string label, string title, string body) Diagnose()
        {
            var st = model.Strengths(); var wk = model.Weaknesses();
            string S(IEnumerable<CompetencyState> l) => string.Join(" · ", l.Select(x => model.Name(x.id)));
            if (wk.Count == 0)
                return ("균형형", "다섯 역량을 고르게 지켰습니다",
                    st.Count > 0 ? $"특히 {S(st.Take(2))} 쪽이 강했습니다. 더 어려운 돌발상황으로 다음 단계를 확인해 볼 수 있습니다."
                                 : "뚜렷한 약점은 없지만 강점도 아직 드러나지 않았습니다. 돌발상황을 더 겪어 봐야 합니다.");
            var w0 = wk[0];
            int aimed = model.picks.Count(x => x.target == w0.id);
            string body = (st.Count > 0 ? $"강점은 {S(st.Take(2))}입니다. " : "아직 뚜렷한 강점은 없습니다. ")
                + (aimed > 0 ? $"AI가 이 역량을 노린 돌발상황을 {aimed}번 냈고, 평균 {w0.Mean:+0.0;-0.0;0.0}점에 머물렀습니다."
                             : $"이 역량의 평균은 {w0.Mean:+0.0;-0.0;0.0}점입니다.");
            return ($"{model.Name(w0.id)} 보완", $"{model.Name(w0.id)} 보완이 필요합니다", body);
        }

        void Chip(Transform parent, string text, Color bg, Color fg)
        {
            var chip = NewRect("Chip", parent); Round(chip.gameObject, bg, 1.8f);
            var h = HLayout(chip.gameObject, 0, TextAnchor.MiddleCenter); h.padding = new RectOffset(10, 10, 4, 4);
            AddText(chip, $"<b>{text}</b>", 14, fg, FontStyle.Normal, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // ── 결과 보고서 (STEEL MIND 2.0 시안: 탭 · 레이더 · 강점/보완 근거 · 학습 경로 · 맞춤 재훈련) ──
        static readonly Color RBg = Hex("#0D1117"), RCard = Hex("#151B24"), RLine = Hex("#263043"), RBlue = Hex("#4CC3FF"),
            ROk = Hex("#6FCF97"), RNo = Hex("#FF8A80"), RMute = Hex("#AAB4C5"), RText = Hex("#F1F4F8"), RWarn = Hex("#E0B84A");
        RunRecord lastRecord;
        string retrainTarget;
        Dictionary<string, int> retrainBefore;
        float[] retrainBase;
        ScenarioEvent retrainEvent;
        bool retraining;

        Font reportFont, reportBold;

        void ApplyReportFont(Transform root)
        {
            if (!root || !reportFont) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                bool bold = t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic;
                t.font = bold ? reportBold : reportFont;
                if (bold) t.fontStyle = FontStyle.Normal;
                t.lineSpacing = 1.1f;
            }
        }

        int Score(string id) => Mathf.RoundToInt(model.Radar(id) * 100);
        float Sd(string id) => 0.2887f / Mathf.Sqrt(model.states[id].count + 1f);
        int Conf(string id) => Mathf.RoundToInt(Mathf.Clamp01(1f - Sd(id) / 0.2887f) * 100);

        // 역량별 근거: 점수를 올린/내린 사건과 선택
        List<string> Evidence(RunResult r, string id, bool good)
        {
            var list = new List<string>();
            for (int i = 0; i < r.events.Count && i < eventDeltas.Count; i++)
                if (eventDeltas[i].TryGetValue(id, out var v) && (good ? v > 0 : v < 0))
                    list.Add($"{r.events[i].ev.title}: \"{r.events[i].chosen.label}\"");
            return list.Take(2).ToList();
        }

        RectTransform RBox(Transform parent, string title, int pad = 16)
        {
            var c = NewRect("Box", parent); Round(c.gameObject, RCard, 1.2f);
            var o = c.gameObject.AddComponent<Outline>(); o.effectColor = RLine; o.effectDistance = new Vector2(1, -1);
            VLayout(c.gameObject, pad, pad - 2, 6);
            if (title != null) AddText(c, title, 14, RMute, FontStyle.Bold);
            return c;
        }

        IEnumerator ResultScreen()
        {
            var rep = ReportBuilder.Build(def, picks);
            var r = rep.run;
            var diag = Diagnose();
            rep.type = diag.label;
            var record = retraining && lastRecord != null ? lastRecord : SaveRun(rep);
            lastRecord = record;
            showingResult = true; finished = true;
            var strong = model.Strengths(); var weak = model.Weaknesses();
            var unknown = CompetencyModel.Ids.Where(i => model.states[i].count == 0).ToList();
            Competency Def(string id) { foreach (var c in def.competencies) if (c.id == id) return c; return null; }

            overlay = NewRect("Report", canvas.transform);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(RBg.r, RBg.g, RBg.b, 0.98f);

            // 머리: 제목 + 탭
            var page = NewRect("Head", overlay); page.anchorMin = page.anchorMax = new Vector2(0.5f, 1); page.pivot = new Vector2(0.5f, 1); page.sizeDelta = new Vector2(1180, 80);
            page.anchoredPosition = new Vector2(0, -14);
            var hv = VLayout(page.gameObject, 0, 0, 8);
            AddText(page, $"<b>STEEL MIND</b>  <color=#8B96A8>AI 대처 역량 진단 · {record.no}회차{(retraining ? " · 맞춤 재훈련" : "")}</color>", 20, RText);
            var tabs = NewRect("Tabs", page); HLayout(tabs.gameObject, 24, TextAnchor.MiddleLeft); LE(tabs.gameObject, -1, 32);
            var tabLine = NewRect("TabLine", page); tabLine.gameObject.AddComponent<Image>().color = RLine; LE(tabLine.gameObject, -1, 1);

            var view = NewRect("Viewport", overlay); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMax = new Vector2(0, -104); view.offsetMin = new Vector2(0, 34);
            view.gameObject.AddComponent<RectMask2D>();
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.viewport = view; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            reportPages.Clear(); reportScroll = sr; tabTexts.Clear(); tabBars.Clear();
            string[] titles = { "진단 결과", "AI 판단 로그", "검증 계획", "회차 기록" };
            for (int t = 0; t < titles.Length; t++)
            {
                var pg = NewRect("Page", view); pg.anchorMin = pg.anchorMax = new Vector2(0.5f, 1); pg.pivot = new Vector2(0.5f, 1); pg.sizeDelta = new Vector2(1180, 0);
                var pgv = VLayout(pg.gameObject, 0, 0, 12); pgv.padding = new RectOffset(0, 0, 10, 24);
                pg.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                pg.gameObject.AddComponent<CanvasGroup>();
                reportPages.Add((pg, titles[t]));
                var tb = NewRect("Tab", tabs); VLayout(tb.gameObject, 0, 0, 4);
                var tt = AddText(tb, titles[t], 16, RMute, FontStyle.Bold, TextAnchor.MiddleCenter); tt.horizontalOverflow = HorizontalWrapMode.Overflow;
                var bar = NewRect("Bar", tb); var bi = bar.gameObject.AddComponent<Image>(); bi.color = RBlue; LE(bar.gameObject, -1, 2);
                var btn = tb.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None; int idx = t;
                btn.onClick.AddListener(() => ShowPage(idx, idx > reportPage ? 1 : -1));
                tabTexts.Add(tt); tabBars.Add(bi);
            }
            RectTransform P(int i) => reportPages[i].rt;

            var foot = NewRect("Foot", overlay); foot.anchorMin = new Vector2(0, 0); foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0); foot.sizeDelta = new Vector2(-48, 28); foot.anchoredPosition = new Vector2(0, 6);
            var ft = AddText(foot, "모든 설비 상황·수치·인물은 교육용 가상 설정입니다. 진단 결과는 교육·코칭 추천용이며 인사 배치나 업무 적합성 판단에 쓰지 않습니다. 역량 기준: NCS 열간압연 능력단위 · 비기술적 역량 연구.", 12, RMute);
            var ftr = ft.rectTransform; ftr.anchorMin = Vector2.zero; ftr.anchorMax = Vector2.one; ftr.offsetMin = ftr.offsetMax = Vector2.zero;

            // ── 1. 진단 결과 ──
            if (retrainTarget != null && retrainBefore != null && retrainEvent != null)
            {
                var bn = NewRect("Banner", P(0)); Round(bn.gameObject, Hex("#2A2412"), 1.2f);
                var bo = bn.gameObject.AddComponent<Outline>(); bo.effectColor = RWarn; bo.effectDistance = new Vector2(1, -1);
                VLayout(bn.gameObject, 14, 10, 0);
                AddText(bn, $"<b>재훈련 결과 · {model.Name(retrainTarget)}: {retrainBefore[retrainTarget]} → {Score(retrainTarget)}</b>. 같은 문제가 아니라 조건이 다른 상황({retrainEvent.title})에서 확인했습니다. 회색 선은 재훈련 전 프로필입니다.", 15, RText);
            }
            var row = NewRect("Row", P(0)); var rh = HLayout(row.gameObject, 24, TextAnchor.UpperLeft); rh.childForceExpandHeight = false;
            // 왼쪽: 레이더
            var left = NewRect("Left", row); VLayout(left.gameObject, 0, 0, 6); LE(left.gameObject, 380);
            var radarBox = NewRect("Radar", left); LE(radarBox.gameObject, 380, 360);
            float RR = 120f;
            float[] vals = System.Array.ConvertAll(CompetencyModel.Ids, id => model.Radar(id));
            float[] hi = System.Array.ConvertAll(CompetencyModel.Ids, id => Mathf.Clamp01(model.Radar(id) + 1.5f * Sd(id)));
            RadarChart Layer(float[] v, Color fill, Color line, bool grid, bool doFill, bool doLine)
            {
                var ch = NewRect("Chart", radarBox); ch.anchorMin = ch.anchorMax = new Vector2(0.5f, 0.5f); ch.sizeDelta = new Vector2(RR * 2, RR * 2);
                var rc = ch.gameObject.AddComponent<RadarChart>(); rc.values = v; rc.rings = 4; rc.gridColor = RLine;
                rc.fillColor = fill; rc.lineColor = line; rc.drawGrid = grid; rc.drawFill = doFill; rc.drawLine = doLine; rc.raycastTarget = false;
                return rc;
            }
            Layer(hi, new Color(RBlue.r, RBlue.g, RBlue.b, 0.08f), Color.clear, true, true, false);
            if (retrainBase != null && retrainTarget != null) Layer(retrainBase, Color.clear, RMute, false, false, true);
            Layer(vals, new Color(RBlue.r, RBlue.g, RBlue.b, 0.2f), RBlue, false, true, true);
            for (int i = 0; i < CompetencyModel.Ids.Length; i++)
            {
                string id = CompetencyModel.Ids[i];
                float ang = Mathf.PI / 2f - i * 2f * Mathf.PI / CompetencyModel.Ids.Length;
                var dirv = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var lab = NewRect("Axis", radarBox); lab.anchorMin = lab.anchorMax = new Vector2(0.5f, 0.5f); lab.sizeDelta = new Vector2(130, 22);
                lab.anchoredPosition = dirv * (RR + 30f);
                var lt = AddText(lab, model.Name(id), 13, RMute, FontStyle.Normal, TextAnchor.MiddleCenter);
                var ltr = lt.rectTransform; ltr.anchorMin = Vector2.zero; ltr.anchorMax = Vector2.one; ltr.offsetMin = ltr.offsetMax = Vector2.zero;
                var sc = NewRect("Score", radarBox); sc.anchorMin = sc.anchorMax = new Vector2(0.5f, 0.5f); sc.sizeDelta = new Vector2(80, 20);
                sc.anchoredPosition = dirv * Mathf.Max(RR * vals[i] + 14f, 30f);
                var st = AddText(sc, model.states[id].count == 0 ? "근거 없음" : Score(id).ToString(), 12, model.states[id].count == 0 ? RMute : RText, FontStyle.Bold, TextAnchor.MiddleCenter);
                var str = st.rectTransform; str.anchorMin = Vector2.zero; str.anchorMax = Vector2.one; str.offsetMin = str.offsetMax = Vector2.zero;
                var dot = NewRect("Dot", radarBox); dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(7, 7);
                dot.anchoredPosition = dirv * (RR * vals[i]); Round(dot.gameObject, RBlue, 0.5f);
            }
            AddText(left, "AI 대처 역량 프로파일 · 바깥 옅은 영역 = 불확실 범위", 12, RMute, FontStyle.Normal, TextAnchor.UpperCenter);

            // 오른쪽: AI 분석 + 강점/보완 + 학습 경로 + 버튼
            var right = NewRect("Right", row); VLayout(right.gameObject, 0, 0, 12); LE(right.gameObject, -1, -1, 1);
            var chip = AiTag(right, "AI 분석");
            string Q(string id) => $"\"{model.Name(id)}\"";
            var txt = new System.Text.StringBuilder();
            if (strong.Count > 0) txt.Append(string.Join(", ", strong.Take(2).Select(x => Q(x.id)))).Append(" 쪽은 안정적이었습니다. ");
            if (weak.Count > 0)
            {
                txt.Append("반면 ").Append(string.Join(", ", weak.Take(2).Select(x => Q(x.id)))).Append(" 쪽에서 놓친 판단이 있었습니다. ");
                var ev0 = Evidence(r, weak[0].id, false);
                if (ev0.Count > 0) txt.Append("가장 큰 근거: ").Append(ev0[0]).Append(". ");
                txt.Append("우선 ").Append(Q(weak[0].id)).Append(" 재훈련을 추천합니다.");
            }
            else txt.Append("뚜렷하게 약한 역량은 보이지 않았습니다.");
            if (unknown.Count > 0) txt.Append(" ").Append(string.Join(", ", unknown.Select(Q))).Append(" 쪽은 이번에 근거가 없어 다음 회차에서 확인합니다.");
            var analysisText = AddText(right, txt.ToString(), 17, RText);

            var cols = NewRect("Cols", right); var ch2 = HLayout(cols.gameObject, 12); ch2.childForceExpandWidth = true; ch2.childForceExpandHeight = true;
            var good = RBox(cols, "관찰된 강점"); LE(good.gameObject, 10, -1, 1);
            if (strong.Count == 0) AddText(good, "근거가 더 필요합니다.", 14, RMute);
            foreach (var s0 in strong.Take(2))
            {
                AddText(good, $"<b>{model.Name(s0.id)}</b>  <color=#8B96A8>{Score(s0.id)}점</color>", 16, ROk);
                foreach (var e in Evidence(r, s0.id, true)) AddText(good, "· " + e, 13, RMute);
            }
            var bad = RBox(cols, "추가 훈련이 필요한 영역"); LE(bad.gameObject, 10, -1, 1);
            if (weak.Count == 0) AddText(bad, "뚜렷한 약점 없음", 14, RMute);
            foreach (var w0 in weak.Take(2))
            {
                AddText(bad, $"<b>{model.Name(w0.id)}</b>  <color=#8B96A8>{Score(w0.id)}점 · 확실도 {Conf(w0.id)}%</color>", 16, RNo);
                foreach (var e in Evidence(r, w0.id, false)) AddText(bad, "· " + e + " 선택", 13, RMute);
            }

            var path = RBox(right, "개인별 학습 경로 (가상 예시)");
            var focus = weak.Count > 0 ? weak.Take(2).ToList() : new List<CompetencyState>();
            foreach (var w0 in focus) { var dd = Def(w0.id); if (dd != null) AddText(path, $"<b>추천 체험</b> · {dd.training}", 15, RText); }
            if (focus.Count > 0) { var dd = Def(focus[0].id); if (dd != null) AddText(path, $"<b>추천 사수·멘토</b> · {dd.mentor}", 15, RText); }
            foreach (var s0 in strong.Take(1)) { var dd = Def(s0.id); if (dd != null) AddText(path, $"<b>강점을 살릴 현장 직무</b> · {dd.role}  <color=#8B96A8>({dd.ncs})</color>", 14, RText); }
            AddText(path, "공정 체험·교육 경로 추천이며, \"이 사람은 OO 부서에 적합하다\"는 판단을 하지 않습니다. 최종 결정은 교육 담당자와 사수가 합니다.", 12, RMute);

            var btns = NewRect("Buttons", right); HLayout(btns.gameObject, 8, TextAnchor.MiddleLeft);
            if (weak.Count > 0 && !retraining)
            {
                string k0 = weak[0].id;
                var rt0 = Button_(btns, $"맞춤 재훈련 시작 · {model.Name(k0)}", RBlue, Hex("#0D1117"), 44); LE(rt0.gameObject, 260, 44);
                rt0.onClick.AddListener(() => StartRetrain(k0));
            }
            var again = Button_(btns, "처음으로 (R)", RCard, RText, 44); LE(again.gameObject, 130, 44); again.onClick.AddListener(Restart);
            var dl = Button_(btns, "진단 기록 JSON 저장", RCard, RText, 44); LE(dl.gameObject, 190, 44); dl.onClick.AddListener(() => SaveDiagnosisJson(record, r));
            var close = Button_(btns, "닫기 (Esc)", RCard, RText, 44); LE(close.gameObject, 120, 44); close.onClick.AddListener(CloseReport);

            // ── 2. AI 판단 로그 ──
            AddText(P(1), $"AI가 매 상황 전에 무엇을 보고 다음 상황을 골랐는지, 각 상황 뒤 역량 점수가 어떻게 바뀌었는지입니다.  <color=#8B96A8>운영 효율 {r.finalEfficiency}% · 최선의 선택 {rep.bestCount}/{def.events.Length}</color>", 14, RMute);
            var logBox = RBox(P(1), null, 14);
            LogRow(logBox, "#", "상황", "AI가 고른 이유", "판단", "점수 변화", true);
            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                bool sur = surprisePool.Contains(e.ev);
                string why = pickAt.TryGetValue(i, out var pk) ? (pk.byGemini ? "[Gemini] " : "") + pk.reason : "기본 측정 · 모든 사람이 같은 상황으로 기준선을 잽니다";
                LogRow(logBox, (i + 1).ToString(), e.ev.title + (sur ? "  <color=#E0B84A><b>돌발</b></color>" : ""), why, e.chosen.label,
                    i < eventDeltas.Count ? DeltaText(eventDeltas[i]) : "", false);
            }

            // ── 3. 검증 계획 ──
            AddText(P(2), "\"매뉴얼을 그냥 주면 더 빠르지 않나?\"는 실험으로 답해야 합니다. 아래는 <b>검증 계획</b>이며, 아직 실제 사람으로 검증한 결과가 아닙니다.", 16, RText);
            var vb = RBox(P(2), null, 14);
            LogRow(vb, null, "비교 항목", "기존 방식", "STEEL MIND", "", true);
            foreach (var (a, b, c) in new[] {
                ("교육", "동일한 매뉴얼", "취약 역량별 상황"),
                ("평가", "공통 문제 세트", "공통 문제 + AI 적응형 돌발상황"),
                ("피드백", "정답과 해설", "역량별 근거(어떤 선택이 점수를 올리고 내렸나)"),
                ("재평가", "같은 문제 반복", "조건이 다른 처음 보는 상황"),
                ("성과 측정", "정답률", "역량 점수 변화, 위험한 오판 수, 교육 시간") })
                LogRow(vb, null, a, b, c, "", false);
            AddText(P(2), "검증 목표: ① 같은 시간 안에 취약 역량을 얼마나 정확히 찾는가 ② 재훈련 뒤 처음 보는 상황에서 수행이 나아지는가.", 14, RMute);
            AddText(P(2), "근거: 상황판단검사(SJT)의 직무 성과 예측 타당도 ρ=.34 (McDaniel 외, 2001) · 적응형 검사의 최대 정보량 문항 선택 (Han, 2018) · NCS 1601020010 열간압연 안전관리 외.", 13, RMute);

            // ── 4. 회차 기록 ──
            var runsCard = RBox(P(3), null, 16);
            var rhd = NewRect("Head", runsCard); HLayout(rhd.gameObject, 8, TextAnchor.MiddleLeft);
            var rtt = AddText(rhd, "<b>회차 기록</b>  <color=#8B96A8>끝낸 판은 자동으로 저장됩니다 (최근 순)</color>", 16, RText, FontStyle.Normal, TextAnchor.MiddleLeft); LE(rtt.gameObject, -1, -1, 1);
            var clearBtn = SmallButton(rhd, "기록 지우기");
            var runsBox = NewRect("Runs", runsCard); VLayout(runsBox.gameObject, 0, 8, 0);
            System.Action draw = () => { DrawRuns(runsBox, record.no); ApplyReportFont(runsBox); };
            clearBtn.onClick.AddListener(() => { SaveRuns(new RunList()); draw(); });
            draw();

            ApplyReportFont(overlay);
            LayoutRebuilder.ForceRebuildLayoutImmediate(page);
            foreach (var pg in reportPages) LayoutRebuilder.ForceRebuildLayoutImmediate(pg.rt);
            ShowPage(0, 0);
            StartCoroutine(AskDiagnosis(r, null, analysisText, chip, path));
            yield break;
        }

        void LogRow(Transform parent, string n, string a, string b, string c, string d, bool head)
        {
            var row = NewRect("Row", parent); var h = HLayout(row.gameObject, 12, TextAnchor.UpperLeft); h.padding = new RectOffset(4, 4, 8, 8);
            h.childControlHeight = true; h.childForceExpandHeight = false;
            int fs = head ? 13 : 14; var col = head ? RMute : RText;
            if (n != null) { var t0 = AddText(row, n, fs, RMute); LE(t0.gameObject, 26); }
            var t1 = AddText(row, a, fs, col); LE(t1.gameObject, 250);
            var t2 = AddText(row, b, fs, head ? col : RMute); LE(t2.gameObject, d.Length > 0 ? 360 : 330);
            var t3 = AddText(row, c, fs, col); LE(t3.gameObject, d.Length > 0 ? 190 : 420);
            if (d.Length > 0) { var t4 = AddText(row, d, fs - 1, col); LE(t4.gameObject, -1, -1, 1); }
            var line = NewRect("Line", parent); line.gameObject.AddComponent<Image>().color = RLine; LE(line.gameObject, -1, 1);
        }

        [System.Serializable] class DiagJson { public int run; public string when; public string[] competency; public int[] score, confidence; public string[] events, choices, aiReasons; }

        void SaveDiagnosisJson(RunRecord record, RunResult r)
        {
            var j = new DiagJson
            {
                run = record.no, when = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                competency = CompetencyModel.Ids.Select(model.Name).ToArray(),
                score = CompetencyModel.Ids.Select(Score).ToArray(), confidence = CompetencyModel.Ids.Select(Conf).ToArray(),
                events = r.events.Select(e => e.ev.title).ToArray(), choices = r.events.Select(e => e.chosen.label).ToArray(),
                aiReasons = Enumerable.Range(0, r.events.Count).Select(i => pickAt.TryGetValue(i, out var pk) ? pk.reason : "기본 측정").ToArray(),
            };
            string path = System.IO.Path.Combine(Application.persistentDataPath, $"steelmind_진단기록_{record.no}회차.json");
            try { System.IO.File.WriteAllText(path, JsonUtility.ToJson(j, true)); StartCoroutine(AiToast("진단 기록 저장", path, 4f, false)); }
            catch (System.Exception e) { StartCoroutine(AiToast("저장 실패", e.Message, 4f, false)); }
        }

        // 맞춤 재훈련: 약한 역량을 조건이 다른, 아직 안 본 상황으로 한 번 더 확인한다
        void StartRetrain(string k)
        {
            var cand = surprisePool.Concat(baseEvents)
                .Where(e => !played.Contains(e) && e.probes != null && System.Array.IndexOf(e.probes, k) >= 0)
                .OrderByDescending(e => e.probes[0] == k ? 1 : 0).FirstOrDefault();
            if (cand == null) { StartCoroutine(AiToast("맞춤 재훈련", "남은 재훈련 상황이 없습니다", 2.5f, false)); return; }
            retrainTarget = k; retrainEvent = cand;
            retrainBefore = CompetencyModel.Ids.ToDictionary(i => i, Score);
            retrainBase = System.Array.ConvertAll(CompetencyModel.Ids, id => model.Radar(id));
            if (overlay) Destroy(overlay.gameObject);
            overlay = null; showingResult = false; finished = false;
            StartCoroutine(RetrainRun(cand, k));
        }

        IEnumerator RetrainRun(ScenarioEvent ev, string k)
        {
            var pk = new Pick { ev = ev, target = k, reason = $"재훈련 · {model.Name(k)} · 조건이 다른 처음 보는 상황으로 다시 확인합니다" };
            model.picks.Add(pk);
            int i = played.Count; pickAt[i] = pk;
            played.Add(ev); def.events = played.ToArray(); planned = played.Count;
            yield return AiToast("AI 판단 · 맞춤 재훈련", $"노리는 역량  <b>{model.Name(k)}</b>\n조건이 다른 처음 보는 상황으로 다시 확인합니다", 3f, true);
            yield return new WaitForSeconds(1.5f);
            yield return GoToSite(i);
            yield return EventChat(i);
            retraining = true;
            yield return new WaitForSeconds(1.5f);
            yield return ResultScreen();
            retraining = false;
        }

        // ── 보고서 탭 넘기기 ──
        readonly List<(RectTransform rt, string title)> reportPages = new List<(RectTransform, string)>();
        readonly List<Text> tabTexts = new List<Text>();
        readonly List<Image> tabBars = new List<Image>();
        ScrollRect reportScroll;
        int reportPage;
        Coroutine pageAnim;

        void ShowPage(int i, int dir)
        {
            if (reportPages.Count == 0) return;
            i = Mathf.Clamp(i, 0, reportPages.Count - 1);
            if (i == reportPage && dir != 0) return;
            reportPage = i;
            for (int k = 0; k < reportPages.Count; k++) if (reportPages[k].rt) reportPages[k].rt.gameObject.SetActive(k == i);
            var pg = reportPages[i].rt;
            if (reportScroll) { reportScroll.content = pg; reportScroll.verticalNormalizedPosition = 1; }
            for (int k = 0; k < tabTexts.Count; k++)
            {
                if (tabTexts[k]) tabTexts[k].color = k == i ? RText : RMute;
                if (tabBars[k]) tabBars[k].color = k == i ? RBlue : Color.clear;
            }
            if (pageAnim != null) StopCoroutine(pageAnim);
            pageAnim = StartCoroutine(SlideIn(pg, dir));
        }

        IEnumerator SlideIn(RectTransform pg, int dir)
        {
            var cg = pg.GetComponent<CanvasGroup>();
            float t = 0, dur = 0.22f;
            while (t < dur && pg)
            {
                t += Time.unscaledDeltaTime; float k = Mathf.SmoothStep(0, 1, t / dur);
                pg.anchoredPosition = new Vector2(Mathf.Lerp(50f * dir, 0, k), pg.anchoredPosition.y);
                if (cg) cg.alpha = dir == 0 ? 1 : k;
                yield return null;
            }
            if (pg) pg.anchoredPosition = new Vector2(0, pg.anchoredPosition.y);
            if (cg) cg.alpha = 1;
            pageAnim = null;
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
