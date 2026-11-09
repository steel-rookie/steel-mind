using UnityEngine;

namespace SteelMind
{
    /// <summary>
    /// 움직이는 물체(플레이어 등)의 그리기 순서를 발 위치로 정한다.
    /// 지도는 왼쪽 위가 (0,0)이고 아래로 갈수록 world y가 작아진다. 아래쪽(앞쪽)일수록 나중에 그린다.
    /// 고정 물체는 RollingMapBuilder가 layout.json의 sortY로 미리 정해 둔다.
    /// </summary>
    [ExecuteAlways]
    public class YSort : MonoBehaviour
    {
        public const float PixelsPerUnit = 16f;
        [Tooltip("스프라이트 기준점에서 발까지의 거리(px). 기준점이 발이면 0")]
        public float footOffsetPx = 0f;

        SpriteRenderer[] renderers;

        void OnEnable() => renderers = GetComponentsInChildren<SpriteRenderer>();

        void LateUpdate()
        {
            int order = Mathf.RoundToInt(-transform.position.y * PixelsPerUnit + footOffsetPx);
            foreach (var r in renderers)
                if (r) r.sortingOrder = order;
        }
    }
}
