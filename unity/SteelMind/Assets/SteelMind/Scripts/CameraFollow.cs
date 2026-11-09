using UnityEngine;

namespace SteelMind
{
    /// <summary>플레이어를 따라가되 지도 밖은 보여 주지 않는다. 위치는 1px 단위로 맞춘다.</summary>
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        [Tooltip("지도 크기(px). RollingMapBuilder가 채운다")]
        public Vector2 mapSizePx = new Vector2(1536, 560);
        public float smooth = 10f;
        Camera cam;

        void Awake() => cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (!target || !cam) return;
            const float ppu = YSort.PixelsPerUnit;
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            float mapW = mapSizePx.x / ppu, mapH = mapSizePx.y / ppu;
            Vector3 want = target.position;
            want.x = mapW <= halfW * 2 ? mapW / 2 : Mathf.Clamp(want.x, halfW, mapW - halfW);
            want.y = mapH <= halfH * 2 ? -mapH / 2 : Mathf.Clamp(want.y, -mapH + halfH, -halfH);
            Vector3 p = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-smooth * Time.deltaTime));
            p.x = Mathf.Round(p.x * ppu) / ppu;
            p.y = Mathf.Round(p.y * ppu) / ppu;
            p.z = -10f;
            transform.position = p;
        }
    }
}
