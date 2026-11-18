using System.Linq;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using LNStudy.Interaction;
using LNStudy.Player;
using LNStudy.Tools;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 지금 열린 씬(Level Design)에 "고양이 원정대" 게임플레이를 얹는다. 사용자가 만든 맵은 건드리지 않고
    /// 생성물은 전부 루트 "CatPartyGame" 아래에 둔다 (다시 실행하면 그 루트만 지우고 다시 만든다).
    /// - 고양이 2마리 (LittleFriends): 전투 고양이(컵케이크 포물선 던지기) / 힘센 고양이(상자 잡고 끌기)
    ///   둘 다 필드에 있고 Tab으로 조종 대상을 바꾼다. 조종하지 않는 쪽은 자동으로 따라온다.
    /// - 기믹: 무거운 상자 → 압력 발판 → 다람쥐 마을 문 열림 / 마을 안 상자를 받침대 옆으로 옮겨 케이크 되찾기
    /// - 몬스터: 다람쥐 5마리 (MonsterBrain + NavMesh)
    /// 기존 Main Camera는 지우지 않고 Camera·AudioListener만 끈다 (Volume은 그대로 사용).
    /// </summary>
    public static class CatPartyBuilder
    {
        const string RootName = "CatPartyGame";
        const string Kit = "Assets/LittleFriends-CartoonAnimals-Lite";
        const string AnimFolder = Kit + "/Animations";
        const string OutFolder = "Assets/_Project/CatParty";
        const string CatControllerPath = OutFolder + "/LittleCatPlayer.controller";
        const string SquirrelControllerPath = OutFolder + "/LittleSquirrelMonster.controller";
        const string UpperMaskPath = "Assets/_Project/Animation/UpperBody.mask";
        const string CupcakePrefabPath = OutFolder + "/Cupcake.prefab";
        const string CreamSplashPath = OutFolder + "/CreamSplash.prefab";
        const string NavMeshPath = OutFolder + "/CatParty_NavMesh.asset";

        const float CatScale = 2.1f;      // 0.57m → 약 1.2m
        const float CatHeight = 1.2f;
        const float SquirrelScale = 2.0f; // 약 1.15m

        // 맵 배치 (Low Poly 맵 좌표 기준)
        static readonly Vector2 StartXZ = new Vector2(-55f, 44f);
        static readonly Rect Camp = new Rect(-97f, 34f, 18f, 24f); // x, z, w, h (윗단 평지 y≈-5.7)
        const float GateZ = 45f;
        const float GateWidth = 3.2f;
        const float WallHeight = 3.2f;
        static readonly Vector2 PlateXZ = new Vector2(-72f, 42f);
        static readonly Vector2 CrateAXZ = new Vector2(-66f, 49f);
        static readonly Vector2 CrateBXZ = new Vector2(-86f, 48f);
        static readonly Vector2 CakePedestalXZ = new Vector2(-93f, 39f);
        const float PedestalHeight = 2.3f;
        static readonly Vector2[] SquirrelSpawns =
        {
            new Vector2(-88f, 38f), new Vector2(-84f, 53f), new Vector2(-93f, 47f),
            new Vector2(-85f, 42f), new Vector2(-90f, 49f),
        };

        static Material woodMat, darkWoodMat, crateMat, stoneMat;

        [MenuItem("LNStudy/Cat Party/Setup In Current Scene")]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            CombatArenaBuilder.EnsureFolder(OutFolder);
            int playerLayer = CombatArenaBuilder.EnsureLayer("Player");
            int enemyLayer = CombatArenaBuilder.EnsureLayer("Enemy");
            BuildMaterials();
            var catController = BuildPlayerController(CatControllerPath, "Cat", 2.4f, 4.8f);
            var squirrelController = BuildSquirrelController();
            var cupcake = EnsureCupcakePrefab(enemyLayer);

            var root = new GameObject(RootName);
            var env = new GameObject("Environment").transform;
            env.SetParent(root.transform, false);

            // ── 지형 위 구조물 (NavMesh 굽기 전에 만들 것)
            BuildPalisade(env);
            BuildRamp(env);
            var pedestal = BuildPedestal(env, out var cakeVisual);

            // ── NavMesh: 마을 안쪽만 (다람쥐 전용)
            var surface = BakeCampNavMesh(root, playerLayer, enemyLayer);

            // ── 굽기에 넣지 않는 것들 (움직이는 상자, 문, 캐릭터)
            var gate = BuildGate(env);
            var crateA = BuildCrate(env, "HeavyCrate_A", CrateAXZ);
            var crateB = BuildCrate(env, "HeavyCrate_B", CrateBXZ);
            var plate = BuildPressurePlate(env, PlateXZ);

            // 카메라
            DisableOldCamera();
            var cam = BuildCamera(root.transform);

            // 고양이
            var cats = new GameObject("Cats").transform;
            cats.SetParent(root.transform, false);
            Vector3 start = Ground(StartXZ);
            Quaternion face = Quaternion.Euler(0f, -90f, 0f); // 서쪽(마을 방향)
            var combatCat = BuildCat(cats, "CombatCat", $"{Kit}/Prefabs/LittleCat_Happy.prefab", catController,
                start, face, playerLayer, cam.transform, out var combatModel);
            var strongCat = BuildCat(cats, "StrongCat", $"{Kit}/Prefabs/LittleCat_Angry.prefab", catController,
                start + face * new Vector3(0.9f, 0f, -1.8f), face, playerLayer, cam.transform, out var strongModel);
            SetupCombatCat(combatCat, combatModel, cupcake, enemyLayer);
            SetupStrongCat(strongCat);
            CombatArenaBuilder.SetField(cam.GetComponent<ThirdPersonCamera>(), "target", combatCat.transform);

            // 다람쥐
            var monsters = new GameObject("Squirrels").transform;
            monsters.SetParent(root.transform, false);
            var squirrelHealths = SquirrelSpawns
                .Select((xz, i) => BuildSquirrel(monsters, i, xz, squirrelController, enemyLayer, combatCat.transform))
                .Where(h => h != null).ToArray();

            // 스테이지 진행 + HUD
            var hud = BuildHud(root.transform);
            var stage1 = BuildStage(root.transform, "Stage1_Gate", "다람쥐 마을 앞",
                new Vector3(-66f, 0f, 45f), new Vector3(26f, 14f, 30f),
                "힘센 고양이로 상자를 발판 위에 올려 문 열기\n(Tab: 교체 · E: 잡기/놓기)", new Health[0], gate, false);
            var stage2 = BuildStage(root.transform, "Stage2_Camp", "다람쥐 마을",
                new Vector3(Camp.center.x, 0f, Camp.center.y), new Vector3(Camp.width - 2f, 14f, Camp.height - 2f),
                "상자를 받침대 옆으로 옮겨 올라가서 케이크 되찾기", squirrelHealths, null, true);

            UnityEventTools.AddPersistentListener(plate.onPressed, stage1.MarkGimmickDone);

            var goal = pedestal.Find("CakeGoal").GetComponent<PlayerTrigger>();
            CombatArenaBuilder.SetField(goal, "hideOnEnter", cakeVisual);
            UnityEventTools.AddPersistentListener(goal.onEnter, stage2.MarkGimmickDone);

            // 파티 (Tab 교체)
            var spawn = new GameObject("FallbackSpawn").transform;
            spawn.SetParent(root.transform, false);
            spawn.position = start + Vector3.up * 0.2f;
            var party = root.AddComponent<CatParty>();
            var so = new SerializedObject(party);
            var mem = so.FindProperty("members");
            mem.arraySize = 2;
            void SetMember(int i, string name, GameObject cat, GameObject model)
            {
                var e = mem.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = name;
                e.FindPropertyRelative("input").objectReferenceValue = cat.GetComponent<PlayerInputReader>();
                e.FindPropertyRelative("follower").objectReferenceValue = cat.GetComponent<CatFollower>();
                e.FindPropertyRelative("health").objectReferenceValue = cat.GetComponent<Health>();
                e.FindPropertyRelative("visual").objectReferenceValue = model;
            }
            SetMember(0, "전투 고양이", combatCat, combatModel);
            SetMember(1, "힘센 고양이", strongCat, strongModel);
            so.FindProperty("cam").objectReferenceValue = cam.GetComponent<ThirdPersonCamera>();
            so.FindProperty("fallbackSpawn").objectReferenceValue = spawn;
            so.FindProperty("killY").floatValue = start.y - 30f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[CatPartyBuilder] 완료: 고양이 2, 다람쥐 {squirrelHealths.Length}, NavMesh {(surface.navMeshData != null ? "OK" : "실패")}");
        }

        // ───────────────────────── Helpers ─────────────────────────

        /// <summary>맵 바닥 높이 (위에서 아래로 레이). 이 빌더가 만든 것은 무시</summary>
        static Vector3 Ground(Vector2 xz)
        {
            // 위에서부터 첫 "바닥" 면. 나무(가지·잎)와 경사면은 건너뛴다 (절벽 메시가 겹쳐 있어 가장 낮은 면은 쓰면 안 됨)
            var hits = Physics.RaycastAll(new Vector3(xz.x, 40f, xz.y), Vector3.down, 120f, 1, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance);
            foreach (var h in hits)
            {
                if (h.collider.transform.root.name == RootName) continue;
                if (h.normal.y < 0.7f || h.collider.name.Contains("Tree")) continue;
                return h.point;
            }
            return new Vector3(xz.x, -6f, xz.y);
        }

        static Material Mat(string name, Color color, float smooth = 0.15f)
        {
            string path = $"{OutFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildMaterials()
        {
            woodMat = Mat("CP_Wood", new Color(0.62f, 0.42f, 0.25f));
            darkWoodMat = Mat("CP_DarkWood", new Color(0.38f, 0.25f, 0.15f));
            crateMat = Mat("CP_Crate", new Color(0.78f, 0.55f, 0.3f));
            stoneMat = Mat("CP_Stone", new Color(0.62f, 0.62f, 0.6f));
            var glow = Mat("CP_PlateGlow", new Color(1f, 0.55f, 0.2f), 0.4f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.2f) * 1.5f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = true, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Log(Transform parent, Vector3 basePos, float height, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Log";
            go.transform.SetParent(parent, false);
            go.transform.position = basePos + Vector3.up * height * 0.5f;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.transform.rotation = Quaternion.Euler(Random.Range(-2f, 2f), Random.Range(0f, 360f), Random.Range(-2f, 2f));
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ───────────────────────── Environment ─────────────────────────

        /// <summary>통나무 울타리 (마을 둘레). 동쪽 벽 가운데에 문 자리</summary>
        static void BuildPalisade(Transform env)
        {
            var wallRoot = new GameObject("Palisade").transform;
            wallRoot.SetParent(env, false);
            Random.InitState(7);

            float x0 = Camp.xMin, x1 = Camp.xMax, z0 = Camp.yMin, z1 = Camp.yMax;
            WallSegment(wallRoot, "Wall_S", new Vector2(x0, z0), new Vector2(x1, z0));
            WallSegment(wallRoot, "Wall_N", new Vector2(x0, z1), new Vector2(x1, z1));
            WallSegment(wallRoot, "Wall_W", new Vector2(x0, z0), new Vector2(x0, z1));
            WallSegment(wallRoot, "Wall_E1", new Vector2(x1, z0), new Vector2(x1, GateZ - GateWidth * 0.5f));
            WallSegment(wallRoot, "Wall_E2", new Vector2(x1, GateZ + GateWidth * 0.5f), new Vector2(x1, z1));

            // 문 기둥 + 횃불
            foreach (float dz in new[] { -1f, 1f })
            {
                Vector3 p = Ground(new Vector2(x1, GateZ + dz * (GateWidth * 0.5f + 0.25f)));
                Log(wallRoot, p + Vector3.down * 0.5f, WallHeight + 1.4f, 0.32f, darkWoodMat);
                var lightGo = new GameObject("GateTorch");
                lightGo.transform.SetParent(wallRoot, false);
                lightGo.transform.position = p + new Vector3(0.6f, WallHeight + 0.6f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.4f);
                l.intensity = 2.5f;
                l.range = 7f;
                l.shadows = LightShadows.None;
                var flame = Box(lightGo.transform, "Flame", lightGo.transform.position, Vector3.one * 0.25f,
                    AssetDatabase.LoadAssetAtPath<Material>($"{OutFolder}/CP_PlateGlow.mat"), false);
                flame.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
            }
            // 문 위 가로대
            Vector3 gateTop = Ground(new Vector2(x1, GateZ)) + Vector3.up * (WallHeight + 0.9f);
            Box(wallRoot, "GateBeam", gateTop, new Vector3(0.45f, 0.45f, GateWidth + 1.2f), darkWoodMat, false);
        }

        static void WallSegment(Transform parent, string name, Vector2 a, Vector2 b)
        {
            var seg = new GameObject(name).transform;
            seg.SetParent(parent, false);
            Vector2 d = b - a;
            float len = d.magnitude;
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 0.55f));
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i <= n; i++)
            {
                Vector2 xz = Vector2.Lerp(a, b, i / (float)n);
                Vector3 g = Ground(xz);
                minY = Mathf.Min(minY, g.y);
                maxY = Mathf.Max(maxY, g.y);
                Log(seg, g + Vector3.down * 0.6f, WallHeight + 0.6f + Random.Range(-0.25f, 0.35f), Random.Range(0.25f, 0.31f),
                    i % 3 == 0 ? darkWoodMat : woodMat);
            }
            // 가로 띠 (장식)
            Vector2 mid = (a + b) * 0.5f;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            foreach (float h in new[] { 0.8f, 2.3f })
                Box(seg, "Rail", new Vector3(mid.x, maxY + h, mid.y), new Vector3(0.18f, 0.22f, len), darkWoodMat, false,
                    Quaternion.Euler(0f, yaw, 0f));

            // 충돌: 세그먼트 전체 하나 (통나무마다 콜라이더를 두지 않는다)
            var col = new GameObject("Collider");
            col.transform.SetParent(seg, false);
            col.transform.SetPositionAndRotation(new Vector3(mid.x, (minY - 0.6f + maxY + WallHeight) * 0.5f, mid.y),
                Quaternion.Euler(0f, yaw, 0f));
            var bc = col.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.6f, maxY + WallHeight - (minY - 0.6f), len + 0.5f);
            col.isStatic = true;
        }

        /// <summary>낮은 들판 → 마을 윗단(약 0.5m 턱) 문 앞 경사로. 상자를 끌고 올라갈 수 있게</summary>
        static void BuildRamp(Transform env)
        {
            float topX = Camp.xMax + 0.3f, lowX = Camp.xMax + 4.5f;
            Vector3 top = Ground(new Vector2(topX - 1f, GateZ));
            Vector3 low = Ground(new Vector2(lowX + 1f, GateZ));
            Vector3 a = new Vector3(topX, top.y, GateZ);
            Vector3 b = new Vector3(lowX, low.y, GateZ);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var ramp = Box(env, "GateRamp", (a + b) * 0.5f + rot * (Vector3.down * 0.1f), new Vector3(GateWidth + 0.6f, 0.2f, len + 0.4f), woodMat, true, rot);
            ramp.isStatic = true;
        }

        /// <summary>케이크 받침대 (2.3m — 고양이가 바로는 못 오르고, 상자를 딛고 올라가야 한다)</summary>
        static Transform BuildPedestal(Transform env, out GameObject cakeVisual)
        {
            Vector3 g = Ground(CakePedestalXZ);
            var root = new GameObject("CakePedestal").transform;
            root.SetParent(env, false);
            root.position = g;
            var body = Box(root, "Body", g + Vector3.up * (PedestalHeight * 0.5f - 0.3f), new Vector3(3f, PedestalHeight + 0.6f, 3f), stoneMat);
            body.isStatic = true;
            Box(root, "Top", g + Vector3.up * (PedestalHeight + 0.05f), new Vector3(3.3f, 0.1f, 3.3f), woodMat, false);

            var cakePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/Prefabs/ChocolateCake.prefab");
            cakeVisual = new GameObject("BigCake");
            cakeVisual.transform.SetParent(root, false);
            cakeVisual.transform.position = g + Vector3.up * (PedestalHeight + 0.1f);
            if (cakePrefab != null)
            {
                var cake = (GameObject)PrefabUtility.InstantiatePrefab(cakePrefab, cakeVisual.transform);
                cake.transform.localPosition = Vector3.zero;
                cake.transform.localScale = Vector3.one * 4f;
                foreach (var c in cake.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            var rot = cakeVisual.AddComponent<Rotator>();
            CombatArenaBuilder.SetField(rot, "degreesPerSecond", new Vector3(0f, 30f, 0f));
            var glow = new GameObject("CakeLight").AddComponent<Light>();
            glow.transform.SetParent(cakeVisual.transform, false);
            glow.transform.localPosition = Vector3.up * 1.2f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.85f, 0.6f);
            glow.intensity = 3f;
            glow.range = 6f;

            var goal = new GameObject("CakeGoal");
            goal.transform.SetParent(root, false);
            goal.transform.position = g + Vector3.up * (PedestalHeight + 0.8f);
            var bc = goal.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(3f, 1.4f, 3f);
            var pt = goal.AddComponent<PlayerTrigger>();
            CombatArenaBuilder.SetField(pt, "message", "케이크를 되찾았다!");
            return root;
        }

        static NavMeshSurface BakeCampNavMesh(GameObject root, int playerLayer, int enemyLayer)
        {
            var navGo = new GameObject("CampNavMesh");
            navGo.transform.SetParent(root.transform, false);
            Vector3 c = Ground(Camp.center);
            navGo.transform.position = new Vector3(Camp.center.x, c.y, Camp.center.y);
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.size = new Vector3(Camp.width + 1f, 10f, Camp.height + 1f);
            surface.center = Vector3.zero;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~((1 << playerLayer) | (1 << enemyLayer));
            surface.BuildNavMesh();

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
                EditorUtility.SetDirty(surface);
            }
            return surface;
        }

        /// <summary>마을 문 (StageBarrier: 발판이 눌리면 아래로 꺼진다)</summary>
        static StageBarrier BuildGate(Transform env)
        {
            Vector3 g = Ground(new Vector2(Camp.xMax, GateZ));
            var gate = new GameObject("CampGate");
            gate.transform.SetParent(env, false);
            gate.transform.position = g;
            for (int i = 0; i < 5; i++)
            {
                float z = -GateWidth * 0.5f + 0.32f + i * (GateWidth - 0.64f) / 4f;
                Log(gate.transform, g + new Vector3(0f, -0.4f, z), WallHeight + 0.3f, 0.3f, i % 2 == 0 ? darkWoodMat : woodMat);
            }
            Box(gate.transform, "Brace", g + Vector3.up * 1.4f, new Vector3(0.2f, 0.25f, GateWidth), darkWoodMat, false,
                Quaternion.Euler(40f, 0f, 0f));
            var bc = gate.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, WallHeight * 0.5f - 0.2f, 0f);
            bc.size = new Vector3(0.6f, WallHeight + 0.4f, GateWidth + 0.2f);
            var obs = gate.AddComponent<NavMeshObstacle>();
            obs.carving = true;
            obs.center = bc.center;
            obs.size = bc.size;
            var barrier = gate.AddComponent<StageBarrier>();
            CombatArenaBuilder.SetField(barrier, "sinkDistance", WallHeight + 1f);
            return barrier;
        }

        static Rigidbody BuildCrate(Transform env, string name, Vector2 xz)
        {
            const float size = 1.2f;
            Vector3 g = Ground(xz);
            var crate = Box(env, name, g + Vector3.up * (size * 0.5f + 0.02f), Vector3.one * size, crateMat);
            // 위아래 테두리 띠 (장식)
            foreach (float y in new[] { -0.4f, 0.4f })
            {
                var band = Box(crate.transform, "Band", crate.transform.position, Vector3.one, darkWoodMat, false);
                band.transform.localScale = new Vector3(1.04f, 0.1f, 1.04f);
                band.transform.localPosition = new Vector3(0f, y, 0f);
            }
            var rb = crate.AddComponent<Rigidbody>();
            rb.mass = 60f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = crate.AddComponent<Grabbable>();
            CombatArenaBuilder.SetField(grab, "maxDragMass", 140f);
            CombatArenaBuilder.SetField(grab, "minSpeedMultiplier", 0.45f);
            var obs = crate.AddComponent<NavMeshObstacle>();
            obs.carving = true;
            obs.carveOnlyStationary = true;
            obs.size = Vector3.one;
            return rb;
        }

        static PressurePlate BuildPressurePlate(Transform env, Vector2 xz)
        {
            Vector3 g = Ground(xz);
            var root = new GameObject("PressurePlate");
            root.transform.SetParent(env, false);
            root.transform.position = g;

            var frame = Box(root.transform, "Frame", g + Vector3.up * -0.07f, new Vector3(2.4f, 0.2f, 2.4f), stoneMat);
            frame.isStatic = true;
            var plate = Box(root.transform, "Plate", g + Vector3.up * 0.045f, new Vector3(1.8f, 0.05f, 1.8f),
                AssetDatabase.LoadAssetAtPath<Material>($"{OutFolder}/CP_PlateGlow.mat"), false);
            // 상자 그림 표시 (발판 위 작은 상자 모양)
            Box(plate.transform, "Icon", g + Vector3.up * 0.075f, Vector3.one, darkWoodMat, false).transform.localScale = new Vector3(0.35f, 0.4f, 0.35f);

            var lightGo = new GameObject("PlateLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.position = g + Vector3.up * 1.2f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 4f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.6f, 0f);
            trigger.size = new Vector3(1.8f, 1.2f, 1.8f);

            var pp = root.AddComponent<PressurePlate>();
            CombatArenaBuilder.SetField(pp, "plate", plate.transform);
            CombatArenaBuilder.SetField(pp, "indicator", plate.GetComponent<Renderer>());
            CombatArenaBuilder.SetField(pp, "indicatorLight", l);
            CombatArenaBuilder.SetField(pp, "requiredMass", 30f);
            return pp;
        }

        // ───────────────────────── Camera / HUD / Stage ─────────────────────────

        static void DisableOldCamera()
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.transform.root.name == RootName) continue;
                c.enabled = false;
                var al = c.GetComponent<AudioListener>();
                if (al != null) al.enabled = false;
                if (c.CompareTag("MainCamera")) c.tag = "Untagged";
            }
        }

        static Camera BuildCamera(Transform root)
        {
            var go = new GameObject("CatCamera");
            go.transform.SetParent(root, false);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var orbit = go.AddComponent<ThirdPersonCamera>();
            CombatArenaBuilder.SetField(orbit, "collisionMask", (LayerMask)1);
            CombatArenaBuilder.SetField(orbit, "pivotHeight", 0.95f);
            CombatArenaBuilder.SetField(orbit, "distance", 4.6f);
            CombatArenaBuilder.SetField(orbit, "maxDistance", 8f);
            CombatArenaBuilder.SetField(orbit, "startPitch", 14f);
            CombatArenaBuilder.SetField(orbit, "startYaw", -90f);
            Vector3 start = Ground(StartXZ);
            go.transform.position = start + new Vector3(4.5f, 2.2f, 0f);
            go.transform.LookAt(start + Vector3.up);
            return cam;
        }

        static StageHUD BuildHud(Transform root)
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(root, false);
            var hud = go.AddComponent<StageHUD>();
            CombatArenaBuilder.SetField(hud, "openingBanner", "고양이 원정대");
            CombatArenaBuilder.SetField(hud, "openingToast", "다람쥐들이 케이크를 훔쳐 갔다! Tab으로 고양이를 바꿔 가며 되찾자");
            var so = new SerializedObject(hud);
            so.FindProperty("controlsText").stringValue =
                "WASD 이동 · Shift 달리기 · Space 점프 · Tab 고양이 교체 · 마우스 시점 · Alt 커서\n" +
                "전투 고양이: 좌클릭 꾹 눌러 컵케이크 던지기 · 힘센 고양이: E 상자 잡기/놓기";
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        static StageController BuildStage(Transform root, string name, string title, Vector3 centerXZ, Vector3 size,
            string hint, Health[] monsters, StageBarrier exit, bool final)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            Vector3 g = Ground(new Vector2(centerXZ.x, centerXZ.z));
            go.transform.position = new Vector3(centerXZ.x, g.y + size.y * 0.25f, centerXZ.z);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = size;
            var stage = go.AddComponent<StageController>();
            var so = new SerializedObject(stage);
            so.FindProperty("stageTitle").stringValue = title;
            so.FindProperty("gimmickHint").stringValue = hint;
            so.FindProperty("requiresGimmick").boolValue = true;
            so.FindProperty("isFinalStage").boolValue = final;
            so.FindProperty("exitBarrier").objectReferenceValue = exit;
            var m = so.FindProperty("monsters");
            m.arraySize = monsters.Length;
            for (int i = 0; i < monsters.Length; i++) m.GetArrayElementAtIndex(i).objectReferenceValue = monsters[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return stage;
        }

        // ───────────────────────── Cats ─────────────────────────

        static GameObject BuildCat(Transform parent, string name, string prefabPath, RuntimeAnimatorController controller,
            Vector3 pos, Quaternion rot, int layer, Transform cam, out GameObject model)
        {
            var cat = new GameObject(name);
            cat.tag = "Player";
            cat.transform.SetParent(parent, false);
            cat.transform.SetPositionAndRotation(Ground(new Vector2(pos.x, pos.z)) + Vector3.up * 0.05f, rot);

            var rb = cat.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var capsule = cat.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = CatHeight;
            capsule.center = new Vector3(0f, CatHeight * 0.5f, 0f);
            cat.AddComponent<PlayerInputReader>();
            var obstacle = cat.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.4f;
            obstacle.height = CatHeight;
            obstacle.center = capsule.center;
            obstacle.carving = false;

            var motor = cat.AddComponent<PlayerMotor>();
            CombatArenaBuilder.SetField(motor, "walkSpeed", 2.4f);
            CombatArenaBuilder.SetField(motor, "runSpeed", 4.8f);
            CombatArenaBuilder.SetField(motor, "standHeight", CatHeight);
            CombatArenaBuilder.SetField(motor, "crouchHeight", 0.75f);
            CombatArenaBuilder.SetField(motor, "jumpHeight", 1.0f);
            CombatArenaBuilder.SetField(motor, "maxClimbHeight", 1.6f);
            CombatArenaBuilder.SetField(motor, "moveSpace", cam);

            var health = cat.AddComponent<Health>();
            CombatArenaBuilder.SetField(health, "maxHealth", 100f);
            CombatArenaBuilder.SetField(health, "aimHeight", 0.6f);
            CombatArenaBuilder.SetField(health, "invulnerableAfterHit", 0.6f);

            var follower = cat.AddComponent<CatFollower>();
            CombatArenaBuilder.SetField(follower, "obstacleMask", (LayerMask)1);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cat.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * CatScale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var pa = model.AddComponent<PlayerAnimator>();
            CombatArenaBuilder.SetField(pa, "handHeight", 0.45f);
            CombatArenaBuilder.SetField(pa, "handSpread", 0.18f);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            CombatArenaBuilder.SetLayerRecursive(cat, layer);
            return cat;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static void SetupCombatCat(GameObject cat, GameObject model, RamenProjectile cupcake, int enemyLayer)
        {
            // 손에 든 컵케이크
            GameObject held = null;
            var hand = FindDeep(model.transform, "Arm02_R_end") ?? FindDeep(model.transform, "Arm02_R");
            var cakePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/Prefabs/StrawberrySpongeCake.prefab");
            if (hand != null && cakePrefab != null)
            {
                held = new GameObject("HeldCupcake");
                held.transform.SetParent(hand, false);
                held.transform.localPosition = Vector3.zero;
                held.transform.rotation = Quaternion.identity;
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(cakePrefab, held.transform);
                foreach (var c in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                // 월드 크기 약 0.3m (손 뼈 스케일과 무관하게)
                var rs = vis.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(vis.transform.position, Vector3.one * 0.25f);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float worldSize = Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
                vis.transform.localScale *= 0.32f / worldSize;
                vis.transform.position = hand.position + Vector3.down * 0.08f;
                held.layer = cat.layer;
                CombatArenaBuilder.SetLayerRecursive(held, cat.layer);
            }

            int enemyMask = 1 << enemyLayer;
            var line = CombatArenaBuilder.CreatePreviewLine(cat.transform, "CupcakeTrajectory", 0.06f, new Color(1f, 0.6f, 0.8f), false);
            var ring = CombatArenaBuilder.CreatePreviewLine(cat.transform, "CupcakeLandingRing", 0.05f, new Color(1f, 0.45f, 0.7f), true);
            var thrower = cat.AddComponent<RamenThrower>();
            var so = new SerializedObject(thrower);
            so.FindProperty("ramenPrefab").objectReferenceValue = cupcake;
            so.FindProperty("trajectoryLine").objectReferenceValue = line;
            so.FindProperty("landingRing").objectReferenceValue = ring;
            so.FindProperty("heldVisual").objectReferenceValue = held;
            so.FindProperty("hitMask").intValue = 1 | enemyMask;
            so.FindProperty("enemyMask").intValue = enemyMask;
            so.FindProperty("cooldown").floatValue = 0.8f;
            so.FindProperty("maxRange").floatValue = 15f;
            so.FindProperty("throwOffset").vector3Value = new Vector3(0f, 1.0f, 0.3f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupStrongCat(GameObject cat)
        {
            var grab = cat.AddComponent<PlayerGrab>();
            CombatArenaBuilder.SetField(grab, "grabMask", (LayerMask)1);
            CombatArenaBuilder.SetField(grab, "reach", 0.6f);
            CombatArenaBuilder.SetField(grab, "playerMassDominance", 3f);
            // 힘센 고양이는 조금 묵직하게
            CombatArenaBuilder.SetField(cat.GetComponent<PlayerMotor>(), "jumpHeight", 0.9f);
            CombatArenaBuilder.SetField(cat.GetComponent<Health>(), "maxHealth", 140f);
        }

        // ───────────────────────── Squirrels ─────────────────────────

        static Health BuildSquirrel(Transform parent, int index, Vector2 xz, RuntimeAnimatorController controller, int enemyLayer, Transform player)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{Kit}/Prefabs/LittleSquirrel_{(index % 2 == 0 ? "Angry" : "Idle")}.prefab");
            if (prefab == null) return null;

            Vector3 p = Ground(xz);
            if (NavMesh.SamplePosition(p, out var nh, 3f, NavMesh.AllAreas)) p = nh.position;

            var root = new GameObject($"Squirrel_{index + 1}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * SquirrelScale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.36f;
            col.height = 1.15f;
            col.center = new Vector3(0f, 0.575f, 0f);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.36f;
            agent.height = 1.15f;
            agent.angularSpeed = 540f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 0.3f;

            var health = root.AddComponent<Health>();
            CombatArenaBuilder.SetField(health, "maxHealth", 60f);
            CombatArenaBuilder.SetField(health, "aimHeight", 0.6f);
            var bar = root.AddComponent<HealthBar>();
            CombatArenaBuilder.SetField(bar, "height", 1.55f);
            CombatArenaBuilder.SetField(bar, "size", new Vector2(0.7f, 0.07f));

            var brain = root.AddComponent<MonsterBrain>();
            CombatArenaBuilder.SetField(brain, "target", player);
            CombatArenaBuilder.SetField(brain, "animator", anim);
            CombatArenaBuilder.SetField(brain, "obstacleMask", (LayerMask)1);
            CombatArenaBuilder.SetField(brain, "eyeHeight", 0.9f);
            CombatArenaBuilder.SetField(brain, "viewDistance", 10f);
            CombatArenaBuilder.SetField(brain, "hearDistance", 3.5f);
            CombatArenaBuilder.SetField(brain, "patrolSpeed", 1.5f);
            CombatArenaBuilder.SetField(brain, "chaseSpeed", 3.6f);
            CombatArenaBuilder.SetField(brain, "patrolRadius", 4f);
            CombatArenaBuilder.SetField(brain, "leashDistance", 16f);
            CombatArenaBuilder.SetField(brain, "attackRange", 1.4f);
            CombatArenaBuilder.SetField(brain, "attackHitRange", 1.9f);
            CombatArenaBuilder.SetField(brain, "attackDamage", 10f);
            CombatArenaBuilder.SetField(brain, "attackWindup", 0.45f);
            CombatArenaBuilder.SetField(brain, "attackCooldown", 1.4f);
            CombatArenaBuilder.SetField(brain, "attackKnockback", 3.5f);
            CombatArenaBuilder.SetField(brain, "respawnAfterDeath", false);

            CombatArenaBuilder.SetLayerRecursive(root, enemyLayer);
            return health;
        }

        // ───────────────────────── Cupcake projectile ─────────────────────────

        static RamenProjectile EnsureCupcakePrefab(int enemyLayer)
        {
            var cakePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/Prefabs/StrawberrySpongeCake.prefab");
            var splash = BuildCreamSplash();

            var root = new GameObject("Cupcake");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.2f;

            if (cakePrefab != null)
            {
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(cakePrefab, root.transform);
                foreach (var c in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                vis.transform.localScale = Vector3.one * 1.7f; // 0.25m → 약 0.42m (잘 보이게)
                var b = new Bounds(vis.transform.position, Vector3.zero);
                bool first = true;
                foreach (var r in vis.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                vis.transform.localPosition = -b.center;
            }

            var proj = root.AddComponent<RamenProjectile>();
            CombatArenaBuilder.SetField(proj, "splashVfx", splash);
            CombatArenaBuilder.SetField(proj, "centerDamage", 28f);
            CombatArenaBuilder.SetField(proj, "edgeDamage", 10f);
            CombatArenaBuilder.SetField(proj, "splashRadius", 1.7f);
            CombatArenaBuilder.SetField(proj, "explosionForce", 3f);

            PrefabUtility.SaveAsPrefabAsset(root, CupcakePrefabPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(CupcakePrefabPath)?.GetComponent<RamenProjectile>();
        }

        /// <summary>생크림·딸기 조각 튀는 효과</summary>
        static GameObject BuildCreamSplash()
        {
            var root = new GameObject("CreamSplash");
            var mat = CombatArenaBuilder.AlphaParticle("RamenSplash_Mat");

            void Burst(string name, Color color, int count, Vector2 speed, Vector2 size, Vector2 life, float gravity, bool stretch)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
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
                shape.angle = 60f;
                shape.radius = 0.15f;
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
                r.sharedMaterial = mat;
            }

            Burst("Cream", new Color(1f, 0.97f, 0.92f, 1f), 36, new Vector2(2f, 5.5f), new Vector2(0.08f, 0.18f), new Vector2(0.4f, 0.8f), 1.5f, false);
            Burst("Strawberry", new Color(1f, 0.3f, 0.45f, 1f), 14, new Vector2(2.5f, 5f), new Vector2(0.06f, 0.12f), new Vector2(0.5f, 0.9f), 1.8f, false);
            Burst("Sprinkles", new Color(1f, 0.7f, 0.85f, 1f), 24, new Vector2(3f, 6f), new Vector2(0.03f, 0.05f), new Vector2(0.4f, 0.7f), 1.2f, true);

            var flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(root.transform, false);
            flash.transform.localPosition = Vector3.up * 0.3f;
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.6f, 0.75f);
            flash.intensity = 4f;
            flash.range = 4f;
            flash.gameObject.AddComponent<LightFade>();

            PrefabUtility.SaveAsPrefabAsset(root, CreamSplashPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(CreamSplashPath);
        }

        // ───────────────────────── Animator controllers ─────────────────────────

        internal static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimFolder}/{name}.anim");

        static AnimatorController LoadOrClear(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return AnimatorController.CreateAnimatorControllerAtPath(path);
            // GUID 유지: 지우지 않고 내용만 비운다
            for (int i = controller.layers.Length - 1; i >= 0; i--) controller.RemoveLayer(i);
            for (int i = controller.parameters.Length - 1; i >= 0; i--) controller.RemoveParameter(i);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub != null && sub != controller) Object.DestroyImmediate(sub, true);
            controller.AddLayer("Base Layer");
            return controller;
        }

        static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }

        static BlendTree Loco(AnimatorController c, string name, AnimationClip idle, AnimationClip walk, AnimationClip run, float walkAt, float runAt)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, walkAt);
            tree.AddChild(run, runAt);
            return tree;
        }

        /// <summary>PlayerAnimator 파라미터와 같은 구성의 플레이어 컨트롤러 (animal = "Cat" / "Squirrel")</summary>
        internal static RuntimeAnimatorController BuildPlayerController(string path, string animal, float walkSpeed, float runSpeed)
        {
            var c = LoadOrClear(path);
            foreach (var f in new[] { "Speed", "VerticalSpeed" }) c.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (var b in new[] { "Grounded", "Crouch", "Grab" }) c.AddParameter(b, AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "Jump", "Climb", "Hit", "Attack", "Skill", "IdleFidget" }) c.AddParameter(t, AnimatorControllerParameterType.Trigger);
            c.AddParameter("IdleVariant", AnimatorControllerParameterType.Int);

            var layers = c.layers;
            layers[0].iKPass = true;
            c.layers = layers;
            var sm = c.layers[0].stateMachine;

            var idle = Clip($"02_Idle_{animal}_Copy");
            var walk = Clip($"03_Walk_{animal}_Copy");
            var run = Clip($"04_Run_{animal}_Copy");
            var jump = Clip($"05_Jump_{animal}_Copy");
            var greet = Clip($"06_Greeting_{animal}_Copy");

            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = Loco(c, animal + "Loco", idle, walk, run, walkSpeed, runSpeed);
            sm.defaultState = loco;

            var crouch = sm.AddState("Crouch", new Vector3(300, 120, 0));
            crouch.motion = walk;
            crouch.speed = 0.6f;
            T(loco, crouch, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");
            T(crouch, loco, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");

            var air = sm.AddState("Air", new Vector3(560, 0, 0));
            air.motion = jump;
            T(loco, air, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(loco, air, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(crouch, air, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(air, loco, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            var climb = sm.AddState("Climb", new Vector3(560, -120, 0));
            climb.motion = jump;
            climb.speed = 1.8f;
            var anyClimb = sm.AddAnyStateTransition(climb);
            anyClimb.hasExitTime = false;
            anyClimb.duration = 0.05f;
            anyClimb.canTransitionToSelf = false;
            anyClimb.AddCondition(AnimatorConditionMode.If, 0, "Climb");
            var climbOut = climb.AddTransition(loco);
            climbOut.hasExitTime = true;
            climbOut.exitTime = 0.85f;
            climbOut.duration = 0.1f;

            var variants = new[] { greet, Clip($"07_Eat_{animal}_Copy"), Clip($"08_Sleep01_{animal}_Copy"), Clip($"09_Sleep02_{animal}_Copy") };
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null) continue;
                var st = sm.AddState($"IdleVariant{i + 1}", new Vector3(40, -120 + i * 70, 0));
                st.motion = variants[i];
                var enter = T(loco, st, 0.3f);
                enter.AddCondition(AnimatorConditionMode.If, 0, "IdleFidget");
                enter.AddCondition(AnimatorConditionMode.Equals, i + 1, "IdleVariant");
                enter.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                var done = st.AddTransition(loco);
                done.hasExitTime = true;
                done.exitTime = i >= 2 ? 2.9f : 0.95f; // 잠자기는 3번 반복
                done.duration = 0.3f;
                T(st, loco, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                T(st, air, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            }

            // 상체: 던지기(인사 동작을 빠르게) · 피격
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMaskPath);
            c.AddLayer("UpperBody");
            layers = c.layers;
            var upper = layers[layers.Length - 1];
            upper.avatarMask = mask;
            upper.defaultWeight = 1f;
            layers[layers.Length - 1] = upper;
            c.layers = layers;
            var usm = c.layers[layers.Length - 1].stateMachine;
            var empty = usm.AddState("Empty", new Vector3(300, 0, 0));
            usm.defaultState = empty;
            void Action(string name, AnimationClip clip, string trigger, float speed, float offset, float exit, float y)
            {
                var st = usm.AddState(name, new Vector3(560, y, 0));
                st.motion = clip;
                st.speed = speed;
                var enter = usm.AddAnyStateTransition(st);
                enter.hasExitTime = false;
                enter.duration = 0.05f;
                enter.offset = offset;
                enter.canTransitionToSelf = true;
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
                var back = st.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = exit;
                back.duration = 0.15f;
            }
            Action("Attack", greet, "Attack", 2.6f, 0.15f, 0.5f, -60);
            Action("Skill", greet, "Skill", 1.6f, 0.1f, 0.7f, 30);
            Action("Hit", jump, "Hit", 2.2f, 0.4f, 0.4f, 120);

            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>MonsterBrain 파라미터(Speed/Hit/Attack/Dead)에 맞춘 다람쥐 컨트롤러</summary>
        static RuntimeAnimatorController BuildSquirrelController()
        {
            var c = LoadOrClear(SquirrelControllerPath);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            var sm = c.layers[0].stateMachine;

            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = Loco(c, "SquirrelLoco", Clip("02_Idle_Squirrel_Copy"), Clip("03_Walk_Squirrel_Copy"), Clip("04_Run_Squirrel_Copy"), 1.4f, 3.4f);
            sm.defaultState = loco;

            var attack = sm.AddState("Attack", new Vector3(560, -80, 0));
            attack.motion = Clip("05_Jump_Squirrel_Copy"); // 덮치기
            attack.speed = 1.4f;
            var toAtk = sm.AddAnyStateTransition(attack);
            toAtk.hasExitTime = false;
            toAtk.duration = 0.05f;
            toAtk.canTransitionToSelf = false;
            toAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            toAtk.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var atkBack = attack.AddTransition(loco);
            atkBack.hasExitTime = true;
            atkBack.exitTime = 0.95f;
            atkBack.duration = 0.15f;

            var hit = sm.AddState("Hit", new Vector3(560, 40, 0));
            hit.motion = Clip("06_Greeting_Squirrel_Copy");
            hit.speed = 2.2f;
            var toHit = sm.AddAnyStateTransition(hit);
            toHit.hasExitTime = false;
            toHit.duration = 0.05f;
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            toHit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var hitBack = hit.AddTransition(loco);
            hitBack.hasExitTime = true;
            hitBack.exitTime = 0.45f;
            hitBack.duration = 0.15f;

            var die = sm.AddState("Die", new Vector3(560, 160, 0));
            die.motion = Clip("08_Sleep01_Squirrel_Copy"); // 기절해서 잠든 모습
            var toDie = sm.AddAnyStateTransition(die);
            toDie.hasExitTime = false;
            toDie.duration = 0.15f;
            toDie.canTransitionToSelf = false;
            toDie.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            T(die, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            AssetDatabase.SaveAssets();
            return c;
        }
    }
}
