using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>
    /// 장면을 부드럽게 바꾼다: 서서히 어두워짐 → 뒤에서 불러오기 → 다 불러오면 서서히 밝아짐.
    /// 장면이 바뀌어도 남는 오브젝트라 화면이 뚝 끊기지 않는다.
    /// </summary>
    public class SceneFade : MonoBehaviour
    {
        Image black;
        bool busy;

        public static void Go(string scene, float fadeOut = 0.6f, float fadeIn = 1.0f)
        {
            var f = FindFirstObjectByType<SceneFade>();
            if (!f) { f = new GameObject("SceneFade").AddComponent<SceneFade>(); DontDestroyOnLoad(f.gameObject); }
            if (!f.busy) f.StartCoroutine(f.Run(scene, fadeOut, fadeIn));
        }

        void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var go = new GameObject("Black", typeof(RectTransform)); go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            black = go.AddComponent<Image>(); black.color = new Color(0, 0, 0, 0); black.raycastTarget = false;
        }

        IEnumerator Run(string scene, float fadeOut, float fadeIn)
        {
            busy = true;
            black.raycastTarget = true;
            Travel.from = SceneManager.GetActiveScene().name;
            yield return Fade(0, 1, fadeOut);
            var op = SceneManager.LoadSceneAsync(scene);
            while (!op.isDone) yield return null;
            yield return null;                       // 새 장면 첫 화면이 준비될 때까지 한 번 더
            yield return Fade(1, 0, fadeIn);
            black.raycastTarget = false;
            busy = false;
        }

        IEnumerator Fade(float a, float b, float dur)
        {
            float t = 0;
            while (t < dur) { t += Time.unscaledDeltaTime; black.color = new Color(0, 0, 0, Mathf.Lerp(a, b, Mathf.SmoothStep(0, 1, t / dur))); yield return null; }
            black.color = new Color(0, 0, 0, b);
        }
    }
}
