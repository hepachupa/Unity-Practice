using LNStudy.Combat;
using LNStudy.Player;
using UnityEngine;
using UnityEngine.UI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 간단한 스테이지 HUD (프리팹 없이 런타임 생성): 스테이지 배너, 목표, 알림, 체력바, 조작 안내, 라멘 쿨타임.
    /// </summary>
    public class StageHUD : MonoBehaviour
    {
        public static StageHUD Instance { get; private set; }

        [SerializeField] Health playerHealth;
        [SerializeField] CharacterSwitcher switcher;
        [SerializeField] string openingBanner = "";
        [SerializeField] string openingToast = "";
        [Tooltip("체력바 표시 (퍼즐 전용 씬에서는 끈다)")]
        [SerializeField] bool showHealthBar = true;
        [Tooltip("배너·알림·목표·조작 안내·캐릭터 이름 등 글자를 전부 숨긴다 (글자 없는 연출용)")]
        [SerializeField] bool hideAllText;
        [TextArea] [SerializeField] string controlsText =
            "WASD 이동 · Shift 달리기 · Space 점프 · Tab 캐릭터 교체\n유니짱: 좌클릭 드론 공격 · 시사: 좌클릭 꾹 눌러 라멘 던지기 · Q 미사일";

        Text banner, objective, toast, controls, charName;
        RectTransform hpFill;
        Image hpFillImage;
        CanvasGroup bannerGroup, toastGroup;
        float bannerUntil, toastUntil;
        StageController stage;
        RamenThrower ramen;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(openingBanner)) ShowBanner(openingBanner, 3f);
            if (!string.IsNullOrEmpty(openingToast)) ShowToast(openingToast, 5f);
        }

        public void SetStage(StageController s) => stage = s;

        /// <summary>체력바 대상 변경 (캐릭터 교체)</summary>
        public void SetPlayerHealth(Health h) => playerHealth = h;

        /// <summary>교체 시스템(CharacterSwitcher) 대신 직접 캐릭터 표시 문구를 넣는다. null이면 기본 동작</summary>
        public string ExternalLabel { get; set; }

        /// <summary>StageController 없이 목표 문구를 직접 넣는다 (웨이브 방어 등)</summary>
        public string ObjectiveOverride { get; set; }

        public void ShowBanner(string text, float duration)
        {
            banner.text = text;
            bannerUntil = Time.time + duration;
        }

        public void ShowToast(string text, float duration = 2f)
        {
            toast.text = text;
            toastUntil = Time.time + duration;
        }

        void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            banner = MakeText(root, "Banner", font, 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.78f), new Vector2(1400, 120));
            bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            AddShadow(banner);

            toast = MakeText(root, "Toast", font, 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.68f), new Vector2(1200, 60));
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            AddShadow(toast);

            objective = MakeText(root, "Objective", font, 28, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(700, 140));
            var ort = objective.rectTransform;
            ort.pivot = new Vector2(0f, 1f);
            ort.anchoredPosition = new Vector2(40, -40);
            AddShadow(objective);

            controls = MakeText(root, "Controls", font, 20, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(1100, 70));
            var crt = controls.rectTransform;
            crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-30, 30);
            controls.text = controlsText;
            controls.color = new Color(1f, 1f, 1f, 0.75f);
            AddShadow(controls);

            // 체력바
            var hpBack = new GameObject("HP Back", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            hpBack.SetParent(root, false);
            hpBack.anchorMin = hpBack.anchorMax = new Vector2(0f, 0f);
            hpBack.pivot = new Vector2(0f, 0f);
            hpBack.anchoredPosition = new Vector2(40, 40);
            hpBack.sizeDelta = new Vector2(420, 26);
            hpBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            hpFill = new GameObject("HP Fill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            hpFill.SetParent(hpBack, false);
            hpFill.anchorMin = Vector2.zero;
            hpFill.anchorMax = Vector2.one;
            hpFill.offsetMin = new Vector2(3, 3);
            hpFill.offsetMax = new Vector2(-3, -3);
            hpFillImage = hpFill.GetComponent<Image>();
            hpFillImage.color = new Color(0.45f, 0.95f, 0.5f);

            hpBack.gameObject.SetActive(showHealthBar);

            charName = MakeText(root, "CharName", font, 26, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(600, 40));
            var cn = charName.rectTransform;
            cn.pivot = new Vector2(0f, 0f);
            cn.anchoredPosition = new Vector2(42, 72);
            AddShadow(charName);

            if (hideAllText)
                foreach (var t in new[] { banner, toast, objective, controls, charName })
                    t.gameObject.SetActive(false);
        }

        static Text MakeText(Transform parent, string name, Font font, int size, TextAnchor align, Vector2 anchor, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = sizeDelta;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void AddShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.8f);
            s.effectDistance = new Vector2(2, -2);
        }

        void Update()
        {
            bannerGroup.alpha = Mathf.Clamp01((bannerUntil - Time.time) * 2f);
            toastGroup.alpha = Mathf.Clamp01((toastUntil - Time.time) * 2f);

            objective.text = stage != null ? $"<b>{stage.Title}</b>\n{stage.ObjectiveText()}" : (ObjectiveOverride ?? "");

            if (playerHealth != null)
            {
                float v = playerHealth.Normalized;
                hpFill.anchorMax = new Vector2(Mathf.Max(0.001f, v), 1f);
                hpFillImage.color = Color.Lerp(new Color(1f, 0.3f, 0.25f), new Color(0.45f, 0.95f, 0.5f), v);
            }

            string who = ExternalLabel ?? "";
            if (ExternalLabel == null && switcher != null && switcher.ActiveEntry != null)
            {
                who = switcher.ActiveEntry.displayName;
                if (ramen == null || !ramen.isActiveAndEnabled)
                    ramen = switcher.ActiveEntry.visualRoot != null ? switcher.ActiveEntry.visualRoot.GetComponent<RamenThrower>() : null;
                if (ramen != null && ramen.isActiveAndEnabled)
                    who += ramen.CooldownRemaining > 0f ? $"   라멘 {ramen.CooldownRemaining:F1}s" : "   라멘 준비";
            }
            charName.text = who;
        }
    }
}
