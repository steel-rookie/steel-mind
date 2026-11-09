using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SteelMind.EditorTools
{
    /// <summary>
    /// Art/&lt;공정&gt;/layout.json마다 장면(Scenes/&lt;공정&gt;.unity)을 만든다. 제선·제강·연주·압연.
    /// 메뉴: STEEL MIND > 공정 맵 전부 만들기. 그림이 바뀌면 RollingMapAutoBuild가 자동으로 다시 만든다.
    /// </summary>
    public static class RollingMapBuilder
    {
        public const string ArtRoot = "Assets/SteelMind/Art";
        public const string SceneDir = "Assets/SteelMind/Scenes";
        const string Common = ArtRoot + "/Common";
        const string BgmPath = "Assets/SteelMind/Audio/bgm_rolling_mill.wav";
        const float PPU = 16f;
        const int ViewW = 480, ViewH = 270;   // 카메라에 보이는 화면(px). 클수록 넓게 보인다
        static string dir;

        [Serializable] class MapInfo { public int width, height, wallHeight; public string title; }
        [Serializable] class LayerInfo { public string id, file, toggle; public int order; }
        [Serializable] class ObjInfo { public string id, label, file, warnFile, toggle; public int[] rect; public int[] collider; public int sortY; public bool equipment; }
        [Serializable] class ColInfo { public string id; public int[] rect; }
        [Serializable] class FxInfo { public int stripTop, stripHeight, furnaceExit, coilRadius; public int[] furnaceGlow, descaler, roughing, finishing, runout, coil, sprayShort, sprayLong; }
        [Serializable] class EffInfo { public string type, target, file; public string[] files; public float[] path; public float x, y, x0, x1, speed, fps, interval, pause, rate, period; public int order; public bool pingpong; }
        [Serializable] class PortalInfo { public string scene, label; public int[] rect; }
        [Serializable] class SpawnInfo { public string scene; public float[] pos; }
        [Serializable] class Layout { public PortalInfo[] portals; public SpawnInfo[] spawns; public string scene; public MapInfo map; public LayerInfo[] layers; public ObjInfo[] objects; public ColInfo[] colliders; public int[] spawn; public FxInfo fx; public EffInfo[] effects; }

        public static IEnumerable<string> ProcessDirs()
        {
            foreach (var d in Directory.GetDirectories(ArtRoot))
                if (File.Exists(Path.Combine(d, "layout.json"))) yield return d.Replace('\\', '/');
        }

        public static string SceneFor(string artDir)
        {
            var L = JsonUtility.FromJson<Layout>(File.ReadAllText(Path.Combine(artDir, "layout.json")));
            return $"{SceneDir}/{(string.IsNullOrEmpty(L.scene) ? Path.GetFileName(artDir) : L.scene)}.unity";
        }

        [MenuItem("STEEL MIND/공정 맵 전부 만들기")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            ConfigureSprites(Common);
            foreach (var d in ProcessDirs()) BuildOne(d);
            BuildTitleScene();
            var order = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(TitleScene, true) };
            foreach (var n in SceneSwitcher.Scenes)
            {
                var p = $"{SceneDir}/{n}.unity";
                if (File.Exists(p)) order.Add(new EditorBuildSettingsScene(p, true));
            }
            EditorBuildSettings.scenes = order.ToArray();
            var rolling = TitleScene;
            if (File.Exists(rolling)) EditorSceneManager.OpenScene(rolling);
        }

        public static void BuildFromCommandLine() => Build();

        public const string TitleScene = SceneDir + "/Title.unity";

        // 메인 화면: 카메라와 TitleScreen만 있는 장면 (화면은 실행할 때 코드로 만든다)
        public static void BuildTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            new GameObject("Title").AddComponent<TitleScreen>();
            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, TitleScene);
            Debug.Log($"[RollingMap] 메인 화면 장면을 만들었습니다: {TitleScene}");
        }

        public static void BuildOne(string artDir)
        {
            dir = artDir;
            var L = JsonUtility.FromJson<Layout>(File.ReadAllText($"{dir}/layout.json"));
            ConfigureSprites(dir);
            var scenePath = SceneFor(dir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var unlit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

            var root = new GameObject("Map");
            var line = root.AddComponent<RollingLine>();
            foreach (var layer in L.layers)
            {
                var go = MakeSprite(layer.id, layer.file, Vector2.zero, layer.order, root.transform, unlit);
                if (layer.toggle == "line_running") go.SetActive(false);
            }
            var objects = new GameObject("Objects").transform;
            objects.SetParent(root.transform, false);
            foreach (var o in L.objects)
            {
                var go = MakeSprite(o.id, o.file, new Vector2(o.rect[0], o.rect[1]), o.sortY, objects, unlit);
                if (o.collider != null && o.collider.Length == 4) AddBox(go, o.collider);
                if (o.toggle == "line_running") go.SetActive(false);
                if (o.equipment)
                    line.equipment.Add(new RollingLine.Equipment { id = o.id, label = o.label, renderer = go.GetComponent<SpriteRenderer>(), normal = LoadSprite(o.file), warn = LoadSprite(o.warnFile) });
            }
            if (L.fx != null && L.fx.descaler != null && L.fx.descaler.Length == 2)
                line.runningOnly.Add(MakeRollingProcess(L.fx, root.transform, objects));
            if (L.effects != null && L.effects.Length > 0)
                line.runningOnly.Add(MakeEffects(L.effects, root.transform, objects));

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            foreach (var c in L.colliders) { var go = new GameObject(c.id); go.transform.SetParent(walls, false); AddBox(go, c.rect); }

            if (L.portals != null)
                foreach (var pt in L.portals)
                {
                    var go = new GameObject("Portal_" + pt.scene);
                    go.transform.SetParent(walls, false);
                    AddBox(go, pt.rect);
                    go.GetComponent<BoxCollider2D>().isTrigger = true;
                    var portal = go.AddComponent<Portal>(); portal.scene = pt.scene; portal.label = pt.label;
                }

            var player = MakePlayer(L, unlit);
            if (L.spawns != null && L.spawns.Length > 0)
            {
                var ws = player.AddComponent<WorldSpawn>();
                ws.scenes = new string[L.spawns.Length]; ws.points = new Vector2[L.spawns.Length];
                for (int i = 0; i < L.spawns.Length; i++) { ws.scenes[i] = L.spawns[i].scene; ws.points[i] = new Vector2(L.spawns[i].pos[0], L.spawns[i].pos[1]); }
            }
            var cam = MakeCamera(L, player.transform);
            MakeUI(L, cam, player);

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[RollingMap] 장면을 만들었습니다: {scenePath} (물체 {L.objects.Length}개)");
        }

        static void ConfigureSprites(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool player = Path.GetFileName(path).StartsWith("player_");
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                var align = (int)(player ? SpriteAlignment.BottomCenter : SpriteAlignment.TopLeft);
                if (ti.textureType == TextureImporterType.Sprite && ti.filterMode == FilterMode.Point && s.spriteAlignment == align && ti.spritePixelsPerUnit == PPU) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PPU;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.maxTextureSize = 4096;
                ti.ReadTextureSettings(s);
                s.spriteMeshType = SpriteMeshType.FullRect;
                s.spriteAlignment = align;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
            }
        }

        static Sprite LoadSprite(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/{file}") ?? AssetDatabase.LoadAssetAtPath<Sprite>($"{Common}/{file}");
        }

        static Vector3 ToWorld(float px, float py) => new Vector3(px / PPU, -py / PPU, 0);

        static GameObject MakeSprite(string name, string file, Vector2 px, int order, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = ToWorld(px.x, px.y);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite(file);
            sr.sortingOrder = order;
            if (mat) sr.sharedMaterial = mat;
            if (!sr.sprite) Debug.LogWarning($"[RollingMap] 그림을 찾지 못했습니다: {dir}/{file}");
            return go;
        }

        static void AddBox(GameObject go, int[] r)
        {
            var box = go.AddComponent<BoxCollider2D>();
            Vector3 center = ToWorld(r[0] + r[2] / 2f, r[1] + r[3] / 2f);
            box.offset = center - go.transform.position;
            box.size = new Vector2(r[2] / PPU, r[3] / PPU);
        }

        static GameObject MakeRollingProcess(FxInfo f, Transform root, Transform objects)
        {
            var go = new GameObject("Process");
            go.transform.SetParent(root, false);
            var p = go.AddComponent<LineProcess>();
            p.slab = LoadSprite("fx_slab.png"); p.bar = LoadSprite("fx_bar.png"); p.furnaceGlow = LoadSprite("fx_furnace_glow.png");
            for (int i = 0; i < 2; i++) p.strip[i] = LoadSprite($"fx_strip_{i}.png");
            for (int i = 0; i < 3; i++) { p.sprayShort[i] = LoadSprite($"fx_spray_short_{i}.png"); p.sprayLong[i] = LoadSprite($"fx_spray_long_{i}.png"); }
            for (int i = 0; i < 4; i++) { p.coil[i] = LoadSprite($"fx_coil_{i}.png"); p.steam[i] = LoadSprite($"fx_steam_{i}.png"); }
            p.stripTop = f.stripTop; p.stripHeight = f.stripHeight; p.furnaceExit = f.furnaceExit; p.coilRadius = f.coilRadius;
            Vector2 V(int[] a) => new Vector2(a[0], a[1]);
            p.descaler = V(f.descaler); p.roughing = V(f.roughing); p.finishing = V(f.finishing); p.runout = V(f.runout);
            p.coilCenter = V(f.coil); p.sprayShortPos = V(f.sprayShort); p.sprayLongPos = V(f.sprayLong); p.furnaceGlowPos = V(f.furnaceGlow);
            p.roughingMill = objects.Find("roughing_mill"); p.finishingMill = objects.Find("finishing_mill"); p.coiler = objects.Find("coiler");
            return go;
        }

        static GameObject MakeEffects(EffInfo[] effects, Transform root, Transform objects)
        {
            var go = new GameObject("Process");
            go.transform.SetParent(root, false);
            var p = go.AddComponent<ProcessFx>();
            p.objects = objects;
            for (int i = 0; i < 4; i++) p.steam[i] = LoadSprite($"fx_steam_{i}.png");
            foreach (var e in effects)
            {
                var files = e.files != null && e.files.Length > 0 ? e.files : new[] { e.file };
                var sprites = new List<Sprite>();
                foreach (var f in files) { var s = LoadSprite(f); if (s) sprites.Add(s); }
                p.effects.Add(new ProcessFx.Fx
                {
                    type = e.type, target = e.target, sprites = sprites.ToArray(), path = e.path, x = e.x, y = e.y, x0 = e.x0, x1 = e.x1,
                    speed = e.speed, fps = e.fps, interval = e.interval, pause = e.pause, rate = e.rate, period = e.period, order = e.order, pingpong = e.pingpong,
                });
            }
            return go;
        }

        static GameObject MakePlayer(Layout L, Material mat)
        {
            var go = new GameObject("Player");
            go.transform.position = ToWorld(L.spawn[0], L.spawn[1]);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite("player_down_0.png");
            if (mat) sr.sharedMaterial = mat;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0; body.freezeRotation = true;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(12 / PPU, 6 / PPU); box.offset = new Vector2(0, 3 / PPU);
            go.AddComponent<PlayerMove>();
            go.AddComponent<YSort>();
            var anim = go.AddComponent<PlayerAnimator>();
            for (int i = 0; i < 4; i++)
            {
                anim.down[i] = LoadSprite($"player_down_{i}.png"); anim.left[i] = LoadSprite($"player_left_{i}.png");
                anim.right[i] = LoadSprite($"player_right_{i}.png"); anim.up[i] = LoadSprite($"player_up_{i}.png");
            }
            return go;
        }

        static Camera MakeCamera(Layout L, Transform target)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = ViewH / 2f / PPU;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(11, 10, 14, 255);
            go.transform.position = target.position + new Vector3(0, 0, -10);
            var follow = go.AddComponent<CameraFollow>();
            follow.target = target;
            follow.mapSizePx = new Vector2(L.map.width, L.map.height);
            go.AddComponent<AudioListener>();
            var bgm = AssetDatabase.LoadAssetAtPath<AudioClip>(BgmPath);
            if (bgm) { var src = go.AddComponent<AudioSource>(); src.clip = bgm; src.loop = true; src.playOnAwake = true; src.volume = 0.5f; }
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((t = asm.GetType("UnityEngine.Rendering.Universal.PixelPerfectCamera")) != null) break;
            if (t != null)
            {
                var ppc = go.AddComponent(t);
                Set(ppc, "assetsPPU", (int)PPU); Set(ppc, "refResolutionX", ViewW); Set(ppc, "refResolutionY", ViewH);
            }
            return cam;
        }

        static void Set(object o, string prop, object v)
        {
            var p = o.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(o, v);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            return rt;
        }

        static Button MakeButton(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 pos, string label, Color32 color, out Text text)
        {
            var rt = Rect(name, parent, anchor, size, pos);
            rt.gameObject.AddComponent<Image>().color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            var tr = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            tr.SetParent(rt, false); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            text = tr.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(20, 16, 24, 255); text.text = label;
            return btn;
        }

        static void MakeUI(Layout L, Camera cam, GameObject player)
        {
            var mapSize = new Vector2(L.map.width, L.map.height);
            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 미니맵 (오른쪽 위)
            var mmSprite = LoadSprite("ui_minimap.png");
            if (mmSprite)
            {
                float mmW = 360f, mmH = mmW * mmSprite.rect.height / mmSprite.rect.width;
                var mm = Rect("Minimap", canvasGo.transform, new Vector2(1, 1), new Vector2(mmW, mmH), new Vector2(-16, -16));
                mm.gameObject.AddComponent<Image>().sprite = mmSprite;
                var mk = Rect("Marker", mm, new Vector2(0, 1), new Vector2(10, 10), Vector2.zero);
                mk.pivot = new Vector2(0.5f, 0.5f);
                mk.gameObject.AddComponent<Image>().sprite = LoadSprite("ui_marker.png");
                var minimap = canvasGo.AddComponent<Minimap>();
                minimap.player = player.transform; minimap.map = mm; minimap.marker = mk; minimap.mapSizePx = mapSize;
            }

            // 버튼은 두지 않고 단축키만: Tab 전체 공정 보기, 1~4 공정 이동, M 미니맵, H UI 숨기기
            var ov = cam.gameObject.AddComponent<OverviewCamera>();
            ov.cam = cam; ov.follow = cam.GetComponent<CameraFollow>(); ov.playerMove = player.GetComponent<PlayerMove>(); ov.mapSizePx = mapSize;
            foreach (var bh in cam.GetComponents<Behaviour>()) if (bh.GetType().Name == "PixelPerfectCamera") ov.pixelPerfect = bh;
            canvasGo.AddComponent<SceneSwitcher>();

            // H 키로 미니맵·전체 공정 보기·공정 버튼을 숨기기/보이기 (버튼 없음)
            var toggle = canvasGo.AddComponent<UiToggle>();
            foreach (Transform child in canvasGo.transform) toggle.targets.Add(child.gameObject);

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            Type mod = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((mod = asm.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule")) != null) break;
            if (mod != null) es.AddComponent(mod); else es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}
