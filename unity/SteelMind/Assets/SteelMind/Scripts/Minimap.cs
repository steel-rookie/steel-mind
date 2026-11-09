using UnityEngine;

namespace SteelMind
{
    /// <summary>화면 구석 미니맵에 플레이어 위치(노란 점)를 표시한다. (M 키는 전체 지도로 바뀜, 숨기기는 H)</summary>
    public class Minimap : MonoBehaviour
    {
        public Transform player;
        public RectTransform map, marker;
        public Vector2 mapSizePx = new Vector2(1536, 560);
        const float PPU = 16f;

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
#endif
            if (!player || !map || !marker) return;
            float u = Mathf.Clamp01(player.position.x * PPU / mapSizePx.x);
            float v = Mathf.Clamp01(-player.position.y * PPU / mapSizePx.y);
            var r = map.rect;
            const float border = 2f;
            float s = (r.width) / (mapSizePx.x / 4f + border * 2);
            marker.anchoredPosition = new Vector2((border + u * mapSizePx.x / 4f) * s, -(border + v * mapSizePx.y / 4f) * s);
            marker.gameObject.SetActive(Mathf.FloorToInt(Time.unscaledTime * 3) % 3 != 0);
        }
    }
}
