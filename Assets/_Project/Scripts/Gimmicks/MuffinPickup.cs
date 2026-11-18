using System.Collections;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 숨겨진 작은 머핀. 둥실둥실 돌다가 캐릭터가 닿으면 통 커졌다가 반짝이며 사라지고 카운터가 올라간다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MuffinPickup : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] Material dustMaterial;
        [SerializeField] float bobHeight = 0.12f;
        [SerializeField] float spinSpeed = 70f;
        [SerializeField] Light glow;

        bool taken;
        Vector3 visualBase;
        Vector3 visualScale;
        float seed;
        float sparkleTimer;

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) { visualBase = visual.localPosition; visualScale = visual.localScale; }
            seed = Random.value * 10f;
            DustFx.Init(dustMaterial);
        }

        void Update()
        {
            if (taken || visual == null) return;
            float t = Time.time + seed;
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(t * 2.2f) * bobHeight);
            visual.localRotation = Quaternion.Euler(0f, t * spinSpeed, Mathf.Sin(t * 1.3f) * 8f);
            sparkleTimer -= Time.deltaTime;
            if (sparkleTimer <= 0f)
            {
                sparkleTimer = Random.Range(0.4f, 0.9f);
                DustFx.Burst(visual.position + Random.insideUnitSphere * 0.25f, 1, DustFx.SparkleColor, 0.3f, 0.09f, 0.7f);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (taken || !other.CompareTag("Player")) return;
            taken = true;
            var juice = other.GetComponentInParent<Rigidbody>()?.GetComponentInChildren<LNStudy.Player.CharacterJuice>();
            if (juice != null) juice.Pop(0.3f);
            StartCoroutine(CollectRoutine());
        }

        IEnumerator CollectRoutine()
        {
            Vector3 start = visual != null ? visual.position : transform.position;
            Sfx.Play("pop", start, 0.6f, 0.95f);
            DustFx.Burst(start, 18, DustFx.SparkleColor, 2.6f, 0.13f, 0.6f);
            MuffinHUD.Instance?.Collect(start); // 화면 속 이 자리에서 왼쪽 위 그림자 칸으로 날아감

            // 월드 머핀은 통 부풀었다가 바로 사라진다 (UI 아이콘이 이어받음)
            float lightStart = glow != null ? glow.intensity : 0f;
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float k = t / 0.18f;
                if (visual != null) visual.localScale = visualScale * Mathf.Lerp(1.2f, 0f, k * k);
                if (glow != null) glow.intensity = lightStart * (1f - k);
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
