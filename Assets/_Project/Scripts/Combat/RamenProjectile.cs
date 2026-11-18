using LNStudy.CameraSystem;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 포물선으로 날아가는 라멘 그릇. 실제 Rigidbody 중력으로 날아가고(앵그리버드처럼),
    /// 무언가에 처음 부딪히면 국물이 터지며 범위 피해 + 주변 물리 물체를 밀어낸다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RamenProjectile : MonoBehaviour
    {
        [SerializeField] float splashRadius = 1.8f;
        [SerializeField] float centerDamage = 30f;
        [SerializeField] float edgeDamage = 12f;
        [SerializeField] float knockback = 3f;
        [Tooltip("범위 안 Rigidbody(상자 등)에 주는 폭발력")]
        [SerializeField] float explosionForce = 6f;
        [SerializeField] float lifetime = 6f;
        [SerializeField] GameObject splashVfx;

        Rigidbody body;
        LayerMask hitMask;
        GameObject owner;
        bool exploded;
        float age;
        readonly Collider[] buffer = new Collider[32];

        public static RamenProjectile Throw(RamenProjectile prefab, Vector3 position, Vector3 velocity,
            LayerMask hitMask, GameObject owner)
        {
            var r = Instantiate(prefab, position, Quaternion.LookRotation(Vector3.ProjectOnPlane(velocity, Vector3.up).normalized + Vector3.up * 0.001f));
            r.hitMask = hitMask;
            r.owner = owner;
            r.body = r.GetComponent<Rigidbody>();
            r.body.linearVelocity = velocity;
            // 앞으로 굴러가듯 회전 (진행 방향 기준 옆축)
            Vector3 side = Vector3.Cross(Vector3.up, velocity.normalized);
            r.body.angularVelocity = side * Random.Range(6f, 10f) + Random.insideUnitSphere * 2f;

            // 던진 사람과는 부딪히지 않게
            if (owner != null)
                foreach (var oc in owner.GetComponentsInChildren<Collider>())
                    foreach (var mc in r.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(oc, mc);
            return r;
        }

        void Awake() => body = GetComponent<Rigidbody>();

        void Update()
        {
            age += Time.deltaTime;
            if (!exploded && age > lifetime) Explode(transform.position);
        }

        void OnCollisionEnter(Collision c)
        {
            if (exploded) return;
            if (owner != null && c.transform.IsChildOf(owner.transform)) return;
            Explode(c.contacts.Length > 0 ? c.contacts[0].point : transform.position);
        }

        void Explode(Vector3 point)
        {
            exploded = true;
            bool hitEnemy = false;

            int n = Physics.OverlapSphereNonAlloc(point, splashRadius, buffer, hitMask | Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var damaged = new System.Collections.Generic.HashSet<Health>();
            var pushed = new System.Collections.Generic.HashSet<Rigidbody>();
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                if (owner != null && col.transform.IsChildOf(owner.transform)) continue;
                if (col.attachedRigidbody == body) continue;

                var h = col.GetComponentInParent<Health>();
                if (h != null && !h.IsDead && damaged.Add(h) && ((1 << col.gameObject.layer) & hitMask) != 0)
                {
                    float d = Vector3.Distance(point, col.ClosestPoint(point));
                    float k = 1f - Mathf.Clamp01(d / splashRadius);
                    Vector3 dir = h.transform.position - point;
                    dir.y = 0f;
                    hitEnemy |= h.TakeDamage(new DamageInfo
                    {
                        amount = Mathf.Lerp(edgeDamage, centerDamage, k),
                        point = point,
                        direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward,
                        knockback = knockback * (0.5f + 0.5f * k),
                        heavy = k > 0.5f,
                        source = owner,
                    });
                }

                var rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic && pushed.Add(rb) && h == null)
                    rb.AddExplosionForce(explosionForce, point, splashRadius, 0.4f, ForceMode.Impulse);
            }

            LNStudy.Gimmicks.RamenButton.NotifyImpact(point, splashRadius);
            if (splashVfx != null) Destroy(Instantiate(splashVfx, point, Quaternion.identity), 3f);
            if (hitEnemy) HitStop.Do(0.05f);
            ThirdPersonCamera.Active?.Shake(hitEnemy ? 0.12f : 0.06f, 0.18f);
            Destroy(gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, splashRadius);
        }
    }
}
