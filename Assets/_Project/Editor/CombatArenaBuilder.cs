using System.Collections.Generic;
using System.Linq;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Companion;
using LNStudy.Player;
using LNStudy.Tools;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 3인칭 전투 테스트 씬(Combat_Arena)을 처음부터 다시 만든다. 여러 번 실행해도 같은 결과.
    /// - 맵: Creepy_Cat 3D Scifi Kit Starter Kit HD (6m 격자, URP 패키지 + 남은 레거시 머티리얼 변환)로
    ///        30×30m, 천장 8m 전투 홀. 중앙 단상, 기둥 4개, 엄폐용 상자·펜스, 천창 4개
    /// - 키트 프리팹의 조명은 전부 Baked 모드라 실시간 씬에서는 꺼진 것과 같다 → 실시간 조명을 따로 배치
    /// - 플레이어: Unity-chan + 기존 PlayerMotor/PlayerAnimator, 카메라 기준 이동
    /// - 카메라: ThirdPersonCamera (원신·스타레일식 궤도 카메라)
    /// - 펫: Robot_Scout_HyperX 두 대가 양 어깨 위를 따라다님 (머티리얼 URP 변환)
    /// - 몬스터: Sci-Fi Robots Bundle 로봇 (휴머노이드 리타깃 + MonsterBrain + NavMesh)
    /// - 조명: 따뜻한 천장등 + 안개 + 포스트 프로세싱 + 먼지 파티클
    /// </summary>
    public static class CombatArenaBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Combat_Arena.unity";
        const string SceneDataFolder = "Assets/_Project/Scenes/Combat_Arena";
        const string KitPrefabs = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs";
        const string KitFolder = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD";
        const string UnityChanPrefab = "Assets/unity-chan!/Unity-chan! Model/Prefabs/unitychan_dynamic.prefab";
        const string PlayerController = "Assets/_Project/Animation/PlayerAnimator.controller";
        const string DefaultPetPrefab = "Assets/Sci_fi_Drones/Prefabs/Robot_Scout_HyperX.prefab";
        const string PetPrefabPrefKey = "LNStudy.CombatArena.PetPrefab";
        const string ShisaFolder = "Assets/_Project/Characters/Shisa";
        const string ShisaModel = ShisaFolder + "/Shisa.fbx";
        const string ShisaMaterialPath = ShisaFolder + "/Shisa_VertexColorToon.mat";
        const float ShisaHeight = 1.0f;
        const float ShisaYaw = 0f; // 모델 정면 보정
        const string RamenFolder = "Assets/_Project/Props/Ramen";
        const float PetTrailWidth = 0.32f;
        const float PetTrailTime = 0.26f;

        /// <summary>씬에서 사용자가 조정한 펫 모델 배치 (재빌드 때 그대로 복원)</summary>
        struct PetPose { public Vector3 pos; public Quaternion rot; public Vector3 scale; }
        static PetPose? capturedPetPose;
        const string DustMaterialPath = "Assets/_Project/Materials/Dust.mat";
        const string VolumePath = SceneDataFolder + "/Combat_Arena_Volume.asset";
        const string NavMeshPath = SceneDataFolder + "/Combat_Arena_NavMesh.asset";
        const string CombatPrefabFolder = "Assets/_Project/Prefabs/Combat";

        const float Tile = 6f;
        const int Tiles = 5;                  // 30m × 30m
        const float Half = Tile * Tiles / 2f; // 15
        const float WallHeight = 4f;
        const int WallRows = 2;               // 4m × 2 = 8m 벽
        const float CeilingY = WallHeight * WallRows;
        const float PetScale = 0.22f;

        static readonly (string robot, Vector3 pos, float yaw)[] MonsterSpawns =
        {
            ("Robert", new Vector3(-7f, 0f, 7f), 150f),
            ("Engie", new Vector3(8f, 0f, 9f), 210f),
            ("Paperman", new Vector3(0f, 0f, 11f), 180f),
        };

        [MenuItem("LNStudy/Combat Arena/Build Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[CombatArenaBuilder] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }

            // 0) 준비: 레이어, 머티리얼, 몬스터 리그
            int enemyLayer = EnsureLayer("Enemy");
            int playerLayer = LayerMask.NameToLayer("Player");
            StandardToURPConverter.ConvertFolder(StandardToURPConverter.DroneMaterialFolder);
            StandardToURPConverter.ConvertFolder(StandardToURPConverter.ScifiKitFolder);
            var humanoidRobots = MonsterRigSetup.SetupAll();
            PlayerAnimatorBuilder.Build();
            EnsureFolder(SceneDataFolder);
            EnsureFolder("Assets/_Project/Materials");
            EnsureFolder(CombatPrefabFolder);
            BuildHitVfx("HitSpark", new Color(0.5f, 0.9f, 1f));
            BuildHitVfx("MissileBurst", new Color(1f, 0.6f, 0.25f));
            var missile = BuildProjectilePrefab("Missile", new Color(1f, 0.55f, 0.2f), 0.08f, 0.09f, 0.3f, 18f, 600f, "MissileBurst");

            // 1) 새 씬 (열린 씬은 저장 후 교체)
            EditorSceneManager.SaveOpenScenes();
            string petPrefab = ResolvePetPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2) 맵
            var arena = new GameObject("Arena");
            var dressing = new GameObject("Arena_CeilingAndLights");
            BuildFloorAndWalls(arena.transform);
            BuildInterior(arena.transform);
            BuildCeiling(dressing.transform);
            BuildSigns(dressing.transform);
            SetStaticRecursive(arena, true);
            SetStaticRecursive(dressing, true);

            // 3) 조명·분위기
            BuildLighting(dressing.transform);
            BuildDust();

            // 4) 플레이어 + 카메라 + 펫
            var player = BuildPlayer(playerLayer, out var unityChanAnimator);
            var cam = BuildCamera(player.transform);
            SetField(player.GetComponent<PlayerMotor>(), "moveSpace", cam.transform);
            var petL = BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Left);
            var petR = BuildPet(petPrefab, player.transform, unityChanAnimator, ShoulderPet.Side.Right);
            SetupPlayerCombat(player, cam, new[] { petL, petR }, enemyLayer);
            SetupShisaAndSwitcher(player, new[] { petL, petR }, cam);

            // 5) 네비메시 (몬스터보다 먼저)
            BakeNavMesh(arena);

            // 6) 몬스터
            var monsterRoot = new GameObject("Monsters");
            foreach (var (robot, pos, yaw) in MonsterSpawns)
            {
                if (!humanoidRobots.Contains(robot))
                {
                    Debug.LogWarning($"[CombatArenaBuilder] {robot}: 휴머노이드 설정 실패로 건너뜀");
                    continue;
                }
                BuildMonster(robot, pos, yaw, enemyLayer, player.transform, monsterRoot.transform);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"[CombatArenaBuilder] 완료: {ScenePath} (몬스터 {monsterRoot.transform.childCount}마리)");
        }

        // ───────────────────────── Map ─────────────────────────
        // 키트 규칙 (프리팹 바운드로 확인): 바닥 6×6은 피벗 기준 x∈[-6,0], z∈[0,6].
        // 벽(…_Long)은 x∈[-2,0], z∈[0,6], 높이 4 이고 디테일 면이 +X → 회전 0이면 서쪽 벽(안쪽이 +X).

        static GameObject Kit(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{name}.prefab");
            if (prefab == null) { Debug.LogWarning("[CombatArenaBuilder] 키트 프리팹 없음: " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// <summary>충돌체가 없는 키트 소품에 렌더러 크기에 맞춘 박스 충돌체를 붙인다</summary>
        static void EnsureBoxCollider(GameObject go)
        {
            if (go == null || go.GetComponentInChildren<Collider>() != null) return;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            var s = go.transform.InverseTransformVector(b.size);
            box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }

        static void BuildFloorAndWalls(Transform root)
        {
            var floor = new GameObject("Floor").transform; floor.SetParent(root);
            var walls = new GameObject("Walls").transform; walls.SetParent(root);

            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                    Kit("Floors/Floor_Squared_01_6x6", floor, new Vector3(-Half + Tile * (i + 1), 0f, -Half + Tile * j));

            string[] lower = { "Walls/Wall_Simple_01_Long", "Walls/Wall_Simple_03_Long", "Walls/Wall_Simple_02_Long", "Walls/Wall_Simple_03_Long", "Walls/Wall_Simple_01_Long" };
            for (int row = 0; row < WallRows; row++)
            {
                float y = row * WallHeight;
                for (int k = 0; k < Tiles; k++)
                {
                    string w = row == 0 ? lower[k] : "Walls/Wall_Simple_04_Long";
                    // 동·서 벽 가운데는 관측창 벽으로 변화를 준다
                    string side = row == 0 && k == Tiles / 2 ? "Walls/Porthole_01_Long" : w;
                    Kit(side, walls, new Vector3(-Half, y, -Half + Tile * k), 0f);              // 서
                    Kit(side, walls, new Vector3(Half, y, -Half + Tile * (k + 1)), 180f);       // 동
                    Kit(w, walls, new Vector3(-Half + Tile * k, y, Half), 90f);                 // 북
                    Kit(w, walls, new Vector3(-Half + Tile * (k + 1), y, -Half), 270f);         // 남
                }
                // 모서리 2×2 블록
                foreach (var c in new[] { new Vector3(-Half, y, -Half - 2f), new Vector3(-Half, y, Half),
                                          new Vector3(Half + 2f, y, Half), new Vector3(Half + 2f, y, -Half - 2f) })
                    Kit("Walls/Wall_Simple_01_Squared", walls, c);
            }
        }

        static void BuildInterior(Transform root)
        {
            var props = new GameObject("Props").transform; props.SetParent(root);

            // 중앙 단상 6×6, 높이 1m (점프로 오를 수 있음, 몬스터는 돌아감)
            var dais = new GameObject("Dais").transform; dais.SetParent(props);
            foreach (float x in new[] { -1f, 1f, 3f })
                EnsureBoxCollider(Kit("Walls/Wall_Cube_01_Flat", dais, new Vector3(x, 0f, -3f)));

            // 천장까지 닿는 기둥 4개 (2×2, 엄폐물). 피벗 기준 x∈[-2,0], z∈[0,2] → 중심 = 피벗 + (-1, 0, 1)
            foreach (var c in new[] { new Vector3(-9f, 0f, -6f), new Vector3(9f, 0f, -6f), new Vector3(-9f, 0f, 6f), new Vector3(9f, 0f, 6f) })
                for (int row = 0; row < WallRows; row++)
                    Kit("Walls/Wall_Cube_01_Round", props, new Vector3(c.x + 1f, row * WallHeight, c.z - 1f));

            // 상자 무더기 (키트 상자는 충돌체가 없어 박스를 붙인다)
            var crates = new (Vector3 pos, float yaw)[]
            {
                (new Vector3(-12f, 0f, -12f), 15f), (new Vector3(-10.5f, 0f, -12.5f), 80f), (new Vector3(-12f, 0.8f, -12f), 40f),
                (new Vector3(12f, 0f, 12f), 200f), (new Vector3(10.6f, 0f, 12.4f), 95f),
                (new Vector3(12.5f, 0f, -11f), 120f), (new Vector3(-12.5f, 0f, 11f), 300f), (new Vector3(-11f, 0f, 12.5f), 10f),
            };
            foreach (var (pos, yaw) in crates)
                EnsureBoxCollider(Kit("Props/Crate_01", props, pos, yaw));

            // 낮은 펜스 (엄폐, 1.3m)
            Kit("Fences/Fence_Long_01", props, new Vector3(-4.5f, 0f, 7f), 90f);
            Kit("Fences/Fence_Long_01", props, new Vector3(9.5f, 0f, -9f), 90f);
        }

        static void BuildCeiling(Transform root)
        {
            var ceiling = new GameObject("Ceiling").transform; ceiling.SetParent(root);
            var glass = new HashSet<(int, int)> { (1, 1), (1, 3), (3, 1), (3, 3) };
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                {
                    string roof = glass.Contains((i, j)) ? "Roofs/Roof_Glass_01_6x6" : "Roofs/Roof_Squared_01_6x6";
                    Kit(roof, ceiling, new Vector3(-Half + Tile * (i + 1), CeilingY, -Half + Tile * j));
                }

            // 가운데 대형 환풍기 (천천히 회전)
            var fan = Kit("Props/Airing_01", ceiling, new Vector3(0f, CeilingY - 0.05f, 0f));
            if (fan != null)
            {
                fan.AddComponent<Rotator>();
                SetField(fan.GetComponent<Rotator>(), "degreesPerSecond", new Vector3(0f, 25f, 0f));
            }
        }

        static void BuildSigns(Transform root)
        {
            var signs = new GameObject("Signs").transform; signs.SetParent(root);
            // 문자 데칼은 YZ 평면, 벽면에서 살짝 띄운다
            Kit("Symbols/Word_Zone", signs, new Vector3(-Half + 0.05f, 5.6f, -7f), 0f);
            Kit("Symbols/Sign_Danger_01", signs, new Vector3(Half - 0.05f, 5.8f, 6f), 180f);
            Kit("Symbols/Num_1", signs, new Vector3(-Half + 0.05f, 5.6f, 8f), 0f);
        }

        // ───────────────────────── Lighting ─────────────────────────

        static void BuildLighting(Transform root)
        {
            // 천창 너머로 보이는 하늘 (키트 스카이박스)
            var skyGuid = AssetDatabase.FindAssets("Skybox t:Material", new[] { KitFolder }).FirstOrDefault();
            RenderSettings.skybox = skyGuid != null ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(skyGuid)) : null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.11f, 0.13f, 0.17f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.085f, 0.085f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.035f, 0.035f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.07f, 0.08f, 0.10f);
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            // 키 라이트 (그림자 담당)
            var sun = new GameObject("Key Light (Directional)").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.72f);
            sun.intensity = 1.1f; // 천창으로만 들어오는 빛줄기
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(62f, -35f, 0f);
            RenderSettings.sun = sun;

            // 천장등 (키트 조명 메쉬 + 실시간 포인트 라이트). 가운데는 환풍기, 대각선 4칸은 천창
            var lamps = new GameObject("Ceiling Lamps").transform; lamps.SetParent(root);
            foreach (float x in new[] { -12f, 0f, 12f })
                foreach (float z in new[] { -12f, 0f, 12f })
                {
                    if (x == 0f && z == 0f) continue;
                    Kit("Lights/Light_Corridor_01", lamps, new Vector3(x, CeilingY - 0.05f, z), 0f);
                    AddPointLight(lamps, new Vector3(x, CeilingY - 1.2f, z), new Color(1f, 0.8f, 0.6f), 9f, 11f);
                }

            // 벽 따라 청록 보조광 (키트 네온과 맞춤)
            foreach (float t in new[] { -9f, 3f, 9f })
            {
                AddPointLight(lamps, new Vector3(-Half + 1.2f, 2.2f, t), new Color(0.3f, 0.8f, 1f), 3f, 6f);
                AddPointLight(lamps, new Vector3(Half - 1.2f, 2.2f, -t), new Color(0.3f, 0.8f, 1f), 3f, 6f);
                AddPointLight(lamps, new Vector3(t, 2.2f, Half - 1.2f), new Color(0.3f, 0.8f, 1f), 3f, 6f);
            }

            // 차가운 보조광 (레퍼런스의 청록 대비)
            foreach (var (p, yaw) in new[] { (new Vector3(-13f, 4.5f, -13f), 45f), (new Vector3(13f, 4.5f, 13f), 225f) })
            {
                var s = new GameObject("Rim Spot").AddComponent<Light>();
                s.transform.SetParent(root);
                s.transform.SetPositionAndRotation(p, Quaternion.Euler(30f, yaw, 0f));
                s.type = LightType.Spot;
                s.color = new Color(0.35f, 0.75f, 1f);
                s.intensity = 6f;
                s.range = 22f;
                s.spotAngle = 55f;
                s.shadows = LightShadows.None;
            }

            // 포스트 프로세싱
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                if (c != null) Object.DestroyImmediate(c, true);
            }

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.65f);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.1f);
            color.contrast.Override(14f);
            color.saturation.Override(10f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.35f);
            vignette.smoothness.Override(0.45f);

            foreach (var c in profile.components)
                if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void AddPointLight(Transform parent, Vector3 pos, Color color, float intensity, float range)
        {
            var l = new GameObject("Point Light").AddComponent<Light>();
            l.transform.SetParent(parent);
            l.transform.position = pos;
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        static void BuildDust()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, DustMaterialPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 0.55f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);

            var go = new GameObject("Dust Particles");
            go.transform.position = new Vector3(0f, CeilingY * 0.5f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.maxParticles = 700;
            main.gravityModifier = 0.004f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 50f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Half * 1.9f, CeilingY * 0.9f, Half * 1.9f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        }

        // ───────────────────────── Player / Camera / Pets ─────────────────────────

        internal static GameObject BuildPlayer(int playerLayer, out Animator modelAnimator)
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);

            var rb = player.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.radius = 0.28f;
            capsule.height = 1.55f;
            capsule.center = new Vector3(0f, 0.775f, 0f);
            player.AddComponent<PlayerInputReader>();
            // 몬스터 NavMeshAgent가 플레이어를 장애물로 보고 비켜 가게 (파고들어 밀어내는 것 방지)
            var obstacle = player.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.35f;
            obstacle.height = 1.55f;
            obstacle.center = new Vector3(0f, 0.775f, 0f);
            obstacle.carving = false;
            var motor = player.AddComponent<PlayerMotor>();
            SetField(motor, "walkSpeed", 2.2f);
            SetField(motor, "runSpeed", 5.2f);
            SetField(motor, "standHeight", 1.55f);
            SetField(motor, "crouchHeight", 0.9f);
            SetField(motor, "jumpHeight", 1.1f);
            SetField(motor, "maxClimbHeight", 1.8f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnityChanPrefab);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            model.name = "unitychan_dynamic";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            StripLegacyUnityChanScripts(model);

            modelAnimator = model.GetComponent<Animator>();
            modelAnimator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerController);
            modelAnimator.applyRootMotion = false;
            modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (model.GetComponent<PlayerAnimator>() == null) model.AddComponent<PlayerAnimator>();
            if (model.GetComponent<PlayerFace>() == null) model.AddComponent<PlayerFace>();

            if (playerLayer >= 0) SetLayerRecursive(player, playerLayer);
            return player;
        }

        /// <summary>구 Input API를 쓰는 샘플 스크립트 제거 (Input System 전용 프로젝트에서 예외 발생)</summary>
        static void StripLegacyUnityChanScripts(GameObject model)
        {
            var keep = new HashSet<string> { "AutoBlink", "SpringManager", "SpringBone", "SpringCollider" };
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (t.Namespace == "UnityChan" && !keep.Contains(t.Name))
                    Object.DestroyImmediate(mb);
            }
        }

        internal static Camera BuildCamera(Transform player)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = RenderSettings.fogColor;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var orbit = go.AddComponent<ThirdPersonCamera>();
            SetField(orbit, "target", player);
            SetField(orbit, "collisionMask", (LayerMask)(1 << 0)); // 환경(Default)만
            SetField(orbit, "startPitch", 12f);
            SetField(orbit, "distance", 4.2f);
            go.transform.position = player.position + new Vector3(0f, 2f, -4f);
            go.transform.LookAt(player.position + Vector3.up * 1.3f);
            return cam;
        }

        /// <summary>
        /// 펫 프리팹 결정: 씬에서 직접 바꾼 드론이 있으면 그것을 유지한다 (재빌드가 사용자 선택을 덮어쓰지 않게).
        /// 우선순위: 현재 열린 Combat_Arena의 펫 → 마지막으로 기억한 값 → 기본값(HyperX)
        /// </summary>
        internal static string ResolvePetPrefab()
        {
            capturedPetPose = null;
            {
                foreach (var pet in Object.FindObjectsByType<ShoulderPet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    foreach (Transform child in pet.transform)
                    {
                        var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                        string path = src != null ? AssetDatabase.GetAssetPath(src) : null;
                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                        {
                            EditorPrefs.SetString(PetPrefabPrefKey, path);
                            capturedPetPose = new PetPose { pos = child.localPosition, rot = child.localRotation, scale = child.localScale };
                            EditorPrefs.SetString(PetPrefabPrefKey + ".Pose", JsonUtility.ToJson(new PoseData
                            {
                                pos = child.localPosition, rot = child.localRotation, scale = child.localScale
                            }));
                            return path;
                        }
                    }
                }
            }
            string saved = EditorPrefs.GetString(PetPrefabPrefKey, DefaultPetPrefab);
            string poseJson = EditorPrefs.GetString(PetPrefabPrefKey + ".Pose", "");
            if (!string.IsNullOrEmpty(poseJson))
            {
                var pd = JsonUtility.FromJson<PoseData>(poseJson);
                capturedPetPose = new PetPose { pos = pd.pos, rot = pd.rot, scale = pd.scale };
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(saved) != null ? saved : DefaultPetPrefab;
        }

        [System.Serializable]
        class PoseData { public Vector3 pos; public Quaternion rot; public Vector3 scale; }

        /// <summary>펫 드론 프리팹을 직접 지정 (프로젝트 창에서 프리팹 선택 후 실행)</summary>
        [MenuItem("LNStudy/Combat Arena/Use Selected Prefab as Pet")]
        static void UseSelectedAsPet()
        {
            var go = Selection.activeObject as GameObject;
            string path = go != null ? AssetDatabase.GetAssetPath(go) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                Debug.LogWarning("[CombatArenaBuilder] 프로젝트 창에서 프리팹을 선택하세요.");
                return;
            }
            EditorPrefs.SetString(PetPrefabPrefKey, path);
            EditorPrefs.DeleteKey(PetPrefabPrefKey + ".Pose"); // 다른 프리팹이면 이전 배치값은 맞지 않음
            Debug.Log("[CombatArenaBuilder] 펫 프리팹 지정: " + path);
        }

        internal static ShoulderPet BuildPet(string prefabPath, Transform owner, Animator ownerAnimator, ShoulderPet.Side side)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning("[CombatArenaBuilder] 펫 프리팹 없음: " + prefabPath); return null; }

            var root = new GameObject($"Pet_{prefab.name}_{side}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * PetScale;
            model.transform.localPosition = Vector3.zero;

            // 충돌체 끄기: 어깨 위 펫 충돌체가 주인 캡슐과 겹쳐 주인을 계속 밀어내던 원인
            // (프리팹 인스턴스라 삭제 대신 비활성화 — ShoulderPet.Awake에서도 한 번 더 끈다)
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            if (capturedPetPose.HasValue)
            {
                // 씬에서 직접 맞춘 크기·위치 유지
                model.transform.localPosition = capturedPetPose.Value.pos;
                model.transform.localRotation = capturedPetPose.Value.rot;
                model.transform.localScale = capturedPetPose.Value.scale;
            }
            else
            {
                var rs = model.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    model.transform.localPosition = -b.center;
                }
            }

            // 돌진할 때만 켜지는 꼬리
            var trailGo = new GameObject("DashTrail");
            trailGo.transform.SetParent(root.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = PetTrailTime;
            trail.minVertexDistance = 0.03f;
            trail.widthMultiplier = PetTrailWidth;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var grad = new Gradient();
            var c = new Color(0.4f, 0.85f, 1f);
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.4f), new GradientColorKey(c, 1f) },
                         new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
            trail.sharedMaterial = AdditiveParticle("PetTrail", Color.white);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;

            var pet = root.AddComponent<ShoulderPet>();
            SetField(pet, "dashTrail", trail);
            SetField(pet, "strikeVfx", AssetDatabase.LoadAssetAtPath<GameObject>($"{CombatPrefabFolder}/HitSpark.prefab"));
            SetField(pet, "owner", owner);
            SetField(pet, "ownerAnimator", ownerAnimator);
            SetEnumField(pet, "side", side == ShoulderPet.Side.Left ? 0 : 1);
            root.transform.position = owner.position + Vector3.up * 1.6f + owner.right * ((int)side * 0.32f);
            return pet;
        }

        // ───────────────────────── Combat ─────────────────────────

        internal static void SetupPlayerCombat(GameObject player, Camera cam, ShoulderPet[] pets, int enemyLayer)
        {
            var health = player.AddComponent<Health>();
            SetField(health, "maxHealth", 100f);
            SetField(health, "invulnerableAfterHit", 0.4f);
            SetField(health, "aimHeight", 1.0f);

            var combat = player.AddComponent<PlayerCombat>();
            var so = new SerializedObject(combat);
            var petsProp = so.FindProperty("pets");
            petsProp.arraySize = pets.Length;
            for (int i = 0; i < pets.Length; i++) petsProp.GetArrayElementAtIndex(i).objectReferenceValue = pets[i];
            // 프리팹 저장 후 받은 참조는 에셋 갱신으로 끊길 수 있어 경로로 다시 읽는다
            so.FindProperty("missilePrefab").objectReferenceValue = LoadProjectile("Missile");
            so.FindProperty("cameraRig").objectReferenceValue = cam.GetComponent<ThirdPersonCamera>();
            so.FindProperty("playerAnimator").objectReferenceValue = player.GetComponentInChildren<PlayerAnimator>();
            so.FindProperty("enemyMask").intValue = 1 << enemyLayer;
            so.FindProperty("hitMask").intValue = (1 << enemyLayer) | 1; // 적 + 환경(Default)
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static Projectile LoadProjectile(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{CombatPrefabFolder}/{name}.prefab")?.GetComponent<Projectile>();

        internal static Material UnlitGlow(string name, Color color, float intensity)
        {
            string path = $"{CombatPrefabFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color * intensity); // 1 이상 → 블룸으로 빛남
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>가산 혼합 파티클 머티리얼 (꼬리·불꽃용). 정점 색을 곱해 쓴다.</summary>
        internal static Material AdditiveParticle(string name, Color color)
        {
            string path = $"{CombatPrefabFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f); // Additive
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static Projectile BuildProjectilePrefab(string name, Color color, float coreSize, float trailWidth,
            float trailTime, float speed, float turnRate, string hitVfxName)
        {
            var root = new GameObject(name);

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(coreSize, coreSize, coreSize * 2.2f);
            var mr = core.GetComponent<MeshRenderer>();
            mr.sharedMaterial = UnlitGlow(name + "_Core", color, 6f);
            mr.shadowCastingMode = ShadowCastingMode.Off;

            // 꼬리는 자식에 둬야 명중 후 떼어내서 자연스럽게 사라지게 할 수 있다
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.3f), new GradientColorKey(color, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
            trail.sharedMaterial = AdditiveParticle(name + "_Trail", Color.white);
            trail.shadowCastingMode = ShadowCastingMode.Off;

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 1.5f;
            light.range = 2f;
            light.shadows = LightShadows.None;

            var proj = root.AddComponent<Projectile>();
            SetField(proj, "speed", speed);
            SetField(proj, "turnRate", turnRate);
            SetField(proj, "hitVfx", AssetDatabase.LoadAssetAtPath<GameObject>($"{CombatPrefabFolder}/{hitVfxName}.prefab"));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{CombatPrefabFolder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Projectile>();
        }

        internal static GameObject BuildHitVfx(string name, Color color)
        {
            var root = new GameObject(name);
            var ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = color * 3f;
            main.gravityModifier = 0.6f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.02f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var r = root.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            r.sharedMaterial = AdditiveParticle(name + "_Mat", Color.white);

            // 순간 섬광
            var flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(root.transform, false);
            flash.type = LightType.Point;
            flash.color = color;
            flash.intensity = 4f;
            flash.range = 2.5f;
            flash.gameObject.AddComponent<LightFade>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{CombatPrefabFolder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ───────────────────────── Monsters ─────────────────────────

        internal static GameObject BuildMonster(string robot, Vector3 pos, float yaw, int enemyLayer, Transform player, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterRigSetup.PrefabPath(robot));
            if (prefab == null) return null;

            var root = new GameObject("Monster_" + robot);
            root.transform.SetParent(parent);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SwapToURPMaterials(model);

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.avatar = MonsterRigSetup.LoadAvatar(robot);
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MonsterRigSetup.ControllerPath);
            anim.applyRootMotion = false;

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.45f;
            col.height = 2.1f;
            col.center = new Vector3(0f, 1.05f, 0f);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 2.1f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.3f;

            var health = root.AddComponent<Health>();
            SetField(health, "maxHealth", 120f);
            SetField(health, "aimHeight", 1.3f);
            root.AddComponent<HealthBar>();

            var brain = root.AddComponent<MonsterBrain>();
            SetField(brain, "target", player);
            SetField(brain, "animator", anim);
            SetField(brain, "obstacleMask", (LayerMask)(1 << 0));

            SetLayerRecursive(root, enemyLayer);
            return root;
        }

        /// <summary>로봇 프리팹은 Built-in(SRP 폴더) 머티리얼을 쓰므로 같은 이름의 URP 폴더 머티리얼로 바꾼다.</summary>
        static void SwapToURPMaterials(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var urp = AssetDatabase.LoadAssetAtPath<Material>($"{MonsterRigSetup.RobotFolder}/Materials/URP/{mats[i].name}.mat");
                    if (urp != null) mats[i] = urp;
                }
                r.sharedMaterials = mats;
            }
        }

        // ───────────────────────── NavMesh ─────────────────────────

        static void BakeNavMesh(GameObject arena)
        {
            var surface = arena.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            var old = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            if (old != null) AssetDatabase.DeleteAsset(NavMeshPath);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
                EditorUtility.SetDirty(surface);
            }
        }

        // ───────────────────────── Second Character (Shisa) ─────────────────────────

        /// <summary>
        /// 지금 열린 씬을 다시 만들지 않고 고친다: 펫 충돌체 끄기, 꼬리 굵게, 시사 캐릭터·교체 시스템 추가.
        /// (전체 재빌드는 씬에서 손으로 바꾼 것을 덮어쓰므로, 추가 작업은 이 메뉴로)
        /// </summary>
        [MenuItem("LNStudy/Combat Arena/Update Current Scene (Shisa + Fixes)")]
        public static void UpdateCurrentScene()
        {
            if (EditorApplication.isPlaying) return;
            var player = GameObject.FindGameObjectWithTag("Player");
            var cam = Camera.main;
            if (player == null || cam == null) { Debug.LogWarning("[CombatArenaBuilder] Player / Main Camera를 찾지 못했습니다."); return; }

            EnsureFolder(CombatPrefabFolder);
            BuildHitVfx("MissileBurst", new Color(1f, 0.6f, 0.25f));
            BuildProjectilePrefab("Missile", new Color(1f, 0.55f, 0.2f), 0.08f, 0.09f, 0.3f, 18f, 600f, "MissileBurst");

            var pets = Object.FindObjectsByType<ShoulderPet>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (var pet in pets)
            {
                foreach (var col in pet.GetComponentsInChildren<Collider>(true))
                {
                    Undo.RecordObject(col, "Disable pet collider");
                    col.enabled = false;
                }
                var trail = pet.GetComponentInChildren<TrailRenderer>(true);
                if (trail != null)
                {
                    Undo.RecordObject(trail, "Thicker pet trail");
                    trail.widthMultiplier = PetTrailWidth;
                    trail.time = PetTrailTime;
                }
            }

            var combat = player.GetComponent<PlayerCombat>();
            if (combat != null)
            {
                var so = new SerializedObject(combat);
                so.FindProperty("missilePrefab").objectReferenceValue = LoadProjectile("Missile");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            SetupShisaAndSwitcher(player, pets, cam);
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log("[CombatArenaBuilder] 현재 씬 업데이트 완료 (펫 충돌체 끔, 꼬리 굵게, 시사 추가). Tab으로 캐릭터 교체");
        }

        static Material EnsureShisaMaterial()
        {
            var shader = Shader.Find("LNStudy/VertexColorToon");
            if (shader == null) { Debug.LogWarning("[CombatArenaBuilder] LNStudy/VertexColorToon 셰이더 없음"); return null; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ShisaMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ShisaMaterialPath);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static void SetupShisaAndSwitcher(GameObject player, ShoulderPet[] pets, Camera cam)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShisaModel);
            if (modelAsset == null) { Debug.LogWarning("[CombatArenaBuilder] 시사 모델 없음: " + ShisaModel); return; }

            // 이전에 만든 것 정리 (여러 번 실행해도 같은 결과)
            var old = player.transform.Find("Shisa");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var oldSwitcher = player.GetComponent<CharacterSwitcher>();
            if (oldSwitcher != null) Object.DestroyImmediate(oldSwitcher);

            // 원점에서 크기·피벗을 맞춘 뒤 플레이어 밑으로 옮긴다
            var root = new GameObject("Shisa");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, visual);
            model.transform.localRotation = Quaternion.Euler(0f, ShisaYaw, 0f);

            var mat = EnsureShisaMaterial();
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float scale = ShisaHeight / Mathf.Max(0.0001f, b.size.y);
            model.transform.localScale = Vector3.one * scale;
            b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z); // 발바닥 = 피벗

            var proc = root.AddComponent<ProceduralCharacterAnimator>();
            SetField(proc, "visual", visual);
            SetField(proc, "shoulderHeight", ShisaHeight * 0.75f);

            // 시사 기본 공격: 라멘 던지기 (궤적 미리보기 선 + 착지 원)
            var ramen = EnsureRamenPrefab();
            var line = CreatePreviewLine(root.transform, "RamenTrajectory", 0.07f, new Color(1f, 0.85f, 0.45f), false);
            var ring = CreatePreviewLine(root.transform, "RamenLandingRing", 0.05f, new Color(1f, 0.55f, 0.25f), true);
            var thrower = root.AddComponent<RamenThrower>();
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int enemyMask = enemyLayer >= 0 ? 1 << enemyLayer : 0;
            var tso = new SerializedObject(thrower);
            tso.FindProperty("ramenPrefab").objectReferenceValue = ramen;
            tso.FindProperty("trajectoryLine").objectReferenceValue = line;
            tso.FindProperty("landingRing").objectReferenceValue = ring;
            tso.FindProperty("hitMask").intValue = 1 | enemyMask;
            tso.FindProperty("enemyMask").intValue = enemyMask;
            tso.ApplyModifiedPropertiesWithoutUndo();

            root.transform.SetParent(player.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) SetLayerRecursive(root, playerLayer);
            root.SetActive(false);

            // 교체 시스템
            var unityChan = player.transform.Find("unitychan_dynamic");
            var switcher = player.AddComponent<CharacterSwitcher>();
            var so = new SerializedObject(switcher);
            var chars = so.FindProperty("characters");
            chars.arraySize = 2;
            void SetEntry(int i, string name, GameObject go, float stand, float crouch, float radius, float pivot)
            {
                var e = chars.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = name;
                e.FindPropertyRelative("visualRoot").objectReferenceValue = go;
                e.FindPropertyRelative("standHeight").floatValue = stand;
                e.FindPropertyRelative("crouchHeight").floatValue = crouch;
                e.FindPropertyRelative("radius").floatValue = radius;
                e.FindPropertyRelative("cameraPivotHeight").floatValue = pivot;
            }
            SetEntry(0, "Unity-chan", unityChan != null ? unityChan.gameObject : null, 1.55f, 0.9f, 0.28f, 1.3f);
            SetEntry(1, "Shisa", root, ShisaHeight, ShisaHeight * 0.7f, 0.32f, 0.85f);

            var petsProp = so.FindProperty("pets");
            petsProp.arraySize = pets.Length;
            for (int i = 0; i < pets.Length; i++) petsProp.GetArrayElementAtIndex(i).objectReferenceValue = pets[i];
            so.FindProperty("cameraRig").objectReferenceValue = cam.GetComponent<ThirdPersonCamera>();
            so.FindProperty("switchVfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{CombatPrefabFolder}/MissileBurst.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();

            var combat = player.GetComponent<PlayerCombat>();
            if (combat != null) SetField(combat, "switcher", switcher);
        }

        // ───────────────────────── Ramen ─────────────────────────

        internal static LineRenderer CreatePreviewLine(Transform parent, string name, float width, Color color, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = 0;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                         loop ? new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.9f, 1f) }
                              : new[] { new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0.9f, 1f) });
            lr.colorGradient = grad;
            lr.sharedMaterial = AdditiveParticle("RamenAim", Color.white);
            lr.enabled = false;
            return lr;
        }

        internal static RamenProjectile EnsureRamenPrefab()
        {
            string objPath = RamenFolder + "/Ramen.obj";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
            if (model == null) { Debug.LogWarning("[CombatArenaBuilder] 라멘 모델 없음: " + objPath); return null; }

            // 텍스처 임포트 설정 (노멀 / 선형)
            void SetTex(string file, bool normal, bool srgb)
            {
                var ti = AssetImporter.GetAtPath($"{RamenFolder}/{file}") as TextureImporter;
                if (ti == null) return;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.sRGBTexture == srgb) return;
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.SaveAndReimport();
            }
            SetTex("Ramen_Albedo.png", false, true);
            SetTex("Ramen_Normal.png", true, false);
            SetTex("Ramen_MetallicSmoothness.png", false, false);

            string matPath = RamenFolder + "/Ramen.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RamenFolder + "/Ramen_Albedo.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RamenFolder + "/Ramen_Normal.png"));
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RamenFolder + "/Ramen_MetallicSmoothness.png"));
            mat.SetFloat("_Smoothness", 0.7f);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(mat);

            var splash = BuildRamenSplash();

            var root = new GameObject("Ramen");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.17f;

            var bowl = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            bowl.name = "Bowl";
            const float bowlScale = 0.36f;          // 지름 1m → 36cm
            bowl.transform.localScale = Vector3.one * bowlScale;
            bowl.transform.localPosition = new Vector3(0f, -0.446f * bowlScale * 0.5f, 0f); // 그릇 중심 = 루트
            foreach (var r in bowl.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            var proj = root.AddComponent<RamenProjectile>();
            SetField(proj, "splashVfx", splash);

            PrefabUtility.SaveAsPrefabAsset(root, RamenFolder + "/Ramen.prefab");
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(RamenFolder + "/Ramen.prefab")?.GetComponent<RamenProjectile>();
        }

        /// <summary>국물 방울 + 면발 + 섬광</summary>
        static GameObject BuildRamenSplash()
        {
            var root = new GameObject("RamenSplash");

            ParticleSystem Burst(string name, Color color, int count, Vector2 speed, Vector2 size, Vector2 life, float gravity, float lengthScale)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 위쪽으로 분출
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = 0.4f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
                main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
                main.startColor = color;
                main.gravityModifier = gravity;
                main.stopAction = ParticleSystemStopAction.Destroy;
                var em = ps.emission;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 50f;
                shape.radius = 0.15f;
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = lengthScale;
                r.sharedMaterial = AlphaParticle("RamenSplash_Mat");
                return ps;
            }

            Burst("Broth", new Color(0.95f, 0.62f, 0.28f, 0.9f), 40, new Vector2(2.5f, 6f), new Vector2(0.05f, 0.12f), new Vector2(0.4f, 0.8f), 1.6f, 1.2f);
            Burst("Noodles", new Color(1f, 0.93f, 0.65f, 1f), 14, new Vector2(2f, 4.5f), new Vector2(0.03f, 0.05f), new Vector2(0.5f, 0.9f), 1.4f, 6f);

            var flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(root.transform, false);
            flash.transform.localPosition = Vector3.up * 0.3f;
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.65f, 0.3f);
            flash.intensity = 5f;
            flash.range = 4f;
            flash.gameObject.AddComponent<LightFade>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RamenFolder + "/RamenSplash.prefab");
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(RamenFolder + "/RamenSplash.prefab");
        }

        /// <summary>알파 혼합 파티클 머티리얼 (국물처럼 불투명한 방울용)</summary>
        internal static Material AlphaParticle(string name)
        {
            string path = $"{RamenFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ───────────────────────── Utils ─────────────────────────

        internal static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[CombatArenaBuilder] {target.GetType().Name}.{field} 없음"); return; }
            switch (value)
            {
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case Vector3 v: p.vector3Value = v; break;
                case Vector2 v2: p.vector2Value = v2; break;
                case LayerMask m: p.intValue = m.value; break;
                case Color col: p.colorValue = col; break;
                case string str: p.stringValue = str; break;
                case Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetEnumField(Object target, string field, int index)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).enumValueIndex = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        internal static void SetStaticRecursive(GameObject go, bool on)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = on;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        internal static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = name;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }
            Debug.LogWarning("[CombatArenaBuilder] 빈 레이어가 없어 Default를 사용합니다.");
            return 0;
        }

        internal static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
