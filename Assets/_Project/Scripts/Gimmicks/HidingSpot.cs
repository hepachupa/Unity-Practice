using LNStudy.Player;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 숨을 수 있는 곳 (덤불 등). 플레이어가 안에서 웅크리고 있으면 IsPlayerHidden = true.
    /// 거인 감시자(GiantWatcher)는 숨은 플레이어를 보지 못한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HidingSpot : MonoBehaviour
    {
        static int insideCount;
        static PlayerMotor playerMotor;

        /// <summary>덤불 안 + 웅크림</summary>
        public static bool IsPlayerHidden => insideCount > 0 && playerMotor != null && playerMotor.IsCrouching;
        /// <summary>덤불 안 (웅크리지 않아도)</summary>
        public static bool IsPlayerInside => insideCount > 0;

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            insideCount++;
            playerMotor = other.GetComponentInParent<PlayerMotor>();
            if (insideCount == 1) StageHUD.Instance?.ShowToast("Ctrl로 웅크리면 숨을 수 있어", 1.5f);
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            insideCount = Mathf.Max(0, insideCount - 1);
        }

        void OnDisable() => insideCount = 0;
    }
}
