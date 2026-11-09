using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SteelMind
{
    /// <summary>
    /// 압연 공정이 실제로 도는 것처럼 보여 준다 (반복).
    /// 가열로에서 슬래브가 나옴 → 스케일 제거기 물 분사 → 조압연기 왕복 압연(바로 늘어남)
    /// → 사상압연기 통과하며 긴 강판 → 런아웃 테이블 냉각수·김 → 권취기에서 코일로 감김.
    /// 이 오브젝트를 끄면(RollingLine.SetRunning(false)) 멈춘다.
    /// </summary>
    public class LineProcess : MonoBehaviour
    {
        const float PPU = 16f;
        [Header("그림")]
        public Sprite slab, bar, furnaceGlow;
        public Sprite[] strip = new Sprite[2], sprayShort = new Sprite[3], sprayLong = new Sprite[3], coil = new Sprite[4], steam = new Sprite[4];
        [Header("위치(px, 지도 왼쪽 위 기준)")]
        public float stripTop = 254, stripHeight = 17, furnaceExit = 438, coilRadius = 13;
        public Vector2 descaler, roughing, finishing, runout, coilCenter, sprayShortPos, sprayLongPos, furnaceGlowPos;
        [Header("흔들 설비")]
        public Transform roughingMill, finishingMill, coiler;
        [Header("속도")]
        public float speed = 1f;

        SpriteRenderer piece, stripSr, sprayS, sprayL, coilSr, glow;
        readonly List<SpriteRenderer> puffs = new List<SpriteRenderer>();
        readonly Dictionary<Transform, Vector3> basePos = new Dictionary<Transform, Vector3>();
        float hotMin = -1, hotMax = -1;     // 김이 날 구간(px)
        bool shakeRough, shakeFinish, coiling;

        static Vector3 W(float px, float py) => new Vector3(px / PPU, -py / PPU, 0);

        SpriteRenderer Make(string n, Sprite s, int order)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.sortingOrder = order;
            var mat = GetComponentInParent<RollingLine>()?.GetComponentInChildren<SpriteRenderer>()?.sharedMaterial;
            if (mat) sr.sharedMaterial = mat;
            return sr;
        }

        void Awake()
        {
            piece = Make("Piece", slab, 300);
            stripSr = Make("Strip", strip[0], 300);
            sprayS = Make("SprayDescaler", sprayShort[0], 330); sprayS.transform.position = W(sprayShortPos.x, sprayShortPos.y);
            sprayL = Make("SprayRunout", sprayLong[0], 330); sprayL.transform.position = W(sprayLongPos.x, sprayLongPos.y);
            coilSr = Make("CoilSpin", coil[0], 392); coilSr.transform.position = W(coilCenter.x - coilRadius - 1, coilCenter.y - coilRadius - 1);
            glow = Make("FurnaceGlow", furnaceGlow, 348); glow.transform.position = W(furnaceGlowPos.x, furnaceGlowPos.y);
            for (int i = 0; i < 10; i++) { var p = Make("Steam", steam[0], 600); p.enabled = false; puffs.Add(p); }
            foreach (var t in new[] { roughingMill, finishingMill, coiler })
                if (t) basePos[t] = t.position;
        }

        void OnEnable()
        {
            StartCoroutine(Loop());
            StartCoroutine(Ambient());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (var kv in basePos) if (kv.Key) kv.Key.position = kv.Value;
            if (piece) { piece.enabled = stripSr.enabled = sprayS.enabled = sprayL.enabled = coilSr.enabled = false; }
            foreach (var p in puffs) if (p) p.enabled = false;
        }

        void SetPiece(Sprite s, float x)
        {
            piece.sprite = s;
            float h = s.rect.height;
            piece.transform.position = W(x, stripTop + stripHeight - h + 1);
        }

        IEnumerator MovePiece(float from, float to, float pxPerSec)
        {
            float w = piece.sprite.rect.width;
            float x = from;
            while (!Mathf.Approximately(x, to))
            {
                x = Mathf.MoveTowards(x, to, pxPerSec * speed * Time.deltaTime);
                SetPiece(piece.sprite, Mathf.Round(x));
                sprayS.enabled = x + w > descaler.x && x < descaler.y;
                hotMin = x; hotMax = x + w;
                yield return null;
            }
        }

        void SetStrip(float tail, float head)
        {
            stripSr.enabled = head > tail;
            stripSr.transform.position = W(Mathf.Round(tail), stripTop);
            stripSr.transform.localScale = new Vector3(Mathf.Max(head - tail, 0), 1, 1);
            sprayL.enabled = head > runout.x && tail < runout.y;
            shakeFinish = head > finishing.x && tail < finishing.y;
            hotMin = tail; hotMax = head;
        }

        IEnumerator Loop()
        {
            while (true)
            {
                stripSr.enabled = false; coilSr.enabled = false; piece.enabled = true;
                // 1) 가열로 → 스케일 제거기
                SetPiece(slab, furnaceExit - 40);
                yield return MovePiece(furnaceExit - 40, roughing.x - 30, 60);
                // 2) 조압연기 왕복 (가역식)
                shakeRough = true;
                yield return MovePiece(roughing.x - 30, roughing.y - 50, 90);
                piece.sprite = bar;
                yield return MovePiece(roughing.y - 50, roughing.x - 10, 90);
                yield return MovePiece(roughing.x - 10, roughing.y - 40, 110);
                shakeRough = false;
                sprayS.enabled = false;
                yield return MovePiece(roughing.y - 40, finishing.x - 60, 80);
                // 3) 사상압연 → 긴 강판, 런아웃 냉각, 권취
                piece.enabled = false;
                float tail = finishing.x - 60, head = finishing.x;
                while (head < coilCenter.x)
                {
                    head = Mathf.Min(head + 200 * speed * Time.deltaTime, coilCenter.x);
                    tail += 20 * speed * Time.deltaTime;
                    SetStrip(tail, head);
                    yield return null;
                }
                coilSr.enabled = true; coiling = true;
                while (tail < coilCenter.x - 2)
                {
                    tail = Mathf.Min(tail + 170 * speed * Time.deltaTime, coilCenter.x);
                    SetStrip(tail, coilCenter.x);
                    yield return null;
                }
                SetStrip(0, 0); stripSr.enabled = false; sprayL.enabled = false; shakeFinish = false;
                hotMin = hotMax = -1;
                yield return new WaitForSeconds(1.2f / speed);
                coiling = false; coilSr.enabled = false;
                yield return new WaitForSeconds(0.8f / speed);
            }
        }

        IEnumerator Ambient()
        {
            float steamClock = 0;
            while (true)
            {
                float t = Time.time;
                int f3 = Mathf.FloorToInt(t * 12) % 3;
                sprayS.sprite = sprayShort[f3]; sprayL.sprite = sprayLong[f3];
                stripSr.sprite = strip[Mathf.FloorToInt(t * 6) % 2];
                if (coiling) coilSr.sprite = coil[Mathf.FloorToInt(t * 14) % 4];
                glow.color = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin(t * 2.2f));
                Shake(roughingMill, shakeRough, t); Shake(finishingMill, shakeFinish, t); Shake(coiler, coiling, t);
                steamClock += Time.deltaTime;
                if (hotMax > hotMin && steamClock > 0.18f)
                {
                    steamClock = 0;
                    var p = puffs.Find(x => !x.enabled);
                    if (p) StartCoroutine(Puff(p, Random.Range(hotMin, hotMax), stripTop - 8));
                }
                yield return null;
            }
        }

        void Shake(Transform tr, bool on, float t)
        {
            if (!tr || !basePos.ContainsKey(tr)) return;
            float dx = on && Mathf.FloorToInt(t * 20) % 2 == 0 ? 1f / PPU : 0f;
            tr.position = basePos[tr] + new Vector3(dx, 0, 0);
        }

        IEnumerator Puff(SpriteRenderer p, float x, float y)
        {
            p.enabled = true;
            for (int i = 0; i < 4; i++)
            {
                p.sprite = steam[i];
                p.transform.position = W(Mathf.Round(x - 7 + i), Mathf.Round(y - 7 - i * 4));
                yield return new WaitForSeconds(0.22f / speed);
            }
            p.enabled = false;
        }
    }
}
