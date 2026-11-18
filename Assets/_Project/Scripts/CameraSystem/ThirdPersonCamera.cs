using LNStudy.Player;
using UnityEngine;

namespace LNStudy.CameraSystem
{
    /// <summary>
    /// 원신·스타레일 스타일 3인칭 궤도 카메라.
    /// 마우스/오른쪽 스틱으로 회전, 휠로 줌, 벽에 막히면 앞으로 당겨지고 천천히 다시 물러난다.
    /// 커서는 잠그고, Alt를 누르는 동안 / Esc를 누르면 풀린다 (화면 클릭 시 다시 잠김).
    /// 이동 계산은 PlayerMotor.MoveSpace에 이 카메라를 넣어 카메라 기준으로 맞춘다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] Transform target;
        [Tooltip("캐릭터 발 기준 시선 높이 (가슴~목)")]
        [SerializeField] float pivotHeight = 1.3f;
        [Tooltip("화면에서 캐릭터를 살짝 옆으로 비킬 때 (0 = 정중앙)")]
        [SerializeField] float shoulderOffset = 0f;
        [Tooltip("피벗이 캐릭터를 따라가는 지연 (작을수록 딱 붙음)")]
        [SerializeField] float followSmoothTime = 0.06f;

        [Header("Orbit")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] bool invertY;
        [SerializeField] float minPitch = -35f;
        [SerializeField] float maxPitch = 70f;
        [SerializeField] float startYaw;
        [SerializeField] float startPitch = 12f;

        [Header("Zoom")]
        [SerializeField] float distance = 4f;
        [SerializeField] float minDistance = 1.6f;
        [SerializeField] float maxDistance = 7f;
        [SerializeField] float zoomStep = 0.6f;
        [SerializeField] float zoomSmoothTime = 0.12f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = ~0;
        [SerializeField] float collisionRadius = 0.2f;
        [Tooltip("벽에서 벗어난 뒤 원래 거리로 돌아가는 속도 (m/s)")]
        [SerializeField] float recoverSpeed = 4f;

        /// <summary>현재 활성 카메라 (타격 흔들림 등에서 사용)</summary>
        public static ThirdPersonCamera Active { get; private set; }

        /// <summary>캐릭터 교체 시 시선 높이 변경 (피벗이 SmoothDamp로 부드럽게 따라감)</summary>
        public float PivotHeight { get => pivotHeight; set => pivotHeight = value; }

        public float Yaw => yaw;
        public float Pitch => pitch;

        PlayerInputReader input;
        float yaw, pitch;
        float desiredDistance, smoothDistance, zoomVel;
        float collidedDistance;
        Vector3 pivot, pivotVel;
        bool cursorLocked;
        float shakeAmp, shakeDuration, shakeTime;

        public Transform Target
        {
            get => target;
            set { target = value; CacheInput(); SnapToTarget(); }
        }

        // ── 연출(대화 등) 오버라이드: 지정한 위치·시선·화각으로 부드럽게 넘어갔다가 돌아온다
        bool cineActive;
        Vector3 cinePos, cineLook;
        float cineFov, cineWeight, cineWeightVel, baseFov = -1f;
        Camera cam;

        /// <summary>연출 카메라로 전환 (원신·스타레일식 대화 클로즈업 등). 호출할 때마다 목표를 갱신해도 된다</summary>
        public void SetCinematic(Vector3 position, Vector3 lookAt, float fov)
        {
            cineActive = true;
            cinePos = position;
            cineLook = lookAt;
            cineFov = fov;
        }

        public void ClearCinematic() => cineActive = false;
        public bool InCinematic => cineActive || cineWeight > 0.01f;

        /// <summary>순간이동 없이 대상만 바꾼다 (피벗이 SmoothDamp로 따라감) — 캐릭터 교체용</summary>
        public void SetTargetSmooth(Transform newTarget)
        {
            target = newTarget;
            CacheInput();
        }

        void Awake()
        {
            yaw = startYaw;
            pitch = startPitch;
            desiredDistance = smoothDistance = collidedDistance = distance;

            // 플레이어 자신에 카메라가 부딪히지 않게
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) collisionMask &= ~(1 << playerLayer);

            CacheInput();
        }

        void Start()
        {
            SnapToTarget();
            SetCursorLock(true);
        }

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this) Active = null;
            SetCursorLock(false);
        }

        /// <summary>카메라 흔들림. 진행 중이면 더 센 쪽을 쓴다</summary>
        public void Shake(float amplitude, float duration)
        {
            float remaining = shakeDuration > 0f ? shakeAmp * (1f - shakeTime / shakeDuration) : 0f;
            if (amplitude < remaining) return;
            shakeAmp = amplitude;
            shakeDuration = Mathf.Max(0.01f, duration);
            shakeTime = 0f;
        }

        /// <summary>커서가 잠겨 있을 때만 전투 입력을 받는다 (UI 조작 중 오발 방지)</summary>
        public bool IsCursorLocked => cursorLocked;

        void CacheInput() => input = target != null ? target.GetComponentInParent<PlayerInputReader>() : null;

        void SnapToTarget()
        {
            if (target == null) return;
            pivot = PivotTarget();
            pivotVel = Vector3.zero;
            ApplyTransform(desiredDistance);
        }

        Vector3 PivotTarget() => target.position + Vector3.up * pivotHeight;

        void LateUpdate()
        {
            if (target == null) return;

            UpdateCursor();
            if (cursorLocked && input != null && !cineActive) ReadOrbitInput();

            pivot = Vector3.SmoothDamp(pivot, PivotTarget(), ref pivotVel, followSmoothTime);
            smoothDistance = Mathf.SmoothDamp(smoothDistance, desiredDistance, ref zoomVel, zoomSmoothTime);

            // 벽 충돌: 막히면 즉시 당기고, 풀리면 천천히 복귀 (튀는 느낌 방지)
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 origin = pivot + rot * Vector3.right * shoulderOffset;
            Vector3 back = rot * Vector3.back;
            float allowed = smoothDistance;
            if (Physics.SphereCast(origin, collisionRadius, back, out RaycastHit hit, smoothDistance,
                    collisionMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(hit.distance, 0.3f);

            collidedDistance = allowed < collidedDistance
                ? allowed
                : Mathf.MoveTowards(collidedDistance, allowed, recoverSpeed * Time.deltaTime);

            ApplyTransform(collidedDistance);
            ApplyCinematic();
        }

        void ApplyCinematic()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null && baseFov < 0f) baseFov = cam.fieldOfView;
            cineWeight = Mathf.SmoothDamp(cineWeight, cineActive ? 1f : 0f, ref cineWeightVel, cineActive ? 0.45f : 0.35f);
            if (cineWeight < 0.001f) { if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov; return; }
            float w = cineWeight * cineWeight * (3f - 2f * cineWeight);
            Quaternion look = Quaternion.LookRotation(cineLook - cinePos);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, cinePos, w), Quaternion.Slerp(transform.rotation, look, w));
            if (cam != null) cam.fieldOfView = Mathf.Lerp(baseFov, cineFov, w);
        }

        void ReadOrbitInput()
        {
            Vector2 look = input.Look;
            if (input.LookFromGamepad) look *= gamepadSensitivity * Time.deltaTime;
            else look *= mouseSensitivity;

            yaw += look.x;
            pitch += invertY ? look.y : -look.y;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            float z = input.Zoom;
            if (Mathf.Abs(z) > 0.01f)
                desiredDistance = Mathf.Clamp(desiredDistance - Mathf.Sign(z) * zoomStep, minDistance, maxDistance);
        }

        void ApplyTransform(float dist)
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 origin = pivot + rot * Vector3.right * shoulderOffset;
            Vector3 shakeOffset = Vector3.zero;
            if (shakeTime < shakeDuration)
            {
                shakeTime += Time.unscaledDeltaTime;
                float k = shakeAmp * (1f - shakeTime / shakeDuration);
                float t = Time.unscaledTime * 40f;
                shakeOffset = rot * new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * (2f * k);
            }
            transform.SetPositionAndRotation(origin + rot * Vector3.back * dist + shakeOffset, rot);
        }

        void UpdateCursor()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;

            bool wantLocked = cursorLocked;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) wantLocked = false;
            else if (!cursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame
                     && (input == null || !input.FreeCursorHeld)) wantLocked = true;

            if (input != null && input.FreeCursorHeld) wantLocked = false;
            else if (input != null && !input.FreeCursorHeld && altWasHeld) wantLocked = true;
            altWasHeld = input != null && input.FreeCursorHeld;

            if (wantLocked != cursorLocked) SetCursorLock(wantLocked);
        }

        bool altWasHeld;

        void SetCursorLock(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnValidate()
        {
            minDistance = Mathf.Max(0.5f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }
    }
}
