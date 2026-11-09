using UnityEngine;

namespace SteelMind
{
    /// <summary>이동 방향에 맞춰 앞·뒤·왼쪽·오른쪽 걷기 그림을 바꾼다. 멈추면 그 방향으로 서 있는다.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : MonoBehaviour
    {
        public Sprite[] down = new Sprite[4], left = new Sprite[4], right = new Sprite[4], up = new Sprite[4];
        public float framesPerSecond = 8f;
        SpriteRenderer sr;
        Rigidbody2D body;
        Sprite[] current;
        float clock;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            body = GetComponentInParent<Rigidbody2D>();
            current = down;
        }

        void Update()
        {
            Vector2 v = body ? body.linearVelocity : Vector2.zero;
            bool moving = v.sqrMagnitude > 0.01f;
            if (moving)
                current = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? right : left) : (v.y > 0 ? up : down);
            clock = moving ? clock + Time.deltaTime * framesPerSecond : 0f;
            int f = moving ? Mathf.FloorToInt(clock) % 4 : 0;
            if (current != null && current.Length == 4 && current[f]) sr.sprite = current[f];
        }
    }
}
