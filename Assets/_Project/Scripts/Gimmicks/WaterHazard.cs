using LNStudy.Combat;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>깊은 물·구덩이. 플레이어가 빠지면 피해를 입고 마지막 체크포인트로 돌아간다.</summary>
    [RequireComponent(typeof(Collider))]
    public class WaterHazard : MonoBehaviour
    {
        [SerializeField] float damage = 10f;
        [SerializeField] string message = "풍덩! 물에 빠졌다";

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var combat = other.GetComponentInParent<PlayerCombat>();
            if (combat == null) return;
            combat.ReturnToCheckpoint(damage);
            if (!string.IsNullOrEmpty(message)) StageHUD.Instance?.ShowToast(message);
        }
    }
}
