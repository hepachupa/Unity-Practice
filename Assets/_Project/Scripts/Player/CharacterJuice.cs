using LNStudy.CameraSystem;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 캐릭터 손맛 (모델만 건드리는 시각·청각 효과. 물리·판정에는 영향 없음)
    /// - 스쿼시&스트레치: 점프 순간 늘어나고, 착지하면 낙하 속도만큼 찌그러졌다가 튕겨 돌아옴
    /// - 기울기: 가속 방향으로 몸을 숙이고, 상자를 밀면 앞으로·당기면 뒤로 버팀
    /// - 달리기 통통 튐 + 발소리 + 발밑 먼지, 급하게 방향을 틀면 미끄럼 먼지
    /// - 무거운 캐릭터는 높은 곳에서 착지하면 화면이 흔들림
    /// SideScrollFacing보다 늦게 실행되어 그 회전 위에 기울기를 덧씌운다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CharacterJuice : MonoBehaviour
    {
        [Tooltip("무거운 캐릭터: 착지 소리가 낮고 화면이 흔들림")]
        [SerializeField] bool heavy;
        [SerializeField] Material dustMaterial;
        [Header("Squash")]
        [SerializeField] float stiffness = 260f;
        [SerializeField] float damping = 14f;
        [SerializeField] float jumpStretch = 0.28f;
        [SerializeField] float landSquashPerSpeed = 0.045f;
        [SerializeField] float maxSquash = 0.38f;
        [Header("Lean")]
        [SerializeField] float accelLean = 1.4f;
        [SerializeField] float maxLean = 14f;
        [SerializeField] float pushLean = 11f;
        [Header("Steps")]
        [SerializeField] float strideLength = 0.55f;
        [SerializeField] float runBob = 0.06f;

        PlayerMotor motor;
        PlayerGrab grab;
        SideScrollFacing facing;
        Rigidbody body;
        Vector3 baseScale;
        float squash = 1f, squashVel;
        float lean, leanVel;
        float prevVelX;
        float accelX;
        float minAirVy;
        float strideAcc;
        float bobPhase;
        float lastSkid;
        float facingSign;
        float strainTimer;

        void Awake()
        {
            motor = GetComponentInParent<PlayerMotor>();
            grab = GetComponentInParent<PlayerGrab>();
            body = motor != null ? motor.GetComponent<Rigidbody>() : null;
            facing = GetComponent<SideScrollFacing>();
            baseScale = transform.localScale;
            DustFx.Init(dustMaterial);
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.Jumped += OnJumped;
            motor.Landed += OnLanded;
            motor.ClimbStarted += OnClimb;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.Jumped -= OnJumped;
            motor.Landed -= OnLanded;
            motor.ClimbStarted -= OnClimb;
            transform.localScale = baseScale;
        }

        Vector3 Feet => motor.transform.position + Vector3.up * 0.05f;

        /// <summary>통통 튀는 반응 (교체·부르기 등). amount&gt;0 이면 늘어남</summary>
        public void Pop(float amount = 0.25f) => squashVel += amount * 12f;

        void OnJumped()
        {
            squashVel += jumpStretch * 14f;
            Sfx.Play("jump", Feet, heavy ? 0.55f : 0.5f, heavy ? 0.85f : 1.25f);
            DustFx.Puff(Feet, heavy ? 6 : 4, heavy ? 0.4f : 0.28f, 1.2f);
        }

        void OnClimb()
        {
            squashVel += 0.2f * 14f;
            Sfx.Play("jump", Feet, 0.35f, heavy ? 0.7f : 1.05f);
        }

        void OnLanded()
        {
            float impact = Mathf.Max(0f, -minAirVy);
            minAirVy = 0f;
            if (impact < 1.2f) return;

            squashVel -= Mathf.Min(maxSquash, impact * landSquashPerSpeed) * 16f;
            float vol = Mathf.Clamp01(impact / 9f) * (heavy ? 1f : 0.75f);
            Sfx.Play("land", Feet, 0.25f + vol, heavy ? 0.75f : 1.3f);
            int dust = Mathf.RoundToInt(Mathf.Lerp(3, heavy ? 14 : 9, Mathf.Clamp01(impact / 9f)));
            DustFx.Puff(Feet, dust, heavy ? 0.45f : 0.3f, 1.4f + impact * 0.12f);
            if (heavy && impact > 4.5f) SideFollowCamera.Shake(Mathf.Clamp(impact * 0.012f, 0.03f, 0.12f), 0.22f);
        }

        void Update()
        {
            if (motor == null || body == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = body.isKinematic ? Vector3.zero : body.linearVelocity;

            if (!motor.IsGrounded) minAirVy = Mathf.Min(minAirVy, v.y);

            // 가속도 (부드럽게)
            float a = (v.x - prevVelX) / dt;
            prevVelX = v.x;
            accelX = Mathf.Lerp(accelX, a, 1f - Mathf.Exp(-12f * dt));

            bool holding = grab != null && grab.Current != null;
            float speed = Mathf.Abs(v.x);

            // 발소리·먼지 (땅 위에서 움직일 때)
            if (motor.IsGrounded && speed > 0.4f && !motor.IsClimbing)
            {
                strideAcc += speed * dt;
                bobPhase += speed / strideLength * Mathf.PI * dt;
                float stride = strideLength * (motor.IsRunning ? 1.3f : 1f);
                if (strideAcc >= stride)
                {
                    strideAcc = 0f;
                    Sfx.Play("step", Feet, motor.IsRunning ? 0.5f : 0.3f, (heavy ? 0.8f : 1.35f) * Random.Range(0.9f, 1.1f));
                    if (motor.IsRunning) DustFx.Puff(Feet - motor.transform.forward * 0.15f, 2, heavy ? 0.28f : 0.2f, 0.7f);
                }
            }
            else
            {
                bobPhase = 0f;
            }

            // 급회전 미끄럼
            float sign = Mathf.Abs(motor.transform.forward.x) > 0.5f ? Mathf.Sign(motor.transform.forward.x) : facingSign;
            if (sign != facingSign && facingSign != 0f && motor.IsGrounded && speed > 2f && Time.time - lastSkid > 0.25f)
            {
                lastSkid = Time.time;
                Sfx.Play("skid", Feet, 0.5f, heavy ? 0.8f : 1.2f);
                DustFx.Puff(Feet, 5, heavy ? 0.35f : 0.25f, 1.8f);
                squashVel -= 0.12f * 14f;
            }
            facingSign = sign;

            // 상자 끄는 중: 힘주는 들썩임 + 먼지
            if (holding && speed > 0.3f)
            {
                strainTimer += dt;
                if (strainTimer > 0.42f)
                {
                    strainTimer = 0f;
                    squashVel -= 0.08f * 14f;
                    DustFx.Puff(Feet, 2, 0.25f, 0.6f);
                }
            }

            // 스쿼시 스프링
            float airStretch = motor.IsGrounded ? 0f : Mathf.Clamp(Mathf.Abs(v.y) * 0.025f, 0f, 0.12f);
            float target = 1f + airStretch;
            squashVel += (-(squash - target) * stiffness - squashVel * damping) * dt;
            squash += squashVel * dt;
            squash = Mathf.Clamp(squash, 1f - maxSquash, 1f + maxSquash);

            // 기울기 (+면 진행 방향(X+)으로 숙임)
            float wantLean = Mathf.Clamp(accelX * accelLean * 0.1f + v.x * 0.9f, -maxLean, maxLean);
            if (holding && speed > 0.2f)
            {
                bool pushing = Mathf.Sign(v.x) == Mathf.Sign(motor.transform.forward.x);
                wantLean = Mathf.Sign(v.x) * (pushing ? pushLean : -pushLean * 0.7f);
            }
            if (!motor.IsGrounded || motor.IsClimbing) wantLean *= 0.3f;
            lean = Mathf.SmoothDamp(lean, wantLean, ref leanVel, 0.08f);
        }

        void LateUpdate()
        {
            if (motor == null) return;
            float bob = motor.IsRunning && motor.IsGrounded ? Mathf.Abs(Mathf.Sin(bobPhase)) * runBob : 0f;
            float sy = squash + bob;
            float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
            transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

            // 정면 보기 컴포넌트가 이번 프레임 회전을 써 주지 않으면 기본 자세에서 시작 (기울기 누적 방지)
            if (facing == null || !facing.enabled) transform.localRotation = Quaternion.identity;
            // X+로 숙이려면 Z축 기준 음의 회전
            transform.rotation = Quaternion.AngleAxis(-lean, Vector3.forward) * transform.rotation;
        }
    }
}
