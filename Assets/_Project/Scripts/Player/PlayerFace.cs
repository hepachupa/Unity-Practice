using UnityEngine;
using UnityEngine.InputSystem;

namespace LNStudy.Player
{
    /// <summary>Unity-chan 표정 클립 목록. 이름이 Animator Face 레이어의 상태 이름과 같다.</summary>
    public enum FaceExpression
    {
        Default,
        Smile1,
        Smile2,
        Angry1,
        Angry2,
        Surprise,
        Ashamed,
        Conf,
        Sap,
        Disstract1,
        Disstract2,
        EyeClose,
    }

    /// <summary>
    /// 표정 전환. Animator의 Face 레이어(얼굴 블렌드셰이프 전용 마스크)에서 상태를 CrossFade한다.
    /// 표정이 눈을 움직이므로 기본 표정이 아닐 때는 AutoBlink를 꺼서 충돌을 막는다.
    /// Animator가 붙은 캐릭터 모델에 붙인다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerFace : MonoBehaviour
    {
        public const string FaceLayerName = "Face";

        [SerializeField] float crossFadeTime = 0.12f;

        [Header("Debug")]
        [Tooltip("숫자키 1~9, 0, -, = 로 표정 확인 (플레이 중 테스트용)")]
        [SerializeField] bool debugHotkeys = true;

        Animator animator;
        UnityChan.AutoBlink blink;
        int faceLayer = -1;
        float revertAt = -1f;

        public FaceExpression Current { get; private set; } = FaceExpression.Default;

        static readonly Key[] DebugKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
            Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals,
        };

        /// <summary>표정 → 클립 파일 이름 (빌더에서 사용)</summary>
        public static string GetClipFileName(FaceExpression expr) => expr switch
        {
            FaceExpression.Surprise => "SURPRISE.anim",
            FaceExpression.Ashamed => "ASHAMED.anim",
            FaceExpression.EyeClose => "eye_close@unitychan.anim",
            _ => expr.ToString().ToLowerInvariant() + "@unitychan.anim",
        };

        void Awake()
        {
            animator = GetComponent<Animator>();
            blink = GetComponent<UnityChan.AutoBlink>();
            faceLayer = animator.GetLayerIndex(FaceLayerName);
            if (faceLayer < 0)
                Debug.LogWarning("[PlayerFace] Animator에 Face 레이어가 없습니다. LNStudy/Animation/Build Player Animator를 다시 실행하세요.", this);
        }

        /// <summary>
        /// 표정을 바꾼다. holdSeconds > 0 이면 그 시간 뒤 기본 표정으로 돌아간다.
        /// </summary>
        public void SetExpression(FaceExpression expr, float holdSeconds = 0f)
        {
            if (faceLayer < 0) return;

            animator.CrossFadeInFixedTime(expr.ToString(), crossFadeTime, faceLayer);
            Current = expr;
            revertAt = holdSeconds > 0f && expr != FaceExpression.Default ? Time.time + holdSeconds : -1f;

            // 기본 표정일 때만 자동 눈 깜빡임
            if (blink != null) blink.isActive = expr == FaceExpression.Default;
        }

        public void ResetExpression() => SetExpression(FaceExpression.Default);

        void Update()
        {
            if (revertAt > 0f && Time.time >= revertAt)
                ResetExpression();

            if (!debugHotkeys) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < DebugKeys.Length; i++)
            {
                if (kb[DebugKeys[i]].wasPressedThisFrame)
                {
                    var expr = (FaceExpression)i;
                    SetExpression(expr == Current ? FaceExpression.Default : expr);
                    break;
                }
            }
        }
    }
}
