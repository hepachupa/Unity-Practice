using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.AI
{
    /// <summary>
    /// 공격받는 쪽(플레이어·친구)에 붙는 "교전 관리자".
    ///
    /// 1) 공격 토큰: 동시에 공격할 수 있는 몬스터 수를 제한한다.
    ///    토큰을 가진 몬스터만 공격 거리까지 들어와 공격하고, 공격이 끝나면 반납한다.
    ///    (갓 오브 워·아캄 시리즈 방식 — 다수에게 둘러싸여도 한 번에 한두 마리만 덤벼서 읽을 수 있는 전투가 된다)
    /// 2) 포위 슬롯: 토큰이 없는 몬스터는 대상 주위 링 위의 빈 자리를 배정받아 천천히 맴돌며 차례를 기다린다.
    ///    링은 천천히 회전하고, 슬롯 좌표는 NavMesh.Raycast로 벽 안쪽으로 당겨서 좁은 복도에서도 벽 속에 박히지 않는다.
    ///
    /// 토큰/슬롯 상태는 대상 쪽이 소유한다 → 몬스터끼리 서로를 몰라도 협동하는 것처럼 보인다.
    /// </summary>
    public class EngagementTarget : MonoBehaviour
    {
        [Tooltip("동시에 공격할 수 있는 몬스터 토큰 수 (큰 양은 토큰 2개를 쓴다)")]
        [SerializeField] int attackTokens = 2;
        [Tooltip("대기 링 반지름 (공격 거리보다 조금 밖)")]
        [SerializeField] float ringRadius = 2.6f;
        [SerializeField] int slotCount = 8;
        [Tooltip("링 회전 속도 (도/초) — 대기 중인 몬스터가 맴도는 느낌")]
        [SerializeField] float ringSpin = 14f;

        struct Holder
        {
            public MonsterBrain brain;
            public int cost;
        }

        readonly List<Holder> holders = new();
        MonsterBrain[] slots;

        // 기아 방지: 토큰을 요청했다가 못 받은 몬스터가 언제부터 기다렸는지.
        // 오래(fairnessDelay 이상) 기다린 몬스터가 있으면 그 몬스터가 먼저 받는다
        // (안 그러면 토큰 1개짜리 작은 양들이 계속 채가서 토큰 2개가 필요한 큰 양은 영영 공격을 못 함)
        class Waiter
        {
            public MonsterBrain brain;
            public float since;
            public float lastAsk;
        }
        readonly List<Waiter> waiters = new();
        const float FairnessDelay = 1f;

        public float RingRadius => ringRadius;
        public int Capacity => attackTokens;

        void Awake() => slots = new MonsterBrain[Mathf.Max(1, slotCount)];

        void OnDisable()
        {
            holders.Clear();
            waiters.Clear();
            if (slots != null) System.Array.Clear(slots, 0, slots.Length);
        }

        static bool Alive(MonsterBrain m) => m != null && m.isActiveAndEnabled && !m.IsDead;

        void Clean()
        {
            holders.RemoveAll(h => !Alive(h.brain));
            // 0.8초 동안 다시 요청하지 않은 대기자는 포기한 것으로 본다 (대상 변경·경직 등)
            waiters.RemoveAll(w => !Alive(w.brain) || Time.time - w.lastAsk > 0.8f);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !Alive(slots[i])) slots[i] = null;
        }

        public int UsedTokens
        {
            get
            {
                int used = 0;
                foreach (var h in holders) used += h.cost;
                return used;
            }
        }

        public bool Holds(MonsterBrain m)
        {
            foreach (var h in holders) if (h.brain == m) return true;
            return false;
        }

        /// <summary>토큰을 요청한다. 이미 가지고 있으면 true</summary>
        public bool TryAcquire(MonsterBrain m, int cost)
        {
            Clean();
            if (Holds(m)) return true;

            Waiter mine = null, oldest = null;
            foreach (var w in waiters)
            {
                if (w.brain == m) mine = w;
                if (oldest == null || w.since < oldest.since) oldest = w;
            }
            if (mine == null) { mine = new Waiter { brain = m, since = Time.time }; waiters.Add(mine); }
            mine.lastAsk = Time.time;
            if (oldest == null) oldest = mine;

            // 오래 기다린 다른 몬스터가 있으면 양보
            if (oldest != mine && Time.time - oldest.since >= FairnessDelay) return false;

            int free = attackTokens - UsedTokens;
            // 토큰 용량보다 비싼 몬스터(친구 1 vs 큰 양 2)도 아무도 공격 중이 아니면 혼자 공격할 수 있게
            bool fits = cost <= free || (holders.Count == 0 && cost > attackTokens);
            if (!fits) return false;
            holders.Add(new Holder { brain = m, cost = cost });
            waiters.Remove(mine);
            ReleaseSlot(m);
            return true;
        }

        /// <summary>토큰과 슬롯을 모두 반납</summary>
        public void Release(MonsterBrain m)
        {
            holders.RemoveAll(h => h.brain == m);
            waiters.RemoveAll(w => w.brain == m);
            ReleaseSlot(m);
        }

        public void ReleaseSlot(MonsterBrain m)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++) if (slots[i] == m) slots[i] = null;
        }

        float SlotAngle(int i) => i * 360f / slots.Length + Time.time * ringSpin;

        /// <summary>
        /// 대기 자리 좌표. 처음 요청하면 몬스터의 현재 방향에서 가장 가까운 빈 슬롯을 배정한다
        /// (몬스터가 대상을 가로질러 반대편으로 돌아가지 않도록).
        /// </summary>
        public Vector3 GetSlotPosition(MonsterBrain m, int agentTypeID)
        {
            Clean();
            int idx = System.Array.IndexOf(slots, m);
            Vector3 center = transform.position;
            Vector3 from = m.transform.position - center;
            from.y = 0f;
            float myAngle = from.sqrMagnitude > 0.001f ? Quaternion.LookRotation(from).eulerAngles.y : 0f;

            if (idx < 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null) continue;
                    float d = Mathf.Abs(Mathf.DeltaAngle(myAngle, SlotAngle(i)));
                    if (d < best) { best = d; idx = i; }
                }
                if (idx >= 0) slots[idx] = m;
            }

            // 슬롯이 다 찼으면 링 바깥쪽 자기 방향에서 대기
            float angle = idx >= 0 ? SlotAngle(idx) : myAngle;
            float radius = idx >= 0 ? ringRadius : ringRadius + 1.2f;
            Vector3 want = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;

            var filter = new NavMeshQueryFilter { agentTypeID = agentTypeID, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(center, out var origin, 1.5f, filter))
            {
                // 대상에서 슬롯까지 직선이 벽에 막히면 벽 앞에서 멈춘 지점을 쓴다 (좁은 복도 대응)
                if (NavMesh.Raycast(origin.position, want, out var blocked, filter)) return blocked.position;
                if (NavMesh.SamplePosition(want, out var onMesh, 1f, filter)) return onMesh.position;
            }
            return want;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || slots == null) return;
            Vector3 c = transform.position + Vector3.up * 0.05f;
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Gizmos.DrawLine(c + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * ringRadius,
                                c + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * ringRadius);
            }
            for (int i = 0; i < slots.Length; i++)
            {
                Vector3 p = c + Quaternion.Euler(0f, SlotAngle(i), 0f) * Vector3.forward * ringRadius;
                Gizmos.color = slots[i] != null ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
                Gizmos.DrawWireSphere(p, 0.15f);
            }
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
            foreach (var h in holders)
                if (h.brain != null) Gizmos.DrawLine(c + Vector3.up * 0.5f, h.brain.transform.position + Vector3.up * 0.5f);
        }
    }
}
