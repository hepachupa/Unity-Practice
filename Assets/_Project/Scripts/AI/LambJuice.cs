using System.Collections;
using LNStudy.Combat;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.AI
{
    /// <summary>
    /// 양 몬스터 손맛: 등장할 때 뿅 커지며 털 뭉치, 맞으면 통 찌그러지고 털이 날림,
    /// 쓰러지면 털이 크게 터지고 작아지며 사라짐.
    /// </summary>
    public class LambJuice : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] Material dustMaterial;
        [SerializeField] float spawnTime = 0.35f;

        static readonly Color Wool = new Color(1f, 0.98f, 0.94f, 1f);

        Health health;
        Vector3 baseScale;
        float squash, squashVel;

        void Awake()
        {
            health = GetComponent<Health>();
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) baseScale = visual.localScale;
            DustFx.Init(dustMaterial);
        }

        void OnEnable()
        {
            if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
            StartCoroutine(SpawnPop());
        }

        void OnDisable()
        {
            if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        }

        IEnumerator SpawnPop()
        {
            if (visual == null) yield break;
            Sfx.Play("pop", transform.position, 0.5f, 0.8f);
            DustFx.Burst(transform.position + Vector3.up * 0.5f, 14, Wool, 2f, 0.16f, 0.6f);
            for (float t = 0f; t < spawnTime; t += Time.deltaTime)
            {
                float k = t / spawnTime;
                float s = k < 0.7f ? Mathf.Lerp(0f, 1.2f, k / 0.7f) : Mathf.Lerp(1.2f, 1f, (k - 0.7f) / 0.3f);
                visual.localScale = baseScale * s;
                yield return null;
            }
            visual.localScale = baseScale;
        }

        void OnDamaged(DamageInfo info)
        {
            squashVel -= info.heavy ? 9f : 6f;
            DustFx.Burst(transform.position + Vector3.up * 0.6f, info.heavy ? 12 : 6, Wool, 2.2f, 0.14f, 0.55f);
            Sfx.Play("pop", transform.position, 0.45f, Random.Range(0.75f, 0.95f));
        }

        void OnDied()
        {
            Sfx.Play("pop", transform.position, 0.9f, 0.6f);
            DustFx.Burst(transform.position + Vector3.up * 0.6f, 30, Wool, 3.2f, 0.2f, 0.8f);
            StartCoroutine(Shrink());
        }

        IEnumerator Shrink()
        {
            if (visual == null) yield break;
            yield return new WaitForSeconds(0.9f);
            Vector3 s0 = visual.localScale;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                visual.localScale = s0 * (1f - t / 0.4f);
                yield return null;
            }
            visual.localScale = Vector3.zero;
        }

        void LateUpdate()
        {
            if (visual == null || (health != null && health.IsDead)) return;
            squashVel += (-squash * 220f - squashVel * 12f) * Time.deltaTime;
            squash += squashVel * Time.deltaTime;
            float sy = Mathf.Clamp(1f + squash * 0.06f, 0.65f, 1.35f);
            if (visual.localScale.sqrMagnitude > 0.0001f && Time.timeSinceLevelLoad > 0.1f)
            {
                float sxz = 1f / Mathf.Sqrt(sy);
                Vector3 target = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);
                // 등장 연출 중에는 건드리지 않는다
                if (Mathf.Abs(visual.localScale.y / Mathf.Max(0.0001f, baseScale.y) - 1f) < 0.4f) visual.localScale = target;
            }
        }
    }
}
