using LNStudy.Combat;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>지나가면 리스폰 위치를 갱신하는 트리거 (한 번만). 선택적으로 안내 문구를 띄운다.</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [TextArea] [SerializeField] string message;
        [SerializeField] Transform respawnPoint;

        bool used;

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (used || !other.CompareTag("Player")) return;
            used = true;
            var combat = other.GetComponentInParent<PlayerCombat>();
            Transform p = respawnPoint != null ? respawnPoint : transform;
            if (combat != null) combat.SetCheckpoint(p.position, p.rotation);
            else LNStudy.Player.CatParty.Instance?.SetCheckpoint(p.position);
            if (!string.IsNullOrEmpty(message)) StageHUD.Instance?.ShowToast(message, 3f);
        }
    }
}
