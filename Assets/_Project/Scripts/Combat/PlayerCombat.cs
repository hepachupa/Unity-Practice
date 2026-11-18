using System.Collections;
using System.Collections.Generic;
using LNStudy.CameraSystem;
using LNStudy.Companion;
using LNStudy.Player;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 드론 지휘형 전투 (어깨 드론 = 카이사 포드, 공격 방식 = 엘소드 이브의 모비·레미).
    /// - 일반 공격(좌클릭 / 패드 X): 4단 콤보. 드론이 직접 날아가 들이받는다.
    ///   1타 왼쪽 돌진 → 2타 오른쪽 돌진 → 3타 양쪽 X자 교차 → 4타 양쪽 회전 연타 + 마무리(강공격).
    ///   누르고 있으면 계속 이어지고, 마지막 입력 후 comboResetTime이 지나면 1타로 돌아간다. 선입력 버퍼 있음.
    /// - 스킬(Q / 우클릭 / 패드 Y): 유도 미사일 세례. 근처 적들에게 나눠서 발사, 쿨타임.
    /// - 조준: 카메라 정면 원뿔 안의 적 우선(원신식 소프트 락온) → 없으면 가까운 적 → 없으면 카메라 정면 지점.
    /// - 공격 중에는 캐릭터가 목표를 바라본다(이동은 가능).
    /// 피격·사망 반응(경직 애니메이션, 리스폰)도 여기서 처리한다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerCombat : MonoBehaviour
    {
        [System.Serializable]
        public struct ComboStep
        {
            public bool left, right;
            public ShoulderPet.StrikeStyle style;
            [Tooltip("Spin일 때 타수")] public int hits;
            public float damage;
            public bool heavy;
            public float knockback;
            [Tooltip("두 드론이 동시에 나갈 때 두 번째 드론의 출발 지연")] public float stagger;
            [Tooltip("다음 단계까지 최소 간격")] public float recovery;
        }

        [Header("References")]
        [SerializeField] ShoulderPet[] pets;
        [SerializeField] Projectile missilePrefab;
        [SerializeField] ThirdPersonCamera cameraRig;
        [Tooltip("교체 시스템이 없을 때 쓰는 기본 외형")]
        [SerializeField] PlayerAnimator playerAnimator;
        [SerializeField] CharacterSwitcher switcher;

        ICharacterVisual Visual => switcher != null && switcher.Active != null ? switcher.Active : playerAnimator;

        [Header("Targeting")]
        [SerializeField] LayerMask enemyMask;
        [Tooltip("탄이 부딪히는 레이어 (적 + 환경)")]
        [SerializeField] LayerMask hitMask;
        [Tooltip("일반 공격(드론 돌진) 조준 거리")]
        [SerializeField] float targetRange = 8f;
        [Tooltip("카메라 정면 기준 이 각도 안의 적을 우선 조준")]
        [SerializeField] float aimConeAngle = 35f;
        [Tooltip("원뿔 밖이어도 이 거리 안이면 자동 조준")]
        [SerializeField] float closeAssistRange = 5f;

        [Header("Normal Attack")]
        [SerializeField] ComboStep[] combo =
        {
            new ComboStep { left = true, style = ShoulderPet.StrikeStyle.Dash, damage = 12f, knockback = 0.8f, recovery = 0.28f },
            new ComboStep { right = true, style = ShoulderPet.StrikeStyle.Dash, damage = 12f, knockback = 0.8f, recovery = 0.28f },
            new ComboStep { left = true, right = true, style = ShoulderPet.StrikeStyle.Cross, damage = 10f, knockback = 1.2f, stagger = 0.06f, recovery = 0.38f },
            new ComboStep { left = true, right = true, style = ShoulderPet.StrikeStyle.Spin, hits = 3, damage = 16f, heavy = true, knockback = 3f, stagger = 0.05f, recovery = 0.75f },
        };
        [Tooltip("드론이 목표 앞까지 날아가는 시간")]
        [SerializeField] float dashTime = 0.11f;
        [SerializeField] float strikeRadius = 0.7f;
        [Tooltip("목표가 없을 때 드론이 날아가는 거리")]
        [SerializeField] float emptyStrikeDistance = 3f;
        [SerializeField] float comboResetTime = 0.9f;
        [SerializeField] float inputBuffer = 0.3f;
        [SerializeField] float faceTargetTime = 0.45f;

        [Header("Skill: Missile Rain")]
        [SerializeField] int missileCount = 12;
        [SerializeField] float missileDamage = 7f;
        [SerializeField] float missileInterval = 0.045f;
        [SerializeField] float missileLaunchSpeed = 7f;
        [SerializeField] float missileHomingDelay = 0.18f;
        [SerializeField] float skillCooldown = 6f;
        [SerializeField] float skillRange = 12f;

        [Header("Hit Reaction")]
        [SerializeField] float respawnDelay = 2f;

        public float SkillCooldownRemaining => Mathf.Max(0f, nextSkillTime - Time.time);
        public float SkillCooldownNormalized => skillCooldown > 0f ? SkillCooldownRemaining / skillCooldown : 0f;
        public int ComboIndex => comboIndex;

        PlayerInputReader input;
        PlayerMotor motor;
        Rigidbody body;
        Health health;
        Vector3 spawnPos;
        Quaternion spawnRot;

        int comboIndex;
        float nextAttackTime;
        float lastAttackTime = -999f;
        float nextSkillTime;
        float faceUntil;
        Vector3 faceDir;
        bool skillRunning;

        readonly Collider[] overlap = new Collider[32];
        readonly List<Health> candidates = new();

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            body = GetComponent<Rigidbody>();
            health = GetComponent<Health>();
            if (playerAnimator == null) playerAnimator = GetComponentInChildren<PlayerAnimator>();
            if (switcher == null) switcher = GetComponent<CharacterSwitcher>();
            grab = GetComponent<PlayerGrab>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<ThirdPersonCamera>();
            spawnPos = transform.position;
            spawnRot = transform.rotation;
        }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            if (motor != null) motor.SetFacingLock(this, false);
        }

        PlayerGrab grab;

        /// <summary>현재 캐릭터가 자기만의 기본 공격(예: 시사 라멘)을 갖고 있으면 드론 기본 공격을 쉰다</summary>
        bool HasPrimaryOverride =>
            Visual is MonoBehaviour vb && vb != null && vb.GetComponent<IPrimaryAttackOverride>() is IPrimaryAttackOverride o && o.IsOverriding;
        /// <summary>물체를 잡고 있으면 공격·방향 전환을 하지 않는다 (관절로 묶인 상자와 회전이 싸우며 튕기는 것 방지)</summary>
        bool IsGrabbing => grab != null && grab.Current != null;

        void Update()
        {
            if (health.IsDead || IsGrabbing) return;
            bool canInput = cameraRig == null || cameraRig.IsCursorLocked;

            if (Time.time - lastAttackTime > comboResetTime) comboIndex = 0;

            if (canInput && input.HasBufferedSkill(inputBuffer) && !skillRunning && Time.time >= nextSkillTime)
            {
                input.ClearSkill();
                StartCoroutine(MissileRain());
            }
            else if (canInput && !HasPrimaryOverride && Time.time >= nextAttackTime && !skillRunning
                     && (input.HasBufferedAttack(inputBuffer) || input.AttackHeld))
            {
                input.ClearAttack();
                DoComboStep();
            }

        }

        void FixedUpdate()
        {
            // 공격 중에는 목표를 바라본다 (이동 입력 방향 대신)
            bool facing = !health.IsDead && !IsGrabbing && Time.time < faceUntil;
            motor.SetFacingLock(this, facing);
            if (facing && faceDir.sqrMagnitude > 0.001f)
            {
                Quaternion want = Quaternion.LookRotation(faceDir);
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, want, 1080f * Time.fixedDeltaTime));
            }
        }

        // ───────────────────────── Normal Attack ─────────────────────────

        void DoComboStep()
        {
            var step = combo[comboIndex];
            Health target = FindTarget(targetRange);
            Vector3 aimPoint = target != null ? target.AimPoint : EmptyStrikePoint();

            BeginFacing(aimPoint);
            Visual?.PlayAttack();

            var info = new DamageInfo { amount = step.damage, heavy = step.heavy, knockback = step.knockback, source = gameObject };
            StartCoroutine(SendPets(step, target, aimPoint, info));

            lastAttackTime = Time.time;
            nextAttackTime = Time.time + step.recovery;
            comboIndex = (comboIndex + 1) % combo.Length;
        }

        IEnumerator SendPets(ComboStep step, Health target, Vector3 aimPoint, DamageInfo info)
        {
            bool first = true;
            foreach (var pet in pets)
            {
                if (pet == null) continue;
                bool isLeft = pet.PetSide == ShoulderPet.Side.Left;
                if ((isLeft && !step.left) || (!isLeft && !step.right)) continue;

                if (!first && step.stagger > 0f) yield return new WaitForSeconds(step.stagger);
                first = false;

                pet.Strike(new ShoulderPet.StrikeOrder
                {
                    style = step.style,
                    target = target,
                    point = aimPoint,
                    damage = info,
                    enemyMask = enemyMask,
                    hitRadius = strikeRadius,
                    hits = step.hits,
                    dashTime = dashTime,
                });
            }
        }

        /// <summary>목표가 없을 때: 카메라 정면 방향으로 일정 거리 앞 (벽이 있으면 벽 앞)</summary>
        Vector3 EmptyStrikePoint()
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 fwd = cameraRig != null ? cameraRig.transform.forward : transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : transform.forward;
            int envMask = hitMask & ~enemyMask;
            float dist = emptyStrikeDistance;
            if (Physics.Raycast(origin, fwd, out var hit, dist, envMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0.5f, hit.distance - 0.4f);
            return origin + fwd * dist;
        }

        // ───────────────────────── Skill ─────────────────────────

        IEnumerator MissileRain()
        {
            skillRunning = true;
            nextSkillTime = Time.time + skillCooldown;

            var targets = FindTargetsInRange(skillRange);
            Vector3 fallback = CameraAimPoint(skillRange * 0.7f);
            BeginFacing(targets.Count > 0 ? targets[0].AimPoint : fallback, 0.3f + missileCount * missileInterval);
            Visual?.PlaySkill();

            yield return new WaitForSeconds(0.15f); // 손 드는 동작과 맞추기

            for (int i = 0; i < missileCount; i++)
            {
                var pet = pets[i % pets.Length];
                if (pet == null) continue;

                // 대상이 여럿이면 골고루 나눈다
                Health t = targets.Count > 0 ? targets[i % targets.Count] : null;
                if (t != null && t.IsDead) t = null;
                Vector3 aim = t != null ? t.AimPoint : fallback + Random.insideUnitSphere * 1.2f;

                // 위·바깥쪽으로 퍼지며 튀어나간 뒤 목표로 꺾인다
                float sideSign = pet.PetSide == ShoulderPet.Side.Left ? -1f : 1f;
                Vector3 launch = (Vector3.up * Random.Range(0.8f, 1.2f)
                                  + transform.right * sideSign * Random.Range(0.4f, 0.9f)
                                  - transform.forward * Random.Range(0f, 0.4f)).normalized * missileLaunchSpeed;

                var info = new DamageInfo { amount = missileDamage, knockback = 0.6f, heavy = i == missileCount - 1, source = gameObject };
                pet.Kick(launch);
                Projectile.Fire(missilePrefab, pet.MuzzlePosition, launch, t, aim, info, hitMask, missileHomingDelay);

                yield return new WaitForSeconds(missileInterval);
            }

            skillRunning = false;
        }

        // ───────────────────────── Targeting ─────────────────────────

        Health FindTarget(float range)
        {
            Transform cam = cameraRig != null ? cameraRig.transform : null;
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Health best = null;
            float bestScore = float.MaxValue;

            int n = Physics.OverlapSphereNonAlloc(origin, range, overlap, enemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (h == null || h.IsDead || h == health) continue;

                Vector3 to = h.AimPoint - origin;
                float dist = to.magnitude;
                float angle = cam != null ? Vector3.Angle(cam.forward, to) : Vector3.Angle(transform.forward, to);

                bool inCone = angle <= aimConeAngle;
                bool close = dist <= closeAssistRange;
                if (!inCone && !close) continue;

                // 원뿔 안 = 각도 우선, 가까운 보정 = 거리 우선 (원뿔 안이 항상 이김)
                float score = inCone ? angle * 0.5f + dist : 1000f + dist;
                if (score < bestScore && HasLineOfSight(origin, h.AimPoint)) { bestScore = score; best = h; }
            }
            return best;
        }

        List<Health> FindTargetsInRange(float range)
        {
            candidates.Clear();
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            int n = Physics.OverlapSphereNonAlloc(origin, range, overlap, enemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (h != null && !h.IsDead && h != health && !candidates.Contains(h)) candidates.Add(h);
            }
            candidates.Sort((a, b) =>
                (a.transform.position - transform.position).sqrMagnitude.CompareTo((b.transform.position - transform.position).sqrMagnitude));
            return candidates;
        }

        bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            int envMask = hitMask & ~enemyMask;
            return !Physics.Linecast(from, to, envMask, QueryTriggerInteraction.Ignore);
        }

        Vector3 CameraAimPoint(float range)
        {
            if (cameraRig == null) return transform.position + transform.forward * range + Vector3.up * 1.2f;
            var cam = cameraRig.transform;
            int envMask = hitMask & ~enemyMask;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, range + 6f, envMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return cam.position + cam.forward * (range + 6f);
        }

        void BeginFacing(Vector3 point, float duration = -1f)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            faceDir = d.normalized;
            faceUntil = Time.time + (duration > 0f ? duration : faceTargetTime);
        }

        // ───────────────────────── Hit / Death ─────────────────────────

        void OnDamaged(DamageInfo info)
        {
            Visual?.PlayHit();
            cameraRig?.Shake(0.12f, 0.2f);
            if (info.knockback > 0f)
            {
                Vector3 push = info.direction;
                push.y = 0f;
                if (push.sqrMagnitude > 0.001f) motor.AddImpulse(push.normalized * info.knockback);
            }
        }

        /// <summary>리스폰 위치 갱신 (스테이지 입구 등)</summary>
        public void SetCheckpoint(Vector3 position, Quaternion rotation)
        {
            spawnPos = position;
            spawnRot = rotation;
        }

        /// <summary>물에 빠지는 등: 피해를 주고 체크포인트로 순간이동</summary>
        public void ReturnToCheckpoint(float damage)
        {
            if (health.IsDead) return;
            motor.Halt();
            body.position = spawnPos;
            body.rotation = spawnRot;
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            if (damage > 0f) health.TakeDamage(new DamageInfo { amount = damage, direction = Vector3.zero, source = null });
        }

        void OnDied() => StartCoroutine(RespawnRoutine());

        IEnumerator RespawnRoutine()
        {
            // 죽어 있는 동안 이동 로직이 꺼지므로, 마찰 0 몸체가 미끄러지지 않게 고정
            motor.Halt();
            motor.enabled = false;
            body.isKinematic = true;
            yield return new WaitForSeconds(respawnDelay);
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.position = spawnPos;
            body.rotation = spawnRot;
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            health.Revive();
            motor.enabled = true;
            comboIndex = 0;
        }
    }
}
