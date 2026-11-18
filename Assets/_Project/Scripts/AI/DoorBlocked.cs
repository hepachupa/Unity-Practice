using LNStudy.Gimmicks;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.AI
{
    /// <summary>
    /// 문을 못 여는 NavMeshAgent(양)용. NavMesh는 문짝 없이 구워져 있어 경로는 문을 지나가지만,
    /// NavMeshAgent는 물리 충돌을 무시하므로 그대로 두면 닫힌 문을 통과해 버린다.
    /// 매 프레임 문짝 충돌체와 겹치면 밀어내고 에이전트 위치를 맞춰 → 닫힌 문 앞에 막혀서 모여 있게 된다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class DoorBlocked : MonoBehaviour
    {
        [SerializeField] float searchRadius = 2f;

        NavMeshAgent agent;
        CapsuleCollider body;
        SwingDoor[] doors;
        readonly Collider[] buffer = new Collider[16];

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            body = GetComponent<CapsuleCollider>();
        }

        void Start() => doors = FindObjectsByType<SwingDoor>(FindObjectsSortMode.None);

        void LateUpdate()
        {
            if (body == null || doors == null || !agent.enabled) return;

            Vector3 pos = transform.position;
            bool near = false;
            foreach (var d in doors)
                if (d != null && d.DistanceTo(pos) < searchRadius) { near = true; break; }
            if (!near) return;

            Vector3 center = transform.TransformPoint(body.center);
            float half = Mathf.Max(0f, body.height * 0.5f - body.radius);
            Vector3 a = center + Vector3.up * half, b = center - Vector3.up * half;
            int n = Physics.OverlapCapsuleNonAlloc(a, b, body.radius, buffer, ~0, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = buffer[i];
                if (c == body || c.GetComponentInParent<SwingDoor>() == null) continue;
                if (Physics.ComputePenetration(body, transform.position, transform.rotation,
                        c, c.transform.position, c.transform.rotation, out var dir, out float dist))
                {
                    dir.y = 0f;
                    push += dir.normalized * dist;
                }
            }
            if (push.sqrMagnitude < 1e-6f) return;
            transform.position += push;
            agent.nextPosition = transform.position;
            agent.velocity = Vector3.ProjectOnPlane(agent.velocity, push.normalized);
        }
    }
}
