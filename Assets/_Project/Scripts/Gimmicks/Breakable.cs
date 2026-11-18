using System.Collections.Generic;
using LNStudy.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 몬스터가 부수고 지나갈 수 있는 장애물 (닫힌 문, 사물함 바리케이드).
    /// 몬스터는 길이 막혀 멈추면 근처의 "막고 있는" Breakable을 찾아 대상으로 삼고 두드린다 (MonsterBrain.breakObstacles).
    /// 체력이 0이 되면 Broken 이벤트 → 주인 컴포넌트(SwingDoor / Barricade)가 물리 연출로 부서진다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Breakable : MonoBehaviour
    {
        static readonly List<Breakable> all = new();

        public Health Health { get; private set; }
        public bool Broken => Health != null && Health.IsDead;

        SwingDoor door;
        FireShutter shutter;
        Renderer[] renderers;

        /// <summary>지금 길을 막고 있는지 (문은 닫혀 있을 때만)</summary>
        public bool Blocking => isActiveAndEnabled && !Broken
                                && (door == null || (!door.IsOpen && !door.Broken))
                                && (shutter == null || shutter.Closed);

        void Awake()
        {
            Health = GetComponent<Health>();
            door = GetComponent<SwingDoor>();
            shutter = GetComponent<FireShutter>();
            renderers = GetComponentsInChildren<Renderer>();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>p에서 가장 가까운 표면 점 (렌더러 바운드 기준 — non-convex 충돌체에서도 동작)</summary>
        public Vector3 ClosestPoint(Vector3 p)
        {
            Vector3 best = transform.position;
            float bestSqr = float.MaxValue;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (r.GetComponentInParent<Canvas>() != null) continue; // 체력바 제외
                Vector3 q = r.bounds.ClosestPoint(new Vector3(p.x, r.bounds.center.y, p.z));
                q.y = p.y;
                float d = (q - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = q; }
            }
            return best;
        }

        static NavMeshPath scratchPath; // static 초기화식에서 만들면 Unity가 막는다 → 처음 쓸 때 생성

        /// <summary>
        /// 경로가 끊겼을 때 "어느 장애물을 부숴야 대상에게 가까워지는가"를 고른다.
        /// 장애물마다 앞뒤·좌우로 0.7m 떨어진 '설 자리' 후보를 만들고,
        ///   - NavMesh 위에 있고, 지금 위치에서 실제로 갈 수 있으며(PathComplete),
        ///   - 설 자리와 장애물 사이에 벽이 없는(Linecast) 것만 남긴 뒤
        /// 비용 = 거기까지 경로 길이 + 거기서 대상까지 직선거리 가 가장 작은 것을 고른다.
        /// (단순히 "대상에게 가장 가까운 장애물"을 고르면 벽 너머 엉뚱한 사물함을 두드리고,
        ///  "끊긴 경로의 끝점"은 대상과 기하학적으로만 가까운 방 구석이라 벽 앞에서 멈춘다)
        /// </summary>
        public static Breakable FindBestToBreak(Vector3 from, Vector3 target, int agentTypeID, out Vector3 stand)
        {
            stand = from;
            Breakable best = null;
            float bestCost = float.MaxValue;
            var filter = new NavMeshQueryFilter { agentTypeID = agentTypeID, areaMask = NavMesh.AllAreas };
            scratchPath ??= new NavMeshPath();
            foreach (var b in all)
            {
                if (!b.Blocking) continue;
                Bounds bb = b.WorldBounds();
                Vector3 c = new Vector3(bb.center.x, from.y, bb.center.z);
                Vector3[] offsets =
                {
                    Vector3.right * (bb.extents.x + 0.7f), Vector3.left * (bb.extents.x + 0.7f),
                    Vector3.forward * (bb.extents.z + 0.7f), Vector3.back * (bb.extents.z + 0.7f),
                };
                foreach (var o in offsets)
                {
                    if (!NavMesh.SamplePosition(c + o, out var hit, 0.5f, filter)) continue;
                    // 벽 너머 자리 제외: 설 자리 → 장애물 중심 사이에 다른 충돌체가 있으면 안 됨
                    Vector3 eye = hit.position + Vector3.up * 1f, mid = new Vector3(bb.center.x, eye.y, bb.center.z);
                    if (Physics.Linecast(eye, mid, out var block, 1, QueryTriggerInteraction.Ignore)
                        && block.collider.GetComponentInParent<Breakable>() != b) continue;
                    if (!NavMesh.CalculatePath(from, hit.position, filter, scratchPath) || scratchPath.status != NavMeshPathStatus.PathComplete) continue;
                    float len = 0f;
                    var corners = scratchPath.corners;
                    for (int i = 1; i < corners.Length; i++) len += Vector3.Distance(corners[i - 1], corners[i]);
                    float cost = len + Vector3.Distance(hit.position, target);
                    if (cost < bestCost) { bestCost = cost; best = b; stand = hit.position; }
                }
            }
            return best;
        }

        public Bounds WorldBounds()
        {
            Bounds bb = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r.GetComponentInParent<Canvas>() != null) continue;
                if (first) { bb = r.bounds; first = false; } else bb.Encapsulate(r.bounds);
            }
            return bb;
        }

        /// <summary>pos에서 radius 안에 있는, 길을 막고 있는 가장 가까운 장애물</summary>
        public static Breakable FindBlocking(Vector3 pos, float radius)
        {
            Breakable best = null;
            float bestD = radius;
            foreach (var b in all)
            {
                if (!b.Blocking) continue;
                Vector3 q = b.ClosestPoint(pos);
                q.y = pos.y;
                float d = Vector3.Distance(q, pos);
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }
    }
}
