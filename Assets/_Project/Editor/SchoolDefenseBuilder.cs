using System.Linq;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using LNStudy.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 지금 열린 학교 씬(Haons SD 캐릭터 3명)에 "학교 방어" 게임을 얹는다.
    /// - 플레이어: prf_Set Costume03 Unity-Chan. Tab으로 평상복 → 대검(Costume02 UTC WGS) → 쌍검(Costume02 UTC WTD) 모드 전환
    ///   (씬에 있는 세 인스턴스를 Player_UTC 아래로 옮겨 외형으로 쓴다)
    /// - 좌클릭 평타 콤보, Q/우클릭 스킬 (대검: 내려찍기, 쌍검: 관통 돌진)
    /// - 친구(미사키·유코)를 노리고 복도 양 끝 문에서 양(Cozy Sheep) 떼가 웨이브로 들어온다
    /// 다시 실행해도 된다: Player_UTC는 재사용하고 컴포넌트 설정만 덮어쓰며, SchoolDefense 루트(카메라 제외)는 새로 만든다.
    /// </summary>
    public static class SchoolDefenseBuilder
    {
        const string Out = "Assets/_Project/SchoolDefense";
        const string Haons = "Assets/Haons SD series Pack/Animation";
        const string SheepPrefab = "Assets/Cozy_Sheep/Sheep_Assets/Sheep_Prefab/SheepPrefab.prefab";
        const string SheepAnims = "Assets/Cozy_Sheep/Sheep_Assets/Animations";
        const string DustMatPath = "Assets/_Project/MuffinTime/MT_Dust.mat";

        // 작은 캐릭터용 NavMesh 에이전트 타입 (기본 Humanoid는 반지름 0.5라 교실 문(폭 1.27m)을 거의 못 지나간다)
        const string AgentTypeName = "SchoolSmall";
        const int AgentTypeId = 1347293;
        static readonly Vector3 SpawnNorth = new Vector3(7.45f, 0f, 8.4f);
        static readonly Vector3 SpawnSouth = new Vector3(7.45f, 0f, -20.6f);

        [MenuItem("LNStudy/School Defense/Setup In Current Scene")]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            CombatArenaBuilder.EnsureFolder(Out);
            int playerLayer = CombatArenaBuilder.EnsureLayer("Player");
            int enemyLayer = CombatArenaBuilder.EnsureLayer("Enemy");
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMatPath);

            // ── 캐릭터 찾기
            var player = GameObject.Find("Player_UTC");
            GameObject Find(string n)
            {
                if (player != null) { var t = player.transform.Find(n); if (t != null) return t.gameObject; }
                return GameObject.Find(n);
            }
            var utc = Find("prf_Set Costume03 Unity-Chan");
            var wgs = Find("prf_Set Costume02 UTC WGS");
            var wtd = Find("prf_Set Costume02 UTC WTD");
            var misaki = GameObject.Find("prf_Set Costume03 Misaki");
            var yuko = GameObject.Find("prf_Set Costume03 Yuko");
            if (utc == null || wgs == null || wtd == null || misaki == null || yuko == null)
            {
                Debug.LogError("[SchoolDefense] 씬에서 캐릭터를 찾지 못했습니다 (Costume03 Unity-Chan / Costume02 UTC WGS / UTC WTD / Misaki / Yuko)");
                return;
            }

            // ── 애니메이터
            var baseCtrl = BuildUtcController(Out + "/UTC_Base.controller",
                Clip("Common/StandA.FBX", "StandA@loop"), Clip("Common/WalkA_Front.FBX", "Walk_front@loop"), Clip("Common/RunA_Front.FBX", "RunA_front@loop"),
                new[] { Clip("WeaponMaster Twin dagger(WTD)/WTD_AttackA np.FBX", "WTD_AttackA1 np"), Clip("WeaponMaster Twin dagger(WTD)/WTD_AttackA np.FBX", "WTD_AttackA2 np") },
                null, 1f);
            var wgsCtrl = BuildUtcController(Out + "/UTC_WGS.controller",
                Clip("WeaponMaster Greatsword(WGS)/WGS_Stand.FBX", "WGS_Stand@loop"), Clip("WeaponMaster Greatsword(WGS)/WGS_Walk_Front.FBX", "WGS_Walk_Front@Loop"),
                Clip("WeaponMaster Greatsword(WGS)/WGS_Run_Front.FBX", "WGS_Run_Front@Loop"),
                Enumerable.Range(1, 4).Select(i => Clip("WeaponMaster Greatsword(WGS)/WGS_AttackA_set.FBX", "WGS_attackA" + i)).ToArray(),
                Clip("WeaponMaster Greatsword(WGS)/WGS_AttackA_set.FBX", "WGS_attackA5"), 1f);
            var wtdCtrl = BuildUtcController(Out + "/UTC_WTD.controller",
                Clip("WeaponMaster Twin dagger(WTD)/WTD_Stand.FBX", "WTD_Stand@loop"), Clip("WeaponMaster Twin dagger(WTD)/WTD_Walk_Front.FBX", "WTD_Walk_Front@Loop"),
                Clip("WeaponMaster Twin dagger(WTD)/WTD_Run_Front.FBX", "WTD_Run_Front@Loop"),
                Enumerable.Range(1, 3).Select(i => Clip("WeaponMaster Twin dagger(WTD)/WTD_AttackA np.FBX", $"WTD_AttackA{i} np")).ToArray(),
                Clip("WeaponMaster Twin dagger(WTD)/WTD_AttackA np.FBX", "WTD_AttackA3 np"), 1.6f);
            int agentType = EnsureAgentType();
            var lambCtrl = BuildLambController();
            var friendCtrl = BuildFriendController();

            // ── 몬스터 프리팹
            var lamb = BuildLambPrefab("Lamb", 0.5f, 45f, 7f, enemyLayer, lambCtrl, dust, agentType);
            var bigLamb = BuildLambPrefab("BigLamb", 0.8f, 220f, 14f, enemyLayer, lambCtrl, dust, agentType);

            // ── SchoolDefense 루트 (다시 만들기)
            var old = GameObject.Find("SchoolDefense");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("SchoolDefense").transform;

            // ── 카메라: 씬 카메라에 3인칭 카메라를 붙인다 (URP 볼륨·후처리 설정 유지)
            Camera cam = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.enabled) { cam = c; break; }
            if (cam == null) cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 55f;

            // ── 플레이어
            Vector3 startPos = utc.transform.position;
            Quaternion startRot = Quaternion.Euler(0f, utc.transform.eulerAngles.y, 0f);
            if (player == null)
            {
                player = new GameObject("Player_UTC");
                player.transform.SetPositionAndRotation(new Vector3(startPos.x, 0.02f, startPos.z), startRot);
            }
            player.tag = "Player";
            SetupEngagement(player, 2, 2.3f);
            foreach (var v in new[] { utc, wgs, wtd })
            {
                v.transform.SetParent(player.transform, false);
                v.transform.localPosition = Vector3.zero;
                v.transform.localRotation = Quaternion.identity;
            }
            var rb = Get<Rigidbody>(player);
            rb.mass = 1f;
            var cap = Get<CapsuleCollider>(player);
            cap.radius = 0.25f;
            cap.height = 1.2f;
            cap.center = new Vector3(0f, 0.6f, 0f);
            Get<PlayerInputReader>(player);
            var motor = Get<PlayerMotor>(player);
            Set(motor, "moveSpace", cam.transform);
            Set(motor, "walkSpeed", 1.8f);
            Set(motor, "runSpeed", 4.2f);
            Set(motor, "jumpHeight", 0.8f);
            Set(motor, "standHeight", 1.2f);
            Set(motor, "crouchHeight", 1.2f);
            Set(motor, "maxClimbHeight", 0.9f);
            var hp = Get<Health>(player);
            Set(hp, "maxHealth", 150f);
            Set(hp, "aimHeight", 0.7f);
            Set(hp, "invulnerableAfterHit", 0.5f);

            void SetupVisual(GameObject v, RuntimeAnimatorController ctrl)
            {
                var a = v.GetComponent<Animator>();
                a.runtimeAnimatorController = ctrl;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (v.GetComponent<PlayerAnimator>() == null) v.AddComponent<PlayerAnimator>();
                var j = v.GetComponent<CharacterJuice>();
                if (j == null) j = v.AddComponent<CharacterJuice>();
                Set(j, "dustMaterial", dust);
                Set(j, "strideLength", 0.45f);
            }
            SetupVisual(utc, baseCtrl);
            SetupVisual(wgs, wgsCtrl);
            SetupVisual(wtd, wtdCtrl);

            var sw = Get<CharacterSwitcher>(player);
            var sso = new SerializedObject(sw);
            var chars = sso.FindProperty("characters");
            chars.arraySize = 3;
            void Entry(int i, string n, GameObject v)
            {
                var e = chars.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = n;
                e.FindPropertyRelative("visualRoot").objectReferenceValue = v;
                e.FindPropertyRelative("standHeight").floatValue = 1.2f;
                e.FindPropertyRelative("crouchHeight").floatValue = 1.2f;
                e.FindPropertyRelative("radius").floatValue = 0.25f;
                e.FindPropertyRelative("cameraPivotHeight").floatValue = 0.95f;
            }
            Entry(0, "평상복", utc);
            Entry(1, "대검 모드", wgs);
            Entry(2, "쌍검 모드", wtd);
            sso.FindProperty("pets").arraySize = 0;
            sso.FindProperty("switchCooldown").floatValue = 0.35f;
            sso.FindProperty("cameraRig").objectReferenceValue = Get<ThirdPersonCamera>(cam.gameObject);
            sso.ApplyModifiedPropertiesWithoutUndo();

            // 돌진 잔상
            var trailT = player.transform.Find("DashTrail");
            if (trailT == null) { trailT = new GameObject("DashTrail").transform; trailT.SetParent(player.transform, false); }
            trailT.localPosition = new Vector3(0f, 0.6f, 0f);
            var trail = Get<TrailRenderer>(trailT.gameObject);
            trail.time = 0.25f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0f));
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(0.6f, 0.9f, 1f), 0f), new GradientColorKey(new Color(1f, 0.6f, 0.9f), 1f) },
                         new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
            trail.sharedMaterial = CombatArenaBuilder.AdditiveParticle("DashTrail", Color.white);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;

            Get<DoorInteractor>(player);
            var combat = Get<SwordCombat>(player);
            ConfigureCombat(combat, enemyLayer, trail, dust);
            CombatArenaBuilder.SetLayerRecursive(player, playerLayer);

            // 3인칭 카메라
            var orbit = Get<ThirdPersonCamera>(cam.gameObject);
            Set(orbit, "target", player.transform);
            Set(orbit, "pivotHeight", 0.95f);
            Set(orbit, "distance", 3.6f);
            Set(orbit, "minDistance", 1.4f);
            Set(orbit, "maxDistance", 6f);
            Set(orbit, "startPitch", 16f);
            Set(orbit, "startYaw", player.transform.eulerAngles.y);
            Set(orbit, "collisionMask", (LayerMask)1);
            cam.transform.position = player.transform.position + player.transform.rotation * new Vector3(0f, 1.8f, -3.4f);
            cam.transform.LookAt(player.transform.position + Vector3.up * 0.95f);

            // ── 친구
            FriendNPC Friend(GameObject go, string name)
            {
                go.tag = "Player";
                var c = Get<CapsuleCollider>(go);
                c.radius = 0.25f;
                c.height = 1.05f;
                c.center = new Vector3(0f, 0.53f, 0f);
                var h = Get<Health>(go);
                Set(h, "maxHealth", 100f);
                Set(h, "aimHeight", 0.6f);
                var bar = Get<HealthBar>(go);
                Set(bar, "height", 1.35f);
                Set(bar, "size", new Vector2(0.6f, 0.06f));
                Set(bar, "fillColor", new Color(0.4f, 0.9f, 0.45f));
                Set(bar, "hideAfter", 999f);
                // 도망다니기: NavMeshAgent (장애물 컴포넌트와는 같이 못 씀)
                var obs = go.GetComponent<NavMeshObstacle>();
                if (obs != null) Object.DestroyImmediate(obs);
                var ag = Get<NavMeshAgent>(go);
                ag.agentTypeID = agentType;
                ag.radius = 0.25f;
                ag.height = 1.05f;
                ag.speed = 2.6f;
                ag.acceleration = 14f;
                ag.angularSpeed = 720f;
                ag.stoppingDistance = 0.2f;
                ag.avoidancePriority = 20;
                var an = go.GetComponent<Animator>();
                if (an != null) { an.runtimeAnimatorController = friendCtrl; an.applyRootMotion = false; }
                var f = Get<FriendNPC>(go);
                Set(f, "displayName", name);
                Set(f, "dustMaterial", dust);
                Set(f, "tearMaterial", TearMaterial());
                var fso = new SerializedObject(f);
                var lines = fso.FindProperty("talkLines");
                string[] text = name == "유코" ? new[] { "양들이 요즘 이상해졌어..." } : new[] { "이게 무슨 소리지?" };
                lines.arraySize = text.Length;
                for (int li = 0; li < text.Length; li++) lines.GetArrayElementAtIndex(li).stringValue = text[li];
                fso.FindProperty("talkShape").stringValue = name == "유코" ? "Expression.Fsad" : "Expression.Fsup";
                fso.FindProperty("talkSfx").stringValue = name == "유코" ? "" : "rumble";
                fso.ApplyModifiedPropertiesWithoutUndo();
                CombatArenaBuilder.SetLayerRecursive(go, playerLayer);
                SetupEngagement(go, 1, 2.0f);
                return f;
            }
            var fMisaki = Friend(misaki, "미사키");
            var fYuko = Friend(yuko, "유코");

            // ── 문: 정적 배칭에서 빼고 여닫이 컴포넌트
            int doorCount = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("a door")) continue;
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);
                Get<SwingDoor>(t.gameObject);
                doorCount++;
            }

            // ── 벽 충돌체: convex 메시 충돌체는 문 구멍까지 막아 버린다 → 정적인 것은 전부 non-convex로
            int unconvex = 0;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!mc.convex || mc.attachedRigidbody != null) continue;
                mc.convex = false;
                EditorUtility.SetDirty(mc);
                unconvex++;
            }
            // 문짝은 단순한 판이라 convex로 둔다 (양을 밀어내는 겹침 계산이 convex에서 정확함)
            foreach (var d in Object.FindObjectsByType<SwingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var mc in d.GetComponentsInChildren<MeshCollider>(true)) { mc.convex = true; EditorUtility.SetDirty(mc); }

            // ── NavMesh: 학교 바닥 전체. 문짝은 열고 닫히므로 빼고 굽는다 (문 앞에 오면 양·친구가 스스로 연다)
            Bounds school = SchoolBounds();
            var navGo = new GameObject("SchoolNavMesh");
            navGo.transform.SetParent(root, false);
            navGo.transform.position = school.center;
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentType;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = Vector3.zero;
            surface.size = new Vector3(school.size.x + 2f, Mathf.Max(4f, school.size.y + 1f), school.size.z + 2f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~((1 << playerLayer) | (1 << enemyLayer));
            BakeSchoolNavMesh(surface);

            // ── 스폰 지점
            Transform Spawn(string n, Vector3 p, float yaw)
            {
                var t = new GameObject(n).transform;
                t.SetParent(root, false);
                var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
                if (NavMesh.SamplePosition(p, out var hit, 3f, filter)) p = hit.position;
                t.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                return t;
            }
            var sN = Spawn("Spawn_NorthDoor", SpawnNorth, 180f);
            var sS = Spawn("Spawn_SouthDoor", SpawnSouth, 0f);

            // ── HUD
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(root, false);
            var hud = hudGo.AddComponent<StageHUD>();
            var hso = new SerializedObject(hud);
            hso.FindProperty("playerHealth").objectReferenceValue = hp;
            hso.FindProperty("switcher").objectReferenceValue = sw;
            hso.FindProperty("openingBanner").stringValue = "방과 후의 학교";
            hso.FindProperty("openingToast").stringValue = "친구들에게 말을 걸어 보자 (F)";
            hso.FindProperty("controlsText").stringValue =
                "WASD 이동 · Shift 달리기 · Space 점프 · Tab 모드 변환 (평상복 → 대검 → 쌍검) · F 대화 / 문 열기·닫기 · Alt 커서\n" +
                "좌클릭 평타 · Q/우클릭 스킬 (대검: 내려찍기 · 쌍검: 관통 돌진)";
            hso.ApplyModifiedPropertiesWithoutUndo();

            // ── 진행
            var gameGo = new GameObject("Game");
            gameGo.transform.SetParent(root, false);
            var game = gameGo.AddComponent<SchoolDefenseGame>();
            var gso = new SerializedObject(game);
            gso.FindProperty("lambPrefab").objectReferenceValue = lamb;
            gso.FindProperty("bigLambPrefab").objectReferenceValue = bigLamb;
            var sp = gso.FindProperty("spawnPoints");
            sp.arraySize = 2;
            sp.GetArrayElementAtIndex(0).objectReferenceValue = sN;
            sp.GetArrayElementAtIndex(1).objectReferenceValue = sS;
            var fr = gso.FindProperty("friends");
            fr.arraySize = 2;
            fr.GetArrayElementAtIndex(0).objectReferenceValue = fMisaki;
            fr.GetArrayElementAtIndex(1).objectReferenceValue = fYuko;
            gso.FindProperty("player").objectReferenceValue = combat;
            var wv = gso.FindProperty("waves");
            wv.arraySize = 3;
            void Wave(int i, int count, int big, float interval)
            {
                var e = wv.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("count").intValue = count;
                e.FindPropertyRelative("bigCount").intValue = big;
                e.FindPropertyRelative("interval").floatValue = interval;
            }
            Wave(0, 4, 0, 1.4f);
            Wave(1, 7, 0, 1.1f);
            Wave(2, 9, 1, 0.9f);
            gso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            CombatArenaBuilder.AddToBuildSettings(scene.path);
            Debug.Log($"[SchoolDefense] 완료. NavMesh={(surface.navMeshData != null)} 문={doorCount} non-convex로 바꾼 충돌체={unconvex} 바닥범위={school.center}/{school.size} 스폰 N={sN.position} S={sS.position}");
        }

        // ───────────────────────── Ceiling ─────────────────────────

        /// <summary>
        /// 학교 천장: 바닥(floor*) 판 모양대로 벽 꼭대기 높이에 천장판을 덮고, 격자 무늬 천장 타일 + 형광등 패널을 붙인다.
        /// 천장은 그림자를 드리우지 않게 해서(태양광이 그대로 들어옴) 지금 밝기를 유지한다.
        /// 다시 실행하면 SchoolCeiling만 지우고 새로 만든다.
        /// </summary>
        [MenuItem("LNStudy/School Defense/Build Ceiling")]
        public static void BuildCeiling()
        {
            var old = GameObject.Find("SchoolCeiling");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("SchoolCeiling").transform;

            // 벽 범위·높이
            float wallTop = 4.25f;
            bool any = false;
            Bounds walls = new Bounds();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("wall")) continue;
                if (!any) { walls = r.bounds; any = true; } else walls.Encapsulate(r.bounds);
            }
            if (any) wallTop = walls.max.y - 0.05f;

            var tileMat = CeilingMaterial();
            var lampMat = AssetDatabase.LoadAssetAtPath<Material>(Out + "/CeilingLamp.mat");
            if (lampMat == null) { lampMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(lampMat, Out + "/CeilingLamp.mat"); }
            lampMat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f));
            lampMat.EnableKeyword("_EMISSION");
            lampMat.SetColor("_EmissionColor", new Color(1f, 0.98f, 0.92f) * 1.6f);
            lampMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(lampMat);

            // 바닥 판 → 벽 범위로 자른 사각형. 크게 겹치는 판(복제된 바닥)은 하나로 합쳐 천장이 겹쳐 깜빡이지 않게
            var rects = new System.Collections.Generic.List<Rect>();
            var names = new System.Collections.Generic.List<string>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("floor") || r.transform.IsChildOf(root)) continue;
                Bounds fb = r.bounds;
                float fx0 = any ? Mathf.Max(fb.min.x, walls.min.x) : fb.min.x, fx1 = any ? Mathf.Min(fb.max.x, walls.max.x) : fb.max.x;
                float fz0 = any ? Mathf.Max(fb.min.z, walls.min.z) : fb.min.z, fz1 = any ? Mathf.Min(fb.max.z, walls.max.z) : fb.max.z;
                if (fx1 - fx0 < 0.5f || fz1 - fz0 < 0.5f) continue;
                var rc = Rect.MinMaxRect(fx0, fz0, fx1, fz1);
                bool merged = false;
                for (int i = 0; i < rects.Count && !merged; i++)
                {
                    var o = rects[i];
                    float ix = Mathf.Min(o.xMax, rc.xMax) - Mathf.Max(o.xMin, rc.xMin);
                    float iz = Mathf.Min(o.yMax, rc.yMax) - Mathf.Max(o.yMin, rc.yMin);
                    float inter = Mathf.Max(0f, ix) * Mathf.Max(0f, iz);
                    if (inter > 0.5f * Mathf.Min(o.width * o.height, rc.width * rc.height))
                    {
                        rects[i] = Rect.MinMaxRect(Mathf.Min(o.xMin, rc.xMin), Mathf.Min(o.yMin, rc.yMin),
                                                   Mathf.Max(o.xMax, rc.xMax), Mathf.Max(o.yMax, rc.yMax));
                        merged = true;
                    }
                }
                if (!merged) { rects.Add(rc); names.Add(r.name); }
            }

            int panels = 0, lamps = 0;
            for (int ri = 0; ri < rects.Count; ri++)
            {
                var rc = rects[ri];
                float x0 = rc.xMin, x1 = rc.xMax, z0 = rc.yMin, z1 = rc.yMax;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Ceiling_" + names[ri];
                panel.transform.SetParent(root, false);
                panel.transform.position = new Vector3((x0 + x1) * 0.5f, wallTop + 0.075f, (z0 + z1) * 0.5f);
                panel.transform.localScale = new Vector3(x1 - x0, 0.15f, z1 - z0);
                var pr = panel.GetComponent<Renderer>();
                pr.sharedMaterial = tileMat;
                pr.shadowCastingMode = ShadowCastingMode.Off; // 태양광을 막지 않음 (실내가 어두워지지 않게)
                // 타일 크기 일정하게 (1.2m 격자)
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_BaseMap_ST", new Vector4((x1 - x0) / 1.2f, (z1 - z0) / 1.2f, 0f, 0f));
                pr.SetPropertyBlock(mpb);
                panels++;

                // 형광등 패널: 4m 간격 격자 (벽에서 1.5m 안쪽)
                for (float x = x0 + 2f; x < x1 - 1.5f; x += 4f)
                    for (float z = z0 + 2f; z < z1 - 1.5f; z += 4f)
                    {
                        var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        lamp.name = "Lamp";
                        Object.DestroyImmediate(lamp.GetComponent<Collider>());
                        lamp.transform.SetParent(panel.transform.parent, false);
                        lamp.transform.position = new Vector3(x, wallTop - 0.03f, z);
                        lamp.transform.localScale = new Vector3(1.2f, 0.05f, 0.3f);
                        var lr = lamp.GetComponent<Renderer>();
                        lr.sharedMaterial = lampMat;
                        lr.shadowCastingMode = ShadowCastingMode.Off;
                        lamps++;
                    }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SchoolDefense] 천장 {panels}장, 형광등 {lamps}개 (높이 {wallTop:F2})");
        }

        /// <summary>격자 무늬 천장 타일 텍스처를 코드로 만들어 URP Lit 머티리얼에 넣는다</summary>
        static Material CeilingMaterial()
        {
            string texPath = Out + "/CeilingTile.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
                var rng = new System.Random(3);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool edge = x < 3 || y < 3 || x >= n - 2 || y >= n - 2;
                        float speck = (float)rng.NextDouble() * 0.04f;
                        float v = edge ? 0.72f : 0.93f - speck;
                        tex.SetPixel(x, y, new Color(v, v, v * 0.98f, 1f));
                    }
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(texPath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
            }
            string matPath = Out + "/Ceiling.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, matPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.1f);
            // 천장 아랫면은 해를 못 받아 칙칙해지므로, 형광등에 비친 느낌으로 살짝 자체 발광
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
            m.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.54f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP가 _EMISSION 키워드를 꺼버림
            EditorUtility.SetDirty(m);
            return m;
        }

        // ───────────────────────── Combat config ─────────────────────────

        static SwordCombat.Step S(string state, float dur, float hit, float dmg, float reach, float radius, float arc, float kb, float lunge, bool heavy = false)
            => new SwordCombat.Step { state = state, duration = dur, hitTime = hit, damage = dmg, reach = reach, radius = radius, arc = arc, knockback = kb, lunge = lunge, heavy = heavy };

        static void ConfigureCombat(SwordCombat combat, int enemyLayer, TrailRenderer trail, Material dust)
        {
            var modes = new[]
            {
                new SwordCombat.Mode
                {
                    displayName = "평상복",
                    combo = new[]
                    {
                        S("Attack1", 0.42f, 0.18f, 8f, 1.1f, 0.7f, 140f, 1.5f, 1.2f),
                        S("Attack2", 0.36f, 0.12f, 10f, 1.1f, 0.7f, 140f, 2f, 1.2f),
                    },
                    skill = SwordCombat.SkillKind.None,
                },
                new SwordCombat.Mode
                {
                    displayName = "대검 모드",
                    combo = new[]
                    {
                        S("Attack1", 0.62f, 0.32f, 18f, 1.6f, 1.0f, 160f, 2.5f, 1.5f),
                        S("Attack2", 0.55f, 0.28f, 18f, 1.6f, 1.0f, 160f, 2.5f, 1.5f),
                        S("Attack3", 0.75f, 0.40f, 22f, 1.7f, 1.1f, 180f, 3f, 1.8f),
                        S("Attack4", 0.85f, 0.42f, 30f, 1.8f, 1.2f, 200f, 5f, 2f, true),
                    },
                    skill = SwordCombat.SkillKind.Slam,
                    skillName = "내려찍기",
                    skillState = "Skill",
                    skillDuration = 1.15f,
                    skillHitTime = 0.62f,
                    skillDamage = 60f,
                    skillCooldown = 4f,
                },
                new SwordCombat.Mode
                {
                    displayName = "쌍검 모드",
                    combo = new[]
                    {
                        S("Attack1", 0.38f, 0.15f, 10f, 1.2f, 0.8f, 140f, 1.5f, 1.8f),
                        S("Attack2", 0.30f, 0.10f, 10f, 1.2f, 0.8f, 140f, 1.5f, 1.8f),
                        S("Attack3", 0.70f, 0.35f, 18f, 1.4f, 1.0f, 180f, 3f, 2.2f, true),
                    },
                    skill = SwordCombat.SkillKind.Pierce,
                    skillName = "관통 돌진",
                    skillState = "Skill",
                    skillDuration = 0.55f,
                    skillDamage = 35f,
                    skillCooldown = 3f,
                },
            };

            // 직렬화 필드에 직접 넣기 (SerializedObject로 중첩 배열을 일일이 쓰는 대신 리플렉션)
            var f = typeof(SwordCombat).GetField("modes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(combat, modes);
            EditorUtility.SetDirty(combat);
            Set(combat, "enemyMask", (LayerMask)(1 << enemyLayer));
            Set(combat, "wallMask", (LayerMask)1);
            Set(combat, "dashTrail", trail);
            Set(combat, "dustMaterial", dust);
        }

        /// <summary>
        /// NavMesh 굽기. 움직이는 것(문짝, 바리케이드 사물함)은 충돌체를 잠시 끄고 굽는다
        /// → 문짝 자리는 지나갈 수 있는 길, 사물함 자리는 빈 바닥. 막는 건 런타임에(문 = DoorBlocked, 사물함 = carving).
        /// </summary>
        static void BakeSchoolNavMesh(NavMeshSurface surface)
        {
            var off = new System.Collections.Generic.List<Collider>();
            void Hide(Component root)
            {
                foreach (var c in root.GetComponentsInChildren<Collider>())
                    if (c.enabled) { c.enabled = false; off.Add(c); }
            }
            foreach (var d in Object.FindObjectsByType<SwingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Hide(d);
            foreach (var b in Object.FindObjectsByType<Barricade>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Hide(b);
            foreach (var f in Object.FindObjectsByType<FireShutter>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Hide(f);
            foreach (var p in Object.FindObjectsByType<PressurePlate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Hide(p);
            // 굽는 범위 = 건물 벽 바깥면까지만. 여유를 두면 건물 바깥 테두리에 길이 생겨
            // 양이 닫힌 출입문 → 건물 밖 → 반대쪽 출입문으로 돌아 들어와 셔터·바리케이드를 우회했다.
            Bounds school = SchoolBounds();
            surface.transform.position = school.center;
            surface.center = Vector3.zero;
            surface.size = new Vector3(school.size.x, Mathf.Max(4f, school.size.y + 1f), school.size.z);
            surface.BuildNavMesh();
            foreach (var c in off) c.enabled = true;
            string navPath = Out + "/School_NavMesh.asset";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath) != null) AssetDatabase.DeleteAsset(navPath);
            if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        }

        /// <summary>
        /// 바리케이드 업데이트 (씬의 다른 수정은 유지):
        /// - 교실 문마다 가장 가까운 키 큰 사물함을 끌 수 있는 바리케이드로 바꾼다
        ///   (정적 해제, Box 충돌체, Rigidbody, Grabbable, 체력, NavMeshObstacle carving, Barricade, CrateJuice)
        /// - 문에 체력 + Breakable → 양이 닫힌 문을 부술 수 있다 (부서지면 HingeJoint 연출)
        /// - 플레이어에 PlayerGrab (E 잡기 = FixedJoint)
        /// - 양 프리팹에 장애물 부수기 켜기, NavMesh 다시 굽기
        /// </summary>
        [MenuItem("LNStudy/School Defense/Apply Barricade Update (keep scene edits)")]
        public static void ApplyBarricadeUpdate()
        {
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMatPath);

            // 이미 만든 바리케이드도 최신 설정으로 갱신
            foreach (var b in Object.FindObjectsByType<Barricade>(FindObjectsSortMode.None)) MakeBarricade(b.gameObject, dust);

            // ── 문
            var doors = Object.FindObjectsByType<SwingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var d in doors)
            {
                var h = Get<Health>(d.gameObject);
                Set(h, "maxHealth", 80f);
                Set(h, "aimHeight", 1.2f);
                var bar = Get<HealthBar>(d.gameObject);
                Set(bar, "height", 2.5f);
                Set(bar, "size", new Vector2(0.9f, 0.07f));
                Set(bar, "fillColor", new Color(0.95f, 0.7f, 0.3f));
                Set(bar, "hideAfter", 3f);
                Get<Breakable>(d.gameObject);
                EditorUtility.SetDirty(d.gameObject);
            }

            // ── 사물함 → 바리케이드 (교실 문 = 복도 양옆 벽에 있는 문, 복도 끝 출입문 제외)
            var tall = new System.Collections.Generic.List<MeshRenderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (r.name.StartsWith("locker") && r.bounds.size.y > 2f && r.GetComponent<Barricade>() == null) tall.Add(r);
            int made = 0;
            foreach (var d in doors)
            {
                Bounds db = default;
                bool first = true;
                foreach (var dr in d.GetComponentsInChildren<Renderer>())
                {
                    if (dr.GetComponentInParent<Canvas>() != null) continue;
                    if (first) { db = dr.bounds; first = false; } else db.Encapsulate(dr.bounds);
                }
                if (first) continue;
                // 문이 Z축으로 놓였으면(복도 벽 쪽 교실 문) 대상
                bool classroomDoor = db.size.z > db.size.x;
                if (!classroomDoor) continue;
                bool already = false;
                foreach (var b in Object.FindObjectsByType<Barricade>(FindObjectsSortMode.None))
                    if (Mathf.Abs(b.transform.position.x - db.center.x) < 1.5f && Mathf.Abs(b.transform.position.z - db.center.z) < 4f) { already = true; break; }
                if (already) continue;

                MeshRenderer pick = null;
                float bestD = 6f;
                foreach (var r in tall)
                {
                    if (Mathf.Abs(r.bounds.center.x - db.center.x) > 1.5f) continue; // 같은 벽 쪽 사물함만
                    float dd = Vector3.Distance(new Vector3(r.bounds.center.x, 0f, r.bounds.center.z), new Vector3(db.center.x, 0f, db.center.z));
                    if (dd < bestD) { bestD = dd; pick = r; }
                }
                if (pick == null) continue;
                tall.Remove(pick);
                MakeBarricade(pick.gameObject, dust);
                made++;
            }

            // ── 플레이어 잡기
            var player = GameObject.Find("Player_UTC");
            if (player != null)
            {
                var g = Get<PlayerGrab>(player);
                Set(g, "reach", 0.5f);
                Set(g, "grabMask", (LayerMask)1);
            }

            // ── 양: 장애물 부수기 (프리팹 다시 만들기)
            ApplyCombatAIUpdate();

            // ── NavMesh 다시 굽기
            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface != null) BakeSchoolNavMesh(surface);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SchoolDefense] 바리케이드 {made}개, 부술 수 있는 문 {doors.Length}개");
        }

        // ───────────────────────── Fire shutter ─────────────────────────

        static readonly Vector3 ShutterProbe = new Vector3(7.45f, 0f, -12.5f);
        static readonly Vector3 ShutterPlatePos = new Vector3(6.3f, 0f, -10.9f);

        /// <summary>
        /// 방화 셔터 + 무게 발판 (머핀 타임의 PressurePlate·PuzzleMover 재사용).
        /// 복도 폭을 벽까지 레이캐스트로 재서 셔터를 만들고, 천장 속(닫힌 기준 위치)에 숨겨 둔다.
        /// 발판 onPressed/onReleased → 셔터 AddSignal/RemoveSignal (영구 리스너).
        /// 다시 실행하면 FireShutters 루트만 다시 만든다.
        /// </summary>
        [MenuItem("LNStudy/School Defense/Build Fire Shutter (keep scene edits)")]
        public static void BuildFireShutter()
        {
            var old = GameObject.Find("FireShutters");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("FireShutters").transform;
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMatPath);

            // 복도 폭: 사물함 위 높이에서 양옆 벽까지
            Vector3 probe = ShutterProbe + Vector3.up * 3.2f;
            float left = probe.x - 2.6f, right = probe.x + 2.6f;
            if (Physics.Raycast(probe, Vector3.left, out var hl, 10f, 1, QueryTriggerInteraction.Ignore)) left = hl.point.x;
            if (Physics.Raycast(probe, Vector3.right, out var hr, 10f, 1, QueryTriggerInteraction.Ignore)) right = hr.point.x;
            float ceiling = 4.24f;
            if (Physics.Raycast(ShutterProbe + Vector3.up * 2f, Vector3.up, out var hc, 5f, 1, QueryTriggerInteraction.Ignore)) ceiling = hc.point.y;
            float width = right - left, height = ceiling - 0.02f;

            var shutter = new GameObject("FireShutter");
            shutter.transform.SetParent(root, false);
            // 닫힌 기준 위치 = 천장 속 (PuzzleMover는 신호가 오면 openOffset만큼 = 아래로 내려온다)
            shutter.transform.position = new Vector3((left + right) * 0.5f, 0.02f + height * 0.5f + height + 0.2f, ShutterProbe.z);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(shutter.transform, false);
            panel.transform.localScale = new Vector3(width, height, 0.12f);
            var pr = panel.GetComponent<Renderer>();
            pr.sharedMaterial = ShutterMaterial();
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_BaseMap_ST", new Vector4(1f, height / 0.3f, 0f, 0f));
            pr.SetPropertyBlock(mpb);

            var box = shutter.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, 0.12f);
            var rb = shutter.AddComponent<Rigidbody>();
            rb.mass = 80f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var mover = shutter.AddComponent<PuzzleMover>();
            Set(mover, "openOffset", new Vector3(0f, -(height + 0.2f), 0f));
            Set(mover, "openTime", 1.8f);
            Set(mover, "closeTime", 2.2f);
            Set(mover, "startSfx", "rumble");
            Set(mover, "stopSfx", "thud");

            var obs = shutter.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.size = new Vector3(width, height, 0.4f);
            obs.carving = true;
            obs.carveOnlyStationary = true;
            obs.carvingTimeToStationary = 0.2f;

            var h = shutter.AddComponent<Health>();
            Set(h, "maxHealth", 300f);
            Set(h, "aimHeight", 0f);
            var bar = shutter.AddComponent<HealthBar>();
            Set(bar, "height", 0.3f - height * 0.5f + 2.2f);
            Set(bar, "size", new Vector2(1.6f, 0.09f));
            Set(bar, "fillColor", new Color(1f, 0.55f, 0.2f));
            Set(bar, "hideAfter", 3f);
            shutter.AddComponent<Breakable>();
            shutter.AddComponent<FireShutter>();

            // ── 무게 발판
            var plateGo = new GameObject("ShutterPlate");
            plateGo.transform.SetParent(root, false);
            // 바닥 윗면 높이에 맞춘다 (바닥 메시가 두께가 있어 y=0에 두면 바닥 속에 묻혀 안 보였다)
            Vector3 platePos = ShutterPlatePos;
            if (Physics.Raycast(platePos + Vector3.up * 2f, Vector3.down, out var hf, 5f, 1, QueryTriggerInteraction.Ignore)) platePos.y = hf.point.y;
            plateGo.transform.position = platePos;
            plateGo.layer = 2; // Ignore Raycast: 카메라 충돌·레이캐스트에 안 걸리게
            var trig = plateGo.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.center = new Vector3(0f, 0.3f, 0f);
            trig.size = new Vector3(1.1f, 0.6f, 1.1f);

            var plateVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateVis.name = "Plate";
            Object.DestroyImmediate(plateVis.GetComponent<Collider>());
            plateVis.transform.SetParent(plateGo.transform, false);
            plateVis.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            plateVis.transform.localScale = new Vector3(1.05f, 0.06f, 1.05f);
            plateVis.GetComponent<Renderer>().sharedMaterial = ShutterMaterial();

            // 노랑·검정 경고 테두리 (발판이 있다는 걸 멀리서도 알아보게) — 눌려도 안 내려가는 고정 틀
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "HazardFrame";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(plateGo.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            frame.transform.localScale = new Vector3(1.35f, 0.03f, 1.35f);
            frame.GetComponent<Renderer>().sharedMaterial = HazardMaterial();

            var ind = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ind.name = "Indicator";
            Object.DestroyImmediate(ind.GetComponent<Collider>());
            ind.transform.SetParent(plateVis.transform, false);
            ind.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            ind.transform.localScale = new Vector3(0.6f, 0.25f, 0.6f);
            ind.GetComponent<Renderer>().sharedMaterial = PlateIndicatorMaterial();

            var plate = plateGo.AddComponent<PressurePlate>();
            Set(plate, "requiredWeight", 40f);
            Set(plate, "latch", false);
            Set(plate, "pressedToast", "방화 셔터가 내려온다!");
            Set(plate, "plate", plateVis.transform);
            Set(plate, "pressDepth", 0.025f);
            Set(plate, "indicator", ind.GetComponent<Renderer>());
            UnityEditor.Events.UnityEventTools.AddPersistentListener(plate.onPressed, mover.AddSignal);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(plate.onReleased, mover.RemoveSignal);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SchoolDefense] 방화 셔터 폭 {width:F2}m 높이 {height:F2}m, 발판 {ShutterPlatePos}");
        }

        static Material ShutterMaterial()
        {
            string texPath = Out + "/ShutterSlats.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                const int w = 16, hgt = 32;
                var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, true);
                for (int y = 0; y < hgt; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // 셔터 슬랫: 위쪽 밝은 면 → 아래 홈 그림자
                        float k = y / (float)hgt;
                        float v = y < 3 ? 0.35f : 0.62f + 0.18f * k;
                        tex.SetPixel(x, y, new Color(v, v * 1.01f, v * 1.03f, 1f));
                    }
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(texPath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
            }
            string matPath = Out + "/FireShutter.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, matPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", new Color(0.78f, 0.8f, 0.82f));
            m.SetFloat("_Metallic", 0.55f);
            m.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material HazardMaterial()
        {
            string texPath = Out + "/HazardStripes.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool yellow = ((x + y) / 8) % 2 == 0; // 대각선 줄무늬
                        tex.SetPixel(x, y, yellow ? new Color(1f, 0.78f, 0.1f) : new Color(0.08f, 0.08f, 0.08f));
                    }
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(texPath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
            }
            string path = Out + "/Hazard.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material PlateIndicatorMaterial()
        {
            string path = Out + "/PlateIndicator.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(1f, 0.55f, 0.2f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.2f) * 1.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void MakeBarricade(GameObject go, Material dust)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            if (!go.name.StartsWith("Barricade_")) go.name = "Barricade_" + go.name;
            var mf = go.GetComponent<MeshFilter>();
            Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up, Vector3.one);
            foreach (var mc in go.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
            var box = Get<BoxCollider>(go);
            // 바닥을 살짝 띄운다: 메시 바닥이 바닥면에 묻혀 있어 잡는 순간 튀어 오르던 것 방지
            box.center = lb.center + Vector3.up * 0.03f;
            box.size = lb.size - new Vector3(0f, 0.06f, 0f);

            var rb = Get<Rigidbody>(go);
            rb.mass = 45f;
            rb.isKinematic = true; // 플레이어가 잡았을 때만 dynamic (Barricade가 전환)
            rb.linearDamping = 2f;
            rb.angularDamping = 2f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = Get<LNStudy.Interaction.Grabbable>(go);
            Set(grab, "maxDragMass", 120f);
            Set(grab, "minSpeedMultiplier", 0.35f);

            var h = Get<Health>(go);
            Set(h, "maxHealth", 200f); // 작은 양 3마리가 15초쯤 걸려 부수는 정도
            Set(h, "aimHeight", 1.2f);
            var bar = Get<HealthBar>(go);
            Set(bar, "height", 2.9f);
            Set(bar, "size", new Vector2(0.7f, 0.07f));
            Set(bar, "fillColor", new Color(0.55f, 0.75f, 1f));
            Set(bar, "hideAfter", 3f);

            // carving: 멈춰 있을 때만 구멍. 실제 크기보다 조금 크게 → 문 앞에 대충 놓아도 막힌다
            var obs = Get<NavMeshObstacle>(go);
            obs.shape = NavMeshObstacleShape.Box;
            obs.center = lb.center;
            obs.size = new Vector3(lb.size.x + 0.3f, lb.size.y, lb.size.z + 0.3f);
            obs.carving = true;
            obs.carveOnlyStationary = true;
            obs.carvingMoveThreshold = 0.05f;
            obs.carvingTimeToStationary = 0.3f;

            Get<Breakable>(go);
            var bar2 = Get<Barricade>(go);
            Set(bar2, "dustMaterial", dust);
            var juice = Get<CrateJuice>(go);
            Set(juice, "dustMaterial", dust);
            Set(juice, "pivotAtBottom", true);
            EditorUtility.SetDirty(go);
        }

        // ───────────────────────── Monsters ─────────────────────────

        /// <summary>
        /// 몬스터 AI 업데이트만 적용: 양 프리팹(토큰·어그로·예고 장판·물리 넉백)을 다시 만들고
        /// 플레이어·친구에 EngagementTarget을 붙인다. 씬의 다른 부분(직접 수정한 것)은 건드리지 않는다.
        /// 프리팹은 같은 경로에 덮어써서 GUID가 유지되므로 SchoolDefenseGame의 참조도 그대로다.
        /// </summary>
        [MenuItem("LNStudy/School Defense/Apply Combat AI Update (keep scene edits)")]
        public static void ApplyCombatAIUpdate()
        {
            int enemyLayer = CombatArenaBuilder.EnsureLayer("Enemy");
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMatPath);
            var lambCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Out + "/LambMonster.controller");
            if (lambCtrl == null) lambCtrl = BuildLambController();
            int agentType = EnsureAgentType();
            BuildLambPrefab("Lamb", 0.5f, 45f, 7f, enemyLayer, lambCtrl, dust, agentType);
            BuildLambPrefab("BigLamb", 0.8f, 220f, 14f, enemyLayer, lambCtrl, dust, agentType);

            var player = GameObject.Find("Player_UTC");
            if (player != null) SetupEngagement(player, 2, 2.3f);
            foreach (var f in Object.FindObjectsByType<FriendNPC>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SetupEngagement(f.gameObject, 1, 2.0f);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SchoolDefense] 전투 AI 업데이트 적용 (공격 토큰·포위·어그로·예고 장판·물리 넉백)");
        }

        /// <summary>공격받는 쪽: 동시에 덤빌 수 있는 토큰 수와 대기 링</summary>
        static void SetupEngagement(GameObject go, int tokens, float ring)
        {
            var et = Get<EngagementTarget>(go);
            Set(et, "attackTokens", tokens);
            Set(et, "ringRadius", ring);
            Set(et, "slotCount", 8);
        }

        static Material TelegraphMaterial()
        {
            string path = Out + "/Telegraph.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(1f, 0.25f, 0.2f, 0.4f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject BuildLambPrefab(string name, float scale, float hp, float damage, int enemyLayer, RuntimeAnimatorController ctrl, Material dust, int agentType)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SheepPrefab);
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * scale;
            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;

            float h = 2.39f * scale;
            float r = 0.65f * scale;
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = r;
            col.height = h;
            col.center = new Vector3(0f, h * 0.5f, 0f);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID = agentType;
            agent.radius = r;
            agent.height = h;
            agent.angularSpeed = 540f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 0.3f;
            agent.avoidancePriority = Random.Range(30, 70);

            var health = root.AddComponent<Health>();
            Set(health, "maxHealth", hp);
            Set(health, "aimHeight", h * 0.5f);
            var bar = root.AddComponent<HealthBar>();
            Set(bar, "height", h + 0.25f);
            Set(bar, "size", new Vector2(0.4f + scale * 0.5f, 0.06f));

            var brain = root.AddComponent<MonsterBrain>();
            Set(brain, "animator", anim);
            Set(brain, "obstacleMask", (LayerMask)1);
            Set(brain, "eyeHeight", h * 0.7f);
            Set(brain, "viewDistance", 80f);
            Set(brain, "viewAngle", 360f);
            Set(brain, "hearDistance", 12f);
            Set(brain, "loseTargetTime", 999f);
            Set(brain, "patrolSpeed", 1.2f);
            Set(brain, "chaseSpeed", scale > 0.6f ? 1.7f : 2.3f);
            Set(brain, "patrolRadius", 1f);
            Set(brain, "leashDistance", 999f);
            Set(brain, "attackRange", r + 0.6f);
            Set(brain, "attackHitRange", r + 0.95f);
            Set(brain, "attackHitAngle", 120f);
            Set(brain, "attackDamage", damage);
            Set(brain, "attackWindup", 0.55f);
            Set(brain, "attackRecovery", 0.6f);
            Set(brain, "attackCooldown", 1.6f);
            Set(brain, "attackKnockback", scale > 0.6f ? 5f : 2.5f);
            Set(brain, "staggerTime", scale > 0.6f ? 0.15f : 0.3f);
            Set(brain, "knockbackScale", scale > 0.6f ? 0.15f : 0.4f);
            Set(brain, "sinkDelay", 1.3f);
            Set(brain, "respawnAfterDeath", false);

            // 공격 토큰·포위·어그로
            bool big = scale > 0.6f;
            Set(brain, "useAttackTokens", true);
            Set(brain, "tokenCost", big ? 2 : 1);
            Set(brain, "engageMargin", 1.5f);
            Set(brain, "circleSpeed", big ? 1.0f : 1.3f);
            Set(brain, "useAggro", true);
            Set(brain, "friendPriority", 1.4f);
            Set(brain, "damageThreat", 0.4f);
            Set(brain, "threatDecay", 1.2f);
            Set(brain, "getUpTime", big ? 0.25f : 0.4f);
            Set(brain, "wallStun", big ? 0.6f : 1.0f);
            Set(brain, "breakObstacles", true);
            Set(brain, "trackWithoutSight", true);

            // 공격 예고 장판
            var tele = root.AddComponent<TelegraphIndicator>();
            Set(tele, "material", TelegraphMaterial());
            Set(brain, "telegraph", tele);

            // 물리 넉백: 평소엔 kinematic (에이전트가 이동), 강한 공격에 dynamic으로 전환
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = big ? 3f : 1f;
            rb.isKinematic = true;
            rb.linearDamping = 0.4f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            var kb = root.AddComponent<PhysicsKnockback>();
            Set(kb, "launchThreshold", big ? 6.5f : 3f);
            Set(kb, "weight", big ? 2.5f : 1f);
            Set(kb, "wallSlamDamage", big ? 15f : 6f);

            root.AddComponent<DoorBlocked>(); // 양은 문을 못 연다 → 닫힌 문에 막힘
            var juice = root.AddComponent<LambJuice>();
            Set(juice, "visual", model.transform);
            Set(juice, "dustMaterial", dust);

            CombatArenaBuilder.SetLayerRecursive(root, enemyLayer);
            string path = $"{Out}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // ───────────────────────── Animator controllers ─────────────────────────

        static AnimationClip Clip(string file, string clipName)
        {
            string path = $"{Haons}/{file}";
            var c = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => x.name == clipName);
            if (c == null) Debug.LogWarning($"[SchoolDefense] 클립 없음: {path} :: {clipName}");
            return c;
        }

        static AnimatorController Fresh(string path)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null) return AnimatorController.CreateAnimatorControllerAtPath(path);
            for (int i = c.layers.Length - 1; i >= 0; i--) c.RemoveLayer(i);
            for (int i = c.parameters.Length - 1; i >= 0; i--) c.RemoveParameter(i);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub != null && sub != c) Object.DestroyImmediate(sub, true);
            c.AddLayer("Base Layer");
            return c;
        }

        static AnimatorStateTransition T(AnimatorState a, AnimatorState b, float dur)
        {
            var t = a.AddTransition(b);
            t.hasExitTime = false;
            t.duration = dur;
            return t;
        }

        static void ExitTo(AnimatorState from, AnimatorState to, float exit, float dur)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exit;
            t.duration = dur;
        }

        /// <summary>PlayerAnimator 파라미터 + 전투 상태(Attack1..n, Skill, Victory). SwordCombat이 상태 이름으로 직접 재생</summary>
        static RuntimeAnimatorController BuildUtcController(string path, AnimationClip idle, AnimationClip walk, AnimationClip run,
            AnimationClip[] attacks, AnimationClip skill, float skillSpeed)
        {
            var c = Fresh(path);
            foreach (var f in new[] { "Speed", "VerticalSpeed" }) c.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (var b in new[] { "Grounded", "Crouch", "Grab" }) c.AddParameter(b, AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "Jump", "Climb", "Hit", "Attack", "Skill", "IdleFidget" }) c.AddParameter(t, AnimatorControllerParameterType.Trigger);
            c.AddParameter("IdleVariant", AnimatorControllerParameterType.Int);
            var sm = c.layers[0].stateMachine;

            var tree = new BlendTree { name = "Loco", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.8f);
            tree.AddChild(run, 4.2f);
            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = tree;
            sm.defaultState = loco;

            var jump = AssetDatabase.LoadAllAssetsAtPath($"{Haons}/Action Adventure(Adv)/Jump_One-cycle-Type(root).FBX")
                .OfType<AnimationClip>().FirstOrDefault(x => x.name == "Jump_Small root");
            var air = sm.AddState("Air", new Vector3(560, 0, 0));
            air.motion = jump;
            T(loco, air, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(loco, air, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(air, loco, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            for (int i = 0; i < attacks.Length; i++)
            {
                var s = sm.AddState("Attack" + (i + 1), new Vector3(300 + i * 200, 160, 0));
                s.motion = attacks[i];
                ExitTo(s, loco, 0.92f, 0.12f);
            }
            if (skill != null)
            {
                var s = sm.AddState("Skill", new Vector3(560, -140, 0));
                s.motion = skill;
                s.speed = skillSpeed;
                ExitTo(s, loco, 0.95f, 0.12f);
            }

            var spin = AssetDatabase.LoadAllAssetsAtPath($"{Haons}/Action Adventure(Adv)/VictoryPose_UTCStyleSpin_3spin.fbx")
                .OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
            var hold = AssetDatabase.LoadAllAssetsAtPath($"{Haons}/Action Adventure(Adv)/VictoryPose_UTCStyleSpin.fbx")
                .OfType<AnimationClip>().FirstOrDefault(x => x.name == "VictoryPose_UTCStyleSpin_Hold@loop");
            var victory = sm.AddState("Victory", new Vector3(40, -140, 0));
            victory.motion = spin;
            if (hold != null)
            {
                var vh = sm.AddState("VictoryHold", new Vector3(40, -220, 0));
                vh.motion = hold;
                ExitTo(victory, vh, 0.98f, 0.1f);
            }

            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>친구: 서 있기·걷기·뛰기 (FriendNPC가 NavMeshAgent 속도를 Speed로 넣음)</summary>
        static RuntimeAnimatorController BuildFriendController()
        {
            var c = Fresh(Out + "/Friend.controller");
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = c.layers[0].stateMachine;
            var tree = new BlendTree { name = "FriendLoco", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(Clip("Common/StandA_idleA.FBX", "StandA_idleA"), 0f);
            tree.AddChild(Clip("Common/WalkA_Front.FBX", "Walk_front@loop"), 1.3f);
            tree.AddChild(Clip("Common/RunA_Front.FBX", "RunA_front@loop"), 3.2f);
            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = tree;
            sm.defaultState = loco;
            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>MonsterBrain 파라미터(Speed/Hit/Attack/Dead). 공격 = 폴짝 뛰어 박기, 쓰러짐 = 빙글빙글</summary>
        static RuntimeAnimatorController BuildLambController()
        {
            var c = Fresh(Out + "/LambMonster.controller");
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            var sm = c.layers[0].stateMachine;

            AnimationClip Sheep(string f) => AssetDatabase.LoadAllAssetsAtPath($"{SheepAnims}/{f}.fbx").OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
            var tree = new BlendTree { name = "LambLoco", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(Sheep("Idle"), 0f);
            tree.AddChild(Sheep("Walk"), 0.9f);
            tree.AddChild(Sheep("Run"), 2.2f);
            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = tree;
            sm.defaultState = loco;

            var hop = AssetDatabase.LoadAllAssetsAtPath($"{Haons}/Action Adventure(Adv)/Jump_One-cycle-Type(root).FBX")
                .OfType<AnimationClip>().FirstOrDefault(x => x.name == "Jump_Small root");
            var atk = sm.AddState("Attack", new Vector3(560, -80, 0));
            atk.motion = hop;
            atk.speed = 1.1f;
            var toAtk = sm.AddAnyStateTransition(atk);
            toAtk.hasExitTime = false;
            toAtk.duration = 0.05f;
            toAtk.canTransitionToSelf = false;
            toAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            toAtk.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            ExitTo(atk, loco, 0.95f, 0.1f);

            var spin = AssetDatabase.LoadAllAssetsAtPath($"{Haons}/Action Adventure(Adv)/VictoryPose_UTCStyleSpin_3spin.fbx")
                .OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
            var die = sm.AddState("Die", new Vector3(560, 120, 0));
            die.motion = spin;
            die.speed = 2f;
            var toDie = sm.AddAnyStateTransition(die);
            toDie.hasExitTime = false;
            toDie.duration = 0.1f;
            toDie.canTransitionToSelf = false;
            toDie.AddCondition(AnimatorConditionMode.If, 0, "Dead");

            AssetDatabase.SaveAssets();
            return c;
        }

        static Material TearMaterial()
        {
            string path = Out + "/Tear.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(0.62f, 0.86f, 1f, 0.85f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>학교 건물 범위: 벽(wall*) 렌더러들을 감싸는 박스 (없으면 문·바닥 기준)</summary>
        static Bounds SchoolBounds()
        {
            bool any = false;
            Bounds b = new Bounds(new Vector3(7f, 2f, -10.7f), new Vector3(61f, 4.5f, 65f));
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("wall") && !r.name.StartsWith("floor")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>작은 에이전트 타입을 ProjectSettings/NavMeshAreas에 추가 (이미 있으면 갱신)</summary>
        static int EnsureAgentType()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0];
            var so = new SerializedObject(asset);
            var settings = so.FindProperty("m_Settings");
            var names = so.FindProperty("m_SettingNames");
            int index = -1;
            for (int i = 0; i < names.arraySize; i++)
                if (names.GetArrayElementAtIndex(i).stringValue == AgentTypeName) index = i;
            if (index < 0)
            {
                index = settings.arraySize;
                settings.arraySize++;   // 마지막 항목(Humanoid 등)이 복사되어 추가됨 → 아래에서 덮어씀
                names.arraySize++;
                names.GetArrayElementAtIndex(index).stringValue = AgentTypeName;
            }
            var e = settings.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("agentTypeID").intValue = AgentTypeId;
            e.FindPropertyRelative("agentRadius").floatValue = 0.25f;
            e.FindPropertyRelative("agentHeight").floatValue = 1.2f;
            e.FindPropertyRelative("agentSlope").floatValue = 45f;
            e.FindPropertyRelative("agentClimb").floatValue = 0.3f;
            e.FindPropertyRelative("minRegionArea").floatValue = 0.5f;
            e.FindPropertyRelative("manualCellSize").intValue = 1;
            e.FindPropertyRelative("cellSize").floatValue = 0.08f;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return AgentTypeId;
        }

        // ───────────────────────── Utils ─────────────────────────

        static T Get<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static void Set(Object o, string field, object value) => CombatArenaBuilder.SetField(o, field, value);
    }
}
