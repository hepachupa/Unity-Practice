using System;
using System.Collections;
using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 플레이어 이동 (Rigidbody 기반). 걷기·달리기·웅크리기·점프·턱 오르기를 담당한다.
    /// moveSpace가 비어 있으면 2.5D(입력 X = 월드 X, 입력 Y = 월드 Z),
    /// 카메라를 넣으면 3인칭(카메라가 보는 방향 기준)으로 움직인다.
    /// 다른 시스템(잡기, 전투)은 SpeedMultiplier / LockFacing / Can* 플래그로만 이동에 개입한다.
    /// 피벗은 발바닥 기준 (CapsuleCollider.center.y = height / 2).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PlayerInputReader))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speed")]
        [SerializeField] float walkSpeed = 2.2f;
        [SerializeField] float runSpeed = 4.0f;
        [SerializeField] float crouchSpeed = 1.1f;
        [SerializeField] float acceleration = 25f;
        [SerializeField] float deceleration = 35f;
        [Tooltip("공중에서의 가감속 비율")]
        [SerializeField, Range(0f, 1f)] float airControl = 0.35f;
        [SerializeField] float turnSpeed = 720f;
        [Tooltip("이동 입력의 기준 방향 (3인칭이면 카메라). 비우면 월드 축 기준")]
        [SerializeField] Transform moveSpace;
        [Tooltip("2.5D 횡스크롤: X축으로만 움직이고 Z 위치를 고정한다 (위/아래 입력 무시)")]
        [SerializeField] bool sideScroll;
        [Tooltip("입력이 없을 때 이 속도 아래면 즉시 멈춘다 (마찰 0 재질이라 잔여 속도로 미끄러지는 것 방지)")]
        [SerializeField] float idleSnapSpeed = 0.6f;

        [Header("Jump / Gravity")]
        [SerializeField] float jumpHeight = 1.2f;
        [Tooltip("떨어질 때 중력 배율 (점프가 둥둥 뜨지 않게)")]
        [SerializeField] float fallGravityMultiplier = 2f;
        [SerializeField] float jumpBufferTime = 0.12f;
        [Tooltip("발판에서 벗어난 직후에도 점프를 허용하는 시간")]
        [SerializeField] float coyoteTime = 0.1f;

        [Header("Ground Check")]
        [Tooltip("자기 레이어는 Awake에서 자동으로 제외된다")]
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float groundProbeDistance = 0.08f;
        [SerializeField, Range(0f, 89f)] float maxSlopeAngle = 45f;
        [Header("Edge Slip")]
        [Tooltip("모서리에 살짝 걸쳤을 때(몸 중심 아래에 바닥이 없을 때) 미끄러져 떨어진다")]
        [SerializeField] bool edgeSlip = true;
        [Tooltip("중심에서 이 비율(반지름 기준) 이상 바깥에 걸쳤으면 미끄러짐. 작을수록 잘 미끄러진다")]
        [SerializeField, Range(0f, 1f)] float edgeSlipThreshold = 0.2f;
        [SerializeField] float edgeSlipSpeed = 2.2f;
        [Tooltip("공중에서 발보다 높은 모서리를 들이받으면 그쪽 속도를 없앤다 (둥근 캡슐이 모서리를 타고 미끄러져 올라가는 것 방지). 턱 오르기는 점프 키로만")]
        [SerializeField] bool blockAirStepUp = true;

        [Header("Crouch")]
        [SerializeField] float standHeight = 1.2f;
        [SerializeField] float crouchHeight = 0.7f;

        [Header("Ledge Climb")]
        [Tooltip("이 높이보다 낮은 턱은 오르지 않는다")]
        [SerializeField] float minClimbHeight = 0.35f;
        [SerializeField] float maxClimbHeight = 1.6f;
        [Tooltip("켜면 점프로 올라갈 수 있는 높이(jumpHeight + 여유)의 턱은 오르기 대신 점프로 처리한다. " +
                 "낮은 모서리에 걸쳤을 때 갑자기 위로 끌려 올라가는 현상을 막는다")]
        [SerializeField] bool climbOnlyAboveJumpReach = true;
        [SerializeField] float jumpReachMargin = 0.05f;
        [Tooltip("공중에서 벽 쪽으로 이동하기만 해도 턱을 자동으로 잡을지 (끄면 점프 버튼을 눌러야 오른다)")]
        [SerializeField] bool autoMantleInAir = false;
        [SerializeField] float wallProbeDistance = 0.3f;
        [SerializeField] float climbUpTime = 0.3f;
        [SerializeField] float climbForwardTime = 0.15f;

        public event Action Jumped, Landed, ClimbStarted, ClimbFinished;

        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsClimbing { get; private set; }
        public bool IsRunning { get; private set; }
        /// <summary>마지막으로 땅에 닿은 뒤 지난 시간 (점프하면 즉시 큰 값)</summary>
        public float TimeSinceGrounded => IsGrounded ? 0f : Time.time - lastGroundedTime;
        public Vector3 PlanarVelocity => new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        public float CurrentHeight => capsule.height;
        public float Radius => capsule.radius;

        /// <summary>실제로 오르기가 허용되는 최소 높이</summary>
        public float EffectiveMinClimbHeight =>
            climbOnlyAboveJumpReach ? Mathf.Max(minClimbHeight, jumpHeight + jumpReachMargin) : minClimbHeight;

        /// <summary>외부 시스템이 거는 속도 배율 (1 = 기본). 예: 무거운 물체를 끌 때</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>true면 이동 방향으로 회전하지 않는다. 예: 물체를 잡고 있을 때</summary>
        public bool LockFacing
        {
            get => lockFacing || facingLocks.Count > 0;
            set => lockFacing = value;
        }

        /// <summary>
        /// 여러 시스템이 동시에 회전을 잠글 때 서로 덮어쓰지 않도록 요청자별로 잠근다.
        /// (예: 전투가 매 프레임 LockFacing=false를 써서 잡기의 잠금을 풀어버리던 문제)
        /// </summary>
        public void SetFacingLock(object owner, bool locked)
        {
            if (locked) facingLocks.Add(owner);
            else facingLocks.Remove(owner);
        }

        bool lockFacing;
        readonly System.Collections.Generic.HashSet<object> facingLocks = new();
        public bool CanJump { get; set; } = true;
        public bool CanRun { get; set; } = true;
        public bool CanClimb { get; set; } = true;
        /// <summary>이동 입력의 기준 방향. null이면 월드 축</summary>
        public Transform MoveSpace { get => moveSpace; set => moveSpace = value; }
        public bool SideScroll => sideScroll;

        /// <summary>연출 등에서 횡스크롤 고정을 잠시 풀거나 다시 건다 (Z 위치 고정 포함)</summary>
        public void SetSideScroll(bool on)
        {
            sideScroll = on;
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (on) rb.constraints |= RigidbodyConstraints.FreezePositionZ;
            else rb.constraints &= ~RigidbodyConstraints.FreezePositionZ;
        }

        /// <summary>AI 동료용: true면 입력 대신 ExternalMove(월드 방향, 길이 0~1)로 움직인다</summary>
        public bool UseExternalMove { get; set; }
        public Vector3 ExternalMove { get; set; }
        public bool ExternalRun { get; set; }

        /// <summary>
        /// 넉백 같은 외부 충격. 일반 AddForce는 다음 물리 프레임에 이동 로직이 바로 덮어써서 무시되거나,
        /// 반대로 정지 브레이크가 꺼진 채 남으면 계속 밀린다. 여기서는 정해진 시간 동안만 감속 곡선으로 밀리게 한다.
        /// </summary>
        public void AddImpulse(Vector3 velocityChange, float duration = 0.18f)
        {
            if (rb == null || rb.isKinematic) return;
            velocityChange.y = 0f;
            rb.linearVelocity += velocityChange;
            impulseUntil = Time.time + duration;
        }

        /// <summary>캐릭터 교체 시 몸체 크기 변경</summary>
        public void SetBodySize(float newStandHeight, float newCrouchHeight, float radius)
        {
            standHeight = newStandHeight;
            crouchHeight = Mathf.Min(newCrouchHeight, newStandHeight);
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            capsule.radius = radius;
            SetCapsuleHeight(IsCrouching ? crouchHeight : standHeight);
        }

        /// <summary>즉시 정지 (사망·리스폰 등)</summary>
        public void Halt()
        {
            if (rb == null || rb.isKinematic) return;
            rb.linearVelocity = Vector3.zero;
            impulseUntil = 0f;
        }

        public struct LedgeInfo
        {
            public Vector3 top;
            public Vector3 standPoint;
            public Vector3 forward;
        }

        Rigidbody rb;
        CapsuleCollider capsule;
        PlayerInputReader input;
        Vector3 groundNormal = Vector3.up;
        Vector3 slipDir;
        /// <summary>모서리에 걸쳐 미끄러지는 중</summary>
        public bool IsSlipping { get; private set; }
        float lastGroundedTime = -999f;
        int obstacleMask;
        Coroutine climbRoutine;
        float impulseUntil;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            input = GetComponent<PlayerInputReader>();

            rb.freezeRotation = true;
            if (sideScroll) rb.constraints |= RigidbodyConstraints.FreezePositionZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (gameObject.layer == 0)
                Debug.LogWarning("[PlayerMotor] 플레이어가 Default 레이어에 있습니다. 지면 판정이 자기 자신을 맞히지 않도록 전용 레이어(Player)로 옮기세요.", this);
            else
                groundMask &= ~(1 << gameObject.layer);
            obstacleMask = groundMask;

            // 벽에 붙어 미끄러지지 않도록 마찰 0 재질
            if (capsule.sharedMaterial == null)
            {
                capsule.sharedMaterial = new PhysicsMaterial("PlayerNoFriction")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }

            SetCapsuleHeight(standHeight);
        }

        void OnDisable()
        {
            if (climbRoutine != null)
            {
                StopCoroutine(climbRoutine);
                climbRoutine = null;
            }
            if (IsClimbing)
            {
                rb.isKinematic = false;
                IsClimbing = false;
            }
        }

        void FixedUpdate()
        {
            if (IsClimbing) return;

            bool wasGrounded = IsGrounded;
            UpdateGround();
            if (IsGrounded) lastGroundedTime = Time.time;
            if (!wasGrounded && IsGrounded) Landed?.Invoke();

            UpdateCrouch();

            Vector2 mv = input.Move;
            Vector3 wishDir = UseExternalMove ? Vector3.ProjectOnPlane(ExternalMove, Vector3.up) : ToWorldDirection(mv);
            if (sideScroll) wishDir.z = 0f;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();
            bool hasInput = wishDir.sqrMagnitude > 0.01f;

            IsRunning = CanRun && !IsCrouching && (input.RunHeld || (UseExternalMove && ExternalRun)) && hasInput;
            float speed = IsCrouching ? crouchSpeed : (IsRunning ? runSpeed : walkSpeed);
            Vector3 targetPlanar = wishDir * speed * SpeedMultiplier;

            Vector3 v = rb.linearVelocity;
            Vector3 planar = new Vector3(v.x, 0f, v.z);
            bool knocked = Time.time < impulseUntil;
            float rate = targetPlanar.sqrMagnitude >= planar.sqrMagnitude ? acceleration : deceleration;
            if (!IsGrounded) rate *= airControl;
            if (knocked) rate *= 0.35f; // 넉백 중에는 천천히 감속 (밀려나는 느낌)
            planar = Vector3.MoveTowards(planar, targetPlanar, rate * Time.fixedDeltaTime);

            // 서 있을 때는 잔여 속도를 남기지 않는다
            if (IsGrounded && !hasInput && !knocked && planar.sqrMagnitude < idleSnapSpeed * idleSnapSpeed)
                planar = Vector3.zero;

            // 지면에서는 경사면에 투영해 비탈을 오르내릴 때 튀지 않게 한다
            Vector3 newV = IsGrounded
                ? Vector3.ProjectOnPlane(planar, groundNormal)
                : new Vector3(planar.x, v.y, planar.z);

            // 점프 입력: 점프로 닿지 않는 높은 턱이면 오르고, 아니면 점프
            if (input.HasBufferedJump(jumpBufferTime))
            {
                if (CanClimb && TryFindLedge(out var ledge))
                {
                    input.ClearJump();
                    climbRoutine = StartCoroutine(ClimbRoutine(ledge));
                    return;
                }

                if (CanJump && !IsCrouching && Time.time - lastGroundedTime <= coyoteTime)
                {
                    input.ClearJump();
                    newV.y = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
                    IsGrounded = false;
                    lastGroundedTime = -999f;
                    Jumped?.Invoke();
                }
            }
            // (옵션) 공중에서 벽 쪽으로 이동 중이면 턱을 자동으로 잡는다
            else if (autoMantleInAir && !IsGrounded && CanClimb && hasInput && v.y < 1.5f
                     && Vector3.Dot(wishDir.normalized, transform.forward) > 0.5f
                     && TryFindLedge(out var airLedge))
            {
                climbRoutine = StartCoroutine(ClimbRoutine(airLedge));
                return;
            }

            if (blockAirStepUp && !IsGrounded) BlockAirStepUp(ref newV);

            // 모서리 걸침: 바깥쪽으로 최소 속도를 보장해 미끄러져 떨어지게
            if (IsSlipping)
            {
                float along = Vector3.Dot(new Vector3(newV.x, 0f, newV.z), slipDir);
                if (along < edgeSlipSpeed) newV += slipDir * (edgeSlipSpeed - along);
            }

            if (!IsGrounded && newV.y < 0f)
                newV += Physics.gravity * ((fallGravityMultiplier - 1f) * Time.fixedDeltaTime);

            rb.linearVelocity = newV;

            if (!LockFacing && hasInput)
            {
                Quaternion targetRot = Quaternion.LookRotation(wishDir);
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
            }
        }

        /// <summary>
        /// 공중에서 진행 방향 앞에 "윗면이 발보다 높은" 물체가 있으면(= 몸통·발 옆면으로 부딪치는 중)
        /// 그 물체 쪽 수평 속도를 없앤다. 그대로 두면 마찰 0 캡슐의 둥근 아래쪽이 모서리를 타고 위로 밀려 올라가
        /// 살짝만 걸쳐도 상자 위로 "쓱" 올라가 버린다. 이제는 옆면을 따라 그대로 떨어진다.
        /// </summary>
        void BlockAirStepUp(ref Vector3 newV)
        {
            Vector3 planarV = new Vector3(newV.x, 0f, newV.z);
            float sp = planarV.magnitude;
            if (sp < 0.01f) return;
            Vector3 dir = planarV / sp;
            float r = capsule.radius * 0.98f;
            Vector3 bottom = rb.position + Vector3.up * r;
            Vector3 top = rb.position + Vector3.up * Mathf.Max(r, capsule.height - r);
            float dist = sp * Time.fixedDeltaTime + 0.05f;
            if (!Physics.CapsuleCast(bottom, top, r, dir, out var hit, dist, groundMask, QueryTriggerInteraction.Ignore)) return;
            if (hit.point.y <= rb.position.y + 0.03f) return; // 발 아래 높이 = 위에 내려앉는 중 (막지 않음)

            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-4f) n = -dir;
            n.Normalize();
            float into = Vector3.Dot(planarV, -n);
            if (into <= 0f) return;
            planarV += n * into;
            newV.x = planarV.x;
            newV.z = planarV.z;
        }

        Vector3 ToWorldDirection(Vector2 mv)
        {
            if (moveSpace == null) return new Vector3(mv.x, 0f, mv.y);

            // 카메라가 위아래를 봐도 이동은 수평으로만
            Vector3 fwd = Vector3.ProjectOnPlane(moveSpace.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(moveSpace.up, Vector3.up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            return right * mv.x + fwd * mv.y;
        }

        void UpdateGround()
        {
            // 점프 직후 상승 중에는 지면으로 보지 않는다 (경사 투영이 점프 속도를 지우지 않게)
            if (rb.linearVelocity.y > 0.5f)
            {
                IsGrounded = false;
                IsSlipping = false;
                groundNormal = Vector3.up;
                return;
            }

            float r = capsule.radius * 0.95f;
            Vector3 origin = rb.position + Vector3.up * (r + 0.05f);
            if (Physics.SphereCast(origin, r, Vector3.down, out var hit, 0.05f + groundProbeDistance,
                    groundMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle)
            {
                IsGrounded = true;
                groundNormal = hit.normal;
                IsSlipping = false;

                // 모서리 걸침: 몸 중심 바로 아래에 바닥이 없고, 닿은 지점이 중심에서 충분히 바깥이면 미끄러뜨린다
                if (edgeSlip)
                {
                    Vector3 offset = hit.point - rb.position;
                    offset.y = 0f;
                    if (sideScroll) offset.z = 0f;
                    bool centerSupported = Physics.Raycast(rb.position + Vector3.up * 0.1f, Vector3.down,
                        0.15f + groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);
                    if (!centerSupported && offset.magnitude > capsule.radius * edgeSlipThreshold)
                    {
                        IsGrounded = false;
                        IsSlipping = true;
                        groundNormal = Vector3.up;
                        slipDir = -offset.normalized;
                    }
                }
            }
            else
            {
                IsGrounded = false;
                IsSlipping = false;
                groundNormal = Vector3.up;
            }
        }

        void UpdateCrouch()
        {
            bool want = input.CrouchHeld && IsGrounded;
            if (want && !IsCrouching)
            {
                SetCapsuleHeight(crouchHeight);
                IsCrouching = true;
            }
            else if (!want && IsCrouching && HasRoomToStand(rb.position))
            {
                SetCapsuleHeight(standHeight);
                IsCrouching = false;
            }
        }

        bool HasRoomToStand(Vector3 feet)
        {
            float r = capsule.radius;
            Vector3 bottom = feet + Vector3.up * (r + 0.02f);
            Vector3 top = feet + Vector3.up * (standHeight - r);
            return !Physics.CheckCapsule(bottom, top, r * 0.95f, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        void SetCapsuleHeight(float h)
        {
            capsule.height = h;
            capsule.center = new Vector3(0f, h * 0.5f, 0f);
        }

        /// <summary>정면의 턱을 찾는다. 벽 감지 → 위에서 아래로 윗면 탐색 → 올라설 공간 확인.</summary>
        public bool TryFindLedge(out LedgeInfo ledge)
        {
            ledge = default;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return false;
            fwd.Normalize();

            Vector3 feet = rb.position;
            float r = capsule.radius;
            float probeLen = r + wallProbeDistance;
            float minH = EffectiveMinClimbHeight;
            if (minH >= maxClimbHeight) return false;

            // 1) 정면 벽: 가슴 높이 또는 무릎 높이 중 하나라도 맞으면 벽이 있다고 본다
            Vector3 chest = feet + Vector3.up * Mathf.Min(capsule.height * 0.75f, maxClimbHeight);
            Vector3 knee = feet + Vector3.up * Mathf.Min(minH * 0.8f, capsule.height * 0.5f);
            if (!Physics.Raycast(chest, fwd, out var wall, probeLen, obstacleMask, QueryTriggerInteraction.Ignore) &&
                !Physics.Raycast(knee, fwd, out wall, probeLen, obstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            // 2) 벽 안쪽 위에서 아래로 쏴서 윗면을 찾는다
            Vector3 downOrigin = wall.point + fwd * (r + 0.05f);
            downOrigin.y = feet.y + maxClimbHeight + 0.1f;
            float downLen = maxClimbHeight + 0.1f - minH + 0.05f;
            if (!Physics.Raycast(downOrigin, Vector3.down, out var top, downLen, obstacleMask, QueryTriggerInteraction.Ignore))
                return false;
            if (top.normal.y < 0.7f) return false;

            float h = top.point.y - feet.y;
            if (h < minH || h > maxClimbHeight) return false;

            // 3) 올라선 자리에 서 있을 공간이 있는지
            Vector3 stand = top.point + Vector3.up * 0.02f;
            if (!HasRoomToStand(stand)) return false;

            ledge = new LedgeInfo { top = top.point, standPoint = stand, forward = fwd };
            return true;
        }

        IEnumerator ClimbRoutine(LedgeInfo ledge)
        {
            IsClimbing = true;
            ClimbStarted?.Invoke();

            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;

            Vector3 start = rb.position;
            Vector3 mid = new Vector3(start.x, ledge.standPoint.y, start.z);
            yield return MoveOver(start, mid, climbUpTime);
            yield return MoveOver(mid, ledge.standPoint, climbForwardTime);

            rb.isKinematic = false;
            IsClimbing = false;
            climbRoutine = null;
            ClimbFinished?.Invoke();
        }

        IEnumerator MoveOver(Vector3 from, Vector3 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.fixedDeltaTime;
                rb.MovePosition(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration))));
                yield return new WaitForFixedUpdate();
            }
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<CapsuleCollider>();
            if (col == null) return;
            Vector3 feet = transform.position;
            Vector3 fwd = transform.forward;
            float len = col.radius + wallProbeDistance;
            float minH = climbOnlyAboveJumpReach ? Mathf.Max(minClimbHeight, jumpHeight + jumpReachMargin) : minClimbHeight;

            Gizmos.color = Color.cyan;
            Vector3 chest = feet + Vector3.up * Mathf.Min(col.height * 0.75f, maxClimbHeight);
            Vector3 knee = feet + Vector3.up * Mathf.Min(minH * 0.8f, col.height * 0.5f);
            Gizmos.DrawLine(chest, chest + fwd * len);
            Gizmos.DrawLine(knee, knee + fwd * len);

            // 노란 막대 = 오르기가 발동하는 높이 범위
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(feet + Vector3.up * ((minH + maxClimbHeight) * 0.5f) + fwd * len,
                new Vector3(0.05f, Mathf.Max(0f, maxClimbHeight - minH), 0.05f));
        }
    }
}
