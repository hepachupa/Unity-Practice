using System.Collections.Generic;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.AI
{
    /// <summary>
    /// 지켜야 하는 친구.
    /// - 양이 panicRadius 안에 들어오면 우는 표정(Face 블렌드셰이프)을 짓고 눈물을 흘리며 양에게서 먼 쪽으로 도망다닌다
    ///   (주변 후보 지점 중 "가장 가까운 양과의 거리"가 가장 먼 곳으로 계속 다시 고름 → 구석에 몰려도 빈 쪽으로 빠져나감)
    /// - 양이 calmRadius 밖으로 멀어지고 잠시 지나면 진정 (표정 복귀, 멈춤)
    /// - 맞으면 움찔 + 놀란 표정 + 알림, 쓰러지면 옆으로 눕고 SchoolDefenseGame에 알린다
    /// - 평소에는 처음 자리 주변을 천천히 돌아다닌다 (문 앞에 오면 스스로 연다)
    /// - 대화(DialogueDirector): 멈춰서 플레이어를 바라보고 대화 표정, 대사 시작 때 효과음(예: 미사키 "이게 무슨 소리지?" + 우르릉)
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class FriendNPC : MonoBehaviour
    {
        [SerializeField] string displayName = "친구";
        [SerializeField] Transform visual;
        [Header("Panic")]
        [SerializeField] float panicRadius = 6f;
        [SerializeField] float calmRadius = 9f;
        [SerializeField] float calmDelay = 1.5f;
        [SerializeField] float fleeSpeed = 2.6f;
        [SerializeField] float repathInterval = 0.4f;
        [SerializeField] float fleeStep = 4.5f;
        [Header("Face")]
        [SerializeField] SkinnedMeshRenderer face;
        [SerializeField] string cryShape = "Expression.Fsad";
        [SerializeField] string hurtShape = "Expression.Fdam";
        [SerializeField] Material dustMaterial;
        [Tooltip("우는 동안 눈 아래로 흐르는 눈물 줄기 (반투명 하늘색)")]
        [SerializeField] Material tearMaterial;
        [Header("Talk")]
        [TextArea] [SerializeField] string[] talkLines = new string[0];
        [SerializeField] string talkShape = "Expression.Fsad";
        [Tooltip("첫 대사가 시작될 때 재생할 효과음 이름 (Sfx, 비우면 없음)")]
        [SerializeField] string talkSfx = "";
        [Header("Wander")]
        [SerializeField] float wanderRadius = 5f;
        [SerializeField] float wanderSpeed = 1.1f;
        [SerializeField] Vector2 wanderPause = new Vector2(2f, 5f);

        public string DisplayName => displayName;
        public Health Health { get; private set; }
        public bool Panicking { get; private set; }
        public bool Talking { get; private set; }
        public bool Talked { get; private set; }
        public string[] TalkLines => talkLines;
        public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * 0.85f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly Color TearColor = new Color(0.55f, 0.8f, 1f, 1f);

        NavMeshAgent agent;
        Animator animator;
        Vector3 baseScale;
        float flinch;
        float nextToast;
        float nextRepath;
        float lastThreatTime = -999f;
        float tearTimer;
        int cryIndex = -1, hurtIndex = -1;
        float cryWeight, hurtWeight;
        readonly List<MonsterBrain> threats = new();
        readonly List<Transform> tears = new();
        readonly List<Vector3> tearScales = new();
        Transform head;
        Transform talkTarget;
        int talkIndex = -1;
        float talkWeight;
        Vector3 home;
        float nextWander;

        void Awake()
        {
            Health = GetComponent<Health>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
            if (visual == null) visual = transform;
            baseScale = visual.localScale;
            if (face == null)
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex(cryShape) >= 0) { face = smr; break; }
            if (face != null)
            {
                cryIndex = face.sharedMesh.GetBlendShapeIndex(cryShape);
                hurtIndex = face.sharedMesh.GetBlendShapeIndex(hurtShape);
            }
            DustFx.Init(dustMaterial);
            if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (face != null && !string.IsNullOrEmpty(talkShape)) talkIndex = face.sharedMesh.GetBlendShapeIndex(talkShape);
            home = transform.position;
            nextWander = Time.time + Random.Range(1f, 3f);
            BuildTears();
        }

        /// <summary>
        /// 이 캐릭터 팩의 표정 블렌드셰이프는 입·눈썹만 살짝 바뀌어 멀리서는 우는 게 안 보인다.
        /// 그래서 양쪽 눈 아래에 눈물 줄기(쿼드)를 머리 뼈에 붙여 두고 울 때만 켠다.
        /// </summary>
        void BuildTears()
        {
            if (tearMaterial == null || animator == null || !animator.isHuman) return;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("prf_Eye") || r.name.Contains("Light")) continue;
                Bounds b = r.bounds;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Tear";
                Destroy(q.GetComponent<Collider>());
                var qr = q.GetComponent<Renderer>();
                qr.sharedMaterial = tearMaterial;
                qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                qr.receiveShadows = false;
                q.transform.SetParent(head, true);
                // 눈 아래 볼 위, 얼굴 앞쪽으로 살짝
                q.transform.position = b.center + Vector3.down * (b.extents.y * 0.6f + 0.035f) + transform.forward * (b.extents.z + 0.015f);
                q.transform.rotation = Quaternion.LookRotation(-transform.forward, Vector3.up);
                Vector3 world = new Vector3(Mathf.Max(0.015f, b.size.x * 0.2f), 0.07f, 1f);
                Vector3 ls = head.lossyScale;
                q.transform.localScale = new Vector3(world.x / ls.x, world.y / ls.y, 1f / ls.z);
                tears.Add(q.transform);
                tearScales.Add(q.transform.localScale);
                q.SetActive(false);
            }
        }

        void UpdateTears()
        {
            for (int i = 0; i < tears.Count; i++)
            {
                var t = tears[i];
                if (t == null) continue;
                if (t.gameObject.activeSelf != Panicking) t.gameObject.SetActive(Panicking);
                if (!Panicking) continue;
                float wob = 1f + Mathf.Sin(Time.time * 18f + i * 1.7f) * 0.12f;
                var s = tearScales[i];
                t.localScale = new Vector3(s.x * (2f - wob), s.y * wob, s.z);
            }
        }

        void OnEnable()
        {
            Health.Damaged += OnDamaged;
            Health.Died += OnDied;
        }

        void OnDisable()
        {
            Health.Damaged -= OnDamaged;
            Health.Died -= OnDied;
        }

        // ───────────────────────── Panic / flee ─────────────────────────

        void Update()
        {
            if (Health.IsDead) return;
            bool gameOver = SchoolDefenseGame.Instance != null && SchoolDefenseGame.Instance.Finished;

            float nearest = float.MaxValue;
            threats.Clear();
            foreach (var m in MonsterBrain.Active)
            {
                if (m == null || m.IsDead) continue;
                float d = Vector3.Distance(m.transform.position, transform.position);
                if (d < calmRadius + 4f) threats.Add(m);
                nearest = Mathf.Min(nearest, d);
            }

            if (Talking)
            {
                // 대화 중: 멈춰서 플레이어를 바라본다
                if (talkTarget != null)
                {
                    Vector3 to = talkTarget.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                }
            }
            else if (!gameOver && nearest < panicRadius)
            {
                lastThreatTime = Time.time;
                if (!Panicking) StartPanic();
            }
            else if (Panicking && (gameOver || (nearest > calmRadius && Time.time - lastThreatTime > calmDelay)))
            {
                StopPanic();
            }

            if (Panicking && agent != null && agent.isOnNavMesh && Time.time >= nextRepath)
            {
                nextRepath = Time.time + repathInterval;
                agent.SetDestination(PickFleePoint());
            }

            if (!Talking && !Panicking && !gameOver) Wander();

            if (Panicking)
            {
                tearTimer -= Time.deltaTime;
                if (tearTimer <= 0f)
                {
                    tearTimer = 0.18f;
                    Vector3 head = transform.position + Vector3.up * 0.85f + transform.right * Random.Range(-0.12f, 0.12f);
                    DustFx.Burst(head, 1, TearColor, 1.2f, 0.07f, 0.45f);
                }
            }

            if (animator != null)
                animator.SetFloat(SpeedId, agent != null && agent.enabled ? agent.velocity.magnitude : 0f, 0.1f, Time.deltaTime);

            UpdateTears();

            // 표정
            talkWeight = Mathf.MoveTowards(talkWeight, Talking && !Panicking ? 100f : 0f, Time.deltaTime * 300f);
            if (face != null && talkIndex >= 0 && talkIndex != cryIndex && talkIndex != hurtIndex)
                face.SetBlendShapeWeight(talkIndex, talkWeight);
            cryWeight = Mathf.MoveTowards(cryWeight, Panicking ? 100f : 0f, Time.deltaTime * 400f);
            hurtWeight = Mathf.MoveTowards(hurtWeight, 0f, Time.deltaTime * 150f);
            if (face != null)
            {
                if (cryIndex >= 0) face.SetBlendShapeWeight(cryIndex, Mathf.Max(0f, Mathf.Max(cryWeight, talkIndex == cryIndex ? talkWeight : 0f) - hurtWeight));
                if (hurtIndex >= 0) face.SetBlendShapeWeight(hurtIndex, Mathf.Max(hurtWeight, cryWeight * 0.6f));
            }
        }

        // ───────────────────────── Wander / Talk ─────────────────────────

        void Wander()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            bool arrived = !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.1f);
            if (!arrived || Time.time < nextWander) return;
            nextWander = Time.time + Random.Range(wanderPause.x, wanderPause.y) + 2f;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                Vector3 p = home + new Vector3(r.x, 0f, r.y);
                if (!NavMesh.SamplePosition(p, out var hit, 1.5f, filter)) continue;
                agent.isStopped = false;
                agent.speed = wanderSpeed;
                agent.SetDestination(hit.position);
                return;
            }
        }

        public void BeginTalk(Transform player)
        {
            Talking = true;
            talkTarget = player;
            if (agent != null && agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; agent.updateRotation = false; }
        }

        /// <summary>DialogueDirector가 첫 대사를 띄울 때 부른다</summary>
        public void OnLineStarted()
        {
            if (string.IsNullOrEmpty(talkSfx)) return;
            Sfx.Play(talkSfx, transform.position, 0.9f, 0.8f);
            LNStudy.CameraSystem.ThirdPersonCamera.Active?.Shake(0.05f, 0.6f);
        }

        public void EndTalk()
        {
            Talking = false;
            Talked = true;
            talkTarget = null;
            if (agent != null) agent.updateRotation = true;
            nextWander = Time.time + Random.Range(wanderPause.x, wanderPause.y);
        }

        void StartPanic()
        {
            Panicking = true;
            nextRepath = 0f;
            if (agent != null) { agent.isStopped = false; agent.speed = fleeSpeed; }
            Sfx.Play("call", transform.position, 0.5f, 1.6f);
            if (Time.time >= nextToast)
            {
                nextToast = Time.time + 4f;
                StageHUD.Instance?.ShowToast($"{displayName}: 으아앙 양이다!", 1.5f);
            }
        }

        void StopPanic()
        {
            Panicking = false;
            if (agent != null && agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
        }

        /// <summary>주변 후보 지점 중 양들과 가장 먼 곳 (조금이라도 덜 움직이는 쪽을 약하게 선호)</summary>
        Vector3 PickFleePoint()
        {
            Vector3 best = transform.position;
            float bestScore = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                float r = i % 2 == 0 ? fleeStep : fleeStep * 1.8f;
                Vector3 cand = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                var filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };
                if (!NavMesh.SamplePosition(cand, out var hit, 1.5f, filter)) continue;
                float minD = float.MaxValue;
                foreach (var m in threats)
                {
                    if (m == null || m.IsDead) continue;
                    // 양을 가로질러 가는 경로는 감점 (양 바로 옆을 스쳐 지나가지 않게)
                    float d = Vector3.Distance(hit.position, m.transform.position);
                    Vector3 toCand = hit.position - transform.position, toM = m.transform.position - transform.position;
                    if (Vector3.Dot(toCand.normalized, toM.normalized) > 0.6f && toM.magnitude < toCand.magnitude) d *= 0.3f;
                    minD = Mathf.Min(minD, d);
                }
                float score = minD - Vector3.Distance(hit.position, transform.position) * 0.1f;
                if (score > bestScore) { bestScore = score; best = hit.position; }
            }
            return best;
        }

        // ───────────────────────── Damage ─────────────────────────

        void OnDamaged(DamageInfo info)
        {
            flinch = 1f;
            hurtWeight = 100f;
            Sfx.Play("skid", transform.position, 0.5f, 1.7f);
            DustFx.Burst(transform.position + Vector3.up * 0.85f, 6, TearColor, 1.8f, 0.08f, 0.5f);
            if (Time.time >= nextToast)
            {
                nextToast = Time.time + 3f;
                StageHUD.Instance?.ShowToast($"{displayName}이(가) 공격받고 있어!", 1.5f);
            }
        }

        void OnDied()
        {
            Panicking = false;
            UpdateTears();
            if (agent != null) agent.enabled = false;
            if (animator != null) animator.SetFloat(SpeedId, 0f);
            visual.localRotation = Quaternion.Euler(0f, visual.localEulerAngles.y, 85f);
            Sfx.Play("thud", transform.position, 0.8f, 1.2f);
            SchoolDefenseGame.Instance?.OnFriendDown(this);
        }

        void LateUpdate()
        {
            if (Health.IsDead || flinch <= 0f) return;
            flinch = Mathf.MoveTowards(flinch, 0f, Time.deltaTime * 4f);
            float s = 1f - Mathf.Sin(flinch * Mathf.PI) * 0.15f;
            visual.localScale = new Vector3(baseScale.x / Mathf.Sqrt(s), baseScale.y * s, baseScale.z / Mathf.Sqrt(s));
        }
    }
}
