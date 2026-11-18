using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 플레이어(Player 태그)가 들어오면 onEnter 실행 + 알림. 목표 지점·이벤트 트리거용.
    /// requiredPlayers가 2면 두 캐릭터가 모두 안에 있어야 발동한다 (협동 골인 지점).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlayerTrigger : MonoBehaviour
    {
        [TextArea] [SerializeField] string message;
        [Tooltip("발동 시 화면 가운데 큰 글씨 (비우면 없음)")]
        [SerializeField] string banner;
        [SerializeField] bool once = true;
        [SerializeField, Min(1)] int requiredPlayers = 1;
        [Tooltip("들어오면 숨길 오브젝트 (예: 되찾은 케이크)")]
        [SerializeField] GameObject hideOnEnter;

        public UnityEvent onEnter = new();

        bool used;
        readonly HashSet<Rigidbody> inside = new();

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if ((once && used) || !other.CompareTag("Player")) return;
            if (other.attachedRigidbody != null) inside.Add(other.attachedRigidbody);
            inside.RemoveWhere(b => b == null);
            if (inside.Count < requiredPlayers) return;

            used = true;
            if (!string.IsNullOrEmpty(message)) StageHUD.Instance?.ShowToast(message, 3f);
            if (!string.IsNullOrEmpty(banner)) StageHUD.Instance?.ShowBanner(banner, 5f);
            if (hideOnEnter != null) hideOnEnter.SetActive(false);
            onEnter.Invoke();
        }

        void OnTriggerExit(Collider other)
        {
            if (other.attachedRigidbody != null) inside.Remove(other.attachedRigidbody);
        }
    }
}
