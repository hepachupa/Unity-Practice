using LNStudy.CameraSystem;
using LNStudy.Player;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 시사의 기본 공격: 앵그리버드식 라멘 던지기.
    /// - 좌클릭을 누르고 있으면 힘을 모은다(멀리 날아감). 모으는 동안 예상 궤적과 착지 원이 보인다.
    /// - 떼면 포물선으로 던진다. 쿨타임 1초.
    /// - 방향: 3인칭이면 카메라 정면, 아니면 캐릭터 정면. 그 방향 원뿔 안에 적이 있으면 방향만 살짝 보정(거리는 플레이어가 조절).
    /// 외형(시사) 루트에 붙이며, 켜져 있는 동안 PlayerCombat의 드론 기본 공격을 대신한다.
    /// </summary>
    public class RamenThrower : MonoBehaviour, IPrimaryAttackOverride
    {
        [Header("Refs")]
        [SerializeField] RamenProjectile ramenPrefab;
        [SerializeField] LineRenderer trajectoryLine;
        [SerializeField] LineRenderer landingRing;
        [Tooltip("손에 들고 있는 모습 (던지면 숨겼다가 쿨타임이 끝나면 다시 보임)")]
        [SerializeField] GameObject heldVisual;

        [Header("Throw")]
        [SerializeField] float cooldown = 1f;
        [Tooltip("이 시간 동안 누르면 최대 사거리")]
        [SerializeField] float chargeTime = 0.8f;
        [SerializeField] float minRange = 3f;
        [SerializeField] float maxRange = 14f;
        [SerializeField, Range(15f, 75f)] float launchAngle = 42f;
        [Tooltip("발 기준 던지는 위치 (머리 위)")]
        [SerializeField] Vector3 throwOffset = new Vector3(0f, 1.05f, 0.25f);
        [SerializeField] LayerMask hitMask = 1;

        [Header("Aim Assist")]
        [SerializeField] LayerMask enemyMask;
        [SerializeField] float assistAngle = 25f;

        [Header("Preview")]
        [SerializeField] float previewStep = 0.03f;
        [SerializeField] int previewMaxPoints = 90;
        [SerializeField] float ringRadius = 1.8f;

        public bool IsOverriding => isActiveAndEnabled;
        public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);
        public float Charge => charge;

        PlayerInputReader input;
        PlayerMotor motor;
        Rigidbody body;
        ProceduralCharacterAnimator proc;
        ICharacterVisual visual;
        Health ownerHealth;
        bool charging;
        float charge;
        float readyTime;
        Vector3 aimDir;
        readonly Vector3[] points = new Vector3[128];
        readonly Collider[] overlap = new Collider[32];

        void Awake()
        {
            input = GetComponentInParent<PlayerInputReader>();
            motor = GetComponentInParent<PlayerMotor>();
            body = motor != null ? motor.GetComponent<Rigidbody>() : null;
            ownerHealth = GetComponentInParent<Health>();
            proc = GetComponent<ProceduralCharacterAnimator>();
            visual = GetComponentInChildren<ICharacterVisual>();
        }

        void OnDisable()
        {
            charging = false;
            charge = 0f;
            ShowPreview(false);
            proc?.SetCharge(0f);
            motor?.SetFacingLock(this, false);
        }

        void Update()
        {
            if (input == null || motor == null) return;

            bool canAct = (ThirdPersonCamera.Active == null || ThirdPersonCamera.Active.IsCursorLocked)
                          && (ownerHealth == null || !ownerHealth.IsDead)
                          && !motor.IsClimbing;
            bool held = canAct && input.AttackHeld;
            if (heldVisual != null) heldVisual.SetActive(Time.time >= readyTime);
            input.ClearAttack(); // 버퍼 입력은 쓰지 않는다 (누르고 떼는 동작으로만 던짐)

            if (Time.time < readyTime)
            {
                if (!held) charging = false;
                ShowPreview(false);
                proc?.SetCharge(0f);
                return;
            }

            if (held)
            {
                charging = true;
                charge = Mathf.Clamp01(charge + Time.deltaTime / Mathf.Max(0.05f, chargeTime));
                aimDir = ComputeAimDirection();
                Vector3 v = LaunchVelocity(charge);
                DrawPreview(ThrowOrigin(), v);
                proc?.SetCharge(charge);
                motor.SetFacingLock(this, true);
            }
            else if (charging)
            {
                Throw();
            }
        }

        void FixedUpdate()
        {
            // 모으는 동안 조준 방향을 바라본다
            if (charging && body != null && aimDir.sqrMagnitude > 0.001f)
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(aimDir), 900f * Time.fixedDeltaTime));
        }

        void Throw()
        {
            charging = false;
            Vector3 origin = ThrowOrigin();
            Vector3 v = LaunchVelocity(charge);
            if (ramenPrefab != null)
                RamenProjectile.Throw(ramenPrefab, origin, v, hitMask, motor.gameObject);

            readyTime = Time.time + cooldown;
            charge = 0f;
            ShowPreview(false);
            proc?.SetCharge(0f);
            if (proc != null) proc.PlayAttack();
            else visual?.PlayAttack();
            motor.SetFacingLock(this, false);
            ThirdPersonCamera.Active?.Shake(0.03f, 0.08f);
        }

        Vector3 ThrowOrigin()
        {
            Transform root = motor.transform;
            Vector3 fwd = aimDir.sqrMagnitude > 0.001f ? aimDir : root.forward;
            return root.position + Vector3.up * throwOffset.y + fwd * throwOffset.z;
        }

        Vector3 ComputeAimDirection()
        {
            Vector3 dir;
            var cam = ThirdPersonCamera.Active;
            if (cam != null) dir = cam.transform.forward;
            else dir = motor.transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;

            // 방향 보정: 원뿔 안에서 가장 정면에 가까운 적 쪽으로
            Vector3 origin = motor.transform.position;
            int n = Physics.OverlapSphereNonAlloc(origin, maxRange, overlap, enemyMask, QueryTriggerInteraction.Ignore);
            float best = assistAngle;
            Vector3 bestDir = dir;
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (h == null || h.IsDead || h == ownerHealth) continue;
                Vector3 to = h.transform.position - origin;
                to.y = 0f;
                float a = Vector3.Angle(dir, to);
                if (a < best) { best = a; bestDir = to.normalized; }
            }
            return bestDir;
        }

        /// <summary>평지 기준 사거리 공식으로 초기 속도 계산: v = √(gR / sin2θ)</summary>
        Vector3 LaunchVelocity(float t)
        {
            float range = Mathf.Lerp(minRange, maxRange, t);
            float g = -Physics.gravity.y;
            float theta = launchAngle * Mathf.Deg2Rad;
            float speed = Mathf.Sqrt(g * range / Mathf.Sin(2f * theta));
            Vector3 dir = aimDir.sqrMagnitude > 0.001f ? aimDir : motor.transform.forward;
            return dir * (speed * Mathf.Cos(theta)) + Vector3.up * (speed * Mathf.Sin(theta));
        }

        void DrawPreview(Vector3 origin, Vector3 v)
        {
            if (trajectoryLine == null) return;
            int count = 0;
            Vector3 prev = origin;
            points[count++] = origin;
            bool landed = false;
            Vector3 hitPoint = origin, hitNormal = Vector3.up;

            for (int i = 1; i < Mathf.Min(previewMaxPoints, points.Length); i++)
            {
                float t = i * previewStep;
                Vector3 p = origin + v * t + 0.5f * Physics.gravity * t * t;
                Vector3 seg = p - prev;
                if (Physics.Raycast(prev, seg.normalized, out var hit, seg.magnitude, hitMask, QueryTriggerInteraction.Ignore))
                {
                    points[count++] = hit.point;
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    landed = true;
                    break;
                }
                points[count++] = p;
                prev = p;
            }

            trajectoryLine.positionCount = count;
            trajectoryLine.SetPositions(points);
            trajectoryLine.enabled = true;

            if (landingRing != null)
            {
                landingRing.enabled = landed;
                if (landed) DrawRing(hitPoint + hitNormal * 0.03f, hitNormal);
            }
        }

        void DrawRing(Vector3 center, Vector3 normal)
        {
            const int segs = 40;
            landingRing.positionCount = segs;
            landingRing.loop = true;
            Vector3 a = Vector3.Cross(normal, Vector3.forward);
            if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(normal, Vector3.right);
            a.Normalize();
            Vector3 b = Vector3.Cross(normal, a);
            for (int i = 0; i < segs; i++)
            {
                float ang = i / (float)segs * Mathf.PI * 2f;
                landingRing.SetPosition(i, center + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * ringRadius);
            }
        }

        void ShowPreview(bool on)
        {
            if (trajectoryLine != null) trajectoryLine.enabled = on;
            if (landingRing != null) landingRing.enabled = on;
        }
    }
}
