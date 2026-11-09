using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>
    /// 메인 화면(Title 장면). 밤의 제철소 배경에 굴뚝 연기·경고등이 움직이고, Space·Enter·클릭으로 공장(압연)에 들어간다.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        public string firstScene = "Rolling";
        const float BgW = 480, BgH = 270;

        [System.Serializable] class LayoutData { public int[] size; public List<Stack> stacks; }
        [System.Serializable] class Stack { public int x, y; }

        Font font, boldFont;
        RectTransform bg;
        Text hint;
        Image fade;
        Sprite puff;
        readonly List<RectTransform> lights = new List<RectTransform>();
        readonly List<Vector2> stackTops = new List<Vector2>();
        bool leaving;

        void Start()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11") ?? Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 20);
            boldFont = Resources.Load<Font>("Fonts/Galmuri11-Bold") ?? font;
            puff = MakePuff(32);
            var cam = Camera.main;
            if (cam)
            {
                var clip = Resources.Load<AudioClip>("Audio/bgm_title");
                if (clip) { var src = cam.gameObject.AddComponent<AudioSource>(); src.clip = clip; src.loop = true; src.volume = 0.5f; src.Play(); }
            }
            Build();
            StartCoroutine(Smoke());
        }

        void Build()
        {
            var go = new GameObject("TitleUI");
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (!FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>())
            {
                var es = new GameObject("EventSystem"); es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            var root = go.GetComponent<RectTransform>();
            var black = Rect("Black", root, Vector2.zero, Vector2.one); black.gameObject.AddComponent<Image>().color = Color.black;

            // 배경 (화면을 꽉 채우도록)
            bg = Rect("Background", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            bg.sizeDelta = new Vector2(BgW, BgH);
            var raw = bg.gameObject.AddComponent<RawImage>();
            var bytes = Resources.Load<TextAsset>("Title/title_bg");
            if (bytes)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); tex.LoadImage(bytes.bytes); tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                raw.texture = tex;
            }
            var fit = bg.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = BgW / BgH;

            var lay = Resources.Load<TextAsset>("Title/title_layout");
            if (lay)
            {
                var raw2 = MiniJsonStacks(lay.text);
                foreach (var s in raw2)
                {
                    stackTops.Add(s);
                    var l = Rect("Light", bg, new Vector2(s.x / BgW, 1 - s.y / BgH), new Vector2(s.x / BgW, 1 - s.y / BgH));
                    l.sizeDelta = new Vector2(2, 2); l.anchoredPosition = new Vector2(0, 2);
                    l.gameObject.AddComponent<Image>().color = new Color(1f, 0.25f, 0.2f);
                    lights.Add(l);
                }
            }

            // 위쪽을 살짝 어둡게 해서 글자가 잘 보이게
            var top = Rect("Shade", root, new Vector2(0, 0.45f), Vector2.one);
            var shade = top.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, 0.25f);

            var col = Rect("Logo", root, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f)); col.sizeDelta = new Vector2(1100, 260);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.MiddleCenter; v.spacing = 6;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandHeight = false;
            var steelBlue = new Color32(169, 196, 220, 255);
            Label(col, "POSCO K-뉴딜 아카데미 해커톤 · 팀 철든신입", 18, steelBlue);
            // 로고: 도트로 그린 크롬 글자 (tools/pixel-art/title_logo.py)
            var logoBytes = Resources.Load<TextAsset>("Title/title_logo");
            if (logoBytes)
            {
                var tex = new Texture2D(2, 2); tex.LoadImage(logoBytes.bytes); tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                var lr = Rect("LogoImage", col, Vector2.zero, Vector2.zero);
                lr.gameObject.AddComponent<RawImage>().texture = tex;
                var le = lr.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = tex.width * 6; le.preferredHeight = tex.height * 6;
            }
            else
            {
                var logo = Label(col, "STEEL MIND", 120, Color.white); logo.font = boldFont;
            }
            Label(col, "AI Agent들의 회의로 운영하는 제철소", 26, new Color(0.88f, 0.91f, 0.95f));

            var h = Rect("Hint", root, new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f)); h.sizeDelta = new Vector2(700, 50);
            hint = Label(h, "PRESS SPACE", 28, Color.white); hint.font = boldFont;
            Stretch(hint.rectTransform);
            var hs = hint.gameObject.AddComponent<Shadow>(); hs.effectColor = new Color(0, 0, 0, 0.8f); hs.effectDistance = new Vector2(2, -2);
            var f = Rect("Foot", root, new Vector2(0.5f, 0.04f), new Vector2(0.5f, 0.04f)); f.sizeDelta = new Vector2(1000, 26);
            var ft = Label(f, "가상의 교육·체험 시뮬레이션입니다. 모든 값은 데모용이며 실제 제철소 기준이 아닙니다.", 14, new Color(0.75f, 0.73f, 0.8f));
            Stretch(ft.rectTransform);

            var click = Rect("Click", root, Vector2.zero, Vector2.one);
            var ci = click.gameObject.AddComponent<Image>(); ci.color = new Color(0, 0, 0, 0);
            click.gameObject.AddComponent<Button>().onClick.AddListener(Enter);

            fade = Rect("Fade", root, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            fade.color = new Color(0, 0, 0, 1); fade.raycastTarget = false;
            StartCoroutine(FadeTo(0f, 0.8f));
        }

        // title_layout.json 의 stacks 를 읽는다 ({"stacks": [[x, y], ...]})
        static List<Vector2> MiniJsonStacks(string json)
        {
            var list = new List<Vector2>();
            int i = json.IndexOf("\"stacks\"");
            if (i < 0) return list;
            i = json.IndexOf('[', i) + 1;
            while (true)
            {
                int a = json.IndexOf('[', i); int end = json.IndexOf(']', i);
                if (a < 0 || (end >= 0 && end < a)) break;
                int b = json.IndexOf(']', a);
                var parts = json.Substring(a + 1, b - a - 1).Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y)) list.Add(new Vector2(x, y));
                i = b + 1;
            }
            return list;
        }

        void Update()
        {
            float t = Time.time;
            if (hint) hint.color = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 2.4f)));
            for (int i = 0; i < lights.Count; i++)
                if (lights[i]) lights[i].gameObject.SetActive(Mathf.FloorToInt(t * 1.2f + i * 0.37f) % 2 == 0);
            var k = Keyboard.current;
            if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Enter();
        }

        void Enter()
        {
            if (leaving) return;
            leaving = true;
            StartCoroutine(Leave());
        }

        IEnumerator Leave()
        {
            // 글자를 먼저 걷어 내고, 공장으로 천천히 넘어간다
            float t = 0;
            var logo = bg ? bg.parent.Find("Logo") : null;
            CanvasGroup g = null;
            if (logo) { g = logo.GetComponent<CanvasGroup>(); if (!g) g = logo.gameObject.AddComponent<CanvasGroup>(); }
            while (t < 0.5f) { t += Time.deltaTime; if (g) g.alpha = 1 - t / 0.5f; if (hint) hint.color = new Color(1, 1, 1, 1 - t / 0.5f); yield return null; }
            SceneFade.Go(firstScene, 0.9f, 1.4f);
        }

        IEnumerator FadeTo(float a, float dur)
        {
            float a0 = fade.color.a, t = 0;
            while (t < dur) { t += Time.deltaTime; fade.color = new Color(0, 0, 0, Mathf.Lerp(a0, a, t / dur)); yield return null; }
            fade.color = new Color(0, 0, 0, a);
        }

        // 굴뚝 연기: 꼭대기에서 올라가며 커지고 옅어진다
        IEnumerator Smoke()
        {
            while (true)
            {
                foreach (var s in stackTops) StartCoroutine(Puff(s));
                yield return new WaitForSeconds(0.55f);
            }
        }

        IEnumerator Puff(Vector2 top)
        {
            if (!bg) yield break;
            var rt = Rect("Smoke", bg, new Vector2(top.x / BgW, 1 - top.y / BgH), new Vector2(top.x / BgW, 1 - top.y / BgH));
            rt.SetAsFirstSibling();
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = puff;
            float life = 4.5f, t = 0, drift = Random.Range(6f, 12f), size0 = Random.Range(5f, 7f);
            while (t < life && rt)
            {
                t += Time.deltaTime; float k = t / life;
                rt.anchoredPosition = new Vector2(Mathf.Round(drift * k * 2.5f), Mathf.Round(4 + 30 * k));
                float s = Mathf.Round(size0 + 16 * k); rt.sizeDelta = new Vector2(s, s);
                img.color = new Color(0.78f, 0.62f, 0.58f, 0.6f * (1 - k));   // 불빛에 물든 밤 연기
                yield return null;
            }
            if (rt) Destroy(rt.gameObject);
        }

        static Sprite MakePuff(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    bool on = d < 0.95f && ((x + y) % 2 == 0 || d < 0.6f);
                    px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        Text Label(Transform parent, string s, int size, Color c)
        {
            var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); t.font = font; t.fontSize = size; t.color = c; t.alignment = TextAnchor.MiddleCenter;
            t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.text = s;
            return t;
        }
    }
}
