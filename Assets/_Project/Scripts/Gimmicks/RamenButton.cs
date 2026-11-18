using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 라멘을 맞혀야 눌리는 버튼 (높은 곳·물 건너편 등 손이 닿지 않는 곳에 둔다).
    /// RamenProjectile이 터질 때 NotifyImpact로 근처 버튼에 알린다 → 드론 공격으로는 못 누름 (시사 전용 기믹).
    /// resetAfter > 0 이면 일정 시간 뒤 다시 꺼진다 (동시에 여러 개 눌러야 하는 퍼즐용).
    /// </summary>
    public class RamenButton : MonoBehaviour
    {
        static readonly List<RamenButton> all = new();

        [Tooltip("이 거리 안에서 라멘이 터지면 눌림")]
        [SerializeField] float hitRadius = 1.2f;
        [Tooltip("0이면 한 번 눌리면 계속 유지")]
        [SerializeField] float resetAfter;
        [SerializeField] Renderer indicator;
        [SerializeField] Light indicatorLight;
        [SerializeField] Color idleColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField] Color pressedColor = new Color(0.25f, 1f, 0.45f);
        [SerializeField] Transform plate;
        [SerializeField] float pressDepth = 0.12f;
        [Tooltip("아직 안 눌렸을 때 위치를 알려주는 빛기둥 (눌리면 꺼짐)")]
        [SerializeField] GameObject idleBeacon;

        public UnityEvent onPressed = new();
        public UnityEvent onReleased = new();

        public bool IsPressed { get; private set; }

        float releaseAt;
        Vector3 plateUp;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>라멘 폭발 지점 근처의 버튼을 누른다</summary>
        public static void NotifyImpact(Vector3 point, float splashRadius)
        {
            foreach (var b in all)
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                float reach = b.hitRadius + splashRadius * 0.35f;
                if ((b.transform.position - point).sqrMagnitude <= reach * reach) b.Press();
            }
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (plate != null) plateUp = plate.localPosition;
            ApplyVisual();
        }

        public void Press()
        {
            if (resetAfter > 0f) releaseAt = Time.time + resetAfter;
            if (IsPressed) return;
            IsPressed = true;
            ApplyVisual();
            onPressed.Invoke();
        }

        public void Release()
        {
            if (!IsPressed) return;
            IsPressed = false;
            ApplyVisual();
            onReleased.Invoke();
        }

        void Update()
        {
            if (IsPressed && resetAfter > 0f && Time.time >= releaseAt) Release();

            if (plate != null)
            {
                Vector3 target = plateUp + Vector3.down * (IsPressed ? pressDepth : 0f);
                plate.localPosition = Vector3.MoveTowards(plate.localPosition, target, Time.deltaTime * 1.5f);
            }

            // 대기 중엔 깜빡여서 "여기를 맞혀라"를 알려준다
            if (!IsPressed && indicatorLight != null)
                indicatorLight.intensity = 2f + Mathf.Sin(Time.time * 4f) * 1.2f;
        }

        void ApplyVisual()
        {
            Color c = IsPressed ? pressedColor : idleColor;
            if (indicator != null)
            {
                indicator.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                mpb.SetColor(EmissionId, c * 3f);
                indicator.SetPropertyBlock(mpb);
            }
            if (idleBeacon != null) idleBeacon.SetActive(!IsPressed);
            if (indicatorLight != null)
            {
                indicatorLight.color = c;
                indicatorLight.intensity = IsPressed ? 3f : 2f;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsPressed ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
    }
}
