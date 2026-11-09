using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SteelMind
{
    /// <summary>
    /// 공정 연출을 layout.json의 effects 목록대로 돌린다 (제선·제강·연주).
    /// pulse: 불빛 깜빡임 / frames: 불꽃·물 분사 프레임 / steam: 김 / mover: 차량·크레인 왕복
    /// spawner: 컨베이어 원료·잘린 슬래브가 길을 따라 이동 / shake: 설비 진동
    /// </summary>
    public class ProcessFx : MonoBehaviour
    {
        [System.Serializable]
        public class Fx
        {
            public string type, target;
            public Sprite[] sprites;
            public float[] path;
            public float x, y, x0, x1, speed = 1, fps = 10, interval = 2, pause = 1, rate = 0.3f, period = 0.1f;
            public int order;
            public bool pingpong;
        }

        const float PPU = 16f;
        public List<Fx> effects = new List<Fx>();
        public Sprite[] steam = new Sprite[4];
        public Transform objects;
        Material mat;

        static Vector3 W(float px, float py) => new Vector3(Mathf.Round(px) / PPU, -Mathf.Round(py) / PPU, 0);

        SpriteRenderer Make(string n, Sprite s, int order)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.sortingOrder = order;
            if (mat) sr.sharedMaterial = mat;
            return sr;
        }

        void OnEnable()
        {
            var any = GetComponentInParent<RollingLine>()?.GetComponentInChildren<SpriteRenderer>();
            if (any) mat = any.sharedMaterial;
            foreach (var f in effects)
            {
                switch (f.type)
                {
                    case "pulse": StartCoroutine(Pulse(f)); break;
                    case "frames": StartCoroutine(Frames(f)); break;
                    case "steam": StartCoroutine(Steam(f)); break;
                    case "mover": StartCoroutine(Mover(f)); break;
                    case "spawner": StartCoroutine(Spawner(f)); break;
                    case "shake": StartCoroutine(Shake(f)); break;
                }
            }
        }

        void OnDisable() => StopAllCoroutines();

        IEnumerator Pulse(Fx f)
        {
            var sr = Make("Pulse", f.sprites[0], f.order);
            sr.transform.position = W(f.x, f.y);
            float ph = Random.value * 6f;
            while (true)
            {
                sr.color = new Color(1, 1, 1, 0.5f + 0.5f * Mathf.Sin(Time.time * f.speed + ph));
                yield return null;
            }
        }

        IEnumerator Frames(Fx f)
        {
            var sr = Make("Frames", f.sprites[0], f.order);
            sr.transform.position = W(f.x, f.y);
            int i = 0;
            while (true)
            {
                sr.sprite = f.sprites[i++ % f.sprites.Length];
                yield return new WaitForSeconds(1f / Mathf.Max(f.fps, 1));
            }
        }

        IEnumerator Steam(Fx f)
        {
            while (true)
            {
                StartCoroutine(Puff(Random.Range(f.x0, f.x1), f.y, f.order));
                yield return new WaitForSeconds(f.rate);
            }
        }

        IEnumerator Puff(float x, float y, int order)
        {
            var sr = Make("Steam", steam[0], order);
            for (int i = 0; i < steam.Length; i++)
            {
                sr.sprite = steam[i];
                sr.transform.position = W(x - 7 + i, y - 7 - i * 5);
                yield return new WaitForSeconds(0.25f);
            }
            Destroy(sr.gameObject);
        }

        Transform Find(string id) => objects ? objects.Find(id) : null;

        IEnumerator Mover(Fx f)
        {
            var t = Find(f.target);
            if (!t || f.path == null || f.path.Length < 4) yield break;
            var a = W(f.path[0], f.path[1]); var b = W(f.path[2], f.path[3]);
            var offset = t.position - a;
            while (true)
            {
                yield return MoveTo(t, a + offset, b + offset, f.speed);
                yield return new WaitForSeconds(f.pause);
                if (!f.pingpong) { t.position = a + offset; continue; }
                yield return MoveTo(t, b + offset, a + offset, f.speed);
                yield return new WaitForSeconds(f.pause);
            }
        }

        IEnumerator MoveTo(Transform t, Vector3 from, Vector3 to, float pxPerSec)
        {
            float d = Vector3.Distance(from, to) * PPU, time = 0, dur = d / Mathf.Max(pxPerSec, 1);
            while (time < dur)
            {
                time += Time.deltaTime;
                var p = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, time / dur));
                t.position = new Vector3(Mathf.Round(p.x * PPU) / PPU, Mathf.Round(p.y * PPU) / PPU, p.z);
                yield return null;
            }
        }

        IEnumerator Spawner(Fx f)
        {
            while (true)
            {
                StartCoroutine(Travel(f));
                yield return new WaitForSeconds(f.interval);
            }
        }

        IEnumerator Travel(Fx f)
        {
            var sr = Make("Item", f.sprites[0], f.order);
            for (int i = 0; i + 3 < f.path.Length; i += 2)
            {
                Vector2 a = new Vector2(f.path[i], f.path[i + 1]), b = new Vector2(f.path[i + 2], f.path[i + 3]);
                float d = Vector2.Distance(a, b), time = 0, dur = d / Mathf.Max(f.speed, 1);
                while (time < dur)
                {
                    time += Time.deltaTime;
                    var p = Vector2.Lerp(a, b, time / dur);
                    sr.transform.position = W(p.x, p.y);
                    yield return null;
                }
            }
            Destroy(sr.gameObject);
        }

        IEnumerator Shake(Fx f)
        {
            var t = Find(f.target);
            if (!t) yield break;
            var basePos = t.position;
            while (true)
            {
                t.position = basePos + new Vector3(Mathf.FloorToInt(Time.time / f.period) % 2 == 0 ? 1f / PPU : 0, 0, 0);
                yield return null;
            }
        }
    }
}
