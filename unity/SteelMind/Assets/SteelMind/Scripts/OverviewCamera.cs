using UnityEngine;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>「전체 공정 보기」: 카메라를 빼서 압연 라인 전체를 보여 주고, 다시 누르면 캐릭터로 돌아온다. Tab 키도 된다.</summary>
    public class OverviewCamera : MonoBehaviour
    {
        public Camera cam;
        public CameraFollow follow;
        public Behaviour pixelPerfect;
        public PlayerMove playerMove;
        public Text label;
        public Vector2 mapSizePx = new Vector2(1536, 560);
        public bool overview;
        float normalSize;
        const float PPU = 16f;

        void Start() { if (cam) normalSize = cam.orthographicSize; Apply(); }

        public void Toggle() { overview = !overview; Apply(); }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.tabKey.wasPressedThisFrame) Toggle();
#endif
            if (overview && cam)
            {
                float w = mapSizePx.x / PPU, h = mapSizePx.y / PPU;
                cam.orthographicSize = Mathf.Max(h / 2f, w / 2f / cam.aspect);
                cam.transform.position = new Vector3(w / 2f, -h / 2f, -10f);
            }
        }

        void Apply()
        {
            if (pixelPerfect) pixelPerfect.enabled = !overview;
            if (follow) follow.enabled = !overview;
            if (playerMove) playerMove.enabled = !overview;
            if (!overview && cam && normalSize > 0) cam.orthographicSize = normalSize;
            if (label) label.text = overview ? "캐릭터로 돌아가기 (Tab)" : "전체 공정 보기 (Tab)";
        }
    }
}
