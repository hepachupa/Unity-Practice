using System;
using System.Collections;
using LNStudy.AI;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 원신·스타레일식 대화 연출.
    /// - 위아래 검은 띠(레터박스)가 들어오고, 카메라가 말하는 친구 얼굴로 다가간다
    ///   (플레이어 어깨 너머, 화자는 화면 오른쪽 1/3 — 오버 더 숄더)
    /// - 아래 대화창: 이름 + 한 글자씩 나오는 대사. F / Space / 클릭으로 넘김 (타이핑 중이면 한 번에 표시)
    /// - 대화 중에는 플레이어 조작·전투 잠금, 화자와 플레이어가 서로를 바라본다
    /// 필요할 때 자동으로 만들어진다 (씬에 미리 둘 필요 없음).
    /// </summary>
    public class DialogueDirector : MonoBehaviour
    {
        static DialogueDirector instance;
        public static DialogueDirector Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("[Dialogue]").AddComponent<DialogueDirector>();
                return instance;
            }
        }

        public bool Busy { get; private set; }

        [SerializeField] float charsPerSecond = 28f;
        [SerializeField] float shotDistance = 1.35f;
        [SerializeField] float shotSide = 0.45f;
        [Tooltip("머리 뼈에서 얼굴 중심까지 높이 (SD 캐릭터는 머리가 커서 위로 올려 잡음)")]
        [SerializeField] float faceOffset = 0.14f;
        [SerializeField] float shotFov = 30f;

        InputAction advance;
        RectTransform topBar, bottomBar, box;
        CanvasGroup boxGroup;
        Text nameText, bodyText, nextMark;

        void Awake()
        {
            instance = this;
            advance = new InputAction("DialogueNext", InputActionType.Button);
            advance.AddBinding("<Keyboard>/f");
            advance.AddBinding("<Keyboard>/space");
            advance.AddBinding("<Mouse>/leftButton");
            advance.AddBinding("<Gamepad>/buttonSouth");
            advance.AddBinding("<Gamepad>/buttonEast");
            advance.Enable();
            BuildUI();
        }

        void OnDestroy()
        {
            advance?.Dispose();
            if (instance == this) instance = null;
        }

        public void Play(FriendNPC speaker, Transform player, string[] lines, Action onDone)
        {
            if (Busy || speaker == null || lines == null || lines.Length == 0) return;
            StartCoroutine(Run(speaker, player, lines, onDone));
        }

        IEnumerator Run(FriendNPC speaker, Transform player, string[] lines, Action onDone)
        {
            Busy = true;
            var input = player.GetComponent<PlayerInputReader>();
            var combat = player.GetComponent<SwordCombat>();
            var motor = player.GetComponent<PlayerMotor>();
            var body = player.GetComponent<Rigidbody>();
            input?.SetControlled(false);
            if (combat != null) combat.Locked = true;
            if (body != null) body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);

            speaker.BeginTalk(player);
            var cam = ThirdPersonCamera.Active;

            // 레터박스 + 대화창 등장
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                SetBars(t / 0.3f);
                FacePlayer(player, speaker.transform, body);
                Shot(cam, speaker, player);
                yield return null;
            }
            SetBars(1f);

            for (int i = 0; i < lines.Length; i++)
            {
                nameText.text = speaker.DisplayName;
                bodyText.text = "";
                nextMark.enabled = false;
                if (i == 0) speaker.OnLineStarted();
                Sfx.Play("switch", speaker.transform.position, 0.35f, 1.4f);

                string line = lines[i];
                float shown = 0f;
                yield return null; // 대화를 연 F 입력이 바로 넘김으로 처리되지 않게
                while (shown < line.Length)
                {
                    shown += charsPerSecond * Time.deltaTime;
                    int n = Mathf.Min(line.Length, Mathf.FloorToInt(shown));
                    if (bodyText.text.Length != n)
                    {
                        bodyText.text = line.Substring(0, n);
                        if (n % 3 == 0) Sfx.Play("step", speaker.transform.position, 0.15f, 2.2f);
                    }
                    if (advance.WasPressedThisFrame()) break;
                    FacePlayer(player, speaker.transform, body);
                    Shot(cam, speaker, player);
                    yield return null;
                }
                bodyText.text = line;
                nextMark.enabled = true;
                yield return null;
                while (!advance.WasPressedThisFrame())
                {
                    nextMark.color = new Color(1f, 1f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
                    FacePlayer(player, speaker.transform, body);
                    Shot(cam, speaker, player);
                    yield return null;
                }
            }

            // 퇴장
            cam?.ClearCinematic();
            speaker.EndTalk();
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                SetBars(1f - t / 0.25f);
                yield return null;
            }
            SetBars(0f);
            input?.SetControlled(true);
            if (combat != null) combat.Locked = false;
            Busy = false;
            onDone?.Invoke();
        }

        /// <summary>오버 더 숄더 클로즈업: 플레이어 쪽에서 화자 얼굴을, 화자를 화면 오른쪽에 두고</summary>
        void Shot(ThirdPersonCamera cam, FriendNPC speaker, Transform player)
        {
            if (cam == null) return;
            Vector3 head = speaker.HeadPosition + Vector3.up * faceOffset;
            Vector3 toPlayer = player.position - speaker.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) toPlayer = speaker.transform.forward;
            toPlayer.Normalize();
            Vector3 camFwd = -toPlayer;
            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
            Vector3 pos = head - camFwd * shotDistance + camRight * shotSide;
            Vector3 look = head - camRight * 0.24f - Vector3.up * 0.1f; // 얼굴이 화면 오른쪽 위 1/3에 오게
            cam.SetCinematic(pos, look, shotFov);
        }

        static void FacePlayer(Transform player, Transform speaker, Rigidbody body)
        {
            Vector3 to = speaker.position - player.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            var rot = Quaternion.RotateTowards(player.rotation, Quaternion.LookRotation(to), 540f * Time.deltaTime);
            if (body != null) body.MoveRotation(rot);
            player.rotation = rot;
        }

        // ───────────────────────── UI ─────────────────────────

        void SetBars(float k)
        {
            float e = k * k * (3f - 2f * k);
            topBar.sizeDelta = new Vector2(0f, 110f * e);
            bottomBar.sizeDelta = new Vector2(0f, 110f * e);
            boxGroup.alpha = e;
        }

        void BuildUI()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("Dialogue Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            RectTransform Bar(string n, bool top)
            {
                var rt = new GameObject(n, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rt.SetParent(root, false);
                rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
                rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
                rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
                rt.sizeDelta = new Vector2(0f, 0f);
                var img = rt.GetComponent<Image>();
                img.color = Color.black;
                img.raycastTarget = false;
                return rt;
            }
            topBar = Bar("LetterboxTop", true);
            bottomBar = Bar("LetterboxBottom", false);

            box = new GameObject("DialogueBox", typeof(RectTransform), typeof(Image), typeof(CanvasGroup)).GetComponent<RectTransform>();
            box.SetParent(root, false);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.anchoredPosition = new Vector2(0f, 130f);
            box.sizeDelta = new Vector2(1300f, 190f);
            var bg = box.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.1f, 0.62f);
            bg.raycastTarget = false;
            boxGroup = box.GetComponent<CanvasGroup>();
            boxGroup.alpha = 0f;

            Text T(string n, int size, TextAnchor align, Vector2 pos, Vector2 sizeDelta, Color color)
            {
                var go = new GameObject(n, typeof(RectTransform), typeof(Text), typeof(Shadow));
                go.transform.SetParent(box, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = sizeDelta;
                var t = go.GetComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.alignment = align;
                t.color = color;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                go.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.7f);
                return t;
            }
            nameText = T("Name", 32, TextAnchor.UpperLeft, new Vector2(40f, -22f), new Vector2(600f, 44f), new Color(1f, 0.84f, 0.5f));
            bodyText = T("Body", 36, TextAnchor.UpperLeft, new Vector2(40f, -76f), new Vector2(1180f, 100f), Color.white);
            nextMark = T("Next", 28, TextAnchor.LowerRight, new Vector2(1180f, -140f), new Vector2(80f, 40f), Color.white);
            nextMark.text = "▼";
            nextMark.enabled = false;
        }
    }
}
