using LNStudy.Interaction;
using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// PlayerMotor·PlayerGrab 상태를 Animator 파라미터로 옮기고, 잡기 중에는 손 IK로 물체를 짚게 한다.
    /// Animator가 붙은 캐릭터 모델(Player의 자식)에 붙인다. 이동은 물리가 담당하므로 루트 모션은 쓰지 않는다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour, ICharacterVisual
    {
        [Tooltip("속도 값 변화를 부드럽게 (블렌드 트리 떨림 방지)")]
        [SerializeField] float speedDampTime = 0.1f;
        [Tooltip("이 시간보다 짧게 땅에서 떨어진 건 공중으로 보지 않는다 (달리다 타일 이음새에서 점프 모션이 깜빡이는 것 방지)")]
        [SerializeField] float airborneGrace = 0.15f;

        [Header("Grab Hand IK")]
        [Tooltip("손을 짚는 높이 (캐릭터 발 기준)")]
        [SerializeField] float handHeight = 0.55f;
        [Tooltip("양손 사이 간격의 절반")]
        [SerializeField] float handSpread = 0.15f;
        [SerializeField] float ikBlendSpeed = 8f;

        [Header("Idle Variants")]
        [Tooltip("가만히 서 있을 때 대기 포즈를 바꾸는 간격 (초, 최소~최대 무작위)")]
        [SerializeField] Vector2 idleFidgetInterval = new Vector2(6f, 10f);
        [Tooltip("대기 포즈 종류 수 (컨트롤러의 IdleVariant1~N)")]
        [SerializeField] int idleVariantCount = 4;
        [Tooltip("피격 시 잠깐 짓는 표정")]
        [SerializeField] FaceExpression hitExpression = FaceExpression.EyeClose;
        [SerializeField] float hitExpressionTime = 0.6f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int GrabId = Animator.StringToHash("Grab");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int ClimbId = Animator.StringToHash("Climb");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int SkillId = Animator.StringToHash("Skill");
        static readonly int IdleVariantId = Animator.StringToHash("IdleVariant");
        static readonly int IdleFidgetId = Animator.StringToHash("IdleFidget");

        Animator animator;
        PlayerMotor motor;
        PlayerGrab grab;
        Rigidbody body;
        PlayerFace face;
        float ikWeight;
        float idleTimer;
        float nextFidget;
        int lastVariant;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            motor = GetComponentInParent<PlayerMotor>();
            grab = GetComponentInParent<PlayerGrab>();
            body = GetComponentInParent<Rigidbody>();
            face = GetComponent<PlayerFace>();
            ScheduleFidget();
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.Jumped += OnJumped;
            motor.ClimbStarted += OnClimbStarted;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.Jumped -= OnJumped;
            motor.ClimbStarted -= OnClimbStarted;
        }

        public Animator HumanoidAnimator => animator != null ? animator : GetComponent<Animator>();
        public float ShoulderHeight => 1.3f;

        void OnJumped() => animator.SetTrigger(JumpId);
        void OnClimbStarted() => animator.SetTrigger(ClimbId);

        /// <summary>피격 반응 (상체 레이어). 전투 시스템에서 호출</summary>
        public void PlayHit()
        {
            animator.SetTrigger(HitId);
            if (face != null) face.SetExpression(hitExpression, hitExpressionTime);
        }

        /// <summary>공격 지시 동작 (상체)</summary>
        public void PlayAttack()
        {
            animator.SetTrigger(AttackId);
            idleTimer = 0f;
        }

        /// <summary>스킬 지시 동작 (상체)</summary>
        public void PlaySkill()
        {
            animator.SetTrigger(SkillId);
            idleTimer = 0f;
        }

        /// <summary>대기 포즈를 직접 재생 (1~idleVariantCount). 멈춰 있을 때만 들어간다</summary>
        public void PlayIdleVariant(int variant)
        {
            variant = Mathf.Clamp(variant, 1, idleVariantCount);
            lastVariant = variant;
            animator.SetInteger(IdleVariantId, variant);
            animator.SetTrigger(IdleFidgetId);
            idleTimer = 0f;
            ScheduleFidget();
        }

        void ScheduleFidget() => nextFidget = Random.Range(idleFidgetInterval.x, idleFidgetInterval.y);

        void UpdateIdleFidget()
        {
            bool idle = motor.IsGrounded && !motor.IsClimbing && !motor.IsCrouching
                        && motor.PlanarVelocity.sqrMagnitude < 0.01f
                        && (grab == null || grab.Current == null);
            if (!idle)
            {
                // 움직이면 타이머와 남은 트리거를 지워 나중에 엉뚱하게 재생되지 않게 한다
                idleTimer = 0f;
                animator.ResetTrigger(IdleFidgetId);
                return;
            }

            idleTimer += Time.deltaTime;
            if (idleTimer < nextFidget || idleVariantCount <= 0) return;

            // 직전과 다른 포즈를 고른다
            int v = Random.Range(1, idleVariantCount + 1);
            if (idleVariantCount > 1 && v == lastVariant) v = v % idleVariantCount + 1;
            PlayIdleVariant(v);
        }

        void Update()
        {
            if (motor == null) return;

            animator.SetFloat(SpeedId, motor.PlanarVelocity.magnitude, speedDampTime, Time.deltaTime);
            // 잠깐 떠 있는 것(이음새·턱)은 무시. 점프는 Jump 트리거가 따로 처리한다
            bool grounded = motor.IsGrounded || motor.IsClimbing || motor.TimeSinceGrounded < airborneGrace;
            animator.SetBool(GroundedId, grounded);
            animator.SetFloat(VerticalSpeedId, body != null && !body.isKinematic ? body.linearVelocity.y : 0f);
            animator.SetBool(CrouchId, motor.IsCrouching);
            animator.SetBool(GrabId, grab != null && grab.Current != null);
            UpdateIdleFidget();
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;

            Grabbable held = grab != null ? grab.Current : null;
            float target = held != null ? 1f : 0f;
            ikWeight = Mathf.MoveTowards(ikWeight, target, ikBlendSpeed * Time.deltaTime);
            if (ikWeight <= 0f || held == null)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                return;
            }

            // 물체 표면에서 손 높이에 해당하는 지점을 찾아 양손을 좌우로 벌려 짚는다
            Vector3 root = transform.position;
            Vector3 right = transform.right;
            Vector3 chestPoint = root + Vector3.up * handHeight;
            Collider col = held.GetComponentInChildren<Collider>();
            Vector3 surface = col != null ? col.ClosestPoint(chestPoint + transform.forward * 0.5f) : held.transform.position;
            surface.y = Mathf.Clamp(surface.y, root.y + 0.2f, chestPoint.y);

            SetHand(AvatarIKGoal.LeftHand, surface - right * handSpread);
            SetHand(AvatarIKGoal.RightHand, surface + right * handSpread);
        }

        void SetHand(AvatarIKGoal goal, Vector3 position)
        {
            animator.SetIKPositionWeight(goal, ikWeight);
            animator.SetIKPosition(goal, position);
        }
    }
}
