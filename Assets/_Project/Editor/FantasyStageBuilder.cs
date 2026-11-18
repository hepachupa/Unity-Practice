using System.Collections.Generic;
using System.Linq;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Companion;
using LNStudy.Gimmicks;
using LNStudy.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// Idyllic Fantasy Nature 에셋으로 큰 야외 맵(Fantasy_Stages)을 만든다. 여러 번 실행해도 같은 결과(고정 시드).
    ///
    /// 구성 (260×260m 지형을 코드로 조각):
    ///   캠프(시작) → [1] 꽃의 들판 → 장벽 → [2] 거울 호수 → 장벽 → [3] 바람의 고원 → 장벽 → 성소(클리어)
    /// - 스테이지 사이 길은 산으로 막혀 있고, 장벽은 스테이지 클리어 조건을 채워야 열린다.
    /// - 라멘 버튼 기믹 (시사로 교체해서 좌클릭 꾹 → 던지기):
    ///   [1] 높은 바위기둥 위 버튼 → 장벽 해제 조건
    ///   [2] 호수 가운데 섬의 버튼 → 물속 징검다리가 떠오름 (물에 빠지면 체크포인트로)
    ///   [3] 양쪽 기둥 위 버튼 2개를 6초 안에 모두 맞혀야 성소 장벽 해제
    /// - 난이도: 몬스터 체력·공격력·추격 속도·시야 상향, 공격 예비동작 단축, 부활 없음, 마지막 스테이지에 엘리트
    /// </summary>
    public static class FantasyStageBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Fantasy_Stages.unity";
        const string DataFolder = "Assets/_Project/Scenes/Fantasy_Stages";
        const string KitPrefabs = "Assets/Idyllic Fantasy Nature/Prefabs";
        const string KitFolder = "Assets/Idyllic Fantasy Nature";
        const string CombatArenaScene = "Assets/_Project/Scenes/Combat_Arena.unity";

        const float Size = 260f;
        const float MaxHeight = 50f;
        const int HeightRes = 513;
        const int AlphaRes = 512;
        const float FloorH = 6f;
        const float PlateauH = 12f;
        const float WaterLevel = 4.6f;
        const int Seed = 20261117;

        // ───────────────────────── 레이아웃 ─────────────────────────

        struct Area
        {
            public string name;
            public Vector2 a, b;      // 캡슐 양 끝 (원이면 a == b)
            public float radius;
            public float hA, hB;      // 바닥 높이 (a → b 보간, 경사로용)
            public float blend;       // 산으로 이어지는 폭
            public bool isPath;
        }

        static readonly Area[] Areas =
        {
            new Area { name = "Camp", a = V(40, 45), b = V(40, 45), radius = 15, hA = FloorH, hB = FloorH, blend = 9 },
            new Area { name = "PathCamp", a = V(48, 53), b = V(72, 80), radius = 4.5f, hA = FloorH, hB = FloorH, blend = 6, isPath = true },
            new Area { name = "Stage1", a = V(85, 95), b = V(85, 95), radius = 26, hA = FloorH, hB = FloorH, blend = 9 },
            new Area { name = "Path12", a = V(104, 104), b = V(124, 114), radius = 4.5f, hA = FloorH, hB = FloorH, blend = 6, isPath = true },
            new Area { name = "Stage2", a = V(120, 116), b = V(182, 124), radius = 17, hA = FloorH, hB = FloorH, blend = 9 },
            new Area { name = "Path23", a = V(178, 128), b = V(198, 168), radius = 4.5f, hA = FloorH, hB = PlateauH, blend = 6, isPath = true },
            new Area { name = "Stage3", a = V(205, 190), b = V(205, 190), radius = 26, hA = PlateauH, hB = PlateauH, blend = 9 },
            new Area { name = "Shrine", a = V(222, 211), b = V(222, 211), radius = 7, hA = PlateauH, hB = PlateauH, blend = 5 },
        };

        // 호수: Stage2 골짜기의 가운데를 가로로 꽉 채워서 돌아갈 길이 없다
        const float LakeMinX = 138f, LakeMaxX = 162f, LakeBlend = 1.6f, LakeBedH = 0.5f;
        static readonly Vector2 Island = V(150, LakeCenterZ(150));
        const float IslandRadius = 3.5f, IslandH = 6.3f;

        static Vector2 V(float x, float z) => new Vector2(x, z);
        static float LakeCenterZ(float x) => Mathf.Lerp(116f, 124f, Mathf.InverseLerp(120f, 182f, x));

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem("LNStudy/Fantasy Stages/Build Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) return;

            // 0) 준비
            int enemyLayer = CombatArenaBuilder.EnsureLayer("Enemy");
            int playerLayer = LayerMask.NameToLayer("Player");
            var humanoids = MonsterRigSetup.SetupAll();
            CombatArenaBuilder.EnsureFolder(DataFolder);

            // 펫 드론 선택은 전투 씬에서 가져온다 (직접 바꾼 드론·크기 유지)
            EditorSceneManager.SaveOpenScenes();
            if (System.IO.File.Exists(CombatArenaScene)) EditorSceneManager.OpenScene(CombatArenaScene);
            string petPrefab = CombatArenaBuilder.ResolvePetPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rng = new System.Random(Seed);

            // 1) 지형
            var world = new GameObject("World");
            var terrain = BuildTerrain(world.transform);
            var dynamicRoot = new GameObject("Gimmicks").transform; // 네비메시에 굽지 않는 움직이는 것들

            // 2) 물 + 징검다리
            BuildLake(world.transform, dynamicRoot, terrain, out var platforms);

            // 3) 장벽
            var b1 = BuildBarrier(dynamicRoot, terrain, V(114, 109), V(20, 10), 11f, "Barrier_1to2");
            var b2 = BuildBarrier(dynamicRoot, terrain, V(181.5f, 135f), V(20, 40), 11f, "Barrier_2to3");
            var b3 = BuildBarrier(dynamicRoot, terrain, V(220.8f, 209.4f), V(17, 21), 14f, "Barrier_Shrine");

            // 4) 기믹 기둥·버튼 (기둥은 정적 → 네비메시에 포함)
            var pillars = new GameObject("Pillars").transform;
            pillars.SetParent(world.transform);
            var btn1 = BuildPillarButton(pillars, terrain, V(101, 86), 6f, 0f, "Button_Stage1");
            var btn2 = BuildIslandButton(dynamicRoot, terrain);
            var btn3a = BuildPillarButton(pillars, terrain, V(196, 199), 5f, 6f, "Button_Stage3_A");
            var btn3b = BuildPillarButton(pillars, terrain, V(214, 181), 5f, 6f, "Button_Stage3_B");

            // 5) 식생·바위
            ScatterNature(world.transform, terrain, rng);
            BuildShrineDecor(world.transform, terrain);

            // 6) 조명
            BuildLighting();

            // 7) 네비메시 (지형 + 정적 소품)
            BakeNavMesh(world);

            // 8) 플레이어
            Vector3 spawn = Ground(terrain, V(36, 40));
            float spawnYaw = Mathf.Atan2(85 - 36, 95 - 40) * Mathf.Rad2Deg;
            var player = CombatArenaBuilder.BuildPlayer(playerLayer, out var unityChanAnimator);
            player.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, spawnYaw, 0f));
            var cam = CombatArenaBuilder.BuildCamera(player.transform);
            cam.farClipPlane = 600f;
            cam.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
            CombatArenaBuilder.SetField(cam.GetComponent<ThirdPersonCamera>(), "startYaw", spawnYaw);
            CombatArenaBuilder.SetField(cam.GetComponent<ThirdPersonCamera>(), "collisionMask", (LayerMask)1);
            CombatArenaBuilder.SetField(player.GetComponent<PlayerMotor>(), "moveSpace", cam.transform);
            var petL = CombatArenaBuilder.BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Left);
            var petR = CombatArenaBuilder.BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Right);
            CombatArenaBuilder.SetupPlayerCombat(player, cam, new[] { petL, petR }, enemyLayer);
            CombatArenaBuilder.SetupShisaAndSwitcher(player, new[] { petL, petR }, cam);

            // 9) 몬스터 + 스테이지
            var monsters = new GameObject("Monsters").transform;
            var s1Monsters = SpawnGroup(monsters, terrain, humanoids, enemyLayer, player.transform, "S1", V(84, 96), V(70, 82), new[]
            {
                ("Robert", V(-6, 4)), ("Paperman", V(5, 7)), ("Engie", V(8, -5)), ("Robert", V(-3, -8)),
            });
            var s2Monsters = SpawnGroup(monsters, terrain, humanoids, enemyLayer, player.transform, "S2", V(172, 123), V(130, 118), new[]
            {
                ("Engie", V(-3, -6)), ("Paperman", V(2, 6)), ("Robert", V(6, -1)), ("Paperman", V(-4, 3)), ("Robert", V(9, 7)),
            });
            var s3Monsters = SpawnGroup(monsters, terrain, humanoids, enemyLayer, player.transform, "S3", V(207, 192), V(196, 168), new[]
            {
                ("Robert", V(-9, 3)), ("Paperman", V(7, 9)), ("Engie", V(10, -6)), ("Robert", V(-4, -10)), ("Paperman", V(-11, -4)),
            });
            var elite = SpawnMonster(monsters, terrain, humanoids, enemyLayer, player.transform, "Engie", V(212, 199), V(196, 168), elite: true);
            if (elite != null) s3Monsters.Add(elite);

            var stages = new GameObject("Stages").transform;
            var s1 = BuildStage(stages, terrain, "1. 꽃의 들판", "높은 바위기둥 위 버튼을 라멘으로 맞혀라 (Tab → 시사, 좌클릭 꾹)",
                V(85, 95), 54f, s1Monsters, true, b1, V(72, 81), V(85, 95), false);
            UnityEventTools.AddPersistentListener(btn1.onPressed, s1.MarkGimmickDone);

            var s2 = BuildStage(stages, terrain, "2. 거울 호수", "호수 가운데 섬의 버튼을 라멘으로 맞혀 길을 만들어라",
                V(151, 120), 70f, s2Monsters, true, b2, V(128, 116), V(170, 122), false, depth: 40f);
            UnityEventTools.AddPersistentListener(btn2.onPressed, platforms.Activate);
            UnityEventTools.AddPersistentListener(btn2.onPressed, s2.MarkGimmickDone);

            var s3 = BuildStage(stages, terrain, "3. 바람의 고원", "양쪽 기둥 위 버튼 두 개를 6초 안에 모두 맞혀라",
                V(205, 190), 54f, s3Monsters, true, b3, V(196, 167), V(205, 190), false);
            var group = new GameObject("ButtonGroup_Stage3").AddComponent<ButtonGroup>();
            group.transform.SetParent(stages);
            var gso = new SerializedObject(group);
            var arr = gso.FindProperty("buttons");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = btn3a;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = btn3b;
            gso.ApplyModifiedPropertiesWithoutUndo();
            UnityEventTools.AddPersistentListener(group.onAllPressed, s3.MarkGimmickDone);

            BuildStage(stages, terrain, "성소", "", V(224, 213), 12f, new List<Health>(), false, null, V(221, 210), V(226, 215), true);

            // 10) HUD
            var hud = new GameObject("HUD").AddComponent<StageHUD>();
            CombatArenaBuilder.SetField(hud, "playerHealth", player.GetComponent<Health>());
            CombatArenaBuilder.SetField(hud, "switcher", player.GetComponent<CharacterSwitcher>());
            var hso = new SerializedObject(hud);
            hso.FindProperty("openingBanner").stringValue = "꽃의 섬";
            hso.FindProperty("openingToast").stringValue = "길을 따라 첫 번째 스테이지로 가자 · Tab으로 시사와 교체할 수 있어";
            hso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            CombatArenaBuilder.AddToBuildSettings(ScenePath);
            Debug.Log($"[FantasyStageBuilder] 완료: {ScenePath} (몬스터 {s1Monsters.Count + s2Monsters.Count + s3Monsters.Count})");
        }

        // ───────────────────────── 지형 ─────────────────────────

        static float Playable(Vector2 p, out Area best, out float floor)
        {
            float w = 0f;
            best = Areas[0];
            floor = FloorH;
            foreach (var a in Areas)
            {
                float d = DistToSegment(p, a.a, a.b, out float t);
                float wi = 1f - Smooth01((d - a.radius) / a.blend);
                if (wi > w + 1e-4f || (Mathf.Abs(wi - w) < 1e-4f && a.isPath))
                {
                    w = wi;
                    best = a;
                    floor = Mathf.Lerp(a.hA, a.hB, t);
                }
            }
            return w;
        }

        static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        static float LakeWeight(Vector2 p)
        {
            if (DistToSegment(p, Areas[4].a, Areas[4].b, out _) > Areas[4].radius + Areas[4].blend) return 0f;
            float inside = Mathf.Min(p.x - LakeMinX, LakeMaxX - p.x); // 양수 = 호수 안
            return Smooth01((inside + LakeBlend) / LakeBlend);
        }

        static float HeightAt(float x, float z)
        {
            var p = V(x, z);
            float mountain = 22f + 10f * Mathf.PerlinNoise(x * 0.018f + 3.1f, z * 0.018f + 7.7f)
                                 + 3f * Mathf.PerlinNoise(x * 0.07f, z * 0.07f);
            float w = Playable(p, out _, out float floor);
            floor += 0.35f * (Mathf.PerlinNoise(x * 0.11f + 11f, z * 0.11f + 5f) - 0.5f);
            float h = Mathf.Lerp(mountain, floor, w);

            float lake = LakeWeight(p);
            if (lake > 0f) h = Mathf.Lerp(h, LakeBedH, lake);

            float island = 1f - Smooth01((Vector2.Distance(p, Island) - IslandRadius) / 1.4f);
            if (island > 0f) h = Mathf.Lerp(h, IslandH, island);

            // 맵 가장자리는 높게 (밖으로 못 나가게)
            float edge = Mathf.Min(Mathf.Min(x, Size - x), Mathf.Min(z, Size - z));
            if (edge < 12f) h = Mathf.Max(h, Mathf.Lerp(34f, h, edge / 12f));
            return h;
        }

        static Terrain BuildTerrain(Transform parent)
        {
            string tdPath = DataFolder + "/Fantasy_Terrain.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null)
            {
                td = new TerrainData();
                AssetDatabase.CreateAsset(td, tdPath);
            }
            td.heightmapResolution = HeightRes;
            td.alphamapResolution = AlphaRes;
            td.size = new Vector3(Size, MaxHeight, Size);

            var heights = new float[HeightRes, HeightRes];
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = xi / (float)(HeightRes - 1) * Size;
                    float z = zi / (float)(HeightRes - 1) * Size;
                    heights[zi, xi] = Mathf.Clamp01(HeightAt(x, z) / MaxHeight);
                }
            td.SetHeights(0, 0, heights);

            // 텍스처 레이어: 풀 / 흙길 / 바위 / 모래 / 숲 / 자갈
            string[] layerNames = { "Grass_Layer", "Dirt_Layer", "Rock_Layer", "Sand_Layer", "Forest_Layer", "Cobblestone_Layer" };
            var layers = layerNames.Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{KitFolder}/Terrain Layer/{n}.terrainlayer")).ToArray();
            td.terrainLayers = layers.Where(l => l != null).ToArray();
            int L = td.terrainLayers.Length;

            var alpha = new float[AlphaRes, AlphaRes, L];
            for (int zi = 0; zi < AlphaRes; zi++)
                for (int xi = 0; xi < AlphaRes; xi++)
                {
                    float nx = xi / (float)(AlphaRes - 1), nz = zi / (float)(AlphaRes - 1);
                    var p = V(nx * Size, nz * Size);
                    float steep = td.GetSteepness(nx, nz);
                    float play = Playable(p, out var area, out _);

                    float rock = Smooth01((steep - 26f) / 14f);
                    float path = 0f;
                    foreach (var a in Areas)
                        if (a.isPath) path = Mathf.Max(path, 1f - Smooth01((DistToSegment(p, a.a, a.b, out _) - a.radius * 0.55f) / 2.5f));
                    float sand = 0f;
                    if (DistToSegment(p, Areas[4].a, Areas[4].b, out _) < Areas[4].radius + Areas[4].blend)
                    {
                        float edgeDist = Mathf.Max(LakeMinX - p.x, p.x - LakeMaxX); // 호수 안이면 음수
                        sand = 1f - Smooth01((edgeDist - 1.5f) / 3f);
                    }
                    float shrine = 1f - Smooth01((Vector2.Distance(p, V(222, 211)) - 6f) / 2f);
                    float forest = (1f - play) * (1f - rock);

                    float[] wts = new float[6];
                    wts[0] = play * (1f - path) * (1f - sand) * (1f - shrine);
                    wts[1] = path * play;
                    wts[2] = rock;
                    wts[3] = sand * play;
                    wts[4] = forest;
                    wts[5] = shrine * play;
                    // 레이어 일부가 없으면 앞쪽 레이어로 합침
                    float sum = 0f;
                    for (int l = 0; l < L; l++) sum += wts[l];
                    for (int l = L; l < 6; l++) wts[0] += wts[l];
                    if (sum <= 1e-4f) { wts[0] = 1f; sum = 1f; }
                    for (int l = 0; l < L; l++) alpha[zi, xi, l] = wts[l] / sum;
                }
            td.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            var litShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (litShader != null)
            {
                string matPath = DataFolder + "/Fantasy_TerrainLit.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(litShader); AssetDatabase.CreateAsset(mat, matPath); }
                terrain.materialTemplate = mat;
            }
            return terrain;
        }

        static Vector3 Ground(Terrain t, Vector2 p) => new Vector3(p.x, t.SampleHeight(new Vector3(p.x, 0f, p.y)) + t.transform.position.y, p.y);

        // ───────────────────────── 호수 / 징검다리 ─────────────────────────

        static void BuildLake(Transform world, Transform dynamicRoot, Terrain terrain, out RisingPlatforms platforms)
        {
            float cx = (LakeMinX + LakeMaxX) * 0.5f;
            float cz = LakeCenterZ(cx);

            var waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/Water.prefab");
            if (waterPrefab != null)
            {
                var water = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab, world);
                water.name = "Lake Water";
                water.transform.position = new Vector3(cx, WaterLevel, cz);
                water.transform.localScale = new Vector3((LakeMaxX - LakeMinX + 6f) / 10f, 1f, 4.4f);
                foreach (var c in water.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }

            // 물에 빠지면 체크포인트로
            var hazard = new GameObject("Water Hazard");
            hazard.transform.SetParent(dynamicRoot);
            hazard.transform.position = new Vector3(cx, WaterLevel - 2.6f, cz);
            var box = hazard.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(LakeMaxX - LakeMinX + 2f, 4f, 44f);
            hazard.AddComponent<WaterHazard>();

            // 징검다리 (평소엔 물속, 섬 버튼을 맞히면 떠오름)
            var root = new GameObject("Stepping Stones");
            root.transform.SetParent(dynamicRoot);
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Rocks/Cliff.mat");
            var list = new List<Transform>();
            foreach (float x in new[] { 140.6f, 144.6f, 155.4f, 159.4f, 162.6f })
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                s.name = "Stone";
                s.transform.SetParent(root.transform);
                s.transform.localScale = new Vector3(2.8f, 0.6f, 2.8f);   // 지름 2.8, 높이 1.2
                s.transform.position = new Vector3(x, FloorH + 0.15f - 0.6f, LakeCenterZ(x));
                if (stoneMat != null) s.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
                list.Add(s.transform);
            }
            platforms = root.AddComponent<RisingPlatforms>();
            var so = new SerializedObject(platforms);
            var arr = so.FindProperty("platforms");
            arr.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            so.FindProperty("sunkenDepth").floatValue = 4.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 장벽 / 버튼 / 스테이지 ─────────────────────────

        static Material BarrierMaterial()
        {
            string path = DataFolder + "/Barrier.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(0.4f, 0.85f, 1f, 0.35f) * new Color(2f, 2f, 2f, 1f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static StageBarrier BuildBarrier(Transform parent, Terrain terrain, Vector2 at, Vector2 pathDir, float width, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            Vector3 pos = Ground(terrain, at);
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(pathDir.x, 0f, pathDir.y)));

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = new Vector3(0f, 3f, 0f);
            wall.transform.localScale = new Vector3(width, 9f, 0.5f);
            var mr = wall.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BarrierMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(width, 6f, 1f);
            obstacle.center = new Vector3(0f, 3f, 0f);
            obstacle.carving = true;

            foreach (float x in new[] { -width * 0.3f, width * 0.3f })
            {
                var l = new GameObject("Glow").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                l.transform.localPosition = new Vector3(x, 2f, -0.8f);
                l.type = LightType.Point;
                l.color = new Color(0.45f, 0.85f, 1f);
                l.intensity = 3f;
                l.range = 7f;
                l.shadows = LightShadows.None;
            }
            return root.AddComponent<StageBarrier>();
        }

        static Material ButtonMaterial()
        {
            string path = DataFolder + "/RamenButton.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 3f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>버튼 본체 (받침 + 누르는 판 + 불빛 + 위치 알림 빛기둥)</summary>
        internal static RamenButton BuildButton(Transform parent, Vector3 pos, float resetAfter, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = pos;

            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basePlate.name = "Base";
            basePlate.transform.SetParent(root.transform, false);
            basePlate.transform.localScale = new Vector3(1.9f, 0.12f, 1.9f);
            basePlate.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            var cliff = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Rocks/Cliff.mat");
            if (cliff != null) basePlate.GetComponent<MeshRenderer>().sharedMaterial = cliff;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = "Plate";
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(root.transform, false);
            plate.transform.localScale = new Vector3(1.3f, 0.08f, 1.3f);
            plate.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = ButtonMaterial();

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 1f, 0f);
            light.type = LightType.Point;
            light.range = 6f;
            light.shadows = LightShadows.None;

            // 빛기둥 (멀리서도 보이게)
            var beacon = new GameObject("Beacon");
            beacon.transform.SetParent(root.transform, false);
            var lr = beacon.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPositions(new[] { new Vector3(0f, 0.3f, 0f), new Vector3(0f, 14f, 0f) });
            lr.widthMultiplier = 0.35f;
            var grad = new Gradient();
            var c = new Color(1f, 0.6f, 0.2f);
            grad.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                         new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = grad;
            lr.sharedMaterial = CombatArenaBuilder.AdditiveParticle("ButtonBeacon", Color.white);
            lr.shadowCastingMode = ShadowCastingMode.Off;

            var btn = root.AddComponent<RamenButton>();
            var so = new SerializedObject(btn);
            so.FindProperty("indicator").objectReferenceValue = plate.GetComponent<MeshRenderer>();
            so.FindProperty("indicatorLight").objectReferenceValue = light;
            so.FindProperty("plate").objectReferenceValue = plate.transform;
            so.FindProperty("idleBeacon").objectReferenceValue = beacon;
            so.FindProperty("resetAfter").floatValue = resetAfter;
            so.FindProperty("hitRadius").floatValue = 1.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return btn;
        }

        /// <summary>올라갈 수 없는 바위기둥 위 버튼 (라멘을 위로 던져야 함)</summary>
        static RamenButton BuildPillarButton(Transform parent, Terrain terrain, Vector2 at, float height, float resetAfter, string name)
        {
            Vector3 g = Ground(terrain, at);
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = name + "_Pillar";
            pillar.transform.SetParent(parent);
            pillar.transform.localScale = new Vector3(2.6f, height * 0.5f + 0.5f, 2.6f);
            pillar.transform.position = g + Vector3.up * (height * 0.5f - 0.5f);
            var cliff = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Rocks/Cliff.mat");
            if (cliff != null) pillar.GetComponent<MeshRenderer>().sharedMaterial = cliff;
            pillar.isStatic = true;

            // 기둥 밑둥을 바위로 감싸 자연스럽게
            var rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/Rock_Small_02.prefab");
            if (rockPrefab != null)
                for (int i = 0; i < 3; i++)
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, parent);
                    float a = i * 120f * Mathf.Deg2Rad;
                    r.transform.position = g + new Vector3(Mathf.Cos(a), -0.2f, Mathf.Sin(a)) * 1.5f;
                    r.transform.rotation = Quaternion.Euler(0f, i * 97f, 0f);
                }

            return BuildButton(parent, g + Vector3.up * height, resetAfter, name);
        }

        static RamenButton BuildIslandButton(Transform parent, Terrain terrain)
        {
            Vector3 g = Ground(terrain, Island);
            var btn = BuildButton(parent, g, 0f, "Button_Stage2_Island");
            var flower = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/BlossomTree_02.prefab");
            if (flower != null)
            {
                var t = (GameObject)PrefabUtility.InstantiatePrefab(flower, parent);
                t.transform.position = g + new Vector3(1.8f, -0.1f, 1.6f);
                t.transform.localScale = Vector3.one * 0.6f;
            }
            return btn;
        }

        static StageController BuildStage(Transform parent, Terrain terrain, string title, string hint, Vector2 center, float size,
            List<Health> monsters, bool requiresGimmick, StageBarrier exit, Vector2 checkpointAt, Vector2 lookAt, bool final, float depth = -1f)
        {
            var go = new GameObject("Stage_" + title);
            go.transform.SetParent(parent);
            Vector3 c = Ground(terrain, center);
            go.transform.position = c;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size, 14f, depth > 0f ? depth : size);
            box.center = new Vector3(0f, 4f, 0f);

            var cp = new GameObject("Checkpoint").transform;
            cp.SetParent(go.transform);
            Vector3 cpPos = Ground(terrain, checkpointAt);
            Vector3 look = Ground(terrain, lookAt) - cpPos;
            look.y = 0f;
            cp.SetPositionAndRotation(cpPos + Vector3.up * 0.1f, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));

            var stage = go.AddComponent<StageController>();
            var so = new SerializedObject(stage);
            so.FindProperty("stageTitle").stringValue = title;
            so.FindProperty("gimmickHint").stringValue = hint;
            so.FindProperty("requiresGimmick").boolValue = requiresGimmick;
            so.FindProperty("exitBarrier").objectReferenceValue = exit;
            so.FindProperty("checkpoint").objectReferenceValue = cp;
            so.FindProperty("isFinalStage").boolValue = final;
            var arr = so.FindProperty("monsters");
            arr.arraySize = monsters.Count;
            for (int i = 0; i < monsters.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = monsters[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return stage;
        }

        // ───────────────────────── 몬스터 ─────────────────────────

        static List<Health> SpawnGroup(Transform parent, Terrain terrain, List<string> humanoids, int enemyLayer, Transform player,
            string group, Vector2 center, Vector2 faceToward, (string robot, Vector2 offset)[] list)
        {
            var result = new List<Health>();
            foreach (var (robot, offset) in list)
            {
                var h = SpawnMonster(parent, terrain, humanoids, enemyLayer, player, robot, center + offset, faceToward, false);
                if (h != null) result.Add(h);
            }
            return result;
        }

        static Health SpawnMonster(Transform parent, Terrain terrain, List<string> humanoids, int enemyLayer, Transform player,
            string robot, Vector2 at, Vector2 faceToward, bool elite)
        {
            if (!humanoids.Contains(robot)) return null;
            Vector3 pos = Ground(terrain, at);
            // 나무·바위와 겹치지 않게 가장 가까운 네비메시 위로
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            Vector2 d = faceToward - at;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            var go = CombatArenaBuilder.BuildMonster(robot, pos, yaw, enemyLayer, player, parent);
            if (go == null) return null;

            // 난이도 상향 (기존 전투 씬 대비)
            var brain = go.GetComponent<MonsterBrain>();
            var health = go.GetComponent<Health>();
            CombatArenaBuilder.SetField(health, "maxHealth", elite ? 420f : 160f);
            CombatArenaBuilder.SetField(brain, "viewDistance", 14f);
            CombatArenaBuilder.SetField(brain, "hearDistance", 4f);
            CombatArenaBuilder.SetField(brain, "chaseSpeed", elite ? 3.4f : 3.9f);
            CombatArenaBuilder.SetField(brain, "attackDamage", elite ? 26f : 16f);
            CombatArenaBuilder.SetField(brain, "attackWindup", elite ? 0.6f : 0.45f);
            CombatArenaBuilder.SetField(brain, "attackCooldown", elite ? 1.6f : 1.3f);
            CombatArenaBuilder.SetField(brain, "attackKnockback", elite ? 6f : 3.5f);
            CombatArenaBuilder.SetField(brain, "leashDistance", 30f);
            CombatArenaBuilder.SetField(brain, "patrolRadius", 7f);
            CombatArenaBuilder.SetField(brain, "respawnAfterDeath", false);
            if (elite)
            {
                go.name += "_Elite";
                go.transform.localScale = Vector3.one * 1.35f;
                CombatArenaBuilder.SetField(health, "aimHeight", 1.75f);
                CombatArenaBuilder.SetField(brain, "attackRange", 2.5f);
                CombatArenaBuilder.SetField(brain, "attackHitRange", 3.1f);
                CombatArenaBuilder.SetField(go.GetComponent<HealthBar>(), "height", 3.3f);
                CombatArenaBuilder.SetField(go.GetComponent<HealthBar>(), "size", new Vector2(1.4f, 0.11f));
            }
            return health;
        }

        // ───────────────────────── 식생 ─────────────────────────

        static readonly string[] MountainTrees =
        {
            "Fir_01", "Fir_02", "Fir_03", "Fir_04", "Fir_05",
            "BroadleafTree_01_Green", "BroadleafTree_02_Green", "BroadleafTree_03_Green", "BroadleafTree_04_Green", "BroadleafTree_05_Green",
            "BroadleafTree_02_Red", "BroadleafTree_04_Purple", "WillowTree_01_Green", "WillowTree_03_Blue",
        };
        static readonly string[] StageTrees =
        {
            "BlossomTree_01", "BlossomTree_03", "BlossomTree_05", "BroadleafTree_01_Red", "WillowTree_02_Pink", "BroadleafTree_03_Purple",
        };
        static readonly string[] Bushes = { "Bush_01_01", "Bush_01_02", "Bush_02_01", "Bush_02_02", "Bush_03_01", "Bush_03_02" };
        static readonly string[] Flowers =
        {
            "FlowerMeadow_Blue", "FlowerMeadow_Pink", "FlowerMeadow_Purple", "FlowerMeadow_RedOrange", "FlowerMeadow_White",
            "FlowerMeadow_OrangePinkRedPurpleBlue", "Flower_Yellow", "Flower_Pink", "Flower_Blue_01", "Flower_White",
        };
        static readonly string[] Grass = { "Grass_01", "Grass_02", "Grass_03", "Plants" };
        static readonly string[] EdgeRocks = { "Rock_Big_01", "Rock_Big_02", "Rock_Big_03", "Rock_Medium_01", "Rock_Medium_02", "Rock_Medium_03" };

        /// <summary>식생을 피해야 하는 핵심 위치 (스폰·버튼·장벽·호수)</summary>
        static readonly (Vector2 p, float r)[] KeepClear =
        {
            (V(36, 40), 5), (V(101, 86), 4), (V(196, 199), 4), (V(214, 181), 4), (V(114, 109), 7), (V(181.5f, 135f), 7),
            (V(220.8f, 209.4f), 8), (V(150, 120), 6), (V(85, 95), 6), (V(205, 190), 7), (V(222, 211), 6),
        };

        static GameObject Place(string name, Transform parent, Terrain t, Vector2 p, System.Random rng, float minScale, float maxScale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{name}.prefab");
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = Ground(t, p) + Vector3.down * 0.05f;
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
            go.isStatic = true;
            return go;
        }

        static bool Clear(Vector2 p, float extra)
        {
            foreach (var (k, r) in KeepClear) if (Vector2.Distance(p, k) < r + extra) return false;
            foreach (var a in Areas)
                if (a.isPath && DistToSegment(p, a.a, a.b, out _) < a.radius + 0.5f) return false;
            if (LakeWeight(p) > 0.01f) return false;
            return true;
        }

        static float Steep(TerrainData td, Vector2 p) => td.GetSteepness(p.x / Size, p.y / Size);

        static void ScatterNature(Transform world, Terrain t, System.Random rng)
        {
            var root = new GameObject("Nature").transform;
            root.SetParent(world);
            var trees = new GameObject("Trees").transform; trees.SetParent(root);
            var rocks = new GameObject("Rocks").transform; rocks.SetParent(root);
            var deco = new GameObject("Decoration").transform; deco.SetParent(root);
            var td = t.terrainData;

            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            string Pick(string[] arr) => arr[rng.Next(arr.Length)];

            // 산 비탈 숲: 플레이 영역 밖, 너무 가파르지 않은 곳
            int placed = 0;
            for (int i = 0; i < 6000 && placed < 320; i++)
            {
                var p = V(Rand(8, Size - 8), Rand(8, Size - 8));
                float w = Playable(p, out _, out _);
                if (w > 0.15f) continue;
                float steep = td.GetSteepness(p.x / Size, p.y / Size);
                if (steep > 34f) continue;
                Place(Pick(MountainTrees), trees, t, p, rng, 0.9f, 1.35f);
                placed++;
            }

            // 플레이 영역 가장자리 바위 (경계를 자연스럽게)
            placed = 0;
            for (int i = 0; i < 4000 && placed < 70; i++)
            {
                var p = V(Rand(10, Size - 10), Rand(10, Size - 10));
                float w = Playable(p, out var a, out _);
                if (w < 0.25f || w > 0.7f || a.isPath || !Clear(p, 3f) || Steep(td, p) > 30f) continue;
                var rock = Place(Pick(EdgeRocks), rocks, t, p, rng, 0.7f, 1.2f);
                if (rock != null) rock.transform.position += Vector3.down * 0.6f; // 경사면에 떠 보이지 않게 묻는다
                placed++;
            }

            // 스테이지 안 엄폐용 나무·덤불
            placed = 0;
            for (int i = 0; i < 4000 && placed < 22; i++)
            {
                var p = V(Rand(20, Size - 20), Rand(20, Size - 20));
                float w = Playable(p, out var a, out _);
                if (w < 0.98f || a.isPath || !Clear(p, 6f) || Steep(td, p) > 18f) continue;
                Place(Pick(StageTrees), trees, t, p, rng, 0.85f, 1.1f);
                placed++;
            }
            placed = 0;
            for (int i = 0; i < 4000 && placed < 60; i++)
            {
                var p = V(Rand(20, Size - 20), Rand(20, Size - 20));
                float w = Playable(p, out var a, out _);
                if (w < 0.9f || a.isPath || !Clear(p, 2.5f) || Steep(td, p) > 18f) continue;
                Place(Pick(Bushes), deco, t, p, rng, 0.8f, 1.2f);
                placed++;
            }

            // 꽃밭·풀 (충돌 없음, 장식)
            placed = 0;
            for (int i = 0; i < 12000 && placed < 650; i++)
            {
                var p = V(Rand(15, Size - 15), Rand(15, Size - 15));
                float w = Playable(p, out _, out _);
                if (w < 0.75f || LakeWeight(p) > 0.01f || Steep(td, p) > 20f) continue;
                bool flower = rng.NextDouble() < 0.45;
                var go = Place(flower ? Pick(Flowers) : Pick(Grass), deco, t, p, rng, flower ? 2.2f : 1.2f, flower ? 3.5f : 2f);
                if (go != null) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                placed++;
            }

            // 호숫가 갈대·수련
            for (int i = 0; i < 40; i++)
            {
                float x = rng.NextDouble() < 0.5 ? Rand(LakeMinX - 1.5f, LakeMinX + 1f) : Rand(LakeMaxX - 1f, LakeMaxX + 1.5f);
                float z = LakeCenterZ(x) + Rand(-15f, 15f);
                var p = V(x, z);
                if (Vector2.Distance(p, Island) < 6f || Mathf.Abs(z - LakeCenterZ(x)) < 3f) continue;
                Place(rng.NextDouble() < 0.5 ? "Reeds_01" : "Cattail_02", deco, t, p, rng, 1f, 1.4f);
            }
            for (int i = 0; i < 14; i++)
            {
                var p = V(Rand(LakeMinX + 3, LakeMaxX - 3), LakeCenterZ(150) + Rand(-14f, 14f));
                if (Vector2.Distance(p, Island) < 5f || Mathf.Abs(p.y - LakeCenterZ(p.x)) < 2.5f) continue;
                var go = Place(rng.NextDouble() < 0.5 ? "LilyPads_01" : "Waterlily_01", deco, t, p, rng, 1f, 1.3f);
                if (go != null) go.transform.position = new Vector3(p.x, WaterLevel + 0.6f, p.y);
            }
        }

        static void BuildShrineDecor(Transform world, Terrain t)
        {
            var root = new GameObject("Shrine").transform;
            root.SetParent(world);
            var rng = new System.Random(7);
            Vector2 c = V(224.5f, 214f);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                Place("Stone_Big_0" + (1 + i % 3), root, t, c + V(Mathf.Cos(a), Mathf.Sin(a)) * 4.5f, rng, 2.5f, 3.2f);
            }
            Place("BlossomTree_04", root, t, c, rng, 1.2f, 1.2f);
            var l = new GameObject("Shrine Light").AddComponent<Light>();
            l.transform.SetParent(root);
            l.transform.position = Ground(t, c) + Vector3.up * 3f;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.75f, 0.9f);
            l.intensity = 4f;
            l.range = 12f;
        }

        // ───────────────────────── 조명 / 네비 ─────────────────────────

        static void BuildLighting()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{KitFolder}/Materials/Skybox/Skybox.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.9f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.92f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;

            string profilePath = DataFolder + "/Fantasy_Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                if (c != null) Object.DestroyImmediate(c, true);
            }
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.5f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.1f);
            color.saturation.Override(-4f);
            color.contrast.Override(8f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            foreach (var c in profile.components)
                if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 키트 물 셰이더는 깊이·불투명 텍스처로 물 색과 굴절을 계산한다 → 꺼져 있으면 물이 안 보임
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.supportsCameraDepthTexture = true;
                urp.supportsCameraOpaqueTexture = true;
                EditorUtility.SetDirty(urp);
            }

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            SlowVegetationWind();
        }

        /// <summary>
        /// 식물 흔들림 속도 낮추기. 키트 기본값(대부분 _Wind_Speed 1.0 = 최대)이 너무 빨라 느긋하게 바꾼다.
        /// (키트의 WindControl은 에디터 OnValidate에서만 머티리얼을 바꾸는 도구라 런타임엔 쓰지 않는다)
        /// </summary>
        [MenuItem("LNStudy/Fantasy Stages/Slow Down Vegetation Wind")]
        public static void SlowVegetationWind()
        {
            const float maxSpeed = 0.05f, maxStrength = 0.15f;
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { KitFolder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_Wind_Speed")) continue;
                m.SetFloat("_Wind_Speed", Mathf.Min(m.GetFloat("_Wind_Speed"), maxSpeed));
                if (m.HasProperty("_Wind_Strength")) m.SetFloat("_Wind_Strength", Mathf.Min(m.GetFloat("_Wind_Strength"), maxStrength));
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FantasyStageBuilder] 식물 바람 속도 조정: {n}개 머티리얼");
        }

        static void BakeNavMesh(GameObject world)
        {
            var surface = world.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            string path = DataFolder + "/Fantasy_NavMesh.asset";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, path);
                EditorUtility.SetDirty(surface);
            }
        }
    }
}
