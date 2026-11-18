using System;
using LNStudy.CameraSystem;
using LNStudy.Companion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LNStudy.Player
{
    /// <summary>
    /// 원신식 캐릭터 교체. 이동·전투·펫은 플레이어 루트에 그대로 두고 "외형"만 바꾼다.
    /// 교체 시 몸체 크기(캡슐), 카메라 시선 높이, 펫이 붙는 어깨 높이를 캐릭터에 맞게 바꾼다.
    /// 입력: Tab(다음 캐릭터), 패드 십자키 오른쪽. 교체 쿨타임 있음.
    /// </summary>
    public class CharacterSwitcher : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string displayName;
            [Tooltip("ICharacterVisual 구현 컴포넌트가 붙은 외형 루트")]
            public GameObject visualRoot;
            public float standHeight = 1.55f;
            public float crouchHeight = 0.9f;
            public float radius = 0.28f;
            public float cameraPivotHeight = 1.3f;
        }

        [SerializeField] Entry[] characters;
        [SerializeField] int startIndex;
        [SerializeField] float switchCooldown = 0.6f;
        [SerializeField] GameObject switchVfx;
        [SerializeField] ShoulderPet[] pets;
        [SerializeField] ThirdPersonCamera cameraRig;

        public int ActiveIndex { get; private set; } = -1;
        public ICharacterVisual Active { get; private set; }
        public Entry ActiveEntry => ActiveIndex >= 0 ? characters[ActiveIndex] : null;
        public event Action<int> Switched;

        PlayerMotor motor;
        InputAction next;
        float nextSwitchTime;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<ThirdPersonCamera>();

            next = new InputAction("NextCharacter", InputActionType.Button, "<Keyboard>/tab");
            next.AddBinding("<Gamepad>/dpad/right");
            next.performed += _ => TrySwitch((ActiveIndex + 1) % Mathf.Max(1, characters.Length));
        }

        void OnEnable() => next.Enable();
        void OnDisable() => next.Disable();
        void OnDestroy() => next.Dispose();

        void Start()
        {
            for (int i = 0; i < characters.Length; i++)
                if (characters[i].visualRoot != null) characters[i].visualRoot.SetActive(false);
            Apply(Mathf.Clamp(startIndex, 0, characters.Length - 1), false);
        }

        public bool TrySwitch(int index)
        {
            if (characters == null || characters.Length < 2) return false;
            if (index == ActiveIndex || Time.time < nextSwitchTime) return false;
            if (motor != null && (motor.IsClimbing || !motor.IsGrounded)) return false; // 공중·오르기 중에는 교체 불가
            Apply(index, true);
            return true;
        }

        void Apply(int index, bool withFx)
        {
            if (index < 0 || index >= characters.Length) return;

            if (ActiveIndex >= 0 && characters[ActiveIndex].visualRoot != null)
                characters[ActiveIndex].visualRoot.SetActive(false);

            var e = characters[index];
            if (e.visualRoot != null) e.visualRoot.SetActive(true);
            ActiveIndex = index;
            Active = e.visualRoot != null ? e.visualRoot.GetComponent<ICharacterVisual>() : null;

            if (motor != null) motor.SetBodySize(e.standHeight, e.crouchHeight, e.radius);
            if (cameraRig != null) cameraRig.PivotHeight = e.cameraPivotHeight;

            var anim = Active?.HumanoidAnimator;
            float shoulder = Active != null ? Active.ShoulderHeight : e.standHeight * 0.8f;
            foreach (var pet in pets)
                if (pet != null) pet.SetOwnerVisual(anim != null && anim.isHuman ? anim : null, shoulder);

            if (withFx)
            {
                nextSwitchTime = Time.time + switchCooldown;
                if (switchVfx != null)
                    Destroy(Instantiate(switchVfx, transform.position + Vector3.up * (e.standHeight * 0.5f), Quaternion.identity), 2f);
                ThirdPersonCamera.Active?.Shake(0.04f, 0.12f);
            }
            Switched?.Invoke(index);
        }
    }
}
