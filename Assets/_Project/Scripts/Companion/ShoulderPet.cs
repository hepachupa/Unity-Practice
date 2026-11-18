using System.Collections;
using System.Collections.Generic;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using UnityEngine;

namespace LNStudy.Companion
{
    /// <summary>
    /// 어깨 위에 떠서 주인을 따라다니는 펫 드론 (카이사 어깨 포드 느낌).
    /// - 위치: 어깨 뼈 위쪽 목표점을 SmoothDamp로 늦게 따라가 관성이 생기게 한다.
    /// - 둥실거림: 좌우 펫의 위상을 다르게 줘서 똑같이 움직이지 않게 한다.
    /// - 회전: 평소엔 주인이 보는 방향, 근처에 몬스터가 있으면 그쪽을 본다. 이동 가속에 따라 기울어진다.
    /// 모델은 자식으로 두고, 이 오브젝트는 씬 루트에 둔다 (주인 Transform에 붙이면 지연이 사라진다).
    /// 공격 명령(Strike)을 받으면 따라다니기를 멈추고 적에게 돌진해 들이받은 뒤 다시 어깨로 돌아온다 (엘소드 이브의 모비·레미 느낌).
    /// </summary>
    public class ShoulderPet : MonoBehaviour
    {
        public enum Side { Left = -1, Right = 1 }

        [Header("Owner")]
        [SerializeField] Transform owner;
        [Tooltip("주인 Animator (휴머노이드). 있으면 어깨 뼈를 기준으로 높이를 잡는다")]
        [SerializeField] Animator ownerAnimator;
        [SerializeField] Side side = Side.Left;

        [Header("Placement")]
        [Tooltip("어깨에서 바깥쪽으로 떨어진 거리")]
        [SerializeField] float sideOffset = 0.32f;
        [Tooltip("어깨 위로 뜬 높이")]
        [SerializeField] float heightOffset = 0.28f;
        [Tooltip("+면 앞쪽, -면 뒤쪽")]
        [SerializeField] float forwardOffset = -0.08f;
        [Tooltip("어깨 뼈를 못 찾을 때 쓰는 발 기준 어깨 높이")]
        [SerializeField] float fallbackShoulderHeight = 1.3f;

        [Header("Follow")]
        [SerializeField] float followSmoothTime = 0.2f;
        [SerializeField] float maxFollowSpeed = 25f;
        [Tooltip("이보다 멀어지면 순간이동 (리스폰·텔레포트 대비)")]
        [SerializeField] float teleportDistance = 14f;

        [Header("Hover")]
        [SerializeField] float bobAmplitude = 0.04f;
        [SerializeField] float bobFrequency = 1.6f;
        [SerializeField] float swayAmplitude = 0.025f;

        [Header("Rotation")]
        [SerializeField] float turnSharpness = 8f;
        [Tooltip("이동 가속에 따라 기우는 정도 (도 / (m/s))")]
        [SerializeField] float bankPerSpeed = 6f;
        [SerializeField] float maxBank = 25f;
        [Tooltip("모델 정면이 +Z가 아니면 보정 (도)")]
        [SerializeField] float modelYawOffset;

        [Header("Targeting")]
        [SerializeField] float lookAtEnemyRange = 9f;

        Transform shoulderBone;
        Vector3 followVel;
        Vector3 lastPos;
        Vector3 smoothedVel;
        float phase;

        [Header("Combat")]
        [Tooltip("모델 중심에서 총구까지 (로컬 +Z)")]
        [SerializeField] float muzzleForward = 0.18f;
        [SerializeField] float recoilKick = 1.8f;

        [Header("Strike")]
        [Tooltip("돌진할 때 켜지는 꼬리 (선택)")]
        [SerializeField] TrailRenderer dashTrail;
        [SerializeField] GameObject strikeVfx;
        [SerializeField] float maxStrikeDistance = 7f;

        public enum StrikeStyle { Dash, Cross, Spin }

        /// <summary>펫 공격 명령 한 건</summary>
        public struct StrikeOrder
        {
            public StrikeStyle style;
            public Health target;          // 없으면 point로
            public Vector3 point;
            public DamageInfo damage;
            public LayerMask enemyMask;
            public float hitRadius;
            public int hits;               // Spin일 때 타수
            public float dashTime;
        }

        Vector3? aimOverride;
        float aimOverrideUntil;
        Coroutine strikeRoutine;
        readonly Collider[] hitBuffer = new Collider[16];
        readonly HashSet<Health> hitThisSwing = new();

        /// <summary>공격 중이면 true (따라다니기 정지)</summary>
        public bool IsStriking => strikeRoutine != null;

        public Transform Owner => owner;
        public Side PetSide => side;
        public Vector3 MuzzlePosition => transform.position + transform.forward * muzzleForward;

        /// <summary>공격 중 잠깐 특정 지점을 조준하게 한다</summary>
        public void AimAt(Vector3 point, float duration)
        {
            aimOverride = point;
            aimOverrideUntil = Time.time + duration;
        }

        /// <summary>발사 반동: 쏜 반대 방향으로 살짝 밀려났다가 SmoothDamp로 돌아온다</summary>
        public void Kick(Vector3 fireDirection) => followVel -= fireDirection.normalized * recoilKick;

        /// <summary>주인 외형이 바뀌었을 때 (캐릭터 교체): 어깨 뼈 또는 고정 높이를 다시 잡는다</summary>
        public void SetOwnerVisual(Animator anim, float shoulderHeight)
        {
            ownerAnimator = anim;
            fallbackShoulderHeight = shoulderHeight;
            ResolveShoulder();
        }

        public void Setup(Transform newOwner, Animator anim, Side newSide)
        {
            owner = newOwner;
            ownerAnimator = anim;
            side = newSide;
            ResolveShoulder();
            Snap();
        }

        void Awake()
        {
            // 펫 모델의 충돌체가 주인 캡슐과 겹치면 매 프레임 주인을 밀어낸다 → 전부 끈다
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

            // 좌우가 같은 리듬으로 움직이지 않도록 위상을 다르게
            phase = side == Side.Left ? 0f : Mathf.PI * 0.65f;
            ResolveShoulder();
        }

        void Start() => Snap();

        void ResolveShoulder()
        {
            shoulderBone = null;
            if (ownerAnimator == null || !ownerAnimator.isHuman) return;
            var bone = side == Side.Left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            shoulderBone = ownerAnimator.GetBoneTransform(bone);
        }

        Vector3 AnchorPoint()
        {
            Vector3 right = owner.right;
            Vector3 fwd = owner.forward;
            // 어깨 높이는 뼈를 따르되(웅크리기·점프 반영), 좌우 위치는 루트 기준으로 잡아 팔 흔들림을 따라가지 않게 한다
            float shoulderY = shoulderBone != null ? shoulderBone.position.y : owner.position.y + fallbackShoulderHeight;
            Vector3 basePos = new Vector3(owner.position.x, shoulderY, owner.position.z);
            return basePos
                   + right * ((int)side * sideOffset)
                   + Vector3.up * heightOffset
                   + fwd * forwardOffset;
        }

        void Snap()
        {
            if (owner == null) return;
            transform.position = AnchorPoint();
            transform.rotation = Quaternion.LookRotation(owner.forward) * Quaternion.Euler(0f, modelYawOffset, 0f);
            lastPos = transform.position;
            followVel = smoothedVel = Vector3.zero;
        }

        // ───────────────────────── Strike ─────────────────────────

        /// <summary>공격 명령. 진행 중인 공격이 있으면 끊고 현재 위치에서 새로 시작한다.</summary>
        public void Strike(StrikeOrder order)
        {
            if (strikeRoutine != null) StopCoroutine(strikeRoutine);
            strikeRoutine = StartCoroutine(StrikeRoutine(order));
        }

        Vector3 TargetPoint(StrikeOrder o) =>
            o.target != null && !o.target.IsDead ? o.target.AimPoint : o.point;

        IEnumerator StrikeRoutine(StrikeOrder o)
        {
            if (dashTrail != null) { dashTrail.Clear(); dashTrail.emitting = true; }
            float sideSign = (int)side;

            Vector3 ApproachPoint(float standoff, float sideAmount)
            {
                Vector3 aim = Aim(o);
                Vector3 dir = aim - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = owner.forward;
                dir.Normalize();
                Vector3 sideDir = Vector3.Cross(Vector3.up, dir);
                return aim - dir * standoff + sideDir * (sideSign * sideAmount) + Vector3.up * 0.1f;
            }

            // 1) 돌진: 빠르게 출발해서 감속 (Cross는 좌우로 벌어진 지점, 나머지는 정면)
            float sideAmount = o.style == StrikeStyle.Cross ? 0.9f : 0f;
            yield return MoveTo(() => ApproachPoint(0.75f, sideAmount), Mathf.Max(0.05f, o.dashTime), true);

            if (o.style == StrikeStyle.Spin)
            {
                // 2-a) 목표 주위를 한 바퀴 돌며 여러 번 타격 → 마지막에 들이받기
                int hits = Mathf.Max(1, o.hits);
                float spinTime = 0.4f;
                Vector3 c0 = Aim(o);
                float startAngle = Mathf.Atan2(transform.position.z - c0.z, transform.position.x - c0.x);
                float dir = sideSign;
                int dealt = 0;
                for (float t = 0f; t < spinTime; t += Time.deltaTime)
                {
                    float k = t / spinTime;
                    float ang = startAngle + dir * k * Mathf.PI * 2f;
                    Vector3 c = Aim(o);
                    Vector3 pos = c + new Vector3(Mathf.Cos(ang), 0.05f, Mathf.Sin(ang)) * 0.85f;
                    FaceTowards(c, pos - transform.position);
                    transform.position = pos;
                    if (dealt < hits - 1 && k >= (dealt + 1f) / hits)
                    {
                        var light = o.damage;
                        light.heavy = false;
                        light.amount *= 0.5f;
                        DealDamage(o, light, true);
                        dealt++;
                    }
                    yield return null;
                }
                yield return Lunge(o, o.damage);
            }
            else
            {
                // 2-b) 들이받기
                yield return Lunge(o, o.damage);
            }

            // 3) 잠깐 머문 뒤 복귀 (복귀는 LateUpdate의 SmoothDamp가 담당)
            yield return new WaitForSeconds(0.06f);
            if (dashTrail != null) dashTrail.emitting = false;
            followVel = Vector3.zero;
            lastPos = transform.position;
            strikeRoutine = null;
        }

        IEnumerator Lunge(StrikeOrder o, DamageInfo dmg)
        {
            Vector3 back = transform.position;
            Vector3 aim = Aim(o);
            Vector3 dir = (aim - back);
            Vector3 contact = aim - dir.normalized * 0.25f;

            yield return MoveTo(() => contact, 0.05f, false);
            DealDamage(o, dmg, false);
            // 반동으로 튕겨나옴
            Vector3 recoil = contact - dir.normalized * 0.7f + Vector3.up * 0.25f;
            yield return MoveTo(() => recoil, 0.1f, true);
        }

        /// <summary>목표 지점 (사거리 제한: 주인에게서 너무 먼 목표는 사거리 끝까지만)</summary>
        Vector3 Aim(StrikeOrder o)
        {
            Vector3 p = TargetPoint(o);
            Vector3 from = owner.position + Vector3.up * 1.2f;
            Vector3 d = p - from;
            return d.magnitude > maxStrikeDistance ? from + d.normalized * maxStrikeDistance : p;
        }

        IEnumerator MoveTo(System.Func<Vector3> target, float duration, bool easeOut)
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                k = easeOut ? 1f - (1f - k) * (1f - k) : k * k;
                Vector3 goal = target();
                Vector3 next = Vector3.LerpUnclamped(start, goal, k);
                FaceTowards(goal, next - transform.position);
                transform.position = next;
                yield return null;
            }
            transform.position = target();
        }

        void FaceTowards(Vector3 point, Vector3 moveDelta)
        {
            Vector3 d = point - transform.position;
            if (d.sqrMagnitude < 0.0001f) d = moveDelta;
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
        }

        void DealDamage(StrikeOrder o, DamageInfo dmg, bool allowRepeat)
        {
            hitThisSwing.Clear(); // 한 번의 판정 안에서만 중복 방지 (회전 공격은 판정마다 다시 맞음)
            Vector3 center = transform.position + transform.forward * 0.25f;
            int n = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.3f, o.hitRadius), hitBuffer, o.enemyMask, QueryTriggerInteraction.Ignore);

            // 지정 대상은 판정 범위 밖으로 살짝 벗어나도 맞힌다 (빗나감 방지)
            bool hitAny = false;
            if (o.target != null && !o.target.IsDead && Vector3.Distance(o.target.AimPoint, center) < 1.4f)
                hitAny |= Apply(o.target, dmg, center);
            for (int i = 0; i < n; i++)
            {
                var h = hitBuffer[i].GetComponentInParent<Health>();
                if (h == null || h == o.target || hitThisSwing.Contains(h)) continue;
                hitAny |= Apply(h, dmg, center);
            }

            if (hitAny)
            {
                if (strikeVfx != null) Destroy(Instantiate(strikeVfx, center, transform.rotation), 2f);
                if (dmg.heavy) HitStop.Do(0.07f);
                ThirdPersonCamera.Active?.Shake(dmg.heavy ? 0.12f : 0.04f, dmg.heavy ? 0.2f : 0.1f);
            }
        }

        bool Apply(Health h, DamageInfo dmg, Vector3 point)
        {
            hitThisSwing.Add(h);
            dmg.point = point;
            Vector3 d = h.transform.position - owner.position;
            d.y = 0f;
            dmg.direction = d.sqrMagnitude > 0.001f ? d.normalized : transform.forward;
            return h.TakeDamage(dmg);
        }

        void LateUpdate()
        {
            if (owner == null) return;
            if (strikeRoutine != null) return; // 공격 중에는 코루틴이 위치·회전을 직접 제어
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            Vector3 anchor = AnchorPoint();
            if ((anchor - transform.position).sqrMagnitude > teleportDistance * teleportDistance)
            {
                Snap();
                return;
            }

            float t = Time.time * bobFrequency * Mathf.PI * 2f + phase;
            Vector3 hover = Vector3.up * (Mathf.Sin(t) * bobAmplitude)
                            + owner.right * (Mathf.Sin(t * 0.5f) * swayAmplitude);

            Vector3 pos = Vector3.SmoothDamp(transform.position, anchor + hover, ref followVel,
                followSmoothTime, maxFollowSpeed, dt);
            transform.position = pos;

            // 실제 이동 속도로 기울기 계산
            Vector3 vel = (pos - lastPos) / dt;
            lastPos = pos;
            smoothedVel = Vector3.Lerp(smoothedVel, vel, 1f - Mathf.Exp(-10f * dt));

            Vector3 lookDir = owner.forward;
            var enemy = MonsterBrain.FindNearest(pos, lookAtEnemyRange);
            if (aimOverride.HasValue && Time.time < aimOverrideUntil)
            {
                Vector3 toAim = aimOverride.Value - pos;
                if (toAim.sqrMagnitude > 0.01f) lookDir = toAim;
            }
            else if (enemy != null)
            {
                Vector3 toEnemy = enemy.AimPoint - pos;
                if (toEnemy.sqrMagnitude > 0.01f) lookDir = toEnemy;
            }

            Quaternion look = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            Vector3 localVel = Quaternion.Inverse(look) * smoothedVel;
            float roll = Mathf.Clamp(-localVel.x * bankPerSpeed, -maxBank, maxBank);
            float pitchTilt = Mathf.Clamp(localVel.z * bankPerSpeed * 0.6f, -maxBank, maxBank);
            Quaternion targetRot = look * Quaternion.Euler(pitchTilt, modelYawOffset, roll);

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSharpness * dt));
        }

        void OnDrawGizmosSelected()
        {
            if (owner == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(AnchorPoint(), 0.08f);
            Gizmos.DrawWireSphere(transform.position, lookAtEnemyRange);
        }
    }
}
