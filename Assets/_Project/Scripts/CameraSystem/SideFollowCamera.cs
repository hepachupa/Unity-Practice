using UnityEngine;

namespace LNStudy.CameraSystem
{
    /// <summary>
    /// 측면에서 대상을 따라가는 2.5D 카메라. 회전은 고정하고 위치만 부드럽게 따라간다.
    /// 진행 방향으로 살짝 앞을 보여 준다 (look-ahead).
    /// - Shake(세기, 시간): 착지·문 닫힘 등 손맛용 흔들림
    /// - SetFramingOverride: 연출 중 원하는 위치·시선으로 부드럽게 옮김 (ClearFramingOverride로 복귀)
    /// </summary>
    public class SideFollowCamera : MonoBehaviour
    {
        public static SideFollowCamera Active { get; private set; }

        [SerializeField] Transform target;
        [Tooltip("대상 기준 카메라 위치")]
        [SerializeField] Vector3 offset = new Vector3(0f, 1.6f, -7f);
        [Tooltip("카메라가 바라보는 지점 (대상 기준)")]
        [SerializeField] Vector3 lookOffset = new Vector3(0f, 0.7f, 0f);
        [SerializeField] float smoothTime = 0.25f;
        [Tooltip("진행 방향(X)으로 앞을 더 보여 주는 거리")]
        [SerializeField] float lookAhead = 0.8f;
        [SerializeField] float lookAheadSmoothTime = 0.5f;
        [SerializeField] float maxShake = 0.35f;

        Vector3 velocity;
        float currentAhead;
        float aheadVelocity;
        Vector3 basePosition;
        float shakeAmp, shakeDuration, shakeTime;
        bool hasOverride;
        Vector3 overridePos, overrideLook;
        float overrideSmooth = 0.8f;

        public Transform Target => target;
        public void SetTarget(Transform t) => target = t;

        public static void Shake(float amplitude, float duration)
        {
            if (Active == null) return;
            // 진행 중인 흔들림보다 셀 때만 덮어쓴다
            float remaining = Active.shakeDuration > 0f ? Active.shakeAmp * (1f - Active.shakeTime / Active.shakeDuration) : 0f;
            if (amplitude < remaining) return;
            Active.shakeAmp = Mathf.Min(amplitude, Active.maxShake);
            Active.shakeDuration = Mathf.Max(0.01f, duration);
            Active.shakeTime = 0f;
        }

        public void SetFramingOverride(Vector3 position, Vector3 lookAt, float smooth = 0.8f)
        {
            hasOverride = true;
            overridePos = position;
            overrideLook = lookAt;
            overrideSmooth = smooth;
        }

        public void ClearFramingOverride() => hasOverride = false;

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this) Active = null;
        }

        void Start()
        {
            basePosition = transform.position;
            if (target == null) return;
            transform.rotation = Quaternion.LookRotation(lookOffset - offset);
            basePosition = target.position + offset;
            transform.position = basePosition;
        }

        void LateUpdate()
        {
            Quaternion rot;
            if (hasOverride)
            {
                basePosition = Vector3.SmoothDamp(basePosition, overridePos, ref velocity, overrideSmooth);
                Quaternion want = Quaternion.LookRotation(overrideLook - basePosition);
                rot = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-3f * Time.deltaTime));
            }
            else
            {
                if (target == null) return;
                float dirX = Mathf.Abs(target.forward.x) > 0.3f ? Mathf.Sign(target.forward.x) : 0f;
                currentAhead = Mathf.SmoothDamp(currentAhead, dirX * lookAhead, ref aheadVelocity, lookAheadSmoothTime);

                Vector3 desired = target.position + offset + Vector3.right * currentAhead;
                basePosition = Vector3.SmoothDamp(basePosition, desired, ref velocity, smoothTime);
                rot = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookOffset - offset), 1f - Mathf.Exp(-6f * Time.deltaTime));
            }

            Vector3 shake = Vector3.zero;
            if (shakeTime < shakeDuration)
            {
                shakeTime += Time.unscaledDeltaTime;
                float k = 1f - shakeTime / shakeDuration;
                float a = shakeAmp * k * k;
                float tt = Time.unscaledTime * 32f;
                shake = new Vector3(Mathf.PerlinNoise(tt, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tt) - 0.5f, 0f) * (2f * a);
            }

            transform.SetPositionAndRotation(basePosition + rot * shake, rot);
        }
    }
}
