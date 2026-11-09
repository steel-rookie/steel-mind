using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>
    /// 제철소 섬 지도 (도트). Resources/WorldMap/Island 의 레이어를 겹쳐 보여 주고,
    /// 공정 위에 마우스를 올리면 그 공정만 주황 외곽선으로 강조, 누르면 onPick(공정 번호)을 부른다.
    /// 공정 번호는 WorldMap.Maps 순서와 같다: 0 제선, 1 제강, 2 연주, 3 압연(열연).
    /// 쓰는 법: IslandMapView.Build(부모 RectTransform, 번호 => 이동, 지금 장면 이름)
    /// </summary>
    public class IslandMapView : MonoBehaviour
    {
        [Serializable] class Area { public string name, scene, hover, mask, here; }
        [Serializable] class Cloud { public string file; public float x, y, w, h, amp; }
        [Serializable] class Layout { public int[] size; public string background; public Area[] areas; public Cloud[] clouds; public float cloudPeriodSeconds = 8, smokeFps = 12.5f; public int smokeFrames = 20; }

        const string Dir = "WorldMap/Island/";
        Layout layout;
        RectTransform board;
        Action<int> onPick;
        readonly List<RawImage> hovers = new List<RawImage>();
        readonly List<Texture2D> masks = new List<Texture2D>();
        readonly List<(RectTransform rt, Cloud c)> clouds = new List<(RectTransform, Cloud)>();
        Texture2D[] smoke;
        RawImage smokeImg;
        int hovered = -1;

        /// <summary>지도 그림 판 (테두리 같은 꾸밈을 여기에 붙인다)</summary>
        public RectTransform Board => board;

        static Texture2D Tex(string name)
        {
            var b = Resources.Load<TextAsset>(Dir + name);
            if (!b) { Debug.LogWarning("[IslandMap] 없는 그림: " + name); return null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(b.bytes);
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>parent 안을 꽉 채우는 섬 지도를 만든다 (가로세로 비율 유지)</summary>
        public static IslandMapView Build(RectTransform parent, Action<int> onPick, string currentScene = null)
        {
            var json = Resources.Load<TextAsset>(Dir + "layout");
            if (!json) { Debug.LogError("[IslandMap] Resources/WorldMap/Island/layout.json 이 없습니다"); return null; }
            var go = new GameObject("IslandMap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<IslandMapView>();
            v.layout = JsonUtility.FromJson<Layout>(json.text);
            v.onPick = onPick;
            v.Make((RectTransform)go.transform, currentScene);
            return v;
        }

        RawImage Layer(string name, Texture2D tex)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(board, false); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<RawImage>(); img.texture = tex; img.raycastTarget = false;
            return img;
        }

        void Make(RectTransform root, string currentScene)
        {
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            board = new GameObject("Board", typeof(RectTransform)).GetComponent<RectTransform>();
            board.SetParent(root, false); board.anchorMin = board.anchorMax = new Vector2(0.5f, 0.5f);
            var fit = board.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = (float)layout.size[0] / layout.size[1];
            if (ColorUtility.TryParseHtmlString(layout.background, out var bg)) board.gameObject.AddComponent<Image>().color = bg;
            if (Resources.Load<TextAsset>(Dir + "sea")) Layer("Sea", Tex("sea"));   // 바다(육각 바탕)가 있는 버전이면 깔기

            foreach (var c in layout.clouds)   // 구름 (섬 뒤)
            {
                var img = Layer(c.file, Tex(c.file));
                var rt = img.rectTransform;
                float W = layout.size[0], H = layout.size[1];
                rt.anchorMin = new Vector2(c.x / W, 1 - (c.y + c.h) / H); rt.anchorMax = new Vector2((c.x + c.w) / W, 1 - c.y / H);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                clouds.Add((rt, c));
            }
            Layer("Base", Tex("base"));
            smoke = new Texture2D[layout.smokeFrames];
            for (int i = 0; i < smoke.Length; i++) smoke[i] = Tex($"smoke_{i:00}");
            smokeImg = Layer("Smoke", smoke.Length > 0 ? smoke[0] : null);
            Layer("Labels", Tex("labels"));
            foreach (var a in layout.areas)   // 지금 있는 공정은 늘 주황 테로 표시 (마우스 강조보다 아래)
                if (a.scene == currentScene && !string.IsNullOrEmpty(a.here)) Layer("Here_" + a.scene, Tex(a.here));
            foreach (var a in layout.areas)
            {
                var h = Layer("Hover_" + a.scene, Tex(a.hover)); h.enabled = false;
                hovers.Add(h); masks.Add(Tex(a.mask));
            }
        }

        /// <summary>화면 좌표 → 지도 픽셀 좌표 (왼쪽 위 0,0). 지도 밖이면 false</summary>
        bool ToPixel(Vector2 screen, out int px, out int py)
        {
            px = py = -1;
            var cam = board.GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay ? null : board.GetComponentInParent<Canvas>().worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screen, cam, out var lp)) return false;
            var r = board.rect;
            float u = (lp.x - r.xMin) / r.width, v = (lp.y - r.yMin) / r.height;
            if (u < 0 || u > 1 || v < 0 || v > 1) return false;
            px = (int)(u * layout.size[0]); py = (int)(v * layout.size[1]);   // Texture2D는 아래가 0
            return true;
        }

        void Update()
        {
            float t = Time.unscaledTime;
            foreach (var (rt, c) in clouds)   // 구름 좌우로 천천히
            {
                float dx = c.amp * Mathf.Sin(2 * Mathf.PI * t / layout.cloudPeriodSeconds + c.x) / layout.size[0];
                float x0 = c.x / layout.size[0] + dx, x1 = (c.x + c.w) / layout.size[0] + dx;
                rt.anchorMin = new Vector2(x0, rt.anchorMin.y); rt.anchorMax = new Vector2(x1, rt.anchorMax.y);
            }
            if (smoke.Length > 0) smokeImg.texture = smoke[(int)(t * layout.smokeFps) % smoke.Length];

            int now = -1;
            var mouse = Mouse.current;
            if (mouse != null && ToPixel(mouse.position.ReadValue(), out int px, out int py))
                for (int i = 0; i < masks.Count; i++)
                    if (masks[i] && masks[i].GetPixel(px, py).a > 0.5f) { now = i; break; }
            if (now != hovered)
            {
                for (int i = 0; i < hovers.Count; i++) hovers[i].enabled = i == now;
                hovered = now;
            }
            if (now >= 0 && mouse != null && mouse.leftButton.wasPressedThisFrame) onPick?.Invoke(now);
        }
    }
}
