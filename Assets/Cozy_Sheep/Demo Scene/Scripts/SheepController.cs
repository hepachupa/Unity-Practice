using UnityEngine;
using UnityEngine.InputSystem;

namespace CozySheep
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]

    public class SheepController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float mouseSensitivity = 0.4f;
        [SerializeField] private float gravity = -20f;

        private const float AccelerationSmoothing = 0.1f;
        private const float KeyboardTurnSpeed = 90f;
        private const float GamepadSensitivity = 120f;
        private const float MouseRotationSmooth = 14f;
        private const float AnimatorSpeedDamp = 0.1f;

        private CharacterController _cc;
        private Animator _animator;

        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _lookAction;

        private Vector2 _rawInput;
        private float _yaw;
        private float _targetYaw;
        private Vector3 _smoothVelocity;
        private float _verticalVelocity;
        private float _currentSpeed;
        private float _smoothedSpeed;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashIsRunning = Animator.StringToHash("IsRunning");

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();

            if (inputActions == null)
            {
                Debug.LogError("[SheepController] InputActionAsset is not assigned.");
                return;
            }

            var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
            map.Enable();

            _moveAction = map.FindAction("Move", throwIfNotFound: true);
            _sprintAction = map.FindAction("Sprint", throwIfNotFound: true);
            _lookAction = map.FindAction("Look", throwIfNotFound: false)
                         ?? InputSystem.actions?.FindAction("Look", throwIfNotFound: false);

            _lookAction?.Enable();

            _yaw = transform.eulerAngles.y;
            _targetYaw = _yaw;
        }

        private void OnDestroy()
        {
            inputActions?.FindActionMap("Player")?.Disable();
        }

        private void Update()
        {
            if (_moveAction == null) return;

            ReadInput();
            HandleMovement();
            HandleGravity();
            UpdateAnimator();
        }

        private void ReadInput()
        {
            _rawInput = _moveAction.ReadValue<Vector2>();

            float mouseX = 0f;
            if (_lookAction != null)
            {
                Vector2 look = _lookAction.ReadValue<Vector2>();
                bool isGamepad = _lookAction.activeControl?.device is Gamepad;
                mouseX = isGamepad
                    ? look.x * GamepadSensitivity * Time.deltaTime
                    : look.x * mouseSensitivity;
            }
            else
            {
                mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 10f;
            }

            _targetYaw += mouseX;
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, 1f - Mathf.Exp(-MouseRotationSmooth * Time.deltaTime));
        }

        private void HandleMovement()
        {
            bool isSprinting = _sprintAction.IsPressed();

            Vector3 inputDir = new Vector3(_rawInput.x, 0f, _rawInput.y).normalized;
            bool hasInput = inputDir.magnitude > 0.01f;

            _currentSpeed = hasInput ? (isSprinting ? runSpeed : walkSpeed) : 0f;

            Vector3 moveDirection = Vector3.zero;

            if (hasInput)
            {
                float inputAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg;
                float targetAngle = _yaw + inputAngle;
                float newYaw = Mathf.MoveTowardsAngle(_yaw, targetAngle, KeyboardTurnSpeed * Time.deltaTime);

                _yaw = newYaw;
                _targetYaw = newYaw;
                moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            }

            if (hasInput)
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            Vector3 targetVelocity = moveDirection * _currentSpeed;
            _smoothVelocity = Vector3.Lerp(_smoothVelocity, targetVelocity,
                1f - Mathf.Pow(AccelerationSmoothing, Time.deltaTime));

            Vector3 motion = _smoothVelocity * Time.deltaTime;
            motion.y = _verticalVelocity * Time.deltaTime;
            _cc.Move(motion);
        }

        private void HandleGravity()
        {
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += gravity * Time.deltaTime;
        }

        private void UpdateAnimator()
        {
            float normalizedSpeed = runSpeed > 0f ? _currentSpeed / runSpeed : 0f;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, normalizedSpeed,
                1f - Mathf.Pow(AnimatorSpeedDamp, Time.deltaTime));

            bool isRunning = _sprintAction.IsPressed() && _rawInput.magnitude > 0.01f;

            _animator.SetFloat(HashSpeed, _smoothedSpeed);
            _animator.SetBool(HashIsRunning, isRunning);
        }

        public float Yaw => _yaw;
    }
}