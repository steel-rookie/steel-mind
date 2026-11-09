using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SteelMind
{
    /// <summary>미니맵·전체 공정 보기·공정 버튼을 한 번에 숨기거나 보인다. 버튼 또는 H 키.</summary>
    public class UiToggle : MonoBehaviour
    {
        public List<GameObject> targets = new List<GameObject>();
        public Text label;
        public bool hidden;

        void Start() => Apply();

        public void Toggle() { hidden = !hidden; Apply(); }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.hKey.wasPressedThisFrame) Toggle();
#endif
        }

        void Apply()
        {
            foreach (var t in targets) if (t) t.SetActive(!hidden);
            if (label) label.text = hidden ? "UI 보이기 (H)" : "UI 숨기기 (H)";
        }
    }
}
