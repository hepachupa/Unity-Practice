using System;
using LNStudy.Interaction;
using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 잡기 버튼을 한 번 누르면 잡고, 다시 누르면 놓는다 (토글).
    /// 잡을 때는 정면을 우선하되, 몸 주변 가까운 Grabbable이면 그쪽으로 몸을 돌려 잡는다.
    /// 잡는 동안에는 방향 고정, 점프·달리기·턱 오르기를 막고, 물체 질량에 따라 속도를 줄인다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader))]
    public class PlayerGrab : MonoBehaviour
    {
        [Tooltip("몸 표면에서 이 거리 안의 물체를 잡을 수 있다")]
        [SerializeField] float reach = 0.45f;
        [Tooltip("정면에 있는 물체를 얼마나 우선할지 (0 = 거리만, 클수록 정면 우선)")]
        [SerializeField, Range(0f, 2f)] float facingBias = 0.6f;
        [SerializeField] LayerMask grabMask = ~0;
        [Tooltip("잡은 물체가 플레이어를 끌고 가지 않도록 플레이어 쪽 질량에 곱하는 값")]
        [SerializeField] float playerMassDominance = 2f;
        [Tooltip("이 시간 넘게 공중에 있으면 놓는다 (상자에 걸려 잠깐 뜨는 경우는 유지)")]
        [SerializeField] float airborneReleaseDelay = 0.25f;
        [Tooltip("잡은 물체의 수평 속도를 플레이어 속도에 맞추는 정도 (0 = Joint에만 맡김, 1 = 즉시 맞춤). " +
                 "바닥 마찰이 Joint를 이겨 물체가 버티는 것을 막는다")]
        [SerializeField, Range(0f, 1f)] float velocityAssist = 0.8f;

        public event Action<Grabbable> Grabbed, Released;
        public Grabbable Current { get; private set; }

        PlayerMotor motor;
        PlayerInputReader input;
        Rigidbody rb;
        FixedJoint joint;
        float airborneTime;
        readonly Collider[] overlapBuffer = new Collider[16];

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            rb = GetComponent<Rigidbody>();
            if (gameObject.layer != 0) grabMask &= ~(1 << gameObject.layer);
        }

        void OnDisable() => Release();

        void FixedUpdate()
        {
            bool pressed = input.ConsumeGrabPressed();

            if (Current != null)
            {
                airborneTime = motor.IsGrounded ? 0f : airborneTime + Time.fixedDeltaTime;

                // 다시 누르거나, 연결이 끊어지거나(breakForce), 공중에 오래 있으면 놓는다
                if (pressed || joint == null || airborneTime > airborneReleaseDelay)
                {
                    Release();
                    return;
                }

                // Joint만으로는 바닥 마찰 때문에 물체가 버티거나 늦게 따라온다.
                // 물체의 수평 속도를 플레이어 쪽으로 보정해, 끄는 속도는 플레이어 이동(=질량 배율)이 결정하게 한다.
                Rigidbody body = Current.Body;
                Vector3 bv = body.linearVelocity;
                Vector3 target = motor.PlanarVelocity;
                bv.x = Mathf.Lerp(bv.x, target.x, velocityAssist);
                bv.z = Mathf.Lerp(bv.z, target.z, velocityAssist);
                body.linearVelocity = bv;
                return;
            }

            if (pressed && !motor.IsClimbing)
                TryGrab();
        }

        void TryGrab()
        {
            Vector3 feet = rb.position;
            float height = motor.CurrentHeight;
            float radius = motor.Radius + reach;
            Vector3 bottom = feet + Vector3.up * radius;
            Vector3 top = feet + Vector3.up * Mathf.Max(radius, height - radius * 0.5f);

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuffer, grabMask, QueryTriggerInteraction.Ignore);

            Vector3 chest = feet + Vector3.up * (height * 0.5f);
            Vector3 fwd = transform.forward;
            Grabbable best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = overlapBuffer[i];
                var g = col.GetComponentInParent<Grabbable>();
                if (g == null || g.IsHeld) continue;

                Vector3 closest = col.ClosestPoint(chest);
                Vector3 to = closest - chest;
                to.y = 0f;
                float dist = to.magnitude;
                float facing = dist > 0.001f ? Vector3.Dot(fwd, to / dist) : 1f;

                // 거리가 가까울수록, 정면에 가까울수록 점수가 낮다 (낮을수록 우선)
                float score = dist - facing * facingBias * reach;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = g;
                }
            }

            if (best != null) Attach(best);
        }

        void Attach(Grabbable g)
        {
            // 물체 쪽으로 몸을 돌린 뒤 그 자세 그대로 연결한다
            Vector3 toObj = g.Body.worldCenterOfMass - rb.position;
            toObj.y = 0f;
            if (toObj.sqrMagnitude > 0.0001f)
            {
                Quaternion face = Quaternion.LookRotation(toObj.normalized);
                rb.rotation = face;
                transform.rotation = face;
            }

            joint = g.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = rb;
            joint.breakForce = g.BreakForce;
            joint.breakTorque = g.BreakForce;
            joint.enableCollision = false;
            // massScale은 "역질량"에 곱해진다. 물체(body) 쪽 역질량을 키워 물체를 가볍게 취급해야
            // 플레이어가 끌려가지 않고 물체를 끌고 간다. (connectedMassScale을 키우면 반대로 플레이어가 가벼워진다)
            joint.massScale = Mathf.Max(1f, g.Body.mass / rb.mass) * playerMassDominance;
            joint.connectedMassScale = 1f;

            Current = g;
            g.IsHeld = true;
            airborneTime = 0f;

            motor.LockFacing = true;
            motor.CanJump = false;
            motor.CanRun = false;
            motor.CanClimb = false;
            motor.SpeedMultiplier = g.GetDragSpeedMultiplier();

            Grabbed?.Invoke(g);
        }

        public void Release()
        {
            if (joint != null) Destroy(joint);
            joint = null;

            var prev = Current;
            if (prev != null) prev.IsHeld = false;
            Current = null;

            if (motor != null)
            {
                motor.LockFacing = false;
                motor.CanJump = true;
                motor.CanRun = true;
                motor.CanClimb = true;
                motor.SpeedMultiplier = 1f;
            }

            if (prev != null) Released?.Invoke(prev);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<CapsuleCollider>();
            if (col == null) return;
            Gizmos.color = Current != null ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (col.height * 0.5f), col.radius + reach);
        }
    }
}
