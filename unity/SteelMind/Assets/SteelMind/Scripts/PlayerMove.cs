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
        Rigidbody2D body;
        Vector2 input;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        void Update()
        {
            input = ReadInput();
            if (input.sqrMagnitude > 1f) input.Normalize();
        }

        void FixedUpdate() => body.linearVelocity = input * speed;

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
