using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SteelMind.EditorTools
{
    /// <summary>
    /// Art/Rolling/layout.json을 읽어 압연 맵 장면(Scenes/Rolling.unity)을 만든다.
    /// 메뉴: STEEL MIND > 압연 맵 만들기. 그림을 다시 뽑았으면 이 메뉴를 다시 누르면 된다.
    /// </summary>
    public static class RollingMapBuilder
    {
        const string ArtDir = "Assets/SteelMind/Art/Rolling";
        const string ScenePath = "Assets/SteelMind/Scenes/Rolling.unity";
        const float PPU = 16f;
        const string PlayerPrefix = "player_";
        const string BgmPath = "Assets/SteelMind/Audio/bgm_rolling_mill.wav";
        // 카메라에 보이는 화면 크기(px). 작을수록 캐릭터가 크게 보인다
        const int ViewW = 320, ViewH = 180;

        [Serializable] class MapInfo { public int width, height, tile, pixelsPerUnit, wallHeight; }
        [Serializable] class LayerInfo { public string id, file, toggle; public int order; }
        [Serializable] class ObjInfo { public string id, label, file, warnFile, toggle; public int[] rect; public int[] collider; public int sortY; public bool equipment; }
        [Serializable] class ColInfo { public string id; public int[] rect; }
        [Serializable] class FxInfo { public int stripTop, stripHeight, furnaceExit, coilRadius; public int[] furnaceGlow, descaler, roughing, finishing, runout, coil, sprayShort, sprayLong; }
        [Serializable] class Layout { public MapInfo map; public LayerInfo[] layers; public ObjInfo[] objects; public ColInfo[] colliders; public int[] spawn; public FxInfo fx; }

        [MenuItem("STEEL MIND/압연 맵 만들기")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{ArtDir}/layout.json");
            if (!json) { Debug.LogError($"[RollingMap] {ArtDir}/layout.json 이 없습니다."); return; }
            var L = JsonUtility.FromJson<Layout>(json.text);
            ConfigureSprites();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var unlit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

            var root = new GameObject("RollingMap");
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
                var pos = new Vector2(o.rect[0], o.rect[1]);
                var go = MakeSprite(o.id, o.file, pos, o.sortY, objects, unlit);
                if (o.collider != null && o.collider.Length == 4) AddBox(go, o.collider);
                if (o.toggle == "line_running") go.SetActive(false);
                if (o.equipment)
                    line.equipment.Add(new RollingLine.Equipment
                    {
                        id = o.id, label = o.label, renderer = go.GetComponent<SpriteRenderer>(),
                        normal = LoadSprite(o.file), warn = LoadSprite(o.warnFile),
                    });
            }

            if (L.fx != null && L.fx.descaler != null && L.fx.descaler.Length == 2)
                line.runningOnly.Add(MakeProcess(L.fx, root.transform, objects));

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            foreach (var c in L.colliders)
            {
                var go = new GameObject(c.id);
                go.transform.SetParent(walls, false);
                AddBox(go, c.rect);
            }

            var player = MakePlayer(L, unlit);
            var cam = MakeCamera(L, player.transform);
            MakeUI(L, cam, player);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"[RollingMap] 장면을 만들었습니다: {ScenePath} (물체 {L.objects.Length}개)");
        }

        // 배치 모드용: Unity -batchmode -executeMethod SteelMind.EditorTools.RollingMapBuilder.BuildFromCommandLine -quit
        public static void BuildFromCommandLine() => Build();

        static void ConfigureSprites()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PPU;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.maxTextureSize = 4096;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteMeshType = SpriteMeshType.FullRect;
                s.spriteAlignment = (int)(Path.GetFileName(path).StartsWith(PlayerPrefix) ? SpriteAlignment.BottomCenter : SpriteAlignment.TopLeft);
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
            }
        }

        static Sprite LoadSprite(string file) =>
            string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{file}");

        // 지도 px(왼쪽 위 기준, 아래로 +) → world (아래로 -)
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
            if (!sr.sprite) Debug.LogWarning($"[RollingMap] 그림을 찾지 못했습니다: {file}");
            return go;
        }

        static void AddBox(GameObject go, int[] r)
        {
            var box = go.AddComponent<BoxCollider2D>();
            Vector3 center = ToWorld(r[0] + r[2] / 2f, r[1] + r[3] / 2f);
            box.offset = center - go.transform.position;
            box.size = new Vector2(r[2] / PPU, r[3] / PPU);
        }

        static GameObject MakeProcess(FxInfo f, Transform root, Transform objects)
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

        static GameObject MakePlayer(Layout L, Material mat)
        {
            var go = new GameObject("Player");
            go.transform.position = ToWorld(L.spawn[0], L.spawn[1]);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite("player_down_0.png");
            if (mat) sr.sharedMaterial = mat;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            body.freezeRotation = true;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(12 / PPU, 6 / PPU);
            box.offset = new Vector2(0, 3 / PPU);
            go.AddComponent<PlayerMove>();
            go.AddComponent<YSort>();
            var anim = go.AddComponent<PlayerAnimator>();
            for (int i = 0; i < 4; i++)
            {
                anim.down[i] = LoadSprite($"player_down_{i}.png");
                anim.left[i] = LoadSprite($"player_left_{i}.png");
                anim.right[i] = LoadSprite($"player_right_{i}.png");
                anim.up[i] = LoadSprite($"player_up_{i}.png");
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
            if (bgm)
            {
                var src = go.AddComponent<AudioSource>();
                src.clip = bgm; src.loop = true; src.playOnAwake = true; src.volume = 0.5f;
            }

            // URP Pixel Perfect Camera (ViewW x ViewH, 16 PPU). 패키지가 없으면 건너뛴다.
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((t = asm.GetType("UnityEngine.Rendering.Universal.PixelPerfectCamera")) != null) break;
            if (t != null)
            {
                var ppc = go.AddComponent(t);
                Set(ppc, "assetsPPU", (int)PPU);
                Set(ppc, "refResolutionX", ViewW);
                Set(ppc, "refResolutionY", ViewH);
            }
            else Debug.LogWarning("[RollingMap] Pixel Perfect Camera를 찾지 못했습니다. 카메라에 직접 붙여 주세요.");
            return cam;
        }

        static void MakeUI(Layout L, Camera cam, GameObject player)
        {
            var mapSize = new Vector2(L.map.width, L.map.height);
            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // 미니맵 (오른쪽 위)
            var mmSprite = LoadSprite("ui_minimap.png");
            var mm = new GameObject("Minimap", typeof(RectTransform)).GetComponent<RectTransform>();
            mm.SetParent(canvasGo.transform, false);
            mm.anchorMin = mm.anchorMax = mm.pivot = new Vector2(1, 1);
            float mmW = 400f, mmH = mmW * mmSprite.rect.height / mmSprite.rect.width;
            mm.sizeDelta = new Vector2(mmW, mmH);
            mm.anchoredPosition = new Vector2(-16, -16);
            var mmImg = mm.gameObject.AddComponent<UnityEngine.UI.Image>(); mmImg.sprite = mmSprite;
            var mk = new GameObject("Marker", typeof(RectTransform)).GetComponent<RectTransform>();
            mk.SetParent(mm, false);
            mk.anchorMin = mk.anchorMax = new Vector2(0, 1); mk.pivot = new Vector2(0.5f, 0.5f);
            mk.sizeDelta = new Vector2(10, 10);
            mk.gameObject.AddComponent<UnityEngine.UI.Image>().sprite = LoadSprite("ui_marker.png");
            var minimap = canvasGo.AddComponent<Minimap>();
            minimap.player = player.transform; minimap.map = mm; minimap.marker = mk; minimap.mapSizePx = mapSize;

            // 전체 공정 보기 버튼 (왼쪽 위)
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var btnRt = new GameObject("OverviewButton", typeof(RectTransform)).GetComponent<RectTransform>();
            btnRt.SetParent(canvasGo.transform, false);
            btnRt.anchorMin = btnRt.anchorMax = btnRt.pivot = new Vector2(0, 1);
            btnRt.sizeDelta = new Vector2(260, 52);
            btnRt.anchoredPosition = new Vector2(16, -16);
            var bg = btnRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color32(232, 173, 34, 235);
            var btn = btnRt.gameObject.AddComponent<UnityEngine.UI.Button>();
            var txtRt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            txtRt.SetParent(btnRt, false);
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one; txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;
            var txt = txtRt.gameObject.AddComponent<UnityEngine.UI.Text>();
            txt.font = font; txt.fontSize = 24; txt.fontStyle = FontStyle.Bold; txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color32(20, 16, 24, 255); txt.text = "전체 공정 보기 (Tab)";

            var ov = cam.gameObject.AddComponent<OverviewCamera>();
            ov.cam = cam; ov.follow = cam.GetComponent<CameraFollow>(); ov.playerMove = player.GetComponent<PlayerMove>();
            ov.label = txt; ov.mapSizePx = mapSize;
            foreach (var b in cam.GetComponents<Behaviour>())
                if (b.GetType().Name == "PixelPerfectCamera") ov.pixelPerfect = b;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, ov.Toggle);

            // UI 클릭용 EventSystem (새 Input System 모듈)
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            Type mod = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((mod = asm.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule")) != null) break;
            if (mod != null) es.AddComponent(mod); else es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        static void Set(object o, string prop, object v)
        {
            var p = o.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(o, v);
        }

        static void AddToBuildSettings(string path)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
