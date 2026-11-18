using System.Collections.Generic;
using System.Linq;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Companion;
using LNStudy.Gimmicks;
using LNStudy.Interaction;
using LNStudy.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 2.5D 챕터 "어두운 숲" (리틀 나이트메어 2의 숲 챕터 느낌). 같은 Idyllic Fantasy Nature 에셋을
    /// 밤 조명·짙은 안개·저채도 보정으로 바꿔 쓰고, 밝은 분홍 나무·꽃은 빼고 침엽수·바위·덤불만 쓴다.
    ///
    /// 진행 (왼쪽 → 오른쪽, X축):
    ///  1) 시작                          x 0~20
    ///  2) 물리 퍼즐: 통나무 상자를 밀어 2.6m 절벽 오르기 (E 잡기)       x 20~30
    ///  3) 라멘 기믹: 구덩이 건너편 바위 위 버튼 → 통나무 다리가 쓰러져 길이 됨  x 46~58
    ///  4) 잠입: 뒤쪽을 걷는 거인의 손전등 피하기 (덤불 안에서 Ctrl 웅크리면 숨음)  x 60~100
    ///  5) 전투: 숲속 공터의 로봇 2마리 처치 → 장벽 해제              x 102~123
    ///  6) 출구                          x 128~138
    /// 플레이 라인은 z ≈ 0 (W/S로 깊이 방향 약간 이동 가능, 보이지 않는 벽으로 제한)
    /// </summary>
    public static class DarkForestBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/DarkForest_2_5D.unity";
        const string DataFolder = "Assets/_Project/Scenes/DarkForest_2_5D";
        const string KitPrefabs = "Assets/Idyllic Fantasy Nature/Prefabs";
        const string KitFolder = "Assets/Idyllic Fantasy Nature";
        const string CombatArenaScene = "Assets/_Project/Scenes/Combat_Arena.unity";

        const float X0 = -6f, LengthX = 152f, Z0 = -30f, WidthZ = 60f, MaxH = 30f;
        const int Res = 513;
        const float LaneHalf = 2.6f;

        const float StartH = 2f, UpperH = 4.6f;
        const float LedgeX = 30f;
        const float PitMin = 50f, PitMax = 56f, PitBottom = -4f;
        const float GiantZ = 8.5f, GiantMinX = 62f, GiantMaxX = 100f;

        /// <summary>true = 밝은 낮 숲 (기본), false = 어두운 밤 숲</summary>
        const bool Bright = true;

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem("LNStudy/Dark Forest 2.5D/Build Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) return;

            int enemyLayer = CombatArenaBuilder.EnsureLayer("Enemy");
            int playerLayer = LayerMask.NameToLayer("Player");
            var humanoids = MonsterRigSetup.SetupAll();
            CombatArenaBuilder.EnsureFolder(DataFolder);
            FantasyStageBuilder.SlowVegetationWind();

            EditorSceneManager.SaveOpenScenes();
            if (System.IO.File.Exists(CombatArenaScene)) EditorSceneManager.OpenScene(CombatArenaScene);
            string petPrefab = CombatArenaBuilder.ResolvePetPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rng = new System.Random(4242);

            var world = new GameObject("World");
            var terrain = BuildTerrain(world.transform);
            var gimmicks = new GameObject("Gimmicks").transform;

            BuildLaneWalls(gimmicks);
            BuildLedgeDecor(world.transform, terrain);
            var crate = BuildPushLog(gimmicks, terrain);
            var bridge = BuildBridge(gimmicks, terrain);
            var button = BuildButtonPillar(world.transform, gimmicks, terrain);
            UnityEventTools.AddPersistentListener(button.onPressed, bridge.Lower);
            BuildPitHazard(gimmicks);
            BuildHidingBushes(gimmicks, terrain);
            BuildForest(world.transform, terrain, rng);
            BuildLighting(world.transform, terrain);
            if (!Bright)
            {
                DarkenVegetation(world.transform);
                DarkenVegetation(gimmicks);
            }

            // 네비메시 (공터 전투용)
            var surface = world.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            string navPath = DataFolder + "/DarkForest_NavMesh.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath) != null) AssetDatabase.DeleteAsset(navPath);
            if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            // 플레이어 (2.5D: 카메라 기준이 아니라 월드 축 기준 이동)
            var player = CombatArenaBuilder.BuildPlayer(playerLayer, out var unityChanAnimator);
            player.transform.SetPositionAndRotation(Ground(terrain, 3f, 0f), Quaternion.LookRotation(Vector3.right));
            var grab = player.AddComponent<PlayerGrab>();
            CombatArenaBuilder.SetField(grab, "velocityAssist", 0.8f);
            var cam = BuildSideCamera(player.transform);
            var petL = CombatArenaBuilder.BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Left);
            var petR = CombatArenaBuilder.BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Right);
            CombatArenaBuilder.SetupPlayerCombat(player, cam, new[] { petL, petR }, enemyLayer);
            CombatArenaBuilder.SetupShisaAndSwitcher(player, new[] { petL, petR }, cam);
            CombatArenaBuilder.SetField(player.GetComponent<Health>(), "maxHealth", 100f);

            // 거인 감시자
            BuildGiant(gimmicks, terrain, player.transform);

            // 전투 공터
            var monsters = new GameObject("Monsters").transform;
            var list = new List<Health>();
            foreach (var (robot, x, z) in new[] { ("Paperman", 110f, 0.5f), ("Robert", 117f, -0.8f) })
            {
                if (!humanoids.Contains(robot)) continue;
                var pos = Ground(terrain, x, z);
                var go = CombatArenaBuilder.BuildMonster(robot, pos, -90f, enemyLayer, player.transform, monsters);
                if (go == null) continue;
                var brain = go.GetComponent<MonsterBrain>();
                var h = go.GetComponent<Health>();
                CombatArenaBuilder.SetField(h, "maxHealth", 160f);
                CombatArenaBuilder.SetField(brain, "attackDamage", 16f);
                CombatArenaBuilder.SetField(brain, "attackWindup", 0.45f);
                CombatArenaBuilder.SetField(brain, "attackCooldown", 1.3f);
                CombatArenaBuilder.SetField(brain, "chaseSpeed", 3.6f);
                CombatArenaBuilder.SetField(brain, "viewDistance", 12f);
                CombatArenaBuilder.SetField(brain, "patrolRadius", 3f);
                CombatArenaBuilder.SetField(brain, "leashDistance", 14f);
                CombatArenaBuilder.SetField(brain, "respawnAfterDeath", false);
                list.Add(h);
            }
            var barrier = FantasyStageBuilder.BuildBarrier(gimmicks, terrain, new Vector2(123.5f, 0f), new Vector2(1f, 0f), 7f, "Barrier_Clearing");
            BuildStage(gimmicks, terrain, "숲속 공터", 113f, 22f, list, barrier, 104f, false);
            BuildStage(gimmicks, terrain, Bright ? "숲속 오솔길" : "어두운 숲", 134f, 8f, new List<Health>(), null, 130f, true);

            // 체크포인트 + 안내
            BuildCheckpoint(gimmicks, terrain, 3f, "E로 통나무를 잡고 밀어서 절벽을 올라가자");
            BuildCheckpoint(gimmicks, terrain, 34f, "구덩이 건너 바위 위 버튼 → Tab으로 시사 교체, 좌클릭 꾹 눌러 라멘!");
            BuildCheckpoint(gimmicks, terrain, 59f, "거인이 손전등으로 길을 비춘다. 덤불 안에서 Ctrl로 웅크려 숨자");
            BuildCheckpoint(gimmicks, terrain, 80.5f, "");
            BuildCheckpoint(gimmicks, terrain, 101f, "");

            // HUD
            var hud = new GameObject("HUD").AddComponent<StageHUD>();
            var hso = new SerializedObject(hud);
            hso.FindProperty("playerHealth").objectReferenceValue = player.GetComponent<Health>();
            hso.FindProperty("switcher").objectReferenceValue = player.GetComponent<CharacterSwitcher>();
            hso.FindProperty("openingBanner").stringValue = Bright ? "숲속 오솔길" : "어두운 숲";
            hso.FindProperty("openingToast").stringValue = "E로 통나무를 잡고 밀어서 절벽을 올라가자";
            hso.FindProperty("controlsText").stringValue =
                "A/D 이동 · W/S 깊이 · Shift 달리기 · Space 점프 · Ctrl 웅크리기(숨기) · E 잡기 · Tab 캐릭터 교체\n유니짱: 좌클릭 드론 공격 · 시사: 좌클릭 꾹 라멘 던지기 · Q 미사일";
            hso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            CombatArenaBuilder.AddToBuildSettings(ScenePath);
            Debug.Log("[DarkForestBuilder] 완료: " + ScenePath);
        }

        // ───────────────────────── 지형 ─────────────────────────

        static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>플레이 라인의 바닥 높이 (X 기준 단면)</summary>
        static float Profile(float x)
        {
            float h = StartH;
            h = Mathf.Lerp(h, UpperH, Smooth01((x - LedgeX) / 0.35f));                    // 2.6m 절벽
            float pit = Smooth01((x - PitMin) / 0.4f) * (1f - Smooth01((x - PitMax) / 0.4f)); // 구덩이
            h = Mathf.Lerp(h, PitBottom, pit);
            h += Smooth01((x - 125f) / 10f) * 0.6f;                                        // 출구 쪽 완만한 오르막
            h += 0.12f * Mathf.Sin(x * 0.35f) * (x > LedgeX + 1 && (x < PitMin - 1 || x > PitMax + 1) ? 1f : 0f);
            return h;
        }

        static float HeightAt(float x, float z)
        {
            float lane = Profile(x);
            float h;
            if (z > LaneHalf + 0.9f)
            {
                // 뒤쪽 숲: 천천히 오르다가 멀리서 벽
                float d = z - (LaneHalf + 0.9f);
                h = lane + d * 0.18f + 0.9f * Mathf.PerlinNoise(x * 0.08f, z * 0.08f);
                h = Mathf.Lerp(h, 18f, Smooth01((z - 20f) / 6f));
            }
            else if (z < -LaneHalf - 0.9f)
            {
                // 앞쪽(카메라 쪽): 살짝 낮게
                float d = -z - (LaneHalf + 0.9f);
                h = lane - Mathf.Min(d * 0.08f, 0.8f) + 0.4f * Mathf.PerlinNoise(x * 0.1f + 9f, z * 0.1f);
            }
            else h = lane;

            // 구덩이는 모든 깊이에 걸쳐 (돌아갈 수 없게)
            float pit = Smooth01((x - PitMin) / 0.4f) * (1f - Smooth01((x - PitMax) / 0.4f));
            if (z > -14f && z < 20f) h = Mathf.Lerp(h, PitBottom, pit);

            // 시작·끝 벽
            h = Mathf.Max(h, Mathf.Lerp(16f, h, Smooth01((x - X0) / 5f)));
            h = Mathf.Max(h, Mathf.Lerp(h, 16f, Smooth01((x - 141f) / 4f)));
            return h;
        }

        static Terrain BuildTerrain(Transform parent)
        {
            string tdPath = DataFolder + "/DarkForest_Terrain.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, tdPath); }
            td.heightmapResolution = Res;
            td.alphamapResolution = 512;
            // 구덩이 바닥이 0 아래라 지형 자체를 아래로 내린다
            const float baseY = -8f;
            td.size = new Vector3(LengthX, MaxH, WidthZ);

            var heights = new float[Res, Res];
            for (int zi = 0; zi < Res; zi++)
                for (int xi = 0; xi < Res; xi++)
                {
                    float x = X0 + xi / (float)(Res - 1) * LengthX;
                    float z = Z0 + zi / (float)(Res - 1) * WidthZ;
                    heights[zi, xi] = Mathf.Clamp01((HeightAt(x, z) - baseY) / MaxH);
                }
            td.SetHeights(0, 0, heights);

            string[] layerNames = { "Forest_Layer", "Dirt_Layer", "Rock_Layer", "Dirt_Stone_Layer" };
            td.terrainLayers = layerNames.Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{KitFolder}/Terrain Layer/{n}.terrainlayer")).Where(l => l != null).ToArray();
            int L = td.terrainLayers.Length;
            int AR = td.alphamapResolution;
            var alpha = new float[AR, AR, L];
            for (int zi = 0; zi < AR; zi++)
                for (int xi = 0; xi < AR; xi++)
                {
                    float nx = xi / (float)(AR - 1), nz = zi / (float)(AR - 1);
                    float z = Z0 + nz * WidthZ;
                    float steep = td.GetSteepness(nx, nz);
                    float rock = Smooth01((steep - 30f) / 12f);
                    float lane = 1f - Smooth01((Mathf.Abs(z) - LaneHalf) / 1.5f);
                    float[] w = { (1f - lane) * (1f - rock), lane * (1f - rock), rock, 0f };
                    float sum = 0f;
                    for (int l = 0; l < L; l++) sum += w[l];
                    for (int l = 0; l < L; l++) alpha[zi, xi, l] = sum > 0f ? w[l] / sum : (l == 0 ? 1f : 0f);
                }
            td.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(X0, baseY, Z0);
            go.isStatic = true;
            var t = go.GetComponent<Terrain>();
            t.drawInstanced = true;
            t.heightmapPixelError = 2f;
            var litShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (litShader != null)
            {
                string matPath = DataFolder + "/DarkForest_TerrainLit.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(litShader); AssetDatabase.CreateAsset(mat, matPath); }
                t.materialTemplate = mat;
            }
            return t;
        }

        static Vector3 Ground(Terrain t, float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }

        // ───────────────────────── 플레이 라인 / 퍼즐 ─────────────────────────

        static void BuildLaneWalls(Transform parent)
        {
            // 깊이 방향 이동 제한. Ignore Raycast 레이어라 거인 시야·라멘 궤적 미리보기를 막지 않는다
            foreach (float z in new[] { -LaneHalf, LaneHalf })
            {
                var w = new GameObject(z < 0 ? "Lane Wall Front" : "Lane Wall Back");
                w.transform.SetParent(parent);
                w.layer = 2;
                var box = w.AddComponent<BoxCollider>();
                w.transform.position = new Vector3(X0 + LengthX * 0.5f, 5f, z + Mathf.Sign(z) * 0.5f);
                box.size = new Vector3(LengthX, 40f, 1f);
            }
        }

        static GameObject Kit(string name, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{name}.prefab");
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            go.isStatic = true;
            return go;
        }

        static void StripColliders(GameObject go)
        {
            if (go == null) return;
            // 프리팹 인스턴스라 삭제 대신 끈다
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        /// <summary>절벽 면을 바위로 덮는다 (플레이 라인 밖에만 두어 발판이 되지 않게)</summary>
        static void BuildLedgeDecor(Transform world, Terrain t)
        {
            var root = new GameObject("Ledge Rocks").transform;
            root.SetParent(world);
            foreach (float z in new[] { -4.5f, 4.2f, 6.5f })
                Kit("Cliff_04", root, Ground(t, LedgeX - 0.6f, z) + Vector3.down * 0.4f, z * 40f, 1.1f);
            foreach (float z in new[] { -5f, 4.5f })
            {
                Kit("Rock_Medium_02", root, Ground(t, PitMin - 1.2f, z), z * 30f, 1f);
                Kit("Rock_Medium_03", root, Ground(t, PitMax + 1.2f, z), z * 50f, 1f);
            }
        }

        static GameObject BuildPushLog(Transform parent, Terrain t)
        {
            var log = GameObject.CreatePrimitive(PrimitiveType.Cube);
            log.name = "PushLog (Grabbable)";
            log.transform.SetParent(parent);
            log.transform.localScale = new Vector3(1.2f, 1.2f, 1.6f);
            log.transform.position = Ground(t, 22f, 0f) + Vector3.up * 0.62f;
            var bark = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Trees/Tree_Bark.mat");
            if (bark != null) log.GetComponent<MeshRenderer>().sharedMaterial = bark;
            var rb = log.AddComponent<Rigidbody>();
            rb.mass = 20f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            log.AddComponent<Grabbable>();
            return log;
        }

        static LogBridge BuildBridge(Transform parent, Terrain t)
        {
            var root = new GameObject("Log Bridge");
            root.transform.SetParent(parent);
            Vector3 edge = Ground(t, PitMin - 0.2f, 0f);
            root.transform.position = edge;

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Log";
            plank.transform.SetParent(pivot, false);
            float span = PitMax - PitMin + 1.6f;
            plank.transform.localScale = new Vector3(span, 0.5f, 2.2f);
            plank.transform.localPosition = new Vector3(span * 0.5f, -0.2f, 0f);
            var bark = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Trees/Tree_Bark.mat");
            if (bark != null) plank.GetComponent<MeshRenderer>().sharedMaterial = bark;

            var bridge = root.AddComponent<LogBridge>();
            CombatArenaBuilder.SetField(bridge, "pivot", pivot);
            return bridge;
        }

        static RamenButton BuildButtonPillar(Transform world, Transform gimmicks, Terrain t)
        {
            const float x = 59f, z = 3.1f, height = 3.4f;
            Vector3 g = Ground(t, x, z);
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Button Pillar";
            pillar.transform.SetParent(world);
            pillar.transform.localScale = new Vector3(2.2f, height * 0.5f + 0.5f, 2.2f);
            pillar.transform.position = g + Vector3.up * (height * 0.5f - 0.5f);
            var cliff = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Rocks/Cliff.mat");
            if (cliff != null) pillar.GetComponent<MeshRenderer>().sharedMaterial = cliff;
            pillar.isStatic = true;
            return FantasyStageBuilder.BuildButton(gimmicks, g + Vector3.up * height, 0f, "Button_Bridge");
        }

        static void BuildPitHazard(Transform parent)
        {
            var go = new GameObject("Pit Hazard");
            go.transform.SetParent(parent);
            go.transform.position = new Vector3((PitMin + PitMax) * 0.5f, PitBottom + 1.5f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(PitMax - PitMin + 1f, 2.5f, 40f);
            var hz = go.AddComponent<WaterHazard>();
            CombatArenaBuilder.SetField(hz, "damage", 15f);
            var so = new SerializedObject(hz);
            so.FindProperty("message").stringValue = "떨어졌다!";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildHidingBushes(Transform parent, Terrain t)
        {
            var root = new GameObject("Hiding Bushes").transform;
            root.SetParent(parent);
            string[] bushes = { "Bush_02_02", "Bush_01_01", "Bush_02_01", "Bush_01_02", "Bush_02_02" };
            float[] xs = { 66f, 73f, 80f, 87f, 94f };
            for (int i = 0; i < xs.Length; i++)
            {
                var spot = new GameObject("HidingSpot");
                spot.transform.SetParent(root);
                spot.transform.position = Ground(t, xs[i], 0f);
                var box = spot.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2.8f, 2.2f, LaneHalf * 2f);
                box.center = new Vector3(0f, 1f, 0f);
                spot.AddComponent<HidingSpot>();

                // 덤불 2~3개로 라인 폭을 덮는다 (충돌 없음 → 안으로 들어갈 수 있음)
                foreach (float z in new[] { -1.2f, 0.4f, 1.8f })
                {
                    var b = Kit(bushes[i], spot.transform, Ground(t, xs[i] + z * 0.3f, z), i * 70f + z * 40f, 1.35f);
                    StripColliders(b);
                }
            }
        }

        // ───────────────────────── 숲 ─────────────────────────

        static void BuildForest(Transform world, Terrain t, System.Random rng)
        {
            var root = new GameObject("Forest").transform;
            root.SetParent(world);
            string[] trees = Bright
                ? new[] { "Fir_01", "Fir_02", "Fir_03", "Fir_04", "BroadleafTree_01_Green", "BroadleafTree_03_Green", "BroadleafTree_05_Green", "BlossomTree_02", "WillowTree_02_Green", "BroadleafTree_02_Red" }
                : new[] { "Fir_01", "Fir_02", "Fir_03", "Fir_04", "Fir_05" };
            string[] rocks = { "Rock_Big_01", "Rock_Big_02", "Rock_Medium_01", "Rock_Medium_03", "Rock_Small_01", "Rock_Small_03" };
            string[] ground = { "Grass_01", "Grass_02", "Grass_03", "Reeds_01", "Plants", "Branch_01", "Branch_03", "Stones_01" };
            float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            string P(string[] a) => a[rng.Next(a.Length)];

            bool GiantCorridor(float x, float z) => x > GiantMinX - 4f && x < GiantMaxX + 4f && z > GiantZ - 3.5f && z < GiantZ + 3.5f;
            bool PitArea(float x) => x > PitMin - 1.5f && x < PitMax + 1.5f;

            // 뒤쪽 빽빽한 숲 (거인 통로는 비워 손전등이 가리지 않게)
            int n = 0;
            for (int i = 0; i < 4000 && n < 230; i++)
            {
                float x = R(X0 + 2f, X0 + LengthX - 2f), z = R(LaneHalf + 2.5f, 24f);
                if (GiantCorridor(x, z) || PitArea(x)) continue;
                var go = Kit(P(trees), root, Ground(t, x, z) + Vector3.down * 0.1f, R(0, 360), R(0.9f, 1.4f));
                n++;
            }
            // 거인 통로 바로 뒤에 한 줄 더 (실루엣)
            for (float x = GiantMinX; x <= GiantMaxX; x += R(4f, 6f))
                Kit(P(trees), root, Ground(t, x, GiantZ + R(4f, 6f)), R(0, 360), R(1.1f, 1.5f));

            // 앞쪽(카메라 쪽) 전경: 낮은 풀·바위만 (나무는 카메라를 가려서 쓰지 않음)
            for (float x = X0 + 4f; x < X0 + LengthX - 4f; x += R(2.5f, 5f))
            {
                if (PitArea(x)) continue;
                var go = Kit(rng.NextDouble() < 0.25 ? "Rock_Small_02" : P(ground), root, Ground(t, x, R(-5.5f, -3.8f)), R(0, 360), R(0.9f, 1.5f));
                StripColliders(go);
            }

            // 라인 근처 바위·풀 (라인 안에는 충돌 없는 장식만)
            for (int i = 0; i < 260; i++)
            {
                float x = R(X0 + 3f, X0 + LengthX - 3f);
                if (PitArea(x) || (x > LedgeX - 1.5f && x < LedgeX + 1.5f)) continue;
                bool inLane = rng.NextDouble() < 0.45;
                float z = inLane ? R(-LaneHalf, LaneHalf) : (rng.NextDouble() < 0.5 ? R(-6f, -LaneHalf - 0.6f) : R(LaneHalf + 0.6f, 6f));
                if (GiantCorridor(x, z)) continue;
                var go = Kit(inLane ? P(ground) : (rng.NextDouble() < 0.3 ? P(rocks) : P(ground)), root, Ground(t, x, z) + Vector3.down * 0.05f, R(0, 360), inLane ? R(0.8f, 1.3f) : R(0.7f, 1.2f));
                if (inLane) StripColliders(go);
            }
        }

        /// <summary>
        /// 이 씬 전용으로 식물 색을 어둡고 탁하게 바꾼다 (원본 머티리얼은 그대로 두고 복제본을 씀).
        /// 키트 식물 셰이더는 위/아래 그라데이션 색(_Top_Color/_Bottom_Color)으로 칠해져서 밤 조명에서도 형광처럼 튄다.
        /// </summary>
        static void DarkenVegetation(Transform root)
        {
            var cache = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_Top_Color")) continue;
                    if (!cache.TryGetValue(m, out var dark))
                    {
                        string path = $"{DataFolder}/Dark_{m.name}.mat";
                        dark = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (dark == null)
                        {
                            dark = new Material(m);
                            AssetDatabase.CreateAsset(dark, path);
                        }
                        dark.CopyPropertiesFromMaterial(m);
                        dark.SetColor("_Top_Color", Gloom(m.GetColor("_Top_Color"), 0.42f));
                        dark.SetColor("_Bottom_Color", Gloom(m.GetColor("_Bottom_Color"), 0.3f));
                        EditorUtility.SetDirty(dark);
                        cache[m] = dark;
                    }
                    mats[i] = dark;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>채도를 크게 낮추고 청록 쪽으로 기울여 어둡게</summary>
        static Color Gloom(Color c, float brightness)
        {
            float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            var d = Color.Lerp(new Color(g, g, g), c, 0.35f) * brightness;
            d.b += 0.03f;
            d.a = c.a;
            return d;
        }

        // ───────────────────────── 거인 ─────────────────────────

        static void BuildGiant(Transform parent, Terrain t, Transform player)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterRigSetup.PrefabPath("Robert"));
            if (prefab == null) return;

            var root = new GameObject("Giant Watcher");
            root.transform.SetParent(parent);
            root.transform.position = Ground(t, GiantMinX, GiantZ);
            root.transform.rotation = Quaternion.LookRotation(Vector3.right);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localScale = Vector3.one * 2.8f;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var urp = AssetDatabase.LoadAssetAtPath<Material>($"{MonsterRigSetup.RobotFolder}/Materials/URP/{mats[i].name}.mat");
                    if (urp != null) mats[i] = urp;
                }
                r.sharedMaterials = mats;
            }
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.avatar = MonsterRigSetup.LoadAvatar("Robert");
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MonsterRigSetup.ControllerPath);
            anim.applyRootMotion = false;

            // 손전등 (머리 앞, 아래로 비춤)
            var lightGo = new GameObject("Flashlight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0.6f, 5.3f, 0.8f);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.95f, 0.8f);
            spot.intensity = 60f;
            spot.range = 20f;
            spot.spotAngle = 34f;
            spot.innerSpotAngle = 18f;
            spot.shadows = LightShadows.Soft;

            var watcher = root.AddComponent<GiantWatcher>();
            var so = new SerializedObject(watcher);
            so.FindProperty("pointA").vector3Value = new Vector3(GiantMinX, 0f, GiantZ);
            so.FindProperty("pointB").vector3Value = new Vector3(GiantMaxX, 0f, GiantZ);
            so.FindProperty("ground").objectReferenceValue = t;
            so.FindProperty("flashlight").objectReferenceValue = spot;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("occluders").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 스테이지 / 체크포인트 ─────────────────────────

        static void BuildStage(Transform parent, Terrain t, string title, float centerX, float width, List<Health> monsters,
            StageBarrier exit, float checkpointX, bool final)
        {
            var go = new GameObject("Stage_" + title);
            go.transform.SetParent(parent);
            go.transform.position = Ground(t, centerX, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, 10f, LaneHalf * 2f + 2f);
            box.center = new Vector3(0f, 3f, 0f);
            var cp = new GameObject("Checkpoint").transform;
            cp.SetParent(go.transform);
            cp.SetPositionAndRotation(Ground(t, checkpointX, 0f) + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.right));

            var stage = go.AddComponent<StageController>();
            var so = new SerializedObject(stage);
            so.FindProperty("stageTitle").stringValue = title;
            so.FindProperty("exitBarrier").objectReferenceValue = exit;
            so.FindProperty("checkpoint").objectReferenceValue = cp;
            so.FindProperty("isFinalStage").boolValue = final;
            var arr = so.FindProperty("monsters");
            arr.arraySize = monsters.Count;
            for (int i = 0; i < monsters.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = monsters[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildCheckpoint(Transform parent, Terrain t, float x, string message)
        {
            var go = new GameObject($"Checkpoint_{x:F0}");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(Ground(t, x, 0f) + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.right));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 6f, LaneHalf * 2f + 1f);
            box.center = new Vector3(0f, 2f, 0f);
            var cp = go.AddComponent<Checkpoint>();
            var so = new SerializedObject(cp);
            so.FindProperty("message").stringValue = message;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 카메라 / 조명 ─────────────────────────

        static Camera BuildSideCamera(Transform player)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 220f;
            cam.clearFlags = Bright ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.045f, 0.07f);
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var follow = go.AddComponent<SideFollowCamera>();
            CombatArenaBuilder.SetField(follow, "target", player);
            CombatArenaBuilder.SetField(follow, "offset", new Vector3(0f, 2.6f, -10f));
            CombatArenaBuilder.SetField(follow, "lookOffset", new Vector3(0f, 1.1f, 0f));
            CombatArenaBuilder.SetField(follow, "lookAhead", 1.6f);
            go.transform.position = player.position + new Vector3(0f, 2.6f, -10f);
            go.transform.LookAt(player.position + Vector3.up * 1.1f);
            return cam;
        }

        /// <summary>밝은 낮: 키트 하늘, 따뜻한 햇빛, 옅은 안개, 가벼운 보정</summary>
        static void BuildDayLighting(Transform world)
        {
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Skybox/Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.92f);
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 120f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.82f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.sun = sun;

            string path = DataFolder + "/DarkForest_Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                if (c != null) Object.DestroyImmediate(c, true);
            }
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.4f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0f);
            ca.contrast.Override(6f);
            ca.saturation.Override(0f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.18f);
            foreach (var c in profile.components)
                if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static void BuildLighting(Transform world, Terrain t)
        {
            if (Bright)
            {
                BuildDayLighting(world);
                return;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.13f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.075f, 0.1f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.025f, 0.03f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.1f);
            RenderSettings.fogDensity = 0.045f;

            // 달빛: 뒤쪽 위에서 비춰 실루엣과 림을 만든다
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.66f, 0.95f);
            moon.intensity = 0.55f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(32f, 200f, 0f);
            RenderSettings.sun = moon;

            // 길을 읽을 수 있게 따뜻한 등불 몇 개
            var lamps = new GameObject("Lanterns").transform;
            lamps.SetParent(world);
            foreach (float x in new[] { 4f, 26f, 40f, 48f, 61f, 104f, 116f, 133f })
            {
                var l = new GameObject("Lantern").AddComponent<Light>();
                l.transform.SetParent(lamps);
                l.transform.position = Ground(t, x, 1.8f) + Vector3.up * 2.2f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.68f, 0.38f);
                l.intensity = 4f;
                l.range = 7f;
                l.shadows = LightShadows.None;
            }

            // 반딧불
            var ff = new GameObject("Fireflies");
            ff.transform.position = new Vector3(X0 + LengthX * 0.5f, 3f, 1f);
            var ps = ff.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 9f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = new Color(0.8f, 1f, 0.45f) * 3f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 30f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(LengthX, 5f, 12f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0.2f, 0.6f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ff.GetComponent<ParticleSystemRenderer>().sharedMaterial = CombatArenaBuilder.AdditiveParticle("Firefly", Color.white);

            // 포스트: 저채도·차가운 색·비네팅·필름 그레인
            string path = DataFolder + "/DarkForest_Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                if (c != null) Object.DestroyImmediate(c, true);
            }
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.7f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.45f);
            ca.contrast.Override(18f);
            ca.saturation.Override(-38f);
            ca.colorFilter.Override(new Color(0.84f, 0.92f, 1f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.45f);
            vig.smoothness.Override(0.5f);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0.3f);
            foreach (var c in profile.components)
                if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }
    }
}
