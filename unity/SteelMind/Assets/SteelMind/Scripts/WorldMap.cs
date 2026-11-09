using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>
    /// 제철소 전체 지도 (M 키). 공정 맵 4개를 공정 순서대로 보여 주고, 누르면 그 공정으로 이동한다.
    /// 공정 장면이 열리면 스스로 생긴다. 회의·결정 중(캐릭터가 멈춰 있을 때)에는 열리지 않는다.
    /// </summary>
    public class WorldMap : MonoBehaviour
    {
        static readonly (string scene, string name, string desc)[] Maps =
        {
            ("Ironmaking", "제선", "고로에서 철광석을 녹여 쇳물을 만듭니다"),
            ("Steelmaking", "제강", "전로에서 불순물을 빼 강을 만듭니다"),
            ("Casting", "연주", "쇳물을 굳혀 슬라브를 만듭니다"),
            ("Rolling", "압연", "슬라브를 늘려 코일로 감습니다"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (System.Array.FindIndex(Maps, x => x.scene == s.name) < 0 || FindFirstObjectByType<WorldMap>()) return;
            new GameObject("WorldMap").AddComponent<WorldMap>();
        }

        Font font, bold;
        RectTransform panel;
        PlayerMove player;

        void Start()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11") ?? Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 20);
            bold = Resources.Load<Font>("Fonts/Galmuri11-Bold") ?? font;
            player = FindFirstObjectByType<PlayerMove>();
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            bool open = panel;
            if (k.mKey.wasPressedThisFrame)
            {
                if (open) Close();
                else if (!player || player.enabled) Open();   // 회의 중에는 열지 않는다
            }
            if (!panel) return;
            if (k.escapeKey.wasPressedThisFrame) Close();
            if (k.digit1Key.wasPressedThisFrame) Go(0);
            if (k.digit2Key.wasPressedThisFrame) Go(1);
            if (k.digit3Key.wasPressedThisFrame) Go(2);
            if (k.digit4Key.wasPressedThisFrame) Go(3);
        }

        void Go(int i)
        {
            var target = Maps[i].scene;
            if (SceneManager.GetActiveScene().name == target) { Close(); return; }
            Close();
            SceneFade.Go(target, 0.4f, 0.7f);
        }

        void Close()
        {
            if (panel) Destroy(panel.transform.root.gameObject == panel.gameObject ? panel.gameObject : panel.GetComponentInParent<Canvas>().gameObject);
            panel = null;
            if (player) player.enabled = true;
            foreach (var s in FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None)) s.enabled = true;
        }

        [System.Serializable] class Zone { public string label; public int[] rect; public int[] anchor; }
        RectTransform pin;

        void Open()
        {
            if (player) { player.enabled = false; var b = player.GetComponent<Rigidbody2D>(); if (b) b.linearVelocity = Vector2.zero; }
            foreach (var s in FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None)) s.enabled = false;   // 1~4는 지도에서 쓴다

            var go = new GameObject("WorldMapUI");
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1280, 720); sc.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (!FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>())
            {
                var es = new GameObject("EventSystem"); es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            var root = go.GetComponent<RectTransform>();
            var dim = R("Dim", root, Vector2.zero, Vector2.one); dim.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            // 지도 그림 (640x400 도트를 1.6배)
            const float MW = 640, MH = 400, K = 1.6f;
            panel = R("Map", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)); panel.sizeDelta = new Vector2(MW * K, MH * K);
            var frame = panel.gameObject.AddComponent<Outline>();
            var raw = panel.gameObject.AddComponent<RawImage>();
            frame.effectColor = new Color32(90, 70, 48, 255); frame.effectDistance = new Vector2(5, -5);
            var bytes = Resources.Load<TextAsset>("WorldMap/world");
            if (bytes) { var tex = new Texture2D(2, 2); tex.LoadImage(bytes.bytes); tex.filterMode = FilterMode.Point; raw.texture = tex; }

            // 두루마리 제목
            var scroll = Ribbon(panel, "STEEL MIND 제철소", 30, new Vector2(1, 1), new Vector2(-24, -20), new Vector2(1, 1), true);

            // 공정 띠 이름표 + 누를 수 있는 건물 자리
            string here = SceneManager.GetActiveScene().name;
            var lay = Resources.Load<TextAsset>("WorldMap/world_layout");
            var zones = new Dictionary<string, Zone>();
            if (lay)
            {
                foreach (var (scene, _, _) in Maps)
                {
                    int i = lay.text.IndexOf("\"" + scene + "\"");
                    if (i < 0) continue;
                    int a2 = lay.text.IndexOf('{', i), b2 = lay.text.IndexOf('}', a2);
                    zones[scene] = JsonUtility.FromJson<Zone>(lay.text.Substring(a2, b2 - a2 + 1));
                }
            }
            for (int i = 0; i < Maps.Length; i++)
            {
                var (scene, name, desc) = Maps[i];
                if (!zones.TryGetValue(scene, out var z)) continue;
                int idx = i;
                var hit = R("Hit", panel, new Vector2(0, 1), new Vector2(0, 1)); hit.pivot = new Vector2(0, 1);
                hit.anchoredPosition = new Vector2(z.rect[0] * K, -z.rect[1] * K); hit.sizeDelta = new Vector2(z.rect[2] * K, z.rect[3] * K);
                var hi = hit.gameObject.AddComponent<Image>(); hi.color = new Color(1, 1, 1, 0);
                var hb = hit.gameObject.AddComponent<Button>(); hb.targetGraphic = hi;
                var hc = hb.colors; hc.highlightedColor = new Color(1, 1, 1, 0.12f); hb.colors = hc;
                hb.onClick.AddListener(() => Go(idx));
                bool cur = scene == here;
                var rib = Ribbon(panel, $"{z.label}", 22, new Vector2(0, 1), new Vector2(z.anchor[0] * K, -(z.rect[1] + z.rect[3]) * K - 4), new Vector2(0.5f, 1), false, cur);
                var rb = rib.gameObject.AddComponent<Button>(); rb.onClick.AddListener(() => Go(idx));
                if (cur)
                {
                    pin = R("Pin", panel, new Vector2(0, 1), new Vector2(0, 1)); pin.pivot = new Vector2(0.5f, 0);
                    pin.anchoredPosition = new Vector2(z.anchor[0] * K, -z.anchor[1] * K + 6); pin.sizeDelta = new Vector2(150, 40);
                    var pi = pin.gameObject.AddComponent<Image>(); pi.color = new Color32(229, 83, 61, 255);
                    var pt = T(pin, "▼ 현재 위치", 18, Color.white, bold); Stretch(pt.rectTransform, 0, 0);
                }
            }
            var foot = T(panel, "가고 싶은 공장을 누르세요 · 숫자 1~4 · 닫기 M", 16, new Color32(220, 228, 236, 255), font);
            Place(foot.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(900, 24));
            var fs = foot.gameObject.AddComponent<Shadow>(); fs.effectColor = new Color(0, 0, 0, 0.8f);
        }

        // 띠 모양 이름표 (크림색 종이 + 진한 테두리). 현재 위치는 금색
        RectTransform Ribbon(Transform parent, string text, int size, Vector2 anchor, Vector2 pos, Vector2 pivot, bool scroll, bool current = false)
        {
            var rt = R("Ribbon", parent, anchor, anchor); rt.pivot = pivot; rt.anchoredPosition = pos;
            var img = rt.gameObject.AddComponent<Image>(); img.color = current ? new Color32(255, 214, 102, 255) : new Color32(239, 227, 194, 255);
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color32(74, 54, 34, 255); o.effectDistance = new Vector2(3, -3);
            var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.45f); sh.effectDistance = new Vector2(4, -5);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>(); h.padding = scroll ? new RectOffset(34, 34, 12, 12) : new RectOffset(18, 18, 6, 6);
            h.childControlWidth = h.childControlHeight = true;
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = T(rt, text, size, new Color32(58, 40, 24, 255), bold);
            if (scroll)
            {
                foreach (var side in new[] { 0f, 1f })
                {
                    var end = R("End", rt, new Vector2(side, 0), new Vector2(side, 1)); end.sizeDelta = new Vector2(14, 10); end.anchoredPosition = Vector2.zero;
                    end.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    end.gameObject.AddComponent<Image>().color = new Color32(150, 60, 40, 255);
                }
            }
            return rt;
        }

        void LateUpdate()
        {
            if (pin) pin.localScale = Vector3.one * (1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)));
        }

        RectTransform R(string n, Transform p, Vector2 aMin, Vector2 aMax)
        {
            var rt = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(p, false); rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Stretch(RectTransform r, float padX, float padY) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(padX, padY); r.offsetMax = new Vector2(-padX, -padY); }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size) { r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(anchor.x, anchor.y == 1 ? 1 : 0.5f); r.anchoredPosition = pos; r.sizeDelta = size; }

        Text T(Transform p, string s, int size, Color c, Font f)
        {
            var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(p, false);
            var t = go.AddComponent<Text>(); t.font = f; t.fontSize = size; t.color = c; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.supportRichText = true; t.text = s;
            return t;
        }
    }
}
