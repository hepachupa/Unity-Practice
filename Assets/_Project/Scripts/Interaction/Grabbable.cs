using System;
using UnityEngine;

namespace LNStudy.Interaction
{
    /// <summary>
    /// 플레이어가 잡고 끌 수 있는 물체.
    /// 질량에 따라 끄는 동안의 이동 속도가 줄어들고, 너무 무거우면 움직일 수 없다.
    /// 잡힘 상태가 바뀌면 HeldChanged → 바리케이드는 잡혀 있을 때만 동적 강체가 된다 (플레이어만 옮길 수 있게).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        [Tooltip("이 질량 이상이면 잡을 수는 있지만 끌 수 없다")]
        [SerializeField, Min(0.1f)] float maxDragMass = 80f;

        [Tooltip("maxDragMass에 가까울 때의 이동 속도 배율")]
        [SerializeField, Range(0.05f, 1f)] float minSpeedMultiplier = 0.25f;

        [Tooltip("잡은 연결이 끊어지는 힘. Infinity면 끊어지지 않는다")]
        [SerializeField] float breakForce = Mathf.Infinity;

        public Rigidbody Body { get; private set; }
        public float BreakForce => breakForce;

        bool held;
        public bool IsHeld
        {
            get => held;
            set
            {
                if (held == value) return;
                held = value;
                HeldChanged?.Invoke(value);
            }
        }

        /// <summary>잡힘/놓임 (인자: 잡혔는지)</summary>
        public event Action<bool> HeldChanged;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        /// <summary>끄는 동안 플레이어에게 걸 속도 배율. 0이면 움직일 수 없다.</summary>
        public float GetDragSpeedMultiplier()
        {
            if (Body.mass >= maxDragMass) return 0f;
            return Mathf.Lerp(1f, minSpeedMultiplier, Body.mass / maxDragMass);
        }
    }
}
