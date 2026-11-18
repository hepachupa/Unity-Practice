using System;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>피해 한 번의 정보. 맞은 쪽이 경직·넉백·이펙트를 결정할 때 쓴다.</summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;
        public float knockback;
        /// <summary>강공격 (히트스톱·큰 흔들림)</summary>
        public bool heavy;
        public GameObject source;
    }

    /// <summary>체력. 플레이어·몬스터 공용. 반응(애니메이션, AI)은 이벤트를 구독해서 처리한다.</summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] float maxHealth = 100f;
        [Tooltip("맞은 뒤 무적 시간 (플레이어 연속 피격 방지)")]
        [SerializeField] float invulnerableAfterHit;
        [Tooltip("발 기준 조준점 높이 (투사체가 노리는 곳)")]
        [SerializeField] float aimHeight = 1.2f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
        public bool IsDead { get; private set; }
        public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;

        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action Revived;

        float invulnerableUntil;

        void Awake() => Current = maxHealth;

        /// <summary>피해를 준다. 실제로 들어갔으면 true</summary>
        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || Time.time < invulnerableUntil || info.amount <= 0f) return false;

            Current = Mathf.Max(0f, Current - info.amount);
            invulnerableUntil = Time.time + invulnerableAfterHit;
            Damaged?.Invoke(info);

            if (Current <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
            }
            return true;
        }

        public void Revive()
        {
            Current = maxHealth;
            IsDead = false;
            invulnerableUntil = 0f;
            Revived?.Invoke();
        }
    }
}
