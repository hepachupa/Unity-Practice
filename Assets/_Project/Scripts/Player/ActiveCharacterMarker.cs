using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 지금 조종 중인 캐릭터 머리 위에 떠 있는 작은 표시. 교체하면 새 캐릭터로 날아가며 통 튀고 효과음.
    /// 파티가 잠기면(연출 중) 숨는다.
    /// </summary>
    public class ActiveCharacterMarker : MonoBehaviour
    {
        [SerializeField] CatParty party;
        [SerializeField] Transform visual;
        [SerializeField] float headroom = 0.45f;
        [SerializeField] float bobAmplitude = 0.08f;
        [SerializeField] float spinSpeed = 120f;

        Transform target;
        float height = 1f;
        float pop;
        Vector3 vel;
        Vector3 baseScale;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) baseScale = visual.localScale;
        }

        void OnEnable()
        {
            if (party != null) party.Switched += OnSwitched;
        }

        void OnDisable()
        {
            if (party != null) party.Switched -= OnSwitched;
        }

        void OnSwitched(CatParty.Member m)
        {
            if (m == null || m.input == null) return;
            bool first = target == null;
            target = m.input.transform;
            var cap = m.input.GetComponent<CapsuleCollider>();
            height = cap != null ? cap.height : 1f;
            if (first) transform.position = TargetPos();
            pop = 1f;
            var juice = m.input.GetComponentInChildren<CharacterJuice>();
            if (juice != null) juice.Pop(0.22f);
            if (!first) Sfx.Play("switch", target.position, 0.6f, height > 1f ? 0.85f : 1.2f);
        }

        Vector3 TargetPos() => target.position + Vector3.up * (height + headroom + Mathf.Sin(Time.time * 4f) * bobAmplitude);

        void LateUpdate()
        {
            bool show = target != null && party != null && !party.Locked;
            if (visual != null && visual.gameObject.activeSelf != show) visual.gameObject.SetActive(show);
            if (!show) return;

            transform.position = Vector3.SmoothDamp(transform.position, TargetPos(), ref vel, 0.08f);
            pop = Mathf.MoveTowards(pop, 0f, Time.deltaTime * 3f);
            if (visual != null)
            {
                float s = 1f + Mathf.Sin(pop * Mathf.PI) * 0.8f;
                visual.localScale = baseScale * s;
                visual.localRotation = Quaternion.Euler(0f, Time.time * spinSpeed, 0f) * Quaternion.Euler(0f, 0f, 45f);
            }
        }
    }
}
