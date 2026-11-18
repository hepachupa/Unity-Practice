using System.Linq;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using LNStudy.Interaction;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 머핀 타임 (Muffin Time) — 2.5D 횡스크롤 협동 퍼즐. 사라진 머핀을 찾아 오른쪽으로 나아간다.
    /// 전투 없음. 고양이(무겁고 상자를 끄는 쪽) + 다람쥐(가볍고 작고 높이 오르는 쪽)를 Tab으로 바꿔 가며 진행.
    /// 두고 온 캐릭터는 그 자리에서 기다리고, F로 부르면 따라온다.
    ///
    /// 퍼즐 순서 (x 증가 방향)
    ///  1. 높은 턱(2m)      다람쥐는 바로 오르고, 고양이는 상자를 벽에 붙여 딛고 오른다
    ///  2. 무거운 문        고양이가 무거운 발판을 누르는 동안 문이 열림 → 다람쥐가 건너가 가벼운 스위치로 문 고정
    ///  3. 좁은 굴          다람쥐만 통과 → 반대편 스위치로 굴 위 벽을 들어 올림
    ///  4. 둘이 타는 승강기  둘 다 올라타야(무게 합) 올라간다
    ///  5. 도개교           상자를 무거운 발판에 올려 두면 다리가 내려온다
    ///  6. 오두막           둘 다 도착하면 엔딩: 큰 머핀을 나눠 먹고 오두막 문 안으로 들어간다
    /// 숨은 작은 머핀 3개 (다람쥐가 상자를 딛고 높이 뛰어야 닿는 곳 등).
    /// 손맛: 스쿼시·기울기·먼지·합성 효과음·화면 흔들림·조종 표시 (CharacterJuice, CrateJuice, PressurePlate/PuzzleMover 피드백).
    ///
    /// 메뉴
    ///  - Build Scene: 씬 전체를 새로 만든다 (손으로 바꾼 것은 사라짐)
    ///  - Apply Update (keep scene edits): 지금 씬에 톤·손맛·머핀·엔딩만 덧입힌다 (반복 실행 가능)
    /// </summary>
    public static class MuffinTimeBuilder
    {
        /// <summary>MuffinTime 씬 위치 (사용자가 폴더를 옮겨도 이름으로 찾는다)</summary>
        static string ScenePath
        {
            get
            {
                foreach (var guid in AssetDatabase.FindAssets("MuffinTime t:Scene"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith("/MuffinTime.unity")) return p;
                }
                return "Assets/_Project/Scenes/MuffinTime.unity";
            }
        }
        const string LegacyScenePath = "Assets/_Project/Scenes/CoopPuzzle_2_5D.unity";
        const string LegacyDataFolder = "Assets/_Project/CoopPuzzle";
        const string DataFolder = "Assets/_Project/MuffinTime";
        const string Kit = "Assets/LittleFriends-CartoonAnimals-Lite";
        const string LowPoly = "Assets/Skyden_Games/Low Poly Environment/Prefabs";
        const string CatControllerPath = "Assets/_Project/CatParty/LittleCatPlayer.controller";
        const string SquirrelControllerPath = DataFolder + "/LittleSquirrelPlayer.controller";

        const float KillY = -4f;
        const float FloorFront = -1.5f, FloorBack = 2.5f; // 바닥 단면 Z 범위 (캐릭터는 Z=0)

        static Material ground, grass, rock, concrete, metal, crateMat, glowWarm, abyss, windowGlow;
        static PhysicsMaterial crateSlide;
        static Material dustMat, markerMat, interiorMat;

        [MenuItem("LNStudy/Muffin Time/Build Scene")]
        public static void Build()
        {
            EditorSceneManager.SaveOpenScenes();
            MigrateLegacyPaths();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CombatArenaBuilder.EnsureFolder(DataFolder);
            int playerLayer = CombatArenaBuilder.EnsureLayer("Player");
            BuildMaterials();
            var catCtrl = CatPartyBuilder.BuildPlayerController(CatControllerPath, "Cat", 2.2f, 4.2f);
            var sqCtrl = CatPartyBuilder.BuildPlayerController(SquirrelControllerPath, "Squirrel", 2.6f, 5.0f);

            var world = new GameObject("World").transform;
            var puzzles = new GameObject("Puzzles").transform;
            BuildLighting();
            BuildLevel(world);
            BuildBackground(world);

            // ── 1. 높은 턱
            Crate(puzzles, "Crate_Ledge", new Vector3(13f, 0f, 0f));

            // ── 2. 무거운 문: 고양이가 발판을 누르는 동안 열림, 문 너머 스위치로 고정
            var gate = Door(puzzles, "HeavyGate", 34f, 2f, 3.4f, 3.3f);
            var hp1 = Plate(puzzles, "HeavyPlate_Gate", new Vector3(29f, 2f, 0f), true, false);
            UnityEventTools.AddPersistentListener(hp1.onPressed, gate.AddSignal);
            UnityEventTools.AddPersistentListener(hp1.onReleased, gate.RemoveSignal);
            var lp1 = Plate(puzzles, "LightSwitch_Gate", new Vector3(38.5f, 2f, 0f), false, true);
            UnityEventTools.AddPersistentListener(lp1.onPressed, gate.OpenForever);

            // ── 3. 좁은 굴: 다람쥐만 통과 → 반대편 스위치로 굴 위 벽이 올라감
            var tunnel = TunnelWall(puzzles, 48f, 2f);
            var lp2 = Plate(puzzles, "LightSwitch_Tunnel", new Vector3(53.5f, 2f, 0f), false, true);
            UnityEventTools.AddPersistentListener(lp2.onPressed, tunnel.OpenForever);

            // ── 4. 둘이 타는 승강기
            Elevator(puzzles, 74.75f, 2f, 3.5f);

            // ── 5. 도개교: 상자를 무거운 발판에 올려 둔다
            Crate(puzzles, "Crate_Bridge", new Vector3(82f, 5.5f, 0f));
            var bridge = Drawbridge(puzzles, 92f, 5.5f, 6.6f);
            var hp3 = Plate(puzzles, "HeavyPlate_Bridge", new Vector3(87.5f, 5.5f, 0f), true, false);
            UnityEventTools.AddPersistentListener(hp3.onPressed, bridge.AddSignal);
            UnityEventTools.AddPersistentListener(hp3.onReleased, bridge.RemoveSignal);


            // 체크포인트 (+ 처음 몇 개만 조작 힌트)
            Checkpoint(puzzles, 0f, 0f, "");
            Checkpoint(puzzles, 22.5f, 2f, "Tab: 캐릭터 교체 · 두고 온 쪽은 그 자리에서 기다린다");
            Checkpoint(puzzles, 41f, 2f, "F: 동료를 부르거나 기다리게 하기");
            Checkpoint(puzzles, 57f, 2f, "");
            Checkpoint(puzzles, 79f, 5.5f, "");
            Checkpoint(puzzles, 100.5f, 5.5f, "");

            // 캐릭터
            var chars = new GameObject("Characters").transform;
            var cat = BuildCharacter(chars, "Cat", $"{Kit}/Prefabs/LittleCat_Happy.prefab", catCtrl, new Vector3(1.5f, 0f, 0f),
                scale: 2.1f, height: 1.2f, radius: 0.32f, walk: 2.2f, run: 4.2f, jump: 0.7f, climb: 1.3f, crouch: 1.2f,
                weight: 10f, playerLayer, out var catModel);
            var squirrel = BuildCharacter(chars, "Squirrel", $"{Kit}/Prefabs/LittleSquirrel_Happy.prefab", sqCtrl, new Vector3(-0.2f, 0f, 0f),
                scale: 1.4f, height: 0.8f, radius: 0.24f, walk: 2.6f, run: 5.0f, jump: 1.3f, climb: 2.1f, crouch: 0.55f,
                weight: 2f, playerLayer, out var sqModel);
            var grab = cat.AddComponent<PlayerGrab>();
            CombatArenaBuilder.SetField(grab, "grabMask", (LayerMask)1);
            CombatArenaBuilder.SetField(grab, "reach", 0.55f);
            CombatArenaBuilder.SetField(grab, "playerMassDominance", 3f);
            CombatArenaBuilder.SetField(grab, "velocityAssist", 0.85f);

            var cam = BuildCamera(cat.transform);
            var hud = BuildHud();

            var party = new GameObject("Party").AddComponent<CatParty>();
            var so = new SerializedObject(party);
            var mem = so.FindProperty("members");
            mem.arraySize = 2;
            void SetMember(int i, string name, GameObject go, GameObject model)
            {
                var e = mem.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = name;
                e.FindPropertyRelative("input").objectReferenceValue = go.GetComponent<PlayerInputReader>();
                e.FindPropertyRelative("follower").objectReferenceValue = go.GetComponent<CatFollower>();
                e.FindPropertyRelative("health").objectReferenceValue = go.GetComponent<Health>();
                e.FindPropertyRelative("visual").objectReferenceValue = model;
            }
            SetMember(0, "고양이", cat, catModel);
            SetMember(1, "다람쥐", squirrel, sqModel);
            so.FindProperty("sideCam").objectReferenceValue = cam.GetComponent<SideFollowCamera>();
            so.FindProperty("leftBehindWaits").boolValue = true;
            so.FindProperty("killY").floatValue = KillY;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureMuffinContent();

            EditorSceneManager.SaveScene(scene, ScenePath);
            CombatArenaBuilder.AddToBuildSettings(ScenePath);
            Debug.Log("[MuffinTimeBuilder] 생성 완료: " + ScenePath);
        }

        /// <summary>
        /// 씬을 다시 만들지 않고 고친다 (손으로 바꾼 배치는 그대로): 톤(머티리얼·조명·하늘·보정), 배경 나무 원래 색,
        /// 풀 층, 발판 턱 제거, 상자 마찰, 캐릭터 정면 보기.
        /// </summary>
        [MenuItem("LNStudy/Muffin Time/Apply Update (keep scene edits)")]
        public static void ApplyToCurrentScene()
        {
            MigrateLegacyPaths();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogWarning("[MuffinTimeBuilder] MuffinTime 씬을 열고 실행하세요."); return; }

            BuildMaterials();
            foreach (var n in new[] { "Backlight", "Fill", "Sun", "Global Volume" })
            {
                var g = GameObject.Find(n);
                if (g != null) Object.DestroyImmediate(g);
            }
            BuildLighting();
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = FogColor;
                cam.farClipPlane = 250f;
            }

            // 배경 나무: 덮어씌운 실루엣 머티리얼을 프리팹 원래 머티리얼로
            var bg = GameObject.Find("World/Background");
            if (bg != null)
                foreach (var r in bg.GetComponentsInChildren<Renderer>(true))
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(r);
                    if (src != null) r.sharedMaterials = src.sharedMaterials;
                }

            // 풀 층
            var lv = GameObject.Find("World/Level");
            if (lv != null)
                foreach (var (n, x0, x1, top) in GroundSlabs)
                    if (lv.transform.Find("Grass_" + n) == null) GrassCap(lv.transform, n, x0, x1, top);

            // 발판 턱 제거
            foreach (var pp in Object.FindObjectsByType<PressurePlate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var plate = pp.transform.Find("Plate");
                if (plate == null || pp.transform.Find("Frame") == null) continue; // 승강기 센서 등
                ShapePlate(pp.transform, plate.localScale.x);
                CombatArenaBuilder.SetField(pp, "pressDepth", 0.012f);
            }

            // 상자 마찰
            foreach (var g in Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var c in g.GetComponents<Collider>()) c.sharedMaterial = crateSlide;

            // 캐릭터 정면 보기
            foreach (var pa in Object.FindObjectsByType<PlayerAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pa.GetComponent<SideScrollFacing>() == null) pa.gameObject.AddComponent<SideScrollFacing>();

            EnsureMuffinContent();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MuffinTimeBuilder] 머핀 타임 업데이트 적용 완료");
        }

        // ───────────────────────── Materials / Lighting ─────────────────────────

        static Material Lit(string name, Color c, float smooth = 0.1f, float metallic = 0f)
        {
            string path = $"{DataFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color c)
        {
            string path = $"{DataFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color c, float intensity)
        {
            var m = Lit(name, c, 0.3f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        static void BuildMaterials()
        {
            ground = Lit("CPZ_Ground", new Color(0.52f, 0.38f, 0.25f));
            grass = Lit("CPZ_Grass", new Color(0.46f, 0.68f, 0.3f));
            rock = Lit("CPZ_Rock", new Color(0.58f, 0.55f, 0.5f));
            concrete = Lit("CPZ_Concrete", new Color(0.8f, 0.74f, 0.63f), 0.15f);
            metal = Lit("CPZ_Metal", new Color(0.4f, 0.43f, 0.5f), 0.45f, 0.5f);
            crateMat = Lit("CPZ_Crate", new Color(0.8f, 0.56f, 0.3f));
            glowWarm = Emissive("CPZ_GlowWarm", new Color(1f, 0.5f, 0.12f), 2f);
            windowGlow = Emissive("CPZ_Window", new Color(1f, 0.82f, 0.5f), 2.5f);
            abyss = Unlit("CPZ_Abyss", new Color(0.32f, 0.42f, 0.52f));

            // 상자가 바닥·발판 경계에 걸리지 않게 마찰을 낮춘 재질
            string pmPath = DataFolder + "/CPZ_CrateSlide.physicMaterial";
            crateSlide = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
            if (crateSlide == null) { crateSlide = new PhysicsMaterial("CPZ_CrateSlide"); AssetDatabase.CreateAsset(crateSlide, pmPath); }
            crateSlide.dynamicFriction = 0.25f;
            crateSlide.staticFriction = 0.3f;
            crateSlide.frictionCombine = PhysicsMaterialCombine.Minimum;
            crateSlide.bounciness = 0f;
            EditorUtility.SetDirty(crateSlide);
        }

        static readonly Color FogColor = new Color(0.74f, 0.84f, 0.93f);

        static Material SkyMaterial()
        {
            string path = DataFolder + "/CPZ_Sky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_SunSize", 0.04f);
            m.SetFloat("_AtmosphereThickness", 0.9f);
            m.SetColor("_SkyTint", new Color(0.45f, 0.6f, 0.8f));
            m.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.62f));
            m.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>밝은 낮: 하늘, 따뜻한 햇빛(앞쪽 위에서), 옅은 안개, 가벼운 보정</summary>
        static void BuildLighting()
        {
            RenderSettings.skybox = SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.8f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.62f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.38f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 140f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.sun = sun;

            string path = DataFolder + "/CoopPuzzle_Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                if (c != null) Object.DestroyImmediate(c, true);
            }
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.35f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0f);
            ca.contrast.Override(6f);
            ca.saturation.Override(8f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.15f);
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(22f);
            dof.gaussianEnd.Override(60f);
            dof.gaussianMaxRadius.Override(0.8f);
            foreach (var c in profile.components)
                if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        /// <summary>지면 덩어리 윗면에 얇은 풀 층 (보이기만)</summary>
        static void GrassCap(Transform parent, string slabName, float x0, float x1, float top)
        {
            Box(parent, "Grass_" + slabName, new Vector3((x0 + x1) * 0.5f, top - 0.045f, (FloorFront + FloorBack) * 0.5f - 0.01f),
                new Vector3(x1 - x0 + 0.02f, 0.1f, FloorBack - FloorFront + 0.04f), grass, false);
        }

        // ───────────────────────── Primitives ─────────────────────────

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>윗면 높이 top, x 구간 [x0,x1]의 지면 덩어리 (아래로 깊게)</summary>
        static GameObject Slab(Transform parent, string name, float x0, float x1, float top, Material mat, float bottom = -10f)
        {
            var go = Box(parent, name, new Vector3((x0 + x1) * 0.5f, (top + bottom) * 0.5f, (FloorFront + FloorBack) * 0.5f),
                new Vector3(x1 - x0, top - bottom, FloorBack - FloorFront), mat);
            go.isStatic = true;
            return go;
        }

        // ───────────────────────── Level ─────────────────────────

        static readonly (string name, float x0, float x1, float top)[] GroundSlabs =
        {
            ("F0", -8f, 20f, 0f), ("F1", 20f, 73f, 2f), ("F2", 76.5f, 92f, 5.5f), ("F3", 98.6f, 128f, 5.5f),
        };

        static void BuildLevel(Transform world)
        {
            var lv = new GameObject("Level").transform;
            lv.SetParent(world, false);

            // 지면: F0(y0) → 2m 턱 → F1(y2) → 승강기 → F2(y5.5) → 구덩이 → F3(y5.5)
            foreach (var (n, x0, x1, top) in GroundSlabs)
            {
                Slab(lv, n, x0, x1, top, ground);
                GrassCap(lv, n, x0, x1, top);
            }
            Slab(lv, "ShaftBottom", 73f, 76.5f, 1.55f, rock);

            // 턱 앞면에 콘크리트 테두리 (단이 읽히게)
            Box(lv, "LedgeLip_F1", new Vector3(20.1f, 1.9f, 0.5f), new Vector3(0.3f, 0.25f, 4.05f), concrete, false);
            Box(lv, "LedgeLip_F2", new Vector3(76.6f, 5.4f, 0.5f), new Vector3(0.3f, 0.25f, 4.05f), concrete, false);

            // 양 끝 막는 벽
            Box(lv, "WallStart", new Vector3(-7.5f, 6f, 0.5f), new Vector3(1f, 12f, 4f), rock).isStatic = true;
            Box(lv, "WallEnd", new Vector3(127.5f, 12f, 0.5f), new Vector3(1f, 14f, 4f), rock).isStatic = true;

            // 승강기 쪽 절벽면 + 철골
            Box(lv, "ElevatorRail_L", new Vector3(73.1f, 7f, 1.9f), new Vector3(0.25f, 10f, 0.25f), metal, false);
            Box(lv, "ElevatorRail_R", new Vector3(76.4f, 7f, 1.9f), new Vector3(0.25f, 10f, 0.25f), metal, false);
            // 승강기가 올라간 뒤 왼쪽(아래층)으로 떨어지지 않게 막는 난간 (내려가 있을 땐 그 밑으로 지나다님)
            Box(lv, "ElevatorGuard", new Vector3(72.9f, 6.9f, 0.5f), new Vector3(0.3f, 2.6f, 4f), metal).isStatic = true;
            Box(lv, "ElevatorBeam", new Vector3(74.75f, 11.8f, 1.2f), new Vector3(4f, 0.4f, 1.6f), metal, false);

            // 도개교 구덩이 아래 깊은 바닥 (보이기만)
            Box(lv, "Abyss", new Vector3(95.3f, -14f, 0.5f), new Vector3(12f, 1f, 10f), abyss, false);

            // 뒤쪽 바위벽 (높이를 들쭉날쭉하게)
            Random.InitState(11);
            for (float x = -8f; x < 128f; x += Random.Range(5f, 9f))
            {
                float w = Random.Range(6f, 10f);
                float baseY = x < 20f ? 0f : x < 76.5f ? 2f : 5.5f;
                float h = Random.Range(3f, 7.5f);
                var b = Box(lv, "BackRock", new Vector3(x + w * 0.5f, baseY + h * 0.5f - 1f, Random.Range(6f, 8f)), new Vector3(w, h, 3f), rock, false);
                b.transform.rotation = Quaternion.Euler(0f, Random.Range(-6f, 6f), Random.Range(-3f, 3f));
            }
        }

        static void BuildBackground(Transform world)
        {
            var bg = new GameObject("Background").transform;
            bg.SetParent(world, false);
            Random.InitState(23);

            GameObject Prop(string file, Vector3 pos, float height, Material mat, Transform parent)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{LowPoly}/{file}.prefab");
                if (prefab == null) return null;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return go;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.localScale *= height / Mathf.Max(0.01f, b.size.y);
                // 피벗이 메시에서 멀리 떨어진 프리팹이 있어 (바위 등) 바운드 기준으로 원하는 자리에 다시 맞춘다
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
                foreach (var r in rs)
                {
                    if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                return go;
            }

            // 뒤쪽 숲 (두 겹, Low Poly 원래 색)
            for (float x = -15f; x < 140f; x += Random.Range(3f, 6f))
            {
                string tree = Random.value > 0.5f ? "Tree 1 " : "Tree 2 ";
                Prop(tree, new Vector3(x, Random.Range(1f, 5f), Random.Range(10f, 16f)), Random.Range(9f, 14f), null, bg);
                Prop(tree, new Vector3(x + 2f, Random.Range(2f, 6f), Random.Range(24f, 34f)), Random.Range(12f, 18f), null, bg);
            }
        }

        // ───────────────────────── Puzzle pieces ─────────────────────────

        static Rigidbody Crate(Transform parent, string name, Vector3 floorPos)
        {
            var go = Box(parent, name, floorPos + Vector3.up * 0.52f, Vector3.one, crateMat);
            foreach (float y in new[] { -0.38f, 0.38f })
            {
                var band = Box(go.transform, "Band", go.transform.position, Vector3.one, metal, false);
                band.transform.localScale = new Vector3(1.04f, 0.1f, 1.04f);
                band.transform.localPosition = new Vector3(0f, y, 0f);
            }
            go.GetComponent<Collider>().sharedMaterial = crateSlide;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 40f;
            rb.linearDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            var g = go.AddComponent<Grabbable>();
            CombatArenaBuilder.SetField(g, "maxDragMass", 100f);
            CombatArenaBuilder.SetField(g, "minSpeedMultiplier", 0.45f);
            var reset = go.AddComponent<ResetIfFallen>();
            CombatArenaBuilder.SetField(reset, "minY", KillY);
            return rb;
        }

        /// <summary>무게 발판(heavy: 고양이·상자만) / 가벼운 스위치(아무나)</summary>
        static PressurePlate Plate(Transform parent, string name, Vector3 floorPos, bool heavy, bool latch)
        {
            float w = heavy ? 1.8f : 1.0f;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = floorPos;

            Box(root.transform, "Frame", floorPos, Vector3.one, metal, false);
            var plate = Box(root.transform, "Plate", floorPos, Vector3.one, glowWarm, false);
            ShapePlate(root.transform, w);

            // 뒤쪽 표시등 기둥
            Box(root.transform, "LampPost", floorPos + new Vector3(0f, 0.7f, 1.6f), new Vector3(0.12f, 1.4f, 0.12f), metal, false);
            var lamp = Box(root.transform, "Lamp", floorPos + new Vector3(0f, 1.45f, 1.6f), new Vector3(0.3f, 0.2f, 0.3f), glowWarm, false);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.position = floorPos + new Vector3(0f, 1.5f, 1.1f);
            light.type = LightType.Point;
            light.range = heavy ? 4.5f : 3f;
            light.intensity = heavy ? 2.2f : 1.6f;
            light.shadows = LightShadows.None;

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.6f, 0f);
            trigger.size = new Vector3(w, 1.2f, 1.6f);

            var pp = root.AddComponent<PressurePlate>();
            var so = new SerializedObject(pp);
            so.FindProperty("requiredWeight").floatValue = heavy ? 8f : 1f;
            so.FindProperty("latch").boolValue = latch;
            so.FindProperty("pressedToast").stringValue = "";
            so.FindProperty("plate").objectReferenceValue = plate.transform;
            so.FindProperty("pressDepth").floatValue = 0.012f;
            so.FindProperty("indicator").objectReferenceValue = lamp.GetComponent<Renderer>();
            so.FindProperty("indicatorLight").objectReferenceValue = light;
            so.FindProperty("idleColor").colorValue = new Color(0.85f, 0.38f, 0.12f);
            so.FindProperty("pressedColor").colorValue = new Color(1f, 0.92f, 0.7f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return pp;
        }

        /// <summary>
        /// 발판 모양: 바닥과 거의 같은 높이(1.5cm)의 얇은 판, 충돌체 없음 → 상자가 턱에 걸리지 않고 그대로 밀려 올라간다.
        /// 지탱은 아래 지면이 한다.
        /// </summary>
        static void ShapePlate(Transform root, float w)
        {
            var frame = root.Find("Frame");
            if (frame != null)
            {
                var col = frame.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                frame.gameObject.isStatic = false;
                frame.position = root.position + Vector3.up * -0.005f;
                frame.localScale = new Vector3(w + 0.5f, 0.04f, 2.2f);
            }
            var plate = root.Find("Plate");
            if (plate != null)
            {
                plate.position = root.position + Vector3.up * 0.022f;
                plate.localScale = new Vector3(w, 0.015f, 1.6f);
            }
        }

        static PuzzleMover Mover(GameObject go, Vector3 openOffset, Vector3 openRotation, float openTime, float closeTime, bool stayOpen = false)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var m = go.AddComponent<PuzzleMover>();
            var so = new SerializedObject(m);
            so.FindProperty("openOffset").vector3Value = openOffset;
            so.FindProperty("openRotation").vector3Value = openRotation;
            so.FindProperty("openTime").floatValue = openTime;
            so.FindProperty("closeTime").floatValue = closeTime;
            so.FindProperty("stayOpenWhenReached").boolValue = stayOpen;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        /// <summary>위로 올라가는 철문. 문 위는 막힌 벽(문이 들어가는 집)이라 넘어갈 수 없다</summary>
        static PuzzleMover Door(Transform parent, string name, float x, float floorY, float height, float lift)
        {
            Box(parent, name + "_Housing", new Vector3(x, floorY + height + 3f, 0.5f), new Vector3(1f, 6f, 4f), concrete).isStatic = true;
            var door = new GameObject(name);
            door.transform.SetParent(parent, false);
            door.transform.position = new Vector3(x, floorY, 0f);
            var panel = Box(door.transform, "Panel", new Vector3(x, floorY + height * 0.5f, 0.5f), new Vector3(0.6f, height, 3.9f), metal);
            for (int i = 0; i < 3; i++)
                Box(door.transform, "Rib", new Vector3(x - 0.32f, floorY + 0.6f + i * 1.1f, 0.5f), new Vector3(0.06f, 0.12f, 3.9f), concrete, false);
            return Mover(door, new Vector3(0f, lift, 0f), Vector3.zero, 1.4f, 1.0f);
        }

        /// <summary>바닥에 0.95m 굴만 남긴 벽. 굴 위 블록이 올라가면 고양이도 지나갈 수 있다</summary>
        static PuzzleMover TunnelWall(Transform parent, float x, float floorY)
        {
            const float gap = 0.95f;
            Box(parent, "Tunnel_Housing", new Vector3(x, floorY + 4f + 3f, 0.5f), new Vector3(1.1f, 6f, 4f), concrete).isStatic = true;
            Box(parent, "Tunnel_Pipe", new Vector3(x, floorY + gap + 0.05f, -1.2f), new Vector3(1.4f, 0.12f, 0.3f), metal, false);
            var block = new GameObject("Tunnel_Block");
            block.transform.SetParent(parent, false);
            block.transform.position = new Vector3(x, floorY + gap, 0f);
            Box(block.transform, "Block", new Vector3(x, floorY + gap + 1.5f, 0.5f), new Vector3(1f, 3.05f, 3.9f), rock);
            return Mover(block, new Vector3(0f, 3.1f, 0f), Vector3.zero, 2f, 1f);
        }

        /// <summary>둘 다(무게 11 이상) 올라타야 올라가는 승강기. 꼭대기에 닿으면 그대로 멈춘다</summary>
        static void Elevator(Transform parent, float x, float floorY, float rise)
        {
            var root = new GameObject("Elevator");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, floorY, 0f);
            Box(root.transform, "Deck", new Vector3(x, floorY - 0.2f, 0.5f), new Vector3(3.3f, 0.4f, 3.9f), metal);
            Box(root.transform, "Rail", new Vector3(x, floorY + 0.55f, 1.9f), new Vector3(3.2f, 0.08f, 0.08f), concrete, false);
            Box(root.transform, "Cable_L", new Vector3(x - 1.4f, floorY + 5f, 1.9f), new Vector3(0.05f, 10f, 0.05f), metal, false);
            Box(root.transform, "Cable_R", new Vector3(x + 1.4f, floorY + 5f, 1.9f), new Vector3(0.05f, 10f, 0.05f), metal, false);
            var lamp = Box(root.transform, "Lamp", new Vector3(x, floorY + 0.9f, 1.9f), new Vector3(0.35f, 0.25f, 0.2f), glowWarm, false);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.position = new Vector3(x, floorY + 1.2f, 1.2f);
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 1.8f;
            light.shadows = LightShadows.None;
            // 무게 표시: 바닥 양쪽 발자국 판
            Box(root.transform, "Pad_L", new Vector3(x - 0.8f, floorY + 0.01f, 0f), new Vector3(1.1f, 0.02f, 1.1f), glowWarm, false);
            Box(root.transform, "Pad_R", new Vector3(x + 0.8f, floorY + 0.01f, 0f), new Vector3(1.1f, 0.02f, 1.1f), glowWarm, false);

            var sensor = new GameObject("WeightSensor");
            sensor.transform.SetParent(root.transform, false);
            sensor.transform.position = new Vector3(x, floorY, 0f);
            var bc = sensor.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0f, 0.7f, 0f);
            bc.size = new Vector3(3.2f, 1.4f, 1.6f);
            var pp = sensor.AddComponent<PressurePlate>();
            var so = new SerializedObject(pp);
            so.FindProperty("requiredWeight").floatValue = 11f; // 고양이 10 + 다람쥐 2
            so.FindProperty("latch").boolValue = false;
            so.FindProperty("pressedToast").stringValue = "";
            so.FindProperty("indicator").objectReferenceValue = lamp.GetComponent<Renderer>();
            so.FindProperty("indicatorLight").objectReferenceValue = light;
            so.FindProperty("idleColor").colorValue = new Color(0.85f, 0.38f, 0.12f);
            so.FindProperty("pressedColor").colorValue = new Color(1f, 0.92f, 0.7f);
            so.ApplyModifiedPropertiesWithoutUndo();

            var mover = Mover(root, new Vector3(0f, rise, 0f), Vector3.zero, 3f, 2f, stayOpen: true);
            UnityEventTools.AddPersistentListener(pp.onPressed, mover.AddSignal);
            UnityEventTools.AddPersistentListener(pp.onReleased, mover.RemoveSignal);
        }

        /// <summary>경첩(x)을 축으로 세워져 있다가 내려오는 다리</summary>
        static PuzzleMover Drawbridge(Transform parent, float hingeX, float floorY, float length)
        {
            Box(parent, "Bridge_Post", new Vector3(hingeX - 0.3f, floorY + 1.2f, 1.9f), new Vector3(0.3f, 2.4f, 0.3f), metal, false);
            Box(parent, "Bridge_PostFar", new Vector3(hingeX + length + 0.2f, floorY + 0.8f, 1.9f), new Vector3(0.3f, 1.6f, 0.3f), metal, false);
            var pivot = new GameObject("Drawbridge");
            pivot.transform.SetParent(parent, false);
            pivot.transform.SetPositionAndRotation(new Vector3(hingeX, floorY, 0f), Quaternion.identity);
            // 수평 상태로 판을 만든 뒤 세워 둔다 (닫힘 = 세운 상태)
            Box(pivot.transform, "Deck", new Vector3(hingeX + length * 0.5f, floorY - 0.15f, 0.5f), new Vector3(length, 0.3f, 3.6f), crateMat);
            for (float d = 0.6f; d < length; d += 1.2f)
                Box(pivot.transform, "Plank", new Vector3(hingeX + d, floorY + 0.01f, 0.5f), new Vector3(0.08f, 0.02f, 3.6f), metal, false);
            pivot.transform.rotation = Quaternion.Euler(0f, 0f, 80f);
            return Mover(pivot, Vector3.zero, new Vector3(0f, 0f, -80f), 2.2f, 1.6f);
        }

        static void Checkpoint(Transform parent, float x, float floorY, string message)
        {
            var go = new GameObject($"Checkpoint_{x:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, floorY + 0.1f, 0f);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0f, 1.5f, 0f);
            bc.size = new Vector3(1f, 3f, 3f);
            var cp = go.AddComponent<LNStudy.Gimmicks.Checkpoint>();
            CombatArenaBuilder.SetField(cp, "message", message);
        }

        // ───────────────────────── Characters / Camera / HUD ─────────────────────────

        static GameObject BuildCharacter(Transform parent, string name, string prefabPath, RuntimeAnimatorController controller,
            Vector3 floorPos, float scale, float height, float radius, float walk, float run, float jump, float climb, float crouch,
            float weight, int layer, out GameObject model)
        {
            var go = new GameObject(name);
            go.tag = "Player";
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(floorPos + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.right));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            go.AddComponent<PlayerInputReader>();

            var motor = go.AddComponent<PlayerMotor>();
            CombatArenaBuilder.SetField(motor, "sideScroll", true);
            CombatArenaBuilder.SetField(motor, "walkSpeed", walk);
            CombatArenaBuilder.SetField(motor, "runSpeed", run);
            CombatArenaBuilder.SetField(motor, "standHeight", height);
            CombatArenaBuilder.SetField(motor, "crouchHeight", crouch);
            CombatArenaBuilder.SetField(motor, "jumpHeight", jump);
            CombatArenaBuilder.SetField(motor, "maxClimbHeight", climb);
            CombatArenaBuilder.SetField(motor, "turnSpeed", 1080f);

            var health = go.AddComponent<Health>();
            CombatArenaBuilder.SetField(health, "maxHealth", 100f);
            var w = go.AddComponent<PuzzleWeight>();
            CombatArenaBuilder.SetField(w, "weight", weight);

            var follower = go.AddComponent<CatFollower>();
            CombatArenaBuilder.SetField(follower, "obstacleMask", (LayerMask)1);
            CombatArenaBuilder.SetField(follower, "allowTeleport", false);
            CombatArenaBuilder.SetField(follower, "followDistance", 1.6f);
            CombatArenaBuilder.SetField(follower, "runDistance", 4.5f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * scale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var pa = model.AddComponent<PlayerAnimator>();
            CombatArenaBuilder.SetField(pa, "handHeight", height * 0.38f);
            CombatArenaBuilder.SetField(pa, "handSpread", radius * 0.55f);
            model.AddComponent<SideScrollFacing>();
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            CombatArenaBuilder.SetLayerRecursive(go, layer);
            return go;
        }

        static Camera BuildCamera(Transform target)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 250f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = FogColor;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var follow = go.AddComponent<SideFollowCamera>();
            var offset = new Vector3(0f, 2.6f, -12f);
            var look = new Vector3(0f, 1.1f, 0f);
            CombatArenaBuilder.SetField(follow, "target", target);
            CombatArenaBuilder.SetField(follow, "offset", offset);
            CombatArenaBuilder.SetField(follow, "lookOffset", look);
            CombatArenaBuilder.SetField(follow, "lookAhead", 2.2f);
            CombatArenaBuilder.SetField(follow, "smoothTime", 0.35f);
            go.transform.position = target.position + offset;
            go.transform.rotation = Quaternion.LookRotation(look - offset);
            return cam;
        }

        static StageHUD BuildHud()
        {
            var go = new GameObject("HUD");
            var hud = go.AddComponent<StageHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("showHealthBar").boolValue = false;
            so.FindProperty("openingBanner").stringValue = "머핀 타임";
            so.FindProperty("openingToast").stringValue = "사라진 머핀을 찾아서 — 고양이는 힘이 세고, 다람쥐는 잘 오른다";
            so.FindProperty("controlsText").stringValue =
                "A/D 이동 · Shift 달리기 · Space 점프(벽 앞에서 오르기) · E 상자 잡기/놓기(고양이)\n" +
                "Tab 캐릭터 교체 · F 동료 부르기/기다리게 하기";
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }
    
        // ───────────────────────── Muffin Time ─────────────────────────

        /// <summary>예전 이름(CoopPuzzle)의 씬·폴더를 머핀 타임 이름으로 옮긴다 (GUID 유지, 한 번만)</summary>
        static void MigrateLegacyPaths()
        {
            bool sceneOpen = SceneManager.GetActiveScene().path == LegacyScenePath;
            if (sceneOpen) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyScenePath) != null && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                string err = AssetDatabase.MoveAsset(LegacyScenePath, ScenePath);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[MuffinTimeBuilder] 씬 이동 실패: " + err);
            }
            if (AssetDatabase.IsValidFolder(LegacyDataFolder) && !AssetDatabase.IsValidFolder(DataFolder))
            {
                string err = AssetDatabase.MoveAsset(LegacyDataFolder, DataFolder);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[MuffinTimeBuilder] 폴더 이동 실패: " + err);
            }
            // 옛 경로·없어진 씬 항목 정리
            var list = EditorBuildSettings.scenes
                .Where(sc => sc.path != LegacyScenePath && AssetDatabase.LoadAssetAtPath<SceneAsset>(sc.path) != null).ToList();
            EditorBuildSettings.scenes = list.ToArray();
            CombatArenaBuilder.AddToBuildSettings(ScenePath);
            if (sceneOpen && SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        }

        static void BuildJuiceMaterials()
        {
            CombatArenaBuilder.EnsureFolder(DataFolder);
            string path = DataFolder + "/MT_Dust.mat";
            dustMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (dustMat == null) { dustMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(dustMat, path); }
            dustMat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            dustMat.SetColor("_BaseColor", Color.white);
            dustMat.SetFloat("_Surface", 1f);
            dustMat.SetFloat("_Blend", 0f);
            dustMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            dustMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            dustMat.SetFloat("_ZWrite", 0f);
            dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dustMat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(dustMat);

            markerMat = Emissive("MT_Marker", new Color(1f, 0.62f, 0.2f), 1.8f);
            interiorMat = Emissive("MT_Interior", new Color(1f, 0.72f, 0.4f), 1.2f);
        }

        /// <summary>
        /// 머핀 타임 콘텐츠를 씬에 맞춘다 (반복 실행해도 같은 결과):
        /// 캐릭터 손맛·정면 보기, 상자 손맛, 조종 표시, 작은 머핀 3개, 오두막 엔딩, HUD 제목·카운터.
        /// </summary>
        static void EnsureMuffinContent()
        {
            BuildMaterials();
            BuildJuiceMaterials();
            var party = Object.FindFirstObjectByType<CatParty>();
            var puzzlesGo = GameObject.Find("Puzzles");
            Transform puzzles = puzzlesGo != null ? puzzlesGo.transform : new GameObject("Puzzles").transform;

            // 캐릭터: 정면 보기 + 손맛
            foreach (var pa in Object.FindObjectsByType<PlayerAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pa.GetComponent<SideScrollFacing>() == null) pa.gameObject.AddComponent<SideScrollFacing>();
                var juice = pa.GetComponent<CharacterJuice>();
                if (juice == null) juice = pa.gameObject.AddComponent<CharacterJuice>();
                var cap = pa.GetComponentInParent<CapsuleCollider>();
                bool heavy = cap != null && cap.height > 1f;
                var so = new SerializedObject(juice);
                so.FindProperty("heavy").boolValue = heavy;
                so.FindProperty("dustMaterial").objectReferenceValue = dustMat;
                so.FindProperty("strideLength").floatValue = heavy ? 0.6f : 0.42f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 상자
            foreach (var g in Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var cj = g.GetComponent<CrateJuice>();
                if (cj == null) cj = g.gameObject.AddComponent<CrateJuice>();
                CombatArenaBuilder.SetField(cj, "dustMaterial", dustMat);
            }

            // 조종 표시
            if (party != null && Object.FindFirstObjectByType<ActiveCharacterMarker>() == null)
            {
                var m = new GameObject("ActiveMarker");
                var vis = Box(m.transform, "Diamond", Vector3.zero, new Vector3(0.16f, 0.16f, 0.16f), markerMat, false);
                vis.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                vis.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var marker = m.AddComponent<ActiveCharacterMarker>();
                CombatArenaBuilder.SetField(marker, "party", party);
                CombatArenaBuilder.SetField(marker, "visual", vis.transform);
            }

            // 작은 머핀
            var muffins = GameObject.Find("Muffins");
            if (muffins == null)
            {
                muffins = new GameObject("Muffins");
                // 1) 상자 처음 자리 위 높이: 다람쥐가 상자를 딛고 뛰어야 닿음 (고양이는 점프가 낮아 못 닿음)
                Pickup(muffins.transform, "Muffin_Crate", new Vector3(13f, 3.2f, 0f));
                // 2) 좁은 굴 안: 다람쥐가 지나가며 줍는 보상
                Pickup(muffins.transform, "Muffin_Tunnel", new Vector3(48f, 2.45f, 0f));
                // 3) 도개교 발판 위 높이: 상자를 올려 둔 뒤 그 위에서 다람쥐가 점프
                Pickup(muffins.transform, "Muffin_Bridge", new Vector3(87.5f, 5.5f + 3.2f, 0f));
            }

            // 가독성: 뒤 바위벽은 한 톤 어둡게, 굴 위 블록은 금속으로 (같은 색이라 배경에 묻히던 문제)
            var backRock = Lit("MT_BackRock", new Color(0.47f, 0.5f, 0.47f));
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.name == "BackRock") r.sharedMaterial = backRock;
                else if (r.name == "Block" && r.transform.parent != null && r.transform.parent.name == "Tunnel_Block") r.sharedMaterial = metal;
            }

            // 오두막 엔딩 (항상 새로 만든다)
            var oldGoal = GameObject.Find("Goal");
            if (oldGoal != null) Object.DestroyImmediate(oldGoal);
            BuildGoal(puzzles, 116f, 5.5f, party);

            // HUD
            var hud = Object.FindFirstObjectByType<StageHUD>();
            if (hud != null)
            {
                var so = new SerializedObject(hud);
                so.FindProperty("openingBanner").stringValue = "머핀 타임";
                so.FindProperty("openingToast").stringValue = "사라진 머핀을 찾아서 — 고양이는 힘이 세고, 다람쥐는 잘 오른다";
                so.FindProperty("hideAllText").boolValue = true; // 글자 없는 화면
                so.ApplyModifiedPropertiesWithoutUndo();
                var mh = hud.GetComponent<MuffinHUD>();
                if (mh == null) mh = hud.gameObject.AddComponent<MuffinHUD>();
                CombatArenaBuilder.SetField(mh, "muffinIcon", RenderMuffinIcon());
            }
        }

        static GameObject MuffinVisual(Transform parent, string prefabFile, float targetSize)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/Prefabs/{prefabFile}.prefab");
            var holder = new GameObject("Visual");
            holder.transform.SetParent(parent, false);
            if (prefab == null) return holder;
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            foreach (var c in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var rs = vis.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                vis.transform.localScale *= targetSize / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                vis.transform.position += holder.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
                foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.On;
            }
            return holder;
        }

        static void Pickup(Transform parent, string name, Vector3 center)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.25f;
            var vis = MuffinVisual(go.transform, "StrawberrySpongeCake", 0.42f);
            vis.transform.localPosition = Vector3.down * 0.2f;
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.8f, 0.5f);
            l.range = 2.5f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
            var p = go.AddComponent<MuffinPickup>();
            var so = new SerializedObject(p);
            so.FindProperty("visual").objectReferenceValue = vis.transform;
            so.FindProperty("dustMaterial").objectReferenceValue = dustMat;
            so.FindProperty("glow").objectReferenceValue = l;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 오두막 + 탁자 위 큰 머핀 + 엔딩 연출. 오두막은 문 구멍이 뚫린 벽 조각으로 만들어
        /// 캐릭터가 실제로 문 안으로 걸어 들어가는 모습이 보인다 (충돌체 없음: 통로 Z=0과 겹치지 않음).
        /// </summary>
        static void BuildGoal(Transform parent, float x, float floorY, CatParty party)
        {
            var root = new GameObject("Goal").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(x, floorY, 0f);

            float hx0 = x + 1.6f, hx1 = x + 6.6f, zf = 0.8f, zb = 2.8f, h = 4.2f;
            float doorX = x + 3f, dw = 1.4f, dh = 2.3f, th = 0.15f;
            var hut = new GameObject("Hut").transform;
            hut.SetParent(root, false);
            void Wall(string n, float x0, float x1, float y0, float y1, float z0, float z1, Material m)
                => Box(hut, n, new Vector3((x0 + x1) * 0.5f, floorY + (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                       new Vector3(x1 - x0, y1 - y0, z1 - z0), m, false);
            Wall("Front_L", hx0, doorX - dw * 0.5f, 0f, h, zf, zf + th, concrete);
            Wall("Front_R", doorX + dw * 0.5f, hx1, 0f, h, zf, zf + th, concrete);
            Wall("Front_Top", doorX - dw * 0.5f, doorX + dw * 0.5f, dh, h, zf, zf + th, concrete);
            Wall("Side_L", hx0, hx0 + th, 0f, h, zf, zb, concrete);
            Wall("Side_R", hx1 - th, hx1, 0f, h, zf, zb, concrete);
            Wall("Back", hx0, hx1, 0f, h, zb - th, zb, concrete);
            Wall("Interior", hx0 + th, hx1 - th, 0f, h - 0.1f, zb - th - 0.02f, zb - th, interiorMat);
            Wall("Roof", hx0 - 0.4f, hx1 + 0.4f, h, h + 0.35f, zf - 0.4f, zb + 0.3f, metal);
            Wall("Roof_Top", hx0 + 0.2f, hx1 - 0.2f, h + 0.35f, h + 0.7f, zf, zb, metal);
            Wall("DoorFrame", doorX - dw * 0.5f - 0.1f, doorX + dw * 0.5f + 0.1f, dh, dh + 0.12f, zf - 0.06f, zf + 0.02f, crateMat);
            Wall("Window", x + 5.0f, x + 6.0f, 2.2f, 3.0f, zf - 0.03f, zf - 0.01f, windowGlow);
            Wall("Sign", doorX - 1.3f, doorX + 1.3f, h + 0.75f, h + 1.35f, zf - 0.05f, zf + 0.05f, crateMat);

            // 문짝 (경첩 = 문 왼쪽 모서리, 안쪽으로 열림)
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(root, false);
            hinge.position = new Vector3(doorX - dw * 0.5f, floorY, zf + 0.07f);
            var leaf = Box(hinge, "Leaf", new Vector3(doorX, floorY + dh * 0.5f, zf + 0.07f), new Vector3(dw, dh, 0.08f), crateMat, false);
            Box(leaf.transform, "Knob", new Vector3(doorX + dw * 0.35f, floorY + 1.1f, zf + 0.01f), Vector3.one, metal, false)
                .transform.localScale = new Vector3(0.07f, 0.04f, 1.2f);

            var inLight = new GameObject("InteriorLight").AddComponent<Light>();
            inLight.transform.SetParent(root, false);
            inLight.transform.position = new Vector3(doorX, floorY + 1.8f, 1.9f);
            inLight.type = LightType.Point;
            inLight.color = new Color(1f, 0.75f, 0.45f);
            inLight.range = 5f;
            inLight.intensity = 2.5f;
            inLight.shadows = LightShadows.None;
            var porch = new GameObject("PorchLight").AddComponent<Light>();
            porch.transform.SetParent(root, false);
            porch.transform.position = new Vector3(x + 1f, floorY + 2.2f, -0.8f);
            porch.type = LightType.Point;
            porch.color = new Color(1f, 0.82f, 0.55f);
            porch.range = 7f;
            porch.intensity = 2.5f;
            porch.shadows = LightShadows.None;

            // 탁자 + 큰 머핀 (통로 뒤쪽)
            Box(root, "Table", new Vector3(x, floorY + 0.35f, 1.25f), new Vector3(1.2f, 0.7f, 0.9f), crateMat, false);
            var muffin = new GameObject("BigMuffin").transform;
            muffin.SetParent(root, false);
            muffin.position = new Vector3(x, floorY + 0.7f, 1.25f);
            MuffinVisual(muffin, "ChocolateCake", 0.65f);
            var rot = muffin.gameObject.AddComponent<Rotator>();
            CombatArenaBuilder.SetField(rot, "degreesPerSecond", new Vector3(0f, 25f, 0f));
            var shine = new GameObject("Shine").AddComponent<Light>();
            shine.transform.SetParent(muffin, false);
            shine.transform.localPosition = new Vector3(0f, 0.9f, -0.6f);
            shine.type = LightType.Point;
            shine.color = new Color(1f, 0.85f, 0.6f);
            shine.range = 2.5f;
            shine.intensity = 2f;
            shine.shadows = LightShadows.None;

            Transform Point(string n, Vector3 p)
            {
                var t = new GameObject(n).transform;
                t.SetParent(root, false);
                t.position = p;
                return t;
            }
            var land = Point("MuffinLand", new Vector3(x, floorY, 0f));
            var doorPoint = Point("DoorPoint", new Vector3(doorX, floorY, 0f));
            var camPoint = Point("EndingCamera", new Vector3(x + 0.6f, floorY + 1.9f, -6.5f));
            var camLook = Point("EndingLook", new Vector3(x + 0.6f, floorY + 0.8f, 0.5f));

            var ending = root.gameObject.AddComponent<MuffinTimeEnding>();
            var eso = new SerializedObject(ending);
            eso.FindProperty("party").objectReferenceValue = party;
            eso.FindProperty("bigMuffin").objectReferenceValue = muffin;
            eso.FindProperty("muffinLandPoint").objectReferenceValue = land;
            eso.FindProperty("doorHinge").objectReferenceValue = hinge;
            eso.FindProperty("doorPoint").objectReferenceValue = doorPoint;
            eso.FindProperty("insideZ").floatValue = 1.9f;
            eso.FindProperty("interiorLight").objectReferenceValue = inLight;
            eso.FindProperty("cameraPoint").objectReferenceValue = camPoint;
            eso.FindProperty("cameraLook").objectReferenceValue = camLook;
            eso.FindProperty("dustMaterial").objectReferenceValue = dustMat;
            eso.ApplyModifiedPropertiesWithoutUndo();

            var trig = new GameObject("GoalTrigger");
            trig.transform.SetParent(root, false);
            trig.transform.position = new Vector3(x + 0.3f, floorY + 1.5f, 0f);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(5f, 3f, 3f);
            var pt = trig.AddComponent<PlayerTrigger>();
            var so = new SerializedObject(pt);
            so.FindProperty("requiredPlayers").intValue = 2;
            so.FindProperty("message").stringValue = "";
            so.FindProperty("banner").stringValue = "";
            so.ApplyModifiedPropertiesWithoutUndo();
            UnityEventTools.AddPersistentListener(pt.onEnter, ending.Play);
        }
    
        /// <summary>
        /// HUD용 머핀 아이콘을 실제 모델로 렌더해 PNG 스프라이트로 저장한다 (배경 투명).
        /// 그림자 칸은 같은 스프라이트를 어두운 반투명색으로 칠해 쓴다.
        /// </summary>
        static Sprite RenderMuffinIcon()
        {
            string path = DataFolder + "/MuffinIcon.png";
            const int size = 256;

            var rig = new GameObject("__MuffinIconRig");
            rig.transform.position = new Vector3(0f, -500f, 0f);
            var vis = MuffinVisual(rig.transform, "StrawberrySpongeCake", 1f);
            vis.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);

            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(rig.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

            var camGo = new GameObject("IconCam");
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.62f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            Vector3 center = rig.transform.position + Vector3.up * 0.5f;
            camGo.transform.position = center + new Vector3(0f, 1.6f, -5f);
            camGo.transform.LookAt(center);

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderSettings.fog = fog;

            // 알파가 안 나오는 렌더 경로 대비: 모서리가 불투명이면 배경색 기준으로 오려낸다
            var px = tex.GetPixels();
            if (px[0].a > 0.5f)
            {
                Color bgc = px[0];
                for (int i = 0; i < px.Length; i++)
                {
                    float d = Mathf.Abs(px[i].r - bgc.r) + Mathf.Abs(px[i].g - bgc.g) + Mathf.Abs(px[i].b - bgc.b);
                    px[i].a = Mathf.Clamp01((d - 0.02f) * 12f);
                }
                tex.SetPixels(px);
                tex.Apply();
            }

            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(rig);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
