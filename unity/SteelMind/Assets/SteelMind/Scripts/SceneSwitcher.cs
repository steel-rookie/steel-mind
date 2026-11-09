using UnityEngine;
using UnityEngine.SceneManagement;

namespace SteelMind
{
    /// <summary>화면 위 공정 버튼(제선·제강·연주·압연)과 숫자키 1~4로 공정 장면을 바꾼다.</summary>
    public class SceneSwitcher : MonoBehaviour
    {
        public static readonly string[] Scenes = { "Ironmaking", "Steelmaking", "Casting", "Rolling" };
        public static readonly string[] Labels = { "1 제선", "2 제강", "3 연주", "4 압연" };

        public void Load(int i)
        {
            if (i < 0 || i >= Scenes.Length || SceneManager.GetActiveScene().name == Scenes[i]) return;
            SceneFade.Go(Scenes[i], 0.4f, 0.6f);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k == null) return;
            if (k.digit1Key.wasPressedThisFrame) Load(0);
            if (k.digit2Key.wasPressedThisFrame) Load(1);
            if (k.digit3Key.wasPressedThisFrame) Load(2);
            if (k.digit4Key.wasPressedThisFrame) Load(3);
#endif
        }
    }
}
