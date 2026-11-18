using System.IO;
using System.Linq;
using System;
using LNStudy.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// 플레이어 Animator Controller를 코드로 생성한다. (다시 실행하면 같은 구조로 다시 만든다)
    ///
    /// Base Layer  : Locomotion(1D 블렌드: 대기·걷기·달리기) / Crouch / Air / Climb
    /// UpperBody   : 상체 마스크 레이어. 피격 반응(Hit)처럼 이동 중에도 상체만 재생할 동작을 둔다.
    ///               (이후 둔기 휘두르기도 이 레이어에 추가)
    /// Face        : 표정 레이어. FaceExpression 이름과 같은 상태를 PlayerFace가 CrossFade로 전환한다.
    ///               블렌드셰이프만 움직이는 클립이라 얼굴 전용 마스크를 쓴다.
    /// 대기 변형    : Locomotion에서 오래 서 있으면 IdleVariant1~4 중 하나를 한 번 재생하고 돌아온다.
    ///
    /// 클립은 Unity-chan 패키지의 FBX에서 가져온다. 웅크리기 전용 클립이 없어 임시로 걷기·대기를
    /// 느리게 재생하며, 나중에 웅크리기 클립으로 바꾸면 된다.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        const string AnimFolder = "Assets/unity-chan!/Unity-chan! Model/Art/Animations";
        const string OutFolder = "Assets/_Project/Animation";
        const string ControllerPath = OutFolder + "/PlayerAnimator.controller";
        const string UpperMaskPath = OutFolder + "/UpperBody.mask";
        const string FaceFolder = AnimFolder + "/FaceAnimation";
        const string FaceMaskPath = FaceFolder + "/face only mask.mask";
        public const string FaceLayerName = "Face";
        public const int IdleVariantCount = 4;

        // 파라미터 이름 (런타임 PlayerAnimator와 공유)
        public const string Speed = "Speed";
        public const string Grounded = "Grounded";
        public const string VerticalSpeed = "VerticalSpeed";
        public const string Crouch = "Crouch";
        public const string Grab = "Grab";
        public const string Jump = "Jump";
        public const string Climb = "Climb";
        public const string Hit = "Hit";
        public const string Attack = "Attack";
        public const string Skill = "Skill";
        public const string IdleVariant = "IdleVariant";
        public const string IdleFidget = "IdleFidget";

        // 조정값
        const float RunTimeScale = 1.35f;      // 달리기 클립 재생 배율 (발이 미끄러져 보이지 않게)
        const float WalkTimeScale = 1.0f;
        const float JumpStartOffset = 0.25f;   // JUMP00 앞쪽 준비 동작(웅크림)을 건너뛰는 시작 지점 (정규화 시간)
        const float FallStartOffset = 0.42f;   // 점프 없이 떨어질 때는 공중 자세부터 시작
        const float ClimbStartOffset = 0.2f;   // JUMP01 앞쪽 도움닫기를 건너뛴다

        [MenuItem("LNStudy/Animation/Build Player Animator")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(OutFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Animation");

            var idle = LoadClip("unitychan_WAIT00.fbx");
            var walk = LoadClip("unitychan_WALK00_F.fbx");
            var run = LoadClip("unitychan_RUN00_F.fbx");
            var jump = LoadClip("unitychan_JUMP00.fbx");
            var climb = LoadClip("unitychan_JUMP01.fbx");
            var hit = LoadClip("unitychan_DAMAGED00.fbx");
            var point = LoadClip("unitychan_HANDUP00_R.fbx");
            if (idle == null || walk == null || run == null || jump == null || climb == null || hit == null)
            {
                Debug.LogError("[PlayerAnimatorBuilder] Unity-chan 애니메이션 클립을 찾지 못했습니다: " + AnimFolder);
                return;
            }

            // 이미 있으면 지우지 않고 내용만 비운다. (지우면 GUID가 바뀌어 씬의 Animator 연결이 끊어진다)
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
                ClearController(controller);

            controller.AddParameter(Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(Grounded, AnimatorControllerParameterType.Bool);
            controller.AddParameter(VerticalSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(Crouch, AnimatorControllerParameterType.Bool);
            controller.AddParameter(Grab, AnimatorControllerParameterType.Bool);
            controller.AddParameter(Jump, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Climb, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Skill, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(IdleVariant, AnimatorControllerParameterType.Int);
            controller.AddParameter(IdleFidget, AnimatorControllerParameterType.Trigger);

            var idleVariants = new AnimationClip[IdleVariantCount];
            for (int i = 0; i < IdleVariantCount; i++)
                idleVariants[i] = LoadClip($"unitychan_WAIT0{i + 1}.fbx");

            BuildBaseLayer(controller, idle, walk, run, jump, climb, idleVariants);
            BuildUpperBodyLayer(controller, hit, point);
            BuildFaceLayer(controller);

            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerAnimatorBuilder] 생성 완료: " + ControllerPath, controller);
        }

        static void BuildBaseLayer(AnimatorController controller, AnimationClip idle, AnimationClip walk,
            AnimationClip run, AnimationClip jump, AnimationClip climb, AnimationClip[] idleVariants)
        {
            var layer = controller.layers[0];
            layer.iKPass = true; // 잡기 중 손 IK (PlayerAnimator.OnAnimatorIK)
            var layers = controller.layers;
            layers[0] = layer;
            controller.layers = layers;

            var sm = controller.layers[0].stateMachine;

            // Locomotion: 속도(m/s)로 대기·걷기·달리기를 섞는다. 임계값은 PlayerMotor 속도와 맞춘다
            var locoTree = new BlendTree
            {
                name = "LocomotionTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = Speed,
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(locoTree, controller);
            locoTree.AddChild(idle, 0f);
            locoTree.AddChild(walk, 2.2f);
            locoTree.AddChild(run, 4.0f);
            var children = locoTree.children;
            children[1].timeScale = WalkTimeScale;
            children[2].timeScale = RunTimeScale;
            locoTree.children = children;

            var loco = sm.AddState("Locomotion", new Vector3(300, 0, 0));
            loco.motion = locoTree;
            sm.defaultState = loco;

            // Crouch: 전용 클립이 없어 임시로 대기·걷기를 느리게 재생
            var crouchTree = new BlendTree
            {
                name = "CrouchTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = Speed,
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(crouchTree, controller);
            crouchTree.AddChild(idle, 0f);
            crouchTree.AddChild(walk, 1.1f);
            var crouch = sm.AddState("Crouch", new Vector3(300, 120, 0));
            crouch.motion = crouchTree;
            crouch.speed = 0.7f;

            var air = sm.AddState("Air", new Vector3(560, 0, 0));
            air.motion = jump;

            var climbState = sm.AddState("Climb", new Vector3(560, -120, 0));
            climbState.motion = climb;
            climbState.speed = 1.6f;

            // Locomotion <-> Crouch
            Transition(loco, crouch, 0.15f).AddCondition(AnimatorConditionMode.If, 0, Crouch);
            Transition(crouch, loco, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, Crouch);

            // 점프는 지면 판정보다 먼저 반응하도록 트리거로 진입 (먼저 추가 = 우선 검사)
            // offset으로 클립 앞쪽 준비 동작을 건너뛴다 (클립 원본은 수정하지 않음)
            var jumpT = Transition(loco, air, 0.05f);
            jumpT.offset = JumpStartOffset;
            jumpT.AddCondition(AnimatorConditionMode.If, 0, Jump);

            // 점프 없이 땅에서 떨어지면 공중 자세부터, 착지하면 Locomotion
            var fallT = Transition(loco, air, 0.1f);
            fallT.offset = FallStartOffset;
            fallT.AddCondition(AnimatorConditionMode.IfNot, 0, Grounded);
            var crouchFallT = Transition(crouch, air, 0.1f);
            crouchFallT.offset = FallStartOffset;
            crouchFallT.AddCondition(AnimatorConditionMode.IfNot, 0, Grounded);
            Transition(air, loco, 0.1f).AddCondition(AnimatorConditionMode.If, 0, Grounded);

            // 턱 오르기: 어느 상태에서든 진입, 끝나면 Locomotion
            var anyClimb = sm.AddAnyStateTransition(climbState);
            anyClimb.hasExitTime = false;
            anyClimb.duration = 0.05f;
            anyClimb.canTransitionToSelf = false;
            anyClimb.offset = ClimbStartOffset;
            anyClimb.AddCondition(AnimatorConditionMode.If, 0, Climb);

            var climbOut = climbState.AddTransition(loco);
            climbOut.hasExitTime = true;
            climbOut.exitTime = 0.85f;
            climbOut.duration = 0.1f;

            // 대기 변형: 멈춰 있을 때만 진입, 한 번 재생하면 돌아오고, 움직이면 즉시 끊는다
            for (int i = 0; i < idleVariants.Length; i++)
            {
                if (idleVariants[i] == null) continue;
                int variant = i + 1;
                var state = sm.AddState($"IdleVariant{variant}", new Vector3(40, -120 + i * 70, 0));
                state.motion = idleVariants[i];

                var enter = Transition(loco, state, 0.3f);
                enter.AddCondition(AnimatorConditionMode.If, 0, IdleFidget);
                enter.AddCondition(AnimatorConditionMode.Equals, variant, IdleVariant);
                enter.AddCondition(AnimatorConditionMode.Less, 0.1f, Speed);

                var done = state.AddTransition(loco);
                done.hasExitTime = true;
                done.exitTime = 0.95f;
                done.duration = 0.3f;

                Transition(state, loco, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.1f, Speed);
                Transition(state, crouch, 0.15f).AddCondition(AnimatorConditionMode.If, 0, Crouch);
                var fall = Transition(state, air, 0.1f);
                fall.offset = FallStartOffset;
                fall.AddCondition(AnimatorConditionMode.IfNot, 0, Grounded);
            }
        }

        static void BuildFaceLayer(AnimatorController controller)
        {
            var faceMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(FaceMaskPath);
            if (faceMask == null)
                Debug.LogWarning("[PlayerAnimatorBuilder] 얼굴 마스크를 찾지 못했습니다: " + FaceMaskPath);

            controller.AddLayer(FaceLayerName);
            var layers = controller.layers;
            var face = layers[layers.Length - 1];
            face.avatarMask = faceMask;
            face.defaultWeight = 1f;
            face.blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layers.Length - 1] = face;
            controller.layers = layers;

            var sm = controller.layers[layers.Length - 1].stateMachine;
            int row = 0;
            foreach (FaceExpression expr in Enum.GetValues(typeof(FaceExpression)))
            {
                string clipFile = PlayerFace.GetClipFileName(expr);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FaceFolder + "/" + clipFile);
                if (clip == null)
                {
                    Debug.LogWarning($"[PlayerAnimatorBuilder] 표정 클립 없음: {clipFile}");
                    continue;
                }
                // 상태 이름 = FaceExpression 이름. 런타임에서 이름 해시로 전환한다
                var state = sm.AddState(expr.ToString(), new Vector3(300 + (row % 3) * 220, (row / 3) * 70, 0));
                state.motion = clip;
                if (expr == FaceExpression.Default) sm.defaultState = state;
                row++;
            }
        }

        static void BuildUpperBodyLayer(AnimatorController controller, AnimationClip hit, AnimationClip point)
        {
            var mask = CreateUpperBodyMask();

            controller.AddLayer("UpperBody");
            var layers = controller.layers;
            var upper = layers[layers.Length - 1];
            upper.avatarMask = mask;
            upper.defaultWeight = 1f;
            upper.blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layers.Length - 1] = upper;
            controller.layers = layers;

            var sm = controller.layers[layers.Length - 1].stateMachine;
            var empty = sm.AddState("Empty", new Vector3(300, 0, 0)); // 아무것도 재생하지 않음 = Base Layer 그대로
            sm.defaultState = empty;

            var hitState = sm.AddState("Hit", new Vector3(300, 120, 0));
            hitState.motion = hit;

            var toHit = sm.AddAnyStateTransition(hitState);
            toHit.hasExitTime = false;
            toHit.duration = 0.05f;
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, Hit);

            var back = hitState.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.8f;
            back.duration = 0.2f;

            // 공격: 오른손을 들어 드론에게 지시하는 동작 (Unity-chan에 근접 공격 클립이 없어 원거리 지휘형으로 구성)
            if (point == null) return;
            AddUpperAction(sm, empty, "Attack", point, Attack, speed: 2.4f, offset: 0.1f, exit: 0.55f, pos: new Vector3(560, 0, 0));
            AddUpperAction(sm, empty, "Skill", point, Skill, speed: 1.3f, offset: 0.05f, exit: 0.8f, pos: new Vector3(560, 120, 0));
        }

        static void AddUpperAction(AnimatorStateMachine sm, AnimatorState empty, string name, AnimationClip clip,
            string trigger, float speed, float offset, float exit, Vector3 pos)
        {
            var state = sm.AddState(name, pos);
            state.motion = clip;
            state.speed = speed;

            var enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0.06f;
            enter.offset = offset;
            enter.canTransitionToSelf = true;
            enter.AddCondition(AnimatorConditionMode.If, 0, trigger);

            var back = state.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = exit;
            back.duration = 0.2f;
        }

        static void ClearController(AnimatorController controller)
        {
            // 레이어·파라미터 제거
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                controller.RemoveLayer(i);
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
                controller.RemoveParameter(i);

            // 레이어를 지워도 남는 하위 에셋(블렌드 트리, 상태 머신 등) 정리
            string path = AssetDatabase.GetAssetPath(controller);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (sub == null || sub == controller) continue;
                Object.DestroyImmediate(sub, true);
            }

            controller.AddLayer("Base Layer");
        }

        static AvatarMask CreateUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMaskPath);
            bool isNew = mask == null;
            if (isNew) mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            if (isNew) AssetDatabase.CreateAsset(mask, UpperMaskPath);
            else EditorUtility.SetDirty(mask);
            return mask;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }

        static AnimationClip LoadClip(string fbxName)
        {
            string path = Path.Combine(AnimFolder, fbxName).Replace('\\', '/');
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }
    }
}
