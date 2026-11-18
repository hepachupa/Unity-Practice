using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 플레이어가 조종하지 않는 동료 고양이 AI. 리더(조종 중인 고양이)를 일정 거리로 따라간다.
    /// - followDistance 안이면 멈추고, runDistance 밖이면 달린다
    /// - 앞이 낮은 턱으로 막히거나 리더가 위에 있으면 점프(→ PlayerMotor가 기어오르기까지 처리)
    /// - 너무 멀어지거나 오래 끼어 있으면 리더 뒤로 순간이동
    /// 이동은 PlayerMotor의 ExternalMove로 넣으므로 물리·애니메이션은 플레이어와 완전히 같다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader))]
    public class CatFollower : MonoBehaviour
    {
        [SerializeField] float followDistance = 2.2f;
        [SerializeField] float runDistance = 6f;
        [SerializeField] float teleportDistance = 25f;
        [SerializeField] float stuckTeleportTime = 4f;
        [SerializeField] LayerMask obstacleMask = 1;
        [Tooltip("리더 뒤 순간이동 거리")]
        [SerializeField] float teleportBehind = 1.6f;
        [Tooltip("멀어지거나 끼었을 때 자동 순간이동 (퍼즐 맵에서는 끈다: 닫힌 문을 넘어가 버리므로)")]
        [SerializeField] bool allowTeleport = true;

        public bool Following { get; private set; }
        public Transform Leader { get; private set; }

        PlayerMotor motor;
        PlayerInputReader input;
        Rigidbody rb;
        float stuckTimer;
        float lastJumpTime;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            rb = GetComponent<Rigidbody>();
        }

        public void SetFollowing(bool on, Transform leader)
        {
            Following = on && leader != null;
            Leader = leader;
            motor.UseExternalMove = Following;
            motor.ExternalMove = Vector3.zero;
            motor.ExternalRun = false;
            stuckTimer = 0f;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.ExternalMove = Vector3.zero;
            motor.ExternalRun = false;
        }

        void Update()
        {
            if (!Following || Leader == null) return;

            Vector3 to = Leader.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            float d = to.magnitude;

            if (allowTeleport && d > teleportDistance) { TeleportToLeader(); return; }

            if (d <= followDistance || motor.IsClimbing)
            {
                motor.ExternalMove = Vector3.zero;
                motor.ExternalRun = false;
                stuckTimer = 0f;
                return;
            }

            Vector3 dir = to / d;
            // 목표 거리 근처에서는 천천히 (리더 앞에서 우왕좌왕하지 않게)
            float k = Mathf.Clamp01((d - followDistance) / 1.2f);
            motor.ExternalMove = dir * Mathf.Max(0.35f, k);
            motor.ExternalRun = d > runDistance;

            if (motor.IsGrounded && Time.time - lastJumpTime > 0.8f)
            {
                Vector3 p = transform.position;
                bool lowBlocked = Physics.Raycast(p + Vector3.up * 0.3f, dir, 0.75f, obstacleMask, QueryTriggerInteraction.Ignore);
                bool highFree = !Physics.Raycast(p + Vector3.up * 1.7f, dir, 0.9f, obstacleMask, QueryTriggerInteraction.Ignore);
                bool leaderAbove = dy > 0.6f && d < 3.5f;
                if ((lowBlocked && highFree) || leaderAbove)
                {
                    input.RequestJump();
                    lastJumpTime = Time.time;
                }
            }

            // 끼임 감지: 따라가야 하는데 거의 못 움직이고 있음
            if (motor.PlanarVelocity.sqrMagnitude < 0.3f * 0.3f) stuckTimer += Time.deltaTime;
            else stuckTimer = Mathf.Max(0f, stuckTimer - Time.deltaTime * 2f);
            if (stuckTimer > stuckTeleportTime)
            {
                if (allowTeleport) TeleportToLeader();
                else { motor.ExternalMove = Vector3.zero; stuckTimer = stuckTeleportTime * 0.5f; } // 막혔으면 제자리에서 기다림
            }
        }

        /// <summary>리더 뒤쪽 바닥으로 순간이동</summary>
        public void TeleportToLeader()
        {
            if (Leader == null) return;
            Vector3 back = -Leader.forward;
            back.y = 0f;
            if (motor.SideScroll) back.z = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            Vector3 probe = Leader.position + back.normalized * teleportBehind + Vector3.up * 2f;
            Vector3 dest = Leader.position + Vector3.up * 0.1f;
            if (Physics.Raycast(probe, Vector3.down, out var hit, 5f, obstacleMask, QueryTriggerInteraction.Ignore)
                && !Physics.CheckSphere(hit.point + Vector3.up * 0.7f, 0.3f, obstacleMask, QueryTriggerInteraction.Ignore))
                dest = hit.point + Vector3.up * 0.05f;

            rb.position = dest;
            transform.position = dest;
            rb.linearVelocity = Vector3.zero;
            stuckTimer = 0f;
        }
    }
}
