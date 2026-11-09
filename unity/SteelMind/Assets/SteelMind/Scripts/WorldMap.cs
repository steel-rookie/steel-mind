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
            Travel.from = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(target);
        }

        void Close()
        {
            if (panel) Destroy(panel.transform.root.gameObject == panel.gameObject ? panel.gameObject : panel.GetComponentInParent<Canvas>().gameObject);
            panel = null;
            if (player) player.enabled = true;
            foreach (var s in FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None)) s.enabled = true;
        }

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
            var dim = R("Dim", root, Vector2.zero, Vector2.one); dim.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            panel = R("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)); panel.sizeDelta = new Vector2(1140, 520);
            panel.gameObject.AddComponent<Image>().color = new Color32(28, 26, 33, 250);
            var o = panel.gameObject.AddComponent<Outline>(); o.effectColor = new Color32(240, 192, 64, 255); o.effectDistance = new Vector2(3, -3);
            // 제목 띠
            var bar = R("Bar", panel, new Vector2(0, 1), new Vector2(1, 1)); bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, 56);
            bar.gameObject.AddComponent<Image>().color = new Color32(46, 40, 56, 255);
            var title = T(bar, "제철소 전체 지도", 26, Color.white, bold); Stretch(title.rectTransform, 24, 0); title.alignment = TextAnchor.MiddleLeft;
            var sub = T(bar, "가고 싶은 공정을 누르세요 · 닫기 M", 15, new Color32(170, 166, 182, 255), font); Stretch(sub.rectTransform, 24, 0); sub.alignment = TextAnchor.MiddleRight;

            string here = SceneManager.GetActiveScene().name;
            float cardW = 240, gap = 40, x0 = -(cardW * 4 + gap * 3) / 2f + cardW / 2f;
            for (int i = 0; i < Maps.Length; i++)
            {
                var (scene, name, desc) = Maps[i];
                bool cur = scene == here;
                var card = R("Card", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                card.sizeDelta = new Vector2(cardW, 360); card.anchoredPosition = new Vector2(x0 + i * (cardW + gap), -24);
                var img = card.gameObject.AddComponent<Image>(); img.color = cur ? new Color32(44, 64, 58, 255) : new Color32(40, 37, 48, 255);
                var co = card.gameObject.AddComponent<Outline>(); co.effectColor = cur ? new Color32(108, 195, 176, 255) : new Color32(70, 64, 82, 255); co.effectDistance = new Vector2(2, -2);
                var btn = card.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
                var cs = btn.colors; cs.highlightedColor = new Color(1.25f, 1.25f, 1.25f); btn.colors = cs;
                int idx = i; btn.onClick.AddListener(() => Go(idx));

                var num = T(card, $"{i + 1}", 16, new Color32(240, 192, 64, 255), bold); Place(num.rectTransform, new Vector2(0, 1), new Vector2(16, -12), new Vector2(40, 24)); num.alignment = TextAnchor.UpperLeft;
                var thumb = R("Thumb", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1)); thumb.pivot = new Vector2(0.5f, 1);
                thumb.anchoredPosition = new Vector2(0, -44); thumb.sizeDelta = new Vector2(cardW - 24, 150);
                var ti = thumb.gameObject.AddComponent<RawImage>();
                var bytes = Resources.Load<TextAsset>($"WorldMap/{scene}");
                if (bytes) { var tex = new Texture2D(2, 2); tex.LoadImage(bytes.bytes); tex.filterMode = FilterMode.Point; ti.texture = tex;
                    var fit = thumb.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = (float)tex.width / tex.height; }
                var nm = T(card, $"{name} 공장", 26, Color.white, bold); Place(nm.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 116), new Vector2(cardW, 34));
                var ds = T(card, desc, 14, new Color32(190, 186, 200, 255), font); Place(ds.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(cardW - 24, 54));
                ds.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (cur)
                {
                    var tag = R("Here", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0)); tag.sizeDelta = new Vector2(120, 30); tag.anchoredPosition = new Vector2(0, 26);
                    tag.gameObject.AddComponent<Image>().color = new Color32(108, 195, 176, 255);
                    var tt = T(tag, "현재 위치", 15, new Color32(11, 34, 30, 255), bold); Stretch(tt.rectTransform, 0, 0);
                }
                else
                {
                    var go2 = T(card, "이동하기 ▶", 15, new Color32(240, 192, 64, 255), bold); Place(go2.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(cardW, 26));
                }
                if (i < Maps.Length - 1)
                {
                    var arrow = T(panel, "▶", 26, new Color32(240, 160, 75, 255), bold);
                    Place(arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x0 + i * (cardW + gap) + cardW / 2f + gap / 2f, -24), new Vector2(gap, 40));
                }
            }
            var foot = T(panel, "쇳물부터 코일까지 · 제선 → 제강 → 연주 → 압연", 15, new Color32(170, 166, 182, 255), font);
            Place(foot.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(900, 24));
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
