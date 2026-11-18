using System.Collections;
using System.Collections.Generic;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.AI
{
    /// <summary>
    /// 몬스터 공용 행동 (상태 머신).
    /// Idle → Patrol(집 주변 무작위 지점) → 시야(거리·시야각·가림)나 소리 거리 안에 대상이 들어오면 Chase
    /// → 공격 거리 안에서 Attack(예비 동작 → 판정 → 후딜) → 집에서 너무 멀어지거나 놓치면 Return.
    /// 맞으면 잠깐 경직(Stagger)되고 밀려나며, 맞은 순간 대상을 인지한다.
    /// 체력이 0이 되면 쓰러졌다가 가라앉고, respawnDelay 뒤 집에서 부활한다 (테스트 반복용).
    ///
    /// 선택 기능 (학교 방어의 양에서 켬, 다른 씬은 기존 동작 그대로):
    /// - 공격 토큰 + 포위 (useAttackTokens): 대상의 EngagementTarget에서 토큰을 받아야 공격.
    ///   토큰이 없으면 Circle 상태로 대상 주위 링 슬롯에서 맴돌며 차례를 기다린다.
    /// - 어그로 (useAggro): 거리 + 대상 우선순위(친구 > 플레이어) + 받은 피해 위협도로 대상을 고른다.
    ///   위협도는 시간이 지나면 줄고, 히스테리시스(switchMargin)로 대상을 너무 자주 바꾸지 않는다.
    ///   → 플레이어가 양을 때리면 어그로가 플레이어로 넘어와 친구를 지킬 수 있다.
    /// - 공격 예고 (TelegraphIndicator): 예비 동작 동안 발밑 부채꼴이 차오르고, 다 차면 판정.
    /// - 물리 넉백 (PhysicsKnockback): 강한 공격에 Launched 상태로 물리로 날아갔다가 착지하면 복귀.
    /// - 장애물 부수기 (breakObstacles): 닫힌 문·바리케이드에 길이 막혀 제자리걸음이면
    ///   가까운 Breakable을 임시 대상으로 삼아 두드린다. 부서지거나 열리면 원래 대상 선택으로 돌아간다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public class MonsterBrain : MonoBehaviour
    {
        public enum State { Idle, Patrol, Chase, Circle, Attack, Stagger, Launched, Return, Dead }

        static readonly List<MonsterBrain> active = new();
        public static IReadOnlyList<MonsterBrain> Active => active;

        [Header("Target")]
        [SerializeField] Transform target;
        [Tooltip("시야 판정에서 가림막으로 볼 레이어")]
        [SerializeField] LayerMask obstacleMask = 1;
        [Tooltip("Player 태그가 여럿일 때(동료 캐릭터) 가장 가까운 쪽을 노린다")]
        [SerializeField] bool retargetNearestPlayer = true;

        [Header("Perception")]
        [SerializeField] float viewDistance = 10f;
        [SerializeField, Range(0f, 360f)] float viewAngle = 120f;
        [Tooltip("이 거리 안이면 시야각과 상관없이 감지 (발소리)")]
        [SerializeField] float hearDistance = 3f;
        [SerializeField] float eyeHeight = 1.7f;
        [Tooltip("놓친 뒤 마지막 위치를 확인하러 가는 시간")]
        [SerializeField] float loseTargetTime = 3f;
        [Tooltip("벽 너머에서도 대상 위치를 안다 (쳐들어오는 웨이브 몬스터용). 공격은 여전히 보여야 한다")]
        [SerializeField] bool trackWithoutSight;

        [Header("Movement")]
        [SerializeField] float patrolSpeed = 1.4f;
        [SerializeField] float chaseSpeed = 3.4f;
        [SerializeField] float patrolRadius = 6f;
        [SerializeField] Vector2 idleTime = new Vector2(1.5f, 4f);
        [SerializeField] float leashDistance = 18f;
        [SerializeField] float turnSpeed = 360f;

        [Header("Attack")]
        [SerializeField] float attackRange = 1.9f;
        [SerializeField] float attackDamage = 12f;
        [Tooltip("예비 동작 (플레이어가 보고 피할 시간)")]
        [SerializeField] float attackWindup = 0.55f;
        [SerializeField] float attackRecovery = 0.7f;
        [SerializeField] float attackCooldown = 1.6f;
        [Tooltip("판정: 정면 기준 이 각도 / 거리 안이면 명중")]
        [SerializeField] float attackHitAngle = 70f;
        [SerializeField] float attackHitRange = 2.3f;
        [SerializeField] float attackKnockback = 3f;

        [Header("Hit / Death")]
        [SerializeField] float staggerTime = 0.35f;
        [Tooltip("넉백 1당 밀려나는 거리 (m)")]
        [SerializeField] float knockbackScale = 0.35f;
        [SerializeField] float sinkDelay = 2.2f;
        [SerializeField] float respawnDelay = 6f;
        [Tooltip("끄면 죽은 뒤 부활하지 않는다 (스테이지 모드)")]
        [SerializeField] bool respawnAfterDeath = true;

        [Header("Animation")]
        [SerializeField] Animator animator;
        [SerializeField] float speedDampTime = 0.1f;

        [Header("Coordination (공격 토큰 · 포위)")]
        [SerializeField] bool useAttackTokens;
        [Tooltip("공격 한 번에 쓰는 토큰 (큰 몬스터는 2)")]
        [SerializeField] int tokenCost = 1;
        [Tooltip("링 반지름 + 이 거리 안에 들어오면 토큰이 없을 때 포위 대기로 전환")]
        [SerializeField] float engageMargin = 1.5f;
        [SerializeField] float circleSpeed = 1.4f;
        [SerializeField] float tokenRetry = 0.3f;
        [Tooltip("토큰을 받고도 이 시간 안에 공격하지 못하면(길이 막힘 등) 반납")]
        [SerializeField] float tokenTimeout = 3f;

        [Header("Aggro")]
        [SerializeField] bool useAggro;
        [Tooltip("친구(FriendNPC)를 플레이어보다 얼마나 더 노리는지")]
        [SerializeField] float friendPriority = 1.4f;
        [Tooltip("받은 피해 1당 쌓이는 위협도")]
        [SerializeField] float damageThreat = 0.4f;
        [Tooltip("초당 줄어드는 위협도")]
        [SerializeField] float threatDecay = 1.2f;
        [Tooltip("새 대상 점수가 현재 대상의 몇 배를 넘어야 바꾸는지 (히스테리시스)")]
        [SerializeField] float switchMargin = 1.25f;

        [Header("Feedback")]
        [SerializeField] TelegraphIndicator telegraph;
        [Tooltip("넉백에서 일어나는 시간 (벽꽝이면 wallStun)")]
        [SerializeField] float getUpTime = 0.35f;
        [SerializeField] float wallStun = 0.9f;

        [Header("Obstacles")]
        [SerializeField] bool breakObstacles;
        [Tooltip("막혔다고 판단하는 시간")]
        [SerializeField] float stuckTime = 0.6f;
        [Tooltip("몸에서 이 거리 안의 막는 장애물을 찾는다")]
        [SerializeField] float obstacleSearch = 1.2f;


        public State Current { get; private set; }
        public bool IsDead => Current == State.Dead;
        public Vector3 AimPoint => health != null ? health.AimPoint : transform.position + Vector3.up * (eyeHeight * 0.75f);

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int DeadId = Animator.StringToHash("Dead");

        NavMeshAgent agent;
        Health health;
        Health targetHealth;
        Collider[] colliders;
        Vector3 home;
        Quaternion homeRot;
        float stateTimer;
        float lastSeenTime = -999f;
        float nextAttackTime;
        Vector3 lastSeenPos;
        Coroutine actionRoutine;

        PhysicsKnockback knock;
        EngagementTarget engage;
        bool hasToken;
        float tokenSince;
        float nextTokenTry;

        class Threat
        {
            public Transform who;
            public float value;
        }
        readonly List<Threat> threats = new();

        struct Candidate
        {
            public Transform t;
            public Health health;
            public bool friend;
        }
        static readonly List<Candidate> candidates = new();
        static float candidatesTime = -999f;

        Breakable obstacle;
        Vector3 obstacleStand;
        bool hasStand;
        float nextBreakSearch;
        Vector3 stuckRef;
        float stuckTimer, stuckSample;

        public Transform Target => target;
        public Breakable Obstacle => obstacle;
        public bool HasToken => hasToken;

        public static MonsterBrain FindNearest(Vector3 from, float range)
        {
            MonsterBrain best = null;
            float bestSqr = range * range;
            foreach (var m in active)
            {
                if (m.IsDead) continue;
                float d = (m.transform.position - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = m; }
            }
            return best;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            colliders = GetComponentsInChildren<Collider>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            knock = GetComponent<PhysicsKnockback>();
            if (telegraph == null) telegraph = GetComponent<TelegraphIndicator>();
            home = transform.position;
            homeRot = transform.rotation;
        }

        void OnEnable()
        {
            active.Add(this);
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            if (knock != null) knock.Landed += OnLanded;
        }

        void OnDisable()
        {
            active.Remove(this);
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            if (knock != null) knock.Landed -= OnLanded;
            ReleaseEngagement();
            telegraph?.Hide();
        }

        void Start()
        {
            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
            }
            if (target != null) SetTarget(target);
            Enter(State.Idle);
        }

        // ───────────────────────── Targeting / Aggro ─────────────────────────

        float nextRetarget;

        /// <summary>Player 태그 후보 목록 (모든 몬스터가 공유, 1초마다 갱신 — FindGameObjectsWithTag를 몬스터 수만큼 부르지 않게)</summary>
        static List<Candidate> Candidates()
        {
            if (Time.time - candidatesTime < 1f) return candidates;
            candidatesTime = Time.time;
            candidates.Clear();
            foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
                candidates.Add(new Candidate
                {
                    t = p.transform,
                    health = p.GetComponent<Health>(),
                    friend = p.GetComponent<FriendNPC>() != null,
                });
            return candidates;
        }

        void SetTarget(Transform t)
        {
            if (t == target && targetHealth != null) return;
            ReleaseEngagement();
            target = t;
            targetHealth = t != null ? t.GetComponent<Health>() : null;
            engage = useAttackTokens && t != null ? t.GetComponent<EngagementTarget>() : null;
            if (Current == State.Circle) Enter(State.Chase);
        }

        float ThreatOf(Transform t)
        {
            foreach (var th in threats) if (th.who == t) return th.value;
            return 0f;
        }

        void AddThreat(GameObject source, float amount)
        {
            if (!useAggro || source == null || amount <= 0f) return;
            var t = source.transform;
            foreach (var th in threats)
                if (th.who == t) { th.value += amount; return; }
            threats.Add(new Threat { who = t, value = amount });
            nextRetarget = 0f; // 맞으면 바로 대상 재평가
        }

        float Score(in Candidate c)
        {
            float d = Vector3.Distance(c.t.position, transform.position);
            if (!useAggro) return -d;
            float priority = c.friend ? friendPriority : 1f;
            return priority * 8f / (d + 1.5f) + ThreatOf(c.t);
        }

        void ChooseTarget()
        {
            if (useAggro)
                for (int i = threats.Count - 1; i >= 0; i--)
                {
                    threats[i].value -= threatDecay * Time.deltaTime;
                    if (threats[i].value <= 0f || threats[i].who == null) threats.RemoveAt(i);
                }

            if (!retargetNearestPlayer || Time.time < nextRetarget) return;
            nextRetarget = Time.time + 0.25f;
            if (Current == State.Attack) return; // 휘두르는 중에는 바꾸지 않는다

            Transform best = null;
            float bestScore = float.MinValue, currentScore = float.MinValue;
            foreach (var c in Candidates())
            {
                if (c.t == null || (c.health != null && c.health.IsDead)) continue;
                float s = Score(c);
                if (c.t == target) currentScore = s;
                if (s > bestScore) { bestScore = s; best = c.t; }
            }
            if (best == null || best == target) return;

            bool currentGone = target == null || targetHealth == null || targetHealth.IsDead || currentScore == float.MinValue;
            // 히스테리시스: 점수가 비슷하면 지금 대상을 유지해서 우왕좌왕하지 않게
            bool better = !useAggro ? bestScore > currentScore
                                    : bestScore > currentScore * switchMargin + 0.3f;
            if (currentGone || better) SetTarget(best);
        }

        // ───────────────────────── Obstacles ─────────────────────────

        /// <summary>
        /// 길이 막혔는지 판단: 가려는 속도(desiredVelocity)는 있는데 0.5초 동안 거의 못 움직였거나,
        /// 경로가 끊겨(PathPartial, 바리케이드 carving) 끝점에 도착했으면 막힌 것.
        /// 막혔고 근처에 길을 막는 Breakable이 있으면 그걸 임시 대상으로 삼는다.
        /// </summary>
        void UpdateObstacle()
        {
            if (obstacle != null)
            {
                if (!obstacle.Blocking)
                {
                    // 부서졌거나 열렸다 → 원래 대상으로
                    obstacle = null;
                    nextRetarget = 0f;
                    stuckTimer = 0f;
                }
                return;
            }
            if (Current != State.Chase && Current != State.Circle) { stuckTimer = 0f; return; }

            // 1) 대상에게 가는 길 자체가 끊김 (바리케이드·셔터 carving): 끊긴 경로의 끝점은 "기하학적으로 가장 가까운 곳"이라
            //    벽 하나 너머에서 멈춰 버린다. → 실제로 갈 수 있고, 부수면 대상에게 가장 가까워지는 장애물을 고른다.
            if (target != null && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathPartial && Time.time >= nextBreakSearch)
            {
                nextBreakSearch = Time.time + 1f; // 경로 계산이 여러 번 들어가므로 1초에 한 번만
                var pick = Breakable.FindBestToBreak(transform.position, target.position, agent.agentTypeID, out var stand);
                if (pick != null)
                {
                    obstacle = pick;
                    obstacleStand = stand;
                    hasStand = true;
                    stuckTimer = 0f;
                    ReleaseEngagement();
                    if (Current == State.Circle) Enter(State.Chase);
                    return;
                }
            }

            // 2) 길은 이어져 있는데 못 지나감 (닫힌 문 — NavMesh는 문짝 없이 구워져 있다): 제자리걸음 감지

            if (Time.time >= stuckSample)
            {
                // 0.25초마다 이동량 샘플링
                Vector3 moved = transform.position - stuckRef;
                moved.y = 0f;
                bool wants = agent.hasPath && agent.desiredVelocity.sqrMagnitude > 0.2f;
                bool notMoving = moved.magnitude < 0.08f;
                bool partialEnd = agent.pathStatus == NavMeshPathStatus.PathPartial && !agent.pathPending
                                  && agent.remainingDistance < agent.radius + 0.6f;
                stuckTimer = (wants && notMoving) || partialEnd ? stuckTimer + 0.25f : 0f;
                stuckRef = transform.position;
                stuckSample = Time.time + 0.25f;
            }
            if (stuckTimer < stuckTime) return;

            var b = Breakable.FindBlocking(transform.position, obstacleSearch + agent.radius);
            if (b == null) return;
            obstacle = b;
            hasStand = false;
            stuckTimer = 0f;
            ReleaseEngagement();
            if (Current == State.Circle) Enter(State.Chase);
        }

        float nextRepath;

        /// <summary>
        /// 목적지 갱신을 제한한다. 매 프레임 SetDestination을 부르면 비동기 경로 계산이 매번 처음부터 다시 시작된다.
        /// 평소엔 금방 끝나서 티가 안 나지만, 바리케이드 carving으로 길이 끊기면 "닿을 수 없음"을 확인하느라
        /// 도달 가능한 영역 전체를 탐색해야 해서 여러 프레임이 걸린다 → 계속 리셋되어 pathPending이 끝나지 않고 양이 멈춰 있었다.
        /// </summary>
        Vector3 requestedDest = new Vector3(float.MaxValue, 0f, 0f);

        void SetDest(Vector3 p)
        {
            if (agent.pathPending) return;
            // agent.destination은 경로가 끊기면(PathPartial) "닿을 수 있는 끝점"을 돌려준다 → 요청한 목적지는 따로 기억해 비교
            float moved = (requestedDest - p).sqrMagnitude;
            if (moved < 0.09f && agent.hasPath) return;
            if (Time.time < nextRepath && moved < 1f) return;
            nextRepath = Time.time + (agent.pathStatus == NavMeshPathStatus.PathPartial ? 0.6f : 0.2f);
            requestedDest = p;
            agent.SetDestination(p);
        }

        void ChaseObstacle()
        {
            Vector3 p = obstacle.ClosestPoint(transform.position);
            Vector3 to = p - transform.position;
            to.y = 0f;
            if (to.magnitude <= attackRange)
            {
                agent.isStopped = true;
                FaceTowards(p);
                if (Time.time >= nextAttackTime) Enter(State.Attack);
            }
            else
            {
                agent.isStopped = false;
                agent.stoppingDistance = attackRange * 0.7f;
                SetDest(hasStand ? obstacleStand : p);
            }
        }

        // ───────────────────────── Tokens ─────────────────────────

        bool NeedsToken => useAttackTokens && engage != null;

        bool TryToken()
        {
            if (!NeedsToken || hasToken) return hasToken || !NeedsToken;
            if (Time.time < nextTokenTry) return false;
            nextTokenTry = Time.time + tokenRetry;
            if (!engage.TryAcquire(this, tokenCost)) return false;
            hasToken = true;
            tokenSince = Time.time;
            return true;
        }

        void ReleaseToken(float retryDelay)
        {
            if (engage != null) engage.Release(this);
            hasToken = false;
            nextTokenTry = Time.time + retryDelay;
        }

        void ReleaseEngagement()
        {
            if (engage != null) engage.Release(this);
            hasToken = false;
        }

        // ───────────────────────── Update ─────────────────────────

        void Update()
        {
            if (Current == State.Dead) return;

            // 다른 양에게 부딪혀 날아가는 경우처럼 피해 이벤트 없이 물리가 시작돼도 상태를 맞춘다
            if (knock != null && knock.IsLaunched)
            {
                if (Current != State.Launched) EnterLaunched();
                return;
            }
            if (Current == State.Launched || !agent.enabled) return;

            if (breakObstacles) UpdateObstacle();
            if (obstacle == null) ChooseTarget();

            bool targetValid = target != null && (targetHealth == null || !targetHealth.IsDead);
            bool sees = targetValid && CanSeeTarget();
            bool aware = sees || (trackWithoutSight && targetValid);
            if (aware) { lastSeenTime = Time.time; lastSeenPos = target.position; }

            switch (Current)
            {
                case State.Idle:
                    if (aware) { Enter(State.Chase); break; }
                    if ((stateTimer -= Time.deltaTime) <= 0f) Enter(State.Patrol);
                    break;

                case State.Patrol:
                    if (aware) { Enter(State.Chase); break; }
                    if (Arrived()) Enter(State.Idle);
                    break;

                case State.Chase:
                    if (obstacle != null) { ChaseObstacle(); break; }
                    if (!targetValid || Vector3.Distance(home, transform.position) > leashDistance
                        || Time.time - lastSeenTime > loseTargetTime) { Enter(State.Return); break; }

                    float dist = Vector3.Distance(transform.position, target.position);
                    if (NeedsToken && !TryToken())
                    {
                        // 토큰 없음 → 링 근처까지 오면 포위 대기
                        if (dist <= engage.RingRadius + engageMargin) { Enter(State.Circle); break; }
                    }
                    else if (hasToken && Time.time - tokenSince > tokenTimeout)
                    {
                        // 토큰을 쥐고 못 때리는 중(길 막힘) → 다른 양에게 기회를 넘긴다
                        ReleaseToken(1f);
                        break;
                    }

                    SetDest(aware ? target.position : lastSeenPos);
                    if (sees && dist <= attackRange)
                    {
                        FaceTarget();
                        agent.isStopped = true;
                        if (Time.time >= nextAttackTime) Enter(State.Attack);
                    }
                    else agent.isStopped = false;
                    break;

                case State.Circle:
                    if (!targetValid || engage == null || obstacle != null) { Enter(State.Chase); break; }
                    if (TryToken()) { Enter(State.Chase); break; }
                    {
                        Vector3 slot = engage.GetSlotPosition(this, agent.agentTypeID);
                        SetDest(slot);
                        // 슬롯이 멀면(대상이 도망감) 뛰어서 따라가고, 가까우면 천천히 맴돈다
                        float toSlot = Vector3.Distance(transform.position, slot);
                        agent.speed = toSlot > 2.5f ? chaseSpeed : circleSpeed;
                        float d = Vector3.Distance(transform.position, target.position);
                        if (d > engage.RingRadius + engageMargin * 3f) { Enter(State.Chase); break; }
                        FaceTarget();
                    }
                    break;

                case State.Return:
                    if (sees && Vector3.Distance(home, target.position) < leashDistance) { Enter(State.Chase); break; }
                    if (Arrived()) Enter(State.Idle);
                    break;

                // Attack / Stagger는 코루틴이 끝나면 Chase로 돌아간다
            }

            if (animator != null)
                animator.SetFloat(SpeedId, agent.enabled ? agent.velocity.magnitude : 0f, speedDampTime, Time.deltaTime);
        }

        void Enter(State next)
        {
            if (actionRoutine != null) { StopCoroutine(actionRoutine); actionRoutine = null; }
            if (Current == State.Attack && next != State.Attack) telegraph?.Hide();
            if (Current == State.Circle && next != State.Circle && engage != null) engage.ReleaseSlot(this);
            Current = next;
            if (!agent.enabled || !agent.isOnNavMesh) return;
            // 맴돌 때는 대상을 바라보며 옆걸음질 (에이전트가 진행 방향으로 몸을 돌리지 않게)
            agent.updateRotation = next != State.Circle;

            switch (next)
            {
                case State.Idle:
                    agent.isStopped = true;
                    stateTimer = Random.Range(idleTime.x, idleTime.y);
                    break;
                case State.Patrol:
                    agent.isStopped = false;
                    agent.speed = patrolSpeed;
                    agent.stoppingDistance = 0.3f;
                    agent.SetDestination(RandomPointNear(home, patrolRadius));
                    break;
                case State.Chase:
                    agent.isStopped = false;
                    agent.speed = chaseSpeed;
                    // 플레이어 몸까지 파고들어 밀어내지 않도록 공격 거리 앞에서 멈춘다
                    agent.stoppingDistance = attackRange * 0.85f;
                    break;
                case State.Return:
                    agent.isStopped = false;
                    agent.stoppingDistance = 0.3f;
                    agent.speed = patrolSpeed * 1.5f;
                    agent.SetDestination(home);
                    break;
                case State.Circle:
                    agent.isStopped = false;
                    agent.speed = circleSpeed;
                    agent.stoppingDistance = 0.15f;
                    break;
                case State.Attack:
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                    actionRoutine = StartCoroutine(AttackRoutine());
                    break;
            }
        }

        // ───────────────────────── Attack ─────────────────────────

        IEnumerator AttackRoutine()
        {
            nextAttackTime = Time.time + attackCooldown;
            animator?.SetTrigger(AttackId);
            telegraph?.Show(attackHitRange, attackHitAngle, attackWindup);

            // 예비 동작 앞부분에서만 방향을 맞춘다 (뒷부분은 고정 → 플레이어가 옆으로 피할 수 있게)
            float t = 0f;
            while (t < attackWindup)
            {
                if (t < attackWindup * 0.6f) { if (obstacle != null) FaceTowards(obstacle.ClosestPoint(transform.position)); else FaceTarget(); }
                t += Time.deltaTime;
                yield return null;
            }

            telegraph?.Release();
            if (obstacle != null)
            {
                // 장애물 두드리기: 정면 각도만 느슨하게 보고 거리로 판정
                Vector3 p = obstacle.ClosestPoint(transform.position);
                Vector3 to = p - transform.position;
                to.y = 0f;
                if (to.magnitude <= attackHitRange && !obstacle.Broken)
                    obstacle.Health.TakeDamage(new DamageInfo
                    {
                        amount = attackDamage,
                        point = p + Vector3.up * 1f,
                        direction = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward,
                        knockback = attackKnockback,
                        source = gameObject,
                    });
            }
            else if (target != null && targetHealth != null && !targetHealth.IsDead)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.magnitude <= attackHitRange && Vector3.Angle(transform.forward, to) <= attackHitAngle * 0.5f)
                {
                    targetHealth.TakeDamage(new DamageInfo
                    {
                        amount = attackDamage,
                        point = targetHealth.AimPoint,
                        direction = to.normalized,
                        knockback = attackKnockback,
                        source = gameObject,
                    });
                }
            }

            yield return new WaitForSeconds(attackRecovery);
            actionRoutine = null;
            // 공격이 끝나면 토큰 반납 → 쿨다운 동안은 다시 못 받으니 다른 양에게 차례가 돈다
            if (hasToken) ReleaseToken(attackCooldown);
            Enter(State.Chase);
        }

        // ───────────────────────── Hit / Death ─────────────────────────

        void OnDamaged(DamageInfo info)
        {
            AddThreat(info.source, info.amount * damageThreat);
            if (Current == State.Dead) return;

            animator?.SetTrigger(HitId);
            if (target != null) { lastSeenTime = Time.time; lastSeenPos = target.position; }

            // 강한 공격 → 물리로 날아감 (이미 날아가는 중이면 공중 추가타로 다시 띄움)
            if (knock != null && (knock.IsLaunched || knock.ShouldLaunch(info)))
            {
                if (knock.ShouldLaunch(info)) knock.Launch(info.direction, info.knockback);
                if (Current != State.Launched) EnterLaunched();
                return;
            }

            // 강공격이거나 예비 동작 중이 아니면 경직 (예비 동작은 일반 공격으로 끊기지 않음 = 슈퍼아머)
            if (info.heavy || Current != State.Attack)
            {
                if (actionRoutine != null) StopCoroutine(actionRoutine);
                telegraph?.Hide();
                if (hasToken) ReleaseToken(0.6f);
                if (engage != null) engage.ReleaseSlot(this);
                Current = State.Stagger;
                actionRoutine = StartCoroutine(StaggerRoutine(info));
            }
        }

        IEnumerator StaggerRoutine(DamageInfo info)
        {
            if (agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }

            Vector3 push = info.direction;
            push.y = 0f;
            float distance = info.knockback * knockbackScale;
            float t = 0f;
            while (t < staggerTime)
            {
                float k = 1f - t / staggerTime; // 처음에 빠르게 밀리고 감속
                if (agent.isOnNavMesh && push.sqrMagnitude > 0.001f)
                    agent.Move(push.normalized * (distance * 2f / staggerTime * k * Time.deltaTime));
                t += Time.deltaTime;
                yield return null;
            }
            actionRoutine = null;
            Enter(State.Chase);
        }

        void EnterLaunched()
        {
            if (actionRoutine != null) { StopCoroutine(actionRoutine); actionRoutine = null; }
            telegraph?.Hide();
            ReleaseEngagement();
            Current = State.Launched;
            if (animator != null) animator.SetFloat(SpeedId, 0f);
        }

        void OnLanded(bool wallSlammed)
        {
            if (Current == State.Dead) return; // 죽은 채로 날아간 경우는 DeathRoutine이 처리
            Current = State.Stagger;
            actionRoutine = StartCoroutine(GetUpRoutine(wallSlammed ? wallStun : getUpTime));
        }

        IEnumerator GetUpRoutine(float time)
        {
            if (agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            yield return new WaitForSeconds(time);
            actionRoutine = null;
            Enter(State.Chase);
        }

        void OnDied()
        {
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            telegraph?.Hide();
            ReleaseEngagement();
            bool flying = knock != null && knock.IsLaunched;
            Current = State.Dead;
            if (!flying)
            {
                // 날아가는 중이면 착지할 때까지 충돌체를 남겨 둔다 (안 그러면 바닥을 뚫고 떨어짐)
                if (agent.isOnNavMesh) agent.isStopped = true;
                agent.enabled = false;
                foreach (var c in colliders) c.enabled = false;
            }
            if (animator != null)
            {
                animator.SetFloat(SpeedId, 0f);
                animator.SetBool(DeadId, true);
            }
            actionRoutine = StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            float t0 = Time.time;
            while (knock != null && knock.IsLaunched) yield return null;
            agent.enabled = false;
            foreach (var c in colliders) c.enabled = false;
            yield return new WaitForSeconds(Mathf.Max(0f, sinkDelay - (Time.time - t0)));
            Vector3 start = transform.position;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                transform.position = start + Vector3.down * (t / 1.5f * 2.4f);
                yield return null;
            }

            if (!respawnAfterDeath)
            {
                gameObject.SetActive(false);
                yield break;
            }

            yield return new WaitForSeconds(Mathf.Max(0f, respawnDelay - sinkDelay - 1.5f));

            // 부활
            transform.SetPositionAndRotation(home, homeRot);
            foreach (var c in colliders) c.enabled = true;
            agent.enabled = true;
            agent.Warp(home);
            if (animator != null) animator.SetBool(DeadId, false);
            health.Revive();
            actionRoutine = null;
            Enter(State.Idle);
        }

        // ───────────────────────── Helpers ─────────────────────────

        bool Arrived() => agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

        bool CanSeeTarget()
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = target.position + Vector3.up * 1f;
            Vector3 to = aim - eye;
            float dist = to.magnitude;
            if (dist > viewDistance) return false;
            if (dist > hearDistance && Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return false;
            return !Physics.Raycast(eye, to / dist, dist, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        void FaceTarget()
        {
            if (target == null) return;
            FaceTowards(target.position);
        }

        void FaceTowards(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
        }

        static Vector3 RandomPointNear(Vector3 center, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(center + new Vector3(r.x, 0f, r.y), out var hit, 2f, NavMesh.AllAreas))
                    return hit.position;
            }
            return center;
        }

        /// <summary>플레이 중 씬 뷰 디버그: 상태 라벨, 대상 연결선 (빨강 = 토큰 보유/공격, 노랑 = 포위 대기)</summary>
        void OnDrawGizmos()
        {
            if (!Application.isPlaying || Current == State.Dead) return;
            Vector3 head = transform.position + Vector3.up * (eyeHeight + 0.4f);
            if (target != null)
            {
                Gizmos.color = hasToken || Current == State.Attack ? new Color(1f, 0.2f, 0.2f, 0.9f)
                             : Current == State.Circle ? new Color(1f, 0.85f, 0.2f, 0.6f)
                             : new Color(1f, 1f, 1f, 0.25f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.3f, target.position + Vector3.up * 0.3f);
            }
#if UNITY_EDITOR
            string label = Current.ToString();
            if (hasToken) label += " ★";
            if (obstacle != null) label += " ▣" + obstacle.name;
            if (useAggro && target != null) label += $"\n→ {target.name.Replace("prf_Set Costume03 ", "")}";
            UnityEditor.Handles.Label(head, label);
#endif
        }

        void OnDrawGizmosSelected()
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, hearDistance);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Vector3 l = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward * viewDistance;
            Vector3 r = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward * viewDistance;
            Gizmos.DrawLine(eye, eye + l);
            Gizmos.DrawLine(eye, eye + r);
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, patrolRadius);
        }
    }
}
