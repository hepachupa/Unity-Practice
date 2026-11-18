using System.Collections;
using LNStudy.CameraSystem;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 머핀 타임 엔딩 연출. 도착 트리거(PlayerTrigger, 두 캐릭터 모두)에서 Play()를 부른다.
    ///  1. 조작 잠금, 카메라가 오두막 앞으로 다가감
    ///  2. 두 캐릭터가 머핀 양옆으로 걸어감 → 큰 머핀이 탁자에서 폴짝 뛰어내림
    ///  3. 카메라를 보며 냠냠 (네 입) → 머핀이 줄어들다 사라짐 → 기뻐하는 동작
    ///  4. 오두막 문이 열리고 둘이 차례로 걸어 들어감 → 문이 닫힘
    ///  5. 화면이 따뜻하게 밝아지며 "머핀 타임 / 클리어!" + 찾은 작은 머핀 수, R로 다시 하기
    /// </summary>
    public class MuffinTimeEnding : MonoBehaviour
    {
        [SerializeField] CatParty party;
        [SerializeField] Transform bigMuffin;
        [Tooltip("머핀이 뛰어내려 앉을 바닥 지점 (두 캐릭터 사이)")]
        [SerializeField] Transform muffinLandPoint;
        [SerializeField] Transform doorHinge;
        [SerializeField] float doorOpenAngle = -100f;
        [Tooltip("문 앞 바닥 지점 (Z=0 통로 위)")]
        [SerializeField] Transform doorPoint;
        [Tooltip("이 Z보다 들어가면 오두막 안")]
        [SerializeField] float insideZ = 1.9f;
        [SerializeField] Light interiorLight;
        [SerializeField] Transform cameraPoint;
        [SerializeField] Transform cameraLook;
        [SerializeField] Material dustMaterial;
        [SerializeField] float eatSpacing = 0.8f;

        public bool Playing { get; private set; }
        bool finished;
        CanvasGroup overlay;

        void Awake() => DustFx.Init(dustMaterial);

        public void Play()
        {
            if (Playing || party == null) return;
            Playing = true;
            StartCoroutine(Run());
        }

        void Update()
        {
            if (!finished) return;
            bool restart = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                           || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (restart) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().buildIndex : 0);
        }

        // ───────────────────────── Helpers ─────────────────────────

        struct Actor
        {
            public CatParty.Member member;
            public PlayerMotor motor;
            public PlayerAnimator anim;
            public SideScrollFacing facing;
            public CharacterJuice juice;
            public Transform tr;
            public bool heavy;
        }

        Actor MakeActor(CatParty.Member m)
        {
            var a = new Actor { member = m, tr = m.input.transform };
            a.motor = m.input.GetComponent<PlayerMotor>();
            a.anim = m.input.GetComponentInChildren<PlayerAnimator>();
            a.facing = m.input.GetComponentInChildren<SideScrollFacing>();
            a.juice = m.input.GetComponentInChildren<CharacterJuice>();
            var cap = m.input.GetComponent<CapsuleCollider>();
            a.heavy = cap != null && cap.height > 1f;
            return a;
        }

        IEnumerator WalkTo(Actor a, Vector3 point, float timeout = 5f)
        {
            a.motor.UseExternalMove = true;
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                Vector3 d = point - a.tr.position;
                d.y = 0f;
                if (a.motor.SideScroll) d.z = 0f;
                float dist = d.magnitude;
                if (dist < 0.06f) break;
                a.motor.ExternalMove = d / dist * Mathf.Clamp(dist * 2f, 0.3f, 1f);
                a.motor.ExternalRun = false;
                yield return null;
            }
            a.motor.ExternalMove = Vector3.zero;
        }

        void Stop(Actor a)
        {
            a.motor.ExternalMove = Vector3.zero;
            a.motor.UseExternalMove = false;
        }

        // ───────────────────────── Sequence ─────────────────────────

        IEnumerator Run()
        {
            party.SetLocked(true);
            StageHUD.Instance?.ShowToast("", 0.01f);
            MuffinHUD.Instance?.SetVisible(false);

            var members = party.Members;
            if (members == null || members.Length < 2) yield break;
            var cat = MakeActor(members[0]);
            var sq = MakeActor(members[1]);
            var capA = cat.tr.GetComponent<Collider>();
            var capB = sq.tr.GetComponent<Collider>();
            if (capA != null && capB != null) Physics.IgnoreCollision(capA, capB, true);

            var cam = SideFollowCamera.Active;
            if (cam != null && cameraPoint != null && cameraLook != null)
                cam.SetFramingOverride(cameraPoint.position, cameraLook.position, 0.9f);

            // 착지할 때까지 잠깐
            for (float t = 0f; t < 1f && (!cat.motor.IsGrounded || !sq.motor.IsGrounded); t += Time.deltaTime) yield return null;

            // 2. 머핀 양옆으로
            Vector3 land = muffinLandPoint != null ? muffinLandPoint.position : bigMuffin.position;
            Vector3 catSpot = new Vector3(land.x - eatSpacing, cat.tr.position.y, 0f);
            Vector3 sqSpot = new Vector3(land.x + eatSpacing, sq.tr.position.y, 0f);
            var w1 = StartCoroutine(WalkTo(cat, catSpot));
            yield return WalkTo(sq, sqSpot);
            yield return w1;
            Stop(cat);
            Stop(sq);
            yield return new WaitForSeconds(0.3f);

            // 머핀 폴짝
            if (bigMuffin != null)
            {
                Sfx.Play("jump", bigMuffin.position, 0.5f, 1.4f);
                Vector3 from = bigMuffin.position;
                for (float t = 0f; t < 0.55f; t += Time.deltaTime)
                {
                    float k = t / 0.55f;
                    bigMuffin.position = Vector3.Lerp(from, land, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.1f);
                    bigMuffin.Rotate(0f, 400f * Time.deltaTime, 0f, Space.World);
                    yield return null;
                }
                bigMuffin.position = land;
                Sfx.Play("land", land, 0.6f, 1.2f);
                DustFx.Puff(land, 8, 0.35f, 1.4f);
                yield return Squash(bigMuffin, 0.25f);
                cat.juice?.Pop(0.3f);
                sq.juice?.Pop(0.35f);
            }
            yield return new WaitForSeconds(0.7f); // 정면을 볼 시간

            // 3. 냠냠
            Vector3 startScale = bigMuffin != null ? bigMuffin.localScale : Vector3.one;
            for (int bite = 0; bite < 4; bite++)
            {
                cat.anim?.PlayIdleVariant(2);
                sq.anim?.PlayIdleVariant(2);
                yield return new WaitForSeconds(0.25f);
                Sfx.Play("munch", cat.tr.position, 0.7f, Random.Range(0.8f, 0.9f));
                Sfx.Play("munch", sq.tr.position, 0.6f, Random.Range(1.2f, 1.35f));
                if (bigMuffin != null)
                {
                    DustFx.Burst(bigMuffin.position + Vector3.up * 0.3f, 10, DustFx.CrumbColor, 2.2f, 0.08f, 0.6f);
                    bigMuffin.localScale = startScale * Mathf.Pow(0.75f, bite + 1);
                    StartCoroutine(Squash(bigMuffin, 0.15f));
                }
                cat.juice?.Pop(-0.12f);
                sq.juice?.Pop(-0.12f);
                yield return new WaitForSeconds(0.55f);
            }
            if (bigMuffin != null)
            {
                Sfx.Play("pop", bigMuffin.position, 0.8f, 1f);
                DustFx.Burst(bigMuffin.position + Vector3.up * 0.2f, 24, DustFx.SparkleColor, 3f, 0.14f, 0.8f);
                bigMuffin.gameObject.SetActive(false);
            }
            yield return new WaitForSeconds(0.35f);

            // 기쁨
            Sfx.Play("chime", 0.7f);
            cat.anim?.PlayIdleVariant(1);
            sq.anim?.PlayIdleVariant(1);
            cat.juice?.Pop(0.4f);
            sq.juice?.Pop(0.45f);
            DustFx.Burst(cat.tr.position + Vector3.up * 1.5f, 12, new Color(1f, 0.55f, 0.65f), 1.5f, 0.15f, 0.9f);
            DustFx.Burst(sq.tr.position + Vector3.up * 1.1f, 10, new Color(1f, 0.55f, 0.65f), 1.5f, 0.13f, 0.9f);
            yield return new WaitForSeconds(1.4f);

            // 4. 문 열기
            Quaternion closed = doorHinge != null ? doorHinge.localRotation : Quaternion.identity;
            if (doorHinge != null)
            {
                Sfx.Play("creak", doorHinge.position, 0.7f, 1f);
                float l0 = interiorLight != null ? interiorLight.intensity : 0f;
                for (float t = 0f; t < 0.8f; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / 0.8f);
                    doorHinge.localRotation = closed * Quaternion.Euler(0f, doorOpenAngle * k, 0f);
                    if (interiorLight != null) interiorLight.intensity = l0 * (1f + k * 1.5f);
                    yield return null;
                }
            }
            if (cam != null && doorPoint != null)
                cam.SetFramingOverride(doorPoint.position + new Vector3(-0.6f, 2.2f, -7f), doorPoint.position + new Vector3(0f, 1f, 1f), 1.2f);

            // 문 앞으로 → 안으로 (다람쥐 먼저)
            Vector3 door = doorPoint != null ? doorPoint.position : sq.tr.position + Vector3.right * 2f;
            var w2 = StartCoroutine(WalkTo(cat, new Vector3(door.x - 0.9f, cat.tr.position.y, 0f)));
            yield return WalkTo(sq, new Vector3(door.x + 0.15f, sq.tr.position.y, 0f));
            yield return GoInside(sq);
            yield return w2;
            yield return WalkTo(cat, new Vector3(door.x - 0.1f, cat.tr.position.y, 0f));
            yield return GoInside(cat);
            Stop(cat);
            Stop(sq);

            // 문 닫힘
            if (doorHinge != null)
            {
                for (float t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    float k = t / 0.4f;
                    doorHinge.localRotation = closed * Quaternion.Euler(0f, doorOpenAngle * (1f - k * k), 0f);
                    yield return null;
                }
                doorHinge.localRotation = closed;
                Sfx.Play("thud", doorHinge.position, 0.8f, 1.3f);
                SideFollowCamera.Shake(0.05f, 0.2f);
                DustFx.Puff(doorHinge.position + Vector3.right * 0.7f, 6, 0.3f, 1.2f);
            }
            yield return new WaitForSeconds(0.8f);

            // 5. 마무리 화면
            BuildOverlay();
            Sfx.Play("chime", 0.9f, 0.75f);
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                overlay.alpha = Mathf.SmoothStep(0f, 1f, t / 1.4f);
                yield return null;
            }
            overlay.alpha = 1f;
            yield return PresentResults();
            finished = true;
        }

        IEnumerator GoInside(Actor a)
        {
            if (a.facing != null) a.facing.enabled = false;
            a.motor.SetSideScroll(false);
            a.motor.UseExternalMove = true;
            for (float t = 0f; t < 4f && a.tr.position.z < insideZ; t += Time.deltaTime)
            {
                a.motor.ExternalMove = Vector3.forward;
                yield return null;
            }
            a.motor.ExternalMove = Vector3.zero;
            Sfx.Play("step", a.tr.position, 0.4f, a.heavy ? 0.8f : 1.3f);
        }

        static IEnumerator Squash(Transform t, float amount)
        {
            Vector3 s0 = t.localScale;
            for (float x = 0f; x < 0.35f; x += Time.deltaTime)
            {
                float k = x / 0.35f;
                float sy = 1f - amount * Mathf.Sin(k * Mathf.PI) * (1f - k);
                t.localScale = new Vector3(s0.x / Mathf.Sqrt(sy), s0.y * sy, s0.z / Mathf.Sqrt(sy));
                yield return null;
            }
            t.localScale = s0;
        }

        /// <summary>글자 없는 마무리 화면: 따뜻한 배경 + 가운데 큰 머핀 + 아래에 작은 머핀 칸 (찾은 것만 채워짐)</summary>
        void BuildOverlay()
        {
            var canvasGo = new GameObject("Ending Overlay", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            overlay = canvasGo.GetComponent<CanvasGroup>();
            overlay.alpha = 0f;

            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            bg.SetParent(canvasGo.transform, false);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(1f, 0.93f, 0.8f, 0.94f);

            var hud = MuffinHUD.Instance;
            Sprite icon = hud != null ? hud.Icon : null;
            RectTransform Icon(string name, Vector2 pos, float size, Color color)
            {
                var rt = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rt.SetParent(canvasGo.transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = Vector2.one * size;
                var img = rt.GetComponent<Image>();
                img.sprite = icon;
                img.color = color;
                img.preserveAspect = true;
                img.raycastTarget = false;
                return rt;
            }

            bigIcon = Icon("BigMuffin", new Vector2(0f, 90f), 300f, Color.white);
            bigIcon.localScale = Vector3.zero;
            int total = hud != null ? hud.Total : 0;
            endSlots = new RectTransform[total];
            endFills = new RectTransform[total];
            float size = 110f, gap = 30f;
            float x0 = -(total - 1) * (size + gap) * 0.5f;
            for (int i = 0; i < total; i++)
            {
                var pos = new Vector2(x0 + i * (size + gap), -180f);
                endSlots[i] = Icon("Slot" + i, pos, size, new Color(0.18f, 0.1f, 0.06f, 0.35f));
                endFills[i] = Icon("Fill" + i, pos, size, Color.white);
                endFills[i].localScale = Vector3.zero;
            }
        }

        RectTransform bigIcon;
        RectTransform[] endSlots, endFills;

        /// <summary>큰 머핀이 통 튀어나오고, 찾은 작은 머핀이 하나씩 칸에 들어간다</summary>
        IEnumerator PresentResults()
        {
            if (bigIcon != null)
            {
                for (float t = 0f; t < 0.5f; t += Time.deltaTime)
                {
                    float k = t / 0.5f;
                    bigIcon.localScale = Vector3.one * (Mathf.Sin(k * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f));
                    yield return null;
                }
                bigIcon.localScale = Vector3.one;
            }
            var hud = MuffinHUD.Instance;
            if (endFills == null || hud == null) yield break;
            for (int i = 0; i < endFills.Length; i++)
            {
                yield return new WaitForSeconds(0.3f);
                if (!hud.IsFilled(i)) continue;
                Sfx.Play("pop", 0.7f, 1.1f + i * 0.12f);
                for (float t = 0f; t < 0.3f; t += Time.deltaTime)
                {
                    float k = t / 0.3f;
                    endFills[i].localScale = Vector3.one * (k < 0.7f ? Mathf.Lerp(0f, 1.25f, k / 0.7f) : Mathf.Lerp(1.25f, 1f, (k - 0.7f) / 0.3f));
                    yield return null;
                }
                endFills[i].localScale = Vector3.one;
            }
        }
    }
}
