using System.IO;
using UnityEditor;

namespace SteelMind.EditorTools
{
    /// <summary>공정 그림(layout.json)이 장면보다 새로우면 Unity가 스크립트를 다시 읽을 때 장면을 자동으로 다시 만든다.</summary>
    [InitializeOnLoad]
    static class RollingMapAutoBuild
    {
        static RollingMapAutoBuild() => EditorApplication.delayCall += Check;

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!Directory.Exists(RollingMapBuilder.ArtRoot)) return;
            foreach (var d in RollingMapBuilder.ProcessDirs())
            {
                var scene = RollingMapBuilder.SceneFor(d);
                if (!File.Exists(scene) || File.GetLastWriteTimeUtc(scene) < File.GetLastWriteTimeUtc(Path.Combine(d, "layout.json")))
                {
                    UnityEngine.Debug.Log("[RollingMap] 그림이 바뀌어 공정 장면을 다시 만듭니다.");
                    RollingMapBuilder.Build();
                    return;
                }
            }
        }
    }
}
