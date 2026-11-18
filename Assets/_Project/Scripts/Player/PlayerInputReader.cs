using UnityEngine;
using UnityEngine.InputSystem;

namespace LNStudy.Player
{
    /// <summary>
    /// 입력 액션을 코드로 정의해 읽는다. (.inputactions 에셋 없이 키보드·게임패드 지원)
    /// 점프·잡기는 "눌린 순간"을 저장해 두고, 물리 로직(FixedUpdate)이 꺼내 쓴다.
    /// FixedUpdate는 프레임마다 호출되지 않을 수 있어서, WasPressedThisFrame 대신 이 방식을 쓴다.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move, jump, run, crouch, grab, look, zoom, freeCursor, attack, skill;
        float lastAttackPressedTime = -999f, lastSkillPressedTime = -999f;
        float lastJumpPressedTime = -999f;
        bool grabPressed;

        /// <summary>false면 키 입력을 받지 않는다 (동료 AI가 조종 중인 캐릭터)</summary>
        public bool IsPlayerControlled => controlled;
        bool controlled = true;

        public Vector2 Move => controlled ? move.ReadValue<Vector2>() : Vector2.zero;
        public bool RunHeld => controlled && run.IsPressed();
        public bool CrouchHeld => controlled && crouch.IsPressed();

        /// <summary>플레이어 조종 ↔ AI 조종 전환. AI일 때는 모든 입력 액션을 끈다</summary>
        public void SetControlled(bool value)
        {
            if (controlled == value) return;
            controlled = value;
            if (!isActiveAndEnabled) return;
            if (value) EnableActions(); else DisableActions();
            ClearJump();
            ClearAttack();
            ClearSkill();
            grabPressed = false;
        }

        /// <summary>AI가 점프를 요청 (입력 버퍼와 같은 경로)</summary>
        public void RequestJump() => lastJumpPressedTime = Time.time;

        /// <summary>카메라 회전 입력. 마우스는 이번 프레임 이동량(픽셀), 게임패드는 스틱 값(-1~1)</summary>
        public Vector2 Look => look.ReadValue<Vector2>();
        /// <summary>마지막 Look 입력이 게임패드에서 왔는지 (감도 계산용)</summary>
        public bool LookFromGamepad => look.activeControl != null && look.activeControl.device is Gamepad;
        /// <summary>줌 입력 (마우스 휠: 위 +, 아래 -)</summary>
        public float Zoom => zoom.ReadValue<float>();
        /// <summary>누르고 있는 동안 커서를 풀어준다 (원신의 Alt)</summary>
        public bool FreeCursorHeld => freeCursor.IsPressed();

        public bool AttackHeld => attack.IsPressed();
        /// <summary>window 초 안에 공격이 눌렸는지 (선입력). 사용하면 ClearAttack()</summary>
        public bool HasBufferedAttack(float window) => Time.time - lastAttackPressedTime <= window;
        public void ClearAttack() => lastAttackPressedTime = -999f;
        public bool HasBufferedSkill(float window) => Time.time - lastSkillPressedTime <= window;
        public void ClearSkill() => lastSkillPressedTime = -999f;

        /// <summary>window 초 안에 점프가 눌렸는지. 사용하면 ClearJump()로 소비한다.</summary>
        public bool HasBufferedJump(float window) => Time.time - lastJumpPressedTime <= window;
        public void ClearJump() => lastJumpPressedTime = -999f;

        /// <summary>마지막으로 확인한 뒤 잡기 버튼이 눌렸는지. 한 번 읽으면 소비된다.</summary>
        public bool ConsumeGrabPressed()
        {
            bool pressed = grabPressed;
            grabPressed = false;
            return pressed;
        }

        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");

            jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");

            run = new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift");
            run.AddBinding("<Gamepad>/leftTrigger");

            crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            crouch.AddBinding("<Keyboard>/c");
            crouch.AddBinding("<Gamepad>/buttonEast");

            grab = new InputAction("Grab", InputActionType.Button, "<Keyboard>/e");
            grab.AddBinding("<Gamepad>/rightTrigger");

            look = new InputAction("Look", InputActionType.Value);
            look.AddBinding("<Mouse>/delta");
            look.AddBinding("<Gamepad>/rightStick");

            zoom = new InputAction("Zoom", InputActionType.Value);
            zoom.AddBinding("<Mouse>/scroll/y");
            zoom.AddCompositeBinding("1DAxis")
                .With("Negative", "<Gamepad>/leftShoulder").With("Positive", "<Gamepad>/rightShoulder");

            attack = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            attack.AddBinding("<Gamepad>/buttonWest");
            skill = new InputAction("Skill", InputActionType.Button, "<Keyboard>/q");
            skill.AddBinding("<Mouse>/rightButton");
            skill.AddBinding("<Gamepad>/buttonNorth");
            attack.performed += _ => lastAttackPressedTime = Time.time;
            skill.performed += _ => lastSkillPressedTime = Time.time;

            freeCursor = new InputAction("FreeCursor", InputActionType.Button, "<Keyboard>/leftAlt");

            jump.performed += _ => lastJumpPressedTime = Time.time;
            grab.performed += _ => grabPressed = true;
        }

        void OnEnable()
        {
            if (controlled) EnableActions();
        }

        void OnDisable()
        {
            DisableActions();
            grabPressed = false;
        }

        void EnableActions()
        {
            move.Enable(); jump.Enable(); run.Enable(); crouch.Enable(); grab.Enable();
            look.Enable(); zoom.Enable(); freeCursor.Enable(); attack.Enable(); skill.Enable();
        }

        void DisableActions()
        {
            move.Disable(); jump.Disable(); run.Disable(); crouch.Disable(); grab.Disable();
            look.Disable(); zoom.Disable(); freeCursor.Disable(); attack.Disable(); skill.Disable();
        }

        void OnDestroy()
        {
            move.Dispose(); jump.Dispose(); run.Dispose(); crouch.Dispose(); grab.Dispose();
            look.Dispose(); zoom.Dispose(); freeCursor.Dispose(); attack.Dispose(); skill.Dispose();
        }
    }
}
