using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Interaction;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 사물함 바리케이드.
    /// - 플레이어가 E로 잡고 끌어 문 앞에 둔다 (PlayerGrab = FixedJoint)
    /// - 멈춰 있으면 NavMeshObstacle이 NavMesh에 구멍을 낸다(carving) → 양의 경로가 끊긴다
    ///   → 길이 막힌 양은 이 바리케이드를 대상으로 바꿔 들이받는다 (Breakable)
    /// - 끌고 다니는 동안은 carving을 끈다 (carveOnlyStationary) — 매 프레임 NavMesh를 다시 굽지 않게
    /// - 체력이 0이 되면 회전 고정을 풀고 맞은 방향으로 넘어진다 → carving 해제 → 길이 다시 열림
    /// - 플레이어만 옮길 수 있다: 평소엔 kinematic, 잡혀 있을 때만 dynamic.
    ///   (NavMeshAgent는 물리 힘이 무한대라 dynamic이면 양·친구가 지나가며 밀어 버리고, 날아온 양에도 밀린다)
    /// - 문 앞에 놓으면 그 문은 열고 닫을 수 없다 (SwingDoor.Jammed)
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Breakable), typeof(NavMeshObstacle))]
    public class Barricade : MonoBehaviour
    {
        static readonly System.Collections.Generic.List<Barricade> all = new();
        public static System.Collections.Generic.IReadOnlyList<Barricade> All => all;

        [SerializeField] float toppleImpulse = 4f;
        [SerializeField] float debrisLifetime = 6f;
        [SerializeField] Material dustMaterial;

        Rigidbody body;
        Health health;
        NavMeshObstacle obstacle;
        Grabbable grabbable;
        Vector3 lastHitDir = Vector3.forward;
        // 맞을 때 몸체를 흔들지 않는다: Transform이 움직이면 NavMeshObstacle이 "움직이는 중"으로 보고
        // carving을 풀어 버려(carveOnlyStationary) 그 순간 길이 열리고 모든 양의 경로가 다시 계산된다.

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            health = GetComponent<Health>();
            obstacle = GetComponent<NavMeshObstacle>();
            grabbable = GetComponent<Grabbable>();
            DustFx.Init(dustMaterial);
            body.isKinematic = true;
        }

        void OnEnable()
        {
            all.Add(this);
            health.Damaged += OnDamaged;
            health.Died += OnBroken;
            if (grabbable != null) grabbable.HeldChanged += OnHeldChanged;
        }

        void OnDisable()
        {
            all.Remove(this);
            health.Damaged -= OnDamaged;
            health.Died -= OnBroken;
            if (grabbable != null) grabbable.HeldChanged -= OnHeldChanged;
        }

        public bool Intact => !health.IsDead;

        void OnHeldChanged(bool held)
        {
            if (health.IsDead) return;
            if (held) body.isKinematic = false;
            else
            {
                body.linearVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        /// <summary>p에서 사물함 바닥 면까지의 수평 거리</summary>
        public float FlatDistance(Vector3 p)
        {
            var c = GetComponent<Collider>();
            Bounds b = c != null ? c.bounds : new Bounds(transform.position, Vector3.one * 0.6f);
            Vector3 q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
            q.y = p.y;
            return Vector3.Distance(p, q);
        }

        void OnDamaged(DamageInfo info)
        {
            lastHitDir = info.direction.sqrMagnitude > 0.001f ? info.direction : lastHitDir;
            Sfx.Play("thud", transform.position + Vector3.up, 0.5f, Random.Range(1.1f, 1.3f));
            DustFx.Puff(info.point, 3, 0.3f, 1.2f);
        }

        void OnBroken()
        {
            // 잡고 있던 중이면 놓게 한다
            if (grabbable != null)
            {
                foreach (var g in FindObjectsByType<PlayerGrab>(FindObjectsSortMode.None))
                    if (g.Current == grabbable) g.Release();
                grabbable.enabled = false;
            }

            obstacle.enabled = false; // 길이 다시 열린다
            body.constraints = RigidbodyConstraints.None;
            body.isKinematic = false;
            Vector3 dir = lastHitDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
            dir.Normalize();
            // 위쪽을 밀어 넘어뜨린다
            body.AddForceAtPosition(dir * toppleImpulse * body.mass, transform.position + Vector3.up * 2.2f, ForceMode.Impulse);

            Sfx.Play("thud", transform.position, 1f, 0.6f);
            Sfx.Play("clunk", transform.position, 0.8f, 0.8f);
            DustFx.Burst(transform.position + Vector3.up * 1.2f, 18, DustFx.DustColor, 3f, 0.18f, 0.7f);
            ThirdPersonCamera.Active?.Shake(0.1f, 0.25f);
            HitStop.Do(0.05f);
            StartCoroutine(Debris());
        }

        System.Collections.IEnumerator Debris()
        {
            yield return new WaitForSeconds(debrisLifetime);
            Vector3 s0 = transform.localScale;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                transform.localScale = s0 * (1f - t / 0.5f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
