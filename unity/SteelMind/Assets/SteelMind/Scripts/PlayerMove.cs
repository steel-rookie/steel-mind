using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SteelMind
{
    /// <summary>탑다운 8방향 이동 (WASD·방향키). Rigidbody2D로 움직여 충돌 영역을 지킨다.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMove : MonoBehaviour
    {
        [Tooltip("초당 이동 거리(타일)")]
        public float speed = 5f;
        [Tooltip("보행자 통로(초록 띠) 위에서 곱해지는 속도")]
        public float walkwayBoost = 1.7f;
        Rigidbody2D body;
        Rect walkway;   // 월드 좌표 (지도 왼쪽 위가 0,0 · y는 아래로 음수)
        Vector2 input;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        void Start()
        {
            // 지도 그림(tools/pixel-art)에 칠한 보행자 통로 위치와 똑같이 맞춘다
            var cam = Camera.main;
            var f = cam ? cam.GetComponent<CameraFollow>() : null;
            if (!f) return;
            const float ppu = YSort.PixelsPerUnit;
            float W = f.mapSizePx.x, H = f.mapSizePx.y;
            bool rolling = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Rolling";
            float x0 = 24, x1 = rolling ? 1300 : W - 24, y0 = rolling ? 352 : H - 52, y1 = rolling ? 378 : H - 28;
            const float pad = 6;   // 발이 띠에 걸치기만 해도 빨라지게
            walkway = Rect.MinMaxRect(x0 / ppu, -(y1 + pad) / ppu, x1 / ppu, -(y0 - pad) / ppu);
        }

        void Update()
        {
            input = ReadInput();
            if (input.sqrMagnitude > 1f) input.Normalize();
        }

        void FixedUpdate() => body.linearVelocity = input * speed * (walkway.Contains(body.position) ? walkwayBoost : 1f);

        static Vector2 ReadInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }
    }
}
