using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 방화 셔터 (머핀 타임의 무게 발판 퍼즐 재사용).
    /// PressurePlate(무게) → PuzzleMover(신호) 로 천장 속에서 내려온다. 사물함(Grabbable, 45kg)을 발판에 올려야 눌린다.
    /// 발판에서 사물함을 치우면 다시 올라간다 (latch 끔).
    /// - 완전히 내려와 있을 때만 NavMeshObstacle carving → 복도 한쪽이 끊긴다
    /// - 양은 끊긴 경로 끝(셔터 앞)에서 셔터를 두드린다 (Breakable). 체력이 0이 되면 넘어져 길이 열린다.
    /// 사물함 수가 한정돼 있으니 "교실 문을 막을지, 복도를 끊을지"를 고르게 된다.
    /// </summary>
    [RequireComponent(typeof(PuzzleMover), typeof(NavMeshObstacle), typeof(Breakable))]
    public class FireShutter : MonoBehaviour
    {
        [SerializeField] float toppleImpulse = 2.5f;
        [SerializeField] float debrisLifetime = 6f;

        PuzzleMover mover;
        NavMeshObstacle obstacle;
        Health health;
        Rigidbody body;
        Vector3 lastHitDir = Vector3.forward;
        bool wasClosed;

        /// <summary>완전히 내려와 길을 막고 있는지</summary>
        public bool Closed => !health.IsDead && mover.Progress >= 0.98f;

        void Awake()
        {
            mover = GetComponent<PuzzleMover>();
            obstacle = GetComponent<NavMeshObstacle>();
            health = GetComponent<Health>();
            body = GetComponent<Rigidbody>();
            obstacle.enabled = false;
        }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnBroken;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnBroken;
        }

        void Update()
        {
            bool closed = Closed;
            if (closed == wasClosed) return;
            wasClosed = closed;
            obstacle.enabled = closed; // 올라가 있을 때는 carving 없음
            if (closed)
            {
                ThirdPersonCamera.Active?.Shake(0.06f, 0.25f);
                StageHUD.Instance?.ShowToast("방화 셔터가 닫혔다", 1.5f);
            }
        }

        void OnDamaged(DamageInfo info)
        {
            lastHitDir = info.direction.sqrMagnitude > 0.001f ? info.direction : lastHitDir;
            Sfx.Play("clunk", info.point, 0.55f, Random.Range(0.75f, 0.9f)); // 철판 두드리는 소리
            DustFx.Puff(info.point, 2, 0.25f, 1f);
        }

        void OnBroken()
        {
            mover.enabled = false;
            obstacle.enabled = false;
            if (body != null)
            {
                body.isKinematic = false;
                body.constraints = RigidbodyConstraints.None;
                Vector3 dir = lastHitDir;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
                var col = GetComponent<Collider>();
                float top = col != null ? col.bounds.max.y : transform.position.y + 2f;
                body.AddForceAtPosition(dir.normalized * toppleImpulse * body.mass, new Vector3(transform.position.x, top, transform.position.z), ForceMode.Impulse);
            }
            Sfx.Play("thud", transform.position, 1f, 0.5f);
            Sfx.Play("clunk", transform.position, 1f, 0.6f);
            DustFx.Burst(transform.position, 26, DustFx.DustColor, 3.5f, 0.2f, 0.8f);
            ThirdPersonCamera.Active?.Shake(0.15f, 0.35f);
            HitStop.Do(0.06f);
            StageHUD.Instance?.ShowToast("셔터가 뚫렸다!", 1.5f);
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
