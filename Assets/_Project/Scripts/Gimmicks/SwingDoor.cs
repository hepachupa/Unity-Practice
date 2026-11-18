using System.Collections.Generic;
using LNStudy.CameraSystem;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 여닫이 문 (문짝 1~2장). 문짝 = 충돌체가 붙은 자식들. 각 문짝의 바깥쪽 세로 모서리를 경첩으로 돌린다.
    /// Toggle(누가 여는지)을 부르면 그 사람 반대쪽으로 열린다 (복도에서 열면 교실 쪽으로).
    /// 닫혀 있을 때의 위치·회전을 기억해 정확히 돌아온다. 정적 배칭이 꺼져 있어야 움직인다(빌더가 처리).
    /// autoOpenForAgents: 길을 가던 친구(FriendNPC)가 문 앞에 오면 스스로 연다. 양은 못 연다(DoorBlocked로 막힘). 플레이어는 F키.
    /// 체력(Health)이 붙어 있으면 양이 닫힌 문을 두드려 부술 수 있다 → Break():
    /// 문짝을 HingeJoint로 경첩에 매단 물리 강체로 바꿔 세게 밀친다. 경첩이 버티며 확 열리다가
    /// 한계 각도에 부딪히는 순간의 힘이 breakForce를 넘으면 경첩이 뜯겨 문짝이 날아간다.
    /// </summary>
    public class SwingDoor : MonoBehaviour
    {
        [SerializeField] float openAngle = 95f;
        [SerializeField] float openTime = 0.45f;
        [SerializeField] bool autoOpenForAgents = true;
        [SerializeField] float agentReach = 1.3f;

        static readonly Collider[] scan = new Collider[24];
        float nextScan;
        Vector3 doorCenter;

        class Leaf
        {
            public Transform t;
            public Vector3 closedPos;
            public Quaternion closedRot;
            public Vector3 hinge;
            public float sign = 1f;
            public float angle, target;
        }

        [Header("Break (Health가 있을 때)")]
        [SerializeField] float leafMass = 12f;
        [Tooltip("문짝 끝을 미는 속도 (m/s)")]
        [SerializeField] float breakPush = 6f;
        [Tooltip("경첩이 버티는 힘. 낮을수록 잘 뜯겨 나간다")]
        [SerializeField] float hingeBreakForce = 900f;
        [SerializeField] float debrisLifetime = 7f;
        [Tooltip("문 중심에서 이 거리 안에 바리케이드가 있으면 열고 닫을 수 없다")]
        [SerializeField] float jamDistance = 0.85f;

        readonly List<Leaf> leaves = new();
        public bool IsOpen { get; private set; }
        public bool Broken { get; private set; }

        LNStudy.Combat.Health health;
        Vector3 lastHitDir;

        void OnEnable()
        {
            if (health != null) { health.Died += OnBroken; health.Damaged += OnDamaged; }
        }

        void OnDisable()
        {
            if (health != null) { health.Died -= OnBroken; health.Damaged -= OnDamaged; }
        }

        void OnDamaged(LNStudy.Combat.DamageInfo info)
        {
            lastHitDir = info.direction;
            // 두드릴 때마다 문짝이 살짝 들썩
            foreach (var l in leaves) l.angle += Random.Range(-2.5f, 2.5f);
            Sfx.Play("thud", doorCenter, 0.55f, Random.Range(0.9f, 1.1f));
            ThirdPersonCamera.Active?.Shake(0.015f, 0.08f);
        }

        void OnBroken() => Break(lastHitDir);

        /// <summary>문을 부순다: 문짝 → 경첩(HingeJoint)에 매달린 강체 → 밀침 → 한계에서 경첩이 뜯겨 날아감</summary>
        public void Break(Vector3 hitDir)
        {
            if (Broken) return;
            Broken = true;
            IsOpen = true;
            hitDir.y = 0f;
            if (hitDir.sqrMagnitude < 0.001f) hitDir = transform.forward;
            hitDir.Normalize();

            foreach (var l in leaves)
            {
                var go = l.t.gameObject;
                l.t.SetParent(null, true); // DoorBlocked가 더 이상 문짝으로 보지 않게
                var col = go.GetComponent<Collider>();
                if (col is MeshCollider mc) mc.convex = true; // 동적 강체는 convex여야 함

                // 문틀과 처음부터 겹쳐 있으면 튕겨 나가므로, 지금 겹친 정적 충돌체와는 충돌을 끈다
                if (col != null)
                {
                    var b = col.bounds;
                    foreach (var other in Physics.OverlapBox(b.center, b.extents * 1.02f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        if (other != col && other.attachedRigidbody == null) Physics.IgnoreCollision(col, other, true);
                }

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = leafMass;
                rb.linearDamping = 0.6f;  // 뜯겨 나간 뒤 교실 끝까지 날아가지 않게
                rb.angularDamping = 1.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var r = go.GetComponent<Renderer>();
                float h = r != null ? r.bounds.size.y : 2f;
                var hinge = go.AddComponent<HingeJoint>();
                hinge.autoConfigureConnectedAnchor = true; // connectedBody 없음 = 월드에 고정
                hinge.anchor = l.t.InverseTransformPoint(l.hinge + Vector3.up * (h * 0.5f));
                hinge.axis = l.t.InverseTransformDirection(Vector3.up);
                hinge.useLimits = true;
                hinge.limits = new JointLimits { min = -100f, max = 100f, bounciness = 0.3f };
                hinge.breakForce = hingeBreakForce;
                hinge.breakTorque = hingeBreakForce;

                // 경첩 반대쪽(문짝 끝)을 맞은 방향으로 확 민다
                Vector3 c = r != null ? r.bounds.center : l.t.position;
                Vector3 edge = c + (c - new Vector3(l.hinge.x, c.y, l.hinge.z));
                rb.AddForceAtPosition(hitDir * breakPush * leafMass, edge, ForceMode.Impulse);
                Destroy(go, debrisLifetime);
            }
            leaves.Clear();

            Sfx.Play("thud", doorCenter, 1f, 0.55f);
            Sfx.Play("creak", doorCenter, 0.9f, 0.7f);
            DustFx.Burst(doorCenter + Vector3.up * 0.6f, 22, DustFx.DustColor, 3.5f, 0.18f, 0.7f);
            ThirdPersonCamera.Active?.Shake(0.12f, 0.3f);
            LNStudy.Combat.HitStop.Do(0.06f);
        }

        void Awake()
        {
            health = GetComponent<LNStudy.Combat.Health>();
            var cols = GetComponentsInChildren<Collider>(true);
            Bounds all = default;
            bool first = true;
            foreach (var c in cols)
            {
                if (c.transform == transform) continue;
                var r = c.GetComponent<Renderer>();
                Bounds b = r != null ? r.bounds : c.bounds;
                if (first) { all = b; first = false; } else all.Encapsulate(b);
            }
            foreach (var c in cols)
            {
                if (c.transform == transform) continue;
                var r = c.GetComponent<Renderer>();
                Bounds b = r != null ? r.bounds : c.bounds;
                // 폭 방향 = 가로로 더 긴 축. 경첩 = 문 전체 중심에서 먼 쪽 모서리 (쌍여닫이면 바깥쪽)
                bool alongX = b.size.x >= b.size.z;
                Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                float half = (alongX ? b.size.x : b.size.z) * 0.5f;
                float side = Mathf.Sign(Vector3.Dot(b.center - all.center, axis));
                if (side == 0f) side = -1f;
                Vector3 hinge = b.center + axis * (half * side);
                hinge.y = b.min.y;
                leaves.Add(new Leaf { t = c.transform, closedPos = c.transform.position, closedRot = c.transform.rotation, hinge = hinge });
            }
            doorCenter = first ? transform.position : new Vector3(all.center.x, all.min.y + 0.6f, all.center.z);
        }

        /// <summary>
        /// 문짝 중 가장 가까운 점까지의 수평 거리.
        /// (Collider.ClosestPoint는 non-convex 메시 충돌체에서 동작하지 않고 입력 점을 그대로 돌려줘
        ///  모든 문이 거리 0으로 나왔다 → 엉뚱한 문이 열리고 닫히던 원인. 렌더러 바운드로 계산한다)
        /// </summary>
        public float DistanceTo(Vector3 p)
        {
            float best = float.MaxValue; // 부서진 문은 leaves가 비어 있어 항상 멀다 (상호작용 대상에서 빠짐)
            foreach (var l in leaves)
            {
                var r = l.t.GetComponent<Renderer>();
                Vector3 q = r != null ? r.bounds.ClosestPoint(new Vector3(p.x, r.bounds.center.y, p.z)) : l.t.position;
                q.y = p.y;
                best = Mathf.Min(best, Vector3.Distance(p, q));
            }
            return best;
        }

        /// <summary>문 앞(어느 쪽이든)에 멀쩡한 바리케이드 사물함이 있으면 열고 닫을 수 없다</summary>
        public bool Jammed
        {
            get
            {
                if (Broken) return false;
                foreach (var b in Barricade.All)
                    if (b != null && b.Intact && b.FlatDistance(doorCenter) < jamDistance) return true;
                return false;
            }
        }

        public void Toggle(Vector3 from)
        {
            if (Broken) return;
            if (Jammed)
            {
                Sfx.Play("clunk", doorCenter, 0.6f, 1.3f); // 덜컥 — 사물함에 걸림
                foreach (var l in leaves) l.angle += 3f;  // 살짝 들썩했다 돌아감
                return;
            }
            IsOpen = !IsOpen;
            foreach (var l in leaves)
            {
                if (IsOpen)
                {
                    // 여는 사람에게서 멀어지는 방향을 고른다
                    Vector3 center = l.closedPos;
                    Vector3 a = Rotate(center, l.hinge, openAngle), b = Rotate(center, l.hinge, -openAngle);
                    l.sign = Vector3.Distance(a, from) >= Vector3.Distance(b, from) ? 1f : -1f;
                }
                l.target = IsOpen ? openAngle * l.sign : 0f;
            }
            Vector3 pos = leaves.Count > 0 ? leaves[0].hinge : transform.position;
            Sfx.Play(IsOpen ? "creak" : "thud", pos, IsOpen ? 0.7f : 0.6f, IsOpen ? 1.1f : 1.5f);
            if (!IsOpen) closing = true;
        }

        bool closing;

        static Vector3 Rotate(Vector3 p, Vector3 pivot, float deg) => pivot + Quaternion.Euler(0f, deg, 0f) * (p - pivot);

        void Update()
        {
            if (autoOpenForAgents && !IsOpen && !Broken && Time.time >= nextScan && !Jammed)
            {
                nextScan = Time.time + 0.2f;
                int n = Physics.OverlapSphereNonAlloc(doorCenter, agentReach, scan, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var ag = scan[i].GetComponentInParent<NavMeshAgent>();
                    if (ag == null || !ag.enabled || !ag.hasPath || ag.velocity.sqrMagnitude < 0.01f) continue;
                    if (ag.GetComponent<LNStudy.AI.FriendNPC>() == null) continue; // 문은 친구만 연다 (양은 닫힌 문에 막힘)
                    Toggle(ag.transform.position);
                    break;
                }
            }

            bool moving = false;
            foreach (var l in leaves)
            {
                if (Mathf.Approximately(l.angle, l.target)) continue;
                moving = true;
                float speed = openAngle / Mathf.Max(0.05f, openTime);
                l.angle = Mathf.MoveTowards(l.angle, l.target, speed * Time.deltaTime);
                // 살짝 감속 (끝에서 부드럽게)
                float shown = l.angle;
                l.t.SetPositionAndRotation(Rotate(l.closedPos, l.hinge, shown), Quaternion.Euler(0f, shown, 0f) * l.closedRot);
            }
            if (!moving && closing)
            {
                closing = false;
                SideFollowCamera.Shake(0.02f, 0.1f);
                ThirdPersonCamera.Active?.Shake(0.03f, 0.1f);
            }
        }
    }
}
