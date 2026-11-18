using System;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.AI
{
    /// <summary>
    /// NavMeshAgent ↔ Rigidbody 하이브리드 넉백.
    ///
    /// 평소: NavMeshAgent가 이동을 담당하고 Rigidbody는 kinematic (물리에 밀리지 않음).
    /// 강한 공격을 맞으면:
    ///   1) 에이전트를 끄고 Rigidbody를 dynamic으로 → 실제 물리로 날아간다 (중력·마찰·충돌)
    ///   2) 날아가다 벽에 세게 부딪히면 "벽꽝": 추가 피해 + 튕김 + 히트스톱·화면 흔들림
    ///   3) 다른 양과 부딪히면 "볼링": 운동량 일부를 넘겨 그 양도 날린다 (연쇄)
    ///   4) 땅에서 멈추면(또는 시간 초과) NavMesh 위 가장 가까운 점으로 스냅 → kinematic 복귀 → 에이전트 Warp
    ///
    /// 핵심 포인트: 에이전트와 물리가 같은 Transform을 동시에 움직이지 않게 "소유권"을 명확히 넘긴다.
    /// 에이전트가 켜진 채로 Rigidbody를 움직이면 에이전트가 매 프레임 위치를 되돌려 서로 싸운다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsKnockback : MonoBehaviour
    {
        [Header("Launch")]
        [Tooltip("넉백이 이 값 이상이면 물리로 날아간다 (아래면 MonsterBrain의 짧은 경직 밀림)")]
        [SerializeField] float launchThreshold = 3f;
        [Tooltip("넉백 1당 초기 속도 (m/s)")]
        [SerializeField] float speedPerKnockback = 1.8f;
        [Tooltip("무거울수록 덜 날아간다 (속도 ÷ weight)")]
        [SerializeField] float weight = 1f;
        [Tooltip("수평 속도 대비 위로 띄우는 비율")]
        [SerializeField, Range(0f, 1f)] float lift = 0.3f;
        [SerializeField] float maxAirTime = 2f;
        [Tooltip("땅에 닿은 채 이 속도 아래로 느려지면 착지로 본다")]
        [SerializeField] float settleSpeed = 0.5f;
        [SerializeField] float navSnapRadius = 1.5f;

        [Header("Wall slam")]
        [Tooltip("벽에 이 속도 이상으로 박으면 벽꽝")]
        [SerializeField] float wallSlamSpeed = 4.5f;
        [SerializeField] float wallSlamDamage = 6f;
        [SerializeField, Range(0f, 1f)] float wallBounce = 0.25f;

        [Header("Chain (bowling)")]
        [SerializeField] float chainMinSpeed = 3f;
        [Tooltip("부딪힌 양에게 넘기는 속도 비율")]
        [SerializeField, Range(0f, 1f)] float chainTransfer = 0.65f;
        [SerializeField] float chainDamage = 4f;

        public bool IsLaunched { get; private set; }
        public bool WallSlammed { get; private set; }
        public float Weight => weight;

        /// <summary>착지 (인자: 벽꽝을 당했는지)</summary>
        public event Action<bool> Landed;

        Rigidbody body;
        NavMeshAgent agent;
        Health health;
        CapsuleCollider capsule;
        Vector3 lastVelocity;
        float airTime;
        int groundMask;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            capsule = GetComponent<CapsuleCollider>();
            groundMask = ~(1 << gameObject.layer);
            SetKinematic(true);
        }

        void SetKinematic(bool on)
        {
            if (on)
            {
                // kinematic은 ContinuousDynamic을 지원하지 않으므로 모드를 먼저 바꾼다
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None; // 에이전트가 Transform을 직접 움직이므로 보간 끔
            }
            else
            {
                body.isKinematic = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 빠르게 날아가도 벽을 뚫지 않게
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        public bool ShouldLaunch(DamageInfo info) => isActiveAndEnabled && info.knockback >= launchThreshold;

        /// <summary>넉백 값으로 날린다 (날아가는 중이면 속도를 새로 덮어쓴다 = 공중 추가타)</summary>
        public void Launch(Vector3 direction, float knockback)
        {
            float speed = knockback * speedPerKnockback / Mathf.Max(0.1f, weight);
            LaunchWithVelocity(direction, speed);
        }

        public void LaunchWithVelocity(Vector3 direction, float speed)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
            direction.Normalize();

            if (!IsLaunched)
            {
                // 소유권 이전: 에이전트 → 물리
                if (agent != null && agent.enabled) agent.enabled = false;
                SetKinematic(false);
            }
            body.linearVelocity = direction * speed + Vector3.up * (speed * lift + 1.2f);
            transform.rotation = Quaternion.LookRotation(-direction); // 때린 쪽을 보며 뒤로 날아감
            IsLaunched = true;
            WallSlammed = false;
            airTime = 0f;
        }

        void FixedUpdate()
        {
            if (!IsLaunched) return;
            airTime += Time.fixedDeltaTime;
            lastVelocity = body.linearVelocity;

            Vector3 flat = lastVelocity;
            flat.y = 0f;
            bool grounded = IsGrounded();
            if ((grounded && airTime > 0.2f && flat.magnitude < settleSpeed && Mathf.Abs(lastVelocity.y) < 0.6f)
                || airTime > maxAirTime)
                Settle();
            else if (grounded && airTime > 0.2f)
                body.linearVelocity = new Vector3(flat.x * 0.9f, lastVelocity.y, flat.z * 0.9f); // 바닥 미끄러짐 감속
        }

        bool IsGrounded()
        {
            float r = capsule != null ? capsule.radius * 0.9f : 0.2f;
            Vector3 origin = transform.position + Vector3.up * (r + 0.05f);
            return Physics.SphereCast(origin, r, Vector3.down, out _, 0.12f, groundMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>소유권 이전: 물리 → 에이전트. NavMesh 위로 스냅한 뒤 Warp</summary>
        void Settle()
        {
            body.linearVelocity = Vector3.zero;
            SetKinematic(true);
            IsLaunched = false;

            Vector3 p = transform.position;
            var filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(p, out var hit, navSnapRadius, filter) ||
                NavMesh.SamplePosition(p, out hit, navSnapRadius * 4f, filter))
                p = hit.position;
            transform.position = p;

            if (agent != null && (health == null || !health.IsDead))
            {
                agent.enabled = true;
                agent.Warp(p);
            }
            Landed?.Invoke(WallSlammed);
        }

        void OnCollisionEnter(Collision c)
        {
            if (!IsLaunched || c.contactCount == 0) return;
            var contact = c.GetContact(0);
            Vector3 n = contact.normal;

            // 볼링: 날아가던 양이 서 있는 양과 부딪힘
            var other = c.collider.GetComponentInParent<PhysicsKnockback>();
            if (other != null)
            {
                if (other == this || other.IsLaunched || other.health == null || other.health.IsDead) return;
                Vector3 v = lastVelocity;
                v.y = 0f;
                if (v.magnitude < chainMinSpeed) return;

                Vector3 dir = other.transform.position - transform.position;
                dir.y = 0f;
                dir = (dir.normalized + v.normalized).normalized; // 맞은 방향과 날아온 방향의 중간으로
                other.LaunchWithVelocity(dir, v.magnitude * chainTransfer / Mathf.Max(0.1f, other.weight));
                other.health.TakeDamage(new DamageInfo
                {
                    amount = chainDamage,
                    point = other.health.AimPoint,
                    direction = dir,
                    knockback = 0f, // 이미 날렸으므로 MonsterBrain이 다시 날리지 않게
                    source = gameObject,
                });
                body.linearVelocity = lastVelocity * (1f - chainTransfer); // 운동량을 넘긴 만큼 감속
                Sfx.Play("thud", contact.point, 0.7f, 1.3f);
                DustFx.Burst(contact.point, 10, new Color(1f, 0.98f, 0.94f), 2.6f, 0.14f, 0.5f);
                HitStop.Do(0.03f);
                return;
            }

            // 벽꽝: 옆면(법선이 수평)에 세게 박음. 정적 충돌체만 (움직이는 문짝 등은 제외)
            // 움직이지 않는 것(정적 충돌체, kinematic 바리케이드·셔터)에만 벽꽝
            if (WallSlammed || (c.rigidbody != null && !c.rigidbody.isKinematic) || Mathf.Abs(n.y) > 0.5f) return;
            float impact = Mathf.Abs(Vector3.Dot(lastVelocity, n));
            if (impact < wallSlamSpeed) return;

            WallSlammed = true;
            Vector3 bounce = Vector3.Reflect(lastVelocity, n) * wallBounce;
            bounce.y = Mathf.Max(bounce.y, 0.5f);
            body.linearVelocity = bounce;

            Sfx.Play("thud", contact.point, 1f, 0.7f);
            DustFx.Burst(contact.point, 16, DustFx.DustColor, 3f, 0.16f, 0.6f);
            HitStop.Do(0.06f);
            ThirdPersonCamera.Active?.Shake(0.1f, 0.15f);
            health?.TakeDamage(new DamageInfo
            {
                amount = wallSlamDamage,
                point = contact.point,
                direction = n,
                knockback = 0f,
                heavy = true,
                source = gameObject,
            });
        }

        /// <summary>강제 정지 (게임 종료 등)</summary>
        public void Stop()
        {
            if (IsLaunched) Settle();
        }
    }
}
