using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// Sci-Fi Robots Bundle 로봇을 휴머노이드로 바꿔 Unity-chan 애니메이션을 리타깃해 쓰게 한다.
    /// - 로봇마다 뼈 이름이 달라 자동 매핑 대신 직접 매핑표를 쓴다.
    /// - 팔을 내린 자세(A/I 포즈)로 만들어진 모델은 휴머노이드 기준(T 포즈)이 어긋나므로
    ///   임시 인스턴스에서 팔을 수평으로 편 뒤 그 자세를 스켈레톤으로 저장한다 (Enforce T-Pose와 같은 처리).
    /// - 몬스터용 애니메이터(MonsterAnimator.controller)도 함께 만든다.
    /// </summary>
    public static class MonsterRigSetup
    {
        public const string RobotFolder = "Assets/Same Gev Dudios/Sci-Fi Robots Bundle";
        public const string ControllerPath = "Assets/_Project/Animation/MonsterAnimator.controller";
        const string UpperMaskPath = "Assets/_Project/Animation/UpperBody.mask";
        const string UnityChanAnimFolder = "Assets/unity-chan!/Unity-chan! Model/Art/Animations";

        /// <summary>휴머노이드 뼈 이름 → 모델 뼈 이름 ({S}는 L/R)</summary>
        static readonly Dictionary<string, Dictionary<string, string>> Maps = new()
        {
            ["Robert"] = new()
            {
                ["Hips"] = "Pelvis", ["Spine"] = "Stomach", ["Chest"] = "Chest", ["Neck"] = "Neck", ["Head"] = "Head",
                ["{Side}UpperLeg"] = "Leg0.{S}", ["{Side}LowerLeg"] = "Leg1.{S}", ["{Side}Foot"] = "Foot0.{S}", ["{Side}Toes"] = "Foot1.{S}",
                ["{Side}Shoulder"] = "Shoulder1.{S}", ["{Side}UpperArm"] = "Arm0.{S}", ["{Side}LowerArm"] = "Arm1.{S}", ["{Side}Hand"] = "Hand0.{S}",
            },
            ["Engie"] = new()
            {
                ["Hips"] = "Pelvis", ["Spine"] = "Stomach", ["Chest"] = "Chest", ["Neck"] = "Neck", ["Head"] = "Head",
                ["{Side}UpperLeg"] = "Leg0.{S}", ["{Side}LowerLeg"] = "Leg1.{S}", ["{Side}Foot"] = "Foot.{S}",
                ["{Side}Shoulder"] = "ShoulderBlade.{S}", ["{Side}UpperArm"] = "Arm0.{S}", ["{Side}LowerArm"] = "Arm1.{S}", ["{Side}Hand"] = "Hand.{S}",
            },
            ["Paperman"] = new()
            {
                ["Hips"] = "Pelvis", ["Spine"] = "Stomach", ["Chest"] = "Chest", ["Head"] = "Head",
                ["{Side}UpperLeg"] = "Leg0.{S}", ["{Side}LowerLeg"] = "Leg1.{S}", ["{Side}Foot"] = "Foot0.{S}", ["{Side}Toes"] = "Foot1.{S}",
                ["{Side}UpperArm"] = "Arm0.{S}", ["{Side}LowerArm"] = "Arm1.{S}", ["{Side}Hand"] = "Hand.{S}",
            },
            // Catherine은 발·손 뼈가 없어 휴머노이드가 될 수 없다
        };

        public static IEnumerable<string> SupportedRobots => Maps.Keys;
        public static string ModelPath(string robot) => $"{RobotFolder}/Models/{robot}.fbx";
        public static string PrefabPath(string robot) => $"{RobotFolder}/Prefabs/{robot}.prefab";

        [MenuItem("LNStudy/Monsters/Setup Robot Humanoid Rigs + Animator")]
        public static void SetupAllMenu()
        {
            var ok = SetupAll();
            Debug.Log($"[MonsterRigSetup] 휴머노이드 성공: {string.Join(", ", ok)}");
        }

        /// <summary>모든 로봇을 휴머노이드로 설정하고, 성공한 로봇 이름 목록을 반환한다.</summary>
        public static List<string> SetupAll()
        {
            var ok = new List<string>();
            foreach (var robot in Maps.Keys)
                if (SetupHumanoid(robot)) ok.Add(robot);
            BuildController();
            return ok;
        }

        public static Avatar LoadAvatar(string robot) =>
            AssetDatabase.LoadAllAssetsAtPath(ModelPath(robot)).OfType<Avatar>().FirstOrDefault();

        static bool SetupHumanoid(string robot)
        {
            string path = ModelPath(robot);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (importer == null || model == null) { Debug.LogWarning($"[MonsterRigSetup] 모델 없음: {path}"); return false; }

            var existing = LoadAvatar(robot);
            if (importer.animationType == ModelImporterAnimationType.Human && existing != null && existing.isValid && existing.isHuman)
                return true; // 이미 설정됨

            // 매핑표 펼치기 (L/R)
            var human = new List<HumanBone>();
            var boneOf = new Dictionary<string, string>();
            foreach (var kv in Maps[robot])
            {
                if (kv.Key.Contains("{Side}"))
                {
                    foreach (var (side, s) in new[] { ("Left", "L"), ("Right", "R") })
                        boneOf[kv.Key.Replace("{Side}", side)] = kv.Value.Replace("{S}", s);
                }
                else boneOf[kv.Key] = kv.Value;
            }

            // 임시 인스턴스에서 T 포즈를 만든 뒤 스켈레톤으로 기록
            var inst = Object.Instantiate(model);
            inst.name = model.name;
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => t.name == n);

                foreach (var kv in boneOf)
                {
                    if (Find(kv.Value) == null)
                    {
                        Debug.LogWarning($"[MonsterRigSetup] {robot}: 뼈 '{kv.Value}' 없음 ({kv.Key})");
                        return false;
                    }
                    human.Add(new HumanBone { humanName = kv.Key, boneName = kv.Value, limit = new HumanLimit { useDefaultValues = true } });
                }

                EnforceArmTPose(inst.transform, Find, boneOf, "Left", -1f);
                EnforceArmTPose(inst.transform, Find, boneOf, "Right", 1f);

                var skeleton = all.Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray();

                var desc = importer.humanDescription;
                desc.human = human.ToArray();
                desc.skeleton = skeleton;
                desc.upperArmTwist = 0.5f;
                desc.lowerArmTwist = 0.5f;
                desc.upperLegTwist = 0.5f;
                desc.lowerLegTwist = 0.5f;
                desc.armStretch = 0.05f;
                desc.legStretch = 0.05f;
                desc.feetSpacing = 0f;
                desc.hasTranslationDoF = false;

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.humanDescription = desc;
                importer.SaveAndReimport();
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }

            var avatar = LoadAvatar(robot);
            bool valid = avatar != null && avatar.isValid && avatar.isHuman;
            if (!valid) Debug.LogWarning($"[MonsterRigSetup] {robot}: 휴머노이드 아바타 생성 실패. 콘솔의 아바타 오류를 확인하세요.");
            return valid;
        }

        /// <summary>윗팔·아랫팔을 몸 바깥쪽 수평 방향으로 편다 (모델이 +Z를 보고 있다고 가정).</summary>
        static void EnforceArmTPose(Transform root, System.Func<string, Transform> find,
            Dictionary<string, string> boneOf, string side, float outward)
        {
            var upper = find(boneOf[side + "UpperArm"]);
            var lower = find(boneOf[side + "LowerArm"]);
            var hand = find(boneOf[side + "Hand"]);
            Vector3 dir = root.right * outward;

            Align(upper, lower.position, dir);
            Align(lower, hand.position, dir);
        }

        static void Align(Transform bone, Vector3 childPos, Vector3 dir)
        {
            Vector3 cur = childPos - bone.position;
            if (cur.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(cur.normalized, dir.normalized) * bone.rotation;
        }

        // ───────────────────────── Animator ─────────────────────────

        static AnimationClip Clip(string fbx)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"{UnityChanAnimFolder}/{fbx}")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        public static AnimatorController BuildController()
        {
            var idle = Clip("unitychan_WAIT00.fbx");
            var walk = Clip("unitychan_WALK00_F.fbx");
            var run = Clip("unitychan_RUN00_F.fbx");
            var hit = Clip("unitychan_DAMAGED00.fbx");
            var down = Clip("unitychan_DAMAGED01.fbx");
            var swing = Clip("unitychan_HANDUP00_R.fbx");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                // 제자리 재생성 (GUID 유지 → 씬 참조가 끊기지 않음)
                foreach (var p in controller.parameters) controller.RemoveParameter(p);
                while (controller.layers.Length > 0) controller.RemoveLayer(0);
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (sub != controller && !(sub is AnimatorController)) Object.DestroyImmediate(sub, true);
                controller.AddLayer("Base Layer");
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            // Base: 이동 블렌드 (로봇은 키가 커서 Unity-chan보다 보폭 기준 속도를 높게)
            var sm = controller.layers[0].stateMachine;
            var tree = new BlendTree { name = "MonsterLocomotion", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.4f);
            tree.AddChild(run, 3.4f);
            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = tree;
            sm.defaultState = loco;

            // 사망: 쓰러지는 클립 후 마지막 자세 유지, 부활하면 이동으로 복귀
            var die = sm.AddState("Die", new Vector3(560, 0, 0));
            die.motion = down;
            var toDie = sm.AddAnyStateTransition(die);
            toDie.duration = 0.1f;
            toDie.canTransitionToSelf = false;
            toDie.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            var revive = die.AddTransition(loco);
            revive.duration = 0.1f;
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            // UpperBody: 피격 반응
            controller.AddLayer("UpperBody");
            var layers = controller.layers;
            var upper = layers[1];
            upper.avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMaskPath);
            upper.defaultWeight = 1f;
            layers[1] = upper;
            controller.layers = layers;

            var usm = controller.layers[1].stateMachine;
            var empty = usm.AddState("Empty", new Vector3(300, 0, 0));
            usm.defaultState = empty;
            var hitState = usm.AddState("Hit", new Vector3(300, 100, 0));
            hitState.motion = hit;
            var toHit = usm.AddAnyStateTransition(hitState);
            toHit.duration = 0.05f;
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            var back = hitState.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.8f;
            back.duration = 0.2f;

            // 공격 예비 동작: 팔을 치켜드는 클립을 내려치기 대용으로 사용 (전용 클립이 생기면 교체)
            var atk = usm.AddState("Attack", new Vector3(560, 100, 0));
            atk.motion = swing;
            atk.speed = 1.6f;
            var toAtk = usm.AddAnyStateTransition(atk);
            toAtk.duration = 0.08f;
            toAtk.canTransitionToSelf = false;
            toAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            var atkBack = atk.AddTransition(empty);
            atkBack.hasExitTime = true;
            atkBack.exitTime = 0.75f;
            atkBack.duration = 0.2f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[MonsterRigSetup] 몬스터 애니메이터 생성: " + ControllerPath);
            return controller;
        }
    }
}
