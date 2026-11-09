using UnityEngine;
using UnityEngine.UI;

namespace SteelMind.Scenario
{
    /// <summary>uGUI 레이더(오각형) 차트. values는 0~1.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class RadarChart : MaskableGraphic
    {
        public float[] values = new float[5];
        public Color gridColor = new Color(1, 1, 1, 0.12f), fillColor = new Color(0.42f, 0.76f, 0.69f, 0.35f), lineColor = new Color(0.42f, 0.76f, 0.69f, 1f);
        public int rings = 3;
        public float lineWidth = 2f;

        Vector2 Point(int i, float r)
        {
            int n = values.Length;
            float a = Mathf.PI / 2f - i * 2f * Mathf.PI / n;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = values.Length;
            if (n < 3) return;
            float R = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 2f;
            for (int k = 1; k <= rings; k++)
                for (int i = 0; i < n; i++) Line(vh, Point(i, R * k / rings), Point((i + 1) % n, R * k / rings), 1f, gridColor);
            for (int i = 0; i < n; i++) Line(vh, Vector2.zero, Point(i, R), 1f, gridColor);
            int c0 = vh.currentVertCount;
            vh.AddVert(Vector3.zero, fillColor, Vector2.zero);
            for (int i = 0; i < n; i++) vh.AddVert(Point(i, R * Mathf.Clamp01(values[i])), fillColor, Vector2.zero);
            for (int i = 0; i < n; i++) vh.AddTriangle(c0, c0 + 1 + i, c0 + 1 + (i + 1) % n);
            for (int i = 0; i < n; i++) Line(vh, Point(i, R * Mathf.Clamp01(values[i])), Point((i + 1) % n, R * Mathf.Clamp01(values[(i + 1) % n])), lineWidth, lineColor);
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            var d = (b - a).normalized; var nrm = new Vector2(-d.y, d.x) * w / 2f;
            int i = vh.currentVertCount;
            vh.AddVert(a - nrm, c, Vector2.zero); vh.AddVert(a + nrm, c, Vector2.zero);
            vh.AddVert(b + nrm, c, Vector2.zero); vh.AddVert(b - nrm, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
