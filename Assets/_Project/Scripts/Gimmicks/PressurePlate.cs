using System.Collections.Generic;
using LNStudy.Interaction;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using LNStudy.CameraSystem;
using LNStudy.Tools;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 무게로 눌리는 발판. 트리거 안 물체들의 무게 합이 requiredWeight 이상이면 눌림 → onPressed, 아래로 내려가면 onReleased.
    /// 무게: PuzzleWeight가 있으면 그 값, 없으면 Grabbable(상자)의 Rigidbody 질량, 둘 다 없으면 0.
    /// latch면 한 번 눌린 뒤 계속 눌린 상태로 남는다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class PressurePlate : MonoBehaviour
    {
        [FormerlySerializedAs("requiredMass")]
        [SerializeField] float requiredWeight = 30f;
        [SerializeField] bool latch = true;
        [Tooltip("눌렸을 때 띄울 알림 (비우면 없음)")]
        [SerializeField] string pressedToast = "철컥! 발판이 눌렸다";
        [Header("Visual")]
        [SerializeField] Transform plate;
        [SerializeField] float pressDepth = 0.1f;
        [SerializeField] Renderer indicator;
        [SerializeField] Color idleColor = new Color(1f, 0.55f, 0.2f);
        [SerializeField] Color pressedColor = new Color(0.35f, 1f, 0.45f);
        [SerializeField] Light indicatorLight;
        [Tooltip("눌림/떼짐 효과음·불빛 번쩍임·먼지")]
        [SerializeField] bool feedback = true;

        public UnityEvent onPressed = new();
        public UnityEvent onReleased = new();

        public bool IsPressed { get; private set; }
        public float CurrentWeight { get; private set; }

        readonly HashSet<Rigidbody> bodies = new();
        readonly List<Rigidbody> dead = new();
        Vector3 plateRest;
        float lightBase, pulse;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void Awake()
        {
            if (plate != null) plateRest = plate.localPosition;
            if (indicatorLight != null) lightBase = indicatorLight.intensity;
            ApplyColor(false);
        }

        static float WeightOf(Rigidbody rb)
        {
            if (rb == null) return 0f;
            var w = rb.GetComponent<PuzzleWeight>();
            if (w != null) return w.Value;
            return rb.GetComponent<Grabbable>() != null ? rb.mass : 0f;
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (WeightOf(rb) > 0f) bodies.Add(rb);
        }

        void OnTriggerExit(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb != null) bodies.Remove(rb);
        }

        void FixedUpdate()
        {
            dead.Clear();
            float total = 0f;
            foreach (var b in bodies)
            {
                // kinematic은 빼되(움직이는 발판 등), 놓으면 kinematic이 되는 바리케이드(Grabbable)는 계속 센다
                if (b == null || !b.gameObject.activeInHierarchy || (b.isKinematic && b.GetComponent<Grabbable>() == null)) { dead.Add(b); continue; }
                total += WeightOf(b);
            }
            foreach (var b in dead) bodies.Remove(b);
            CurrentWeight = total;

            bool pressed = total >= requiredWeight || (latch && IsPressed);
            if (pressed != IsPressed)
            {
                IsPressed = pressed;
                ApplyColor(pressed);
                if (feedback) Feedback(pressed);
                if (pressed)
                {
                    onPressed.Invoke();
                    if (!string.IsNullOrEmpty(pressedToast)) StageHUD.Instance?.ShowToast(pressedToast, 2f);
                }
                else onReleased.Invoke();
            }

            if (plate != null)
            {
                Vector3 target = plateRest + Vector3.down * (IsPressed ? pressDepth : 0f);
                plate.localPosition = Vector3.Lerp(plate.localPosition, target, 10f * Time.fixedDeltaTime);
            }
        }

        void Feedback(bool pressed)
        {
            bool heavy = requiredWeight >= 5f;
            Vector3 p = transform.position;
            if (pressed)
            {
                Sfx.Play(heavy ? "clunk" : "click", p, heavy ? 0.8f : 0.6f, heavy ? 1f : 1.15f);
                pulse = 1f;
                DustFx.Puff(p + Vector3.up * 0.05f, heavy ? 6 : 3, 0.3f, 1.2f);
                if (heavy) SideFollowCamera.Shake(0.035f, 0.15f);
            }
            else Sfx.Play("click", p, 0.4f, 0.8f);
        }

        void Update()
        {
            if (indicatorLight == null || pulse <= 0f) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 2.5f);
            indicatorLight.intensity = lightBase * (1f + pulse * 2.5f);
        }

        void ApplyColor(bool pressed)
        {
            Color c = pressed ? pressedColor : idleColor;
            if (indicator != null)
            {
                mpb ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                mpb.SetColor(EmissionId, c * 1.5f);
                indicator.SetPropertyBlock(mpb);
            }
            if (indicatorLight != null) indicatorLight.color = c;
        }
    }
}
