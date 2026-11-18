using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 뼈(리그)가 없는 통짜 모델을 코드로 움직인다. (치이카와식 마스코트 캐릭터용)
    /// 이동 속도·접지·점프 이벤트를 읽어서 시각용 자식(visual)의 위치·회전·크기만 바꾼다. 물리 몸체는 건드리지 않는다.
    /// - 걷기/달리기: 깡총거리는 바운스 + 좌우 뒤뚱 + 진행 방향 앞기울기 (속도에 비례해 박자가 빨라짐)
    /// - 대기: 숨쉬기 (살짝 커졌다 작아짐)
    /// - 점프/착지: 스프링 기반 늘어남·찌그러짐 (squash & stretch)
    /// - 웅크리기: 납작하게
    /// - 공격/스킬/피격: 짧은 포즈 곡선 (앞으로 콩 박기 / 한 바퀴 회전 / 뒤로 휘청)
    /// </summary>
    public class ProceduralCharacterAnimator : MonoBehaviour, ICharacterVisual
    {
        [SerializeField] Transform visual;
        [SerializeField] float shoulderHeight = 0.8f;

        [Header("Locomotion")]
        [Tooltip("1 m/s 당 초당 걸음 수")]
        [SerializeField] float stepsPerMeter = 1.6f;
        [SerializeField] float hopHeight = 0.06f;
        [SerializeField] float waddleAngle = 7f;
        [SerializeField] float leanPerSpeed = 3f;
        [SerializeField] float maxLean = 14f;

        [Header("Idle")]
        [SerializeField] float breatheAmount = 0.025f;
        [SerializeField] float breatheSpeed = 1.6f;

        [Header("Squash & Stretch (스프링)")]
        [SerializeField] float springStiffness = 260f;
        [SerializeField] float springDamping = 14f;
        [SerializeField] float jumpStretch = 0.25f;
        [SerializeField] float landSquash = 0.3f;
        [SerializeField] float crouchSquash = 0.3f;

        [Header("Actions")]
        [SerializeField] float attackTime = 0.22f;
        [SerializeField] float skillTime = 0.5f;
        [SerializeField] float hitTime = 0.3f;

        PlayerMotor motor;
        Vector3 baseLocalPos;
        Quaternion baseLocalRot;
        Vector3 baseLocalScale;

        float stepPhase;
        float squash, squashVel;   // + = 늘어남, - = 찌그러짐
        float lean;
        float attackT = 1f, skillT = 1f, hitT = 1f;
        float idleTime;
        float charge, chargeSmooth;

        public Animator HumanoidAnimator => null;
        public float ShoulderHeight => shoulderHeight;

        void Awake()
        {
            motor = GetComponentInParent<PlayerMotor>();
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null)
            {
                baseLocalPos = visual.localPosition;
                baseLocalRot = visual.localRotation;
                baseLocalScale = visual.localScale;
            }
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.Jumped += OnJumped;
            motor.Landed += OnLanded;
            squash = squashVel = 0f;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.Jumped -= OnJumped;
            motor.Landed -= OnLanded;
            if (visual != null)
            {
                visual.localPosition = baseLocalPos;
                visual.localRotation = baseLocalRot;
                visual.localScale = baseLocalScale;
            }
        }

        void OnJumped() => squashVel += jumpStretch * 18f;
        void OnLanded() => squashVel -= landSquash * 18f;

        /// <summary>힘 모으기 자세 (0~1): 몸을 낮추고 뒤로 젖힌다 (새총을 당기는 느낌)</summary>
        public void SetCharge(float amount) => charge = Mathf.Clamp01(amount);

        public void PlayAttack() => attackT = 0f;
        public void PlaySkill() => skillT = 0f;
        public void PlayHit() => hitT = 0f;

        void LateUpdate()
        {
            if (visual == null || motor == null) return;
            float dt = Time.deltaTime;

            Vector3 vel = motor.PlanarVelocity;
            float speed = vel.magnitude;
            bool grounded = motor.IsGrounded;
            bool moving = speed > 0.15f && grounded;

            // 걸음 박자
            if (moving) stepPhase += speed * stepsPerMeter * dt * Mathf.PI;
            else stepPhase = Mathf.MoveTowards(stepPhase, Mathf.Round(stepPhase / Mathf.PI) * Mathf.PI, dt * 6f);
            float moveWeight = Mathf.Clamp01(speed / 2f) * (grounded ? 1f : 0f);

            float hop = Mathf.Abs(Mathf.Sin(stepPhase)) * hopHeight * moveWeight;
            float waddle = Mathf.Sin(stepPhase) * waddleAngle * moveWeight;

            // 앞기울기 (로컬 기준 전진 속도)
            float forwardSpeed = Vector3.Dot(vel, transform.forward);
            lean = Mathf.Lerp(lean, Mathf.Clamp(forwardSpeed * leanPerSpeed, -maxLean, maxLean), 1f - Mathf.Exp(-8f * dt));

            // 대기 숨쉬기
            idleTime = moving ? 0f : idleTime + dt;
            float breathe = moving ? 0f : Mathf.Sin(idleTime * breatheSpeed * Mathf.PI * 2f) * breatheAmount;

            // 스프링: 목표값(웅크리기)으로 돌아가며 출렁임
            float target = motor.IsCrouching ? -crouchSquash : 0f;
            if (!grounded) target += 0.05f; // 공중에서는 살짝 늘어난 상태
            squashVel += (target - squash) * springStiffness * dt;
            squashVel *= Mathf.Exp(-springDamping * dt);
            squash = Mathf.Clamp(squash + squashVel * dt, -0.6f, 0.6f);

            // 액션 포즈
            Vector3 actionPos = Vector3.zero;
            Vector3 actionEuler = Vector3.zero;
            float actionSquash = 0f;

            if (attackT < 1f)
            {
                attackT = Mathf.Min(1f, attackT + dt / attackTime);
                float k = Mathf.Sin(attackT * Mathf.PI); // 0 → 1 → 0
                actionPos += Vector3.forward * (0.18f * k);
                actionEuler.x += 18f * k;                  // 앞으로 콩
                actionSquash -= 0.15f * k;
            }
            if (skillT < 1f)
            {
                skillT = Mathf.Min(1f, skillT + dt / skillTime);
                float e = 1f - (1f - skillT) * (1f - skillT);
                actionEuler.y += 360f * e;                 // 제자리 한 바퀴
                actionPos += Vector3.up * (0.25f * Mathf.Sin(skillT * Mathf.PI));
                actionSquash += 0.15f * Mathf.Sin(skillT * Mathf.PI);
            }
            if (hitT < 1f)
            {
                hitT = Mathf.Min(1f, hitT + dt / hitTime);
                float k = Mathf.Sin(hitT * Mathf.PI) * (1f - hitT * 0.5f);
                actionEuler.x -= 22f * k;                  // 뒤로 휘청
                actionPos += Vector3.back * (0.08f * k);
                actionSquash -= 0.12f * k;
            }

            chargeSmooth = Mathf.Lerp(chargeSmooth, charge, 1f - Mathf.Exp(-14f * dt));
            actionSquash -= 0.22f * chargeSmooth;
            actionEuler.x -= 14f * chargeSmooth;
            actionPos += Vector3.back * (0.06f * chargeSmooth);
            // 다 모이면 살짝 부들부들
            if (chargeSmooth > 0.95f) actionEuler.z += Mathf.Sin(Time.time * 60f) * 1.5f;

            float s = squash + actionSquash + breathe;
            // 부피 보존: 위로 늘어나면 옆으로 가늘어진다
            float sy = 1f + s;
            float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.2f, sy));
            visual.localScale = Vector3.Scale(baseLocalScale, new Vector3(sxz, sy, sxz));

            visual.localPosition = baseLocalPos + Vector3.up * hop + actionPos;
            visual.localRotation = Quaternion.Euler(lean + actionEuler.x, actionEuler.y, waddle) * baseLocalRot;
        }
    }
}
