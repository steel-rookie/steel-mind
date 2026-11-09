using System.IO;
using UnityEditor;

namespace SteelMind.EditorTools
{
    /// <summary>layout.json이 장면보다 새로우면 Unity가 스크립트를 다시 읽을 때 장면을 자동으로 다시 만든다.</summary>
    [InitializeOnLoad]
    static class RollingMapAutoBuild
    {
        static RollingMapAutoBuild() => EditorApplication.delayCall += Check;

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            const string layout = "Assets/SteelMind/Art/Rolling/layout.json";
            const string scene = "Assets/SteelMind/Scenes/Rolling.unity";
            if (!File.Exists(layout)) return;
            if (File.Exists(scene) && File.GetLastWriteTimeUtc(scene) >= File.GetLastWriteTimeUtc(layout)) return;
            UnityEngine.Debug.Log("[RollingMap] 그림이 바뀌어 장면을 다시 만듭니다.");
            RollingMapBuilder.Build();
        }
    }
}
