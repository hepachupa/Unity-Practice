using LNStudy.Combat;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 리틀 나이트메어식 거인 감시자. 플레이 라인 뒤쪽(깊이 방향)을 오가며 손전등으로 길을 비춘다.
    /// - 빛 원뿔 안 + 시야가 막히지 않음 + 덤불에 숨지 않음 → 의심 게이지가 오른다
    /// - 게이지가 다 차면 "들켰다": 플레이어를 체크포인트로 되돌린다 (피해 포함)
    /// - 의심 중엔 걸음을 멈추고 그쪽을 비춘다 → 빨리 숨으면 넘어갈 수 있다
    /// 전투로 잡을 수 없는 존재 (체력 없음).
    /// </summary>
    public class GiantWatcher : MonoBehaviour
    {
        [Header("Patrol")]
        [SerializeField] Vector3 pointA;
        [SerializeField] Vector3 pointB;
        [SerializeField] float walkSpeed = 1.4f;
        [SerializeField] float waitAtEnds = 2.5f;
        [SerializeField] Terrain ground;

        [Header("Flashlight")]
        [SerializeField] Light flashlight;
        [Tooltip("손전등이 좌우로 훑는 각도")]
        [SerializeField] float sweepAngle = 35f;
        [SerializeField] float sweepSpeed = 0.35f;
        [SerializeField] float pitchDown = 38f;

        [Header("Detection")]
        [SerializeField] Transform player;
        [SerializeField] LayerMask occluders = 1;
        [Tooltip("들키기까지 걸리는 시간")]
        [SerializeField] float detectTime = 0.7f;
        [SerializeField] float catchDamage = 35f;
        [SerializeField] float coolDownAfterCatch = 3f;

        [Header("Animation")]
        [SerializeField] Animator animator;

        public float Suspicion => suspicion;

        static readonly int SpeedId = Animator.StringToHash("Speed");

        float suspicion;
        float waitTimer;
        float ignoreUntil;
        bool toB = true;
        float sweepPhase;
        Color baseColor;
        PlayerCombat playerCombat;

        void Start()
        {
            if (player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
            }
            if (player != null) playerCombat = player.GetComponent<PlayerCombat>();
            if (flashlight != null) baseColor = flashlight.color;
            transform.position = Snap(pointA);
        }

        Vector3 Snap(Vector3 p)
        {
            if (ground != null) p.y = ground.SampleHeight(p) + ground.transform.position.y;
            return p;
        }

        void Update()
        {
            bool sees = CanSeePlayer();
            if (Time.time < ignoreUntil) sees = false;

            suspicion = sees ? suspicion + Time.deltaTime / detectTime : Mathf.Max(0f, suspicion - Time.deltaTime * 0.6f);
            if (suspicion >= 1f) Catch();

            bool alert = suspicion > 0.05f;
            Patrol(alert);
            AimFlashlight(alert);

            if (flashlight != null)
                flashlight.color = Color.Lerp(baseColor, new Color(1f, 0.25f, 0.15f), Mathf.Clamp01(suspicion));
        }

        void Patrol(bool alert)
        {
            float speed = 0f;
            if (!alert)
            {
                if (waitTimer > 0f) waitTimer -= Time.deltaTime;
                else
                {
                    Vector3 target = Snap(toB ? pointB : pointA);
                    Vector3 to = target - transform.position;
                    to.y = 0f;
                    if (to.magnitude < 0.2f)
                    {
                        toB = !toB;
                        waitTimer = waitAtEnds;
                    }
                    else
                    {
                        Vector3 step = to.normalized * Mathf.Min(walkSpeed * Time.deltaTime, to.magnitude);
                        transform.position = Snap(transform.position + step);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 90f * Time.deltaTime);
                        speed = walkSpeed;
                    }
                }
            }
            else if (player != null)
            {
                // 의심 중: 멈춰서 플레이어 쪽을 본다
                Vector3 to = player.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 120f * Time.deltaTime);
            }
            if (animator != null) animator.SetFloat(SpeedId, speed, 0.15f, Time.deltaTime);
        }

        void AimFlashlight(bool alert)
        {
            if (flashlight == null) return;
            Quaternion look;
            if (alert && player != null)
            {
                look = Quaternion.LookRotation((player.position + Vector3.up * 0.6f) - flashlight.transform.position);
            }
            else
            {
                sweepPhase += Time.deltaTime * sweepSpeed * Mathf.PI * 2f;
                float yaw = Mathf.Sin(sweepPhase) * sweepAngle;
                look = transform.rotation * Quaternion.Euler(pitchDown, yaw, 0f);
            }
            flashlight.transform.rotation = Quaternion.Slerp(flashlight.transform.rotation, look, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        bool CanSeePlayer()
        {
            if (player == null || flashlight == null || HidingSpot.IsPlayerHidden) return false;
            Vector3 from = flashlight.transform.position;
            Vector3 target = player.position + Vector3.up * 0.6f;
            Vector3 to = target - from;
            float dist = to.magnitude;
            if (dist > flashlight.range) return false;
            if (Vector3.Angle(flashlight.transform.forward, to) > flashlight.spotAngle * 0.5f) return false;
            return !Physics.Raycast(from, to / dist, dist - 0.3f, occluders, QueryTriggerInteraction.Ignore);
        }

        void Catch()
        {
            suspicion = 0f;
            ignoreUntil = Time.time + coolDownAfterCatch;
            StageHUD.Instance?.ShowToast("들켰다! 덤불에 숨어서 지나가자", 2.5f);
            if (playerCombat != null) playerCombat.ReturnToCheckpoint(catchDamage);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pointA, pointB);
            Gizmos.DrawWireSphere(pointA, 0.4f);
            Gizmos.DrawWireSphere(pointB, 0.4f);
        }
    }
}
