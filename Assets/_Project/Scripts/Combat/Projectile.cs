using LNStudy.CameraSystem;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 드론이 쏘는 탄 (일반 공격 볼트 / 스킬 유도 미사일 공용).
    /// 물리 Rigidbody 대신 매 프레임 SphereCast로 이동 구간을 검사해서, 빠른 탄도 얇은 물체를 뚫지 않는다.
    /// homingDelay 이후 목표(살아 있는 Health 또는 고정 지점) 쪽으로 turnRate만큼 방향을 꺾는다.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [SerializeField] float speed = 24f;
        [Tooltip("초당 꺾을 수 있는 각도 (0 = 직진)")]
        [SerializeField] float turnRate = 0f;
        [SerializeField] float acceleration = 60f;
        [SerializeField] float lifetime = 2.5f;
        [SerializeField] float radius = 0.1f;
        [SerializeField] GameObject hitVfx;
        [Tooltip("명중 시 카메라 흔들림 (일반/강공격)")]
        [SerializeField] Vector2 shake = new Vector2(0.03f, 0.1f);

        Health target;
        Vector3 fallbackPoint;
        bool hasFallback;
        DamageInfo damage;
        LayerMask hitMask;
        Vector3 velocity;
        float age, homingDelay;
        bool done;

        /// <summary>프리팹에서 생성해 발사한다.</summary>
        public static Projectile Fire(Projectile prefab, Vector3 position, Vector3 initialVelocity,
            Health target, Vector3? fallbackPoint, DamageInfo damage, LayerMask hitMask, float homingDelay = 0f)
        {
            var p = Instantiate(prefab, position, Quaternion.LookRotation(initialVelocity.sqrMagnitude > 0.001f ? initialVelocity : Vector3.forward));
            p.target = target;
            p.hasFallback = fallbackPoint.HasValue;
            p.fallbackPoint = fallbackPoint ?? Vector3.zero;
            p.damage = damage;
            p.hitMask = hitMask;
            p.velocity = initialVelocity;
            p.homingDelay = homingDelay;
            return p;
        }

        void Update()
        {
            if (done) return;
            float dt = Time.deltaTime;
            age += dt;
            if (age > lifetime) { Explode(transform.position, null); return; }

            bool targetAlive = target != null && !target.IsDead;
            Vector3 pos = transform.position;

            // 유도
            if (age >= homingDelay && (targetAlive || hasFallback))
            {
                Vector3 aim = targetAlive ? target.AimPoint : fallbackPoint;
                Vector3 to = aim - pos;

                // 유도 미사일이 목표 주위를 맴도는 것 방지: 충분히 가까우면 바로 명중 처리
                if (targetAlive && to.sqrMagnitude < 0.35f * 0.35f) { Explode(aim, target); return; }

                if (to.sqrMagnitude > 0.0001f)
                {
                    float turn = turnRate > 0f ? turnRate * Mathf.Deg2Rad * dt : 0f;
                    float targetSpeed = Mathf.MoveTowards(velocity.magnitude, speed, acceleration * dt);
                    velocity = Vector3.RotateTowards(velocity, to.normalized * targetSpeed, turn, acceleration * dt);
                }
            }
            else
            {
                velocity = velocity.normalized * Mathf.MoveTowards(velocity.magnitude, speed, acceleration * dt);
            }

            float step = velocity.magnitude * dt;
            if (step <= 0f) return;
            Vector3 dir = velocity / velocity.magnitude;

            if (Physics.SphereCast(pos, radius, dir, out RaycastHit hit, step, hitMask, QueryTriggerInteraction.Ignore))
            {
                Explode(hit.point, hit.collider.GetComponentInParent<Health>());
                return;
            }

            transform.SetPositionAndRotation(pos + dir * step, Quaternion.LookRotation(dir));
        }

        void Explode(Vector3 point, Health victim)
        {
            done = true;

            if (victim != null && !victim.IsDead)
            {
                var info = damage;
                info.point = point;
                info.direction = velocity.sqrMagnitude > 0.001f ? velocity.normalized : transform.forward;
                if (victim.TakeDamage(info))
                {
                    if (info.heavy) HitStop.Do(0.06f);
                    ThirdPersonCamera.Active?.Shake(info.heavy ? shake.y : shake.x, info.heavy ? 0.18f : 0.1f);
                }
            }

            if (hitVfx != null)
            {
                var fx = Instantiate(hitVfx, point, Quaternion.LookRotation(-transform.forward));
                Destroy(fx, 2f);
            }

            // 꼬리는 남겨서 자연스럽게 사라지게
            var trail = GetComponentInChildren<TrailRenderer>();
            if (trail != null)
            {
                trail.transform.SetParent(null, true);
                trail.emitting = false;
                Destroy(trail.gameObject, trail.time);
            }
            Destroy(gameObject);
        }
    }
}
