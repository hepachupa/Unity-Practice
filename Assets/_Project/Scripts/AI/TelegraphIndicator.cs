using UnityEngine;

namespace LNStudy.AI
{
    /// <summary>
    /// 공격 예고 장판. 몬스터 발밑에 공격 범위(부채꼴)를 깔고, 예비 동작 시간 동안 안쪽이 차오른다.
    /// 다 차는 순간이 판정 타이밍 → 플레이어가 "언제, 어디로 피해야 하는지" 읽을 수 있다.
    /// 판정 순간 한 번 번쩍이고 사라지며, 공격이 끊기면(경직·날아감) 바로 사라진다.
    /// 몬스터 루트의 자식이라 예비 동작 중 몸을 돌리면 장판도 같이 돈다.
    /// </summary>
    public class TelegraphIndicator : MonoBehaviour
    {
        [Tooltip("URP Unlit 반투명 머티리얼 (색은 MaterialPropertyBlock으로 바꾼다)")]
        [SerializeField] Material material;
        [SerializeField] Color edgeColor = new Color(1f, 0.25f, 0.2f, 0.22f);
        [SerializeField] Color fillColor = new Color(1f, 0.2f, 0.12f, 0.45f);
        [SerializeField] Color flashColor = new Color(1f, 0.95f, 0.85f, 0.8f);
        [SerializeField] float groundOffset = 0.04f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Transform root, fill;
        MeshRenderer edgeRenderer, fillRenderer;
        MeshFilter edgeFilter, fillFilter;
        MaterialPropertyBlock block;
        float builtArc = -1f;
        float timer, duration, flashTimer;
        bool showing;

        public bool Showing => showing || flashTimer > 0f;

        void Awake()
        {
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    material = new Material(shader);
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = 3000;
                }
            }
            block = new MaterialPropertyBlock();

            root = new GameObject("Telegraph").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.up * groundOffset;
            (edgeFilter, edgeRenderer) = MakePart(root, "Edge");
            fill = new GameObject("Fill").transform;
            fill.SetParent(root, false);
            fill.localPosition = Vector3.up * 0.005f;
            (fillFilter, fillRenderer) = MakePart(fill, "FillMesh", true);
            root.gameObject.SetActive(false);
        }

        (MeshFilter, MeshRenderer) MakePart(Transform parent, string name, bool asSelf = false)
        {
            var go = asSelf ? parent.gameObject : new GameObject(name);
            if (!asSelf) go.transform.SetParent(parent, false);
            var f = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return (f, r);
        }

        /// <summary>반지름 1짜리 부채꼴 (+Z 방향 중심, 바닥 XZ 평면)</summary>
        static Mesh BuildSector(float arcDeg)
        {
            int seg = Mathf.Max(6, Mathf.CeilToInt(arcDeg / 6f));
            bool full = arcDeg >= 359f;
            var v = new Vector3[seg + 2];
            var tri = new int[seg * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = (-arcDeg * 0.5f + arcDeg * i / seg) * Mathf.Deg2Rad;
                v[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
            for (int i = 0; i < seg; i++)
            {
                tri[i * 3] = 0;
                tri[i * 3 + 1] = i + 1;
                tri[i * 3 + 2] = full && i == seg - 1 ? 1 : i + 2;
            }
            var m = new Mesh { name = "TelegraphSector" };
            m.vertices = v;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public void Show(float range, float arcDeg, float time)
        {
            if (root == null) return;
            if (!Mathf.Approximately(arcDeg, builtArc))
            {
                var mesh = BuildSector(arcDeg);
                edgeFilter.sharedMesh = mesh;
                fillFilter.sharedMesh = mesh;
                builtArc = arcDeg;
            }
            // 부모(몬스터) 스케일과 상관없이 월드 기준 반지름이 되게
            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            root.localScale = Vector3.one * (range / parentScale);
            fill.localScale = Vector3.zero;
            timer = 0f;
            duration = Mathf.Max(0.05f, time);
            flashTimer = 0f;
            showing = true;
            root.gameObject.SetActive(true);
        }

        /// <summary>판정 순간: 번쩍이고 사라진다</summary>
        public void Release()
        {
            if (!showing) return;
            showing = false;
            flashTimer = 0.12f;
            fill.localScale = Vector3.one;
        }

        /// <summary>공격이 끊겼을 때: 바로 사라진다</summary>
        public void Hide()
        {
            showing = false;
            flashTimer = 0f;
            if (root != null) root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (root == null || !root.gameObject.activeSelf) return;

            if (showing)
            {
                timer += Time.deltaTime;
                float k = Mathf.Clamp01(timer / duration);
                fill.localScale = new Vector3(k, 1f, k);
                // 거의 다 찼을 때 테두리가 빨라지며 깜빡여 "곧 때린다"를 알린다
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 30f, k));
                Tint(edgeRenderer, new Color(edgeColor.r, edgeColor.g, edgeColor.b, edgeColor.a * pulse));
                Tint(fillRenderer, fillColor);
            }
            else if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float a = Mathf.Clamp01(flashTimer / 0.12f);
                Tint(edgeRenderer, new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * a * 0.5f));
                Tint(fillRenderer, new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * a));
                if (flashTimer <= 0f) root.gameObject.SetActive(false);
            }
        }

        void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}
