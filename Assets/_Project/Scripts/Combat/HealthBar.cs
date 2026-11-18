using UnityEngine;
using UnityEngine.UI;

namespace LNStudy.Combat
{
    /// <summary>
    /// 머리 위 체력바 (월드 스페이스, 카메라를 향함). 프리팹 없이 런타임에 UI를 만든다.
    /// 맞으면 잠깐 흰색 잔상 바가 남았다가 줄어든다. 풀피이고 한동안 안 맞으면 숨긴다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HealthBar : MonoBehaviour
    {
        [SerializeField] float height = 2.45f;
        [SerializeField] Vector2 size = new Vector2(0.9f, 0.08f);
        [SerializeField] Color fillColor = new Color(0.95f, 0.25f, 0.2f);
        [SerializeField] float hideAfter = 4f;

        Health health;
        Canvas canvas;
        RectTransform fill, trail;
        float trailValue = 1f;
        float lastHitTime = -999f;

        void Awake()
        {
            health = GetComponent<Health>();
            Build();
        }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Revived += OnRevived;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Revived -= OnRevived;
        }

        void OnDamaged(DamageInfo _) => lastHitTime = Time.time;
        void OnRevived() { trailValue = 1f; lastHitTime = -999f; }

        void Build()
        {
            var go = new GameObject("HealthBar", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * height;
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100f, 100f * size.y / size.x);
            go.transform.localScale = Vector3.one * (size.x / 100f);

            Image(go.transform, "Back", new Color(0f, 0f, 0f, 0.6f), true);
            trail = Image(go.transform, "Trail", new Color(1f, 1f, 1f, 0.85f), false);
            fill = Image(go.transform, "Fill", fillColor, false);
        }

        static RectTransform Image(Transform parent, string name, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = stretch ? new Vector2(-1.5f, -1.5f) : Vector2.zero;
            rt.offsetMax = stretch ? new Vector2(1.5f, 1.5f) : Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        void LateUpdate()
        {
            float v = health.Normalized;
            trailValue = trailValue > v ? Mathf.MoveTowards(trailValue, v, Time.deltaTime * 0.8f) : v;
            fill.anchorMax = new Vector2(v, 1f);
            trail.anchorMax = new Vector2(trailValue, 1f);

            bool show = !health.IsDead && (v < 0.999f || Time.time - lastHitTime < hideAfter);
            if (canvas.enabled != show) canvas.enabled = show;

            var cam = Camera.main;
            if (show && cam != null)
                canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - cam.transform.position);
        }
    }
}
