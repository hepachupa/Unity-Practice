using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 2.5D 횡스크롤에서 캐릭터 "모델"만 카메라 쪽으로 돌려 얼굴이 보이게 한다 (몸통 루트 방향은 그대로).
    /// - 이동 중: 진행 방향에서 카메라 쪽으로 moveTurn도만 비튼 3/4 시점
    /// - 멈춰 있으면 idleDelay 뒤 정면(카메라)을 본다
    /// - 상자를 잡고 있거나 턱을 오르는 중에는 진행 방향 그대로 (손 IK·오르기 동작이 어긋나지 않게)
    /// 모델(Animator) 오브젝트에 붙인다. 루트 모션을 쓰지 않으므로 Animator와 충돌하지 않는다.
    /// </summary>
    public class SideScrollFacing : MonoBehaviour
    {
        [SerializeField, Range(0f, 80f)] float moveTurn = 35f;
        [SerializeField] float idleDelay = 0.35f;
        [SerializeField] float smoothTime = 0.15f;
        [SerializeField] float idleSpeed = 0.2f;

        PlayerMotor motor;
        PlayerGrab grab;
        float yawOffset, yawVelocity, idleTime;

        void Awake()
        {
            motor = GetComponentInParent<PlayerMotor>();
            grab = GetComponentInParent<PlayerGrab>();
        }

        void LateUpdate()
        {
            if (motor == null) return;
            Transform root = motor.transform;

            bool busy = motor.IsClimbing || (grab != null && grab.Current != null);
            bool moving = motor.PlanarVelocity.sqrMagnitude > idleSpeed * idleSpeed || !motor.IsGrounded;
            idleTime = moving ? 0f : idleTime + Time.deltaTime;

            // 카메라 쪽 (= 화면 정면) 방향
            var cam = Camera.main;
            Vector3 front = cam != null ? -cam.transform.forward : Vector3.back;
            front.y = 0f;
            if (front.sqrMagnitude < 1e-4f) front = Vector3.back;
            float frontYaw = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(root.eulerAngles.y, frontYaw);

            float target = busy ? 0f
                : idleTime > idleDelay ? delta
                : Mathf.Clamp(delta, -moveTurn, moveTurn);

            yawOffset = Mathf.SmoothDampAngle(yawOffset, target, ref yawVelocity, smoothTime);
            transform.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
        }
    }
}
