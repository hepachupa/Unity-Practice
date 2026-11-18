using System.Collections;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 머핀 타임 HUD (글자 없음): 왼쪽 위에 작은 머핀 그림자 칸이 놓여 있고,
    /// 머핀을 주우면 화면 속 그 자리에서 머핀 아이콘이 포물선을 그리며 날아와 빈 그림자 칸에 쏙 들어간다.
    /// 칸 수는 시작할 때 씬의 MuffinPickup 수로 정한다.
    /// </summary>
    public class MuffinHUD : MonoBehaviour
    {
        public static MuffinHUD Instance { get; private set; }

        [SerializeField] Sprite muffinIcon;
        [SerializeField] float slotSize = 78f;
        [SerializeField] float slotSpacing = 14f;
        [SerializeField] Vector2 margin = new Vector2(36f, 30f);
        [SerializeField] Color shadowColor = new Color(0.18f, 0.1f, 0.06f, 0.42f);
        [SerializeField] float flyTime = 0.65f;

        public int Collected { get; private set; }
        public int Total { get; private set; }
        public Sprite Icon => muffinIcon;

        RectTransform canvasRect;
        RectTransform[] slots;
        Image[] fills;
        int reserved; // 날아오는 중인 것까지 포함한 다음 빈 칸

        void Awake()
        {
            Instance = this;
            BuildCanvas();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            Total = FindObjectsByType<MuffinPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            BuildSlots();
        }

        public bool IsFilled(int i) => i < Collected;

        /// <summary>월드 위치에서 아이콘이 날아와 다음 빈 칸에 들어간다</summary>
        public void Collect(Vector3 worldFrom)
        {
            if (slots == null || reserved >= slots.Length) { Collected++; return; }
            int index = reserved++;
            StartCoroutine(Fly(worldFrom, index));
        }

        public void SetVisible(bool v)
        {
            if (canvasRect != null) canvasRect.gameObject.SetActive(v);
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("Muffin HUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)canvasGo.transform;
        }

        void BuildSlots()
        {
            slots = new RectTransform[Total];
            fills = new Image[Total];
            for (int i = 0; i < Total; i++)
            {
                var slot = NewImage("Slot" + i, canvasRect, shadowColor);
                slot.anchorMin = slot.anchorMax = new Vector2(0f, 1f);
                slot.pivot = new Vector2(0.5f, 0.5f);
                slot.sizeDelta = Vector2.one * slotSize;
                slot.anchoredPosition = new Vector2(margin.x + slotSize * 0.5f + i * (slotSize + slotSpacing), -(margin.y + slotSize * 0.5f));
                slots[i] = slot;

                var fill = NewImage("Fill", slot, Color.white);
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                fills[i] = fill.GetComponent<Image>();
                fills[i].enabled = false;
            }
        }

        RectTransform NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = muffinIcon;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        Vector2 CanvasPoint(Vector3 world)
        {
            var cam = Camera.main;
            Vector2 screen = cam != null ? (Vector2)cam.WorldToScreenPoint(world) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            return local;
        }

        Vector2 SlotPoint(int i)
        {
            Vector3 world = slots[i].TransformPoint(Vector3.zero);
            return (Vector2)canvasRect.InverseTransformPoint(world);
        }

        IEnumerator Fly(Vector3 worldFrom, int index)
        {
            var flyer = NewImage("Flyer", canvasRect, Color.white);
            flyer.anchorMin = flyer.anchorMax = new Vector2(0.5f, 0.5f);
            flyer.sizeDelta = Vector2.one * slotSize;
            Vector2 from = CanvasPoint(worldFrom);
            flyer.anchoredPosition = from;

            // 제자리에서 통 커졌다가
            for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.18f;
                flyer.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.5f, Mathf.Sin(k * Mathf.PI * 0.5f));
                yield return null;
            }

            // 포물선으로 칸까지 (위로 볼록), 도는 동안 살짝 회전
            Vector2 to = SlotPoint(index);
            Vector2 ctrl = (from + to) * 0.5f + new Vector2(-120f, 260f);
            for (float t = 0f; t < flyTime; t += Time.unscaledDeltaTime)
            {
                float k = t / flyTime;
                float e = k * k * (3f - 2f * k);
                Vector2 a = Vector2.Lerp(from, ctrl, e), b = Vector2.Lerp(ctrl, to, e);
                flyer.anchoredPosition = Vector2.Lerp(a, b, e);
                flyer.localScale = Vector3.one * Mathf.Lerp(1.5f, 0.9f, e);
                flyer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI) * 25f);
                yield return null;
            }
            Destroy(flyer.gameObject);

            // 쏙: 칸이 채워지며 통 튐
            fills[index].enabled = true;
            Collected++;
            Sfx.Play("pop", 0.7f, 1.1f + index * 0.12f);
            if (Collected >= Total) Sfx.Play("chime", 0.5f, 1.4f);
            var slot = slots[index];
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.35f;
                slot.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.35f * (1f - k * 0.5f));
                yield return null;
            }
            slot.localScale = Vector3.one;
        }
    }
}
