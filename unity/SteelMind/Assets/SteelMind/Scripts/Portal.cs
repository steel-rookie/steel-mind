using UnityEngine;
using UnityEngine.SceneManagement;

namespace SteelMind
{
    /// <summary>플레이어가 이 칸(화면 왼쪽·오른쪽 끝)에 들어오면 이전·다음 공정 장면으로 간다.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class Portal : MonoBehaviour
    {
        public string scene;
        public string label;

        void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.GetComponent<PlayerMove>() || string.IsNullOrEmpty(scene)) return;
            SceneFade.Go(scene, 0.4f, 0.6f);
        }
    }

    /// <summary>장면을 넘나들 때 어디서 왔는지 기억한다.</summary>
    public static class Travel
    {
        public static string from;
    }

    /// <summary>옆 공정에서 넘어왔으면 그쪽 끝에 플레이어를 세운다.</summary>
    public class WorldSpawn : MonoBehaviour
    {
        public string[] scenes;
        public Vector2[] points;

        void Start()
        {
            if (string.IsNullOrEmpty(Travel.from) || scenes == null) return;
            int i = System.Array.IndexOf(scenes, Travel.from);
            if (i < 0) return;
            transform.position = new Vector3(points[i].x / 16f, -points[i].y / 16f, 0);
            var cam = Camera.main;
            if (cam) cam.transform.position = new Vector3(transform.position.x, transform.position.y, -10);
        }
    }
}
