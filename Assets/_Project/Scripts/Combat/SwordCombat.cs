using System;
using System.Collections;
using System.Collections.Generic;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Gimmicks;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 모드별 근접 전투 (CharacterSwitcher의 현재 외형 = 모드).
    ///  - 좌클릭: 평타 콤보 (타이밍 안에 다시 누르면 다음 단계)
    ///  - Q / 우클릭: 모드 스킬
    ///      Slam   대검: 크게 내려찍어 앞쪽 원형 범위에 큰 피해 + 넉백 + 화면 흔들림
    ///      Pierce 쌍검: 앞으로 순식간에 돌진하며 경로 위 몬스터를 전부 관통
    /// 애니메이션은 각 외형 Animator의 상태 이름을 직접 CrossFade 한다 (상태 이름은 빌더가 만든 컨트롤러와 맞춤).
    /// 공격 중에는 이동이 느려지고 방향이 고정되며, 가까운 적이 있으면 그쪽으로 몸을 돌려 친다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader))]
    public class SwordCombat : MonoBehaviour
    {
        public enum SkillKind { None, Slam, Pierce }

        [Serializable]
        public class Step
        {
            public string state = "Attack1";
            [Tooltip("다음 입력을 받을 수 있게 되기까지 (초)")] public float duration = 0.5f;
            [Tooltip("판정 시점 (초)")] public float hitTime = 0.2f;
            public float damage = 15f;
            public float reach = 1.4f;
            public float radius = 0.9f;
            [Range(30f, 360f)] public float arc = 140f;
            public float knockback = 2.5f;
            public float lunge = 1.5f;
            public bool heavy;
        }

        [Serializable]
        public class Mode
        {
            public string displayName = "Mode";
            public Step[] combo = new Step[0];
            public SkillKind skill;
            public string skillName = "";
            public string skillState = "Skill";
            public float skillDuration = 1f;
            public float skillHitTime = 0.5f;
            public float skillDamage = 50f;
            public float skillCooldown = 4f;
        }

        [SerializeField] Mode[] modes = new Mode[0];
        [SerializeField] LayerMask enemyMask;
        [SerializeField] LayerMask wallMask = 1;
        [SerializeField] float comboResetTime = 0.6f;
        [SerializeField] float autoAimRange = 4f;
        [SerializeField] float attackMoveMultiplier = 0.15f;
        [Header("Slam")]
        [SerializeField] float slamRadius = 2.4f;
        [SerializeField] float slamForward = 1.3f;
        [Header("Pierce")]
        [SerializeField] float pierceDistance = 6f;
        [SerializeField] float pierceTime = 0.2f;
        [SerializeField] float pierceWidth = 0.75f;
        [SerializeField] TrailRenderer dashTrail;
        [Header("Respawn")]
        [SerializeField] float reviveDelay = 2.5f;
        [SerializeField] Material dustMaterial;

        public Mode CurrentMode => switcher != null && switcher.ActiveIndex >= 0 && switcher.ActiveIndex < modes.Length ? modes[switcher.ActiveIndex] : null;
        public float SkillCooldownRemaining => Mathf.Max(0f, nextSkillTime - Time.time);
        public bool Busy => routine != null;
        public bool Locked { get; set; }

        PlayerMotor motor;
        PlayerInputReader input;
        CharacterSwitcher switcher;
        Rigidbody body;
        Health health;
        Coroutine routine;
        int comboIndex;
        float lastComboEnd = -999f;
        float nextSkillTime;
        bool queued;
        int playerLayer, enemyLayer;
        readonly Collider[] buffer = new Collider[48];
        readonly RaycastHit[] castBuffer = new RaycastHit[48];
        readonly HashSet<Health> hitSet = new();
        Vector3 spawnPoint;
        PlayerGrab grab;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            switcher = GetComponent<CharacterSwitcher>();
            body = GetComponent<Rigidbody>();
            health = GetComponent<Health>();
            grab = GetComponent<PlayerGrab>();
            playerLayer = gameObject.layer;
            enemyLayer = LayerMask.NameToLayer("Enemy");
            spawnPoint = transform.position;
            DustFx.Init(dustMaterial);
            if (dashTrail != null) dashTrail.emitting = false;
        }

        void OnEnable()
        {
            if (switcher != null) switcher.Switched += OnSwitched;
            if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
        }

        void OnDisable()
        {
            if (switcher != null) switcher.Switched -= OnSwitched;
            if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
            EndAction();
        }

        Animator Anim => switcher != null && switcher.Active != null ? switcher.Active.HumanoidAnimator : GetComponentInChildren<Animator>();

        void OnSwitched(int index)
        {
            // 모드를 바꾸면 하던 동작은 끊는다
            if (routine != null) { StopCoroutine(routine); routine = null; }
            EndAction();
            comboIndex = 0;
            Sfx.Play("switch", transform.position, 0.7f, 0.9f + index * 0.15f);
            DustFx.Burst(transform.position + Vector3.up * 0.7f, 18, new Color(0.6f, 0.85f, 1f), 2.4f, 0.12f, 0.5f);
        }

        void Update()
        {
            var mode = CurrentMode;
            bool canAct = mode != null && !Locked && (health == null || !health.IsDead) && !motor.IsClimbing
                          && (grab == null || grab.Current == null) // 사물함을 잡고 있으면 공격 안 함
                          && (ThirdPersonCamera.Active == null || ThirdPersonCamera.Active.IsCursorLocked);
            if (!canAct) { input.ClearAttack(); input.ClearSkill(); return; }

            if (input.HasBufferedSkill(0.2f))
            {
                if (routine == null && mode.skill != SkillKind.None && Time.time >= nextSkillTime)
                {
                    input.ClearSkill();
                    routine = StartCoroutine(mode.skill == SkillKind.Slam ? SlamRoutine(mode) : PierceRoutine(mode));
                }
                else if (mode.skill == SkillKind.None || routine == null) input.ClearSkill();
            }

            if (input.HasBufferedAttack(0.25f))
            {
                if (routine == null)
                {
                    input.ClearAttack();
                    if (Time.time - lastComboEnd > comboResetTime || comboIndex >= mode.combo.Length) comboIndex = 0;
                    if (mode.combo.Length > 0) routine = StartCoroutine(AttackRoutine(mode));
                }
                else queued = true; // 공격 중 클릭 → 다음 단계 예약
            }
        }

        // ───────────────────────── Common ─────────────────────────

        void BeginAction()
        {
            motor.SetFacingLock(this, true);
            motor.SpeedMultiplier = attackMoveMultiplier;
            motor.CanJump = false;
        }

        void EndAction()
        {
            if (motor == null) return;
            motor.SetFacingLock(this, false);
            motor.SpeedMultiplier = 1f;
            motor.CanJump = true;
            if (dashTrail != null) dashTrail.emitting = false;
        }

        /// <summary>가까운 적 쪽(없으면 카메라 정면)으로 몸을 돌린다</summary>
        Vector3 FaceAim()
        {
            Vector3 dir = transform.forward;
            var cam = ThirdPersonCamera.Active;
            Vector2 mv = input.Move;
            if (cam != null && mv.sqrMagnitude > 0.01f)
            {
                Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                Vector3 r = Vector3.Cross(Vector3.up, f);
                dir = (f * mv.y + r * mv.x).normalized;
            }

            var target = MonsterBrain.FindNearest(transform.position, autoAimRange);
            if (target != null)
            {
                Vector3 to = target.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(dir, to) < 100f) dir = to.normalized;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();
            body.MoveRotation(Quaternion.LookRotation(dir));
            transform.rotation = Quaternion.LookRotation(dir);
            return dir;
        }

        void Play(string state, float fade = 0.06f)
        {
            var a = Anim;
            if (a != null && !string.IsNullOrEmpty(state)) a.CrossFadeInFixedTime(state, fade, 0);
        }

        int DamageArea(Vector3 center, float radius, Vector3 forward, float arc, float damage, float knockback, bool heavy)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, buffer, enemyMask, QueryTriggerInteraction.Ignore);
            int hits = 0;
            hitSet.Clear();
            for (int i = 0; i < n; i++)
            {
                var h = buffer[i].GetComponentInParent<Health>();
                if (h == null || h.IsDead || h == health || !hitSet.Add(h)) continue;
                Vector3 to = h.transform.position - transform.position;
                to.y = 0f;
                if (arc < 359f && to.sqrMagnitude > 0.04f && Vector3.Angle(forward, to) > arc * 0.5f) continue;
                if (Hit(h, to.sqrMagnitude > 0.01f ? to.normalized : forward, damage, knockback, heavy)) hits++;
            }
            return hits;
        }

        bool Hit(Health h, Vector3 dir, float damage, float knockback, bool heavy)
        {
            bool ok = h.TakeDamage(new DamageInfo
            {
                amount = damage,
                point = h.AimPoint,
                direction = dir,
                knockback = knockback,
                heavy = heavy,
                source = gameObject,
            });
            if (ok) DustFx.Burst(h.AimPoint, heavy ? 14 : 8, new Color(1f, 0.95f, 0.75f), heavy ? 3.5f : 2.5f, 0.1f, 0.35f);
            return ok;
        }

        // ───────────────────────── Basic combo ─────────────────────────

        IEnumerator AttackRoutine(Mode mode)
        {
            BeginAction();
            while (true)
            {
                var step = mode.combo[comboIndex];
                queued = false;
                Vector3 fwd = FaceAim();
                Play(step.state);
                if (step.lunge > 0f) motor.AddImpulse(fwd * step.lunge, 0.12f);
                Sfx.Play("skid", transform.position, 0.45f, step.heavy ? 0.8f : 1.3f + comboIndex * 0.08f);

                float t = 0f;
                bool didHit = false;
                while (t < step.duration)
                {
                    t += Time.deltaTime;
                    if (!didHit && t >= step.hitTime)
                    {
                        didHit = true;
                        Vector3 center = transform.position + fwd * (step.reach * 0.6f) + Vector3.up * 0.6f;
                        int hits = DamageArea(center, step.radius, fwd, step.arc, step.damage, step.knockback, step.heavy);
                        if (hits > 0)
                        {
                            Sfx.Play("thud", center, 0.7f, step.heavy ? 0.9f : 1.4f);
                            HitStop.Do(step.heavy ? 0.07f : 0.04f);
                            ThirdPersonCamera.Active?.Shake(step.heavy ? 0.1f : 0.05f, 0.12f);
                        }
                    }
                    if (input.HasBufferedAttack(0.25f) && t > step.hitTime) { queued = true; input.ClearAttack(); }
                    yield return null;
                }

                comboIndex++;
                if (!queued || comboIndex >= mode.combo.Length) break;
            }
            if (comboIndex >= mode.combo.Length) comboIndex = 0;
            lastComboEnd = Time.time;
            EndAction();
            routine = null;
        }

        // ───────────────────────── Skills ─────────────────────────

        /// <summary>대검 내려찍기: 앞쪽 원형 범위 강타 + 바닥 먼지 고리 + 큰 흔들림</summary>
        IEnumerator SlamRoutine(Mode mode)
        {
            BeginAction();
            motor.SpeedMultiplier = 0f;
            nextSkillTime = Time.time + mode.skillCooldown;
            Vector3 fwd = FaceAim();
            Play(mode.skillState, 0.05f);
            Sfx.Play("jump", transform.position, 0.6f, 0.7f);

            yield return new WaitForSeconds(mode.skillHitTime);

            Vector3 center = transform.position + fwd * slamForward;
            int hits = DamageArea(center + Vector3.up * 0.4f, slamRadius, fwd, 360f, mode.skillDamage, 7f, true);
            Sfx.Play("thud", center, 1f, 0.6f);
            Sfx.Play("clunk", center, 0.9f, 0.7f);
            ThirdPersonCamera.Active?.Shake(0.22f, 0.35f);
            HitStop.Do(hits > 0 ? 0.1f : 0.04f);
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * slamRadius * 0.6f;
                DustFx.Puff(p + Vector3.up * 0.05f, 2, 0.5f, 2.2f);
            }
            DustFx.Burst(center + Vector3.up * 0.2f, 24, new Color(0.7f, 0.9f, 1f), 4f, 0.14f, 0.5f);

            yield return new WaitForSeconds(Mathf.Max(0f, mode.skillDuration - mode.skillHitTime));
            EndAction();
            routine = null;
        }

        /// <summary>쌍검 관통: 벽까지 또는 pierceDistance만큼 순식간에 돌진, 지나간 길의 적 전부 타격</summary>
        IEnumerator PierceRoutine(Mode mode)
        {
            BeginAction();
            motor.SpeedMultiplier = 0f;
            nextSkillTime = Time.time + mode.skillCooldown;
            Vector3 fwd = FaceAim();
            Play(mode.skillState, 0.04f);
            yield return new WaitForSeconds(0.08f); // 짧은 예비 동작

            // 벽 앞에서 멈출 거리
            Vector3 start = body.position;
            float dist = pierceDistance;
            float r = 0.25f;
            if (Physics.SphereCast(start + Vector3.up * 0.6f, r, fwd, out var wall, pierceDistance, wallMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, wall.distance - 0.15f);
            Vector3 end = start + fwd * dist;

            // 적을 통과하도록 충돌을 잠시 끈다
            bool ignore = enemyLayer >= 0;
            if (ignore) Physics.IgnoreLayerCollision(playerLayer, enemyLayer, true);
            motor.enabled = false;
            if (dashTrail != null) { dashTrail.Clear(); dashTrail.emitting = true; }
            Sfx.Play("skid", start, 0.9f, 1.6f);

            hitSet.Clear();
            int hits = 0;
            float t = 0f;
            Vector3 prev = start;
            while (t < pierceTime)
            {
                t += Time.fixedDeltaTime;
                float k = Mathf.Clamp01(t / pierceTime);
                Vector3 pos = Vector3.Lerp(start, end, 1f - (1f - k) * (1f - k));
                body.linearVelocity = Vector3.zero;
                body.MovePosition(pos);

                // 이번 구간에 스친 적
                int n = Physics.OverlapCapsuleNonAlloc(prev + Vector3.up * 0.3f, pos + Vector3.up * 0.9f, pierceWidth, buffer, enemyMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = buffer[i].GetComponentInParent<Health>();
                    if (h == null || h.IsDead || !hitSet.Add(h)) continue;
                    if (Hit(h, fwd, mode.skillDamage, 4f, true))
                    {
                        hits++;
                        Sfx.Play("pop", h.transform.position, 0.8f, 1.2f + hits * 0.1f);
                    }
                }
                if (k % 0.34f < 0.1f) DustFx.Puff(pos, 2, 0.3f, 1.4f);
                prev = pos;
                yield return new WaitForFixedUpdate();
            }

            body.position = end;
            motor.enabled = true;
            if (ignore) Physics.IgnoreLayerCollision(playerLayer, enemyLayer, false);
            if (hits > 0)
            {
                HitStop.Do(0.08f);
                ThirdPersonCamera.Active?.Shake(0.12f, 0.2f);
                Sfx.Play("thud", end, 0.8f, 1.3f);
            }
            DustFx.Puff(end, 6, 0.4f, 1.8f);
            if (dashTrail != null) dashTrail.emitting = false;

            yield return new WaitForSeconds(Mathf.Max(0f, mode.skillDuration - pierceTime - 0.08f));
            EndAction();
            routine = null;
        }

        // ───────────────────────── Damage taken ─────────────────────────

        void OnDamaged(DamageInfo info)
        {
            Vector3 push = info.direction;
            push.y = 0f;
            if (push.sqrMagnitude > 0.001f) motor.AddImpulse(push.normalized * info.knockback);
            switcher?.Active?.PlayHit();
            ThirdPersonCamera.Active?.Shake(0.08f, 0.15f);
            Sfx.Play("land", transform.position, 0.6f, 1.4f);
        }

        void OnDied() => StartCoroutine(ReviveRoutine());

        IEnumerator ReviveRoutine()
        {
            if (routine != null) { StopCoroutine(routine); routine = null; }
            EndAction();
            Locked = true;
            motor.enabled = false;
            body.linearVelocity = Vector3.zero;
            StageHUD.Instance?.ShowToast("쓰러졌다! 잠시 후 다시 일어난다", 2f);
            var a = Anim;
            var juice = a != null ? a.GetComponent<CharacterJuice>() : null;
            if (juice != null) juice.enabled = false; // 기울기 덮어쓰기 방지
            if (a != null) a.transform.localRotation = Quaternion.Euler(-80f, 0f, 0f);
            yield return new WaitForSeconds(reviveDelay);
            if (a != null) a.transform.localRotation = Quaternion.identity;
            if (juice != null) juice.enabled = true;
            body.position = spawnPoint;
            transform.position = spawnPoint;
            motor.enabled = true;
            health.Revive();
            Locked = false;
        }

        /// <summary>승리 연출 등 외부에서 상태 하나를 재생</summary>
        public void PlayState(string state)
        {
            if (routine != null) { StopCoroutine(routine); routine = null; }
            EndAction();
            Play(state, 0.15f);
        }
    }
}
